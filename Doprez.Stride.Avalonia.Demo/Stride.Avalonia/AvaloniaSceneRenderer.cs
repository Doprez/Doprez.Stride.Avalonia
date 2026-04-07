using System;
using System.Collections.Generic;
using System.Diagnostics;
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
/// headless frame and samples the shared Stride texture directly.
/// Panels are drawn using <see cref="SpriteBatch"/> (fullscreen) or
/// <see cref="Sprite3DBatch"/> plus a texture atlas (world-space panels).
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

    // Round-robin state for spreading ContinuousRedraw updates across frames.
    private int _frameCounter;
    private int _lastContinuousRedrawCount;

    /// <summary>
    /// Maximum number of dirty textures to re-capture and upload per frame.
    /// Spreads burst updates over multiple frames to prevent FPS spikes.
    /// Set to 0 for unlimited (default behaviour).
    /// <para>
    /// For panels with <see cref="AvaloniaComponent.ContinuousRedraw"/> enabled,
    /// updates are spread across frames using round-robin instead of a hard cap,
    /// so that all panels cycle through updates evenly.
    /// </para>
    /// </summary>
    public int MaxDirtyUpdatesPerFrame { get; set; } = 50;

    private static readonly Logger _log = GlobalLogger.GetLogger(nameof(AvaloniaSceneRenderer));
    private bool _loggedComponentDiag;

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

        // One-time diagnostic: dump component state
        if (!_loggedComponentDiag && _frameCounter > 10)
        {
            _loggedComponentDiag = true;
            int fs = 0, ws = 0;
            foreach (var c in components)
            {
                if (c.IsFullScreen) fs++;
                else ws++;
                var rs = c.Page?.RenderSurface;
                var tex = rs?.StrideTexture;
                System.Console.Error.WriteLine(
                    $"[Stride.Avalonia] Component '{c.Entity?.Name}' fullscreen={c.IsFullScreen} " +
                    $"res={c.Resolution} enabled={c.Enabled} page={c.Page != null} " +
                    $"surface={rs != null} surfaceSize={rs?.Width}x{rs?.Height} " +
                    $"tex={tex != null} renderVer={rs?.RenderVersion} " +
                    $"isDirty={c.Page?.IsDirty}");
            }
            System.Console.Error.WriteLine(
                $"[Stride.Avalonia] Total={components.Count} fullscreen={fs} worldspace={ws}");
        }
        using (Profiler.Begin(AvaloniaProfilingKeys.SortComponents))
        {
            SortComponents(components);
        }
        metrics.DrawCollectSortMs = AvaloniaRenderMetrics.ElapsedMs(tCollect);

        int dirtyUpdatesThisFrame = 0;
        int dirtyLimit = MaxDirtyUpdatesPerFrame > 0 ? MaxDirtyUpdatesPerFrame : int.MaxValue;

        // Round-robin: spread ContinuousRedraw re-captures evenly across frames.
        // spreadFactor = how many frames it takes to cycle through all of them.
        _frameCounter++;
        int spreadFactor = dirtyLimit < int.MaxValue
            ? Math.Max(1, (_lastContinuousRedrawCount + dirtyLimit - 1) / dirtyLimit)
            : 1;
        int frameSlot = _frameCounter % spreadFactor;
        int continuousRedrawOrdinal = 0;

        int panelsDrawn = 0;
        int panelsCulled = 0;
        int panelsDirtyUpdated = 0;
        int panelsDirtySkipped = 0;
        double captureAccum = 0;
        double drawAccum = 0;

        _worldSpaceQueue.Clear();
        _worldSpaceNonAtlas.Clear();
        _fullscreenQueue.Clear();

        // ──────────────────────────────────────────────
        //  Phase 1: Capture frames + upload textures
        // ──────────────────────────────────────────────
        foreach (var comp in components)
        {
            if (!comp.Enabled || comp.Page == null)
            {
                _atlasManager?.Remove(comp);
                _textures.Remove(comp);
                continue;
            }

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

            if (comp.IsFullScreen || !comp.UseAtlas)
                _atlasManager?.Remove(comp);

            // Ensure every panel has a window & surface regardless of
            // visibility so that they are ready to render when they
            // enter the frustum.
            comp.Page.EnsureWindow(resW, resH);
            comp.Page.Resize(resW, resH);
            comp.Page.EnsureGpuSurface(GraphicsDevice);

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

            bool wasDirty = comp.Page.IsDirty;

            // Check whether this panel already has a rendered texture.
            bool hasExistingTexture = TryGetCurrentTexture(comp, out _);
            bool needsFirstTexture = !hasExistingTexture;

            // Decide whether to (re-)capture this panel's frame.
            // Fullscreen panels are expensive to upload, so they should follow
            // the same dirty/no-texture policy as world-space panels.
            bool shouldCapture;
            if (comp.IsFullScreen)
            {
                shouldCapture = needsFirstTexture || wasDirty;
            }
            else if (needsFirstTexture)
            {
                // Newly visible world-space panels are cold-start work.
                // Throttle them through the normal per-frame budget so a
                // camera move or initial view doesn't upload hundreds of
                // textures in a single frame.
                shouldCapture = dirtyUpdatesThisFrame < dirtyLimit;
            }
            else if (!wasDirty)
            {
                shouldCapture = false;
            }
            else if (comp.ContinuousRedraw)
            {
                // ContinuousRedraw panels: round-robin so every panel
                // cycles through updates evenly across frames.
                shouldCapture = (continuousRedrawOrdinal % spreadFactor) == frameSlot;
                continuousRedrawOrdinal++;
            }
            else
            {
                // Normal dirty panel: flat per-frame budget cap.
                shouldCapture = dirtyUpdatesThisFrame < dirtyLimit;
            }

            if (comp.IsFullScreen)
            {
                // ── Fullscreen ──
                if (shouldCapture)
                {
                    long tCapture = Stopwatch.GetTimestamp();
                    bool captured;
                    using (Profiler.Begin(AvaloniaProfilingKeys.FrameCapture))
                    {
                        captured = comp.Page.CaptureFrame();
                    }
                    captureAccum += AvaloniaRenderMetrics.ElapsedMs(tCapture);

                    if (!captured)
                    {
                        if (!TryGetCurrentTexture(comp, out _))
                        {
                            panelsDirtySkipped++;
                            continue;
                        }

                        panelsDirtySkipped++;
                        if (comp.Page.Content.IsVisible)
                            _fullscreenQueue.Add(comp);
                        continue;
                    }

                    var strideTexture = comp.Page.RenderSurface?.StrideTexture;
                    if (strideTexture == null) continue;

                    // Register in _textures for the draw phase
                    _textures[comp] = strideTexture;

                    if (wasDirty)
                    {
                        dirtyUpdatesThisFrame++;
                        panelsDirtyUpdated++;
                    }
                }
                else
                {
                    if (!TryGetCurrentTexture(comp, out _))
                    {
                        panelsDirtySkipped++;
                        continue;
                    }
                    panelsDirtySkipped++;
                }

                // Queue fullscreen panel for drawing after world-space panels.
                // Hidden fullscreen roots (for example, a closed pause menu)
                // do not need a fullscreen draw pass.
                if (comp.Page.Content.IsVisible)
                    _fullscreenQueue.Add(comp);
            }
            else
            {
                // ── World-space panel ──
                if (comp.UseAtlas)
                {
                    // Atlas world-space panel
                    bool capturedThisFrame = false;
                    if (shouldCapture)
                    {
                        long tCapture = Stopwatch.GetTimestamp();
                        bool captured;
                        using (Profiler.Begin(AvaloniaProfilingKeys.FrameCapture))
                        {
                            captured = comp.Page.CaptureFrame();
                        }
                        captureAccum += AvaloniaRenderMetrics.ElapsedMs(tCapture);

                        if (!captured)
                        {
                            if (!TryGetCurrentTexture(comp, out _))
                            {
                                panelsDirtySkipped++;
                                continue;
                            }

                            panelsDirtySkipped++;
                            capturedThisFrame = false;
                        }

                        if (captured)
                        {
                            var strideTexture = comp.Page.RenderSurface?.StrideTexture;
                            if (strideTexture == null) continue;

                            _textures[comp] = strideTexture;
                            capturedThisFrame = true;

                            dirtyUpdatesThisFrame++;
                            if (wasDirty)
                            {
                                panelsDirtyUpdated++;
                            }
                        }
                    }
                    else
                    {
                        if (!TryGetCurrentTexture(comp, out _))
                        {
                            panelsDirtySkipped++;
                            continue;
                        }
                        panelsDirtySkipped++;
                    }

                    if (!TryGetCurrentTexture(comp, out var atlasSourceTexture) || atlasSourceTexture == null)
                        continue;

                    _textures[comp] = atlasSourceTexture;

                    if (!TryQueueAtlasComponent(comp, atlasSourceTexture, resW, resH, commandList, capturedThisFrame))
                        _worldSpaceNonAtlas.Add(comp);
                }
                else
                {
                    // Non-atlas world-space panel
                    if (shouldCapture)
                    {
                        long tCapture = Stopwatch.GetTimestamp();
                        bool captured;
                        using (Profiler.Begin(AvaloniaProfilingKeys.FrameCapture))
                        {
                            captured = comp.Page.CaptureFrame();
                        }
                        captureAccum += AvaloniaRenderMetrics.ElapsedMs(tCapture);

                        if (!captured)
                        {
                            if (!TryGetCurrentTexture(comp, out _))
                            {
                                panelsDirtySkipped++;
                                continue;
                            }

                            panelsDirtySkipped++;
                            _worldSpaceNonAtlas.Add(comp);
                            continue;
                        }

                        var strideTexture = comp.Page.RenderSurface?.StrideTexture;
                        if (strideTexture == null) continue;

                        _textures[comp] = strideTexture;

                        dirtyUpdatesThisFrame++;
                        if (wasDirty)
                        {
                            panelsDirtyUpdated++;
                        }
                    }
                    else
                    {
                        if (!TryGetCurrentTexture(comp, out _))
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
                DrawFullscreenQueue(drawContext, backBuffer);
            }
            drawAccum += AvaloniaRenderMetrics.ElapsedMs(tDrawFs);
            panelsDrawn += _fullscreenQueue.Count;
        }

        // Update round-robin state for next frame.
        _lastContinuousRedrawCount = continuousRedrawOrdinal;

        // ── Store metrics ──
        metrics.FrameCaptureMs = captureAccum;
        metrics.SpriteBatchDrawMs = drawAccum;
        metrics.PanelsDrawn = panelsDrawn;
        metrics.PanelsCulled = panelsCulled;
        metrics.PanelsDirtyUpdated = panelsDirtyUpdated;
        metrics.PanelsDirtySkipped = panelsDirtySkipped;
        metrics.AtlasCount = _atlasManager?.AtlasCount ?? 0;
        metrics.DrawTotalMs = AvaloniaRenderMetrics.ElapsedMs(drawStart);

        // Steady-state diagnostic: at frame 100, log which panels are still
        // missing textures/surfaces so we can identify the root cause of
        // permanently invisible panels.
        if (_frameCounter == 100)
        {
            int hasTexCount = 0, noTexCount = 0, noSurfCount = 0, noCaptured = 0;
            foreach (var comp in components)
            {
                if (!comp.Enabled || comp.Page == null || comp.IsFullScreen) continue;
                var rs = comp.Page.RenderSurface;
                if (rs == null) { noSurfCount++; continue; }
                if (rs.StrideTexture == null) { noTexCount++; continue; }
                if (rs.RenderVersion == 0) { noCaptured++; }
                hasTexCount++;
            }
            System.Console.Error.WriteLine(
                $"[Stride.Avalonia] Frame100: hasTex={hasTexCount} noTex={noTexCount} " +
                $"noSurf={noSurfCount} renderVer0={noCaptured} " +
                $"drawn={panelsDrawn} culled={panelsCulled} skipped={panelsDirtySkipped} " +
                $"atlasQ={_worldSpaceQueue.Count} nonAtlasQ={_worldSpaceNonAtlas.Count} " +
                $"texCache={_textures.Count}");

            // Log any panels that have texture but RenderVersion=0
            int logged = 0;
            foreach (var comp in components)
            {
                if (comp.IsFullScreen || !comp.Enabled || comp.Page == null) continue;
                var rs = comp.Page.RenderSurface;
                if (rs == null)
                {
                    System.Console.Error.WriteLine(
                        $"[Stride.Avalonia] MISSING-SURF: '{comp.Entity?.Name}' " +
                        $"isDirty={comp.Page.IsDirty} hasWindow={comp.Page.Window != null}");
                    if (++logged > 20) break;
                }
                else if (rs.RenderVersion == 0)
                {
                    System.Console.Error.WriteLine(
                        $"[Stride.Avalonia] STUCK-RV0: '{comp.Entity?.Name}' " +
                        $"size={rs.Width}x{rs.Height} tex={rs.StrideTexture != null} " +
                        $"needsRecap={rs.NeedsRecapture} isDirty={comp.Page.IsDirty} " +
                        $"sessions={rs.SessionCount} vkHandle={rs.VkImageHandle}");
                    if (++logged > 20) break;
                }
            }
        }
    }

    private bool TryGetCurrentTexture(AvaloniaComponent comp, out Texture? texture)
    {
        texture = comp.Page?.RenderSurface?.StrideTexture;
        if (texture != null)
        {
            _textures[comp] = texture;
            return true;
        }

        return _textures.TryGetValue(comp, out texture) && texture != null;
    }

    private bool TryQueueAtlasComponent(AvaloniaComponent comp,
        Texture sourceTexture,
        int width,
        int height,
        CommandList commandList,
        bool capturedThisFrame)
    {
        if (_atlasManager == null)
            return false;

        bool hadSlot = _atlasManager.TryGetSourceRect(comp, out _, out _);
        if (!_atlasManager.EnsureSlot(comp, width, height, commandList, out _, out var atlasIndex))
            return false;

        if ((capturedThisFrame || !hadSlot)
            && !_atlasManager.UpdateSlot(comp, sourceTexture, commandList))
        {
            _atlasManager.Remove(comp);
            _log.Warning($"Atlas update failed for '{comp.Entity.Name ?? comp.Entity.Id.ToString()}', falling back to direct panel draw.");
            return false;
        }

        _worldSpaceQueue.Add((comp, atlasIndex));
        return true;
    }

    // ──────────────────────────────────────────────
    //  Fullscreen Drawing
    // ──────────────────────────────────────────────

    private void DrawFullscreenQueue(RenderDrawContext drawContext, Texture backBuffer)
    {
        var commandList = drawContext.CommandList;
        commandList.SetRenderTargetAndViewport(null, backBuffer);

        _spriteBatch!.Begin(drawContext.GraphicsContext,
            sortMode: SpriteSortMode.Deferred,
            blendState: BlendStates.AlphaBlend,
            samplerState: GraphicsDevice.SamplerStates.PointClamp,
            depthStencilState: DepthStencilStates.None);

        foreach (var comp in _fullscreenQueue)
        {
            if (!TryGetCurrentTexture(comp, out var texture) || texture == null)
                continue;

            _spriteBatch.Draw(texture,
                new RectangleF(0, 0, backBuffer.Width, backBuffer.Height),
                Color.White);
        }

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
            if (!TryGetCurrentTexture(comp, out var texture) || texture == null)
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
        StridePlatformGraphics.BeginShutdown();

        // Textures in _textures are owned by StrideRenderSurface — don't dispose them here
        _textures.Clear();
        _atlasManager?.Dispose();
        _sprite3DBatch?.Dispose();
        _spriteBatch?.Dispose();

        // Dispose the shared GRContext
        StridePlatformGraphics.ResetSharedContext();

        base.Destroy();
    }
}
