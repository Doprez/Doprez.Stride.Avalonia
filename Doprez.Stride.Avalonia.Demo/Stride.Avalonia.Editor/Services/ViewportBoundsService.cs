using Stride.Core.Mathematics;

namespace Stride.Avalonia.Editor.Services;

/// <summary>
/// Thread-safe implementation of <see cref="IViewportBoundsService"/>.
/// The Avalonia layout thread pushes updated normalised bounds via <see cref="Update"/>,
/// and the Stride render thread reads them each frame via <see cref="ViewportBounds"/>.
/// </summary>
public sealed class ViewportBoundsService : IViewportBoundsService
{
    private volatile float _x, _y, _width, _height;
    private volatile bool _isVisible;

    /// <inheritdoc />
    public RectangleF ViewportBounds => new(_x, _y, _width, _height);

    /// <inheritdoc />
    public bool IsViewportVisible => _isVisible;

    /// <summary>
    /// Called from the Avalonia layout thread when the viewport panel's bounds change.
    /// All values must be in normalised (0–1) coordinates, where (0,0) is the
    /// top-left corner of the editor window and (1,1) is the bottom-right.
    /// </summary>
    /// <param name="isVisible">
    /// <c>true</c> when the viewport control is attached to the visual tree, has
    /// non-zero size, and the host window is not minimized.
    /// </param>
    public void Update(float x, float y, float width, float height, bool isVisible)
    {
        _x = x;
        _y = y;
        _width = width;
        _height = height;
        _isVisible = isVisible;
    }
}
