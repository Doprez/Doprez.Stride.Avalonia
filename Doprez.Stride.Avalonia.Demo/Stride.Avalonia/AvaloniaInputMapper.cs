using global::Avalonia.Input;

using StrideKeys = Stride.Input.Keys;
using StrideMouseButton = Stride.Input.MouseButton;

namespace Stride.Avalonia;

/// <summary>
/// Maps Stride input enums to Avalonia input enums.
/// </summary>
public static class AvaloniaInputMapper
{
    /// <summary>Maps a Stride <see cref="StrideMouseButton"/> to an Avalonia <see cref="MouseButton"/>.</summary>
    public static MouseButton MapMouseButton(StrideMouseButton button) => button switch
    {
        StrideMouseButton.Left   => MouseButton.Left,
        StrideMouseButton.Right  => MouseButton.Right,
        StrideMouseButton.Middle => MouseButton.Middle,
        _ => MouseButton.None,
    };

    /// <summary>
    /// Builds <see cref="RawInputModifiers"/> from the current Stride keyboard state.
    /// </summary>
    public static RawInputModifiers GetModifiers(Stride.Input.InputManager input)
    {
        var mods = RawInputModifiers.None;
        var kb = input.Keyboard;
        if (kb == null) return mods;

        var down = kb.DownKeys;
        if (down.Contains(StrideKeys.LeftShift) || down.Contains(StrideKeys.RightShift))
            mods |= RawInputModifiers.Shift;
        if (down.Contains(StrideKeys.LeftCtrl) || down.Contains(StrideKeys.RightCtrl))
            mods |= RawInputModifiers.Control;
        if (down.Contains(StrideKeys.LeftAlt) || down.Contains(StrideKeys.RightAlt))
            mods |= RawInputModifiers.Alt;

        return mods;
    }

    /// <summary>
    /// Adds mouse-button flags to modifiers based on currently held buttons.
    /// </summary>
    public static RawInputModifiers AddMouseButtonModifiers(
        RawInputModifiers mods, Stride.Input.IMouseDevice mouse)
    {
        var down = mouse.DownButtons;
        if (down.Contains(StrideMouseButton.Left))
            mods |= RawInputModifiers.LeftMouseButton;
        if (down.Contains(StrideMouseButton.Right))
            mods |= RawInputModifiers.RightMouseButton;
        if (down.Contains(StrideMouseButton.Middle))
            mods |= RawInputModifiers.MiddleMouseButton;
        return mods;
    }

    /// <summary>
    /// Maps a Stride key to an Avalonia <see cref="Key"/>.
    /// Returns <see cref="Key.None"/> for unmapped keys.
    /// </summary>
    public static Key MapKey(StrideKeys strideKey) => strideKey switch
    {
        // Letters
        StrideKeys.A => Key.A, StrideKeys.B => Key.B, StrideKeys.C => Key.C,
        StrideKeys.D => Key.D, StrideKeys.E => Key.E, StrideKeys.F => Key.F,
        StrideKeys.G => Key.G, StrideKeys.H => Key.H, StrideKeys.I => Key.I,
        StrideKeys.J => Key.J, StrideKeys.K => Key.K, StrideKeys.L => Key.L,
        StrideKeys.M => Key.M, StrideKeys.N => Key.N, StrideKeys.O => Key.O,
        StrideKeys.P => Key.P, StrideKeys.Q => Key.Q, StrideKeys.R => Key.R,
        StrideKeys.S => Key.S, StrideKeys.T => Key.T, StrideKeys.U => Key.U,
        StrideKeys.V => Key.V, StrideKeys.W => Key.W, StrideKeys.X => Key.X,
        StrideKeys.Y => Key.Y, StrideKeys.Z => Key.Z,

        // Digits
        StrideKeys.D0 => Key.D0, StrideKeys.D1 => Key.D1, StrideKeys.D2 => Key.D2,
        StrideKeys.D3 => Key.D3, StrideKeys.D4 => Key.D4, StrideKeys.D5 => Key.D5,
        StrideKeys.D6 => Key.D6, StrideKeys.D7 => Key.D7, StrideKeys.D8 => Key.D8,
        StrideKeys.D9 => Key.D9,

        // Numpad
        StrideKeys.NumPad0 => Key.NumPad0, StrideKeys.NumPad1 => Key.NumPad1,
        StrideKeys.NumPad2 => Key.NumPad2, StrideKeys.NumPad3 => Key.NumPad3,
        StrideKeys.NumPad4 => Key.NumPad4, StrideKeys.NumPad5 => Key.NumPad5,
        StrideKeys.NumPad6 => Key.NumPad6, StrideKeys.NumPad7 => Key.NumPad7,
        StrideKeys.NumPad8 => Key.NumPad8, StrideKeys.NumPad9 => Key.NumPad9,

        // Function keys
        StrideKeys.F1  => Key.F1,  StrideKeys.F2  => Key.F2,
        StrideKeys.F3  => Key.F3,  StrideKeys.F4  => Key.F4,
        StrideKeys.F5  => Key.F5,  StrideKeys.F6  => Key.F6,
        StrideKeys.F7  => Key.F7,  StrideKeys.F8  => Key.F8,
        StrideKeys.F9  => Key.F9,  StrideKeys.F10 => Key.F10,
        StrideKeys.F11 => Key.F11, StrideKeys.F12 => Key.F12,

        // Navigation
        StrideKeys.Left  => Key.Left,  StrideKeys.Right => Key.Right,
        StrideKeys.Up    => Key.Up,    StrideKeys.Down  => Key.Down,
        StrideKeys.Home  => Key.Home,  StrideKeys.End   => Key.End,
        StrideKeys.PageUp   => Key.PageUp,
        StrideKeys.PageDown => Key.PageDown,

        // Editing
        StrideKeys.Back   => Key.Back,
        StrideKeys.Delete => Key.Delete,
        StrideKeys.Insert => Key.Insert,
        StrideKeys.Enter  => Key.Return,
        StrideKeys.Tab    => Key.Tab,
        StrideKeys.Space  => Key.Space,
        StrideKeys.Escape => Key.Escape,

        // Modifiers
        StrideKeys.LeftShift  => Key.LeftShift,
        StrideKeys.RightShift => Key.RightShift,
        StrideKeys.LeftCtrl   => Key.LeftCtrl,
        StrideKeys.RightCtrl  => Key.RightCtrl,
        StrideKeys.LeftAlt    => Key.LeftAlt,
        StrideKeys.RightAlt   => Key.RightAlt,

        // Punctuation / symbols
        StrideKeys.OemComma     => Key.OemComma,
        StrideKeys.OemPeriod    => Key.OemPeriod,
        StrideKeys.OemMinus     => Key.OemMinus,
        StrideKeys.OemPlus      => Key.OemPlus,
        StrideKeys.OemQuestion  => Key.OemQuestion,
        StrideKeys.OemTilde     => Key.OemTilde,
        StrideKeys.OemOpenBrackets  => Key.OemOpenBrackets,
        StrideKeys.OemCloseBrackets => Key.OemCloseBrackets,
        StrideKeys.OemPipe      => Key.OemPipe,
        StrideKeys.OemQuotes    => Key.OemQuotes,
        StrideKeys.OemSemicolon => Key.OemSemicolon,
        StrideKeys.OemBackslash => Key.OemBackslash,

        _ => Key.None,
    };
}
