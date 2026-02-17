using System.Collections.Generic;
using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Avalonia;

namespace Doprez.Stride.Avalonia.Demo;

/// <summary>
/// Spawns 1000 entities in a 10×10×10 3D grid, each with a billboarded
/// <see cref="AvaloniaComponent"/> displaying a <see cref="CounterLabel"/>
/// that shows the entity's index.
/// </summary>
public class AvaloniaGridSpawner : SyncScript
{
    /// <summary>Spacing between entities in world units.</summary>
    [DataMember(10)]
    public float Spacing { get; set; } = 2.5f;

    private readonly List<Entity> _spawnedEntities = new();

    public override void Start()
    {
        const int gridSize = 10;
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
                        Resolution = new Vector2(128, 48),
                        Size = new Vector2(0.5f, 0.19f),
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

    public override void Update()
    {
    }

    public override void Cancel()
    {
        foreach (var entity in _spawnedEntities)
        {
            var comp = entity.Get<AvaloniaComponent>();
            comp?.Page?.Dispose();
            Entity.Scene.Entities.Remove(entity);
        }
        _spawnedEntities.Clear();
    }
}
