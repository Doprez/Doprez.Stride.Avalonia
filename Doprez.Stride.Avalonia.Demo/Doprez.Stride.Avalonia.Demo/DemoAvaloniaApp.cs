using Avalonia;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Doprez.Stride.Avalonia.Demo;

/// <summary>
/// Custom Avalonia application with the Fluent theme for the demo project.
/// </summary>
public class DemoAvaloniaApp : Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
        base.OnFrameworkInitializationCompleted();
    }
}
