using Stride.Engine;

namespace Stride.Avalonia.Editor.Services;

/// <summary>
/// Shared service that tracks the currently-selected entity in the editor.
/// Views subscribe to <see cref="SelectionChanged"/> to react to selection.
/// </summary>
public sealed class EditorSelectionService
{
    private Entity? _selectedEntity;

    /// <summary>The currently selected entity, or <c>null</c> if nothing is selected.</summary>
    public Entity? SelectedEntity
    {
        get => _selectedEntity;
        set
        {
            if (ReferenceEquals(_selectedEntity, value)) return;
            var old = _selectedEntity;
            _selectedEntity = value;
            SelectionChanged?.Invoke(this, new EditorSelectionChangedEventArgs(old, value));
        }
    }

    /// <summary>Raised whenever the selection changes.</summary>
    public event EventHandler<EditorSelectionChangedEventArgs>? SelectionChanged;
}

public sealed class EditorSelectionChangedEventArgs : EventArgs
{
    public Entity? OldEntity { get; }
    public Entity? NewEntity { get; }

    public EditorSelectionChangedEventArgs(Entity? oldEntity, Entity? newEntity)
    {
        OldEntity = oldEntity;
        NewEntity = newEntity;
    }
}
