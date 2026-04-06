using System;

namespace Stride.Avalonia;

/// <summary>
/// Provides lock-free pixel access to a rendered Avalonia frame.
/// Returned by <see cref="AvaloniaPage.LockPixels"/>.
/// </summary>
public readonly struct PixelAccess : IDisposable
{
    /// <summary>Pointer to the raw BGRA pixel data.</summary>
    public readonly IntPtr Address;
    /// <summary>Total byte count of the pixel data.</summary>
    public readonly int DataSize;
    /// <summary>Bytes per row (may include padding).</summary>
    public readonly int RowBytes;
    /// <summary>Image width in pixels.</summary>
    public readonly int Width;
    /// <summary>Image height in pixels.</summary>
    public readonly int Height;

    private readonly IDisposable? _lock;

    internal PixelAccess(IntPtr address, int dataSize, int rowBytes,
                         int width, int height, IDisposable? lockObj = null)
    {
        Address = address;
        DataSize = dataSize;
        RowBytes = rowBytes;
        Width = width;
        Height = height;
        _lock = lockObj;
    }

    /// <summary>Releases the underlying framebuffer lock (if any).</summary>
    public void Dispose() => _lock?.Dispose();
}
