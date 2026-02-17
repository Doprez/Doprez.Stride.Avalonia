using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Doprez.Stride.Avalonia.Demo;

/// <summary>
/// A minimal Avalonia control that displays a single counter label.
/// </summary>
public class CounterLabel : UserControl
{
    private readonly TextBlock _label;

    public CounterLabel()
    {
        _label = new TextBlock
        {
            Text = "0",
            FontSize = 24,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(180, 30, 30, 30)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4),
            Child = _label,
        };
    }

    /// <summary>
    /// Updates the displayed count value.
    /// </summary>
    public void SetCount(int count)
    {
        _label.Text = count.ToString();
    }
}
