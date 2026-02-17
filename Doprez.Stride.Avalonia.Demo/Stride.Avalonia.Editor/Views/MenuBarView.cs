using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using FluentAvalonia.UI.Controls;

namespace Stride.Avalonia.Editor.Views;

/// <summary>
/// Top-docked menu bar with standard File, Edit, View, Help menus.
/// The application name is configurable via the constructor.
/// </summary>
public class MenuBarView : UserControl, IEditorView
{
    public string Title => "Menu Bar";
    public EditorDock Dock => EditorDock.Top;
    Control IEditorView.Content => this;

    private readonly string _appName;

    /// <summary>
    /// Creates the menu bar.
    /// </summary>
    /// <param name="appName">Application name shown in the Help → About menu item. Defaults to "Stride Editor".</param>
    public MenuBarView(string appName = "Stride Editor")
    {
        _appName = appName;

        var menu = new Menu
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Color.FromArgb(240, 35, 35, 35)),
            Foreground = Brushes.White,
            Items =
            {
                BuildFileMenu(),
                BuildEditMenu(),
                BuildViewMenu(),
                BuildHelpMenu(),
            },
        };

        Content = menu;
    }

    // ── File ─────────────────────────────────────────────

    private static MenuItem BuildFileMenu()
    {
        return new MenuItem
        {
            Header = "_File",
            Items =
            {
                WithIcon("_New Scene",  Symbol.New,    new global::Avalonia.Input.KeyGesture(global::Avalonia.Input.Key.N, global::Avalonia.Input.KeyModifiers.Control)),
                WithIcon("_Open Scene", Symbol.Open,   new global::Avalonia.Input.KeyGesture(global::Avalonia.Input.Key.O, global::Avalonia.Input.KeyModifiers.Control)),
                new Separator(),
                WithIcon("_Save",       Symbol.Save,   new global::Avalonia.Input.KeyGesture(global::Avalonia.Input.Key.S, global::Avalonia.Input.KeyModifiers.Control)),
                WithIcon("Save _As...", Symbol.SaveAs),
                new Separator(),
                new MenuItem { Header = "E_xit" },
            },
        };
    }

    // ── Edit ─────────────────────────────────────────────

    private static MenuItem BuildEditMenu()
    {
        return new MenuItem
        {
            Header = "_Edit",
            Items =
            {
                WithIcon("_Undo",  Symbol.Undo,  new global::Avalonia.Input.KeyGesture(global::Avalonia.Input.Key.Z, global::Avalonia.Input.KeyModifiers.Control)),
                WithIcon("_Redo",  Symbol.Redo,  new global::Avalonia.Input.KeyGesture(global::Avalonia.Input.Key.Y, global::Avalonia.Input.KeyModifiers.Control)),
                new Separator(),
                WithIcon("Cu_t",   Symbol.Cut,   new global::Avalonia.Input.KeyGesture(global::Avalonia.Input.Key.X, global::Avalonia.Input.KeyModifiers.Control)),
                WithIcon("_Copy",  Symbol.Copy,  new global::Avalonia.Input.KeyGesture(global::Avalonia.Input.Key.C, global::Avalonia.Input.KeyModifiers.Control)),
                WithIcon("_Paste", Symbol.Paste, new global::Avalonia.Input.KeyGesture(global::Avalonia.Input.Key.V, global::Avalonia.Input.KeyModifiers.Control)),
                new Separator(),
                WithIcon("Select _All", Symbol.SelectAll, new global::Avalonia.Input.KeyGesture(global::Avalonia.Input.Key.A, global::Avalonia.Input.KeyModifiers.Control)),
            },
        };
    }

    // ── View ─────────────────────────────────────────────

    private static MenuItem BuildViewMenu()
    {
        return new MenuItem
        {
            Header = "_View",
            Items =
            {
                WithIcon("_Hierarchy",  Symbol.List),
                WithIcon("_Properties", Symbol.Settings),
                new Separator(),
                WithIcon("_Fullscreen", Symbol.FullScreen, new global::Avalonia.Input.KeyGesture(global::Avalonia.Input.Key.F11)),
            },
        };
    }

    // ── Help ─────────────────────────────────────────────

    private MenuItem BuildHelpMenu()
    {
        return new MenuItem
        {
            Header = "_Help",
            Items =
            {
                WithIcon($"_About {_appName}", Symbol.Help),
            },
        };
    }

    // ── Helper ────────────────────────────────────────────

    private static MenuItem WithIcon(string header, Symbol symbol, global::Avalonia.Input.KeyGesture? gesture = null)
    {
        var item = new MenuItem
        {
            Header = header,
            Icon = new SymbolIcon { Symbol = symbol, FontSize = 16 },
        };
        if (gesture != null)
            item.InputGesture = gesture;
        return item;
    }
}
