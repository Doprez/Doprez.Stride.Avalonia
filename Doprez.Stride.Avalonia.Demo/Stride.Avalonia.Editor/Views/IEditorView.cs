using global::Avalonia.Controls;

namespace Stride.Avalonia.Editor.Views;

/// <summary>
/// Contract for editor views that can be loaded at runtime into the editor shell.
/// Each view provides a dock position and an Avalonia control to render.
/// </summary>
public interface IEditorView
{
    /// <summary>Human-readable name shown in the UI (e.g. "Hierarchy").</summary>
    string Title { get; }

    /// <summary>Where the view should be docked inside the editor shell.</summary>
    EditorDock Dock { get; }

    /// <summary>The Avalonia control tree for this view.</summary>
    Control Content { get; }
}

/// <summary>Supported dock positions inside the editor shell.</summary>
public enum EditorDock
{
    Left,
    Right,
    Top,
    Bottom,
    Center,
}
