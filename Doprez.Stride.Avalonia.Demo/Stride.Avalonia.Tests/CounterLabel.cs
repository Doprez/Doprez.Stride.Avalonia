using global::Avalonia.Controls;
using global::Avalonia.Layout;
using global::Avalonia.Media;

namespace Stride.Avalonia.Tests;

/// <summary>
/// A minimal Avalonia control that displays a counter number.
/// Used by the stress test to create 1000 world-space UI panels.
/// </summary>
public class CounterLabel : UserControl
{
    public CounterLabel(int number)
    {
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(200, 30, 30, 40)),
            CornerRadius = new global::Avalonia.CornerRadius(6),
            Padding = new global::Avalonia.Thickness(12, 8),
            Child = new TextBlock
            {
                Text = $"#{number}",
                FontSize = 20,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 200, 255)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }
}
