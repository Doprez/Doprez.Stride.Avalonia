using System;
using System.Diagnostics;
using System.IO;
using Stride.CommunityToolkit.Games;
using Stride.CommunityToolkit.Engine;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;
using Stride.Rendering.Compositing;
using Stride.Avalonia;
using Stride.Avalonia.Tests;

Directory.SetCurrentDirectory(AppContext.BaseDirectory);

// Initialise headless Avalonia with Fluent theme
AvaloniaApp.EnsureInitialized<TestAvaloniaApp>();

const int TotalPanels = 1000;
const int SpawnBatchSize = 50;         // panels spawned per frame to avoid freezing
const double MeasureDurationSec = 10.0;
const float GridSpacing = 2.0f;        // world-unit spacing between panels

// Lay out panels in a 3D grid (roughly 10 x 10 x 10)
int gridSize = (int)Math.Ceiling(Math.Cbrt(TotalPanels)); // ~10

int spawned = 0;
bool allSpawned = false;
Stopwatch? measureStopwatch = null;
int frameCount = 0;
double fpsAccumulator = 0;
int fpsSampleCount = 0;
double minFps = double.MaxValue;
double maxFps = double.MinValue;
bool reported = false;
DebugPanel? debugPanel = null;
global::Avalonia.Controls.TextBlock? spawnLine = null;
global::Avalonia.Controls.TextBlock? phaseLine = null;

using var game = new Game();

game.Run(context: null, start: Start, update: Update);

void Start(Scene rootScene)
{
    game.SetMaxFPS(1000); // uncapped to get an accurate measure of max FPS under load
    game.DisableVSync();

    game.Window.AllowUserResizing = true;
    game.Window.Title = "Stride.Avalonia - 1000 Panel Stress Test";

    // 1. Compositor with no post-effects, no 3D geometry/lights
    //    (avoids shader compilation issues with local Stride builds)
    var compositor = GraphicsCompositorHelper.CreateDefault(false);
    game.SceneSystem.GraphicsCompositor = compositor;

    // Position the camera so it can see the full 10x10x10 grid
    float halfGrid = gridSize * GridSpacing * 0.5f;
    var cameraEntity = game.Add3DCamera(
        initialPosition: new Vector3(halfGrid, halfGrid + 5f, halfGrid + gridSize * GridSpacing * 1.5f),
        initialRotation: new Vector3(0, -15, 0)); // slight downward pitch

    var camera = cameraEntity.Get<CameraComponent>()!;
    camera.FarClipPlane = 500f;
    camera.NearClipPlane = 0.1f;

    // Attach fly-around camera controller (WASD + right-click look)
    cameraEntity.Add(new BasicCameraController
    {
        KeyboardMovementSpeed = new Vector3(10f),
        SpeedFactor = 5f,
        MouseRotationSpeed = new Vector2(90f, 60f),
    });

    // 2. Register the Avalonia system (handles input + lifecycle)
    var avaloniaSystem = new AvaloniaSystem(game.Services);
    game.GameSystems.Add(avaloniaSystem);

    // 3. Append the Avalonia scene renderer to the compositor
    game.AddSceneRenderer(new AvaloniaSceneRenderer());

    // 4. Debug overlay
    debugPanel = new DebugPanel { ShowMinMaxFps = true };
    spawnLine = debugPanel.AddCustomLine("Spawned: 0 / " + TotalPanels);
    phaseLine = debugPanel.AddCustomLine("Phase: Spawning...");

    var debugPage = new DefaultAvaloniaPage(debugPanel);
    debugPanel.Page = debugPage; // Wire up so DebugPanel can mark dirty on refresh

    var debugOverlay = new AvaloniaComponent
    {
        IsFullScreen = true,
        Resolution = new Vector2(game.Window.ClientBounds.Width, game.Window.ClientBounds.Height),
        Page = debugPage,
    };
    var debugEntity = new Entity("DebugOverlay") { debugOverlay };
    debugEntity.Scene = rootScene;
}

void Update(Scene rootScene, GameTime gameTime)
{
    // Update the debug panel every frame
    debugPanel?.Update(gameTime, rootScene, game);

    // F3 = dump Avalonia performance metrics to console
    // F4 = start benchmark, F5 = stop + dump benchmark
    var input = game.Services.GetService<InputManager>();
    if (input?.Keyboard != null)
    {
        if (input.Keyboard.IsKeyPressed(Keys.F3))
            AvaloniaRenderMetrics.Instance.DumpToConsole();

        if (input.Keyboard.IsKeyPressed(Keys.F4))
            AvaloniaRenderMetrics.Instance.StartBenchmark();

        if (input.Keyboard.IsKeyPressed(Keys.F5))
        {
            AvaloniaRenderMetrics.Instance.StopBenchmark();
            AvaloniaRenderMetrics.Instance.DumpBenchmark();
        }
    }

    // --- Spawn phase: add panels in batches to avoid a long initial freeze ---
    if (!allSpawned)
    {
        int toSpawn = Math.Min(SpawnBatchSize, TotalPanels - spawned);
        for (int i = 0; i < toSpawn; i++)
        {
            int index = spawned;
            int ix = index % gridSize;
            int iy = (index / gridSize) % gridSize;
            int iz = index / (gridSize * gridSize);

            var panel = new AvaloniaComponent
            {
                IsFullScreen = false,
                Resolution = new Vector2(128, 64),
                Size = new Vector2(1.0f, 0.5f),
                IsBillboard = true,
                Page = new DefaultAvaloniaPage(new CounterLabel(index + 1)),
                //UseAtlas = false,
            };

            var entity = new Entity($"Panel_{index + 1}") { panel };
            entity.Transform.Position = new Vector3(
                ix * GridSpacing,
                iy * GridSpacing,
                iz * GridSpacing);
            entity.Scene = rootScene;

            spawned++;
        }

        if (spawnLine != null)
            global::Avalonia.Threading.Dispatcher.UIThread.Post(
                () => spawnLine.Text = $"Spawned: {spawned} / {TotalPanels}");

        if (spawned >= TotalPanels)
        {
            allSpawned = true;
            measureStopwatch = Stopwatch.StartNew();
            frameCount = 0;
            fpsAccumulator = 0;
            fpsSampleCount = 0;
            minFps = 1;
            maxFps = 5;
            Console.WriteLine($"[StressTest] All {TotalPanels} panels spawned. Measuring FPS for {MeasureDurationSec}s ...");
            if (phaseLine != null)
                global::Avalonia.Threading.Dispatcher.UIThread.Post(
                    () => phaseLine.Text = $"Phase: Measuring ({MeasureDurationSec}s)...");
        }

        return;
    }

    // --- Measure phase: collect FPS samples for the configured duration ---
    if (reported || measureStopwatch == null) return;

    frameCount++;
    double elapsed = measureStopwatch.Elapsed.TotalSeconds;

    // Sample instantaneous FPS every ~0.25s
    if (elapsed > 0.25 * (fpsSampleCount + 1) && elapsed <= MeasureDurationSec)
    {
        double currentFps = frameCount / elapsed;
        fpsAccumulator += currentFps;
        fpsSampleCount++;
        if (currentFps < minFps) minFps = currentFps;
        if (currentFps > maxFps) maxFps = currentFps;
    }

    if (elapsed >= MeasureDurationSec)
    {
        double avgFps = frameCount / elapsed;
        double sampledAvg = fpsSampleCount > 0 ? fpsAccumulator / fpsSampleCount : avgFps;

        Console.WriteLine("========================================================");
        Console.WriteLine("       Stride.Avalonia 1000-Panel Stress Test            ");
        Console.WriteLine("========================================================");
        Console.WriteLine($"  Panels spawned : {TotalPanels,6}");
        Console.WriteLine($"  Measure window : {MeasureDurationSec,6:F1}s");
        Console.WriteLine($"  Total frames   : {frameCount,6}");
        Console.WriteLine($"  Average FPS    : {avgFps,8:F2}");
        Console.WriteLine($"  Sampled Avg    : {sampledAvg,8:F2}");
        Console.WriteLine($"  Min FPS (samp) : {(minFps == double.MaxValue ? 0 : minFps),8:F2}");
        Console.WriteLine($"  Max FPS (samp) : {(maxFps == double.MinValue ? 0 : maxFps),8:F2}");
        Console.WriteLine("========================================================");

        reported = true;
        if (phaseLine != null)
        {
            var avg = avgFps;
            global::Avalonia.Threading.Dispatcher.UIThread.Post(
                () => phaseLine.Text = $"Phase: Done — Avg {avg:F1} FPS");
        }
    }
}
