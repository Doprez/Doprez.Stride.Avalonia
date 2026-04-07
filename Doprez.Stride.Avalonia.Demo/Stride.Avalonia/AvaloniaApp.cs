using System;
using System.Reflection;
using global::Avalonia;
using global::Avalonia.Headless;
using global::Avalonia.Platform;
using global::Avalonia.Styling;
using Stride.Graphics;

namespace Stride.Avalonia;

/// <summary>
/// Minimal Avalonia <see cref="Application"/> for headless rendering inside Stride.
/// Call <see cref="EnsureInitialized()"/> once during startup before creating any
/// <see cref="AvaloniaPage"/> instances.
/// <para>
/// Subclass this to add custom themes (e.g. FluentAvaloniaTheme, DockFluentTheme)
/// and override <see cref="Initialize"/> to register them.
/// </para>
/// </summary>
public class AvaloniaApp : Application
{
    private static bool _initialized;

    /// <summary>
    /// Always <c>true</c> — the custom <see cref="StridePlatformGraphics"/>
    /// rendering pipeline is active.  Avalonia renders to GPU-backed
    /// <see cref="StrideRenderSurface"/> instances for zero-copy texture sharing.
    /// </summary>
    internal static bool UseStridePlatformGraphics => true;

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
    /// Initializes Avalonia and binds the shared graphics path to the given
    /// Stride <see cref="GraphicsDevice"/>.
    /// </summary>
    internal static void EnsureInitialized(GraphicsDevice graphicsDevice)
    {
        EnsureInitialized<AvaloniaApp>(graphicsDevice);
    }

    /// <summary>
    /// Initializes the Avalonia headless platform with a custom <see cref="Application"/> subclass.
    /// Use this to register themes (FluentAvalonia, Dock, etc.) before the UI is created.
    /// <para>
    /// Registers <see cref="StridePlatformGraphics"/> as the GPU rendering
    /// backend.  Avalonia renders directly into Stride-owned GPU textures
    /// via shared graphics context interop.
    /// </para>
    /// Safe to call multiple times — only the first call has an effect.
    /// </summary>
    public static void EnsureInitialized<TApp>() where TApp : Application, new()
    {
        EnsureInitialized<TApp>(graphicsDevice: null);
    }

    /// <summary>
    /// Initializes the Avalonia headless platform with a custom application
    /// subclass and an optional Stride graphics device for shared-context interop.
    /// </summary>
    internal static void EnsureInitialized<TApp>(GraphicsDevice? graphicsDevice) where TApp : Application, new()
    {
        if (!_initialized)
        {
            if (graphicsDevice != null)
                StridePlatformGraphics.EnsureSharedContext(graphicsDevice);

            var builder = AppBuilder.Configure<TApp>()
                .UsePlatformDetect()        // configures Skia rendering backend
                .UseHeadless(new AvaloniaHeadlessPlatformOptions
                {
                    UseHeadlessDrawing = false // use real Skia rendering
                });

            var platformGraphics = new StridePlatformGraphics();

            builder.SetupWithoutStarting();

            // The headless platform creates Compositor(null) — no IPlatformGraphics.
            // Patch the existing Compositor's internal ServerCompositor to use
            // our StridePlatformGraphics. This avoids creating a second Compositor
            // (which would cause windows to not register with it).
            PatchCompositorGraphics(platformGraphics);

            _initialized = true;
        }
        else if (graphicsDevice != null)
        {
            StridePlatformGraphics.EnsureSharedContext(graphicsDevice);
        }
    }

    /// <summary>
    /// Patches the existing headless Compositor's internal rendering pipeline
    /// to use our <see cref="StridePlatformGraphics"/> instead of null.
    /// </summary>
    /// <remarks>
    /// The path is: Compositor._server (ServerCompositor)
    ///            → .RenderInterface (PlatformRenderInterfaceContextManager)
    ///            → ._graphics (IPlatformGraphics)
    /// All internal fields, accessed via reflection.
    /// </remarks>
    private static void PatchCompositorGraphics(StridePlatformGraphics graphics)
    {
        var compositorProp = typeof(AvaloniaHeadlessPlatform).GetProperty("Compositor",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        var compositor = compositorProp?.GetValue(null);
        if (compositor == null) return;

        var serverField = compositor.GetType().GetField("_server",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var server = serverField?.GetValue(compositor);
        if (server == null) return;

        var renderInterfaceProp = server.GetType().GetProperty("RenderInterface",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var renderInterface = renderInterfaceProp?.GetValue(server);
        if (renderInterface == null) return;

        var graphicsField = renderInterface.GetType().GetField("_graphics",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (graphicsField == null) return;

        graphicsField.SetValue(renderInterface, graphics);
    }
}
