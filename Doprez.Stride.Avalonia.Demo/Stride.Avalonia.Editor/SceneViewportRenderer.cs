using Stride.Core.Mathematics;
using Stride.Games;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Compositing;
using Stride.Avalonia.Editor.Services;

namespace Stride.Avalonia.Editor;

/// <summary>
/// Renders the 3D scene into an offscreen <see cref="Texture"/> whose dimensions
/// match the editor's centre viewport panel, then blits the result onto the
/// corresponding region of the back buffer using <see cref="SpriteBatch"/>.
/// <para>
/// This ensures the 3D scene only appears inside the viewport area, rather than
/// being drawn behind the editor's opaque dock panels.
/// </para>
/// </summary>
public class SceneViewportRenderer : SceneRendererBase
{
    private IViewportBoundsService? _viewportBoundsService;
    private GameWindow? _gameWindow;
    private SpriteBatch? _spriteBatch;
    private Texture? _renderTarget;
    private Texture? _depthBuffer;

    // Cached per-frame so CollectCore and DrawCore always use the same values.
    private RectangleF _cachedPixelBounds;
    private bool _cachedIsVisible;

    /// <summary>
    /// The child renderer that draws the 3D scene (typically a <c>SceneCameraRenderer</c>
    /// wrapping the <c>ForwardRenderer</c>).
    /// </summary>
    public ISceneRenderer? Child { get; set; }

    /// <summary>
    /// The colour used to clear the back buffer outside the viewport area.
    /// Defaults to a dark editor background.
    /// </summary>
    public Color4 EditorBackgroundColor { get; set; } = new Color4(0x1E / 255f, 0x1E / 255f, 0x1E / 255f, 1f);

    protected override void InitializeCore()
    {
        base.InitializeCore();
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _viewportBoundsService = Services.GetService<IViewportBoundsService>();
        _gameWindow = Services.GetService<IGame>()?.Window;
    }

    protected override void CollectCore(RenderContext context)
    {
        base.CollectCore(context);

        // Determine if the viewport should be rendered this frame.
        bool viewportVisible = _viewportBoundsService?.IsViewportVisible ?? false;
        bool windowMinimized = _gameWindow?.IsMinimized ?? false;
        _cachedIsVisible = viewportVisible && !windowMinimized;

        // Read normalised bounds and scale to back-buffer pixel space.
        // Cache the result so DrawCore uses the exact same values this frame.
        var normalised = _viewportBoundsService?.ViewportBounds ?? RectangleF.Empty;
        var backBuffer = GraphicsDevice.Presenter?.BackBuffer;
        if (_cachedIsVisible && backBuffer != null && normalised.Width > 0 && normalised.Height > 0)
        {
            _cachedPixelBounds = new RectangleF(
                normalised.X * backBuffer.Width,
                normalised.Y * backBuffer.Height,
                normalised.Width * backBuffer.Width,
                normalised.Height * backBuffer.Height);
        }
        else
        {
            _cachedPixelBounds = RectangleF.Empty;
        }

        int vpW = (int)_cachedPixelBounds.Width;
        int vpH = (int)_cachedPixelBounds.Height;

        if (vpW <= 0 || vpH <= 0)
        {
            // Viewport not ready or hidden — still collect with default output
            // so the child renderer's pipeline stays valid.
            Child?.Collect(context);
            return;
        }

        // Override the render output and viewport so the child renders at the
        // offscreen RT's resolution, not the full back buffer's.
        using (context.SaveRenderOutputAndRestore())
        using (context.SaveViewportAndRestore())
        {
            context.RenderOutput.RenderTargetFormat0 = PixelFormat.R8G8B8A8_UNorm_SRgb;
            context.RenderOutput.RenderTargetCount = 1;
            context.ViewportState.Viewport0 = new Viewport(0, 0, vpW, vpH);

            Child?.Collect(context);
        }
    }

    protected override void DrawCore(RenderContext context, RenderDrawContext drawContext)
    {
        var commandList = drawContext.CommandList;
        var backBuffer = GraphicsDevice.Presenter.BackBuffer;

        // 1. Clear the full back buffer to the editor background colour
        commandList.SetRenderTargetAndViewport(null, backBuffer);
        commandList.Clear(backBuffer, EditorBackgroundColor);

        // 2. If the viewport is hidden/minimized, skip the expensive 3D render
        //    and free the offscreen RT to reclaim GPU memory.
        if (!_cachedIsVisible)
        {
            ReleaseRenderTarget();
            return;
        }

        // 3. Use the pixel bounds cached during CollectCore (same frame)
        var bounds = _cachedPixelBounds;
        int vpW = (int)bounds.Width;
        int vpH = (int)bounds.Height;

        if (vpW <= 0 || vpH <= 0)
        {
            // Viewport not ready — fall back to rendering to the full back buffer
            Child?.Draw(drawContext);
            return;
        }

        // 4. Ensure the offscreen RT and depth buffer match the viewport size
        EnsureRenderTarget(vpW, vpH, context);

        // 5. Render the 3D scene into the offscreen RT
        using (drawContext.PushRenderTargetsAndRestore())
        {
            commandList.SetRenderTargetAndViewport(_depthBuffer, _renderTarget);
            commandList.Clear(_renderTarget, new Color4(0, 0, 0, 1));
            commandList.Clear(_depthBuffer, DepthStencilClearOptions.DepthBuffer | DepthStencilClearOptions.Stencil);

            Child?.Draw(drawContext);
        }

        // 6. Blit the offscreen RT onto the viewport region of the back buffer
        commandList.SetRenderTargetAndViewport(null, backBuffer);

        _spriteBatch!.Begin(drawContext.GraphicsContext,
            sortMode: SpriteSortMode.Immediate,
            blendState: BlendStates.Opaque,
            depthStencilState: DepthStencilStates.None);

        _spriteBatch.Draw(_renderTarget!,
            new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height),
            Color.White);

        _spriteBatch.End();
    }

    // ──────────────────────────────────────────────
    //  Render Target Management
    // ──────────────────────────────────────────────

    /// <summary>
    /// Releases the offscreen render target and depth buffer to free GPU memory
    /// when the viewport is not visible.
    /// </summary>
    private void ReleaseRenderTarget()
    {
        _renderTarget?.Dispose();
        _renderTarget = null;
        _depthBuffer?.Dispose();
        _depthBuffer = null;
    }

    private void EnsureRenderTarget(int width, int height, RenderContext context)
    {
        // Recreate if size changed
        if (_renderTarget != null && _renderTarget.Width == width && _renderTarget.Height == height)
            return;

        _renderTarget?.Dispose();
        _depthBuffer?.Dispose();

        _renderTarget = Texture.New2D(
            GraphicsDevice, width, height,
            PixelFormat.R8G8B8A8_UNorm_SRgb,
            TextureFlags.RenderTarget | TextureFlags.ShaderResource);

        var depthFlags = TextureFlags.DepthStencil;
        if (GraphicsDevice.Features.HasDepthAsSRV)
            depthFlags |= TextureFlags.ShaderResource;

        _depthBuffer = Texture.New2D(
            GraphicsDevice, width, height,
            PixelFormat.D24_UNorm_S8_UInt,
            depthFlags);
    }

    // ──────────────────────────────────────────────
    //  Cleanup
    // ──────────────────────────────────────────────

    protected override void Destroy()
    {
        _renderTarget?.Dispose();
        _depthBuffer?.Dispose();
        _spriteBatch?.Dispose();
        base.Destroy();
    }
}
