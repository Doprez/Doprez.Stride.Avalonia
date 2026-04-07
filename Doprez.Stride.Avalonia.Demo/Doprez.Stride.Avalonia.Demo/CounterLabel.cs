using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System;

namespace Doprez.Stride.Avalonia.Demo;

/// <summary>
/// A minimal Avalonia control that displays a counter label and a progress bar
/// that fills over 10 seconds and resets at 100%.
/// </summary>
public class CounterLabel : UserControl
{
    private readonly TextBlock _label;
    private readonly ProgressBar _progressBar;
    private DispatcherTimer? _timer;

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

        _progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Height = 8,
            Foreground = new SolidColorBrush(Color.FromRgb(0, 180, 80)),
            Background = new SolidColorBrush(Color.FromArgb(100, 60, 60, 60)),
        };

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(128, 30, 30, 30)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4),
            Child = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = 4,
                Children =
                {
                    _label,
                    _progressBar,
                },
            },
        };

        #warning should make a better update example. This causes REAL bad stutter when you have many instances due to all updating in the same frame.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        var next = _progressBar.Value + 10;
        _progressBar.Value = next >= 100 ? 0 : next;
    }

    /// <summary>
    /// Updates the displayed count value.
    /// </summary>
    public void SetCount(int count)
    {
        _label.Text = count.ToString();
    }

    /// <summary>
    /// Stops the timer to prevent leaks when the control is removed.
    /// </summary>
    public void StopTimer()
    {
        _timer?.Stop();
        _timer = null;
    }
}
