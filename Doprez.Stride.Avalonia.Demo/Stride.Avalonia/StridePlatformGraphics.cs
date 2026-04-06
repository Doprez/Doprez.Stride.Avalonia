using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using global::Avalonia;
using global::Avalonia.Platform;
using global::Avalonia.Skia;
using SkiaSharp;

namespace Stride.Avalonia;

/// <summary>
/// Custom <see cref="IPlatformGraphics"/> that redirects Avalonia's Skia
/// rendering to <see cref="StrideRenderSurface"/> instances instead of the
/// default headless framebuffer.
/// <para>
/// When registered via <see cref="AvaloniaLocator"/>, Avalonia's compositor
/// uses this graphics backend.  Each window's render target is backed by an
/// <see cref="SKSurface"/> whose pixel data can be read <b>directly</b> via
/// pointer access — no <c>WriteableBitmap</c>, no LOH allocations, no
/// intermediate copies.
/// </para>
/// <para>
/// <b>GPU upgrade path:</b> Supply a shared <see cref="GRContext"/> (created
/// from the Stride <c>GraphicsDevice</c> via <c>GraphicsMarshal</c>) to
/// create GPU-backed <see cref="SKSurface"/> instances.  This would eliminate
/// the CPU→GPU texture upload entirely.
/// </para>
/// </summary>
internal sealed class StridePlatformGraphics : IPlatformGraphics
{
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
    internal static GRContext? SharedGRContext { get; set; }

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
        if (impl == null) return null;

        IEnumerable<object>? surfaces = null;
        try
        {
            _surfacesProp ??= impl.GetType().GetProperty("Surfaces",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            surfaces = _surfacesProp?.GetValue(impl) as IEnumerable<object>;
        }
        catch { return null; }

        if (surfaces == null) return null;

        foreach (var s in surfaces)
        {
            if (SurfaceMap.TryGetValue(s, out var surface))
                return surface;
        }
        return null;
    }

    private static PropertyInfo? _surfacesProp;

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
            // called by AvaloniaPage before rendering.  Fallback to a
            // minimal 1×1 surface if not yet sized.
            if (_surface.Surface == null)
                _surface.EnsureSize(1, 1);

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

        public StrideSkiaRenderSession(StrideRenderSurface surface)
            => _surface = surface;

        /// <summary>
        /// GPU context for Skia rendering.  <c>null</c> = CPU rendering.
        /// When a shared <see cref="GRContext"/> from Stride's device is
        /// available, this returns it — enabling zero-copy GPU texture sharing.
        /// </summary>
        public GRContext? GrContext => SharedGRContext;

        public SKSurface SkSurface => _surface.Surface!;

        public double ScaleFactor => 1.0;

        public GRSurfaceOrigin SurfaceOrigin => GRSurfaceOrigin.TopLeft;

        public void Dispose()
        {
            // Flush the Skia canvas so all draw commands are rasterised
            // and the pixel buffer is up-to-date for the subsequent
            // comparison + upload in AvaloniaSceneRenderer.
            _surface.Surface?.Canvas.Flush();
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private sealed class NoOp : IDisposable
    {
        public static readonly NoOp Instance = new();
        public void Dispose() { }
    }
}
