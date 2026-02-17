using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using ThemeEditor.Controls;

namespace Stride.Avalonia.Editor.Controls;

/// <summary>
/// Color editor that wraps the ThemeEditor <see cref="ColorPicker"/> control,
/// providing an HSV wheel, hex input, and alpha channel support.
/// <para>
/// Converts between <see cref="global::Avalonia.Media.Color"/> (used by the picker)
/// and Stride's <see cref="Stride.Core.Mathematics.Color4"/> (used by the property system).
/// </para>
/// </summary>
public sealed class ColorEditor : UserControl
{
    private readonly ColorPicker _picker;
    private readonly bool _showAlpha;
    private bool _updating;

    /// <summary>Raised when the colour changes. Event arg is a Stride Color4.</summary>
    public event EventHandler<Stride.Core.Mathematics.Color4>? ValueChanged;

    /// <summary>
    /// Creates a colour editor.
    /// </summary>
    /// <param name="initial">Initial colour as Stride Color4 (0–1 floats).</param>
    /// <param name="showAlpha">Whether to show the alpha channel.</param>
    /// <param name="isFloat">True if the backing type is Color4/Color3 (float), false for Color (byte).</param>
    public ColorEditor(Stride.Core.Mathematics.Color4 initial, bool showAlpha = true, bool isFloat = true)
    {
        _showAlpha = showAlpha;

        _picker = new ColorPicker
        {
            Color = ToAvaloniaColor(initial),
            Width = 200,
            Height = 200,
        };

        _picker.PropertyChanged += OnPickerPropertyChanged;

        Content = _picker;
    }

    public Stride.Core.Mathematics.Color4 Value
    {
        get
        {
            var c = _picker.Color;
            return new Stride.Core.Mathematics.Color4(
                c.R / 255f,
                c.G / 255f,
                c.B / 255f,
                _showAlpha ? c.A / 255f : 1f);
        }
    }

    public Stride.Core.Mathematics.Color ValueAsByteColor
    {
        get
        {
            var c = _picker.Color;
            return new Stride.Core.Mathematics.Color(c.R, c.G, c.B, _showAlpha ? c.A : (byte)255);
        }
    }

    public void SetValue(Stride.Core.Mathematics.Color4 c)
    {
        _updating = true;
        _picker.Color = ToAvaloniaColor(c);
        _updating = false;
    }

    private void OnPickerPropertyChanged(object? sender, global::Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ColorPicker.ColorProperty && !_updating)
        {
            ValueChanged?.Invoke(this, Value);
        }
    }

    private static global::Avalonia.Media.Color ToAvaloniaColor(Stride.Core.Mathematics.Color4 c)
    {
        return global::Avalonia.Media.Color.FromArgb(
            (byte)(c.A * 255),
            (byte)(c.R * 255),
            (byte)(c.G * 255),
            (byte)(c.B * 255));
    }
}
