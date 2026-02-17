using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using FluentAvalonia.UI.Controls;

namespace Stride.Avalonia.Editor.Controls;

/// <summary>
/// Inline editor for Stride <see cref="Stride.Core.Mathematics.Quaternion"/>
/// displayed as Euler angles (degrees) with three colour-coded <see cref="NumberBox"/> fields.
/// </summary>
public sealed class QuaternionEditor : UserControl
{
    private readonly NumberBox _pitch, _yaw, _roll;
    private bool _updating;

    public event EventHandler<Stride.Core.Mathematics.Quaternion>? ValueChanged;

    public QuaternionEditor(Stride.Core.Mathematics.Quaternion initial)
    {
        Stride.Core.Mathematics.Matrix.RotationQuaternion(ref initial, out var matrix);
        matrix.Decompose(out _, out Stride.Core.Mathematics.Quaternion rotation, out _);
        var euler = QuaternionToEulerDeg(initial);

        _pitch = MakeField(euler.X);
        _yaw   = MakeField(euler.Y);
        _roll  = MakeField(euler.Z);

        Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            Children =
            {
                Label("P", Colors.IndianRed), _pitch,
                Label("Y", Colors.MediumSeaGreen), _yaw,
                Label("R", Colors.CornflowerBlue), _roll,
            },
        };
    }

    public Stride.Core.Mathematics.Quaternion Value
    {
        get
        {
            var pitch = Stride.Core.Mathematics.MathUtil.DegreesToRadians((float)_pitch.Value);
            var yaw   = Stride.Core.Mathematics.MathUtil.DegreesToRadians((float)_yaw.Value);
            var roll  = Stride.Core.Mathematics.MathUtil.DegreesToRadians((float)_roll.Value);
            Stride.Core.Mathematics.Quaternion.RotationYawPitchRoll(yaw, pitch, roll, out var q);
            return q;
        }
    }

    public void SetValue(Stride.Core.Mathematics.Quaternion q)
    {
        _updating = true;
        var euler = QuaternionToEulerDeg(q);
        _pitch.Value = euler.X;
        _yaw.Value   = euler.Y;
        _roll.Value  = euler.Z;
        _updating = false;
    }

    private static Stride.Core.Mathematics.Vector3 QuaternionToEulerDeg(Stride.Core.Mathematics.Quaternion q)
    {
        // Convert quaternion → rotation matrix → extract Euler angles
        var mat = Stride.Core.Mathematics.Matrix.RotationQuaternion(q);
        float pitch, yaw, roll;

        // Pitch (X)
        float sp = -mat.M32;
        if (sp >= 1f) pitch = Stride.Core.Mathematics.MathUtil.PiOverTwo;
        else if (sp <= -1f) pitch = -Stride.Core.Mathematics.MathUtil.PiOverTwo;
        else pitch = MathF.Asin(sp);

        // Yaw (Y) and Roll (Z)
        if (MathF.Abs(sp) > 0.9999f)
        {
            yaw = MathF.Atan2(-mat.M13, mat.M11);
            roll = 0f;
        }
        else
        {
            yaw  = MathF.Atan2(mat.M31, mat.M33);
            roll = MathF.Atan2(mat.M12, mat.M22);
        }

        return new Stride.Core.Mathematics.Vector3(
            Stride.Core.Mathematics.MathUtil.RadiansToDegrees(pitch),
            Stride.Core.Mathematics.MathUtil.RadiansToDegrees(yaw),
            Stride.Core.Mathematics.MathUtil.RadiansToDegrees(roll));
    }

    private NumberBox MakeField(double initial)
    {
        var nb = new NumberBox
        {
            Value = initial,
            Minimum = -360,
            Maximum = 360,
            SmallChange = 1,
            LargeChange = 10,
            SimpleNumberFormat = "F1",
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            FontSize = 11,
            MinWidth = 60,
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
