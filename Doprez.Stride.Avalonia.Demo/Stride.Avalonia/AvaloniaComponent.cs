using System;
using System.ComponentModel;
using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Engine;

using Matrix = Stride.Core.Mathematics.Matrix;
using Vector2 = Stride.Core.Mathematics.Vector2;
using Vector3 = Stride.Core.Mathematics.Vector3;

namespace Stride.Avalonia;

/// <summary>
/// Entity component that renders an Avalonia UI page.
/// Supports fullscreen overlay, world-space panels, and billboarding.
/// </summary>
[DataContract("AvaloniaComponent")]
[ComponentCategory("UI")]
public class AvaloniaComponent : ActivableEntityComponent
{
    /// <summary>
    /// The <see cref="AvaloniaPage"/> to render.
    /// <para>
    /// In Stride GameStudio this property appears as a dropdown listing
    /// every concrete <see cref="AvaloniaPage"/> subclass that has a
    /// <c>[DataContract]</c> attribute.  At runtime the page can also
    /// be assigned directly from code.
    /// </para>
    /// </summary>
    [DataMember(10)]
    [DefaultValue(null)]
    public AvaloniaPage? Page { get; set; }

    /// <summary>
    /// When <c>true</c> (default) the UI covers the entire screen as an overlay.
    /// When <c>false</c> the UI is placed in the 3D scene at the entity's transform.
    /// </summary>
    [DataMember(20)]
    [DefaultValue(true)]
    public bool IsFullScreen { get; set; } = true;

    /// <summary>
    /// Virtual resolution of the Avalonia surface in pixels.
    /// Defaults to (1280, 720).
    /// </summary>
    [DataMember(30)]
    public Vector2 Resolution { get; set; } = new(1280, 720);

    /// <summary>
    /// Size of the UI panel in world units.
    /// Only used when <see cref="IsFullScreen"/> is <c>false</c>.
    /// </summary>
    [DataMember(35)]
    public Vector2 Size { get; set; } = new(1.28f, 0.72f);

    /// <summary>
    /// When <c>true</c> (default) and not fullscreen, the UI panel
    /// automatically rotates to face the camera (billboard).
    /// </summary>
    [DataMember(50)]
    [DefaultValue(true)]
    public bool IsBillboard { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, content outside the panel bounds is clipped.
    /// Only meaningful for world-space panels.
    /// </summary>
    [DataMember(55)]
    [DefaultValue(false)]
    public bool ClipToBounds { get; set; } = false;

    /// <summary>
    /// When <c>true</c> (default), world-space panels are packed into a
    /// shared texture atlas for efficient batched rendering.  Set to
    /// <c>false</c> if the panel has animated content that doesn't work
    /// well with atlas packing (e.g. frequent size changes).
    /// Has no effect on fullscreen panels (they never use the atlas).
    /// </summary>
    [DataMember(57)]
    [DefaultValue(true)]
    public bool UseAtlas { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, the page is marked dirty every frame so that
    /// animated Avalonia content (transitions, progress bars, etc.) is
    /// re-captured continuously.  Default is <c>false</c>.
    /// </summary>
    [DataMember(58)]
    [DefaultValue(false)]
    public bool ContinuousRedraw { get; set; } = false;



    /// <summary>
    /// Returns the world matrix for rendering. For billboards the rotation
    /// is replaced so the panel faces the camera.
    /// </summary>
    internal Matrix GetEffectiveWorldMatrix(Vector3 cameraPos)
    {
        var world = Entity.Transform.WorldMatrix;
        return (!IsFullScreen && IsBillboard)
            ? BuildBillboardMatrix(world.TranslationVector, cameraPos)
            : world;
    }

    private static Matrix BuildBillboardMatrix(Vector3 entityPos, Vector3 cameraPos)
    {
        var toCamera = cameraPos - entityPos;
        if (toCamera.LengthSquared() < 1e-6f)
            toCamera = Vector3.UnitZ;
        else
            toCamera = Vector3.Normalize(toCamera);

        var worldUp = Vector3.UnitY;
        var right = Vector3.Cross(worldUp, toCamera);
        if (right.LengthSquared() < 1e-6f)
            right = Vector3.UnitX;
        else
            right = Vector3.Normalize(right);
        var up = Vector3.Cross(toCamera, right);

        return new Matrix(
            right.X,     right.Y,     right.Z,     0,
            up.X,        up.Y,        up.Z,        0,
            toCamera.X,  toCamera.Y,  toCamera.Z,  0,
            entityPos.X, entityPos.Y, entityPos.Z,  1);
    }

    /// <summary>
    /// Maps pixel coordinates (0..resX, 0..resY) to panel-local 3D space.
    /// </summary>
    internal Matrix GetPixelToLocalMatrix()
    {
        return Matrix.Scaling(Size.X / Resolution.X, -Size.Y / Resolution.Y, 1f)
             * Matrix.Translation(-Size.X / 2f, Size.Y / 2f, 0f);
    }

    /// <summary>
    /// Converts a screen-space mouse position (normalised 0..1, origin top-left)
    /// to Avalonia pixel coordinates on this world-space panel.
    /// Returns <c>false</c> if the ray misses the panel.
    /// </summary>
    internal bool TryScreenToPanel(
        Vector2 normScreenPos,
        Matrix cameraViewProjection,
        Matrix cameraView,
        out System.Numerics.Vector2 panelPixel)
    {
        panelPixel = default;

        float ndcX = normScreenPos.X * 2f - 1f;
        float ndcY = -(normScreenPos.Y * 2f - 1f);

        Matrix.Invert(ref cameraViewProjection, out var invVP);
        var nearPt = new Vector3(ndcX, ndcY, 0f);
        var farPt  = new Vector3(ndcX, ndcY, 1f);
        Vector3.TransformCoordinate(ref nearPt, ref invVP, out var worldNear);
        Vector3.TransformCoordinate(ref farPt,  ref invVP, out var worldFar);

        var rayDir = worldFar - worldNear;
        if (rayDir.LengthSquared() < 1e-12f) return false;
        rayDir = Vector3.Normalize(rayDir);

        Matrix.Invert(ref cameraView, out var cameraWorldMat);
        var worldMatrix = GetEffectiveWorldMatrix(cameraWorldMat.TranslationVector);
        var planeNormal = new Vector3(worldMatrix.M31, worldMatrix.M32, worldMatrix.M33);
        var planePoint  = worldMatrix.TranslationVector;

        float denom = Vector3.Dot(planeNormal, rayDir);
        if (MathF.Abs(denom) < 1e-6f) return false;

        float t = Vector3.Dot(planePoint - worldNear, planeNormal) / denom;
        if (t < 0) return false;

        var hitWorld = worldNear + rayDir * t;

        Matrix.Invert(ref worldMatrix, out var invWorld);
        Vector3.TransformCoordinate(ref hitWorld, ref invWorld, out var localHit);

        float px = (localHit.X + Size.X / 2f) / Size.X * Resolution.X;
        float py = (-localHit.Y + Size.Y / 2f) / Size.Y * Resolution.Y;

        if (px < 0 || px > Resolution.X || py < 0 || py > Resolution.Y)
            return false;

        panelPixel = new System.Numerics.Vector2(px, py);
        return true;
    }

    /// <summary>
    /// Computes an axis-aligned bounding box (center + extent) for this
    /// world-space panel, suitable for frustum culling.
    /// For billboards the bounding box is orientation-independent (sphere-like
    /// extent) since the panel always faces the camera.
    /// </summary>
    internal BoundingBoxExt GetWorldBoundingBox()
    {
        var pos = Entity.Transform.WorldMatrix.TranslationVector;
        float halfW = Size.X * 0.5f;
        float halfH = Size.Y * 0.5f;

        if (IsBillboard)
        {
            // Billboard can face any direction — use the diagonal as extent
            // so the AABB encloses all possible orientations.
            float radius = MathF.Sqrt(halfW * halfW + halfH * halfH);
            return new BoundingBoxExt
            {
                Center = pos,
                Extent = new Vector3(radius, radius, radius),
            };
        }

        // Non-billboard: transform the four local corners through the world
        // matrix and derive a tight AABB.
        var world = Entity.Transform.WorldMatrix;
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        Span<Vector3> localCorners =
        [
            new(-halfW, -halfH, 0),
            new( halfW, -halfH, 0),
            new( halfW,  halfH, 0),
            new(-halfW,  halfH, 0),
        ];

        foreach (var lc in localCorners)
        {
            var wc = lc;
            Vector3.TransformCoordinate(ref wc, ref world, out wc);
            min = Vector3.Min(min, wc);
            max = Vector3.Max(max, wc);
        }

        return new BoundingBoxExt(min, max);
    }
}
