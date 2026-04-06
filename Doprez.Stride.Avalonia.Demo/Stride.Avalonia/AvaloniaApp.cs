using System;
using System.Reflection;
using global::Avalonia;
using global::Avalonia.Headless;
using global::Avalonia.Platform;
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
    private const string ExperimentalPlatformGraphicsEnvVar = "STRIDE_AVALONIA_EXPERIMENTAL_PLATFORM_GRAPHICS";
    private static bool _initialized;

    /// <summary>
    /// When <c>true</c>, the custom <see cref="StridePlatformGraphics"/>
    /// rendering pipeline is active — Avalonia renders to an
    /// <see cref="StrideRenderSurface"/> instead of the headless framebuffer.
    /// </summary>
    internal static bool UseStridePlatformGraphics { get; private set; }

    public override void Initialize()
    {
        // Base implementation is intentionally minimal.
        // Subclass and add themes like FluentAvaloniaTheme, DockFluentTheme, etc.
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    /// <summary>
    /// Initializes the Avalonia headless platform with Skia rendering
    /// using the default <see cref="AvaloniaApp"/> (no themes).
    /// <para>
    /// Uses the optimised <see cref="StridePlatformGraphics"/> pipeline
    /// that renders to an <see cref="StrideRenderSurface"/>.
    /// </para>
    /// Safe to call multiple times — only the first call has an effect.
    /// </summary>
    public static void EnsureInitialized()
    {
        EnsureInitialized<AvaloniaApp>();
    }

    /// <summary>
    /// Initializes the Avalonia headless platform with a custom <see cref="Application"/> subclass.
    /// Use this to register themes (FluentAvalonia, Dock, etc.) before the UI is created.
    /// <para>
    /// Registers <see cref="StridePlatformGraphics"/> as the GPU rendering
    /// backend.  Avalonia renders directly to an <see cref="StrideRenderSurface"/>
    /// whose pixels are accessible via pointer — no <c>WriteableBitmap</c>,
    /// no LOH allocations, no intermediate copies.
    /// </para>
    /// Safe to call multiple times — only the first call has an effect.
    /// </summary>
    public static void EnsureInitialized<TApp>() where TApp : Application, new()
    {
        if (_initialized) return;
        _initialized = true;

        UseStridePlatformGraphics = IsStridePlatformGraphicsEnabled();

        var builder = AppBuilder.Configure<TApp>()
            .UsePlatformDetect()        // configures Skia rendering backend
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false // use real Skia rendering
            });

        if (UseStridePlatformGraphics)
        {
            var platformGraphics = new StridePlatformGraphics();
            builder = builder.AfterSetup(_ =>
            {
                // Override the platform graphics with our custom pipeline.
                // AvaloniaLocator.CurrentMutable is internal in Avalonia 11.3+,
                // so we access it via reflection — the same pattern used by
                // AvaloniaInputBridge for other internal Avalonia APIs.
                RegisterPlatformGraphics(platformGraphics);
            });
        }

        builder.SetupWithoutStarting();
    }

    private static bool IsStridePlatformGraphicsEnabled()
    {
        var value = Environment.GetEnvironmentVariable(ExperimentalPlatformGraphicsEnvVar);
        return string.Equals(value, "1", StringComparison.Ordinal)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Registers a custom <see cref="IPlatformGraphics"/> implementation
    /// via reflection on <c>AvaloniaLocator.CurrentMutable</c> (internal
    /// in Avalonia 11.3+).
    /// </summary>
    private static void RegisterPlatformGraphics(StridePlatformGraphics graphics)
    {
        var locatorType = typeof(global::Avalonia.AvaloniaLocator);

        // Get AvaloniaLocator.CurrentMutable (internal static property).
        var currentMutableProp = locatorType.GetProperty("CurrentMutable",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

        var locator = currentMutableProp?.GetValue(null);
        if (locator == null) return;

        // Call locator.BindToSelf<IPlatformGraphics>(graphics)
        // BindToSelf<T>(T constant) registers a singleton service.
        var bindToSelfMethod = locatorType.GetMethod("BindToSelf",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        if (bindToSelfMethod != null)
        {
            var generic = bindToSelfMethod.MakeGenericMethod(typeof(IPlatformGraphics));
            generic.Invoke(locator, [graphics]);
        }
    }
}
