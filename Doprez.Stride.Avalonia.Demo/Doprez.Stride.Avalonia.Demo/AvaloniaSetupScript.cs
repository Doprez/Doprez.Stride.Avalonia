using System.Linq;
using Stride.Engine;
using Stride.Avalonia;
using Stride.Rendering.Compositing;

namespace Doprez.Stride.Avalonia.Demo;

/// <summary>
/// Bootstraps the Avalonia integration for the Stride game.
/// Attach this script to any entity in the scene (typically the root or a
/// dedicated "Systems" entity) to initialise Avalonia, register the
/// <see cref="AvaloniaSystem"/> game system, and add the
/// <see cref="AvaloniaSceneRenderer"/> to the graphics compositor.
/// <para>
/// This script is safe to add in GameStudio. It is idempotent — calling
/// <c>Start</c> more than once (or having multiple instances) will not
/// duplicate registrations.
/// </para>
/// </summary>
public class AvaloniaSetupScript : SyncScript
{
    public override void Start()
    {
        // 1. Initialise headless Avalonia with the Fluent theme
        AvaloniaApp.EnsureInitialized<DemoAvaloniaApp>();

        // 2. Register the AvaloniaSystem game system (if not already present)
        var existingSystem = Game.GameSystems.OfType<AvaloniaSystem>().FirstOrDefault();
        if (existingSystem == null)
        {
            var avaloniaSystem = new AvaloniaSystem(Services);
            Game.GameSystems.Add(avaloniaSystem);
        }

        // 3. Append the AvaloniaSceneRenderer to the graphics compositor
        AddSceneRendererToCompositor();
    }

    public override void Update()
    {
        // Nothing to do — AvaloniaSystem handles its own update cycle.
    }

    private void AddSceneRendererToCompositor()
    {
        var compositor = SceneSystem.GraphicsCompositor;
        if (compositor == null) return;

        // The default Stride compositor structure is:
        //   Game (SceneCameraRenderer) → Child (SceneRendererCollection) → [ForwardRenderer, DebugRenderer, ...]
        // We need to find the SceneRendererCollection and append our renderer.

        SceneRendererCollection? collection = null;

        if (compositor.Game is SceneCameraRenderer cameraRenderer)
        {
            if (cameraRenderer.Child is SceneRendererCollection col)
                collection = col;
        }
        else if (compositor.Game is SceneRendererCollection col)
        {
            collection = col;
        }

        if (collection != null)
        {
            // Only add if not already present
            if (!collection.Children.OfType<AvaloniaSceneRenderer>().Any())
                collection.Children.Add(new AvaloniaSceneRenderer());
        }
        else
        {
            // Fallback: wrap the existing Game renderer in a new collection
            var newCollection = new SceneRendererCollection();
            if (compositor.Game != null)
                newCollection.Children.Add(compositor.Game);
            newCollection.Children.Add(new AvaloniaSceneRenderer());
            compositor.Game = newCollection;
        }
    }
}
