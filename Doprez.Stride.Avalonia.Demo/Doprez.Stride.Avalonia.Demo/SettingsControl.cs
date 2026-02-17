using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Doprez.Stride.Avalonia.Demo;

/// <summary>
/// Settings panel shown within the escape menu. Contains three sections:
/// <list type="bullet">
///   <item><b>Scene UI</b> – slider to change the number of spawned UI panels.</item>
///   <item><b>Camera Controls</b> – sliders for movement speed, rotation speed, sprint multiplier.</item>
///   <item><b>Window</b> – display mode, resolution, resizing toggle.</item>
/// </list>
/// </summary>
public class SettingsControl : UserControl
{
    // ── Scene UI section ────────────────────────────────────────────
    private readonly Slider _gridSizeSlider;
    private readonly TextBlock _gridSizeLabel;

    // ── Camera section ──────────────────────────────────────────────
    private readonly Slider _moveSpeedSlider;
    private readonly TextBlock _moveSpeedLabel;
    private readonly Slider _rotSpeedSlider;
    private readonly TextBlock _rotSpeedLabel;
    private readonly Slider _sprintMultSlider;
    private readonly TextBlock _sprintMultLabel;
    private readonly Slider _mouseRotSpeedSlider;
    private readonly TextBlock _mouseRotSpeedLabel;

    // ── Window section ──────────────────────────────────────────────
    private readonly ComboBox _windowModeCombo;
    private readonly ComboBox _resolutionCombo;
    private readonly ToggleSwitch _resizableToggle;

    // ── Events ──────────────────────────────────────────────────────
    /// <summary>Raised when the Back button is clicked.</summary>
    public event Action? BackClicked;

    /// <summary>Raised when the grid size slider value changes. The int is the new grid dimension (NxNxN).</summary>
    public event Action<int>? GridSizeChanged;

    /// <summary>Raised when camera movement speed changes.</summary>
    public event Action<float>? MoveSpeedChanged;

    /// <summary>Raised when camera keyboard rotation speed changes.</summary>
    public event Action<float>? RotationSpeedChanged;

    /// <summary>Raised when camera sprint multiplier changes.</summary>
    public event Action<float>? SprintMultiplierChanged;

    /// <summary>Raised when camera mouse rotation speed changes.</summary>
    public event Action<float>? MouseRotationSpeedChanged;

    /// <summary>Raised when the window mode selection changes. Values: "Windowed", "Fullscreen Windowed", "Fullscreen Exclusive".</summary>
    public event Action<string>? WindowModeChanged;

    /// <summary>Raised when the resolution selection changes. Tuple is (width, height).</summary>
    public event Action<int, int>? ResolutionChanged;

    /// <summary>Raised when the resizable toggle changes.</summary>
    public event Action<bool>? ResizableChanged;

    public SettingsControl()
    {
        // ═══════ Scene UI section ═══════════════════════════════════
        _gridSizeLabel = MakeValueLabel("10");
        _gridSizeSlider = new Slider
        {
            Minimum = 1,
            Maximum = 15,
            Value = 10,
            IsSnapToTickEnabled = true,
            TickFrequency = 1,
            MinWidth = 250,
        };
        _gridSizeSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty)
            {
                int val = (int)_gridSizeSlider.Value;
                _gridSizeLabel.Text = val.ToString();
                GridSizeChanged?.Invoke(val);
            }
        };

        var sceneSection = MakeSection("Scene UI", new Control[]
        {
            MakeLabeledRow("Grid Size (NxNxN)", _gridSizeSlider, _gridSizeLabel),
        });

        // ═══════ Camera Controls section ════════════════════════════
        _moveSpeedLabel = MakeValueLabel("5.0");
        _moveSpeedSlider = new Slider
        {
            Minimum = 0.5,
            Maximum = 50,
            Value = 5,
            MinWidth = 250,
        };
        _moveSpeedSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty)
            {
                float val = (float)_moveSpeedSlider.Value;
                _moveSpeedLabel.Text = val.ToString("F1", CultureInfo.InvariantCulture);
                MoveSpeedChanged?.Invoke(val);
            }
        };

        _rotSpeedLabel = MakeValueLabel("3.0");
        _rotSpeedSlider = new Slider
        {
            Minimum = 0.5,
            Maximum = 20,
            Value = 3,
            MinWidth = 250,
        };
        _rotSpeedSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty)
            {
                float val = (float)_rotSpeedSlider.Value;
                _rotSpeedLabel.Text = val.ToString("F1", CultureInfo.InvariantCulture);
                RotationSpeedChanged?.Invoke(val);
            }
        };

        _sprintMultLabel = MakeValueLabel("5.0");
        _sprintMultSlider = new Slider
        {
            Minimum = 1,
            Maximum = 20,
            Value = 5,
            MinWidth = 250,
        };
        _sprintMultSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty)
            {
                float val = (float)_sprintMultSlider.Value;
                _sprintMultLabel.Text = val.ToString("F1", CultureInfo.InvariantCulture);
                SprintMultiplierChanged?.Invoke(val);
            }
        };

        _mouseRotSpeedLabel = MakeValueLabel("1.0");
        _mouseRotSpeedSlider = new Slider
        {
            Minimum = 0.1,
            Maximum = 5,
            Value = 1,
            MinWidth = 250,
        };
        _mouseRotSpeedSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty)
            {
                float val = (float)_mouseRotSpeedSlider.Value;
                _mouseRotSpeedLabel.Text = val.ToString("F1", CultureInfo.InvariantCulture);
                MouseRotationSpeedChanged?.Invoke(val);
            }
        };

        var cameraSection = MakeSection("Camera Controls", new Control[]
        {
            MakeLabeledRow("Movement Speed", _moveSpeedSlider, _moveSpeedLabel),
            MakeLabeledRow("Keyboard Rotation Speed", _rotSpeedSlider, _rotSpeedLabel),
            MakeLabeledRow("Sprint Multiplier", _sprintMultSlider, _sprintMultLabel),
            MakeLabeledRow("Mouse Sensitivity", _mouseRotSpeedSlider, _mouseRotSpeedLabel),
        });

        // ═══════ Window Settings section ════════════════════════════
        _windowModeCombo = new ComboBox
        {
            MinWidth = 220,
            ItemsSource = new[] { "Windowed", "Fullscreen Windowed", "Fullscreen Exclusive" },
            SelectedIndex = 0,
        };
        _windowModeCombo.SelectionChanged += (_, _) =>
        {
            if (_windowModeCombo.SelectedItem is string mode)
                WindowModeChanged?.Invoke(mode);
        };

        _resolutionCombo = new ComboBox
        {
            MinWidth = 220,
            ItemsSource = new[]
            {
                "1280 x 720",
                "1366 x 768",
                "1600 x 900",
                "1920 x 1080",
                "2560 x 1440",
                "3840 x 2160",
            },
            SelectedIndex = 0,
        };
        _resolutionCombo.SelectionChanged += (_, _) =>
        {
            if (_resolutionCombo.SelectedItem is string res)
            {
                var parts = res.Replace(" ", "").Split('x');
                if (parts.Length == 2
                    && int.TryParse(parts[0], out int w)
                    && int.TryParse(parts[1], out int h))
                {
                    ResolutionChanged?.Invoke(w, h);
                }
            }
        };

        _resizableToggle = new ToggleSwitch
        {
            IsChecked = true,
            OnContent = "Enabled",
            OffContent = "Disabled",
        };
        _resizableToggle.IsCheckedChanged += (_, _) =>
        {
            ResizableChanged?.Invoke(_resizableToggle.IsChecked == true);
        };

        var windowSection = MakeSection("Window Settings", new Control[]
        {
            MakeLabeledRow("Display Mode", _windowModeCombo),
            MakeLabeledRow("Resolution", _resolutionCombo),
            MakeLabeledRow("Resizing", _resizableToggle),
        });

        // ═══════ Back button ════════════════════════════════════════
        var backButton = new Button
        {
            Content = "← Back",
            FontSize = 16,
            MinWidth = 100,
            MinHeight = 36,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 16, 0, 0),
        };
        backButton.Click += (_, _) => BackClicked?.Invoke();

        // ═══════ Root layout ════════════════════════════════════════
        Content = new ScrollViewer
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new StackPanel
            {
                Spacing = 20,
                MaxWidth = 600,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(32),
                Children =
                {
                    new TextBlock
                    {
                        Text = "Settings",
                        FontSize = 32,
                        FontWeight = FontWeight.Bold,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 0, 0, 8),
                    },
                    sceneSection,
                    cameraSection,
                    windowSection,
                    backButton,
                },
            },
        };
    }

    // ── Public setters for syncing initial state from game ──────────

    /// <summary>Sets the grid size slider value without raising the event.</summary>
    public void SetGridSize(int size) => _gridSizeSlider.Value = size;

    /// <summary>Sets the movement speed slider value.</summary>
    public void SetMoveSpeed(float speed) => _moveSpeedSlider.Value = speed;

    /// <summary>Sets the rotation speed slider value.</summary>
    public void SetRotationSpeed(float speed) => _rotSpeedSlider.Value = speed;

    /// <summary>Sets the sprint multiplier slider value.</summary>
    public void SetSprintMultiplier(float mult) => _sprintMultSlider.Value = mult;

    /// <summary>Sets the mouse rotation speed slider value.</summary>
    public void SetMouseRotationSpeed(float speed) => _mouseRotSpeedSlider.Value = speed;

    /// <summary>Sets the window mode combo box selection.</summary>
    public void SetWindowMode(string mode) => _windowModeCombo.SelectedItem = mode;

    /// <summary>Sets the resolution combo box to the nearest matching entry.</summary>
    public void SetResolution(int width, int height)
    {
        string target = $"{width} x {height}";
        for (int i = 0; i < (_resolutionCombo.ItemsSource as string[])!.Length; i++)
        {
            if ((_resolutionCombo.ItemsSource as string[])![i] == target)
            {
                _resolutionCombo.SelectedIndex = i;
                return;
            }
        }
    }

    /// <summary>Sets the resizable toggle.</summary>
    public void SetResizable(bool resizable) => _resizableToggle.IsChecked = resizable;

    // ── Helpers ─────────────────────────────────────────────────────

    private static Border MakeSection(string title, Control[] rows)
    {
        var panel = new StackPanel { Spacing = 8 };

        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(180, 200, 255)),
            Margin = new Thickness(0, 0, 0, 4),
        });

        foreach (var row in rows)
            panel.Children.Add(row);

        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 40, 40, 50)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 12),
            Child = panel,
        };
    }

    private static Grid MakeLabeledRow(string label, Control control, TextBlock? valueLabel = null)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("200,*,Auto"),
            Margin = new Thickness(0, 2),
        };

        var lbl = new TextBlock
        {
            Text = label,
            FontSize = 14,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(lbl, 0);
        grid.Children.Add(lbl);

        Grid.SetColumn(control, 1);
        control.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(control);

        if (valueLabel != null)
        {
            Grid.SetColumn(valueLabel, 2);
            valueLabel.Margin = new Thickness(8, 0, 0, 0);
            grid.Children.Add(valueLabel);
        }

        return grid;
    }

    private static TextBlock MakeValueLabel(string initial)
    {
        return new TextBlock
        {
            Text = initial,
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
            MinWidth = 40,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right,
        };
    }
}
