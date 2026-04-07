using System.Collections.Generic;
using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Avalonia;

namespace Doprez.Stride.Avalonia.Demo;

/// <summary>
/// Spawns entities in an NxNxN 3D grid, each with a billboarded
/// <see cref="AvaloniaComponent"/> displaying a <see cref="CounterLabel"/>
/// that shows the entity's index.
/// <para>
/// The <see cref="GridSize"/> property (default 10) controls the dimension
/// of the cube and can be changed at runtime via <see cref="Respawn"/>.
/// </para>
/// </summary>
public class AvaloniaGridSpawner : SyncScript
{
    /// <summary>Spacing between entities in world units.</summary>
    [DataMember(10)]
    public float Spacing { get; set; } = 2.5f;

    /// <summary>
    /// Dimension of the grid (NxNxN). Changing this value and calling
    /// <see cref="Respawn"/> will destroy existing panels and recreate
    /// the grid with the new size.
    /// </summary>
    [DataMember(20)]
    public int GridSize { get; set; } = 10;

    private readonly List<Entity> _spawnedEntities = new();
    private readonly List<(CounterLabel Label, AvaloniaComponent Component)> _panels = new();
    private double _tickAccumulator;
    private int _tickCursor;

    public override void Start()
    {
        SpawnGrid();
    }

    /// <summary>
    /// Destroys the current grid and recreates it with the current
    /// <see cref="GridSize"/> value.
    /// </summary>
    public void Respawn()
    {
        DespawnGrid(removeEntities: true);
        SpawnGrid();
    }

    private void SpawnGrid()
    {
        int gridSize = GridSize;
        float offset = (gridSize - 1) * 0.5f;
        int count = 0;

        for (int x = 0; x < gridSize; x++)
        {
            for (int y = 0; y < gridSize; y++)
            {
                for (int z = 0; z < gridSize; z++)
                {
                    var label = new CounterLabel();
                    label.SetCount(count);

                    var page = new DefaultAvaloniaPage(label);

                    var avaloniaComponent = new AvaloniaComponent
                    {
                        IsFullScreen = false,
                        IsBillboard = true,
                        Resolution = new Vector2(192, 64),
                        Size = new Vector2(0.5f, 0.25f),
                        UseAtlas = true,
                        ContinuousRedraw = false,
                    };
                    avaloniaComponent.Page = page;

                    var entity = new Entity($"AvaloniaPanel_{count}")
                    {
                        avaloniaComponent,
                    };

                    entity.Transform.Position = new Vector3(
                        (x - offset) * Spacing,
                        (y - offset) * Spacing,
                        (z - offset) * Spacing);

                    entity.Scene = Entity.Scene;
                    _spawnedEntities.Add(entity);
                    _panels.Add((label, avaloniaComponent));
                    count++;
                }
            }
        }
    }

    private void DespawnGrid(bool removeEntities)
    {
        foreach (var entity in _spawnedEntities)
        {
            var comp = entity.Get<AvaloniaComponent>();
            if (comp?.Page != null)
            {
                comp.Page.Dispose();
                comp.Page = null;
            }

            if (removeEntities)
                entity.Scene?.Entities.Remove(entity);
        }

        _spawnedEntities.Clear();
        _panels.Clear();
        _tickCursor = 0;
    }

    public override void Update()
    {
        if (_panels.Count == 0) return;

        // Advance progress on a small staggered batch each frame.
        // Cap per-frame updates to avoid a death spiral where slow frames
        // accumulate time → more updates → even slower frames.
        const int maxPerFrame = 50;
        _tickAccumulator += Game.UpdateTime.Elapsed.TotalSeconds;
        double tickInterval = 1.0 / _panels.Count; // seconds per label
        int batchSize = 0;
        while (_tickAccumulator >= tickInterval && batchSize < maxPerFrame)
        {
            _tickAccumulator -= tickInterval;
            var (label, comp) = _panels[_tickCursor];
            label.AdvanceProgress();
            comp.Page?.MarkDirty();
            _tickCursor = (_tickCursor + 1) % _panels.Count;
            batchSize++;
        }
        // Clamp accumulator so it never builds up beyond one batch worth
        double maxAccum = tickInterval * maxPerFrame;
        if (_tickAccumulator > maxAccum)
            _tickAccumulator = maxAccum;
    }

    public override void Cancel()
    {
        var isGameExiting = Game is Game strideGame && strideGame.IsExiting;
        DespawnGrid(removeEntities: !isGameExiting);
    }
}
