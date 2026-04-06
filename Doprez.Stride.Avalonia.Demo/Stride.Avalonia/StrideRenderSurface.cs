using System;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace Stride.Avalonia;

/// <summary>
/// Manages a persistent <see cref="SKSurface"/> for off-screen Avalonia rendering.
/// <para>
/// This replaces the <c>WriteableBitmap</c> capture path used by
/// <c>HeadlessWindowImpl</c>.  The <see cref="SKSurface"/> pixel buffer
/// is directly accessible via <see cref="GetPixelPointer"/>, eliminating
/// intermediate bitmap allocations and copies.
/// </para>
/// <para>
/// A native comparison buffer is maintained to detect actual pixel changes
/// via SIMD-optimised <see cref="ReadOnlySpan{T}.SequenceEqual"/>.  When the
/// rendered content is unchanged, <see cref="HasNewContent"/> is <c>false</c>
/// and the GPU texture upload can be skipped entirely.
/// </para>
/// <para>
/// <b>GPU upgrade path:</b> Replace the CPU-backed <see cref="SKSurface"/>
/// with one created from a shared <see cref="GRContext"/> (e.g.
/// <c>SKSurface.Create(grContext, budgeted, info)</c>) to eliminate the
/// CPU→GPU upload.  The rest of the pipeline (comparison, pointer access)
/// works identically.
/// </para>
/// </summary>
internal sealed class StrideRenderSurface : IDisposable
{
    private SKSurface? _surface;
    private int _width;
    private int _height;

    // Native memory comparison buffer — avoids LOH / GC entirely.
    private unsafe byte* _prevPixels;
    private int _prevSize;

    /// <summary>Width of the rendering surface in pixels.</summary>
    public int Width => _width;

    /// <summary>Height of the rendering surface in pixels.</summary>
    public int Height => _height;

    /// <summary>
    /// <c>true</c> if the most recent <see cref="CompareAndUpdate"/> detected
    /// pixel changes compared to the previous frame.  The GPU texture upload
    /// should be skipped when this is <c>false</c>.
    /// </summary>
    public bool HasNewContent { get; private set; } = true;

    /// <summary>The underlying SkiaSharp surface (may be null before first <see cref="EnsureSize"/>).</summary>
    public SKSurface? Surface => _surface;

    /// <summary>
    /// Ensures the surface exists at the requested resolution.
    /// Reallocates only when the size changes.
    /// </summary>
    public void EnsureSize(int width, int height)
    {
        if (_width == width && _height == height && _surface != null)
            return;

        _surface?.Dispose();
        _width = width;
        _height = height;

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        _surface = SKSurface.Create(info);

        HasNewContent = true;
        unsafe { FreePrevBuffer(); }
    }

    /// <summary>
    /// Returns a pointer to the raw BGRA pixel data of the surface.
    /// The pointer is valid until the next <see cref="EnsureSize"/> or
    /// <see cref="Dispose"/> call.
    /// </summary>
    public IntPtr GetPixelPointer()
    {
        if (_surface == null) return IntPtr.Zero;
        _surface.Canvas.Flush();
        using var pixmap = _surface.PeekPixels();
        return pixmap?.GetPixels() ?? IntPtr.Zero;
    }

    /// <summary>Total byte size of the pixel data (width × height × 4).</summary>
    public int GetPixelDataSize() => _width * _height * 4;

    /// <summary>
    /// Compares the current surface pixels with the previous frame.
    /// Sets <see cref="HasNewContent"/> to <c>true</c> if the pixels differ,
    /// and updates the comparison buffer.
    /// </summary>
    /// <remarks>
    /// <see cref="ReadOnlySpan{T}.SequenceEqual"/> is SIMD-optimised on
    /// modern .NET and completes a 1280×720×4 (~3.5 MB) comparison in
    /// ~50–100 µs — far cheaper than the corresponding GPU texture upload.
    /// </remarks>
    public unsafe void CompareAndUpdate()
    {
        if (_surface == null) { HasNewContent = false; return; }

        _surface.Canvas.Flush();
        using var pixmap = _surface.PeekPixels();
        if (pixmap == null) { HasNewContent = false; return; }

        int size = _width * _height * 4;
        var currentPtr = (byte*)pixmap.GetPixels();
        if (currentPtr == null) { HasNewContent = false; return; }

        if (_prevPixels == null || _prevSize != size)
        {
            EnsurePrevBuffer(size);
            Buffer.MemoryCopy(currentPtr, _prevPixels, size, size);
            HasNewContent = true;
            return;
        }

        var current = new ReadOnlySpan<byte>(currentPtr, size);
        var prev = new ReadOnlySpan<byte>(_prevPixels, size);

        if (current.SequenceEqual(prev))
        {
            HasNewContent = false;
        }
        else
        {
            Buffer.MemoryCopy(currentPtr, _prevPixels, size, size);
            HasNewContent = true;
        }
    }

    private unsafe void EnsurePrevBuffer(int size)
    {
        FreePrevBuffer();
        _prevPixels = (byte*)NativeMemory.Alloc((nuint)size);
        _prevSize = size;
    }

    private unsafe void FreePrevBuffer()
    {
        if (_prevPixels != null)
        {
            NativeMemory.Free(_prevPixels);
            _prevPixels = null;
            _prevSize = 0;
        }
    }

    ~StrideRenderSurface()
    {
        Dispose();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _surface?.Dispose();
        _surface = null;
        unsafe { FreePrevBuffer(); }
    }
}
