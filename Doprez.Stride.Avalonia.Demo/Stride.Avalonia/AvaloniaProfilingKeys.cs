using Stride.Core.Diagnostics;

namespace Stride.Avalonia;

/// <summary>
/// <see cref="ProfilingKey"/>s for the Stride.Avalonia rendering pipeline.
/// Enable Stride's built-in profiler to see these timings in the overlay.
/// </summary>
public static class AvaloniaProfilingKeys
{
    // ── Update phase (AvaloniaSystem) ──

    /// <summary>Total time in <c>AvaloniaSystem.Update()</c>.</summary>
    public static readonly ProfilingKey Update =
        new("Avalonia.Update");

    /// <summary>Component collection during Update.</summary>
    public static readonly ProfilingKey CollectComponentsUpdate =
        new(Update, "Avalonia.Update.CollectComponents");

    /// <summary>Input forwarding to headless windows.</summary>
    public static readonly ProfilingKey InputProcessing =
        new(Update, "Avalonia.Update.InputProcessing");

    /// <summary>Running <c>Dispatcher.UIThread.RunJobs()</c>.</summary>
    public static readonly ProfilingKey DispatcherJobs =
        new(Update, "Avalonia.Update.DispatcherJobs");

    // ── Draw phase (AvaloniaSceneRenderer) ──

    /// <summary>Total time in <c>AvaloniaSceneRenderer.DrawCore()</c>.</summary>
    public static readonly ProfilingKey Draw =
        new("Avalonia.Draw");

    /// <summary>Component collection during Draw.</summary>
    public static readonly ProfilingKey CollectComponentsDraw =
        new(Draw, "Avalonia.Draw.CollectComponents");

    /// <summary>Back-to-front sorting of components.</summary>
    public static readonly ProfilingKey SortComponents =
        new(Draw, "Avalonia.Draw.SortComponents");

    /// <summary>Avalonia <c>CaptureRenderedFrame()</c> calls.</summary>
    public static readonly ProfilingKey FrameCapture =
        new(Draw, "Avalonia.Draw.FrameCapture");

    /// <summary>CPU→GPU texture upload via <c>SetData()</c>.</summary>
    public static readonly ProfilingKey TextureUpload =
        new(Draw, "Avalonia.Draw.TextureUpload");

    /// <summary>SpriteBatch Begin/Draw/End calls.</summary>
    public static readonly ProfilingKey SpriteBatchDraw =
        new(Draw, "Avalonia.Draw.SpriteBatchDraw");
}
