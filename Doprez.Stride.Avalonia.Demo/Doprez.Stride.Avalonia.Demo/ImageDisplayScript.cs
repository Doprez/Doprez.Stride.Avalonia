using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Avalonia;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using global::Avalonia.Media.Imaging;

namespace Doprez.Stride.Avalonia.Demo;

/// <summary>
/// Demonstrates loading a Stride texture asset ("StrideLogo") via
/// <see cref="StrideImageHelper"/> and displaying it in a fullscreen
/// Avalonia overlay using an <see cref="Image"/> control.
/// <para>
/// Attach this script to any entity in the scene via GameStudio.
/// </para>
/// </summary>
public class ImageDisplayScript : SyncScript
{
    /// <summary>
    /// Stride asset path for the texture to display (no folder prefix).
    /// </summary>
    [DataMember(10)]
    public string TextureAssetPath { get; set; } = "StrideLogo";

    /// <summary>
    /// Pixel resolution of the offscreen Avalonia UI texture.
    /// Higher = sharper but more GPU memory. 512 is a good default.
    /// </summary>
    [DataMember(20)]
    public int PanelResolution { get; set; } = 512;

    /// <summary>
    /// World-space panel width in meters.
    /// </summary>
    [DataMember(40)]
    public float WorldWidth { get; set; } = 1f;

    /// <summary>
    /// World-space panel height in meters.
    /// </summary>
    [DataMember(50)]
    public float WorldHeight { get; set; } = 1f;

    public override void Start()
    {
        // Load the Stride texture and convert to an Avalonia bitmap
        var bitmap = StrideImageHelper.LoadAsAvaloniaBitmap(
            Content,
            Game.GraphicsContext,
            TextureAssetPath);

        // Build an Avalonia control tree to display the image.
        // The Image fills the available space; sharpness comes from PanelResolution.
        var image = new Image
        {
            Source = bitmap,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        var label = new TextBlock
        {
            Text = $"Loaded: {TextureAssetPath}",
            Foreground = Brushes.White,
            FontSize = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new global::Avalonia.Thickness(0, 8, 0, 0),
        };

        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { image, label },
        };

        // Wrap in a semi-transparent background so it's visible in the 3D scene
        var border = new Border
        {
            Background = new SolidColorBrush(global::Avalonia.Media.Color.FromArgb(180, 30, 30, 30)),
            CornerRadius = new global::Avalonia.CornerRadius(12),
            Padding = new global::Avalonia.Thickness(24),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = panel,
        };

        var page = new DefaultAvaloniaPage(border);

        // World-space panel — rendered as a billboard in 3D, not a screen overlay.
        var avaloniaComponent = new AvaloniaComponent
        {
            IsFullScreen = false,
            IsBillboard = true,
            Resolution = new Vector2(PanelResolution, PanelResolution),
            Size = new Vector2(WorldWidth, WorldHeight),
            UseAtlas = true,
            Page = page,
            ContinuousRedraw = false,
        };

        Entity.Add(avaloniaComponent);
    }

    public override void Update()
    {
        // Static display — nothing needed per frame.
    }
}
