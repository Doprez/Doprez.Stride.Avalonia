# Stride.Avalonia.Tests

A standalone stress test application that benchmarks the Stride.Avalonia rendering pipeline with 1000 world-space panels.

## What It Does

1. Spawns 1000 Avalonia panels in batches of 50 per frame to avoid startup freezes
2. Disables VSync for uncapped frame rate measurement
3. Runs a 10-second benchmark measuring FPS
4. Reports average, minimum, and maximum FPS plus stutter rate
5. Includes a fullscreen debug overlay with real-time metrics

## Running

```bash
cd Doprez.Stride.Avalonia.Demo
dotnet run --project Stride.Avalonia.Tests
```

## Controls

| Key | Action |
|-----|--------|
| F3 | Dump performance metrics to console |
| F4 | Run quick benchmark |
| F5 | Run extended benchmark |

## Dependencies

| Package | Version |
|---------|---------|
| Avalonia.Desktop | 11.3.* |
| Avalonia.Themes.Fluent | 11.3.* |
| Stride.Engine | 4.3.0.2507 |
| Stride.CommunityToolkit | 1.0.0-preview.62 |

**Project References:** `Stride.Avalonia`
