using System;
using System.Collections.Generic;
using System.Diagnostics;
using global::Avalonia.Media.Imaging;
using Stride.Core.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Compositing;

using Matrix = Stride.Core.Mathematics.Matrix;
using Vector3 = Stride.Core.Mathematics.Vector3;

namespace Stride.Avalonia;

/// <summary>
/// A <see cref="SceneRendererBase"/> that captures each <see cref="AvaloniaComponent"/>'s
/// headless frame, copies the pixel data to a Stride texture, and draws it
/// using <see cref="SpriteBatch"/> (fullscreen) or <see cref="Sprite3DBatch"/> +
/// texture atlas (world-space panels).
/// </summary>
/// <remarks>
/// World-space panels are packed into a shared atlas texture so that
/// <see cref="Sprite3DBatch"/> with <see cref="SpriteSortMode.Deferred"/> can
/// batch all of them into a single GPU draw call.
/// </remarks>
public class AvaloniaSceneRenderer : SceneRendererBase
{
    private SpriteBatch? _spriteBatch;           // fullscreen panels only
    private Sprite3DBatch? _sprite3DBatch;       // batched world-space panels
    private AvaloniaTextureAtlasManager? _atlasManager;

    private readonly Dictionary<AvaloniaComponent, Texture> _textures = new(); // fullscreen + non-atlas world-space
    private readonly List<AvaloniaComponent> _componentCache = new();
    private readonly List<(AvaloniaComponent comp, int atlasIndex)> _worldSpaceQueue = new(); // atlas world-space deferred draw list (with atlas index)
    private readonly List<AvaloniaComponent> _worldSpaceNonAtlas = new();      // non-atlas world-space (drawn individually)
    private readonly List<AvaloniaComponent> _fullscreenQueue = new();         // fullscreen panels (drawn last, on top)
    private readonly Dictionary<AvaloniaComponent, float> _distanceCache = new();
    private Matrix _cameraView;
    private Matrix _cameraViewProjection;
    private BoundingFrustum _frustum;
    private bool _hasCameraMatrices;
    private Vector3 _sortCameraPos;

    /// <summary>
    /// Maximum number of dirty textures to re-capture and upload per frame.
    /// Spreads burst updates over multiple frames to prevent FPS spikes.
    /// Set to 0 for unlimited (default behaviour).
    /// </summary>
    public int MaxDirtyUpdatesPerFrame { get; set; } = 50;

    private static readonly Logger _log = GlobalLogger.GetLogger(nameof(AvaloniaSceneRenderer));

    protected override void InitializeCore()
    {
        base.InitializeCore();
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _sprite3DBatch = new Sprite3DBatch(GraphicsDevice, bufferElementCount: 2048);
        _atlasManager = new AvaloniaTextureAtlasManager(GraphicsDevice);
    }

    protected override void DrawCore(RenderContext context, RenderDrawContext drawContext)
    {
        var metrics = AvaloniaRenderMetrics.Instance;
        var drawStart = Stopwatch.GetTimestamp();
        using var _profileDraw = Profiler.Begin(AvaloniaProfilingKeys.Draw);

        _hasCameraMatrices = false;
        var renderView = context.RenderView;
        if (renderView == null && context.RenderSystem.Views.Count > 0)
            renderView = context.RenderSystem.Views[0];

        if (renderView != null)
        {
            _cameraView = renderView.View;
            _cameraViewProjection = renderView.ViewProjection;
            _frustum = new BoundingFrustum(in _cameraViewProjection);
            _hasCameraMatrices = true;
        }

        var commandList = drawContext.CommandList;
        var backBuffer = GraphicsDevice.Presenter.BackBuffer;

        // ── Collect + Sort ──
        long tCollect = Stopwatch.GetTimestamp();
        List<AvaloniaComponent> components;
        using (Profiler.Begin(AvaloniaProfilingKeys.CollectComponentsDraw))
        {
            components = CollectComponents();
        }
        if (components.Count == 0)
        {
            metrics.DrawTotalMs = AvaloniaRenderMetrics.ElapsedMs(drawStart);
            return;
        }
        using (Profiler.Begin(AvaloniaProfilingKeys.SortComponents))
        {
            SortComponents(components);
        }
        metrics.DrawCollectSortMs = AvaloniaRenderMetrics.ElapsedMs(tCollect);

        int dirtyUpdatesThisFrame = 0;
        int dirtyLimit = MaxDirtyUpdatesPerFrame > 0 ? MaxDirtyUpdatesPerFrame : int.MaxValue;
        int panelsDrawn = 0;
        int panelsCulled = 0;
        int panelsDirtyUpdated = 0;
        int panelsDirtySkipped = 0;
        long bytesUploaded = 0;
        double captureAccum = 0;
        double uploadAccum = 0;
        double drawAccum = 0;

        _worldSpaceQueue.Clear();
        _worldSpaceNonAtlas.Clear();
        _fullscreenQueue.Clear();

        // ──────────────────────────────────────────────
        //  Phase 1: Capture frames + upload textures
        // ──────────────────────────────────────────────
        foreach (var comp in components)
        {
            if (!comp.Enabled || comp.Page == null) continue;

            int resW = (int)comp.Resolution.X;
            int resH = (int)comp.Resolution.Y;

            if (comp.IsFullScreen)
            {
                int bbW = backBuffer.Width;
                int bbH = backBuffer.Height;
                if (resW != bbW || resH != bbH)
                {
                    resW = bbW;
                    resH = bbH;
                    comp.Resolution = new Vector2(resW, resH);
                }
            }

            // --- Frustum culling for world-space panels ---
            if (!comp.IsFullScreen && _hasCameraMatrices)
            {
                var bbox = comp.GetWorldBoundingBox();
                if (!_frustum.Contains(in bbox))
                {
                    panelsCulled++;
                    continue;
                }
            }

            comp.Page.EnsureWindow(resW, resH);
            comp.Page.Resize(resW, resH);

            bool wasDirty = comp.Page.IsDirty;

            // Throttle: if this panel is dirty but we've already hit the
            // per-frame budget, skip the re-capture and reuse the old texture.
            bool shouldCapture = !wasDirty || dirtyUpdatesThisFrame < dirtyLimit || comp.IsFullScreen;

            if (comp.IsFullScreen)
            {
                // ── Fullscreen: per-panel texture (not atlased) ──
                Texture? texture;
                if (shouldCapture)
                {
                    long tCapture = Stopwatch.GetTimestamp();
                    WriteableBitmap? bitmap;
                    using (Profiler.Begin(AvaloniaProfilingKeys.FrameCapture))
                    {
                        bitmap = comp.Page.CaptureFrame();
                    }
                    captureAccum += AvaloniaRenderMetrics.ElapsedMs(tCapture);
                    if (bitmap == null) continue;

                    if (wasDirty || !_textures.TryGetValue(comp, out texture))
                    {
                        long tUpload = Stopwatch.GetTimestamp();
                        using (Profiler.Begin(AvaloniaProfilingKeys.TextureUpload))
                        {
                            texture = UpdateTexture(comp, bitmap, commandList, resW, resH);
                        }
                        uploadAccum += AvaloniaRenderMetrics.ElapsedMs(tUpload);
                        if (texture == null) continue;
                        if (wasDirty)
                        {
                            dirtyUpdatesThisFrame++;
                            panelsDirtyUpdated++;
                            bytesUploaded += (long)resW * resH * 4;
                        }
                    }
                }
                else
                {
                    if (!_textures.TryGetValue(comp, out texture) || texture == null)
                    {
                        panelsDirtySkipped++;
                        continue;
                    }
                    panelsDirtySkipped++;
                }

                // Queue fullscreen panel for drawing after world-space panels
                _fullscreenQueue.Add(comp);
            }
            else
            {
                // ── World-space panel ──
                if (comp.UseAtlas)
                {
                    // Atlas path: pack into shared atlas texture(s)
                    int atlasIdx;
                    if (shouldCapture)
                    {
                        long tCapture = Stopwatch.GetTimestamp();
                        WriteableBitmap? bitmap;
                        using (Profiler.Begin(AvaloniaProfilingKeys.FrameCapture))
                        {
                            bitmap = comp.Page.CaptureFrame();
                        }
                        captureAccum += AvaloniaRenderMetrics.ElapsedMs(tCapture);
                        if (bitmap == null) continue;

                        if (wasDirty || !_atlasManager!.TryGetSourceRect(comp, out _, out _))
                        {
                            long tUpload = Stopwatch.GetTimestamp();
                            using (Profiler.Begin(AvaloniaProfilingKeys.TextureUpload))
                            {
                                if (!_atlasManager.EnsureSlot(comp, resW, resH, commandList, out _, out atlasIdx))
                                {
                                    continue;
                                }
                                _atlasManager.UpdateSlot(comp, bitmap, commandList);
                            }
                            uploadAccum += AvaloniaRenderMetrics.ElapsedMs(tUpload);
                            if (wasDirty)
                            {
                                dirtyUpdatesThisFrame++;
                                panelsDirtyUpdated++;
                                bytesUploaded += (long)resW * resH * 4;
                            }
                        }
                        else
                        {
                            _atlasManager.TryGetSourceRect(comp, out _, out atlasIdx);
                        }
                    }
                    else
                    {
                        if (!_atlasManager!.TryGetSourceRect(comp, out _, out atlasIdx))
                        {
                            panelsDirtySkipped++;
                            continue;
                        }
                        panelsDirtySkipped++;
                    }

                    _worldSpaceQueue.Add((comp, atlasIdx));
                }
                else
                {
                    // Non-atlas path: per-panel texture (same as fullscreen upload)
                    Texture? texture;
                    if (shouldCapture)
                    {
                        long tCapture = Stopwatch.GetTimestamp();
                        WriteableBitmap? bitmap;
                        using (Profiler.Begin(AvaloniaProfilingKeys.FrameCapture))
                        {
                            bitmap = comp.Page.CaptureFrame();
                        }
                        captureAccum += AvaloniaRenderMetrics.ElapsedMs(tCapture);
                        if (bitmap == null) continue;

                        if (wasDirty || !_textures.TryGetValue(comp, out texture))
                        {
                            long tUpload = Stopwatch.GetTimestamp();
                            using (Profiler.Begin(AvaloniaProfilingKeys.TextureUpload))
                            {
                                texture = UpdateTexture(comp, bitmap, commandList, resW, resH);
                            }
                            uploadAccum += AvaloniaRenderMetrics.ElapsedMs(tUpload);
                            if (texture == null) continue;
                            if (wasDirty)
                            {
                                dirtyUpdatesThisFrame++;
                                panelsDirtyUpdated++;
                                bytesUploaded += (long)resW * resH * 4;
                            }
                        }
                    }
                    else
                    {
                        if (!_textures.TryGetValue(comp, out texture) || texture == null)
                        {
                            panelsDirtySkipped++;
                            continue;
                        }
                        panelsDirtySkipped++;
                    }

                    _worldSpaceNonAtlas.Add(comp);
                }
            }
        }

        // ──────────────────────────────────────────────
        //  Phase 2: Batched draw of all world-space panels
        // ──────────────────────────────────────────────

        // Atlas-batched panels (one draw call per atlas, with batch breaks for correct depth ordering)
        if (_worldSpaceQueue.Count > 0 && _hasCameraMatrices)
        {
            long tDraw = Stopwatch.GetTimestamp();
            using (Profiler.Begin(AvaloniaProfilingKeys.SpriteBatchDraw))
            {
                DrawWorldSpaceBatched(drawContext, backBuffer);
            }
            drawAccum += AvaloniaRenderMetrics.ElapsedMs(tDraw);
            panelsDrawn += _worldSpaceQueue.Count;
        }

        // Non-atlas world-space panels (individual draw call per panel)
        if (_worldSpaceNonAtlas.Count > 0 && _hasCameraMatrices)
        {
            long tDrawNonAtlas = Stopwatch.GetTimestamp();
            using (Profiler.Begin(AvaloniaProfilingKeys.SpriteBatchDraw))
            {
                DrawWorldSpaceNonAtlas(drawContext, backBuffer);
            }
            drawAccum += AvaloniaRenderMetrics.ElapsedMs(tDrawNonAtlas);
            panelsDrawn += _worldSpaceNonAtlas.Count;
        }

        // Fullscreen panels (drawn LAST so they appear on top of world-space)
        if (_fullscreenQueue.Count > 0)
        {
            long tDrawFs = Stopwatch.GetTimestamp();
            using (Profiler.Begin(AvaloniaProfilingKeys.SpriteBatchDraw))
            {
                foreach (var comp in _fullscreenQueue)
                {
                    if (_textures.TryGetValue(comp, out var fsTex) && fsTex != null)
                    {
                        DrawFullscreen(drawContext, fsTex, backBuffer);
                    }
                }
            }
            drawAccum += AvaloniaRenderMetrics.ElapsedMs(tDrawFs);
            panelsDrawn += _fullscreenQueue.Count;
        }

        // ── Store metrics ──
        metrics.FrameCaptureMs = captureAccum;
        metrics.TextureUploadMs = uploadAccum;
        metrics.SpriteBatchDrawMs = drawAccum;
        metrics.PanelsDrawn = panelsDrawn;
        metrics.PanelsCulled = panelsCulled;
        metrics.PanelsDirtyUpdated = panelsDirtyUpdated;
        metrics.PanelsDirtySkipped = panelsDirtySkipped;
        metrics.BytesUploaded = bytesUploaded;
        metrics.AtlasCount = _atlasManager?.AtlasCount ?? 0;
        metrics.DrawTotalMs = AvaloniaRenderMetrics.ElapsedMs(drawStart);
    }

    // ──────────────────────────────────────────────
    //  Texture Management
    // ──────────────────────────────────────────────

    private Texture? UpdateTexture(
        AvaloniaComponent comp, WriteableBitmap bitmap,
        CommandList commandList, int width, int height)
    {
        if (!_textures.TryGetValue(comp, out var texture)
            || texture.Width != width || texture.Height != height)
        {
            texture?.Dispose();
            texture = Texture.New2D(
                GraphicsDevice, width, height,
                PixelFormat.R8G8B8A8_UNorm_SRgb,
                TextureFlags.ShaderResource,
                usage: GraphicsResourceUsage.Default);
            _textures[comp] = texture;
        }

        using var fb = bitmap.Lock();
        int dataSize = fb.RowBytes * fb.Size.Height;

        texture.SetData(commandList,
            new DataPointer(fb.Address, dataSize));

        return texture;
    }

    // ──────────────────────────────────────────────
    //  Fullscreen Drawing
    // ──────────────────────────────────────────────

    private void DrawFullscreen(RenderDrawContext drawContext, Texture texture, Texture backBuffer)
    {
        var commandList = drawContext.CommandList;
        commandList.SetRenderTargetAndViewport(null, backBuffer);

        _spriteBatch!.Begin(drawContext.GraphicsContext,
            sortMode: SpriteSortMode.Immediate,
            blendState: BlendStates.AlphaBlend,
            samplerState: GraphicsDevice.SamplerStates.PointClamp,
            depthStencilState: DepthStencilStates.None);

        _spriteBatch.Draw(texture,
            new RectangleF(0, 0, backBuffer.Width, backBuffer.Height),
            Color.White);

        _spriteBatch.End();
    }

    // ──────────────────────────────────────────────
    //  World-Space Batched Drawing (Sprite3DBatch + Atlas)
    // ──────────────────────────────────────────────

    private void DrawWorldSpaceNonAtlas(RenderDrawContext drawContext, Texture backBuffer)
    {
        var commandList = drawContext.CommandList;
        var depthStencil = GraphicsDevice.Presenter.DepthStencilBuffer;
        commandList.SetRenderTargetAndViewport(depthStencil, backBuffer);

        var white = Color4.White;

        foreach (var comp in _worldSpaceNonAtlas)
        {
            if (!_textures.TryGetValue(comp, out var texture) || texture == null)
                continue;

            var worldMatrix = comp.GetEffectiveWorldMatrix(_sortCameraPos);
            var elementSize = comp.Size;
            var sourceRect = new RectangleF(0, 0, texture.Width, texture.Height);

            _sprite3DBatch!.Begin(drawContext.GraphicsContext,
                _cameraViewProjection,
                sortMode: SpriteSortMode.Deferred,
                blendState: BlendStates.AlphaBlend,
                depthStencilState: DepthStencilStates.DepthRead);

            _sprite3DBatch.Draw(texture,
                ref worldMatrix,
                ref sourceRect,
                ref elementSize,
                ref white);

            _sprite3DBatch.End();
        }
    }

    /// <summary>
    /// Draws all atlas-batched world-space panels, grouped by atlas texture.
    /// Iterates the depth-sorted list and issues batch breaks when the atlas
    /// changes between consecutive panels, preserving correct back-to-front
    /// ordering across multiple atlases.
    /// </summary>
    private void DrawWorldSpaceBatched(RenderDrawContext drawContext, Texture backBuffer)
    {
        var commandList = drawContext.CommandList;
        var depthStencil = GraphicsDevice.Presenter.DepthStencilBuffer;
        commandList.SetRenderTargetAndViewport(depthStencil, backBuffer);

        var white = Color4.White;
        int currentAtlasIndex = -1;
        Texture? currentAtlasTexture = null;
        bool batchOpen = false;

        foreach (var (comp, atlasIdx) in _worldSpaceQueue)
        {
            if (!_atlasManager!.TryGetSourceRect(comp, out var sourceRect, out _))
                continue;

            var atlasTexture = _atlasManager.GetTexture(atlasIdx);
            if (atlasTexture == null) continue;

            // Break the batch when the atlas changes
            if (atlasIdx != currentAtlasIndex)
            {
                if (batchOpen)
                    _sprite3DBatch!.End();

                currentAtlasIndex = atlasIdx;
                currentAtlasTexture = atlasTexture;

                _sprite3DBatch!.Begin(drawContext.GraphicsContext,
                    _cameraViewProjection,
                    sortMode: SpriteSortMode.Deferred,
                    blendState: BlendStates.AlphaBlend,
                    depthStencilState: DepthStencilStates.DepthRead);
                batchOpen = true;
            }

            var worldMatrix = comp.GetEffectiveWorldMatrix(_sortCameraPos);
            var elementSize = comp.Size;

            _sprite3DBatch!.Draw(currentAtlasTexture!,
                ref worldMatrix,
                ref sourceRect,
                ref elementSize,
                ref white);
        }

        if (batchOpen)
            _sprite3DBatch!.End();
    }

    // ──────────────────────────────────────────────
    //  Component Collection & Sorting
    // ──────────────────────────────────────────────

    private void SortComponents(List<AvaloniaComponent> components)
    {
        if (_hasCameraMatrices)
        {
            Matrix.Invert(ref _cameraView, out var cameraWorld);
            _sortCameraPos = cameraWorld.TranslationVector;
        }
        else
        {
            _sortCameraPos = Vector3.Zero;
        }

        // Pre-compute squared distances to avoid repeated matrix property
        // access + vector math during O(n log n) comparisons.
        _distanceCache.Clear();
        foreach (var comp in components)
        {
            if (!comp.IsFullScreen)
            {
                var diff = comp.Entity.Transform.WorldMatrix.TranslationVector - _sortCameraPos;
                _distanceCache[comp] = diff.LengthSquared();
            }
        }

        components.Sort(CompareComponents);
    }

    /// <summary>
    /// Cached comparison delegate — avoids closure/delegate allocation every frame.
    /// Uses <see cref="_distanceCache"/> which must be populated before calling Sort.
    /// </summary>
    private int CompareComponents(AvaloniaComponent a, AvaloniaComponent b)
    {
        if (a.IsFullScreen != b.IsFullScreen)
            return a.IsFullScreen.CompareTo(b.IsFullScreen);

        if (!a.IsFullScreen)
        {
            float distA = _distanceCache[a];
            float distB = _distanceCache[b];
            return distB.CompareTo(distA);
        }

        return 0;
    }

    private List<AvaloniaComponent> CollectComponents()
    {
        _componentCache.Clear();
        var sceneSystem = Services.GetService<SceneSystem>();
        if (sceneSystem?.SceneInstance == null) return _componentCache;
        CollectFromScene(sceneSystem.SceneInstance.RootScene, _componentCache);
        return _componentCache;
    }

    private static void CollectFromScene(Scene? scene, List<AvaloniaComponent> result)
    {
        if (scene == null) return;
        foreach (var entity in scene.Entities)
        {
            var comp = entity.Get<AvaloniaComponent>();
            if (comp != null)
                result.Add(comp);
        }
        foreach (var child in scene.Children)
            CollectFromScene(child, result);
    }

    protected override void Destroy()
    {
        foreach (var tex in _textures.Values)
            tex?.Dispose();
        _textures.Clear();
        _atlasManager?.Dispose();
        _sprite3DBatch?.Dispose();
        _spriteBatch?.Dispose();
        base.Destroy();
    }
}
