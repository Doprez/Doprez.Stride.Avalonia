using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using FluentAvalonia.UI.Controls;

namespace Stride.Avalonia.Editor.Controls;

/// <summary>
/// Inline editor for Stride <see cref="Stride.Core.Mathematics.Vector4"/>
/// showing four colour-coded <see cref="NumberBox"/> fields (X, Y, Z, W).
/// </summary>
public sealed class Vector4Editor : UserControl
{
    private readonly NumberBox _x, _y, _z, _w;
    private bool _updating;

    public event EventHandler<Stride.Core.Mathematics.Vector4>? ValueChanged;

    public Vector4Editor(
        Stride.Core.Mathematics.Vector4 initial,
        float? min = null, float? max = null,
        float? step = null, int? decimals = null)
    {
        _x = MakeField(initial.X, min, max, step, decimals);
        _y = MakeField(initial.Y, min, max, step, decimals);
        _z = MakeField(initial.Z, min, max, step, decimals);
        _w = MakeField(initial.W, min, max, step, decimals);

        Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            Children =
            {
                Label("X", Colors.IndianRed), _x,
                Label("Y", Colors.MediumSeaGreen), _y,
                Label("Z", Colors.CornflowerBlue), _z,
                Label("W", Colors.Orchid), _w,
            },
        };
    }

    public Stride.Core.Mathematics.Vector4 Value => new(
        (float)_x.Value,
        (float)_y.Value,
        (float)_z.Value,
        (float)_w.Value);

    public void SetValue(Stride.Core.Mathematics.Vector4 v)
    {
        _updating = true;
        _x.Value = v.X;
        _y.Value = v.Y;
        _z.Value = v.Z;
        _w.Value = v.W;
        _updating = false;
    }

    private NumberBox MakeField(double initial, float? min, float? max, float? step, int? decimals)
    {
        var nb = new NumberBox
        {
            Value = initial,
            Minimum = min ?? double.MinValue,
            Maximum = max ?? double.MaxValue,
            SmallChange = step ?? 0.1,
            LargeChange = (step ?? 0.1) * 10,
            SimpleNumberFormat = decimals.HasValue ? $"F{decimals.Value}" : "F2",
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            FontSize = 11,
            MinWidth = 55,
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
        Width = 14,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(2, 0, 0, 0),
    };
}
