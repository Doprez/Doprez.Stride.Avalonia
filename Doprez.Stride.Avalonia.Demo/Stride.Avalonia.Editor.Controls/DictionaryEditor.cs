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
/// Inline editor for <see cref="IDictionary{TKey,TValue}"/> properties.
/// Uses an <see cref="Expander"/> with Add/Remove buttons and a two-column layout
/// for key-value pairs with inline editing of primitive types.
/// </summary>
public sealed class DictionaryEditor : UserControl
{
    private readonly EditableProperty _property;
    private readonly EntityComponent _component;
    private readonly StackPanel _itemsPanel;
    private readonly TextBlock _countLabel;
    private readonly Type? _keyType;
    private readonly Type? _valueType;

    /// <summary>Raised when the dictionary is mutated.</summary>
    public event EventHandler? DictionaryChanged;

    public DictionaryEditor(EditableProperty property, EntityComponent component)
    {
        _property = property;
        _component = component;

        // Try to extract TKey, TValue from the dictionary type
        var dictInterface = property.ClrType.GetInterfaces()
            .Concat(new[] { property.ClrType })
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>));
        if (dictInterface != null)
        {
            var args = dictInterface.GetGenericArguments();
            _keyType = args[0];
            _valueType = args[1];
        }

        _countLabel = new TextBlock
        {
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(200, 180, 140)),
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

        // Column headers
        var headers = new DockPanel
        {
            Margin = new Thickness(0, 2, 0, 0),
        };
        var keyHeader = new TextBlock
        {
            Text = "Key",
            FontSize = 10,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140)),
            Width = 80,
        };
        DockPanel.SetDock(keyHeader, global::Avalonia.Controls.Dock.Left);
        var valHeader = new TextBlock
        {
            Text = "Value",
            FontSize = 10,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140)),
        };
        headers.Children.Add(keyHeader);
        headers.Children.Add(valHeader);

        var expander = new Expander
        {
            Header = BuildHeader(),
            IsExpanded = false,
            Content = new StackPanel
            {
                Spacing = 2,
                Children = { toolbar, headers, _itemsPanel },
            },
        };

        Content = expander;
        Refresh();
    }

    public void Refresh()
    {
        _itemsPanel.Children.Clear();

        var val = _property.GetValue(_component);
        if (val is not System.Collections.IDictionary dict)
        {
            _countLabel.Text = "(not a dictionary)";
            return;
        }

        _countLabel.Text = $"{dict.Count} entries";

        foreach (System.Collections.DictionaryEntry entry in dict)
        {
            var key = entry.Key;
            var value = entry.Value;

            var row = new DockPanel { Margin = new Thickness(0, 1) };

            // Remove button (docked right)
            var removeBtn = new Button
            {
                Content = new SymbolIcon { Symbol = Symbol.Delete, FontSize = 12 },
                Padding = new Thickness(4, 2),
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var capturedKey = key;
            removeBtn.Click += (_, _) =>
            {
                dict.Remove(capturedKey);
                Refresh();
                DictionaryChanged?.Invoke(this, EventArgs.Empty);
            };
            DockPanel.SetDock(removeBtn, global::Avalonia.Controls.Dock.Right);

            // Key display (read-only for safety — editing keys is risky)
            var keyLabel = new TextBlock
            {
                Text = key?.ToString() ?? "(null)",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(180, 200, 255)),
                Width = 80,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(keyLabel, global::Avalonia.Controls.Dock.Left);

            // Value editor
            var editor = CreateValueEditor(dict, key, value);

            row.Children.Add(removeBtn);
            row.Children.Add(keyLabel);
            row.Children.Add(editor);

            _itemsPanel.Children.Add(row);
        }
    }

    private Control CreateValueEditor(System.Collections.IDictionary dict, object key, object? value)
    {
        if (value is null)
            return new TextBlock { Text = "(null)", FontSize = 11, Foreground = Brushes.Gray };

        var type = value.GetType();

        if (type == typeof(string))
        {
            var tb = new TextBox
            {
                Text = (string)value,
                FontSize = 11,
                Padding = new Thickness(4, 1),
                MinWidth = 80,
            };
            tb.LostFocus += (_, _) =>
            {
                dict[key] = tb.Text;
                DictionaryChanged?.Invoke(this, EventArgs.Empty);
            };
            return tb;
        }

        if (type == typeof(int))
        {
            var nb = new NumberBox
            {
                Value = (int)value,
                SimpleNumberFormat = "F0",
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                FontSize = 11,
                MinWidth = 60,
            };
            nb.ValueChanged += (_, _) =>
            {
                dict[key] = (int)nb.Value;
                DictionaryChanged?.Invoke(this, EventArgs.Empty);
            };
            return nb;
        }

        if (type == typeof(float))
        {
            var nb = new NumberBox
            {
                Value = (float)value,
                SmallChange = 0.1,
                SimpleNumberFormat = "F2",
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                FontSize = 11,
                MinWidth = 60,
            };
            nb.ValueChanged += (_, _) =>
            {
                dict[key] = (float)nb.Value;
                DictionaryChanged?.Invoke(this, EventArgs.Empty);
            };
            return nb;
        }

        if (type == typeof(bool))
        {
            var cb = new CheckBox
            {
                IsChecked = (bool)value,
                FontSize = 11,
            };
            cb.IsCheckedChanged += (_, _) =>
            {
                dict[key] = cb.IsChecked == true;
                DictionaryChanged?.Invoke(this, EventArgs.Empty);
            };
            return cb;
        }

        return new TextBlock
        {
            Text = value.ToString() ?? "(object)",
            FontSize = 11,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
        };
    }

    private void OnAddClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var val = _property.GetValue(_component);
        if (val is not System.Collections.IDictionary dict) return;

        var kt = _keyType ?? typeof(string);
        var vt = _valueType ?? typeof(object);

        object? newKey = CreateDefault(kt);
        object? newValue = CreateDefault(vt);

        // Ensure key is unique for string keys
        if (kt == typeof(string))
        {
            var baseKey = "NewKey";
            int suffix = 0;
            while (dict.Contains(baseKey + (suffix == 0 ? "" : $"_{suffix}")))
                suffix++;
            newKey = baseKey + (suffix == 0 ? "" : $"_{suffix}");
        }

        try
        {
            if (newKey != null)
            {
                dict[newKey] = newValue;
                Refresh();
                DictionaryChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch { /* typed dict may reject; ignore */ }
    }

    private static object? CreateDefault(Type t)
    {
        if (t == typeof(string)) return "";
        if (t == typeof(int)) return 0;
        if (t == typeof(float)) return 0f;
        if (t == typeof(double)) return 0.0;
        if (t == typeof(bool)) return false;
        if (t.IsEnum) return Enum.GetValues(t).GetValue(0);
        if (t.IsValueType) return Activator.CreateInstance(t);
        return null;
    }

    private Control BuildHeader()
    {
        var keyName = _keyType?.Name ?? "?";
        var valName = _valueType?.Name ?? "?";
        return new TextBlock
        {
            Text = $"Dictionary<{keyName}, {valName}>",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(200, 180, 140)),
        };
    }
}
