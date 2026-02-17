using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using FluentAvalonia.UI.Controls;
using Stride.Engine;
using Stride.Avalonia.Editor.Controls.Serialization;

namespace Stride.Avalonia.Editor.Controls;

/// <summary>
/// Filterable dropdown picker for selecting an <see cref="Entity"/> from the scene.
/// Uses an <see cref="AutoCompleteBox"/> that searches by entity name.
/// </summary>
public sealed class EntityReferencePicker : UserControl
{
    private readonly EditableProperty _property;
    private readonly EntityComponent _component;
    private readonly AutoCompleteBox _autoComplete;
    private readonly Button _clearButton;
    private List<Entity> _entities = new();

    public event EventHandler<Entity?>? ValueChanged;

    public EntityReferencePicker(EditableProperty property, EntityComponent component)
    {
        _property = property;
        _component = component;

        _autoComplete = new AutoCompleteBox
        {
            Watermark = "Search entity...",
            FontSize = 11,
            MinWidth = 120,
            FilterMode = AutoCompleteFilterMode.ContainsOrdinal,
        };
        _autoComplete.TextChanged += OnTextChanged;
        _autoComplete.SelectionChanged += OnSelectionChanged;

        _clearButton = new Button
        {
            Content = new SymbolIcon { Symbol = Symbol.Delete, FontSize = 12 },
            Padding = new Thickness(4, 2),
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _clearButton.Click += (_, _) =>
        {
            _autoComplete.Text = "";
            _autoComplete.SelectedItem = null;
            var old = _property.GetValue(_component);
            _property.SetValue(_component, null);
            ValueChanged?.Invoke(this, null);
            NotifyComponent(old, null);
        };

        PopulateEntities();

        // Set initial value
        var current = _property.GetValue(_component) as Entity;
        if (current != null)
            _autoComplete.Text = current.Name ?? "(unnamed)";

        Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            Children = { _autoComplete, _clearButton },
        };
    }

    private void PopulateEntities()
    {
        _entities.Clear();

        // Walk up to find the scene from the component's entity
        var entity = _component.Entity;
        if (entity?.Scene != null)
        {
            CollectEntities(entity.Scene, _entities);
        }

        _autoComplete.ItemsSource = _entities.Select(e => e.Name ?? "(unnamed)").ToList();
    }

    private static void CollectEntities(Scene scene, List<Entity> result)
    {
        foreach (var entity in scene.Entities)
        {
            result.Add(entity);
            CollectChildEntities(entity, result);
        }
    }

    private static void CollectChildEntities(Entity parent, List<Entity> result)
    {
        foreach (var childTransform in parent.Transform.Children)
        {
            result.Add(childTransform.Entity);
            CollectChildEntities(childTransform.Entity, result);
        }
    }

    private void OnTextChanged(object? sender, EventArgs e)
    {
        // Refresh entity list on every text change (scene may have changed)
        PopulateEntities();
    }

    private void OnSelectionChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
    {
        if (_autoComplete.SelectedItem is string selectedName)
        {
            var entity = _entities.FirstOrDefault(ent =>
                (ent.Name ?? "(unnamed)") == selectedName);

            if (entity != null)
            {
                var old = _property.GetValue(_component);
                _property.SetValue(_component, entity);
                ValueChanged?.Invoke(this, entity);
                NotifyComponent(old, entity);
            }
        }
    }

    private void NotifyComponent(object? oldValue, object? newValue)
    {
        if (_component is IEditableComponent editable)
        {
            if (!editable.OnPropertyChanged(_property, oldValue, newValue))
                _property.SetValue(_component, oldValue);
        }
    }
}
