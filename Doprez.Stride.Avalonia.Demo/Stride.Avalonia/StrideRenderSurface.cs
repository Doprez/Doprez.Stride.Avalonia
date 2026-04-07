using System;
using SkiaSharp;
using Stride.Graphics;

namespace Stride.Avalonia;

/// <summary>
/// Manages the Stride texture Avalonia renders into via shared GPU rendering.
/// Rendering is always through shared GPU memory — no CPU fallback path exists.
/// </summary>
internal sealed class StrideRenderSurface : IDisposable
{
    private GraphicsDevice? _graphicsDevice;
    private Texture? _strideTexture;
    private StrideVulkanInterop.VulkanImageState _sharedImageState;
    private bool _hasSharedImageState;
    private int _width;
    private int _height;
    private bool _isTearingDown;
    // Skia-managed surface for the Vulkan GPU copy path
    private SKSurface? _skiaManagedSurface;
    private ulong _skiaVkImageHandle;
    private bool _vkImageCaptureInProgress;
    private long _captureToken;
    private bool _needsRecapture;

    /// <summary>Width of the rendering surface in pixels.</summary>
    public int Width => _width;

    /// <summary>Height of the rendering surface in pixels.</summary>
    public int Height => _height;

    /// <summary>
    /// The Stride-owned texture that Avalonia renders into and Stride samples from.
    /// </summary>
    public Texture? StrideTexture => _strideTexture;

    /// <summary>
    /// Monotonically increasing version of successfully flushed Avalonia renders.
    /// </summary>
    public int RenderVersion { get; private set; }

    /// <summary>
    /// Indicates that the owning Avalonia page is closing and this surface
    /// should no longer require the shared GPU path for compositor work.
    /// </summary>
    public bool IsTearingDown => _isTearingDown;

    /// <summary>
    /// Marks the surface as entering page-close teardown.
    /// Prevents new GPU surface allocation but allows existing
    /// resources to be used for close-time compositor work.
    /// </summary>
    public void BeginTeardown()
    {
        _isTearingDown = true;
    }

    /// <summary>
    /// Ensures a raster <see cref="SKSurface"/> exists at the requested
    /// resolution.  Reallocates only when the size changes.
    /// </summary>
    public void EnsureSize(int width, int height)
    {
        if (_width == width && _height == height)
            return;

        _width = width;
        _height = height;
        ReleaseResources(disposeTexture: true);

        // Content from the previous size is stale — reset so that
        // CaptureFrame() won't consume the old compositor render.
        RenderVersion = 0;

        if (width <= 0 || height <= 0) return;

        if (_graphicsDevice != null)
            EnsureGpuSurface(_graphicsDevice);
    }

    /// <summary>
    /// Ensures the Stride-owned texture exists for GPU rendering.
    /// </summary>
    public void EnsureGpuSurface(GraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        _graphicsDevice = device;

        if (_width <= 0 || _height <= 0)
            return;

        EnsureStrideTexture(device);
    }

    /// <summary>
    /// Creates the Skia rendering surface for a single Avalonia compositor pass.
    /// Uses the shared GPU path during steady state. During teardown, if the GPU
    /// path fails, returns a throwaway raster surface to satisfy the compositor's
    /// final render before window destruction (never sampled by Stride).
    /// </summary>
    public (SKSurface Surface, GRContext? GrContext, GRBackendRenderTarget? BackendRenderTarget, object? SyncScope, bool DisposeSurface) BeginRenderingSession()
    {
        if (_width <= 0 || _height <= 0)
            throw new InvalidOperationException("Render surface size must be established before rendering begins.");

        // During teardown, skip the entire GPU path — Vulkan fence/semaphore
        // waits can stall for seconds.  Return a throwaway raster surface that
        // satisfies the compositor's final render (never sampled by Stride).
        if (_isTearingDown)
            return CreateTeardownSurface();

        if (_graphicsDevice != null)
            EnsureGpuSurface(_graphicsDevice);

        if (_strideTexture == null || StridePlatformGraphics.SharedGRContext == null)
            throw StridePlatformGraphics.CreateMissingRenderSessionContextException();

        switch (StridePlatformGraphics.CurrentBackend)
        {
            case StridePlatformGraphics.SharedGraphicsBackend.Vulkan:
            {
                var grContext = StridePlatformGraphics.SharedGRContext!;

                if (_skiaManagedSurface == null || _skiaManagedSurface.Handle == IntPtr.Zero)
                {
                    _skiaManagedSurface?.Dispose();
                    // Start VkImage capture — Skia allocates the VkImage lazily
                    // during the first draw/flush, NOT during SKSurface.Create().
                    // Capture mode stays on until after the first session flush.
                    _captureToken = StrideVulkanInterop.BeginVkImageCapture(
                        (uint)_strideTexture.Width, (uint)_strideTexture.Height);
                    _vkImageCaptureInProgress = true;
                    _skiaManagedSurface = SKSurface.Create(
                        grContext,
                        false,
                        new SKImageInfo(_strideTexture.Width, _strideTexture.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
                    if (_skiaManagedSurface == null)
                    {
                        _vkImageCaptureInProgress = false;
                        StrideVulkanInterop.EndVkImageCapture(_captureToken);
                        throw new InvalidOperationException("SkiaSharp failed to create a GPU-backed SKSurface.");
                    }
                }
                else if (_skiaVkImageHandle == 0 && !_vkImageCaptureInProgress
                    && _strideTexture.Width > 1 && _strideTexture.Height > 1)
                {
                    // Surface exists but VkImage was never captured — the interceptor
                    // missed the vkCreateImage call (race with another same-size panel).
                    // Dispose the SKSurface to force Skia to call vkCreateImage again.
                    _skiaManagedSurface.Dispose();
                    _skiaManagedSurface = null;

                    _captureToken = StrideVulkanInterop.BeginVkImageCapture(
                        (uint)_strideTexture.Width, (uint)_strideTexture.Height);
                    _vkImageCaptureInProgress = true;
                    _skiaManagedSurface = SKSurface.Create(
                        grContext,
                        false,
                        new SKImageInfo(_strideTexture.Width, _strideTexture.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
                    if (_skiaManagedSurface == null)
                    {
                        _vkImageCaptureInProgress = false;
                        StrideVulkanInterop.EndVkImageCapture(_captureToken);
                        throw new InvalidOperationException("SkiaSharp failed to re-create a GPU-backed SKSurface for VkImage capture retry.");
                    }
                }

                return (_skiaManagedSurface, grContext, null, null, false);
            }
            case StridePlatformGraphics.SharedGraphicsBackend.Direct3D11:
            {
                var renderTargetScope = StrideDirect3D11Interop.CreateBackendRenderTarget(_strideTexture);

                try
                {
                    var gpuSurface = SKSurface.Create(
                        StridePlatformGraphics.SharedGRContext,
                        renderTargetScope.BackendRenderTarget,
                        GRSurfaceOrigin.TopLeft,
                        SKColorType.Rgba8888)
                        ?? throw new InvalidOperationException(
                            "SkiaSharp failed to create a Direct3D11 SKSurface for the Stride render target.");

                    return (gpuSurface, StridePlatformGraphics.SharedGRContext, renderTargetScope.BackendRenderTarget, renderTargetScope.Scope, true);
                }
                catch
                {
                    renderTargetScope.BackendRenderTarget.Dispose();
                    renderTargetScope.Scope.Dispose();
                    throw;
                }
            }
            default:
                throw StridePlatformGraphics.CreateMissingRenderSessionContextException();
        }
    }

    /// <summary>
    /// Creates a minimal raster <see cref="SKSurface"/> that satisfies the
    /// compositor's close-time render pass.  This surface is never sampled
    /// by Stride — it exists only so <c>Window.Close()</c> can complete
    /// without throwing.
    /// </summary>
    private (SKSurface Surface, GRContext? GrContext, GRBackendRenderTarget? BackendRenderTarget, object? SyncScope, bool DisposeSurface) CreateTeardownSurface()
    {
        var info = new SKImageInfo(
            Math.Max(_width, 1),
            Math.Max(_height, 1),
            SKColorType.Rgba8888,
            SKAlphaType.Premul);
        var surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException("Failed to create a raster SKSurface during teardown.");
        return (surface, null, null, null, true);
    }

    /// <summary>
    /// Whether a VkImage capture is pending completion after the first render flush.
    /// </summary>
    internal bool IsVkImageCaptureInProgress => _vkImageCaptureInProgress;

    /// <summary>
    /// Completes a pending VkImage capture. Must be called after the first
    /// session flush when the capture was started.
    /// </summary>
    internal void CompleteVkImageCapture()
    {
        if (!_vkImageCaptureInProgress) return;
        _skiaVkImageHandle = StrideVulkanInterop.EndVkImageCapture(_captureToken);
        _vkImageCaptureInProgress = false;

        if (_skiaVkImageHandle == 0)
        {
            // Capture failed — dispose the SKSurface so the next
            // BeginRenderingSession re-creates it and retries.
            _skiaManagedSurface?.Dispose();
            _skiaManagedSurface = null;
            _needsRecapture = true;
        }
        else
        {
            _needsRecapture = false;
        }
    }

    /// <summary>
    /// Whether a VkImage recapture is needed (previous capture failed).
    /// </summary>
    internal bool NeedsRecapture => _needsRecapture;

    /// <summary>
    /// Performs a GPU-to-GPU copy from the Skia-managed surface's VkImage
    /// to the Stride-owned texture.
    /// </summary>
    /// <returns>true if the copy was performed; false if skipped (no valid VkImage).</returns>
    public bool GpuCopySkiaToStride()
    {
        if (_graphicsDevice == null || _strideTexture == null || _skiaVkImageHandle == 0)
            return false;

        var dstImage = unchecked((ulong)StrideVulkanInterop.GetNativeImage(_strideTexture).ToInt64());
        StrideVulkanInterop.GpuCopyImage(
            _graphicsDevice,
            _skiaVkImageHandle,
            dstImage,
            _strideTexture.Width,
            _strideTexture.Height);

        // Sync Stride's internal layout tracking with the actual GPU state.
        // GpuCopyImage transitions the dst image to ShaderReadOnlyOptimal.
        // Without this, Stride would re-transition from the stale tracked
        // layout (e.g. Undefined), which discards the copied pixel data.
        StrideVulkanInterop.ApplyImageState(_strideTexture,
            new StrideVulkanInterop.VulkanImageState(
                Vortice.Vulkan.VkImageLayout.ShaderReadOnlyOptimal,
                Vortice.Vulkan.VkAccessFlags.InputAttachmentRead | Vortice.Vulkan.VkAccessFlags.ShaderRead,
                Vortice.Vulkan.VkPipelineStageFlags.FragmentShader,
                StrideVulkanInterop.GraphicsQueueFamilyIndex,
                Vortice.Vulkan.VkImageAspectFlags.Color));

        return true;
    }

    public void CompleteSharedRenderingSession(StrideVulkanInterop.SharedImageSyncScope syncScope)
    {
        ArgumentNullException.ThrowIfNull(syncScope);

        syncScope.Complete();
        _sharedImageState = syncScope.CurrentState;
        _hasSharedImageState = true;
    }

    /// <summary>
    /// Marks that Avalonia flushed a new frame into this surface.
    /// </summary>
    public void MarkRendered()
    {
        RenderVersion++;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _isTearingDown = true;
        ReleaseResources(disposeTexture: true);
    }

    private void EnsureStrideTexture(GraphicsDevice device)
    {
        if (_width <= 0 || _height <= 0)
            return;

        if (_strideTexture != null && _strideTexture.Width == _width && _strideTexture.Height == _height)
            return;

        _strideTexture?.Dispose();
        _strideTexture = Texture.New2D(
            device,
            _width,
            _height,
            PixelFormat.R8G8B8A8_UNorm,
            TextureFlags.RenderTarget | TextureFlags.ShaderResource);

        if (StridePlatformGraphics.CurrentBackend == StridePlatformGraphics.SharedGraphicsBackend.Vulkan)
        {
            _sharedImageState = StrideVulkanInterop.CaptureImageState(device, _strideTexture);
            _hasSharedImageState = true;
        }
        else
        {
            _hasSharedImageState = false;
        }
    }

    private void EnsureSharedImageState()
    {
        if (_graphicsDevice == null || _strideTexture == null)
            throw new InvalidOperationException("Shared GPU rendering requires both a graphics device and a Stride texture.");

        if (_hasSharedImageState)
            return;

        _sharedImageState = StrideVulkanInterop.CaptureImageState(_graphicsDevice, _strideTexture);
        _hasSharedImageState = true;
    }

    private void ReleaseResources(bool disposeTexture)
    {
        if (_vkImageCaptureInProgress)
        {
            StrideVulkanInterop.EndVkImageCapture(_captureToken);
            _vkImageCaptureInProgress = false;
        }

        _skiaManagedSurface?.Dispose();
        _skiaManagedSurface = null;
        _skiaVkImageHandle = 0;

        if (disposeTexture)
        {
            _strideTexture?.Dispose();
            _strideTexture = null;
            _hasSharedImageState = false;
        }
    }
}
