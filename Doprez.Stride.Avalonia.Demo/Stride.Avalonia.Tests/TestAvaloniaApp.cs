using global::Avalonia;
using global::Avalonia.Styling;
using global::Avalonia.Themes.Fluent;

namespace Stride.Avalonia.Tests;

/// <summary>
/// Minimal Avalonia application with Fluent theme for stress tests.
/// </summary>
public class TestAvaloniaApp : Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
        base.OnFrameworkInitializationCompleted();
    }
}
