using System.Linq;
using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Graphics;
using Stride.Input;
using Stride.Avalonia;

namespace Doprez.Stride.Avalonia.Demo;

/// <summary>
/// Listens for the Escape key and toggles a fullscreen pause menu overlay.
/// The menu offers Resume, Settings (scene-UI count, camera controls, window
/// settings), and Exit.
/// <para>
/// Attach this script to the same entity that has the camera or to a
/// dedicated "Systems" entity.  Assign <see cref="CameraController"/> and
/// <see cref="GridSpawner"/> in GameStudio (or from code) so the settings
/// panel can read/write their values at runtime.
/// </para>
/// </summary>
public class EscapeMenuScript : SyncScript
{
    private EscapeMenuControl? _menuControl;
    private AvaloniaComponent? _avaloniaComponent;
    private bool _isOpen;

    /// <summary>
    /// Reference to the <see cref="BasicCameraController"/> in the scene.
    /// Used by the Settings page to adjust movement/rotation speeds.
    /// </summary>
    [DataMember(10)]
    public BasicCameraController? CameraController { get; set; }

    /// <summary>
    /// Reference to the <see cref="AvaloniaGridSpawner"/> in the scene.
    /// Used by the Settings page to change the grid size at runtime.
    /// </summary>
    [DataMember(20)]
    public AvaloniaGridSpawner? GridSpawner { get; set; }

    public override void Start()
    {
        _menuControl = new EscapeMenuControl();

        // ── Wire up main menu events ────────────────────────────────
        _menuControl.ResumeClicked += () => CloseMenu();
        _menuControl.ExitClicked += () =>
        {
            ((Game)Game).Exit();
        };

        // ── Wire up settings events ─────────────────────────────────
        var settings = _menuControl.Settings;

        // Scene UI
        settings.GridSizeChanged += gridSize =>
        {
            if (GridSpawner != null)
            {
                GridSpawner.GridSize = gridSize;
                GridSpawner.Respawn();
            }
        };

        // Camera controls
        settings.MoveSpeedChanged += speed =>
        {
            if (CameraController != null)
                CameraController.KeyboardMovementSpeed = new Vector3(speed);
        };

        settings.RotationSpeedChanged += speed =>
        {
            if (CameraController != null)
                CameraController.KeyboardRotationSpeed = new Vector2(speed);
        };

        settings.SprintMultiplierChanged += mult =>
        {
            if (CameraController != null)
                CameraController.SpeedFactor = mult;
        };

        settings.MouseRotationSpeedChanged += speed =>
        {
            if (CameraController != null)
                CameraController.MouseRotationSpeed = new Vector2(speed, speed);
        };

        // Window settings
        settings.WindowModeChanged += mode =>
        {
            var window = Game.Window;
            var device = ((Game)Game).GraphicsDevice;

            switch (mode)
            {
                case "Windowed":
                    window.IsFullscreen = false;
                    window.IsBorderLess = false;
                    break;

                case "Fullscreen Windowed":
                    window.IsFullscreen = false;
                    window.IsBorderLess = true;
                    // Stretch to fill the current screen
                    var adapter = device.Adapter;
                    if (adapter?.Outputs != null && adapter.Outputs.Length > 0)
                    {
                        var displayMode = adapter.Outputs[0].CurrentDisplayMode;
                        window.SetSize(new Int2(displayMode.Width, displayMode.Height));
                        window.Position = Int2.Zero;
                    }
                    break;

                case "Fullscreen Exclusive":
                    window.IsFullscreen = true;
                    break;
            }
        };

        settings.ResolutionChanged += (width, height) =>
        {
            Game.Window.SetSize(new Int2(width, height));
        };

        settings.ResizableChanged += resizable =>
        {
            Game.Window.AllowUserResizing = resizable;
        };

        // ── Sync initial values ─────────────────────────────────────
        SyncSettingsFromGame();

        // ── Create the AvaloniaComponent ────────────────────────────
        // The component is always Enabled so that the headless window
        // is created immediately (by the renderer's first DrawCore).
        // Visibility is controlled via the Avalonia control's IsVisible
        // property instead, which avoids a one-frame delay where the
        // window doesn't exist yet and also avoids the shared-pointer
        // capture issue entirely when the menu is hidden.
        _menuControl.IsVisible = false;

        var page = new DefaultAvaloniaPage(_menuControl);

        _avaloniaComponent = new AvaloniaComponent
        {
            IsFullScreen = true,
            Resolution = new Vector2(
                Game.Window.ClientBounds.Width,
                Game.Window.ClientBounds.Height),
            Page = page,
            ContinuousRedraw = false,
        };

        Entity.Add(_avaloniaComponent);
    }

    public override void Update()
    {
        if (Input.IsKeyPressed(Keys.Escape))
        {
            if (_isOpen)
                CloseMenu();
            else
                OpenMenu();
        }

        // Keep resolution in sync with window size while open
        if (_isOpen && _avaloniaComponent != null)
        {
            _avaloniaComponent.Resolution = new Vector2(
                Game.Window.ClientBounds.Width,
                Game.Window.ClientBounds.Height);
        }
    }

    private void OpenMenu()
    {
        if (_avaloniaComponent == null || _menuControl == null) return;

        _isOpen = true;
        _menuControl.IsVisible = true;
        _avaloniaComponent.ContinuousRedraw = true;
        _avaloniaComponent.Page?.MarkDirty();
        _menuControl.ResetToMainMenu();
        SyncSettingsFromGame();

        // Show cursor & disable camera
        Input.UnlockMousePosition();
        Game.IsMouseVisible = true;
        if (CameraController != null)
            CameraController.IsActive = false;
    }

    private void CloseMenu()
    {
        if (_avaloniaComponent == null || _menuControl == null) return;

        _isOpen = false;
        _menuControl.IsVisible = false;
        _avaloniaComponent.ContinuousRedraw = false;
        _avaloniaComponent.Page?.MarkDirty();

        // Restore camera & hide cursor (camera controller will
        // re-show it when the user isn't right-click rotating)
        Game.IsMouseVisible = false;
        if (CameraController != null)
            CameraController.IsActive = true;
    }

    /// <summary>
    /// Reads current values from the game objects and pushes them to the UI.
    /// </summary>
    private void SyncSettingsFromGame()
    {
        if (_menuControl == null) return;
        var settings = _menuControl.Settings;

        if (GridSpawner != null)
            settings.SetGridSize(GridSpawner.GridSize);

        if (CameraController != null)
        {
            settings.SetMoveSpeed(CameraController.KeyboardMovementSpeed.X);
            settings.SetRotationSpeed(CameraController.KeyboardRotationSpeed.X);
            settings.SetSprintMultiplier(CameraController.SpeedFactor);
            settings.SetMouseRotationSpeed(CameraController.MouseRotationSpeed.X);
        }

        settings.SetResizable(Game.Window.AllowUserResizing);
    }

    public override void Cancel()
    {
        if (_avaloniaComponent != null)
        {
            _avaloniaComponent.Page?.Dispose();
            Entity.Remove(_avaloniaComponent);
            _avaloniaComponent = null;
        }
        _menuControl = null;
    }
}
