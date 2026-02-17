# Stride.Avalonia.Editor

A dockable editor shell for inspecting and manipulating Stride scenes at runtime, built entirely with Avalonia UI. Provides a hierarchy tree, property inspector, menu bar, and a transparent viewport region that lets the 3D scene render through.

## Features

### Dockable Layout
- **Dock.Avalonia integration** — panels are dockable and resizable using the Dock.Avalonia framework
- **Configurable dock positions** — views can be placed left, right, top, bottom, or center
- **Transparent viewport** — the center dock region is transparent, allowing the 3D scene to show through
- **Floating windows disabled** — prevents accidental undocking in game contexts

### Hierarchy View (Left Panel)
- **Scene tree** — displays the full entity hierarchy rooted at the Scene node
- **Transform-aware** — respects parent/child relationships via `TransformComponent`
- **Auto-refresh** — 500ms polling with hash-based change detection for structural updates
- **Selection** — clicking a node sets the selected entity via `EditorSelectionService`
- **Icons** — FluentAvalonia `SymbolIcon` decorations for visual clarity

### Properties View (Right Panel)
- **Transform section** — Position, Rotation (Euler), and Scale editors using `Vector3Editor`
- **Component inspector** — auto-discovers editable properties via reflection and Stride attributes
- **Live updates** — 500ms periodic refresh of transform values without full panel rebuild
- **Extensible** — components implementing `IEditableComponent` can provide custom property lists

### Menu Bar (Top)
- **File, Edit, View, Help** menus with FluentAvalonia icons and keyboard shortcuts

### Entity Selection Highlight
- **`HighlightSystem`** — applies a sky-blue emissive glow to the selected entity's materials
- **Animated** — 0.3s fade-in → hold → 0.3s fade-out (1 second total cycle)
- **Non-destructive** — restores original material parameters after the highlight cycle

### Scene Viewport Rendering
- **`SceneViewportRenderer`** — renders the 3D scene to an offscreen render target sized to the editor viewport panel, then blits it onto the correct region of the back buffer
- **Dynamic sizing** — render target resizes when the viewport panel is resized
- **Auto-release** — releases the RT when the viewport is hidden or minimized

## Key Classes

| Class | Description |
|-------|-------------|
| `EditorShell` | Top-level `UserControl` composing all editor views into a dockable layout |
| `HierarchyView` | Left-docked tree view of scene entities |
| `PropertiesView` | Right-docked property inspector for the selected entity |
| `MenuBarView` | Top-docked menu bar with File/Edit/View/Help menus |
| `SceneViewportRenderer` | Offscreen 3D rendering to the viewport region |
| `HighlightSystem` | Emissive glow on selected entities |
| `EditorSelectionService` | Shared service tracking the currently selected `Entity` |
| `ViewportBoundsService` | Thread-safe normalised viewport bounds (Avalonia ↔ Stride) |
| `IEditorView` | Interface for plugging custom views into the dock layout |

## Dependencies

| Package | Version |
|---------|---------|
| Avalonia | 11.3.* |
| Avalonia.Themes.Fluent | 11.3.* |
| FluentAvaloniaUI | 2.* |
| Dock.Avalonia | 11.3.* |
| Dock.Model.Avalonia | 11.3.* |
| Dock.Avalonia.Themes.Fluent | 11.3.* |
| Stride.Engine | 4.3.0.2507 |
| Stride.Rendering | 4.3.0.2507 |
| Stride.Graphics | 4.3.0.2507 |

**Project References:** `Stride.Avalonia`, `Stride.Avalonia.Editor.Controls`

## Target Framework

`net10.0-windows`

## Adding Custom Editor Views

Implement the `IEditorView` interface to add your own panels:

```csharp
public class MyCustomView : IEditorView
{
    public string Title => "My Panel";
    public EditorDock Dock => EditorDock.Bottom;
    public Control Content => new TextBlock { Text = "Hello from my panel!" };
}
```

Then register it with the `EditorShell` when constructing the editor.
