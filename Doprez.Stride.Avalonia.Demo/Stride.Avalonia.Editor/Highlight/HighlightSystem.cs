using Stride.Core.Annotations;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Materials;
using Stride.Rendering.Materials.ComputeColors;
using Stride.Avalonia.Editor.Services;

using SelectionChangedEventArgs = Stride.Avalonia.Editor.Services.EditorSelectionChangedEventArgs;

namespace Stride.Avalonia.Editor.Highlight;

/// <summary>
/// Game system that applies a highlight effect to the selected entity.
/// On selection the highlight fades in over 0.5s, holds briefly, then fades out
/// over 0.5s (total cycle = 1s). The effect adds an emissive colour overlay
/// to every material on the entity's <see cref="ModelComponent"/>.
/// </summary>
public class HighlightSystem : GameSystemBase
{
    private readonly EditorSelectionService _selection;

    // Highlight colour (sky blue glow)
    private static readonly Color4 HighlightColor = new(0.2f, 0.5f, 1.0f, 1.0f);
    private const float HighlightDuration = 1.0f; // seconds
    private const float FadeIn  = 0.3f; // portion of duration for fade-in
    private const float FadeOut = 0.3f; // portion of duration for fade-out

    // Current highlight state
    private Entity? _highlightedEntity;
    private float _highlightTimer;
    private readonly Dictionary<int, Material> _originalMaterials = new();
    private readonly Dictionary<int, Material> _highlightMaterials = new();

    public HighlightSystem([NotNull] Stride.Core.IServiceRegistry registry,
                           EditorSelectionService selection)
        : base(registry)
    {
        _selection = selection;
        Enabled = true;
        Visible = false;
    }

    protected override void LoadContent()
    {
        _selection.SelectionChanged += OnSelectionChanged;
    }

    private void OnSelectionChanged(object? sender, EditorSelectionChangedEventArgs e)
    {
        // Restore previous entity's materials immediately
        RestoreOriginalMaterials();

        _highlightedEntity = e.NewEntity;
        _highlightTimer = 0f;

        if (_highlightedEntity != null)
            CaptureOriginalMaterials(_highlightedEntity);
    }

    public override void Update(GameTime gameTime)
    {
        if (_highlightedEntity == null) return;

        _highlightTimer += (float)gameTime.Elapsed.TotalSeconds;

        if (_highlightTimer >= HighlightDuration)
        {
            // Highlight cycle finished — restore and stop
            RestoreOriginalMaterials();
            _highlightedEntity = null;
            return;
        }

        float intensity = ComputeIntensity(_highlightTimer);
        ApplyHighlight(_highlightedEntity, intensity);
    }

    /// <summary>
    /// Compute a 0→1→0 intensity envelope over the highlight duration.
    /// </summary>
    private static float ComputeIntensity(float t)
    {
        float norm = t / HighlightDuration;

        if (norm < FadeIn)
            return norm / FadeIn; // ramp up

        if (norm > 1f - FadeOut)
            return (1f - norm) / FadeOut; // ramp down

        return 1f; // hold
    }

    // ── Material management ──────────────────────────────

    private void CaptureOriginalMaterials(Entity entity)
    {
        _originalMaterials.Clear();
        _highlightMaterials.Clear();

        var model = entity.Get<ModelComponent>();
        if (model?.Model == null) return;

        for (int i = 0; i < model.Model.Materials.Count; i++)
        {
            var original = model.GetMaterial(i);
            if (original == null) continue;

            _originalMaterials[i] = original;

            // Create a highlight copy with emissive overlay
            var highlightMat = CreateHighlightMaterial(original);
            _highlightMaterials[i] = highlightMat;
        }
    }

    private Material CreateHighlightMaterial(Material source)
    {
        // Clone by creating a new material with emissive
        var desc = new MaterialDescriptor
        {
            Attributes =
            {
                Emissive = new MaterialEmissiveMapFeature(
                    new ComputeColor(new Color4(0, 0, 0, 0))) // will be updated each frame
            }
        };

        var mat = Material.New(GraphicsDevice, desc);

        // Copy passes from the source material so it looks the same, plus emissive
        // We take the simple approach: just return the highlight material
        // and blend it over the original via per-frame material swap
        return mat;
    }

    private void ApplyHighlight(Entity entity, float intensity)
    {
        var model = entity.Get<ModelComponent>();
        if (model?.Model == null) return;

        for (int i = 0; i < model.Model.Materials.Count; i++)
        {
            if (!_originalMaterials.TryGetValue(i, out var original)) continue;

            // Modify emissive on the original material directly
            // (we'll restore it when highlight ends)
            var emissiveColor = HighlightColor * intensity;

            // Use the material's parameter collection to set emissive intensity
            original.Passes[0].Parameters.Set(
                MaterialKeys.EmissiveIntensity, intensity * 2.0f);
            original.Passes[0].Parameters.Set(
                MaterialKeys.EmissiveValue, emissiveColor);
        }
    }

    private void RestoreOriginalMaterials()
    {
        if (_highlightedEntity == null) return;

        var model = _highlightedEntity.Get<ModelComponent>();
        if (model?.Model == null) return;

        foreach (var (index, original) in _originalMaterials)
        {
            // Clear the emissive override
            original.Passes[0].Parameters.Remove(MaterialKeys.EmissiveIntensity);
            original.Passes[0].Parameters.Remove(MaterialKeys.EmissiveValue);
        }

        _originalMaterials.Clear();
        _highlightMaterials.Clear();
    }

    protected override void Destroy()
    {
        RestoreOriginalMaterials();
        _selection.SelectionChanged -= OnSelectionChanged;
        base.Destroy();
    }
}
