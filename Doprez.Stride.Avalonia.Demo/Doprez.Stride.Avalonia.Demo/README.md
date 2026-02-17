# Doprez.Stride.Avalonia.Demo

A demo application showcasing the Stride.Avalonia bridge library. Demonstrates fullscreen overlays, world-space 3D panels, and the built-in debug overlay.

## What It Does

- **1000 world-space panels** — `AvaloniaGridSpawner` creates a 10x10x10 grid of entities, each with a billboarded `AvaloniaComponent` displaying a `CounterLabel` control
- **Fullscreen debug overlay** — `DebugOverlayScript` attaches a `DebugPanel` showing real-time FPS, frame time, entity count, draw calls, memory, and detailed Avalonia rendering metrics
- **Pause menu** — `EscapeMenuScript` toggles a fullscreen pause menu overlay when **Escape** is pressed, offering Resume, Settings, and Exit options. The Settings page lets you adjust the grid size, camera speeds, mouse sensitivity, sprint multiplier, window mode (windowed / fullscreen windowed / fullscreen exclusive), resolution, and resizable toggle — all applied at runtime
- **Avalonia bootstrap** — `AvaloniaSetupScript` handles all initialization: boots the headless platform, registers the `AvaloniaSystem`, and injects the `AvaloniaSceneRenderer` into the graphics compositor
- **WASD camera** — `BasicCameraController` provides standard FPS-style camera movement with keyboard, gamepad, and touch support

## Project Structure

| Project | Description |
|---------|-------------|
| `Doprez.Stride.Avalonia.Demo` | Shared game code (scripts, controls, Stride assets) |
| `Doprez.Stride.Avalonia.Demo.Windows` | Windows executable entry point (`win-x64`) |
| `Doprez.Stride.Avalonia.Demo.Linux` | Linux executable entry point (`linux-x64`) |

## Key Files

| File | Description |
|------|-------------|
| `AvaloniaSetupScript.cs` | Bootstraps Avalonia integration — safe for GameStudio, idempotent |
| `DemoAvaloniaApp.cs` | Custom `AvaloniaApp` with Fluent dark theme |
| `AvaloniaGridSpawner.cs` | Spawns 1000 billboarded world-space panels |
| `CounterLabel.cs` | Minimal styled Avalonia control (TextBlock in a bordered container) |
| `DebugOverlayScript.cs` | Creates the fullscreen debug overlay with F3/F4/F5 hotkeys |
| `EscapeMenuScript.cs` | Listens for Escape and toggles the pause menu overlay |
| `EscapeMenuControl.cs` | Pause menu UI — Resume, Settings, and Exit buttons |
| `SettingsControl.cs` | Settings panel with Scene UI, Camera, and Window sections |
| `BasicCameraController.cs` | WASD + right-click mouse look camera controller |

## Running

### Windows

```bash
cd Doprez.Stride.Avalonia.Demo
dotnet run --project Doprez.Stride.Avalonia.Demo.Windows
```

### Linux

```bash
cd Doprez.Stride.Avalonia.Demo
dotnet run --project Doprez.Stride.Avalonia.Demo.Linux
```

## Controls

| Key | Action |
|-----|--------|
| WASD | Move camera |
| Right-click + mouse | Look around |
| Q / E | Move down / up |
| Shift | Sprint |
| Escape | Toggle pause menu (Resume / Settings / Exit) |
| F3 | Dump performance metrics to console |
| F4 | Run quick benchmark |
| F5 | Run extended benchmark |

## Dependencies

| Package | Version |
|---------|---------|
| Stride.Engine | 4.3.0.2507 |
| Stride.Video / Physics / Navigation / Particles / UI | 4.3.0.2507 |
| Avalonia.Themes.Fluent | 11.3.* |

**Project References:** `Stride.Avalonia`
