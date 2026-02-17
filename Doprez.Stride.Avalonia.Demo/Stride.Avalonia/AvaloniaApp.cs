using global::Avalonia;
using global::Avalonia.Headless;
using global::Avalonia.Styling;

namespace Stride.Avalonia;

/// <summary>
/// Minimal Avalonia <see cref="Application"/> for headless rendering inside Stride.
/// Call <see cref="EnsureInitialized"/> once during startup before creating any
/// <see cref="AvaloniaPage"/> instances.
/// <para>
/// Subclass this to add custom themes (e.g. FluentAvaloniaTheme, DockFluentTheme)
/// and override <see cref="Initialize"/> to register them.
/// </para>
/// </summary>
public class AvaloniaApp : Application
{
    private static bool _initialized;

    public override void Initialize()
    {
        // Base implementation is intentionally minimal.
        // Subclass and add themes like FluentAvaloniaTheme, DockFluentTheme, etc.
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    /// <summary>
    /// Initializes the Avalonia headless platform with Skia rendering
    /// using the default <see cref="AvaloniaApp"/> (no themes).
    /// Safe to call multiple times — only the first call has an effect.
    /// </summary>
    public static void EnsureInitialized()
    {
        EnsureInitialized<AvaloniaApp>();
    }

    /// <summary>
    /// Initializes the Avalonia headless platform with a custom <see cref="Application"/> subclass.
    /// Use this to register themes (FluentAvalonia, Dock, etc.) before the UI is created.
    /// Safe to call multiple times — only the first call has an effect.
    /// </summary>
    public static void EnsureInitialized<TApp>() where TApp : Application, new()
    {
        if (_initialized) return;
        _initialized = true;

        AppBuilder.Configure<TApp>()
            .UsePlatformDetect()        // configures Skia rendering backend
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false // use real Skia rendering
            })
            .SetupWithoutStarting();
    }
}
