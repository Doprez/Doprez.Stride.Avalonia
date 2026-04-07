using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using global::Avalonia;
using global::Avalonia.Platform;
using global::Avalonia.Skia;
using SkiaSharp;
using Stride.Graphics;

namespace Stride.Avalonia;

/// <summary>
/// Custom <see cref="IPlatformGraphics"/> that redirects Avalonia's Skia
/// rendering to <see cref="StrideRenderSurface"/> instances backed by
/// shared GPU textures — no CPU upload, no intermediate copies.
/// <para>
/// A shared <see cref="GRContext"/> is created from the Stride
/// <c>GraphicsDevice</c> (Vulkan or Direct3D11) so Avalonia's compositor
/// renders directly into Stride-owned textures.
/// </para>
/// </summary>
internal sealed class StridePlatformGraphics : IPlatformGraphics
{
    private static readonly object SharedContextSync = new();

    internal enum SharedGraphicsBackend
    {
        None,
        Vulkan,
        Direct3D11,
    }

    private enum SharedContextLifecycleState
    {
        Uninitialized,
        Ready,
        ShuttingDown,
        Failed,
    }

    /// <summary>
    /// Maps surface objects (from <c>ITopLevelImpl.Surfaces</c>) to their
    /// associated <see cref="StrideRenderSurface"/>.  Used by
    /// <see cref="AvaloniaPage"/> to locate its render surface.
    /// </summary>
    internal static readonly ConcurrentDictionary<object, StrideRenderSurface> SurfaceMap = new();

    /// <summary>
    /// Optional shared GPU context.  When set, render sessions will create
    /// GPU-backed <see cref="SKSurface"/> instances.
    /// </summary>
    internal static GRContext? SharedGRContext { get; private set; }

    private static GRContext? RetiredGRContext { get; set; }

    private static IDisposable? SharedBackendResources { get; set; }

    private static IDisposable? RetiredBackendResources { get; set; }

    internal static SharedGraphicsBackend CurrentBackend { get; private set; }

    /// <summary>
    /// The Stride graphics device that owns the shared interop textures.
    /// </summary>
    internal static GraphicsDevice? SharedGraphicsDevice { get; private set; }

    /// <summary>
    /// Captures the reason the shared GPU path could not be enabled.
    /// </summary>
    internal static Exception? SharedContextFailure { get; private set; }

    private static SharedContextLifecycleState SharedContextState { get; set; }

    internal static void EnsureSharedContext(GraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        lock (SharedContextSync)
        {
            if (SharedContextState == SharedContextLifecycleState.ShuttingDown)
            {
                if (SharedGraphicsDevice == null && SharedGRContext == null && RetiredGRContext == null && SharedContextFailure == null)
                {
                    SharedContextState = SharedContextLifecycleState.Uninitialized;
                }
                else
                {
                    throw new InvalidOperationException(
                        "Shared Skia GRContext initialization was requested while the previous shared rendering lifecycle is still shutting down.");
                }
            }

            SharedGraphicsDevice ??= device;

            if (!ReferenceEquals(SharedGraphicsDevice, device))
                throw new InvalidOperationException("Avalonia shared graphics context can only be bound to one Stride GraphicsDevice instance.");

            if (SharedContextState == SharedContextLifecycleState.Ready && SharedGRContext != null)
                return;

            if (SharedContextState == SharedContextLifecycleState.Failed && SharedContextFailure != null)
                throw new InvalidOperationException(
                    "Shared Skia GRContext initialization previously failed for the active Stride graphics device.",
                    SharedContextFailure);

            try
            {
                if (StrideVulkanInterop.IsSupported(device))
                {
                    SharedGRContext = StrideVulkanInterop.CreateGRContext(device);
                    CurrentBackend = SharedGraphicsBackend.Vulkan;
                }
                else if (StrideDirect3D11Interop.IsSupported(device))
                {
                    var sharedContext = StrideDirect3D11Interop.CreateSharedContext(device);
                    SharedGRContext = sharedContext.Context;
                    SharedBackendResources = sharedContext;
                    CurrentBackend = SharedGraphicsBackend.Direct3D11;
                }
                else
                {
                    throw new NotSupportedException(
                        "Shared GPU interop is only implemented for Stride Vulkan and Direct3D11 devices in this build.");
                }

                SharedContextState = SharedContextLifecycleState.Ready;
            }
            catch (Exception ex)
            {
                SharedBackendResources?.Dispose();
                SharedBackendResources = null;
                CurrentBackend = SharedGraphicsBackend.None;
                SharedContextFailure = new InvalidOperationException(
                    "Failed to initialize the shared Skia GRContext for the active Stride graphics device.",
                    ex);
                SharedContextState = SharedContextLifecycleState.Failed;
                throw SharedContextFailure;
            }
        }
    }

    internal static void BeginShutdown()
    {
        lock (SharedContextSync)
        {
            if (SharedGRContext != null && RetiredGRContext == null)
                RetiredGRContext = SharedGRContext;

            if (SharedBackendResources != null && RetiredBackendResources == null)
                RetiredBackendResources = SharedBackendResources;

            SharedGRContext = null;
            SharedBackendResources = null;
            SharedContextState = SharedContextLifecycleState.ShuttingDown;
        }
    }

    internal static void ResetSharedContext()
    {
        GRContext? contextToDispose;
        IDisposable? backendResourcesToDispose;

        lock (SharedContextSync)
        {
            if (SharedGRContext != null && RetiredGRContext == null)
                RetiredGRContext = SharedGRContext;

            if (SharedBackendResources != null && RetiredBackendResources == null)
                RetiredBackendResources = SharedBackendResources;

            contextToDispose = RetiredGRContext;
            backendResourcesToDispose = RetiredBackendResources;
            RetiredGRContext = null;
            RetiredBackendResources = null;
            SharedGRContext = null;
            SharedBackendResources = null;
            SharedGraphicsDevice = null;
            SharedContextFailure = null;
            CurrentBackend = SharedGraphicsBackend.None;
            SharedContextState = SharedContextLifecycleState.ShuttingDown;
        }

        contextToDispose?.Dispose();
        backendResourcesToDispose?.Dispose();
    }

    internal static GRContext GetSharedContextOrThrow()
    {
        lock (SharedContextSync)
        {
            if (SharedContextState == SharedContextLifecycleState.Ready && SharedGRContext != null)
                return SharedGRContext;

            if (SharedContextFailure != null)
                throw new InvalidOperationException(
                    "Shared Skia GRContext is unavailable because initialization failed earlier.",
                    SharedContextFailure);

            if (SharedContextState == SharedContextLifecycleState.ShuttingDown)
                throw new InvalidOperationException(
                    "Shared Skia GRContext is unavailable because shared rendering teardown is in progress.");

            throw new InvalidOperationException(
                "Shared Skia GRContext has not been initialized for the active Stride graphics device.");
        }
    }

    internal static bool TryGetSharedGraphicsDeviceForRendering(out GraphicsDevice? device)
    {
        lock (SharedContextSync)
        {
            if (SharedContextState == SharedContextLifecycleState.Ready && SharedGraphicsDevice != null && SharedGRContext != null)
            {
                device = SharedGraphicsDevice;
                return true;
            }

            device = null;
            return false;
        }
    }

    internal static bool IsShutdownInProgress()
    {
        lock (SharedContextSync)
            return SharedContextState == SharedContextLifecycleState.ShuttingDown;
    }

    internal static InvalidOperationException CreateMissingRenderSessionContextException()
    {
        if (SharedContextState == SharedContextLifecycleState.ShuttingDown)
        {
            return new InvalidOperationException(
                "Avalonia attempted to enter a shared Skia render session while shared-context teardown was already in progress.");
        }

        if (SharedContextFailure != null)
        {
            return new InvalidOperationException(
                "Avalonia entered a shared Skia render session after shared GRContext initialization failed.",
                SharedContextFailure);
        }

        return new InvalidOperationException(
            "Avalonia entered a shared Skia render session without a valid shared GRContext.");
    }

    bool IPlatformGraphics.UsesSharedContext => true;

    IPlatformGraphicsContext IPlatformGraphics.CreateContext()
        => throw new NotSupportedException("Use GetSharedContext().");

    IPlatformGraphicsContext IPlatformGraphics.GetSharedContext()
        => new StrideSkiaGpu();

    /// <summary>
    /// Finds the <see cref="StrideRenderSurface"/> associated with a
    /// window's <see cref="ITopLevelImpl"/>.
    /// </summary>
    /// <remarks>
    /// <c>ITopLevelImpl.Surfaces</c> is internal in Avalonia 11.3+.
    /// We access it via reflection — the same pattern used by
    /// <see cref="AvaloniaInputBridge"/> for other internal APIs.
    /// </remarks>
    internal static StrideRenderSurface? FindSurface(ITopLevelImpl? impl)
    {
        var surfaces = GetRegisteredSurfaces(impl);
        if (surfaces == null)
            return null;

        foreach (var s in surfaces)
        {
            if (SurfaceMap.TryGetValue(s, out var surface))
                return surface;
        }

        return null;
    }

    internal static IReadOnlyList<object> CaptureSurfaceHandles(ITopLevelImpl? impl)
    {
        var surfaces = GetRegisteredSurfaces(impl);
        if (surfaces == null)
            return Array.Empty<object>();

        var handles = new List<object>();
        foreach (var surface in surfaces)
            handles.Add(surface);

        return handles;
    }

    internal static void UnregisterSurfaces(IReadOnlyList<object> surfaceHandles, StrideRenderSurface? expectedSurface)
    {
        var matchedSurface = false;

        foreach (var surfaceHandle in surfaceHandles)
        {
            if (!SurfaceMap.TryGetValue(surfaceHandle, out var registeredSurface))
                continue;

            SurfaceMap.TryRemove(surfaceHandle, out _);
            matchedSurface = true;
        }

        if (!matchedSurface && expectedSurface != null)
        {
            foreach (var pair in SurfaceMap)
            {
                if (!ReferenceEquals(pair.Value, expectedSurface))
                    continue;

                SurfaceMap.TryRemove(pair.Key, out _);
            }
        }
    }

    internal static void BeginSurfaceTeardown(IReadOnlyList<object> surfaceHandles, StrideRenderSurface? expectedSurface)
    {
        var matchedSurface = false;

        foreach (var surfaceHandle in surfaceHandles)
        {
            if (!SurfaceMap.TryGetValue(surfaceHandle, out var registeredSurface))
                continue;

            registeredSurface.BeginTeardown();
            matchedSurface = true;
        }

        if (!matchedSurface)
            expectedSurface?.BeginTeardown();
    }

    private static PropertyInfo? _surfacesProp;

    private static IEnumerable<object>? GetRegisteredSurfaces(ITopLevelImpl? impl)
    {
        if (impl == null)
            return null;

        try
        {
            _surfacesProp ??= impl.GetType().GetProperty("Surfaces",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return _surfacesProp?.GetValue(impl) as IEnumerable<object>;
        }
        catch
        {
            return null;
        }
    }

    // ── ISkiaGpu ────────────────────────────────────────────────────

    private sealed class StrideSkiaGpu : ISkiaGpu
    {
        public bool IsLost => false;
        public void Dispose() { }
        public IDisposable EnsureCurrent() => NoOp.Instance;

        public ISkiaGpuRenderTarget? TryCreateRenderTarget(IEnumerable<object> surfaces)
        {
            foreach (var s in surfaces)
            {
                var renderSurface = SurfaceMap.GetOrAdd(s, static _ => new StrideRenderSurface());
                if (TryGetSharedGraphicsDeviceForRendering(out var device) && device != null)
                    renderSurface.EnsureGpuSurface(device);
                return new StrideSkiaRenderTarget(renderSurface);
            }
            return null;
        }

        public ISkiaSurface? TryCreateSurface(PixelSize size, ISkiaGpuRenderSession? session)
            => null;

        public object? TryGetFeature(Type featureType) => null;
    }

    // ── ISkiaGpuRenderTarget ────────────────────────────────────────

    private sealed class StrideSkiaRenderTarget : ISkiaGpuRenderTarget
    {
        private readonly StrideRenderSurface _surface;

        public StrideSkiaRenderTarget(StrideRenderSurface surface) => _surface = surface;

        public bool IsCorrupted => false;

        public ISkiaGpuRenderSession BeginRenderingSession()
        {
            // Ensure the surface exists — EnsureSize should have been
            // called by AvaloniaPage before rendering.
            if (_surface.Width <= 0 || _surface.Height <= 0)
                _surface.EnsureSize(1, 1);

            if (!_surface.IsTearingDown && TryGetSharedGraphicsDeviceForRendering(out var device) && device != null)
                _surface.EnsureGpuSurface(device);

            return new StrideSkiaRenderSession(_surface);
        }

        public IDrawingContextImpl CreateDrawingContext(bool useScaledDrawing)
        {
            // This IRenderTarget method is the non-Skia fallback path.
            // The compositor should always use BeginRenderingSession()
            // when an ISkiaGpuRenderTarget is returned.
            throw new NotSupportedException(
                "Use BeginRenderingSession() for Skia GPU rendering.");
        }

        public void Dispose() { }
    }

    // ── ISkiaGpuRenderSession ───────────────────────────────────────

    private sealed class StrideSkiaRenderSession : ISkiaGpuRenderSession
    {
        private readonly StrideRenderSurface _surface;
        private readonly SKSurface _skSurface;
        private readonly GRContext? _grContext;
        private readonly GRBackendRenderTarget? _backendRenderTarget;
        private readonly object? _syncScope;
        private readonly bool _disposeSkSurface;
        private readonly bool _useGpuCopy;

        public StrideSkiaRenderSession(StrideRenderSurface surface)
        {
            _surface = surface;

            var renderSession = surface.BeginRenderingSession();
            _skSurface = renderSession.Surface;
            _grContext = renderSession.GrContext;
            _backendRenderTarget = renderSession.BackendRenderTarget;
            _syncScope = renderSession.SyncScope;
            _disposeSkSurface = renderSession.DisposeSurface;

            // The Vulkan copy path uses Skia-managed surfaces with no sync scope
            _useGpuCopy = _grContext != null
                && _backendRenderTarget == null
                && _syncScope == null
                && !_disposeSkSurface;
        }

        /// <summary>
        /// GPU context for Skia rendering.
        /// </summary>
        public GRContext? GrContext => _grContext;

        public SKSurface SkSurface => _skSurface;

        public double ScaleFactor => 1.0;

        public GRSurfaceOrigin SurfaceOrigin => GRSurfaceOrigin.TopLeft;

        public void Dispose()
        {
            try
            {
                // Flush the Skia canvas so all draw commands are rasterised.
                _skSurface.Canvas.Flush();
                _grContext?.Flush();
                _grContext?.Submit(true);

                if (_useGpuCopy)
                {
                    _surface.GpuCopySkiaToStride();
                }
                else
                {
                    switch (_syncScope)
                    {
                        case StrideVulkanInterop.SharedImageSyncScope syncScope:
                            _surface.CompleteSharedRenderingSession(syncScope);
                            break;
                        case StrideDirect3D11Interop.SharedTextureRenderScope d3dScope:
                            d3dScope.Complete();
                            break;
                    }
                }

                _surface.MarkRendered();
            }
            finally
            {
                _backendRenderTarget?.Dispose();
                if (_disposeSkSurface)
                    _skSurface.Dispose();

                (_syncScope as IDisposable)?.Dispose();
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private sealed class NoOp : IDisposable
    {
        public static readonly NoOp Instance = new();
        public void Dispose() { }
    }
}
