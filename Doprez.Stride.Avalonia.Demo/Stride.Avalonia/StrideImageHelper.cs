using System;
using System.Runtime.InteropServices;
using global::Avalonia;
using global::Avalonia.Media.Imaging;
using global::Avalonia.Platform;
using Stride.Core.Mathematics;
using Stride.Core.Serialization.Contents;
using Stride.Graphics;
using Stride.Rendering;

namespace Stride.Avalonia;

/// <summary>
/// Utility for converting Stride <see cref="Texture"/> assets into Avalonia
/// <see cref="WriteableBitmap"/> instances, suitable for use as an
/// <see cref="global::Avalonia.Controls.Image.Source"/>.
/// </summary>
/// <remarks>
/// <para>
/// Stride textures live on the GPU and use RGBA channel order.
/// Avalonia expects CPU-side bitmaps in BGRA order.
/// This helper performs the GPU → CPU readback and the R↔B channel swizzle.
/// </para>
/// <para>
/// Intended for <b>static</b> asset thumbnails and icons (inventory items,
/// menu art, etc.).  Each call performs a full GPU readback — do not use in a
/// per-frame loop without caching the result.
/// </para>
/// <example>
/// <code>
/// // Inside a SyncScript:
/// var bitmap = StrideImageHelper.LoadAsAvaloniaBitmap(
///     Content, Game.GraphicsContext.CommandList, "Textures/MyIcon");
/// var image = new Avalonia.Controls.Image { Source = bitmap, Width = 64, Height = 64 };
/// </code>
/// </example>
/// </remarks>
public static class StrideImageHelper
{
    /// <summary>
    /// Reads pixel data from a Stride <see cref="Texture"/> and returns an
    /// Avalonia <see cref="WriteableBitmap"/> with BGRA8888 pixel format.
    /// </summary>
    /// <param name="strideTexture">
    /// A 2D texture (any <see cref="GraphicsResourceUsage"/>).
    /// Supports R8G8B8A8 and compressed formats (BC1–BC7).
    /// Compressed textures are GPU-decompressed via a render-target blit.
    /// </param>
    /// <param name="graphicsContext">
    /// The graphics context, typically
    /// <c>Game.GraphicsContext</c>.
    /// </param>
    /// <returns>
    /// A new <see cref="WriteableBitmap"/> containing the texture's pixel data.
    /// The caller owns the bitmap and is responsible for disposing it.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="strideTexture"/> or <paramref name="graphicsContext"/> is <c>null</c>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The texture is not 2D.
    /// </exception>
    public static WriteableBitmap ToAvaloniaBitmap(
        Texture strideTexture,
        GraphicsContext graphicsContext)
    {
        ArgumentNullException.ThrowIfNull(strideTexture);
        ArgumentNullException.ThrowIfNull(graphicsContext);

        var commandList = graphicsContext.CommandList;

        if (strideTexture.Dimension != TextureDimension.Texture2D)
        {
            throw new ArgumentException(
                $"Only 2D textures are supported. Got {strideTexture.Dimension}.",
                nameof(strideTexture));
        }

        // If the texture is compressed (BC1–BC7 etc.), decompress via GPU blit.
        Texture readbackTexture = strideTexture;
        Texture? tempRt = null;

        if (!IsRgba8(strideTexture.Format))
        {
            tempRt = DecompressToRgba8(strideTexture, graphicsContext);
            readbackTexture = tempRt;
        }

        try
        {
            // GPU → CPU readback (now guaranteed RGBA8).
            var pixelData = readbackTexture.GetData<byte>(commandList);

            // Swizzle RGBA → BGRA (Stride uses RGBA, Avalonia expects BGRA).
            SwizzleRgbaToBgra(pixelData);

            // Build an Avalonia WriteableBitmap and copy the data in.
            var w = readbackTexture.Width;
            var h = readbackTexture.Height;
            var size = new PixelSize(w, h);
            var dpi = new Vector(96, 96);
            var bitmap = new WriteableBitmap(size, dpi, PixelFormats.Bgra8888, AlphaFormat.Unpremul);

            using (var fb = bitmap.Lock())
            {
                var expectedBytes = w * h * 4;
                var copyLength = Math.Min(pixelData.Length, expectedBytes);

                var srcStride = w * 4;
                if (fb.RowBytes == srcStride)
                {
                    Marshal.Copy(pixelData, 0, fb.Address, copyLength);
                }
                else
                {
                    for (var y = 0; y < h; y++)
                    {
                        var srcOffset = y * srcStride;
                        var dstPtr = fb.Address + y * fb.RowBytes;
                        Marshal.Copy(pixelData, srcOffset, dstPtr, srcStride);
                    }
                }
            }

            return bitmap;
        }
        finally
        {
            tempRt?.Dispose();
        }
    }

    /// <summary>
    /// Loads a texture from the Stride content manager and converts it to an
    /// Avalonia <see cref="WriteableBitmap"/> in a single call.
    /// </summary>
    /// <param name="content">
    /// The content manager, typically <c>Content</c> inside a
    /// <see cref="Stride.Engine.SyncScript"/> or <see cref="Stride.Engine.AsyncScript"/>.
    /// </param>
    /// <param name="graphicsContext">
    /// The graphics context, typically
    /// <c>Game.GraphicsContext</c>.
    /// </param>
    /// <param name="assetPath">
    /// The Stride asset URL, e.g. <c>"Textures/MyIcon"</c>.
    /// </param>
    /// <returns>
    /// A new <see cref="WriteableBitmap"/>. The caller owns the bitmap.
    /// The loaded <see cref="Texture"/> remains managed by the content manager.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Any parameter is <c>null</c>.
    /// </exception>
    public static WriteableBitmap LoadAsAvaloniaBitmap(
        IContentManager content,
        GraphicsContext graphicsContext,
        string assetPath)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(graphicsContext);
        ArgumentNullException.ThrowIfNull(assetPath);

        var texture = content.Load<Texture>(assetPath);
        return ToAvaloniaBitmap(texture, graphicsContext);
    }

    /// <summary>
    /// Decompresses a compressed texture (BC1–BC7, etc.) by rendering it to
    /// an uncompressed R8G8B8A8 render target via <see cref="SpriteBatch"/>.
    /// The GPU handles the decompression during sampling.
    /// </summary>
    private static Texture DecompressToRgba8(
        Texture source, GraphicsContext graphicsContext)
    {
        var graphicsDevice = graphicsContext.CommandList.GraphicsDevice;

        var rt = Texture.New2D(
            graphicsDevice, source.Width, source.Height,
            Stride.Graphics.PixelFormat.R8G8B8A8_UNorm_SRgb,
            TextureFlags.RenderTarget | TextureFlags.ShaderResource);

        using var spriteBatch = new SpriteBatch(graphicsDevice);

        var commandList = graphicsContext.CommandList;
        commandList.SetRenderTargetAndViewport(null, rt);
        commandList.Clear(rt, new Color4(0, 0, 0, 0));

        spriteBatch.Begin(graphicsContext,
            sortMode: SpriteSortMode.Immediate,
            blendState: BlendStates.Opaque,
            samplerState: graphicsDevice.SamplerStates.LinearClamp,
            depthStencilState: DepthStencilStates.None);

        spriteBatch.Draw(source,
            new RectangleF(0, 0, source.Width, source.Height),
            Color.White);

        spriteBatch.End();

        return rt;
    }

    /// <summary>
    /// Checks whether a <see cref="Stride.Graphics.PixelFormat"/> is one of the
    /// supported R8G8B8A8 variants.
    /// </summary>
    private static bool IsRgba8(Stride.Graphics.PixelFormat format) =>
        format is Stride.Graphics.PixelFormat.R8G8B8A8_UNorm
              or Stride.Graphics.PixelFormat.R8G8B8A8_UNorm_SRgb;

    /// <summary>
    /// Swaps R and B channels in-place across a tightly-packed RGBA byte array,
    /// converting RGBA → BGRA (or vice-versa).
    /// </summary>
    private static void SwizzleRgbaToBgra(byte[] data)
    {
        // Process 4 bytes at a time (one pixel).
        for (var i = 0; i < data.Length - 3; i += 4)
        {
            (data[i], data[i + 2]) = (data[i + 2], data[i]);
        }
    }
}
