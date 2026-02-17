using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using Stride.Engine;
using Stride.Avalonia.Editor.Controls.Serialization;

namespace Stride.Avalonia.Editor.Controls;

/// <summary>
/// Displays asset reference information (Prefab, UrlReference, etc.) with a type label.
/// Asset references in Stride typically require Content Manager resolution,
/// so this control shows the current value and type for awareness.
/// Full asset browsing / drag-drop will be added when an asset database is available.
/// </summary>
public sealed class AssetReferencePicker : UserControl
{
    private readonly EditableProperty _property;
    private readonly EntityComponent _component;
    private readonly TextBox _pathBox;
    private readonly TextBlock _typeLabel;

    public AssetReferencePicker(EditableProperty property, EntityComponent component)
    {
        _property = property;
        _component = component;

        var val = property.GetValue(component);

        _typeLabel = new TextBlock
        {
            Text = property.ClrType.Name,
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(180, 160, 100)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };

        _pathBox = new TextBox
        {
            Text = FormatAssetValue(val),
            FontSize = 11,
            IsReadOnly = true,
            Padding = new Thickness(4, 1),
            MinWidth = 120,
            Foreground = new SolidColorBrush(Color.FromRgb(180, 160, 100)),
            Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
        };

        Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children = { _typeLabel, _pathBox },
        };
    }

    public void Refresh()
    {
        var val = _property.GetValue(_component);
        _pathBox.Text = FormatAssetValue(val);
    }

    private static string FormatAssetValue(object? val)
    {
        if (val is null) return "(none)";

        // Stride Prefab
        if (val is Stride.Engine.Prefab prefab)
        {
            return prefab.Entities?.Count > 0
                ? $"Prefab ({prefab.Entities.Count} entities)"
                : "Prefab (empty)";
        }

        // Try to extract Url from UrlReference<T> via reflection
        var urlProp = val.GetType().GetProperty("Url");
        if (urlProp != null)
        {
            var url = urlProp.GetValue(val) as string;
            return !string.IsNullOrEmpty(url) ? url : "(unset)";
        }

        return val.ToString() ?? "(unknown)";
    }
}
