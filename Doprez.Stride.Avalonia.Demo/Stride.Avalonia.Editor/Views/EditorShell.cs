using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using Dock.Avalonia.Controls;
using Dock.Model.Avalonia;
using Dock.Model.Avalonia.Controls;
using Dock.Model.Core;
using Stride.Avalonia.Editor.Services;

using DockAlignment = Dock.Model.Core.Alignment;
using DockOrientation = Dock.Model.Core.Orientation;

namespace Stride.Avalonia.Editor.Views;

/// <summary>
/// The top-level Avalonia control that composes all <see cref="IEditorView"/>
/// instances into a dockable layout using wieslawsoltes/Dock.
/// Views are registered via <see cref="AddView"/> before the shell is attached
/// to the visual tree; the dock layout is built lazily on first attach.
/// Floating windows are disabled — only split/tab docking is allowed.
/// </summary>
public class EditorShell : UserControl
{
    private readonly List<IEditorView> _views = new();
    private bool _layoutBuilt;
    private Border? _viewportBorder;
    private ViewportBoundsService? _viewportBoundsService;

    public IReadOnlyList<IEditorView> Views => _views;

    public EditorShell(ViewportBoundsService? viewportBoundsService = null)
    {
        _viewportBoundsService = viewportBoundsService;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
    }

    /// <summary>
    /// Registers a view to be placed in the dock layout.
    /// Must be called before the shell is attached to the visual tree.
    /// </summary>
    public void AddView(IEditorView view)
    {
        _views.Add(view);
    }

    /// <summary>
    /// Removes a previously registered view. Only effective before layout is built.
    /// </summary>
    public bool RemoveView(IEditorView view)
    {
        return _views.Remove(view);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (!_layoutBuilt)
        {
            BuildDockLayout();
            _layoutBuilt = true;
        }
    }

    // ──────────────────────────────────────────────
    //  Dock Layout Construction
    // ──────────────────────────────────────────────

    private void BuildDockLayout()
    {
        var factory = new Factory();

        // Allow split/tab docking but disable floating windows
        var dockOps = DockOperationMask.Left | DockOperationMask.Right
                    | DockOperationMask.Top | DockOperationMask.Bottom
                    | DockOperationMask.Fill;

        // Group views by dock position
        var topViews    = _views.Where(v => v.Dock == EditorDock.Top).ToList();
        var leftViews   = _views.Where(v => v.Dock == EditorDock.Left).ToList();
        var rightViews  = _views.Where(v => v.Dock == EditorDock.Right).ToList();
        var bottomViews = _views.Where(v => v.Dock == EditorDock.Bottom).ToList();

        // Helper: wrap an IEditorView as a Dock Tool dockable
        Tool MakeTool(IEditorView view) => new()
        {
            Id = view.Title.Replace(" ", ""),
            Title = view.Title,
            Content = view.Content,
            AllowedDockOperations = dockOps,
        };

        // Helper: build a ToolDock from a list of views, or null if empty
        ToolDock? BuildToolDock(
            string id, DockAlignment alignment, double proportion, List<IEditorView> views)
        {
            if (views.Count == 0) return null;
            var tools = views.Select(v => (IDockable)MakeTool(v)).ToArray();
            return new ToolDock
            {
                Id = id,
                Alignment = alignment,
                Proportion = proportion,
                VisibleDockables = factory.CreateList<IDockable>(tools),
                ActiveDockable = tools[0],
            };
        }

        var leftDock   = BuildToolDock("LeftPane",   DockAlignment.Left,   0.20, leftViews);
        var rightDock  = BuildToolDock("RightPane",  DockAlignment.Right,  0.20, rightViews);
        var bottomDock = BuildToolDock("BottomPane", DockAlignment.Bottom, 0.25, bottomViews);

        // Center viewport — transparent so the 3D scene shows through
        _viewportBorder = new Border
        {
            Background = Brushes.Transparent,
            IsHitTestVisible = false,
        };

        // Track viewport bounds for the scene renderer
        if (_viewportBoundsService != null)
        {
            _viewportBorder.LayoutUpdated += OnViewportLayoutUpdated;
        }

        var viewport = new Document
        {
            Id = "Viewport",
            Title = "Viewport",
            Content = _viewportBorder,
        };

        var documentDock = new DocumentDock
        {
            Id = "Documents",
            IsCollapsable = false,
            CanCreateDocument = false,
            VisibleDockables = factory.CreateList<IDockable>(viewport),
            ActiveDockable = viewport,
        };

        // Vertical column: viewport on top, optional bottom tools
        var centerParts = new List<IDockable> { documentDock };
        if (bottomDock != null)
        {
            centerParts.Add(new ProportionalDockSplitter());
            centerParts.Add(bottomDock);
        }

        var centerColumn = new ProportionalDock
        {
            Orientation = DockOrientation.Vertical,
            VisibleDockables = factory.CreateList<IDockable>(centerParts.ToArray()),
        };

        // Horizontal: left | center | right
        var mainParts = new List<IDockable>();
        if (leftDock != null)
        {
            mainParts.Add(leftDock);
            mainParts.Add(new ProportionalDockSplitter());
        }
        mainParts.Add(centerColumn);
        if (rightDock != null)
        {
            mainParts.Add(new ProportionalDockSplitter());
            mainParts.Add(rightDock);
        }

        var mainLayout = new ProportionalDock
        {
            Orientation = DockOrientation.Horizontal,
            VisibleDockables = factory.CreateList<IDockable>(mainParts.ToArray()),
        };

        var root = factory.CreateRootDock();
        root.VisibleDockables = factory.CreateList<IDockable>(mainLayout);
        root.DefaultDockable = mainLayout;

        factory.InitLayout(root);

        var dockControl = new DockControl
        {
            Factory = factory,
            Layout = root,
        };

        // Outer layout: top-docked views (menu bar) above the DockControl
        var outerLayout = new DockPanel
        {
            LastChildFill = true,
            Background = Brushes.Transparent,
        };

        foreach (var topView in topViews)
        {
            DockPanel.SetDock(topView.Content, global::Avalonia.Controls.Dock.Top);
            outerLayout.Children.Add(topView.Content);
        }

        outerLayout.Children.Add(dockControl);

        Content = outerLayout;
    }

    // ──────────────────────────────────────────────
    //  Viewport Bounds Tracking
    // ──────────────────────────────────────────────

    private void OnViewportLayoutUpdated(object? sender, EventArgs e)
    {
        if (_viewportBorder == null || _viewportBoundsService == null) return;

        // TransformToVisual gives us the viewport position relative to the
        // EditorShell, which fills the entire Avalonia headless window.
        var transform = _viewportBorder.TransformToVisual(this);
        if (transform == null) return;

        var shellSize = Bounds.Size;
        if (shellSize.Width <= 0 || shellSize.Height <= 0) return;

        var topLeft = transform.Value.Transform(new global::Avalonia.Point(0, 0));
        var size = _viewportBorder.Bounds.Size;

        // The viewport is visible when the border is in the visual tree and
        // has a non-zero layout size (collapsed/hidden tabs report 0×0).
        bool isVisible = _viewportBorder.IsVisible
                      && size.Width > 0
                      && size.Height > 0;

        // Normalise to 0–1 so the renderer can scale to any back-buffer size.
        _viewportBoundsService.Update(
            (float)(topLeft.X / shellSize.Width),
            (float)(topLeft.Y / shellSize.Height),
            (float)(size.Width / shellSize.Width),
            (float)(size.Height / shellSize.Height),
            isVisible);
    }
}
