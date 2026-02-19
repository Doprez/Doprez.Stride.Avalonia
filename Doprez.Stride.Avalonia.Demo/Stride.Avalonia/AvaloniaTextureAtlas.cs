using global::Avalonia.Media.Imaging;
using Stride.Core.Mathematics;
using Stride.Graphics;
using System;
using System.Collections.Generic;

namespace Stride.Avalonia;

/// <summary>
/// Packs multiple panel textures into a shared GPU atlas texture so that
/// <see cref="Sprite3DBatch"/> can batch all world-space panels into a single
/// draw call (same texture = no batch break).
/// </summary>
/// <remarks>
/// Uses a simple shelf-based packer: panels are placed left-to-right in rows
/// ("shelves") of the tallest panel in that row. When a row is full, a new
/// row starts below it. When the atlas is full, it grows (doubling dimensions
/// up to <see cref="MaxAtlasSize"/>), preserving existing allocations via
/// GPU-side <c>CopyRegion</c>.
/// </remarks>
internal sealed class AvaloniaTextureAtlas : IDisposable
{
    /// <summary>Maximum atlas dimension (width and height).</summary>
    internal const int MaxAtlasSize = 4096;

    /// <summary>Initial atlas width.</summary>
    internal const int InitialWidth = 1024;

    /// <summary>Initial atlas height.</summary>
    internal const int InitialHeight = 1024;

    private readonly GraphicsDevice _device;
    private Texture? _texture;
    private int _atlasWidth;
    private int _atlasHeight;

    // Shelf packer state
    private int _currentX;      // next free X in the current shelf
    private int _currentY;      // Y origin of the current shelf
    private int _shelfHeight;   // height of the tallest item in the current shelf

    // Per-component allocation: position + size in the atlas
    private readonly Dictionary<AvaloniaComponent, AtlasSlot> _slots = new();

    internal readonly record struct AtlasSlot(int X, int Y, int Width, int Height);

    public AvaloniaTextureAtlas(GraphicsDevice device, int index)
    {
        _device = device;
        Index = index;
    }

    /// <summary>Zero-based index of this atlas within the <see cref="AvaloniaTextureAtlasManager"/>.</summary>
    public int Index { get; }

    /// <summary>The shared GPU atlas texture. Null until the first panel is allocated.</summary>
    public Texture? Texture => _texture;

    /// <summary>
    /// Ensures a slot is allocated for <paramref name="comp"/> at the given resolution.
    /// If the atlas is full it will attempt to grow (up to <see cref="MaxAtlasSize"/>),
    /// preserving existing allocations.
    /// Returns <c>false</c> only when the atlas is at maximum size and still full.
    /// </summary>
    public bool EnsureSlot(AvaloniaComponent comp, int width, int height,
                           CommandList commandList, out RectangleF sourceRect)
    {
        // Check for existing allocation with matching size
        if (_slots.TryGetValue(comp, out var existing)
            && existing.Width == width && existing.Height == height)
        {
            sourceRect = new RectangleF(existing.X, existing.Y, existing.Width, existing.Height);
            return true;
        }

        // Need (re-)allocation — remove old slot (space is wasted; full repack
        // would be needed to reclaim, but is rare for fixed-size panels).
        if (existing.Width != 0)
            _slots.Remove(comp);

        // Ensure the atlas texture exists
        if (_texture == null)
            CreateAtlas(Math.Max(width, InitialWidth), Math.Max(height, InitialHeight));

        if (_texture == null)
        {
            sourceRect = default;
            return false;
        }

        // Try to fit in the current shelf
        if (TryAllocate(width, height, out var slot))
        {
            _slots[comp] = slot;
            sourceRect = new RectangleF(slot.X, slot.Y, slot.Width, slot.Height);
            return true;
        }

        // Atlas is full at current size — try to grow
        if (TryGrow(commandList))
        {
            if (TryAllocate(width, height, out slot))
            {
                _slots[comp] = slot;
                sourceRect = new RectangleF(slot.X, slot.Y, slot.Width, slot.Height);
                return true;
            }
        }

        // At maximum size and still can't fit
        IsFull = true;
        sourceRect = default;
        return false;
    }

    /// <summary>
    /// Copies pixel data from a captured <see cref="WriteableBitmap"/> into
    /// the panel's allocated region in the atlas texture.
    /// </summary>
    public unsafe void UpdateSlot(AvaloniaComponent comp, WriteableBitmap bitmap, CommandList commandList)
    {
        if (_texture == null || !_slots.TryGetValue(comp, out var slot))
            return;

        using var fb = bitmap.Lock();

        var region = new ResourceRegion(
            left: slot.X,
            top: slot.Y,
            front: 0,
            right: slot.X + slot.Width,
            bottom: slot.Y + slot.Height,
            back: 1);

        int dataSize = fb.RowBytes * fb.Size.Height;

        _texture.SetData(commandList,
            new Span<byte>(fb.Address.ToPointer(), dataSize),
            region: region);
    }

    /// <summary>
    /// Retrieves the source rectangle for a previously allocated panel.
    /// </summary>
    public bool TryGetSourceRect(AvaloniaComponent comp, out RectangleF sourceRect)
    {
        if (_slots.TryGetValue(comp, out var slot))
        {
            sourceRect = new RectangleF(slot.X, slot.Y, slot.Width, slot.Height);
            return true;
        }
        sourceRect = default;
        return false;
    }

    /// <summary>Removes a panel's allocation from the atlas.</summary>
    public void Remove(AvaloniaComponent comp) => _slots.Remove(comp);

    /// <summary>Returns <c>true</c> if this atlas contains a slot for <paramref name="comp"/>.</summary>
    public bool Contains(AvaloniaComponent comp) => _slots.ContainsKey(comp);

    /// <summary>Whether this atlas has reached maximum size and has no room left.</summary>
    public bool IsFull { get; private set; }

    // ── Allocation ──

    private bool TryAllocate(int width, int height, out AtlasSlot slot)
    {
        // Try to fit in the current shelf
        if (_currentX + width > _atlasWidth)
        {
            // Move to next shelf
            _currentY += _shelfHeight;
            _currentX = 0;
            _shelfHeight = 0;
        }

        if (_currentY + height > _atlasHeight)
        {
            slot = default;
            return false;
        }

        slot = new AtlasSlot(_currentX, _currentY, width, height);
        _currentX += width;
        if (height > _shelfHeight)
            _shelfHeight = height;

        return true;
    }

    // ── Growth ──

    /// <summary>
    /// Doubles the atlas dimensions (up to <see cref="MaxAtlasSize"/>),
    /// creates a new texture, copies old content via GPU <c>CopyRegion</c>,
    /// and keeps all existing slot allocations valid.
    /// </summary>
    private bool TryGrow(CommandList commandList)
    {
        int newW = Math.Min(_atlasWidth * 2, MaxAtlasSize);
        int newH = Math.Min(_atlasHeight * 2, MaxAtlasSize);

        // Already at max — can't grow
        if (newW == _atlasWidth && newH == _atlasHeight)
            return false;

        var newTexture = Stride.Graphics.Texture.New2D(
            _device, newW, newH,
            PixelFormat.R8G8B8A8_UNorm_SRgb,
            TextureFlags.ShaderResource,
            usage: GraphicsResourceUsage.Default);

        // Copy old atlas content to the top-left of the new atlas
        if (_texture != null)
        {
            var srcRegion = new ResourceRegion(0, 0, 0, _atlasWidth, _atlasHeight, 1);
            commandList.CopyRegion(_texture, 0, srcRegion, newTexture, 0, 0, 0, 0);
            _texture.Dispose();
        }

        _texture = newTexture;
        _atlasWidth = newW;
        _atlasHeight = newH;

        // Shelf packer state is preserved — existing slots remain valid,
        // just more space was added at the bottom and right.
        return true;
    }

    // ── Creation ──

    private void CreateAtlas(int width, int height)
    {
        width = Math.Min(width, MaxAtlasSize);
        height = Math.Min(height, MaxAtlasSize);

        _texture?.Dispose();
        _atlasWidth = width;
        _atlasHeight = height;

        _texture = Stride.Graphics.Texture.New2D(
            _device, _atlasWidth, _atlasHeight,
            PixelFormat.R8G8B8A8_UNorm_SRgb,
            TextureFlags.ShaderResource,
            usage: GraphicsResourceUsage.Default);

        _slots.Clear();
        _currentX = 0;
        _currentY = 0;
        _shelfHeight = 0;
    }

    public void Dispose()
    {
        _texture?.Dispose();
        _texture = null;
        _slots.Clear();
    }
}
