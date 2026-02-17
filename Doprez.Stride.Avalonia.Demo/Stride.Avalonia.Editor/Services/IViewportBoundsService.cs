using Stride.Core.Mathematics;

namespace Stride.Avalonia.Editor.Services;

/// <summary>
/// Provides the normalised (0–1) bounds of the scene viewport within the editor window.
/// The renderer reads these bounds each frame, then scales them to back-buffer
/// pixel dimensions before creating the offscreen render target and blitting.
/// </summary>
public interface IViewportBoundsService
{
    /// <summary>
    /// The viewport rectangle in normalised coordinates (0–1 on each axis).
    /// Returns <see cref="RectangleF.Empty"/> when the viewport is not yet known.
    /// </summary>
    RectangleF ViewportBounds { get; }

    /// <summary>
    /// Whether the viewport is currently visible and should be rendered.
    /// Returns <c>false</c> when the viewport tab is closed/hidden or the
    /// host window is minimized.
    /// </summary>
    bool IsViewportVisible { get; }
}
