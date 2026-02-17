# Stride.Avalonia

Core bridge library that enables rendering Avalonia UI controls inside the Stride 3D game engine. This is the foundational package — all other packages in this repository depend on it.

## How It Works

1. **Headless Avalonia** — `AvaloniaApp.EnsureInitialized<T>()` boots Avalonia's headless platform with Skia rendering (no native window). Your `AvaloniaApp` subclass configures themes and styles.
2. **Offscreen pages** — `AvaloniaPage` creates a headless `Window`, manages frame capture with dirty-tracking, and provides the raw pixel data. Uses reflection to access `HeadlessWindowImpl._lastRenderedFrame` directly, avoiding LOH allocations from `CaptureRenderedFrame()`.
3. **Entity integration** — `AvaloniaComponent` is a Stride `EntityComponent` that attaches an Avalonia page to an entity. Supports fullscreen overlays and world-space 3D panels.
4. **Game system** — `AvaloniaSystem` runs every frame during the Update phase, collecting all `AvaloniaComponent` instances, forwarding Stride input to Avalonia, and pumping the Avalonia dispatcher.
5. **Rendering** — `AvaloniaSceneRenderer` captures each component's Avalonia frame, uploads pixel data to GPU textures, and draws via `SpriteBatch` (fullscreen) or `Sprite3DBatch` (world-space).

## Features

### Rendering Modes
- **Fullscreen overlays** — auto-resize to the back buffer; drawn as screen-space sprites
- **World-space 3D panels** — configurable resolution, world size, billboarding, and AABB-based frustum culling
- **Depth-sorted rendering** — back-to-front ordering for correct alpha blending

### Input Forwarding
- **Mouse** — move, button press/release, scroll wheel, with position deduplication during camera rotation
- **Keyboard** — key press/release with full modifier support (Shift, Ctrl, Alt)
- **Text input** — `TextInput` events forwarded for text field editing
- **3D ray-casting** — mouse interaction with world-space panels via ray-cast hit testing
- **Smart input skipping** — input forwarding is skipped when mouse buttons are held (e.g., during camera rotation)

### Texture Atlas System
- **Shelf-based texture packer** — multiple world-space panels packed into shared atlas textures
- **Auto-growth** — starts at 1024x1024, grows up to 4096x4096 by doubling and GPU `CopyRegion`
- **Batched draw calls** — one draw call per atlas instead of one per panel
- **Managed via `AvaloniaTextureAtlasManager`** — handles multiple atlases with O(1) reverse lookup

### Performance
- **Zero-allocation input bridge** — `AvaloniaInputBridge` uses compiled expression delegates to call `PlatformImpl.Input` directly, bypassing `HeadlessWindowExtensions` which runs jobs 20x per event
- **Direct framebuffer access** — reflection-based shortcut avoids LOH allocations
- **Dirty-update throttling** — `MaxDirtyUpdatesPerFrame` on `AvaloniaSceneRenderer` limits GPU texture uploads per frame

### Profiling
- **`AvaloniaRenderMetrics`** — singleton tracking per-phase timings (update, input, dispatcher, draw, capture, upload, sprite batch), panel counts, bytes uploaded, GC pressure, and rolling 120-frame history
- **`AvaloniaProfilingKeys`** — integration with Stride's built-in profiler overlay
- **`DebugPanel`** — ready-to-use Avalonia `UserControl` displaying FPS, frame time, entity count, draw calls, triangles, memory, uptime, and detailed per-phase Avalonia metrics

## Key Classes

| Class | Description |
|-------|-------------|
| `AvaloniaApp` | Minimal `Application` subclass; call `EnsureInitialized<T>()` to boot headless Avalonia |
| `AvaloniaPage` | Abstract base for offscreen UI pages with dirty-tracking and frame capture |
| `DefaultAvaloniaPage` | Convenience wrapper that hosts any existing Avalonia `Control` |
| `AvaloniaComponent` | Stride `EntityComponent` — attach to an entity to display Avalonia UI |
| `AvaloniaSystem` | `GameSystemBase` — input forwarding and dispatcher pumping |
| `AvaloniaSceneRenderer` | `SceneRendererBase` — frame capture, GPU upload, and sprite drawing |
| `AvaloniaInputBridge` | Low-level input injection into headless Avalonia windows |
| `AvaloniaInputMapper` | Stride-to-Avalonia key/mouse/modifier mapping |
| `AvaloniaTextureAtlas` | Shelf-based GPU texture packer |
| `AvaloniaTextureAtlasManager` | Multi-atlas manager with reverse lookup |
| `AvaloniaRenderMetrics` | Performance metrics singleton |
| `DebugPanel` | Built-in debug overlay control |

## Dependencies

| Package | Version |
|---------|---------|
| Avalonia | 11.3.* |
| Avalonia.Desktop | 11.3.* |
| Avalonia.Headless | 11.3.* |
| Stride.Engine | 4.3.0.2507 |

## Target Framework

`net10.0` (cross-platform)

## Usage

See the [root README](../../README.md) for setup instructions and code examples.
