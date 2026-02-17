using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Primitives;
using global::Avalonia.Controls.Templates;
using global::Avalonia.Input;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using global::Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Stride.Engine;
using System.Collections.ObjectModel;
using Stride.Avalonia.Editor.Services;

namespace Stride.Avalonia.Editor.Views;

/// <summary>
/// Represents a node in the scene/entity tree hierarchy.
/// The root node represents the Scene itself.
/// </summary>
public class SceneNode
{
    public string Name { get; set; } = "";
    public Entity? Entity { get; set; }
    public bool IsScene { get; set; }
    public ObservableCollection<SceneNode> Children { get; set; } = new();
}

/// <summary>
/// Left-docked hierarchy view.  The root node is the Scene,
/// and all entities appear as a nested tree below it.
/// Clicking a node selects the entity in <see cref="EditorSelectionService"/>.
/// </summary>
public class HierarchyView : UserControl, IEditorView
{
    public string Title => "Hierarchy";
    public EditorDock Dock => EditorDock.Left;
    Control IEditorView.Content => this;

    private const int PanelWidth = 260;

    private readonly Scene _scene;
    private readonly EditorSelectionService _selection;
    private readonly TreeView _treeView;
    private readonly ObservableCollection<SceneNode> _rootNodes = new();
    private readonly TextBlock _countLabel;
    private int _lastHash = -1;

    public HierarchyView(Scene scene, EditorSelectionService selection)
    {
        _scene = scene;
        _selection = selection;

        _treeView = new TreeView
        {
            ItemsSource = _rootNodes,
            ItemTemplate = BuildTemplate(),
            Background = Brushes.Transparent,
            Margin = new Thickness(0, 4),
        };

        _treeView.SelectionChanged += OnTreeSelectionChanged;

        _countLabel = new TextBlock
        {
            Text = "0 entities",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140)),
            Margin = new Thickness(12, 4),
        };

        var header = BuildHeader();
        DockPanel.SetDock(header, global::Avalonia.Controls.Dock.Top);

        var footer = BuildFooter();
        DockPanel.SetDock(footer, global::Avalonia.Controls.Dock.Bottom);

        var scroll = new ScrollViewer
        {
            Content = _treeView,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(220, 30, 30, 30)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = new DockPanel
            {
                LastChildFill = true,
                Children = { header, footer, scroll },
            },
        };

        RefreshTree();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => RefreshTree();
        timer.Start();
    }

    // ── Template ──────────────────────────────────────────

    private static ITreeDataTemplate BuildTemplate()
    {
        return new FuncTreeDataTemplate<SceneNode>(
            (node, _) =>
            {
                var icon = new SymbolIcon
                {
                    Symbol = node.IsScene ? Symbol.Globe : Symbol.Document,
                    FontSize = 14,
                    Foreground = node.IsScene
                        ? new SolidColorBrush(Color.FromRgb(100, 160, 230))
                        : new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var label = new TextBlock
                {
                    Text = node.Name,
                    FontSize = 13,
                    FontWeight = node.IsScene ? FontWeight.Bold : FontWeight.Normal,
                    Foreground = node.IsScene
                        ? new SolidColorBrush(Color.FromRgb(100, 160, 230))
                        : Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                return new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Children = { icon, label },
                };
            },
            node => node.Children.Count > 0 ? node.Children : null);
    }

    // ── Selection ─────────────────────────────────────────

    private void OnTreeSelectionChanged(object? sender, global::Avalonia.Controls.SelectionChangedEventArgs e)
    {
        if (_treeView.SelectedItem is SceneNode node && !node.IsScene)
            _selection.SelectedEntity = node.Entity;
        else
            _selection.SelectedEntity = null;
    }

    // ── Header / Footer ──────────────────────────────────

    private static Control BuildHeader()
    {
        return new StackPanel
        {
            Margin = new Thickness(0, 8, 0, 0),
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = "Hierarchy",
                    FontSize = 20,
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 160, 230)),
                    Margin = new Thickness(12, 8, 12, 2),
                },
                new Separator { Margin = new Thickness(12, 4) },
            },
        };
    }

    private Control BuildFooter()
    {
        return new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
            Padding = new Thickness(0, 4),
            Child = _countLabel,
        };
    }

    // ── Tree refresh ─────────────────────────────────────

    private void RefreshTree()
    {
        int hash = ComputeHash();
        if (hash == _lastHash) return;
        _lastHash = hash;

        _rootNodes.Clear();

        var sceneNode = new SceneNode
        {
            Name = string.IsNullOrWhiteSpace(_scene.Name) ? "Scene" : _scene.Name,
            IsScene = true,
        };

        // Collect all child entities to exclude from root
        var childEntities = new HashSet<Entity>();
        foreach (var entity in _scene.Entities)
            CollectChildrenRecursive(entity, childEntities);

        int totalCount = 0;
        foreach (var entity in _scene.Entities)
        {
            if (childEntities.Contains(entity)) continue;
            sceneNode.Children.Add(BuildNode(entity, ref totalCount));
        }

        _rootNodes.Add(sceneNode);
        _countLabel.Text = $"{totalCount} {(totalCount == 1 ? "entity" : "entities")}";
    }

    private static void CollectChildrenRecursive(Entity entity, HashSet<Entity> children)
    {
        foreach (var childTransform in entity.Transform.Children)
        {
            children.Add(childTransform.Entity);
            CollectChildrenRecursive(childTransform.Entity, children);
        }
    }

    private static SceneNode BuildNode(Entity entity, ref int count)
    {
        count++;
        var node = new SceneNode
        {
            Name = string.IsNullOrWhiteSpace(entity.Name) ? "(unnamed)" : entity.Name,
            Entity = entity,
        };

        foreach (var childTransform in entity.Transform.Children)
            node.Children.Add(BuildNode(childTransform.Entity, ref count));

        return node;
    }

    private int ComputeHash()
    {
        var h = new HashCode();
        h.Add(_scene.Entities.Count);
        foreach (var entity in _scene.Entities)
        {
            h.Add(entity.Name);
            h.Add(entity.Transform.Children.Count);
        }
        return h.ToHashCode();
    }
}
