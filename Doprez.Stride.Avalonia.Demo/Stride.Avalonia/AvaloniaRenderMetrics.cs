using System;
using System.Diagnostics;

namespace Stride.Avalonia;

/// <summary>
/// Per-frame performance metrics for the Avalonia rendering pipeline.
/// Populated by <see cref="AvaloniaSceneRenderer"/> and <see cref="AvaloniaSystem"/>
/// and read by <see cref="DebugPanel"/> or external diagnostic code.
/// </summary>
public sealed class AvaloniaRenderMetrics
{
    /// <summary>Shared singleton — written by the renderer, readable anywhere.</summary>
    public static readonly AvaloniaRenderMetrics Instance = new();

    // ── Update phase (AvaloniaSystem) ──

    /// <summary>Total time in <c>AvaloniaSystem.Update()</c> (ms).</summary>
    public double UpdateTotalMs { get; internal set; }

    /// <summary>Time spent collecting components during Update (ms).</summary>
    public double UpdateCollectMs { get; internal set; }

    /// <summary>Time spent forwarding input to headless windows (ms).</summary>
    public double InputProcessingMs { get; internal set; }

    /// <summary>Time spent running <c>Dispatcher.UIThread.RunJobs()</c> (ms).</summary>
    public double DispatcherJobsMs { get; internal set; }

    // ── Draw phase (AvaloniaSceneRenderer) ──

    /// <summary>Total time in <c>AvaloniaSceneRenderer.DrawCore()</c> (ms).</summary>
    public double DrawTotalMs { get; internal set; }

    /// <summary>Time spent collecting + sorting components during Draw (ms).</summary>
    public double DrawCollectSortMs { get; internal set; }

    /// <summary>Accumulated time calling <c>CaptureFrame()</c> this frame (ms).</summary>
    public double FrameCaptureMs { get; internal set; }

    /// <summary>Accumulated time uploading textures via <c>SetData()</c> this frame (ms).</summary>
    public double TextureUploadMs { get; internal set; }

    /// <summary>Accumulated time in SpriteBatch draw calls this frame (ms).</summary>
    public double SpriteBatchDrawMs { get; internal set; }

    // ── Counters ──

    /// <summary>Number of panels processed (passed culling) this frame.</summary>
    public int PanelsDrawn { get; internal set; }

    /// <summary>Number of panels frustum-culled this frame.</summary>
    public int PanelsCulled { get; internal set; }

    /// <summary>Number of dirty panels that had their texture re-captured this frame.</summary>
    public int PanelsDirtyUpdated { get; internal set; }

    /// <summary>Number of dirty panels skipped due to the per-frame budget.</summary>
    public int PanelsDirtySkipped { get; internal set; }

    /// <summary>Total bytes uploaded to GPU this frame.</summary>
    public long BytesUploaded { get; internal set; }

    /// <summary>Number of atlas textures currently allocated.</summary>
    public int AtlasCount { get; internal set; }

    /// <summary>
    /// Number of frames where <c>ForceRenderTimerTick()</c> was skipped
    /// because no panels were dirty (dirty-gate) or because the UI Hz
    /// budget had not elapsed (Hz throttle).  Reset each frame.
    /// </summary>
    public int SkippedRenderTicks { get; internal set; }

    // ── GC pressure ──

    /// <summary>Gen-0 GC count at last snapshot.</summary>
    public int GcGen0 { get; internal set; }

    /// <summary>Gen-1 GC count at last snapshot.</summary>
    public int GcGen1 { get; internal set; }

    /// <summary>Gen-2 GC count at last snapshot.</summary>
    public int GcGen2 { get; internal set; }

    // ── Stutter / Benchmark stats ──

    private int _prevGc0, _prevGc1, _prevGc2;
    private long _frameTimestampPrev;
    private long _benchmarkStart;
    private bool _benchmarkRunning;
    private int _benchmarkFrames;
    private double _benchmarkSumMs;
    private double _benchmarkPeakMs;
    private int _benchmarkStutters; // frames > 2x avg
    private int _benchmarkGcDelta0, _benchmarkGcDelta1, _benchmarkGcDelta2;
    private int _benchmarkGcStart0, _benchmarkGcStart1, _benchmarkGcStart2;

    // Rolling frame-time tracking (last 120 frames)
    private const int HistorySize = 120;
    private readonly double[] _frameHistory = new double[HistorySize];
    private int _historyIndex;
    private int _historyCount;

    /// <summary>GC collections that occurred during the last frame.</summary>
    public int GcDelta0 { get; private set; }
    /// <summary>GC collections that occurred during the last frame.</summary>
    public int GcDelta1 { get; private set; }
    /// <summary>GC collections that occurred during the last frame.</summary>
    public int GcDelta2 { get; private set; }

    /// <summary>Total frame time (ms) — Update + Draw + engine overhead.</summary>
    public double TotalFrameMs { get; private set; }

    /// <summary>Peak frame time in the last <see cref="HistorySize"/> frames.</summary>
    public double PeakFrameMs { get; private set; }

    /// <summary>Average frame time in the last <see cref="HistorySize"/> frames.</summary>
    public double AvgFrameMs { get; private set; }

    // ── Helpers ──

    /// <summary>Resets all counters to zero at the start of a frame.</summary>
    internal void ResetFrame()
    {
        // ── Frame-to-frame timing ──
        long now = Stopwatch.GetTimestamp();
        if (_frameTimestampPrev != 0)
        {
            TotalFrameMs = Stopwatch.GetElapsedTime(_frameTimestampPrev, now).TotalMilliseconds;

            // Rolling history
            _frameHistory[_historyIndex] = TotalFrameMs;
            _historyIndex = (_historyIndex + 1) % HistorySize;
            if (_historyCount < HistorySize) _historyCount++;

            // Compute avg and peak from history
            double sum = 0, peak = 0;
            for (int i = 0; i < _historyCount; i++)
            {
                sum += _frameHistory[i];
                if (_frameHistory[i] > peak) peak = _frameHistory[i];
            }
            AvgFrameMs = sum / _historyCount;
            PeakFrameMs = peak;

            // Benchmark accumulation
            if (_benchmarkRunning)
            {
                _benchmarkFrames++;
                _benchmarkSumMs += TotalFrameMs;
                if (TotalFrameMs > _benchmarkPeakMs) _benchmarkPeakMs = TotalFrameMs;
                double runningAvg = _benchmarkSumMs / _benchmarkFrames;
                if (TotalFrameMs > runningAvg * 2.0) _benchmarkStutters++;
            }
        }
        _frameTimestampPrev = now;

        // ── GC delta tracking ──
        int gc0 = GC.CollectionCount(0);
        int gc1 = GC.CollectionCount(1);
        int gc2 = GC.CollectionCount(2);
        GcDelta0 = gc0 - _prevGc0;
        GcDelta1 = gc1 - _prevGc1;
        GcDelta2 = gc2 - _prevGc2;
        _prevGc0 = gc0;
        _prevGc1 = gc1;
        _prevGc2 = gc2;
        GcGen0 = gc0;
        GcGen1 = gc1;
        GcGen2 = gc2;

        if (_benchmarkRunning)
        {
            _benchmarkGcDelta0 = gc0 - _benchmarkGcStart0;
            _benchmarkGcDelta1 = gc1 - _benchmarkGcStart1;
            _benchmarkGcDelta2 = gc2 - _benchmarkGcStart2;
        }

        UpdateTotalMs = 0;
        UpdateCollectMs = 0;
        InputProcessingMs = 0;
        DispatcherJobsMs = 0;

        DrawTotalMs = 0;
        DrawCollectSortMs = 0;
        FrameCaptureMs = 0;
        TextureUploadMs = 0;
        SpriteBatchDrawMs = 0;

        PanelsDrawn = 0;
        PanelsCulled = 0;
        PanelsDirtyUpdated = 0;
        PanelsDirtySkipped = 0;
        BytesUploaded = 0;
        SkippedRenderTicks = 0;
    }

    /// <summary>
    /// Dumps a formatted summary to the console.
    /// </summary>
    public void DumpToConsole()
    {
        Console.WriteLine("╔══════════════════════════════════════════════════════╗");
        Console.WriteLine("║         Stride.Avalonia — Performance Metrics       ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  Update total        : {UpdateTotalMs,8:F3} ms               ║");
        Console.WriteLine($"║    Collect components : {UpdateCollectMs,8:F3} ms               ║");
        Console.WriteLine($"║    Input processing   : {InputProcessingMs,8:F3} ms               ║");
        Console.WriteLine($"║    Dispatcher jobs    : {DispatcherJobsMs,8:F3} ms               ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  Draw total          : {DrawTotalMs,8:F3} ms               ║");
        Console.WriteLine($"║    Collect + sort     : {DrawCollectSortMs,8:F3} ms               ║");
        Console.WriteLine($"║    Frame capture      : {FrameCaptureMs,8:F3} ms               ║");
        Console.WriteLine($"║    Texture upload     : {TextureUploadMs,8:F3} ms               ║");
        Console.WriteLine($"║    SpriteBatch draw   : {SpriteBatchDrawMs,8:F3} ms               ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  Panels drawn        : {PanelsDrawn,6}                   ║");
        Console.WriteLine($"║  Panels culled       : {PanelsCulled,6}                   ║");
        Console.WriteLine($"║  Dirty updated       : {PanelsDirtyUpdated,6}                   ║");
        Console.WriteLine($"║  Dirty skipped       : {PanelsDirtySkipped,6}                   ║");
        Console.WriteLine($"║  Bytes uploaded      : {BytesUploaded,10}               ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  GC Gen0 / Gen1 / Gen2 : {GcGen0} / {GcGen1} / {GcGen2}              ║");
        Console.WriteLine($"║  GC delta (last frame) : {GcDelta0} / {GcDelta1} / {GcDelta2}              ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  Frame time (last)   : {TotalFrameMs,8:F3} ms               ║");
        Console.WriteLine($"║  Frame time (avg120) : {AvgFrameMs,8:F3} ms               ║");
        Console.WriteLine($"║  Frame time (peak)   : {PeakFrameMs,8:F3} ms               ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════╝");
    }

    // ── Benchmark API ──

    /// <summary>
    /// Starts collecting stutter/GC statistics over a measurement window.
    /// Call <see cref="StopBenchmark"/> and then <see cref="DumpBenchmark"/>
    /// to print results.
    /// </summary>
    public void StartBenchmark()
    {
        _benchmarkRunning = true;
        _benchmarkStart = Stopwatch.GetTimestamp();
        _benchmarkFrames = 0;
        _benchmarkSumMs = 0;
        _benchmarkPeakMs = 0;
        _benchmarkStutters = 0;
        _benchmarkGcStart0 = GC.CollectionCount(0);
        _benchmarkGcStart1 = GC.CollectionCount(1);
        _benchmarkGcStart2 = GC.CollectionCount(2);
        _benchmarkGcDelta0 = 0;
        _benchmarkGcDelta1 = 0;
        _benchmarkGcDelta2 = 0;
        Console.WriteLine("[Benchmark] Started.");
    }

    /// <summary>Stops the benchmark measurement.</summary>
    public void StopBenchmark()
    {
        _benchmarkRunning = false;
        Console.WriteLine($"[Benchmark] Stopped after {_benchmarkFrames} frames.");
    }

    /// <summary>Prints benchmark results to the console.</summary>
    public void DumpBenchmark()
    {
        double durationMs = _benchmarkSumMs;
        double durationSec = durationMs / 1000.0;
        double avgMs = _benchmarkFrames > 0 ? _benchmarkSumMs / _benchmarkFrames : 0;
        double avgFps = avgMs > 0 ? 1000.0 / avgMs : 0;
        double stutterPct = _benchmarkFrames > 0
            ? (_benchmarkStutters * 100.0 / _benchmarkFrames) : 0;

        Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
        Console.WriteLine("║          Stride.Avalonia — Benchmark Results            ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  Duration            : {durationSec,8:F2} s                  ║");
        Console.WriteLine($"║  Frames              : {_benchmarkFrames,8}                  ║");
        Console.WriteLine($"║  Average FPS         : {avgFps,8:F1}                  ║");
        Console.WriteLine($"║  Avg frame time      : {avgMs,8:F3} ms                ║");
        Console.WriteLine($"║  Peak frame time     : {_benchmarkPeakMs,8:F3} ms                ║");
        Console.WriteLine($"║  Stutters (>2x avg)  : {_benchmarkStutters,8}                  ║");
        Console.WriteLine($"║  Stutter rate        : {stutterPct,7:F2}%                   ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  GC gen0 collections : {_benchmarkGcDelta0,8}                  ║");
        Console.WriteLine($"║  GC gen1 collections : {_benchmarkGcDelta1,8}                  ║");
        Console.WriteLine($"║  GC gen2 collections : {_benchmarkGcDelta2,8}                  ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
    }

    /// <summary>High-resolution timer helper.</summary>
    internal static double ElapsedMs(long startTimestamp) =>
        Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
}
