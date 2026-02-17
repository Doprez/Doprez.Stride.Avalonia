using System;
using System.Linq.Expressions;
using System.Reflection;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Input.Raw;

using AvaloniaMouseButton = global::Avalonia.Input.MouseButton;

namespace Stride.Avalonia;

/// <summary>
/// Sends raw input events to Avalonia windows by calling the
/// <c>PlatformImpl.Input</c> callback directly via reflection,
/// bypassing <c>HeadlessWindowExtensions</c> which wraps every
/// call in <c>RunJobsOnImpl</c> — a loop that runs
/// <c>Dispatcher.RunJobs()</c> + <c>ForceRenderTimerTick()</c>
/// up to <b>20 times</b> per event.
/// <para>
/// In a game loop we drive the dispatcher ourselves (one
/// <c>RunJobs()</c> at the end of
/// <see cref="AvaloniaSystem.Update"/>), so the extension-method
/// overhead is pure waste.  For a fullscreen panel that receives
/// <c>MouseMove</c> every frame during camera rotation it is the
/// dominant source of GC pressure and CPU stutter.
/// </para>
/// <para>
/// The internal Avalonia APIs (<c>MouseDevice</c> constructor,
/// <c>RawPointerEventArgs</c> constructor, <c>ITopLevelImpl.Input</c>)
/// are accessed through compiled expression delegates and cached
/// reflection.  The one-time setup cost is amortised over the life
/// of the process; per-call overhead is near-zero.
/// </para>
/// </summary>
internal static class AvaloniaInputBridge
{
    // ── One-time lazy initialisation ─────────────────────────────

    /// <summary>Shared <c>MouseDevice</c> created via internal constructor.</summary>
    private static readonly Lazy<IInputDevice> _mouseDevice = new(CreateMouseDevice);

    /// <summary>Compiled delegate: <c>new RawPointerEventArgs(...)</c>.</summary>
    private static readonly Lazy<Func<IInputDevice, ulong, IInputRoot,
        RawPointerEventType, Point, RawInputModifiers, RawPointerEventArgs>>
        _makePointerArgs = new(CompilePointerCtor);

    /// <summary>Compiled delegate: <c>new RawMouseWheelEventArgs(...)</c>.</summary>
    private static readonly Lazy<Func<IInputDevice, ulong, IInputRoot,
        Point, Vector, RawInputModifiers, RawMouseWheelEventArgs>>
        _makeWheelArgs = new(CompileWheelCtor);

    private static ulong Timestamp => (ulong)Environment.TickCount64;

    // ── Public API ───────────────────────────────────────────────

    public static void MouseMove(Window window, Point position, RawInputModifiers modifiers)
    {
        var callback = GetInputCallback(window);
        if (callback == null) return;

        callback(_makePointerArgs.Value(
            _mouseDevice.Value, Timestamp, (IInputRoot)window,
            RawPointerEventType.Move, position, modifiers));
    }

    public static void MouseDown(Window window, Point position,
        AvaloniaMouseButton button, RawInputModifiers modifiers)
    {
        var type = MapButtonDown(button);
        if (type == RawPointerEventType.Move) return; // unknown button

        var callback = GetInputCallback(window);
        if (callback == null) return;

        callback(_makePointerArgs.Value(
            _mouseDevice.Value, Timestamp, (IInputRoot)window,
            type, position, modifiers));
    }

    public static void MouseUp(Window window, Point position,
        AvaloniaMouseButton button, RawInputModifiers modifiers)
    {
        var type = MapButtonUp(button);
        if (type == RawPointerEventType.Move) return;

        var callback = GetInputCallback(window);
        if (callback == null) return;

        callback(_makePointerArgs.Value(
            _mouseDevice.Value, Timestamp, (IInputRoot)window,
            type, position, modifiers));
    }

    public static void MouseWheel(Window window, Point position,
        Vector delta, RawInputModifiers modifiers)
    {
        var callback = GetInputCallback(window);
        if (callback == null) return;

        callback(_makeWheelArgs.Value(
            _mouseDevice.Value, Timestamp, (IInputRoot)window,
            position, delta, modifiers));
    }

    // ── Input callback (reflection, cached) ──────────────────────

    // Simple single-slot cache.  In practice only one window (the
    // fullscreen panel) receives mouse input continuously.
    private static object? _cachedImpl;
    private static Action<RawInputEventArgs>? _cachedCallback;
    private static PropertyInfo? _inputProp;

    private static Action<RawInputEventArgs>? GetInputCallback(Window window)
    {
        var impl = window.PlatformImpl;
        if (impl == null) return null;

        if (ReferenceEquals(impl, _cachedImpl))
            return _cachedCallback;

        _cachedImpl = impl;

        // HeadlessWindowImpl.Input is public on the concrete type
        // even though ITopLevelImpl.Input is internal.
        _inputProp ??= impl.GetType().GetProperty("Input",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        _cachedCallback = _inputProp?.GetValue(impl) as Action<RawInputEventArgs>;
        return _cachedCallback;
    }

    // ── Reflection: construct MouseDevice ────────────────────────

    private static IInputDevice CreateMouseDevice()
    {
        var pointer = new global::Avalonia.Input.Pointer(0, PointerType.Mouse, true);

        var ctor = typeof(MouseDevice).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance,
            null, [typeof(global::Avalonia.Input.Pointer)], null)
            ?? throw new InvalidOperationException(
                "MouseDevice(Pointer) constructor not found. " +
                "Avalonia internals may have changed.");

        return (IInputDevice)ctor.Invoke([pointer]);
    }

    // ── Compiled delegates for internal constructors ─────────────

    private static Func<IInputDevice, ulong, IInputRoot,
        RawPointerEventType, Point, RawInputModifiers, RawPointerEventArgs>
        CompilePointerCtor()
    {
        var ctor = typeof(RawPointerEventArgs).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance,
            null,
            [typeof(IInputDevice), typeof(ulong), typeof(IInputRoot),
             typeof(RawPointerEventType), typeof(Point), typeof(RawInputModifiers)],
            null)
            ?? throw new InvalidOperationException(
                "RawPointerEventArgs 6-arg constructor not found. " +
                "Avalonia internals may have changed.");

        var p1 = Expression.Parameter(typeof(IInputDevice));
        var p2 = Expression.Parameter(typeof(ulong));
        var p3 = Expression.Parameter(typeof(IInputRoot));
        var p4 = Expression.Parameter(typeof(RawPointerEventType));
        var p5 = Expression.Parameter(typeof(Point));
        var p6 = Expression.Parameter(typeof(RawInputModifiers));

        return Expression.Lambda<Func<IInputDevice, ulong, IInputRoot,
            RawPointerEventType, Point, RawInputModifiers, RawPointerEventArgs>>(
            Expression.New(ctor, p1, p2, p3, p4, p5, p6),
            p1, p2, p3, p4, p5, p6).Compile();
    }

    private static Func<IInputDevice, ulong, IInputRoot,
        Point, Vector, RawInputModifiers, RawMouseWheelEventArgs>
        CompileWheelCtor()
    {
        var ctor = typeof(RawMouseWheelEventArgs).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance,
            null,
            [typeof(IInputDevice), typeof(ulong), typeof(IInputRoot),
             typeof(Point), typeof(Vector), typeof(RawInputModifiers)],
            null)
            ?? throw new InvalidOperationException(
                "RawMouseWheelEventArgs 6-arg constructor not found. " +
                "Avalonia internals may have changed.");

        var p1 = Expression.Parameter(typeof(IInputDevice));
        var p2 = Expression.Parameter(typeof(ulong));
        var p3 = Expression.Parameter(typeof(IInputRoot));
        var p4 = Expression.Parameter(typeof(Point));
        var p5 = Expression.Parameter(typeof(Vector));
        var p6 = Expression.Parameter(typeof(RawInputModifiers));

        return Expression.Lambda<Func<IInputDevice, ulong, IInputRoot,
            Point, Vector, RawInputModifiers, RawMouseWheelEventArgs>>(
            Expression.New(ctor, p1, p2, p3, p4, p5, p6),
            p1, p2, p3, p4, p5, p6).Compile();
    }

    // ── Button mapping ───────────────────────────────────────────

    private static RawPointerEventType MapButtonDown(AvaloniaMouseButton button) => button switch
    {
        AvaloniaMouseButton.Left     => RawPointerEventType.LeftButtonDown,
        AvaloniaMouseButton.Right    => RawPointerEventType.RightButtonDown,
        AvaloniaMouseButton.Middle   => RawPointerEventType.MiddleButtonDown,
        AvaloniaMouseButton.XButton1 => RawPointerEventType.XButton1Down,
        AvaloniaMouseButton.XButton2 => RawPointerEventType.XButton2Down,
        _ => RawPointerEventType.Move,
    };

    private static RawPointerEventType MapButtonUp(AvaloniaMouseButton button) => button switch
    {
        AvaloniaMouseButton.Left     => RawPointerEventType.LeftButtonUp,
        AvaloniaMouseButton.Right    => RawPointerEventType.RightButtonUp,
        AvaloniaMouseButton.Middle   => RawPointerEventType.MiddleButtonUp,
        AvaloniaMouseButton.XButton1 => RawPointerEventType.XButton1Up,
        AvaloniaMouseButton.XButton2 => RawPointerEventType.XButton2Up,
        _ => RawPointerEventType.Move,
    };
}
