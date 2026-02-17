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
/// Creates the appropriate Avalonia control for a given <see cref="EditableProperty"/>
/// and wires up two-way data flow between the control and the component instance.
/// </summary>
public static class PropertyControlFactory
{
    /// <summary>
    /// Build the complete property row (label + control) for one <see cref="EditableProperty"/>
    /// on the given <paramref name="component"/>.
    /// </summary>
    public static Control CreatePropertyRow(
        EditableProperty property,
        EntityComponent component)
    {
        var label = new TextBlock
        {
            Text = property.DisplayName,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
            Width = 120,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (property.Description is not null)
            ToolTip.SetTip(label, property.Description);

        var editor = CreateEditor(property, component);

        var row = new DockPanel
        {
            Margin = new Thickness(16, 2, 8, 2),
        };
        DockPanel.SetDock(label, global::Avalonia.Controls.Dock.Left);
        row.Children.Add(label);
        row.Children.Add(editor);

        return row;
    }

    /// <summary>
    /// Creates just the editor control (no label) for a property.
    /// </summary>
    public static Control CreateEditor(
        EditableProperty property,
        EntityComponent component)
    {
        if (property.IsReadOnly)
            return ReadOnlyLabel(property.GetValue(component));

        return property.PropertyType switch
        {
            EditablePropertyType.String => CreateStringEditor(property, component),
            EditablePropertyType.Int => CreateIntEditor(property, component),
            EditablePropertyType.Float => CreateFloatEditor(property, component),
            EditablePropertyType.Double => CreateDoubleEditor(property, component),
            EditablePropertyType.Bool => CreateBoolEditor(property, component),
            EditablePropertyType.Vector2 => CreateVector2Editor(property, component),
            EditablePropertyType.Vector3 => CreateVector3Editor(property, component),
            EditablePropertyType.Vector4 => CreateVector4Editor(property, component),
            EditablePropertyType.Quaternion => CreateQuaternionEditor(property, component),
            EditablePropertyType.Color => CreateColorByteEditor(property, component),
            EditablePropertyType.Color3 => CreateColor3Editor(property, component),
            EditablePropertyType.Color4 => CreateColor4Editor(property, component),
            EditablePropertyType.Enum => CreateEnumEditor(property, component),
            EditablePropertyType.ComponentReference => CreateComponentRefPicker(property, component),
            EditablePropertyType.EntityReference => CreateEntityRefPicker(property, component),
            EditablePropertyType.Prefab => CreateAssetRefPicker(property, component),
            EditablePropertyType.AssetReference => CreateAssetRefPicker(property, component),
            EditablePropertyType.List => CreateListEditor(property, component),
            EditablePropertyType.Dictionary => CreateDictionaryEditor(property, component),
            _ => ReadOnlyLabel(property.GetValue(component)),
        };
    }

    // ══════════════════════════════════════════════════════
    //  String
    // ══════════════════════════════════════════════════════

    private static Control CreateStringEditor(EditableProperty property, EntityComponent component)
    {
        var tb = new TextBox
        {
            Text = property.GetValue(component) as string ?? string.Empty,
            FontSize = 12,
            Padding = new Thickness(4, 2),
            MinWidth = 100,
        };
        tb.LostFocus += (_, _) =>
        {
            var old = property.GetValue(component);
            property.SetValue(component, tb.Text);
            NotifyChanged(component, property, old, tb.Text);
        };
        return tb;
    }

    // ══════════════════════════════════════════════════════
    //  Numeric (int / float / double)
    // ══════════════════════════════════════════════════════

    private static Control CreateIntEditor(EditableProperty property, EntityComponent component)
    {
        var val = property.GetValue(component);
        var nb = new NumberBox
        {
            Value = val is int i ? i : 0,
            Minimum = property.Minimum ?? double.MinValue,
            Maximum = property.Maximum ?? double.MaxValue,
            SmallChange = property.SmallStep ?? 1,
            LargeChange = (property.SmallStep ?? 1) * 10,
            SimpleNumberFormat = "F0",
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            FontSize = 12,
            MinWidth = 80,
        };
        nb.ValueChanged += (_, _) =>
        {
            var old = property.GetValue(component);
            property.SetValue(component, (int)nb.Value);
            NotifyChanged(component, property, old, (int)nb.Value);
        };
        return nb;
    }

    private static Control CreateFloatEditor(EditableProperty property, EntityComponent component)
    {
        var val = property.GetValue(component);
        var nb = new NumberBox
        {
            Value = val is float f ? f : 0,
            Minimum = property.Minimum ?? double.MinValue,
            Maximum = property.Maximum ?? double.MaxValue,
            SmallChange = property.SmallStep ?? 0.1,
            LargeChange = (property.SmallStep ?? 0.1) * 10,
            SimpleNumberFormat = property.DecimalPlaces.HasValue ? $"F{property.DecimalPlaces.Value}" : "F2",
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            FontSize = 12,
            MinWidth = 80,
        };
        nb.ValueChanged += (_, _) =>
        {
            var old = property.GetValue(component);
            property.SetValue(component, (float)nb.Value);
            NotifyChanged(component, property, old, (float)nb.Value);
        };
        return nb;
    }

    private static Control CreateDoubleEditor(EditableProperty property, EntityComponent component)
    {
        var val = property.GetValue(component);
        var nb = new NumberBox
        {
            Value = val is double d ? d : 0,
            Minimum = property.Minimum ?? double.MinValue,
            Maximum = property.Maximum ?? double.MaxValue,
            SmallChange = property.SmallStep ?? 0.1,
            LargeChange = (property.SmallStep ?? 0.1) * 10,
            SimpleNumberFormat = property.DecimalPlaces.HasValue ? $"F{property.DecimalPlaces.Value}" : "F3",
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            FontSize = 12,
            MinWidth = 80,
        };
        nb.ValueChanged += (_, _) =>
        {
            var old = property.GetValue(component);
            property.SetValue(component, nb.Value);
            NotifyChanged(component, property, old, nb.Value);
        };
        return nb;
    }

    // ══════════════════════════════════════════════════════
    //  Bool
    // ══════════════════════════════════════════════════════

    private static Control CreateBoolEditor(EditableProperty property, EntityComponent component)
    {
        var val = property.GetValue(component) is true;
        var toggle = new ToggleSwitch
        {
            IsChecked = val,
            OnContent = "On",
            OffContent = "Off",
            FontSize = 11,
        };
        toggle.IsCheckedChanged += (_, _) =>
        {
            var old = property.GetValue(component);
            property.SetValue(component, toggle.IsChecked == true);
            NotifyChanged(component, property, old, toggle.IsChecked == true);
        };
        return toggle;
    }

    // ══════════════════════════════════════════════════════
    //  Vector2 / Vector3
    // ══════════════════════════════════════════════════════

    private static Control CreateVector2Editor(EditableProperty property, EntityComponent component)
    {
        var val = property.GetValue(component) is Stride.Core.Mathematics.Vector2 v
            ? v
            : default;

        var editor = new Vector2Editor(val,
            property.Minimum, property.Maximum,
            property.SmallStep, property.DecimalPlaces);

        editor.ValueChanged += (_, newVal) =>
        {
            var old = property.GetValue(component);
            property.SetValue(component, newVal);
            NotifyChanged(component, property, old, newVal);
        };
        return editor;
    }

    private static Control CreateVector3Editor(EditableProperty property, EntityComponent component)
    {
        var val = property.GetValue(component) is Stride.Core.Mathematics.Vector3 v
            ? v
            : default;

        var editor = new Vector3Editor(val,
            property.Minimum, property.Maximum,
            property.SmallStep, property.DecimalPlaces);

        editor.ValueChanged += (_, newVal) =>
        {
            var old = property.GetValue(component);
            property.SetValue(component, newVal);
            NotifyChanged(component, property, old, newVal);
        };
        return editor;
    }

    // ══════════════════════════════════════════════════════
    //  Color / Color3 / Color4
    // ══════════════════════════════════════════════════════

    private static Control CreateColorByteEditor(EditableProperty property, EntityComponent component)
    {
        var val = property.GetValue(component) is Stride.Core.Mathematics.Color c
            ? c.ToColor4()
            : new Stride.Core.Mathematics.Color4(1, 1, 1, 1);

        var editor = new ColorEditor(val, showAlpha: true, isFloat: false);
        editor.ValueChanged += (_, newVal) =>
        {
            var old = property.GetValue(component);
            var byteColor = new Stride.Core.Mathematics.Color(
                (byte)(newVal.R * 255), (byte)(newVal.G * 255),
                (byte)(newVal.B * 255), (byte)(newVal.A * 255));
            property.SetValue(component, byteColor);
            NotifyChanged(component, property, old, byteColor);
        };
        return editor;
    }

    private static Control CreateColor3Editor(EditableProperty property, EntityComponent component)
    {
        var val = property.GetValue(component) is Stride.Core.Mathematics.Color3 c3
            ? new Stride.Core.Mathematics.Color4(c3.R, c3.G, c3.B, 1f)
            : new Stride.Core.Mathematics.Color4(1, 1, 1, 1);

        var editor = new ColorEditor(val, showAlpha: false, isFloat: true);
        editor.ValueChanged += (_, newVal) =>
        {
            var old = property.GetValue(component);
            var color3 = new Stride.Core.Mathematics.Color3(newVal.R, newVal.G, newVal.B);
            property.SetValue(component, color3);
            NotifyChanged(component, property, old, color3);
        };
        return editor;
    }

    private static Control CreateColor4Editor(EditableProperty property, EntityComponent component)
    {
        var val = property.GetValue(component) is Stride.Core.Mathematics.Color4 c4
            ? c4
            : new Stride.Core.Mathematics.Color4(1, 1, 1, 1);

        var editor = new ColorEditor(val, showAlpha: true, isFloat: true);
        editor.ValueChanged += (_, newVal) =>
        {
            var old = property.GetValue(component);
            property.SetValue(component, newVal);
            NotifyChanged(component, property, old, newVal);
        };
        return editor;
    }

    // ══════════════════════════════════════════════════════
    //  Enum
    // ══════════════════════════════════════════════════════

    private static Control CreateEnumEditor(EditableProperty property, EntityComponent component)
    {
        var val = property.GetValue(component);
        var names = property.EnumValues ?? Enum.GetNames(property.ClrType);

        var combo = new FAComboBox
        {
            ItemsSource = names,
            SelectedItem = val?.ToString(),
            PlaceholderText = "Select...",
            FontSize = 12,
            MinWidth = 100,
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is string selected &&
                Enum.TryParse(property.ClrType, selected, out var parsed))
            {
                var old = property.GetValue(component);
                property.SetValue(component, parsed);
                NotifyChanged(component, property, old, parsed);
            }
        };
        return combo;
    }

    // ══════════════════════════════════════════════════════
    //  Vector4 / Quaternion
    // ══════════════════════════════════════════════════════

    private static Control CreateVector4Editor(EditableProperty property, EntityComponent component)
    {
        var val = property.GetValue(component) is Stride.Core.Mathematics.Vector4 v
            ? v
            : default;

        var editor = new Vector4Editor(val,
            (float?)property.Minimum, (float?)property.Maximum,
            (float?)property.SmallStep, property.DecimalPlaces);

        editor.ValueChanged += (_, newVal) =>
        {
            var old = property.GetValue(component);
            property.SetValue(component, newVal);
            NotifyChanged(component, property, old, newVal);
        };
        return editor;
    }

    private static Control CreateQuaternionEditor(EditableProperty property, EntityComponent component)
    {
        var val = property.GetValue(component) is Stride.Core.Mathematics.Quaternion q
            ? q
            : Stride.Core.Mathematics.Quaternion.Identity;

        var editor = new QuaternionEditor(val);

        editor.ValueChanged += (_, newVal) =>
        {
            var old = property.GetValue(component);
            property.SetValue(component, newVal);
            NotifyChanged(component, property, old, newVal);
        };
        return editor;
    }

    // ══════════════════════════════════════════════════════
    //  References (filterable pickers)
    // ══════════════════════════════════════════════════════

    private static Control CreateEntityRefPicker(EditableProperty property, EntityComponent component)
    {
        var picker = new EntityReferencePicker(property, component);
        picker.ValueChanged += (_, _) => { }; // side-effects handled inside picker
        return picker;
    }

    private static Control CreateComponentRefPicker(EditableProperty property, EntityComponent component)
    {
        var picker = new ComponentReferencePicker(property, component);
        picker.ValueChanged += (_, _) => { };
        return picker;
    }

    private static Control CreateAssetRefPicker(EditableProperty property, EntityComponent component)
    {
        return new AssetReferencePicker(property, component);
    }

    // ══════════════════════════════════════════════════════
    //  Collections (editable list / dictionary)
    // ══════════════════════════════════════════════════════

    private static Control CreateListEditor(EditableProperty property, EntityComponent component)
    {
        var editor = new ListEditor(property, component);
        editor.ListChanged += (_, _) =>
        {
            NotifyChanged(component, property, null, property.GetValue(component));
        };
        return editor;
    }

    private static Control CreateDictionaryEditor(EditableProperty property, EntityComponent component)
    {
        var editor = new DictionaryEditor(property, component);
        editor.DictionaryChanged += (_, _) =>
        {
            NotifyChanged(component, property, null, property.GetValue(component));
        };
        return editor;
    }

    // ══════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════

    private static Control ReadOnlyLabel(object? value)
    {
        return new TextBlock
        {
            Text = value?.ToString() ?? "(null)",
            FontSize = 12,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    /// <summary>
    /// If the component implements <see cref="IEditableComponent"/>, notify it
    /// that a property value changed so it can validate / react.
    /// </summary>
    private static void NotifyChanged(
        EntityComponent component,
        EditableProperty property,
        object? oldValue,
        object? newValue)
    {
        if (component is IEditableComponent editable)
        {
            if (!editable.OnPropertyChanged(property, oldValue, newValue))
            {
                // Revert
                property.SetValue(component, oldValue);
            }
        }
    }
}
