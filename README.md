# Doprez.Stride.Avalonia

A bridge library that renders [Avalonia UI](https://avaloniaui.net/) controls inside the [Stride 3D game engine](https://www.stride3d.net/). It uses Avalonia's headless (offscreen) rendering via Skia to capture UI frames as bitmaps, uploads them as GPU textures, and draws them in the Stride scene — either as fullscreen HUD overlays or as world-space 3D panels.

The repository also includes a dockable editor shell (hierarchy, properties inspector, viewport) built entirely with Avalonia, plus a comprehensive set of property editor controls for inspecting Stride components at runtime.

## Repository Structure

| Project | Description |
|---------|-------------|
| [Stride.Avalonia](Doprez.Stride.Avalonia.Demo/Stride.Avalonia/) | Core bridge library — headless rendering, input forwarding, texture atlas batching, profiling |
| [Stride.Avalonia.Editor](Doprez.Stride.Avalonia.Demo/Stride.Avalonia.Editor/) | Dockable editor shell with hierarchy tree, property inspector, and viewport |
| [Stride.Avalonia.Editor.Controls](Doprez.Stride.Avalonia.Demo/Stride.Avalonia.Editor.Controls/) | Specialised property editor controls (vectors, colors, entity references, etc.) |
| [Doprez.Stride.Avalonia.Demo](Doprez.Stride.Avalonia.Demo/Doprez.Stride.Avalonia.Demo/) | Demo game showcasing fullscreen overlays and 1000 world-space panels |
| [Stride.Avalonia.Tests](Doprez.Stride.Avalonia.Demo/Stride.Avalonia.Tests/) | 1000-panel stress test with FPS benchmarking |

## Features

### Core Rendering
- **Headless Avalonia rendering** — full Avalonia UI (controls, layout, data binding, themes) rendered offscreen via Skia and uploaded as GPU textures into Stride
- **Fullscreen overlays** — HUD and menu panels that auto-resize to match the back buffer
- **World-space 3D panels** — UI floating in the 3D scene with configurable size, resolution, billboarding, and frustum culling

### Input
- **Complete input forwarding** — mouse (move, click, scroll), keyboard (press/release), and text input from Stride to Avalonia
- **3D panel ray-casting** — mouse interaction with world-space panels via ray-cast hit testing

### Performance
- **Texture atlas batching** — multiple world-space panels packed into shared atlas textures for batched GPU draw calls; auto-growth up to 4096x4096
- **Zero-allocation input bridge** — compiled expression delegates bypass Avalonia's internal job scheduling (20x calls per event avoided)
- **Direct framebuffer access** — reflection-based access to `HeadlessWindowImpl._lastRenderedFrame` avoids LOH allocations from `CaptureRenderedFrame()`
- **Dirty-update throttling** — configurable `MaxDirtyUpdatesPerFrame` limits GPU uploads per frame
- **Frustum culling** — AABB-based culling for world-space panels

### Editor
- **Dockable editor shell** — Dock.Avalonia-based layout with hierarchy tree, property inspector, menu bar, and 3D viewport
- **Reflection-based property inspector** — auto-discovery of component properties via Stride attributes (`[DataMember]`, `[Display]`, `[DataMemberRange]`); supports 20+ property types
- **Specialised editors** — Vector2/3/4, Quaternion (Euler angles), Color (HSV wheel), entity/component/asset references, lists, dictionaries
- **Entity selection highlight** — emissive glow effect on selected entities with fade-in/out animation
- **Extensible** — components can implement `IEditableComponent` to customise their editor appearance

### Profiling & Debugging
- **Per-phase timing metrics** — update, input, dispatcher, draw, capture, upload, sprite batch
- **Panel statistics** — drawn/culled/dirty counts per frame
- **GC pressure tracking** — gen 0/1/2 delta monitoring
- **Rolling FPS history** — 120-frame history with average and peak frame time
- **Benchmark mode** — automated performance measurement with stutter detection
- **Built-in debug overlay** — `DebugPanel` control showing FPS, frame time, entity count, draw calls, triangles, memory, and Avalonia metrics

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Stride 4.3](https://www.stride3d.net/download/) (NuGet packages `4.3.0.2507`)
- [Avalonia 11.3](https://avaloniaui.net/) (pulled automatically via NuGet)

## Getting Started

### 1. Add NuGet References

Add the `Stride.Avalonia` package to your Stride game project:

```xml
<PackageReference Include="Stride.Avalonia" Version="*" />
```

For the editor shell, also add:

```xml
<PackageReference Include="Stride.Avalonia.Editor" Version="*" />
<PackageReference Include="Stride.Avalonia.Editor.Controls" Version="*" />
```

### 2. Create an Avalonia Application Class

Create a class inheriting from `AvaloniaApp` and configure your theme:

```csharp
using Avalonia;
using Avalonia.Themes.Fluent;
using Stride.Avalonia;

public class MyAvaloniaApp : AvaloniaApp
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }
}
```

### 3. Initialize Avalonia on Startup

In your platform entry point (e.g., `Program.cs`), initialize Avalonia before running the game:

```csharp
using Stride.Engine;
using Stride.Avalonia;

AvaloniaApp.EnsureInitialized<MyAvaloniaApp>();

using var game = new Game();
game.Run();
```

### 4. Add the Setup Script

Create a setup script and attach it to an entity in your scene. This registers the `AvaloniaSystem` and appends the `AvaloniaSceneRenderer` to the graphics compositor:

```csharp
using System.Linq;
using Stride.Engine;
using Stride.Avalonia;
using Stride.Rendering.Compositing;

public class AvaloniaSetupScript : SyncScript
{
    public override void Start()
    {
        AvaloniaApp.EnsureInitialized<MyAvaloniaApp>();

        if (!Game.GameSystems.OfType<AvaloniaSystem>().Any())
            Game.GameSystems.Add(new AvaloniaSystem(Services));

        // Append AvaloniaSceneRenderer to the compositor
        var compositor = SceneSystem.GraphicsCompositor;
        if (compositor?.Game is SceneCameraRenderer cam
            && cam.Child is SceneRendererCollection col
            && !col.Children.OfType<AvaloniaSceneRenderer>().Any())
        {
            col.Children.Add(new AvaloniaSceneRenderer());
        }
    }

    public override void Update() { }
}
```

### 5. Add Avalonia UI to Entities

Attach an `AvaloniaComponent` to any entity and assign an `AvaloniaPage`:

```csharp
// Fullscreen overlay
var overlay = new AvaloniaComponent
{
    Page = new DefaultAvaloniaPage(new MyOverlayControl()),
    IsFullscreen = true,
};
entity.Add(overlay);

// World-space 3D panel
var panel = new AvaloniaComponent
{
    Page = new DefaultAvaloniaPage(new MyPanelControl()),
    IsFullscreen = false,
    WorldSize = new Vector2(2f, 1.5f),
    Resolution = new Int2(400, 300),
    Billboard = true,
};
entity.Add(panel);
```

## Running the Demo

```bash
cd Doprez.Stride.Avalonia.Demo
dotnet run --project Doprez.Stride.Avalonia.Demo.Windows
```

On Linux:

```bash
cd Doprez.Stride.Avalonia.Demo
dotnet run --project Doprez.Stride.Avalonia.Demo.Linux
```

The demo spawns 1000 billboarded world-space Avalonia panels in a 10x10x10 grid plus a fullscreen debug overlay with real-time performance metrics. Press **Escape** to open the pause menu, which provides Resume, Settings (grid size, camera speeds, mouse sensitivity, window mode, resolution), and Exit. Use **F3** to dump metrics, **F4** to run a benchmark, and **F5** for extended benchmark.

## Running the Stress Test

```bash
cd Doprez.Stride.Avalonia.Demo
dotnet run --project Stride.Avalonia.Tests
```

Spawns 1000 panels in batches of 50/frame, measures uncapped FPS over 10 seconds, and reports average/min/max FPS with stutter rate.

## Cross-Platform Support

| Platform | Status |
|----------|--------|
| Windows (x64) | Supported |
| Linux (x64) | Supported |

The core `Stride.Avalonia` and `Stride.Avalonia.Editor.Controls` libraries target `net10.0` and are platform-agnostic. The editor shell (`Stride.Avalonia.Editor`) targets `net10.0-windows` due to Dock.Avalonia dependencies.

Demos:

- https://youtu.be/wfMYdvH_ux4
- https://youtu.be/_pIj4hAt25M

