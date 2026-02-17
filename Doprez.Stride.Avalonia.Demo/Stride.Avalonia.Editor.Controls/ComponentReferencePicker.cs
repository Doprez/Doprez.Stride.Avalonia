using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using FluentAvalonia.UI.Controls;
using Stride.Engine;
using Stride.Avalonia.Editor.Controls.Serialization;

namespace Stride.Avalonia.Editor.Controls;

/// <summary>
/// Filterable dropdown picker for selecting an <see cref="EntityComponent"/> from
/// any entity in the scene. Populates with components matching the expected type
/// from <see cref="EditableProperty.ClrType"/>.
/// </summary>
public sealed class ComponentReferencePicker : UserControl
{
    private readonly EditableProperty _property;
    private readonly EntityComponent _component;
    private readonly AutoCompleteBox _autoComplete;
    private readonly Button _clearButton;
    private List<ComponentEntry> _entries = new();

    public event EventHandler<EntityComponent?>? ValueChanged;

    public ComponentReferencePicker(EditableProperty property, EntityComponent component)
    {
        _property = property;
        _component = component;

        _autoComplete = new AutoCompleteBox
        {
            Watermark = "Search component...",
            FontSize = 11,
            MinWidth = 140,
            FilterMode = AutoCompleteFilterMode.ContainsOrdinal,
        };
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

        PopulateComponents();

        // Set initial value
        var current = _property.GetValue(_component) as EntityComponent;
        if (current != null)
            _autoComplete.Text = FormatComponentEntry(current);

        Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            Children = { _autoComplete, _clearButton },
        };
    }

    private void PopulateComponents()
    {
        _entries.Clear();
        var targetType = _property.ClrType;

        var entity = _component.Entity;
        if (entity?.Scene != null)
        {
            foreach (var e in entity.Scene.Entities)
            {
                CollectMatchingComponents(e, targetType);
            }
        }

        _autoComplete.ItemsSource = _entries.Select(e => e.DisplayName).ToList();
    }

    private void CollectMatchingComponents(Entity entity, Type targetType)
    {
        foreach (var comp in entity.Components)
        {
            if (targetType.IsAssignableFrom(comp.GetType()))
            {
                _entries.Add(new ComponentEntry
                {
                    Component = comp,
                    DisplayName = FormatComponentEntry(comp),
                });
            }
        }

        foreach (var childTransform in entity.Transform.Children)
        {
            CollectMatchingComponents(childTransform.Entity, targetType);
        }
    }

    private static string FormatComponentEntry(EntityComponent comp)
    {
        var entityName = comp.Entity?.Name ?? "(detached)";
        var typeName = comp.GetType().Name;
        return $"{entityName} → {typeName}";
    }

    private void OnSelectionChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
    {
        if (_autoComplete.SelectedItem is string selectedDisplay)
        {
            var entry = _entries.FirstOrDefault(en => en.DisplayName == selectedDisplay);
            if (entry != null)
            {
                var old = _property.GetValue(_component);
                _property.SetValue(_component, entry.Component);
                ValueChanged?.Invoke(this, entry.Component);
                NotifyComponent(old, entry.Component);
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

    private class ComponentEntry
    {
        public EntityComponent Component { get; set; } = null!;
        public string DisplayName { get; set; } = "";
    }
}
