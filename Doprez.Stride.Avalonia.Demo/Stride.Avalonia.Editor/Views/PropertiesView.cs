using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Primitives;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using global::Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Stride.Engine;
using Stride.Avalonia.Editor.Controls;
using Stride.Avalonia.Editor.Controls.Serialization;
using Stride.Avalonia.Editor.Services;

namespace Stride.Avalonia.Editor.Views;

/// <summary>
/// Right-docked panel that shows the properties of the selected entity
/// and lists each component with its public property values.
/// <para>
/// Uses <see cref="ComponentInspector"/> to discover editable properties
/// via reflection and Stride attributes, then delegates to
/// <see cref="PropertyControlFactory"/> to create type-appropriate
/// Avalonia editor controls (text boxes, sliders, colour pickers, etc.).
/// </para>
/// <para>
/// Components that implement <see cref="IEditableComponent"/> can override
/// which properties appear and how they are grouped.
/// </para>
/// </summary>
public class PropertiesView : UserControl, IEditorView
{
    public string Title => "Properties";
    public EditorDock Dock => EditorDock.Right;
    Control IEditorView.Content => this;

    private const int PanelWidth = 300;

    private readonly EditorSelectionService _selection;
    private readonly StackPanel _propsStack;
    private readonly TextBlock _headerLabel;
    private Entity? _currentEntity;

    // Track transform editors so the 500ms refresh can update values
    // without rebuilding the entire tree.
    private Vector3Editor? _posEditor;
    private Vector3Editor? _rotEditor;
    private Vector3Editor? _scaleEditor;

    public PropertiesView(EditorSelectionService selection)
    {
        _selection = selection;

        _headerLabel = new TextBlock
        {
            Text = "No Selection",
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(100, 160, 230)),
            Margin = new Thickness(12, 8, 12, 2),
            TextWrapping = TextWrapping.Wrap,
        };

        _propsStack = new StackPanel
        {
            Spacing = 2,
            Margin = new Thickness(0, 4),
        };

        var header = new StackPanel
        {
            Margin = new Thickness(0, 8, 0, 0),
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = "Properties",
                    FontSize = 20,
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 160, 230)),
                    Margin = new Thickness(12, 8, 12, 2),
                },
                new Separator { Margin = new Thickness(12, 4) },
                _headerLabel,
                new Separator { Margin = new Thickness(12, 4) },
            },
        };
        DockPanel.SetDock(header, global::Avalonia.Controls.Dock.Top);

        var scroll = new ScrollViewer
        {
            Content = _propsStack,
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
                Children = { header, scroll },
            },
        };

        _selection.SelectionChanged += (_, _) =>
            Dispatcher.UIThread.Post(RebuildProperties);

        // Periodic refresh for transform changes (driven by game loop)
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => RefreshTransform();
        timer.Start();
    }

    // ── Full rebuild (on selection change) ────────────────

    private void RebuildProperties()
    {
        _currentEntity = _selection.SelectedEntity;
        _propsStack.Children.Clear();
        _posEditor = _rotEditor = _scaleEditor = null;

        if (_currentEntity == null)
        {
            _headerLabel.Text = "No Selection";
            return;
        }

        _headerLabel.Text = string.IsNullOrWhiteSpace(_currentEntity.Name)
            ? "(unnamed)"
            : _currentEntity.Name;

        // ── Transform section (always shown first) ───────
        AddSection("Transform");
        BuildTransformEditors();

        // ── Component sections ───────────────────────────
        foreach (var component in _currentEntity.Components)
        {
            if (component is TransformComponent)
                continue; // already handled above

            var displayName = ComponentInspector.GetDisplayName(component);
            AddSection(displayName);

            var properties = ComponentInspector.GetProperties(component);

            // Group by category if categories are present
            string? currentCategory = null;
            foreach (var prop in properties)
            {
                if (prop.Category is not null && prop.Category != currentCategory)
                {
                    currentCategory = prop.Category;
                    AddCategoryHeader(currentCategory);
                }

                try
                {
                    var row = PropertyControlFactory.CreatePropertyRow(prop, component);
                    _propsStack.Children.Add(row);
                }
                catch
                {
                    AddReadOnlyRow(prop.DisplayName, "(error)");
                }
            }
        }
    }

    // ── Transform editors (editable Vector3 controls) ────

    private void BuildTransformEditors()
    {
        var entity = _currentEntity!;

        // Position
        _posEditor = new Vector3Editor(entity.Transform.Position);
        _posEditor.ValueChanged += (_, v) => entity.Transform.Position = v;
        AddLabelledControl("Position", _posEditor);

        // Rotation (display as degrees)
        var eulerDeg = new Stride.Core.Mathematics.Vector3(
            Stride.Core.Mathematics.MathUtil.RadiansToDegrees(entity.Transform.RotationEulerXYZ.X),
            Stride.Core.Mathematics.MathUtil.RadiansToDegrees(entity.Transform.RotationEulerXYZ.Y),
            Stride.Core.Mathematics.MathUtil.RadiansToDegrees(entity.Transform.RotationEulerXYZ.Z));

        _rotEditor = new Vector3Editor(eulerDeg);
        _rotEditor.ValueChanged += (_, v) =>
        {
            entity.Transform.RotationEulerXYZ = new Stride.Core.Mathematics.Vector3(
                Stride.Core.Mathematics.MathUtil.DegreesToRadians(v.X),
                Stride.Core.Mathematics.MathUtil.DegreesToRadians(v.Y),
                Stride.Core.Mathematics.MathUtil.DegreesToRadians(v.Z));
        };
        AddLabelledControl("Rotation", _rotEditor);

        // Scale
        _scaleEditor = new Vector3Editor(entity.Transform.Scale);
        _scaleEditor.ValueChanged += (_, v) => entity.Transform.Scale = v;
        AddLabelledControl("Scale", _scaleEditor);
    }

    // ── Periodic transform refresh ───────────────────────

    private void RefreshTransform()
    {
        if (_currentEntity == null || !ReferenceEquals(_currentEntity, _selection.SelectedEntity))
            return;

        _posEditor?.SetValue(_currentEntity.Transform.Position);

        var eulerDeg = new Stride.Core.Mathematics.Vector3(
            Stride.Core.Mathematics.MathUtil.RadiansToDegrees(_currentEntity.Transform.RotationEulerXYZ.X),
            Stride.Core.Mathematics.MathUtil.RadiansToDegrees(_currentEntity.Transform.RotationEulerXYZ.Y),
            Stride.Core.Mathematics.MathUtil.RadiansToDegrees(_currentEntity.Transform.RotationEulerXYZ.Z));
        _rotEditor?.SetValue(eulerDeg);

        _scaleEditor?.SetValue(_currentEntity.Transform.Scale);
    }

    // ── UI helpers ───────────────────────────────────────

    private void AddSection(string title)
    {
        var icon = new SymbolIcon
        {
            Symbol = title == "Transform" ? Symbol.Navigation : Symbol.Setting,
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(190, 190, 190)),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var label = new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(190, 190, 190)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _propsStack.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(12, 10, 12, 2),
            Children = { icon, label },
        });
    }

    private void AddCategoryHeader(string category)
    {
        _propsStack.Children.Add(new TextBlock
        {
            Text = category,
            FontSize = 12,
            FontWeight = FontWeight.Medium,
            Foreground = new SolidColorBrush(Color.FromRgb(120, 150, 180)),
            Margin = new Thickness(16, 6, 12, 1),
            FontStyle = FontStyle.Italic,
        });
    }

    private void AddLabelledControl(string label, Control editor)
    {
        var row = new DockPanel
        {
            Margin = new Thickness(16, 2, 8, 2),
        };

        var lbl = new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
            Width = 120,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(lbl, global::Avalonia.Controls.Dock.Left);
        row.Children.Add(lbl);
        row.Children.Add(editor);
        _propsStack.Children.Add(row);
    }

    private void AddReadOnlyRow(string label, string value)
    {
        var row = new DockPanel
        {
            Margin = new Thickness(16, 1, 8, 1),
        };

        var lbl = new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
            Width = 120,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        DockPanel.SetDock(lbl, global::Avalonia.Controls.Dock.Left);

        var val = new TextBlock
        {
            Text = value,
            FontSize = 12,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
        };

        row.Children.Add(lbl);
        row.Children.Add(val);
        _propsStack.Children.Add(row);
    }
}
