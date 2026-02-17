using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using FluentAvalonia.UI.Controls;

namespace Stride.Avalonia.Editor.Controls;

/// <summary>
/// Inline editor for a Stride <see cref="Stride.Core.Mathematics.Vector2"/>.
/// Renders two labelled <see cref="NumberBox"/> controls in a horizontal row.
/// </summary>
public sealed class Vector2Editor : UserControl
{
    private readonly NumberBox _x;
    private readonly NumberBox _y;
    private bool _updating;

    /// <summary>Raised when either component changes.</summary>
    public event EventHandler<Stride.Core.Mathematics.Vector2>? ValueChanged;

    public Vector2Editor(
        Stride.Core.Mathematics.Vector2 initial,
        double? min = null, double? max = null,
        double? smallStep = null, int? decimalPlaces = null)
    {
        _x = MakeField(initial.X, min, max, smallStep, decimalPlaces);
        _y = MakeField(initial.Y, min, max, smallStep, decimalPlaces);

        Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children =
            {
                Label("X", Colors.IndianRed), _x,
                Label("Y", Colors.MediumSeaGreen), _y,
            },
        };
    }

    public Stride.Core.Mathematics.Vector2 Value => new(
        (float)_x.Value,
        (float)_y.Value);

    public void SetValue(Stride.Core.Mathematics.Vector2 v)
    {
        _updating = true;
        _x.Value = v.X;
        _y.Value = v.Y;
        _updating = false;
    }

    private NumberBox MakeField(double value,
        double? min, double? max, double? smallStep, int? decimalPlaces)
    {
        var nb = new NumberBox
        {
            Value = value,
            Minimum = min ?? double.MinValue,
            Maximum = max ?? double.MaxValue,
            SmallChange = smallStep ?? 0.1,
            LargeChange = (smallStep ?? 0.1) * 10,
            SimpleNumberFormat = decimalPlaces.HasValue ? $"F{decimalPlaces.Value}" : "F2",
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            Width = 80,
            FontSize = 11,
        };
        nb.ValueChanged += (_, _) =>
        {
            if (!_updating) ValueChanged?.Invoke(this, Value);
        };
        return nb;
    }

    private static TextBlock Label(string text, Color color) => new()
    {
        Text = text,
        FontSize = 11,
        FontWeight = FontWeight.SemiBold,
        Foreground = new SolidColorBrush(color),
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(2, 0, 0, 0),
    };
}
