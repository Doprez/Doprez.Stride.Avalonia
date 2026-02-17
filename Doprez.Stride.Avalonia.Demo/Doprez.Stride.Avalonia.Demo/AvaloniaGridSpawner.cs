using System.Collections.Generic;
using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Avalonia;

namespace Doprez.Stride.Avalonia.Demo;

/// <summary>
/// Spawns entities in an N×N×N 3D grid, each with a billboarded
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
    /// Dimension of the grid (N×N×N). Changing this value and calling
    /// <see cref="Respawn"/> will destroy existing panels and recreate
    /// the grid with the new size.
    /// </summary>
    [DataMember(20)]
    public int GridSize { get; set; } = 10;

    private readonly List<Entity> _spawnedEntities = new();

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
        DespawnGrid();
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
                        Resolution = new Vector2(84, 28),
                        Size = new Vector2(0.5f, 0.19f),
                        UseAtlas = true,
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
                    count++;
                }
            }
        }
    }

    private void DespawnGrid()
    {
        foreach (var entity in _spawnedEntities)
        {
            var comp = entity.Get<AvaloniaComponent>();
            comp?.Page?.Dispose();
            Entity.Scene.Entities.Remove(entity);
        }
        _spawnedEntities.Clear();
    }

    public override void Update()
    {
    }

    public override void Cancel()
    {
        DespawnGrid();
    }
}
