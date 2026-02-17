using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Primitives;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using FluentAvalonia.UI.Controls;
using Stride.Engine;
using Stride.Avalonia.Editor.Controls.Serialization;

namespace Stride.Avalonia.Editor.Controls;

/// <summary>
/// Inline editor for <see cref="IList{T}"/> / <see cref="ICollection{T}"/> properties.
/// Uses an <see cref="Expander"/> with Add/Remove buttons and an <see cref="ItemsControl"/>
/// to display elements with inline editing.
/// </summary>
public sealed class ListEditor : UserControl
{
    private readonly EditableProperty _property;
    private readonly EntityComponent _component;
    private readonly StackPanel _itemsPanel;
    private readonly TextBlock _countLabel;

    /// <summary>Raised when the list is mutated.</summary>
    public event EventHandler? ListChanged;

    public ListEditor(EditableProperty property, EntityComponent component)
    {
        _property = property;
        _component = component;

        _countLabel = new TextBlock
        {
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(140, 200, 140)),
            VerticalAlignment = VerticalAlignment.Center,
        };

        _itemsPanel = new StackPanel { Spacing = 2 };

        var addButton = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children =
                {
                    new SymbolIcon { Symbol = Symbol.Add, FontSize = 12 },
                    new TextBlock { Text = "Add", FontSize = 11, VerticalAlignment = VerticalAlignment.Center },
                },
            },
            Padding = new Thickness(6, 2),
            Margin = new Thickness(0, 2),
        };
        addButton.Click += OnAddClick;

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Thickness(0, 4, 0, 2),
            Children = { addButton, _countLabel },
        };

        var expander = new Expander
        {
            Header = BuildHeader(),
            IsExpanded = false,
            Content = new StackPanel
            {
                Spacing = 2,
                Children = { toolbar, _itemsPanel },
            },
        };

        Content = expander;
        Refresh();
    }

    public void Refresh()
    {
        _itemsPanel.Children.Clear();

        var val = _property.GetValue(_component);
        if (val is not System.Collections.IList list)
        {
            _countLabel.Text = "(not a list)";
            return;
        }

        _countLabel.Text = $"{list.Count} items";

        for (int i = 0; i < list.Count; i++)
        {
            int index = i; // capture
            var item = list[i];

            var row = new DockPanel { Margin = new Thickness(0, 1) };

            // Index label
            var indexLabel = new TextBlock
            {
                Text = $"[{index}]",
                FontSize = 11,
                Width = 30,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(indexLabel, global::Avalonia.Controls.Dock.Left);

            // Remove button
            var removeBtn = new Button
            {
                Content = new SymbolIcon { Symbol = Symbol.Delete, FontSize = 12 },
                Padding = new Thickness(4, 2),
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            removeBtn.Click += (_, _) =>
            {
                list.RemoveAt(index);
                Refresh();
                ListChanged?.Invoke(this, EventArgs.Empty);
            };
            DockPanel.SetDock(removeBtn, global::Avalonia.Controls.Dock.Right);

            // Value editor — for primitive types, inline edit; otherwise show ToString
            var editor = CreateItemEditor(list, index, item);

            row.Children.Add(indexLabel);
            row.Children.Add(removeBtn);
            row.Children.Add(editor);

            _itemsPanel.Children.Add(row);
        }
    }

    private Control CreateItemEditor(System.Collections.IList list, int index, object? item)
    {
        if (item is null)
            return new TextBlock { Text = "(null)", FontSize = 11, Foreground = Brushes.Gray };

        var type = item.GetType();

        // String
        if (type == typeof(string))
        {
            var tb = new TextBox
            {
                Text = (string)item,
                FontSize = 11,
                Padding = new Thickness(4, 1),
                MinWidth = 80,
            };
            tb.LostFocus += (_, _) =>
            {
                list[index] = tb.Text;
                ListChanged?.Invoke(this, EventArgs.Empty);
            };
            return tb;
        }

        // Int
        if (type == typeof(int))
        {
            var nb = new NumberBox
            {
                Value = (int)item,
                SimpleNumberFormat = "F0",
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                FontSize = 11,
                MinWidth = 60,
            };
            nb.ValueChanged += (_, _) =>
            {
                list[index] = (int)nb.Value;
                ListChanged?.Invoke(this, EventArgs.Empty);
            };
            return nb;
        }

        // Float
        if (type == typeof(float))
        {
            var nb = new NumberBox
            {
                Value = (float)item,
                SmallChange = 0.1,
                SimpleNumberFormat = "F2",
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                FontSize = 11,
                MinWidth = 60,
            };
            nb.ValueChanged += (_, _) =>
            {
                list[index] = (float)nb.Value;
                ListChanged?.Invoke(this, EventArgs.Empty);
            };
            return nb;
        }

        // Bool
        if (type == typeof(bool))
        {
            var cb = new CheckBox
            {
                IsChecked = (bool)item,
                FontSize = 11,
            };
            cb.IsCheckedChanged += (_, _) =>
            {
                list[index] = cb.IsChecked == true;
                ListChanged?.Invoke(this, EventArgs.Empty);
            };
            return cb;
        }

        // Enum
        if (type.IsEnum)
        {
            var combo = new FAComboBox
            {
                ItemsSource = Enum.GetNames(type),
                SelectedItem = item.ToString(),
                PlaceholderText = "Select...",
                FontSize = 11,
                MinWidth = 80,
            };
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is string s && Enum.TryParse(type, s, out var parsed))
                {
                    list[index] = parsed;
                    ListChanged?.Invoke(this, EventArgs.Empty);
                }
            };
            return combo;
        }

        // Fallback: read-only display
        return new TextBlock
        {
            Text = item.ToString() ?? "(object)",
            FontSize = 11,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
        };
    }

    private void OnAddClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var val = _property.GetValue(_component);
        if (val is not System.Collections.IList list) return;

        var elementType = _property.ElementType ?? typeof(object);
        object? defaultVal = null;

        if (elementType == typeof(string)) defaultVal = "";
        else if (elementType == typeof(int)) defaultVal = 0;
        else if (elementType == typeof(float)) defaultVal = 0f;
        else if (elementType == typeof(double)) defaultVal = 0.0;
        else if (elementType == typeof(bool)) defaultVal = false;
        else if (elementType.IsEnum) defaultVal = Enum.GetValues(elementType).GetValue(0);
        else if (elementType.IsValueType) defaultVal = Activator.CreateInstance(elementType);

        try
        {
            list.Add(defaultVal);
            Refresh();
            ListChanged?.Invoke(this, EventArgs.Empty);
        }
        catch { /* list may be fixed-size or typed; ignore */ }
    }

    private Control BuildHeader()
    {
        var elementName = _property.ElementType?.Name ?? "item";
        return new TextBlock
        {
            Text = $"List<{elementName}>",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(140, 200, 140)),
        };
    }
}
