using global::Avalonia.Media.Imaging;
using Stride.Core.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Graphics;
using System;
using System.Collections.Generic;

namespace Stride.Avalonia;

/// <summary>
/// Manages multiple <see cref="AvaloniaTextureAtlas"/> instances so that when
/// one atlas fills up, a new one is created automatically. This removes the
/// hard cap on the number of atlas-backed panels that can be rendered.
/// </summary>
/// <remarks>
/// Each atlas starts at 1024x1024 and grows independently up to 4096x4096.
/// A reverse-lookup dictionary maps each component to its owning atlas index
/// for O(1) slot/texture lookups. When drawing, the caller groups panels by
/// atlas index or breaks the sprite batch when the atlas changes in sorted
/// order, preserving correct depth ordering.
/// </remarks>
internal sealed class AvaloniaTextureAtlasManager : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly List<AvaloniaTextureAtlas> _atlases = new();

    /// <summary>
    /// Reverse lookup: component → index into <see cref="_atlases"/>.
    /// </summary>
    private readonly Dictionary<AvaloniaComponent, int> _componentAtlas = new();

    private bool _loggedFullWarning;

    private static readonly Logger _log = GlobalLogger.GetLogger(nameof(AvaloniaTextureAtlasManager));

    public AvaloniaTextureAtlasManager(GraphicsDevice device)
    {
        _device = device;
    }

    /// <summary>Number of atlas textures currently allocated.</summary>
    public int AtlasCount => _atlases.Count;

    /// <summary>
    /// Returns the GPU texture for the atlas at <paramref name="atlasIndex"/>.
    /// </summary>
    public Texture? GetTexture(int atlasIndex)
    {
        if (atlasIndex < 0 || atlasIndex >= _atlases.Count) return null;
        return _atlases[atlasIndex].Texture;
    }

    /// <summary>
    /// Ensures a slot is allocated for <paramref name="comp"/> at the given
    /// resolution. If the component already has a slot in an atlas, that atlas
    /// is tried first. When an atlas is full, a new one is created.
    /// </summary>
    /// <returns><c>true</c> if a slot was allocated; <c>false</c> only on
    /// catastrophic failure (e.g. GPU cannot create a texture).</returns>
    public bool EnsureSlot(AvaloniaComponent comp, int width, int height,
                           CommandList commandList,
                           out RectangleF sourceRect, out int atlasIndex)
    {
        // ── 1. Try the atlas the component is already in ──
        if (_componentAtlas.TryGetValue(comp, out var existingIdx))
        {
            var atlas = _atlases[existingIdx];
            if (atlas.EnsureSlot(comp, width, height, commandList, out sourceRect))
            {
                atlasIndex = existingIdx;
                return true;
            }

            // Slot didn't fit (e.g. resolution changed and atlas is full).
            // Remove from old atlas so we can try another.
            atlas.Remove(comp);
            _componentAtlas.Remove(comp);
        }

        // ── 2. Try the last (most recently created) atlas ──
        if (_atlases.Count > 0)
        {
            var last = _atlases[^1];
            if (!last.IsFull && last.EnsureSlot(comp, width, height, commandList, out sourceRect))
            {
                atlasIndex = last.Index;
                _componentAtlas[comp] = atlasIndex;
                return true;
            }
        }

        // ── 3. Create a new atlas and retry ──
        var newAtlas = new AvaloniaTextureAtlas(_device, _atlases.Count);
        _atlases.Add(newAtlas);

        if (newAtlas.EnsureSlot(comp, width, height, commandList, out sourceRect))
        {
            atlasIndex = newAtlas.Index;
            _componentAtlas[comp] = atlasIndex;
            return true;
        }

        // If a single panel is larger than MaxAtlasSize this will fail.
        if (!_loggedFullWarning)
        {
            _log.Warning($"Panel ({width}x{height}) exceeds maximum atlas size " +
                          $"({AvaloniaTextureAtlas.MaxAtlasSize}) — cannot allocate");
            _loggedFullWarning = true;
        }

        atlasIndex = -1;
        return false;
    }

    /// <summary>
    /// Copies pixel data from a captured <see cref="WriteableBitmap"/> into
    /// the component's allocated region in the correct atlas texture.
    /// </summary>
    public void UpdateSlot(AvaloniaComponent comp, WriteableBitmap bitmap, CommandList commandList)
    {
        if (!_componentAtlas.TryGetValue(comp, out var idx)) return;
        _atlases[idx].UpdateSlot(comp, bitmap, commandList);
    }

    /// <summary>
    /// Retrieves the source rectangle and atlas index for a previously
    /// allocated panel.
    /// </summary>
    public bool TryGetSourceRect(AvaloniaComponent comp,
                                  out RectangleF sourceRect, out int atlasIndex)
    {
        if (_componentAtlas.TryGetValue(comp, out var idx))
        {
            if (_atlases[idx].TryGetSourceRect(comp, out sourceRect))
            {
                atlasIndex = idx;
                return true;
            }
        }

        sourceRect = default;
        atlasIndex = -1;
        return false;
    }

    /// <summary>Removes a panel's allocation from whichever atlas owns it.</summary>
    public void Remove(AvaloniaComponent comp)
    {
        if (_componentAtlas.TryGetValue(comp, out var idx))
        {
            _atlases[idx].Remove(comp);
            _componentAtlas.Remove(comp);
        }
    }

    public void Dispose()
    {
        foreach (var atlas in _atlases)
            atlas.Dispose();
        _atlases.Clear();
        _componentAtlas.Clear();
    }
}
