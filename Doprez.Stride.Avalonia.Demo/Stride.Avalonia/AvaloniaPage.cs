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
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Captures the last rendered Avalonia frame as a <see cref="WriteableBitmap"/>.
    /// Returns cached bitmap if the page is not dirty.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>CaptureRenderedFrame()</c> internally calls
    /// <c>HeadlessWindowImpl.GetLastRenderedFrame()</c> which creates a
    /// <b>brand-new</b> <see cref="WriteableBitmap"/> copy of the internal
    /// framebuffer on every call.  For a 1280x720 panel that is ~3.5 MB —
    /// large enough to land on the Large-Object Heap and trigger gen-2 GC
    /// pauses (visible as frame stutters).
    /// </para>
    /// <para>
    /// To avoid this, we access the <c>HeadlessWindowImpl._lastRenderedFrame</c>
    /// field directly via reflection (the same pattern
    /// <see cref="AvaloniaInputBridge"/> uses for internal input APIs).
    /// This gives us a read-only reference to the already-rendered bitmap
    /// without allocating a copy.  We then memcpy the pixels into our
    /// reusable <c>_lastFrame</c> bitmap.
    /// </para>
    /// <para>
    /// <c>AvaloniaSystem.Update()</c> calls <c>ForceRenderTimerTick()</c>
    /// before <c>DrawCore</c> runs, so the internal framebuffer is guaranteed
    /// to contain the latest content when this method is called.
    /// </para>
    /// <para>
    /// If reflection fails (Avalonia internals changed), we fall back to
    /// <c>CaptureRenderedFrame()</c> transparently.
    /// </para>
    /// </remarks>
    public WriteableBitmap? CaptureFrame()
    {
        if (_window == null) return null;
        if (!_isDirty && _lastFrame != null)
            return _lastFrame;

        // Fast path: read the internal framebuffer directly (zero LOH allocation).
        // Requires _lastFrame to exist (first call falls through to slow path).
        if (_lastFrame != null && TryCaptureDirectly())
        {
            _isDirty = false;
            return _lastFrame;
        }

        // Slow path: CaptureRenderedFrame (allocates a temp WriteableBitmap on LOH).
        // Used only on the very first capture or if reflection fails.
        var captured = _window.CaptureRenderedFrame();
        if (captured == null)
        {
            _isDirty = false;
            return _lastFrame;
        }

        if (_lastFrame != null
            && _lastFrame.PixelSize == captured.PixelSize
            && !ReferenceEquals(_lastFrame, captured))
        {
            CopyBitmapPixels(captured, _lastFrame);
            captured.Dispose();
        }
        else if (!ReferenceEquals(_lastFrame, captured))
        {
            _lastFrame?.Dispose();
            _lastFrame = captured;
        }

        _isDirty = false;
        return _lastFrame;
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
            }

            CopyBitmapPixels(internalBitmap, _lastFrame);
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

    public void Dispose()
    {
        _lastFrame?.Dispose();
        _lastFrame = null;
        _window?.Close();
        _window = null;
    }
}
