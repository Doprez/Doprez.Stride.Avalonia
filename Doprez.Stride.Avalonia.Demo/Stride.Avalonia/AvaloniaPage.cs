using System;
using System.Reflection;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless;
using global::Avalonia.Media;
using global::Avalonia.Media.Imaging;
using global::Avalonia.Platform;
using global::Avalonia.Threading;
using Stride.Core;
using Stride.Graphics;

namespace Stride.Avalonia;

/// <summary>
/// Abstract base for an Avalonia page that is rendered off-screen inside a
/// headless <see cref="Window"/>.
/// <para>
/// Subclass this type and decorate the subclass with
/// <c>[DataContract("YourPageName")]</c> so that Stride GameStudio can
/// discover it and present it in the editor dropdown.
/// Override <see cref="CreateContent"/> to return the root Avalonia
/// <see cref="Control"/> for the page.
/// </para>
/// </summary>
[DataContract]
public abstract class AvaloniaPage : IDisposable
{
    private Window? _window;
    private StrideRenderSurface? _renderSurface;
    private bool _isDirty = true;
    private Control? _content;
    private int _lastCapturedRenderVersion;
    private bool _isDisposing;
    private bool _isDisposed;

    /// <summary>
    /// The root Avalonia control to render.
    /// Created lazily via <see cref="CreateContent"/> on first access.
    /// </summary>
    [DataMemberIgnore]
    public Control Content => _content ??= CreateContent();

    /// <summary>The headless window hosting the content.</summary>
    [DataMemberIgnore]
    public Window? Window => _window;

    /// <summary>Whether the headless window has been created and shown.</summary>
    [DataMemberIgnore]
    public bool IsReady => _window != null && !_isDisposing;

    /// <summary>
    /// The <see cref="StrideRenderSurface"/> for this page.
    /// Lazily re-looks up from <see cref="StridePlatformGraphics.SurfaceMap"/>
    /// if not yet resolved, since the compositor creates the render target
    /// after <see cref="EnsureWindow"/> returns.
    /// </summary>
    [DataMemberIgnore]
    internal StrideRenderSurface? RenderSurface
    {
        get
        {
            if (_renderSurface == null && _window != null)
                _renderSurface = StridePlatformGraphics.FindSurface(_window.PlatformImpl);
            return _renderSurface;
        }
    }

    /// <summary>
    /// Indicates whether the visual tree has changed since the last capture.
    /// </summary>
    [DataMemberIgnore]
    public bool IsDirty => _isDirty;

    /// <summary>
    /// Creates the root Avalonia <see cref="Control"/> for this page.
    /// Called once, lazily, on first access to <see cref="Content"/>.
    /// </summary>
    protected abstract Control CreateContent();

    /// <summary>
    /// Marks the page as needing a fresh capture on the next render pass.
    /// </summary>
    public void MarkDirty() => _isDirty = true;

    /// <summary>
    /// Creates and shows the headless window at the given pixel resolution.
    /// </summary>
    public void EnsureWindow(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_window != null) return;

        AvaloniaApp.EnsureInitialized();

        if (AvaloniaApp.UseStridePlatformGraphics)
            _ = StridePlatformGraphics.GetSharedContextOrThrow();

        _window = new Window
        {
            Width = width,
            Height = height,
            Content = Content,
            SystemDecorations = SystemDecorations.None,
            Background = Brushes.Transparent,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            UseLayoutRounding = true,
        };

        // Force grayscale anti-aliasing for all text in the window.
        RenderOptions.SetTextRenderingMode(_window, TextRenderingMode.Antialias);

        _window.Show();

        // Pre-create and size the render surface BEFORE RunJobs triggers
        // the compositor.  Without this, the compositor's TryCreateRenderTarget
        // creates a 0×0 surface, and BeginRenderingSession falls back to 1×1
        // — a size too small for reliable VkImage capture.  The compositor then
        // considers the window clean, making subsequent InvalidateVisual calls
        // insufficient to trigger a re-render at the correct resolution.
        _renderSurface = StridePlatformGraphics.FindSurface(_window.PlatformImpl);
        if (_renderSurface != null && width > 0 && height > 0)
            _renderSurface.EnsureSize(width, height);

        // Invalidate so the compositor renders this window on the
        // next ForceRenderTimerTick — without this, headless windows
        // that were never rendered stay "clean" and are skipped.
        _window.InvalidateVisual();

        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Creates the GPU-backed surface for this page's render surface.
    /// Must be called after <see cref="EnsureWindow"/> and after
    /// <see cref="StrideVulkanInterop.CreateGRContext"/> has set
    /// <see cref="StridePlatformGraphics.SharedGRContext"/>.
    /// </summary>
    public void EnsureGpuSurface(GraphicsDevice device)
    {
        RenderSurface?.EnsureGpuSurface(device);
    }

    /// <summary>
    /// Resizes the headless window.
    /// </summary>
    public void Resize(int width, int height)
    {
        if (_window == null) return;

        // Always ensure the render surface is sized correctly.
        var surface = RenderSurface;
        if (surface != null && (surface.Width != width || surface.Height != height))
        {
            surface.EnsureSize(width, height);
            _isDirty = true;

            // The compositor may have rendered at a wrong size (e.g. 1x1
            // fallback).  Invalidate the visual tree so the compositor
            // re-renders at the correct dimensions on the next tick.
            _window?.InvalidateVisual();
        }

        if ((int)_window.Width != width || (int)_window.Height != height)
        {
            _window.Width = width;
            _window.Height = height;
            _isDirty = true;
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>
    /// Captures the last rendered Avalonia frame.
    /// Returns <c>true</c> if frame data is available.
    /// </summary>
    /// <remarks>
    /// The GPU-backed compositor has already rendered to the
    /// <see cref="StrideRenderSurface"/>'s GPU texture.
    /// This method simply tracks the dirty flag.
    /// </remarks>
    public bool CaptureFrame()
    {
        var renderSurface = RenderSurface;
        if (_window == null || renderSurface == null) return false;
        if (renderSurface.RenderVersion == _lastCapturedRenderVersion) return false;

        _lastCapturedRenderVersion = renderSurface.RenderVersion;
        _isDirty = false;
        return renderSurface.StrideTexture != null;
    }

    public void Dispose()
    {
        if (_isDisposed || _isDisposing)
            return;

        _isDisposing = true;

        var window = _window;
        var platformImpl = window?.PlatformImpl;
        var surfaceHandles = StridePlatformGraphics.CaptureSurfaceHandles(platformImpl);
        var renderSurface = RenderSurface;

        try
        {
            // Mark this surface directly — the handle-based lookup in
            // BeginSurfaceTeardown may fail when reflection can't resolve
            // the headless platform's Surfaces property.
            renderSurface?.BeginTeardown();
            StridePlatformGraphics.BeginSurfaceTeardown(surfaceHandles, renderSurface);

            if (window != null)
            {
                try
                {
                    window.Close();
                    Dispatcher.UIThread.RunJobs();
                }
                catch
                {
                    // Avalonia's compositor may fire a render pass during
                    // Window.Close().  If the GPU surface can't be created
                    // (teardown race), swallow the error — the page is
                    // being disposed and the final frame is never sampled.
                }
            }
        }
        finally
        {
            StridePlatformGraphics.UnregisterSurfaces(surfaceHandles, renderSurface);
            renderSurface?.Dispose();
            _renderSurface = null;
            _window = null;
            _isDisposing = false;
            _isDisposed = true;
        }
    }
}
