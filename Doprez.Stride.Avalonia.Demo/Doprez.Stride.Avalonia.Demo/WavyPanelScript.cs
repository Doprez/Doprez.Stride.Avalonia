using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Avalonia;

namespace Doprez.Stride.Avalonia.Demo;

/// <summary>
/// Spawns a world-space Avalonia UI panel rendered with the WavyUIEffect
/// custom SDSL shader.  The panel undulates in 3D via sinusoidal vertex
/// displacement while keeping full Avalonia interactivity.
/// <para>
/// Attach this script to any entity in the scene.  The entity's transform
/// controls where the wavy panel appears in the world.
/// </para>
/// </summary>
/// <remarks>
/// <b>How it works:</b>
/// <list type="number">
///   <item>An Avalonia control (<see cref="WavyPanelContent"/>) is created
///         and wrapped in a <see cref="DefaultAvaloniaPage"/>.</item>
///   <item>An <see cref="AvaloniaComponent"/> is added to the entity with
///         <see cref="AvaloniaComponent.CustomEffectName"/> set to
///         <c>"WavyUIEffect"</c>.</item>
///   <item>The <see cref="AvaloniaSceneRenderer"/> detects the custom effect
///         and draws the panel's texture onto a subdivided quad mesh using
///         the WavyUIEffect SDSL shader instead of the built-in sprite path.</item>
///   <item>The shader displaces vertices along Z using a sine wave driven
///         by <see cref="AvaloniaComponent.EffectTime"/>.</item>
/// </list>
/// </remarks>
public class WavyPanelScript : SyncScript
{
    private WavyPanelContent? _content;
    private AvaloniaComponent? _avaloniaComponent;

    public override void Start()
    {
        _content = new WavyPanelContent();
        var page = new DefaultAvaloniaPage(_content);

        _avaloniaComponent = new AvaloniaComponent
        {
            IsFullScreen = false,
            IsBillboard = false,       // Fixed orientation so the wave is visible from the side
            Resolution = new Vector2(512, 384),
            Size = new Vector2(2f, 1.5f),
            CustomEffectName = "WavyUIEffect",
            MeshSubdivisions = 32,
            ContinuousRedraw = true,   // Keep updating so the wave animates
            UseAtlas = false,          // Custom-effect panels skip the atlas
        };
        _avaloniaComponent.Page = page;

        Entity.Add(_avaloniaComponent);
    }

    public override void Update()
    {
        // Update the time display on the Avalonia control
        if (_content != null && _avaloniaComponent != null)
        {
            _content.UpdateTime(_avaloniaComponent.EffectTime);
        }
    }
}
