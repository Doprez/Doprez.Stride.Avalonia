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
    private WriteableBitmap? _lastFrame;
    private StrideRenderSurface? _renderSurface;
    private bool _isDirty = true;
    private Control? _content;

    // Reflection cache for direct framebuffer access.
    // HeadlessWindowImpl stores the last rendered frame in a private field.
    // Accessing it directly avoids the LOH allocation that
    // GetLastRenderedFrame() makes when it copies the bitmap.
    private static FieldInfo? _frameField;
    private static FieldInfo? _syncField;
    private static bool _reflectionInitialized;
    private static bool _canUseDirect;

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
    public bool IsReady => _window != null;

    /// <summary>
    /// The <see cref="StrideRenderSurface"/> for this page, or <c>null</c>
    /// if the legacy headless framebuffer path is active.
    /// </summary>
    [DataMemberIgnore]
    internal StrideRenderSurface? RenderSurface => _renderSurface;

    /// <summary>
    /// Indicates whether the visual tree has changed since the last capture.
    /// </summary>
    [DataMemberIgnore]
    public bool IsDirty => _isDirty;

    /// <summary>
    /// Indicates whether the last call to <see cref="CaptureFrame"/> produced
    /// pixel data that differs from the previous capture.  When <c>false</c>,
    /// the GPU texture is already up-to-date and the upload can be skipped.
    /// </summary>
    [DataMemberIgnore]
    public bool HasNewContent { get; private set; } = true;

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
        if (_window != null) return;

        AvaloniaApp.EnsureInitialized();

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
        // Sub-pixel (ClearType/LCD) rendering writes per-channel colour fringes
        // that assume an opaque, known background.  When the frame is captured
        // to a texture with transparency and composited onto the 3D scene those
        // fringes appear as dark rectangles behind each glyph.
        RenderOptions.SetTextRenderingMode(_window, TextRenderingMode.Antialias);

        _window.Show();

        // When the custom Stride platform graphics is active, locate the
        // StrideRenderSurface that was created for this window's render
        // target and pre-size it before the first layout pass.
        if (AvaloniaApp.UseStridePlatformGraphics)
        {
            _renderSurface = StridePlatformGraphics.FindSurface(_window.PlatformImpl);
            _renderSurface?.EnsureSize(width, height);
        }

        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Resizes the headless window.
    /// </summary>
    public void Resize(int width, int height)
    {
        if (_window == null) return;
        if ((int)_window.Width == width && (int)_window.Height == height)
            return;

        _window.Width = width;
        _window.Height = height;
        _isDirty = true;
        _renderSurface?.EnsureSize(width, height);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Captures the last rendered Avalonia frame.
    /// Returns <c>true</c> if frame data is available.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Stride platform path</b> (preferred):
    /// The compositor has already rendered to the
    /// <see cref="StrideRenderSurface"/>'s <see cref="SkiaSharp.SKSurface"/>.
    /// This method compares pixels with the previous frame and sets
    /// <see cref="HasNewContent"/> accordingly.  No <c>WriteableBitmap</c> is
    /// involved — pixels are accessed via direct pointer.
    /// </para>
    /// <para>
    /// <b>Legacy headless path</b> (fallback):
    /// Accesses <c>HeadlessWindowImpl._lastRenderedFrame</c> via reflection,
    /// copies pixels to a reusable <c>WriteableBitmap</c>, and compares to
    /// detect changes.
    /// </para>
    /// </remarks>
    public bool CaptureFrame()
    {
        if (_window == null) return false;

        // ── Stride platform path: pixels are in the StrideRenderSurface ──
        if (_renderSurface != null)
        {
            if (!_isDirty)
            {
                HasNewContent = false;
                return _renderSurface.Surface != null;
            }

            _renderSurface.CompareAndUpdate();
            HasNewContent = _renderSurface.HasNewContent;
            _isDirty = false;
            return _renderSurface.Surface != null;
        }

        // ── Legacy headless path: WriteableBitmap capture ──
        return CaptureFrameLegacy();
    }

    /// <summary>
    /// Returns a <see cref="PixelAccess"/> for the current frame data.
    /// The caller must dispose it to release any underlying lock.
    /// </summary>
    public PixelAccess LockPixels()
    {
        if (_renderSurface != null)
        {
            return new PixelAccess(
                _renderSurface.GetPixelPointer(),
                _renderSurface.GetPixelDataSize(),
                _renderSurface.Width * 4,
                _renderSurface.Width,
                _renderSurface.Height);
        }

        // Legacy: lock the WriteableBitmap.
        if (_lastFrame == null)
            return default;

        var fb = _lastFrame.Lock();
        return new PixelAccess(
            fb.Address,
            fb.RowBytes * fb.Size.Height,
            fb.RowBytes,
            fb.Size.Width,
            fb.Size.Height,
            fb);
    }

    /// <summary>Legacy headless capture path using WriteableBitmap.</summary>
    private bool CaptureFrameLegacy()
    {
        if (_window == null) return false;

        if (!_isDirty && _lastFrame != null)
        {
            HasNewContent = false;
            return true;
        }

        HasNewContent = true;

        if (_lastFrame != null && TryCaptureDirectly())
        {
            _isDirty = false;
            return true;
        }

        var captured = _window.CaptureRenderedFrame();
        if (captured == null)
        {
            HasNewContent = false;
            _isDirty = false;
            return _lastFrame != null;
        }

        if (_lastFrame != null
            && _lastFrame.PixelSize == captured.PixelSize
            && !ReferenceEquals(_lastFrame, captured))
        {
            HasNewContent = CopyBitmapIfChanged(captured, _lastFrame);
            captured.Dispose();
        }
        else if (!ReferenceEquals(_lastFrame, captured))
        {
            _lastFrame?.Dispose();
            _lastFrame = captured;
            HasNewContent = true;
        }

        _isDirty = false;
        return true;
    }

    /// <summary>
    /// Reads pixel data directly from <c>HeadlessWindowImpl._lastRenderedFrame</c>
    /// via reflection, copying into <see cref="_lastFrame"/> without any
    /// intermediate heap allocation.
    /// </summary>
    /// <returns><c>true</c> if pixels were copied successfully.</returns>
    private bool TryCaptureDirectly()
    {
        var impl = _window!.PlatformImpl;
        if (impl == null) return false;

        if (!EnsureReflection(impl)) return false;

        var syncObj = _syncField!.GetValue(impl);
        if (syncObj == null) return false;

        lock (syncObj)
        {
            var internalBitmap = _frameField!.GetValue(impl) as WriteableBitmap;
            if (internalBitmap == null) return false;

            var size = internalBitmap.PixelSize;

            // Reallocate _lastFrame only when resolution changes.
            if (_lastFrame!.PixelSize != size)
            {
                _lastFrame.Dispose();
                _lastFrame = new WriteableBitmap(
                    size, internalBitmap.Dpi,
                    PixelFormat.Bgra8888, AlphaFormat.Premul);
                // Resolution changed — must copy all pixels.
                CopyBitmapPixels(internalBitmap, _lastFrame);
                HasNewContent = true;
                return true;
            }

            // Compare pixels before copying: if the rendered content is
            // identical to what we already have, skip the memcpy and signal
            // the renderer to skip the GPU upload as well.
            HasNewContent = CopyBitmapIfChanged(internalBitmap, _lastFrame);
            return true;
        }
    }

    /// <summary>
    /// One-time reflection lookup for <c>HeadlessWindowImpl._lastRenderedFrame</c>
    /// and <c>HeadlessWindowImpl._sync</c>.
    /// </summary>
    private static bool EnsureReflection(object impl)
    {
        if (_reflectionInitialized) return _canUseDirect;
        _reflectionInitialized = true;

        try
        {
            var type = impl.GetType();
            _frameField = type.GetField("_lastRenderedFrame",
                BindingFlags.NonPublic | BindingFlags.Instance);
            _syncField = type.GetField("_sync",
                BindingFlags.NonPublic | BindingFlags.Instance);
            _canUseDirect = _frameField != null && _syncField != null;
        }
        catch
        {
            _canUseDirect = false;
        }

        return _canUseDirect;
    }

    /// <summary>
    /// Copies raw pixel data between two identically-sized <see cref="WriteableBitmap"/>
    /// instances without any new heap allocation.
    /// </summary>
    private static void CopyBitmapPixels(WriteableBitmap source, WriteableBitmap dest)
    {
        using var src = source.Lock();
        using var dst = dest.Lock();
        int bytes = Math.Min(
            src.RowBytes * src.Size.Height,
            dst.RowBytes * dst.Size.Height);

        unsafe
        {
            Buffer.MemoryCopy(
                (void*)src.Address,
                (void*)dst.Address,
                dst.RowBytes * dst.Size.Height,
                bytes);
        }
    }

    /// <summary>
    /// Compares <paramref name="source"/> and <paramref name="dest"/> pixel data.
    /// If different, copies source → dest and returns <c>true</c>.
    /// If identical, returns <c>false</c> without copying — the GPU texture
    /// upload can be skipped entirely.
    /// </summary>
    /// <remarks>
    /// <see cref="ReadOnlySpan{T}.SequenceEqual"/> is SIMD-optimized on modern
    /// .NET and completes a 1280×720×4 (≈3.5 MB) comparison in ≈50–100 µs,
    /// which is an order of magnitude cheaper than the corresponding GPU
    /// texture upload via PCIe.
    /// </remarks>
    private static unsafe bool CopyBitmapIfChanged(WriteableBitmap source, WriteableBitmap dest)
    {
        using var src = source.Lock();
        using var dst = dest.Lock();
        int bytes = Math.Min(
            src.RowBytes * src.Size.Height,
            dst.RowBytes * dst.Size.Height);

        var srcSpan = new ReadOnlySpan<byte>((void*)src.Address, bytes);
        var dstSpan = new ReadOnlySpan<byte>((void*)dst.Address, bytes);

        if (srcSpan.SequenceEqual(dstSpan))
            return false; // Content identical — no copy, no upload needed.

        Buffer.MemoryCopy(
            (void*)src.Address,
            (void*)dst.Address,
            dst.RowBytes * dst.Size.Height,
            bytes);
        return true;
    }

    public void Dispose()
    {
        _renderSurface?.Dispose();
        _renderSurface = null;
        _lastFrame?.Dispose();
        _lastFrame = null;
        _window?.Close();
        _window = null;
    }
}
