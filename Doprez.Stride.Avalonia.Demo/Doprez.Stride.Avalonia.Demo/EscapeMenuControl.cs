using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Doprez.Stride.Avalonia.Demo;

/// <summary>
/// The main escape menu overlay with Resume, Settings, and Exit buttons.
/// When Settings is clicked, the menu transitions to the <see cref="SettingsControl"/>.
/// </summary>
public class EscapeMenuControl : UserControl
{
    private readonly StackPanel _mainMenu;
    private readonly SettingsControl _settingsPanel;
    private readonly Border _container;

    /// <summary>Raised when the player clicks Resume.</summary>
    public event Action? ResumeClicked;

    /// <summary>Raised when the player clicks Exit.</summary>
    public event Action? ExitClicked;

    /// <summary>
    /// Provides access to the settings panel so the host script can
    /// wire up game-specific callbacks.
    /// </summary>
    public SettingsControl Settings => _settingsPanel;

    public EscapeMenuControl()
    {
        // ── Main menu buttons ───────────────────────────────────────
        var resumeButton = MakeMenuButton("Resume");
        resumeButton.Click += (_, _) => ResumeClicked?.Invoke();

        var settingsButton = MakeMenuButton("Settings");
        settingsButton.Click += (_, _) => ShowSettings();

        var exitButton = MakeMenuButton("Exit");
        exitButton.Click += (_, _) => ExitClicked?.Invoke();

        _mainMenu = new StackPanel
        {
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = "Paused",
                    FontSize = 36,
                    FontWeight = FontWeight.Bold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 24),
                },
                resumeButton,
                settingsButton,
                exitButton,
            },
        };

        // ── Settings panel ──────────────────────────────────────────
        _settingsPanel = new SettingsControl();
        _settingsPanel.IsVisible = false;
        _settingsPanel.BackClicked += ShowMainMenu;

        // ── Root container ──────────────────────────────────────────
        _container = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(200, 15, 15, 15)),
            Child = new Grid
            {
                Children =
                {
                    _mainMenu,
                    _settingsPanel,
                },
            },
        };

        Content = _container;
    }

    private void ShowSettings()
    {
        _mainMenu.IsVisible = false;
        _settingsPanel.IsVisible = true;
    }

    private void ShowMainMenu()
    {
        _settingsPanel.IsVisible = false;
        _mainMenu.IsVisible = true;
    }

    /// <summary>
    /// Resets the menu to show the main menu (not settings) when re-opened.
    /// </summary>
    public void ResetToMainMenu()
    {
        ShowMainMenu();
    }

    private static Button MakeMenuButton(string text)
    {
        return new Button
        {
            Content = text,
            FontSize = 20,
            MinWidth = 220,
            MinHeight = 44,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 2),
        };
    }
}
