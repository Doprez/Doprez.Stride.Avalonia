using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using global::Avalonia.Threading;
using Stride.Engine;
using Stride.Games;
using System;

namespace Stride.Avalonia;

/// <summary>
/// A default debug overlay panel that displays runtime diagnostics such as
/// FPS, frame time, entity count, and draw call information.
/// Add this as a fullscreen <see cref="AvaloniaComponent"/> to see live stats.
/// </summary>
public class DebugPanel : UserControl
{
    private readonly TextBlock _fpsText;
    private readonly TextBlock _frameTimeText;
    private readonly TextBlock _entityCountText;
    private readonly TextBlock _drawCallText;
    private readonly TextBlock _memoryText;
    private readonly TextBlock _uptimeText;
    private readonly StackPanel _customLines;

    // Avalonia perf section
    private readonly StackPanel _avaloniaSection;
    private readonly TextBlock _avUpdateText;
    private readonly TextBlock _avInputText;
    private readonly TextBlock _avDispatcherText;
    private readonly TextBlock _avDrawTotalText;
    private readonly TextBlock _avCollectSortText;
    private readonly TextBlock _avCaptureText;
    private readonly TextBlock _avUploadText;
    private readonly TextBlock _avSpriteDrawText;
    private readonly TextBlock _avPanelsText;
    private readonly TextBlock _avBytesText;
    private readonly TextBlock _avAtlasCountText;
    private readonly TextBlock _avGcText;
    private readonly TextBlock _avFrameTimeText;
    private readonly TextBlock _avStutterText;

    private int _frameCount;
    private double _elapsed;
    private double _lastFps;
    private double _minFps = double.MaxValue;
    private double _maxFps;

    /// <summary>
    /// Gets or sets how often (in seconds) the display refreshes. Default is 0.25s.
    /// </summary>
    public double RefreshInterval { get; set; } = 0.25;

    /// <summary>
    /// When true, tracks and displays min/max FPS since the panel was created.
    /// </summary>
    public bool ShowMinMaxFps { get; set; } = true;

    /// <summary>
    /// When true, shows per-phase Avalonia rendering performance metrics.
    /// </summary>
    public bool ShowAvaloniaPerfMetrics { get; set; } = true;

    /// <summary>
    /// Optional reference to the <see cref="AvaloniaPage"/> hosting this panel.
    /// When set, the panel will call <see cref="AvaloniaPage.MarkDirty"/> after
    /// updating its text so the renderer knows to re-capture.
    /// </summary>
    public AvaloniaPage? Page { get; set; }

    public DebugPanel()
    {
        IsHitTestVisible = false; // don't consume mouse events

        _fpsText = MakeLabel("FPS: --");
        _frameTimeText = MakeLabel("Frame: -- ms");
        _entityCountText = MakeLabel("Entities: --");
        _drawCallText = MakeLabel("Draw calls: --");
        _memoryText = MakeLabel("Memory: -- MB");
        _uptimeText = MakeLabel("Uptime: --");
        _customLines = new StackPanel { Spacing = 2 };

        // Avalonia perf section
        _avUpdateText = MakeLabel("Update: -- ms");
        _avInputText = MakeLabel("  Input: -- ms");
        _avDispatcherText = MakeLabel("  Dispatcher: -- ms");
        _avDrawTotalText = MakeLabel("Draw: -- ms");
        _avCollectSortText = MakeLabel("  Collect+Sort: -- ms");
        _avCaptureText = MakeLabel("  Capture: -- ms");
        _avUploadText = MakeLabel("  Upload: -- ms");
        _avSpriteDrawText = MakeLabel("  SpriteBatch: -- ms");
        _avPanelsText = MakeLabel("Panels: --");
        _avBytesText = MakeLabel("Uploaded: -- B");
        _avAtlasCountText = MakeLabel("Atlases: --");
        _avGcText = MakeLabel("GC: --");
        _avFrameTimeText = MakeLabel("Frame: avg -- / peak -- ms");
        _avStutterText = MakeLabel("GC delta: --");

        _avaloniaSection = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new Separator { Margin = new Thickness(0, 4), Background = Brushes.Gray },
                MakeHeader("Avalonia Perf"),
                _avUpdateText,
                _avInputText,
                _avDispatcherText,
                new Separator { Margin = new Thickness(0, 2), Background = Brushes.Transparent },
                _avDrawTotalText,
                _avCollectSortText,
                _avCaptureText,
                _avUploadText,
                _avSpriteDrawText,
                new Separator { Margin = new Thickness(0, 2), Background = Brushes.Transparent },
                _avPanelsText,
                _avBytesText,
                _avAtlasCountText,
                _avGcText,
                _avFrameTimeText,
                _avStutterText,
            },
        };

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(180, 15, 15, 20)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 8),
            Margin = new Thickness(10),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new StackPanel
            {
                Spacing = 3,
                Children =
                {
                    MakeHeader("Debug"),
                    new Separator { Margin = new Thickness(0, 2), Background = Brushes.Gray },
                    _fpsText,
                    _frameTimeText,
                    new Separator { Margin = new Thickness(0, 2), Background = Brushes.Transparent },
                    _entityCountText,
                    _drawCallText,
                    new Separator { Margin = new Thickness(0, 2), Background = Brushes.Transparent },
                    _memoryText,
                    _uptimeText,
                    _customLines,
                    _avaloniaSection,
                },
            },
        };
    }

    // Snapshot fields — written on game thread, read on UI thread via cached Action
    private string _snapshotFps = "";
    private double _snapshotFrameMs;
    private int _snapshotEntityCount;
    private long _snapshotMemoryMb;
    private TimeSpan _snapshotUptime;
    private int _snapshotDrawCalls;
    private long _snapshotTriangles;
    private bool _snapshotHasGame;
    private double _snapshotAvUpdate, _snapshotAvInput, _snapshotAvDispatcher;
    private double _snapshotAvDrawTotal, _snapshotAvCollectSort, _snapshotAvCapture;
    private double _snapshotAvUpload, _snapshotAvSprite;
    private int _snapshotAvDrawn, _snapshotAvCulled, _snapshotAvDirtyUpd, _snapshotAvDirtySkip;
    private long _snapshotAvBytes;
    private int _snapshotAvAtlasCount;
    private int _snapshotAvGc0, _snapshotAvGc1, _snapshotAvGc2;
    private double _snapshotAvAvgFrame, _snapshotAvPeakFrame;
    private int _snapshotAvGcDelta0, _snapshotAvGcDelta1, _snapshotAvGcDelta2;
    private bool _snapshotShowAvPerf;
    private Action? _refreshAction;

    /// <summary>
    /// Call once per frame from your game update loop to feed timing and scene data.
    /// </summary>
    /// <param name="gameTime">The current <see cref="GameTime"/> from the game loop.</param>
    /// <param name="rootScene">The root scene to count entities in.</param>
    /// <param name="game">Optional game instance for draw call info.</param>
    public void Update(GameTime gameTime, Scene? rootScene, Game? game = null)
    {
        _frameCount++;
        _elapsed += gameTime.Elapsed.TotalSeconds;

        if (_elapsed < RefreshInterval) return;

        _lastFps = _frameCount / _elapsed;
        _snapshotFrameMs = (_elapsed / _frameCount) * 1000.0;

        if (_lastFps < _minFps) _minFps = _lastFps;
        if (_lastFps > _maxFps) _maxFps = _lastFps;

        _snapshotEntityCount = rootScene != null ? CountEntities(rootScene) : 0;
        _snapshotMemoryMb = GC.GetTotalMemory(false) / (1024 * 1024);
        _snapshotUptime = gameTime.Total;

        _snapshotFps = ShowMinMaxFps
            ? $"FPS: {_lastFps:F1}  (min {_minFps:F0} / max {_maxFps:F0})"
            : $"FPS: {_lastFps:F1}";

        if (game != null)
        {
            var gd = game.GraphicsDevice;
            _snapshotDrawCalls = (int)(gd?.FrameDrawCalls ?? 0);
            _snapshotTriangles = (long)(gd?.FrameTriangleCount ?? 0);
            _snapshotHasGame = true;
        }
        else
        {
            _snapshotHasGame = false;
        }

        // Snapshot Avalonia metrics
        var m = AvaloniaRenderMetrics.Instance;
        _snapshotAvUpdate = m.UpdateTotalMs;
        _snapshotAvInput = m.InputProcessingMs;
        _snapshotAvDispatcher = m.DispatcherJobsMs;
        _snapshotAvDrawTotal = m.DrawTotalMs;
        _snapshotAvCollectSort = m.DrawCollectSortMs;
        _snapshotAvCapture = m.FrameCaptureMs;
        _snapshotAvUpload = m.TextureUploadMs;
        _snapshotAvSprite = m.SpriteBatchDrawMs;
        _snapshotAvDrawn = m.PanelsDrawn;
        _snapshotAvCulled = m.PanelsCulled;
        _snapshotAvDirtyUpd = m.PanelsDirtyUpdated;
        _snapshotAvDirtySkip = m.PanelsDirtySkipped;
        _snapshotAvBytes = m.BytesUploaded;
        _snapshotAvAtlasCount = m.AtlasCount;
        _snapshotAvGc0 = m.GcGen0;
        _snapshotAvGc1 = m.GcGen1;
        _snapshotAvGc2 = m.GcGen2;
        _snapshotAvAvgFrame = m.AvgFrameMs;
        _snapshotAvPeakFrame = m.PeakFrameMs;
        _snapshotAvGcDelta0 = m.GcDelta0;
        _snapshotAvGcDelta1 = m.GcDelta1;
        _snapshotAvGcDelta2 = m.GcDelta2;
        _snapshotShowAvPerf = ShowAvaloniaPerfMetrics;

        // Use a cached Action to avoid closure + delegate allocation every tick
        _refreshAction ??= RefreshUI;
        Dispatcher.UIThread.Post(_refreshAction);

        // Explicitly mark page dirty so the renderer knows to re-capture
        // after we update the text. This replaces the removed LayoutUpdated approach.
        Page?.MarkDirty();

        _frameCount = 0;
        _elapsed = 0;
    }

    /// <summary>
    /// Applies snapshotted values to the UI controls on the Avalonia thread.
    /// </summary>
    private void RefreshUI()
    {
        _fpsText.Text = _snapshotFps;
        _frameTimeText.Text = $"Frame: {_snapshotFrameMs:F2} ms";
        _entityCountText.Text = $"Entities: {_snapshotEntityCount}";
        _memoryText.Text = $"Memory: {_snapshotMemoryMb} MB (managed)";
        _uptimeText.Text = $"Uptime: {_snapshotUptime.Minutes:D2}:{_snapshotUptime.Seconds:D2}.{_snapshotUptime.Milliseconds / 100}";

        if (_snapshotHasGame)
        {
            _drawCallText.Text = $"Draw calls: {_snapshotDrawCalls}  |  Triangles: {_snapshotTriangles}";
        }

        // Avalonia perf section
        _avaloniaSection.IsVisible = _snapshotShowAvPerf;
        if (_snapshotShowAvPerf)
        {
            _avUpdateText.Text = $"Update: {_snapshotAvUpdate:F3} ms";
            _avInputText.Text = $"  Input: {_snapshotAvInput:F3} ms";
            _avDispatcherText.Text = $"  Dispatcher: {_snapshotAvDispatcher:F3} ms";
            _avDrawTotalText.Text = $"Draw: {_snapshotAvDrawTotal:F3} ms";
            _avCollectSortText.Text = $"  Collect+Sort: {_snapshotAvCollectSort:F3} ms";
            _avCaptureText.Text = $"  Capture: {_snapshotAvCapture:F3} ms";
            _avUploadText.Text = $"  Upload: {_snapshotAvUpload:F3} ms";
            _avSpriteDrawText.Text = $"  SpriteBatch: {_snapshotAvSprite:F3} ms";
            _avPanelsText.Text = $"Panels: {_snapshotAvDrawn} drawn  {_snapshotAvCulled} culled  {_snapshotAvDirtyUpd} dirty  {_snapshotAvDirtySkip} skipped";
            _avBytesText.Text = $"Uploaded: {_snapshotAvBytes / 1024.0:F1} KB";
            _avAtlasCountText.Text = $"Atlases: {_snapshotAvAtlasCount}";
            _avGcText.Text = $"GC: {_snapshotAvGc0}/{_snapshotAvGc1}/{_snapshotAvGc2} (gen0/1/2)";
            _avFrameTimeText.Text = $"Frame: avg {_snapshotAvAvgFrame:F2} / peak {_snapshotAvPeakFrame:F2} ms";
            _avStutterText.Text = $"GC delta: {_snapshotAvGcDelta0}/{_snapshotAvGcDelta1}/{_snapshotAvGcDelta2} (last {RefreshInterval:F2}s)";
        }
    }

    /// <summary>
    /// The most recently computed FPS value.
    /// </summary>
    public double CurrentFps => _lastFps;

    /// <summary>
    /// Minimum FPS recorded since creation.
    /// </summary>
    public double MinFps => _minFps == double.MaxValue ? 0 : _minFps;

    /// <summary>
    /// Maximum FPS recorded since creation.
    /// </summary>
    public double MaxFps => _maxFps;

    /// <summary>
    /// Resets min/max FPS tracking.
    /// </summary>
    public void ResetMinMax()
    {
        _minFps = double.MaxValue;
        _maxFps = 0;
    }

    /// <summary>
    /// Add a custom line of text to the debug panel. Useful for test-specific info.
    /// </summary>
    public TextBlock AddCustomLine(string initialText = "")
    {
        var tb = MakeLabel(initialText);
        _customLines.Children.Add(tb);
        return tb;
    }

    private static int CountEntities(Scene scene)
    {
        int count = scene.Entities.Count;
        foreach (var child in scene.Children)
            count += CountEntities(child);
        return count;
    }

    private static TextBlock MakeLabel(string text) => new()
    {
        Text = text,
        FontSize = 13,
        FontFamily = new FontFamily("Consolas, Courier New, monospace"),
        Foreground = new SolidColorBrush(Color.FromRgb(200, 220, 240)),
    };

    private static TextBlock MakeHeader(string text) => new()
    {
        Text = text,
        FontSize = 15,
        FontWeight = FontWeight.Bold,
        Foreground = new SolidColorBrush(Color.FromRgb(100, 200, 255)),
    };
}
