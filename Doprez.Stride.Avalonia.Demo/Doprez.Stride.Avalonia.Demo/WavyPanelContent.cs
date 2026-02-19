using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Doprez.Stride.Avalonia.Demo;

/// <summary>
/// A stylish Avalonia control designed to demonstrate the wavy SDSL effect.
/// Contains a title, descriptive text, a slider, and a button to show that
/// interactive UI works even on a custom-effect panel.
/// </summary>
public class WavyPanelContent : UserControl
{
    private readonly TextBlock _timeLabel;

    public WavyPanelContent()
    {
        _timeLabel = new TextBlock
        {
            Text = "Effect Time: 0.0s",
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(180, 220, 255)),
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var title = new TextBlock
        {
            Text = "\u2728 Wavy Panel Demo",
            FontSize = 22,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
        };

        var description = new TextBlock
        {
            Text = "This panel is rendered with a custom\nSDSL shader that displaces vertices\nusing a sine wave in real-time.",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 210)),
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12),
        };

        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = 50,
            Width = 200,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
        };

        var button = new Button
        {
            Content = "Click Me!",
            HorizontalAlignment = HorizontalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(20, 6),
            Margin = new Thickness(0, 0, 0, 8),
        };
        button.Click += (_, _) =>
        {
            button.Content = button.Content?.ToString() == "Click Me!"
                ? "\u2714 Clicked!"
                : "Click Me!";
        };

        Content = new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(200, 20, 30, 60), 0),
                    new GradientStop(Color.FromArgb(200, 40, 20, 80), 0.5),
                    new GradientStop(Color.FromArgb(200, 20, 50, 70), 1),
                },
            },
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(24, 16),
            BorderBrush = new SolidColorBrush(Color.FromArgb(120, 100, 140, 255)),
            BorderThickness = new Thickness(2),
            Child = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    title,
                    description,
                    slider,
                    button,
                    _timeLabel,
                },
            },
        };
    }

    /// <summary>
    /// Updates the displayed effect time. Called from the demo script.
    /// </summary>
    public void UpdateTime(float seconds)
    {
        _timeLabel.Text = $"Effect Time: {seconds:F1}s";
    }
}
