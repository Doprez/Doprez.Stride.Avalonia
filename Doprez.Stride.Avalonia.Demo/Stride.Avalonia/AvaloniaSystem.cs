using System.Diagnostics;
using global::Avalonia;
using global::Avalonia.Headless;
using global::Avalonia.Input;
using global::Avalonia.Threading;
using Stride.Core;
using Stride.Core.Annotations;
using Stride.Core.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Input;

using AvaloniaMouseButton = global::Avalonia.Input.MouseButton;
using Matrix = Stride.Core.Mathematics.Matrix;

using AvaloniaPoint  = global::Avalonia.Point;
using AvaloniaVector = global::Avalonia.Vector;
using System.Collections.Generic;

namespace Stride.Avalonia;

/// <summary>
/// Game system that manages the Avalonia headless lifecycle and forwards
/// Stride input events to each <see cref="AvaloniaComponent"/>'s headless window.
/// </summary>
public class AvaloniaSystem : GameSystemBase
{
    private static readonly Logger _log = GlobalLogger.GetLogger(nameof(AvaloniaSystem));
    private InputManager _input = null!;

    // Position deduplication — prevents forwarding redundant mouse moves
    // when the cursor is locked to a fixed point by the camera controller.
    private double _lastFsMouseX = double.NaN;
    private double _lastFsMouseY = double.NaN;

    // ── UI Hz throttle ──────────────────────────────────────────────
    private double _uiAccumulatorMs;

    /// <summary>
    /// Maximum rate (in Hz) at which the Avalonia UI pipeline is updated.
    /// <para>
    /// When set to <c>0</c> (default), the UI runs at the full game
    /// frame rate — identical to previous behaviour.
    /// </para>
    /// <para>
    /// When set to a positive value (e.g. 30), the system accumulates
    /// elapsed time each frame and only runs input processing,
    /// <c>Dispatcher.RunJobs()</c>, and <c>ForceRenderTimerTick()</c>
    /// once the budget (<c>1000 / TargetUiHz</c> ms) has elapsed.
    /// Discrete input events (button press/release, key press/release,
    /// mouse wheel) are always forwarded immediately so that clicks are
    /// never lost; only continuous mouse-move events are deferred.
    /// </para>
    /// </summary>
    public int TargetUiHz { get; set; } = 0;

    internal Matrix CameraView { get; set; }
    internal Matrix CameraViewProjection { get; set; }
    internal bool HasCameraMatrices { get; set; }

    public AvaloniaSystem([NotNull] IServiceRegistry registry) : base(registry) { }

    protected override void LoadContent()
    {
        _input = Services.GetService<InputManager>()!;
        AvaloniaApp.EnsureInitialized();

        if (_input.Keyboard is ITextInputDevice textDevice)
            textDevice.EnabledTextInput();

        Enabled = true;
        Visible = false;
    }

    public override void Update(GameTime gameTime)
    {
        var metrics = AvaloniaRenderMetrics.Instance;
        metrics.ResetFrame();
        var updateStart = Stopwatch.GetTimestamp();
        using var _profileUpdate = Profiler.Begin(AvaloniaProfilingKeys.Update);

        // ── Hz throttle ──
        // When TargetUiHz > 0, accumulate elapsed time and skip the
        // full Avalonia pipeline until the budget has elapsed.
        // Discrete input (buttons, keys, wheel) is forwarded
        // immediately so clicks are never lost — only continuous
        // mouse-move events and the compositor tick are deferred.
        _uiAccumulatorMs += gameTime.Elapsed.TotalMilliseconds;
        bool hzThrottled = false;
        if (TargetUiHz > 0)
        {
            double budgetMs = 1000.0 / TargetUiHz;
            if (_uiAccumulatorMs < budgetMs)
                hzThrottled = true;
            else
                _uiAccumulatorMs -= budgetMs; // consume one tick's worth
        }
        else
        {
            _uiAccumulatorMs = 0; // no throttle — keep accumulator at zero
        }

        // ── Collect components ──
        long t0 = Stopwatch.GetTimestamp();
        using (Profiler.Begin(AvaloniaProfilingKeys.CollectComponentsUpdate))
        {
            // (profiled below)
        }
        var components = CollectComponents();
        metrics.UpdateCollectMs = AvaloniaRenderMetrics.ElapsedMs(t0);

        if (components.Count == 0)
        {
            metrics.UpdateTotalMs = AvaloniaRenderMetrics.ElapsedMs(updateStart);
            return;
        }

        bool hasInput = HasInputThisFrame();
        bool hasMouseActivity = _input.Mouse != null &&
            (_input.Mouse.PressedButtons.Count > 0 ||
             _input.Mouse.ReleasedButtons.Count > 0 ||
             _input.MouseWheelDelta != 0);

        // When Hz-throttled, still forward discrete input (buttons,
        // keys, wheel) so clicks are never lost.  Skip continuous
        // mouse-move events and the compositor tick.
        if (hzThrottled && !hasInput && !hasMouseActivity)
        {
            // Nothing interesting happened and we're under budget — skip.
            metrics.SkippedRenderTicks++;
            metrics.UpdateTotalMs = AvaloniaRenderMetrics.ElapsedMs(updateStart);
            return;
        }
        // When a mouse button is held (e.g. right-click camera rotation)
        // the cursor movement is owned by the camera controller, not by
        // the UI.  Skip forwarding mouse events entirely in that case to
        // avoid per-frame RawPointerEventArgs allocations and Avalonia
        // hit-testing overhead that cause GC stutter.
        // This optimization only applies to world-space panels.
        // Fullscreen panels use position-deduplication instead (see below).

        // ── Input processing ──
        long t1 = Stopwatch.GetTimestamp();
        using (Profiler.Begin(AvaloniaProfilingKeys.InputProcessing))
        {
            foreach (var comp in components)
            {
                if (!comp.Enabled || comp.Page == null || !comp.Page.IsReady) continue;

                // Increment EffectTime for custom-effect panels
                if (!string.IsNullOrEmpty(comp.CustomEffectName))
                    comp.EffectTime += (float)gameTime.Elapsed.TotalSeconds;

                // ContinuousRedraw: mark dirty every frame for animated content
                if (comp.ContinuousRedraw)
                    comp.Page.MarkDirty();

                if (comp.IsFullScreen)
                {
                    // Check whether the absolute cursor position changed.
                    // During camera rotation the camera controller calls
                    // LockMousePosition() which clamps Position to the
                    // centre of the window.  Delta is non-zero (camera
                    // rotates) but Position stays constant.
                    // Forwarding a MouseMove to Avalonia when the position
                    // hasn't changed allocates a RawPointerEventArgs for
                    // nothing and — if we also mark dirty — triggers a
                    // CaptureRenderedFrame() that puts a ~3.5 MB bitmap
                    // on the Large-Object Heap every frame, causing
                    // frequent gen-2 GC pauses (stutter).
                    bool posMoved = false;
                    if (_input.Mouse != null)
                    {
                        var surfSize = GraphicsDevice.Presenter.BackBuffer.Size;
                        double mx = _input.Mouse.Position.X * surfSize.Width;
                        double my = _input.Mouse.Position.Y * surfSize.Height;
                        posMoved = mx != _lastFsMouseX || my != _lastFsMouseY;
                        if (posMoved || hasMouseActivity)
                        {
                            _lastFsMouseX = mx;
                            _lastFsMouseY = my;
                        }
                    }

                    // Forward when there is actual UI-relevant activity:
                    //  • Button press / release / wheel  (hasMouseActivity)
                    //  • Cursor moved to a new position  (posMoved)
                    // Skip when position is locked and no buttons changed —
                    // the user is rotating the camera, not interacting with UI.
                    bool forwardMouse = hasMouseActivity || posMoved;
                    if (forwardMouse || hasInput)
                        ProcessInputFullscreen(comp, forwardMouse);

                    // Mark dirty on discrete input or genuine cursor movement —
                    // NOT on every frame during right-click camera drag.
                    if (hasInput || hasMouseActivity || posMoved)
                        comp.Page.MarkDirty();
                }
                else if (HasCameraMatrices && hasMouseActivity)
                {
                    bool hit = ProcessInputWorldSpace(comp);
                    // Only mark dirty if the mouse actually hit THIS panel
                    if (hit)
                        comp.Page.MarkDirty();
                }
            }
        }
        metrics.InputProcessingMs = AvaloniaRenderMetrics.ElapsedMs(t1);

        // ── Dispatcher jobs + headless render ──
        // RunJobs() processes queued UI updates (e.g. DebugPanel text changes).
        // Always called so bindings, timers, and animation scheduling work.
        // ForceRenderTimerTick() is only called when at least one panel is
        // dirty — skipping it avoids the full Skia compositor render pass
        // (layout, measure, visual tree walk) on frames where nothing changed.
        long t2 = Stopwatch.GetTimestamp();
        using (Profiler.Begin(AvaloniaProfilingKeys.DispatcherJobs))
        {
            Dispatcher.UIThread.RunJobs();

            // Check whether any panel actually needs re-rendering.
            bool anyDirty = false;
            foreach (var comp in components)
            {
                if (comp.Enabled && comp.Page != null && comp.Page.IsDirty)
                {
                    anyDirty = true;
                    break;
                }
            }

            if (anyDirty)
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            }
            else
            {
                metrics.SkippedRenderTicks++;
            }
        }
        metrics.DispatcherJobsMs = AvaloniaRenderMetrics.ElapsedMs(t2);

        metrics.UpdateTotalMs = AvaloniaRenderMetrics.ElapsedMs(updateStart);
    }

    // ──────────────────────────────────────────────
    //  Fullscreen Input
    // ──────────────────────────────────────────────

    private void ProcessInputFullscreen(AvaloniaComponent comp, bool forwardMouse)
    {
        var window = comp.Page!.Window;
        if (window == null) return;

        var mods = AvaloniaInputMapper.GetModifiers(_input);
        if (_input.Mouse != null)
            mods = AvaloniaInputMapper.AddMouseButtonModifiers(mods, _input.Mouse);

        if (forwardMouse && _input.Mouse != null)
        {
            var mouse = _input.Mouse;
            var surfSize = GraphicsDevice.Presenter.BackBuffer.Size;

            double mx = mouse.Position.X * surfSize.Width;
            double my = mouse.Position.Y * surfSize.Height;
            var pos = new AvaloniaPoint(mx, my);

            AvaloniaInputBridge.MouseMove(window, pos, mods);

            if (mouse.PressedButtons.Count > 0)
            {
                foreach (var btn in mouse.PressedButtons)
                {
                    var avBtn = AvaloniaInputMapper.MapMouseButton(btn);
                    if (avBtn != AvaloniaMouseButton.None)
                        AvaloniaInputBridge.MouseDown(window, pos, avBtn, mods);
                }
            }
            if (mouse.ReleasedButtons.Count > 0)
            {
                foreach (var btn in mouse.ReleasedButtons)
                {
                    var avBtn = AvaloniaInputMapper.MapMouseButton(btn);
                    if (avBtn != AvaloniaMouseButton.None)
                        AvaloniaInputBridge.MouseUp(window, pos, avBtn, mods);
                }
            }

            float wheelDelta = _input.MouseWheelDelta;
            if (wheelDelta != 0)
                AvaloniaInputBridge.MouseWheel(window, pos, new AvaloniaVector(0, wheelDelta), mods);
        }

        ProcessKeyboardInput(comp, mods);
    }

    // ──────────────────────────────────────────────
    //  World-Space Input
    // ──────────────────────────────────────────────

    /// <summary>
    /// Returns true if the mouse cursor is over this panel (i.e. input was delivered).
    /// </summary>
    private bool ProcessInputWorldSpace(AvaloniaComponent comp)
    {
        var window = comp.Page!.Window;
        if (window == null || _input.Mouse == null) return false;

        var mouse = _input.Mouse;
        var mods = AvaloniaInputMapper.GetModifiers(_input);
        mods = AvaloniaInputMapper.AddMouseButtonModifiers(mods, mouse);

        bool onPanel = comp.TryScreenToPanel(
            mouse.Position, CameraViewProjection, CameraView,
            out var panelPixel);

        if (onPanel)
        {
            var pos = new AvaloniaPoint(panelPixel.X, panelPixel.Y);
            AvaloniaInputBridge.MouseMove(window, pos, mods);

            if (mouse.PressedButtons.Count > 0)
            {
                foreach (var btn in mouse.PressedButtons)
                {
                    var avBtn = AvaloniaInputMapper.MapMouseButton(btn);
                    if (avBtn != AvaloniaMouseButton.None)
                        AvaloniaInputBridge.MouseDown(window, pos, avBtn, mods);
                }
            }
            if (mouse.ReleasedButtons.Count > 0)
            {
                foreach (var btn in mouse.ReleasedButtons)
                {
                    var avBtn = AvaloniaInputMapper.MapMouseButton(btn);
                    if (avBtn != AvaloniaMouseButton.None)
                        AvaloniaInputBridge.MouseUp(window, pos, avBtn, mods);
                }
            }

            float wheelDelta = _input.MouseWheelDelta;
            if (wheelDelta != 0)
                AvaloniaInputBridge.MouseWheel(window, pos, new AvaloniaVector(0, wheelDelta), mods);
        }

        ProcessKeyboardInput(comp, mods);
        return onPanel;
    }

    // ──────────────────────────────────────────────
    //  Keyboard Input
    // ──────────────────────────────────────────────

    private void ProcessKeyboardInput(AvaloniaComponent comp, RawInputModifiers mods)
    {
        var window = comp.Page?.Window;
        if (window == null || _input.Keyboard == null) return;

        var keyboard = _input.Keyboard;

        if (keyboard.PressedKeys.Count > 0)
        {
            foreach (var key in keyboard.PressedKeys)
            {
                var avKey = AvaloniaInputMapper.MapKey(key);
                if (avKey != Key.None)
                    window.KeyPress(avKey, mods, PhysicalKey.None, null);
            }
        }

        if (keyboard.ReleasedKeys.Count > 0)
        {
            foreach (var key in keyboard.ReleasedKeys)
            {
                var avKey = AvaloniaInputMapper.MapKey(key);
                if (avKey != Key.None)
                    window.KeyRelease(avKey, mods, PhysicalKey.None, null);
            }
        }

        if (_input.Events.Count > 0)
        {
            for (int i = 0; i < _input.Events.Count; i++)
            {
                if (_input.Events[i] is TextInputEvent textEvent && !string.IsNullOrEmpty(textEvent.Text))
                    window.KeyTextInput(textEvent.Text);
            }
        }
    }

    // ──────────────────────────────────────────────
    //  Component Collection
    // ──────────────────────────────────────────────

    private readonly List<AvaloniaComponent> _componentCache = new();

    private bool HasInputThisFrame()
    {
        var mouse = _input.Mouse;
        var keyboard = _input.Keyboard;

        if (mouse != null)
        {
            if (mouse.PressedButtons.Count > 0 || mouse.ReleasedButtons.Count > 0)
                return true;
            if (_input.MouseWheelDelta != 0)
                return true;
        }

        if (keyboard != null)
        {
            if (keyboard.PressedKeys.Count > 0 || keyboard.ReleasedKeys.Count > 0)
                return true;
        }

        // NOTE: _input.Events is intentionally NOT checked here.
        // It contains PointerEvents (mouse move) which fire every frame
        // the cursor moves. Treating those as "input" would mark the
        // fullscreen panel dirty every frame → CaptureRenderedFrame()
        // allocates 2 WriteableBitmaps per call, causing GC stutter.
        // All meaningful discrete input (keys, buttons, wheel) is
        // already covered by the checks above.

        return false;
    }

    private List<AvaloniaComponent> CollectComponents()
    {
        _componentCache.Clear();
        var sceneSystem = Services.GetService<SceneSystem>();
        if (sceneSystem?.SceneInstance == null) return _componentCache;
        CollectFromScene(sceneSystem.SceneInstance.RootScene, _componentCache);
        return _componentCache;
    }

    private static void CollectFromScene(Scene? scene, List<AvaloniaComponent> result)
    {
        if (scene == null) return;
        foreach (var entity in scene.Entities)
        {
            var comp = entity.Get<AvaloniaComponent>();
            if (comp != null)
                result.Add(comp);
        }
        foreach (var child in scene.Children)
            CollectFromScene(child, result);
    }

    protected override void Destroy()
    {
        if (_input?.Keyboard is ITextInputDevice textDevice)
            textDevice.DisableTextInput();

        foreach (var comp in CollectComponents())
            comp.Page?.Dispose();
    }
}
