using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Input;
using Stride.Avalonia;

namespace Doprez.Stride.Avalonia.Demo;

/// <summary>
/// Creates a fullscreen <see cref="DebugPanel"/> overlay that displays
/// FPS, frame time, entity count, memory usage, Avalonia perf metrics, etc.
/// <para>
/// Attach this script to any entity in the scene via GameStudio.
/// The script creates an <see cref="AvaloniaComponent"/> (fullscreen) on the
/// same entity and updates the debug panel every frame.
/// </para>
/// </summary>
public class DebugOverlayScript : SyncScript
{
    private DebugPanel? _debugPanel;

    /// <summary>
    /// When <c>true</c>, the debug panel shows min/max FPS tracking.
    /// </summary>
    [DataMember(10)]
    public bool ShowMinMaxFps { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, the debug panel shows per-phase Avalonia rendering
    /// performance metrics.
    /// </summary>
    [DataMember(20)]
    public bool ShowAvaloniaPerfMetrics { get; set; } = true;

    public override void Start()
    {
        _debugPanel = new DebugPanel
        {
            ShowMinMaxFps = ShowMinMaxFps,
            ShowAvaloniaPerfMetrics = ShowAvaloniaPerfMetrics,
        };

        var page = new DefaultAvaloniaPage(_debugPanel);
        _debugPanel.Page = page;

        // Add a fullscreen AvaloniaComponent to this entity
        var avaloniaComponent = new AvaloniaComponent
        {
            IsFullScreen = true,
            Resolution = new Vector2(
                Game.Window.ClientBounds.Width,
                Game.Window.ClientBounds.Height),
            Page = page,
        };

        Entity.Add(avaloniaComponent);
    }

    public override void Update()
    {
        if (_debugPanel == null) return;

        // Feed timing and scene data to the debug panel every frame
        _debugPanel.Update(
            Game.UpdateTime,
            Entity.Scene,
            Game as Game);

        // F3 = dump Avalonia performance metrics to console
        // F4 = start benchmark, F5 = stop + dump benchmark
        if (Input.HasKeyboard)
        {
            if (Input.IsKeyPressed(Keys.F3))
                AvaloniaRenderMetrics.Instance.DumpToConsole();

            if (Input.IsKeyPressed(Keys.F4))
                AvaloniaRenderMetrics.Instance.StartBenchmark();

            if (Input.IsKeyPressed(Keys.F5))
            {
                AvaloniaRenderMetrics.Instance.StopBenchmark();
                AvaloniaRenderMetrics.Instance.DumpBenchmark();
            }
        }
    }

    public override void Cancel()
    {
        var comp = Entity.Get<AvaloniaComponent>();
        comp?.Page?.Dispose();
        if (comp != null)
            Entity.Remove(comp);
        _debugPanel = null;
    }
}
