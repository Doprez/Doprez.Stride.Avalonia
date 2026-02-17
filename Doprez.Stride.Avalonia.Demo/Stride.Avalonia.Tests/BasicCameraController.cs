using System;
using Stride.Core;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Input;

namespace Stride.Avalonia.Tests;

/// <summary>
/// Fly-around camera controller: WASD to move, right-click + drag to look,
/// Q/E for up/down, Shift for speed boost.
/// </summary>
public class BasicCameraController : SyncScript
{
    private const float MaximumPitch = MathUtil.PiOverTwo * 0.99f;

    private Vector3 _upVector;
    private Vector3 _translation;
    private float _yaw;
    private float _pitch;

    public Vector3 KeyboardMovementSpeed { get; set; } = new(5.0f);

    public float SpeedFactor { get; set; } = 5.0f;

    public Vector2 MouseRotationSpeed { get; set; } = new(90.0f, 60.0f);

    public override void Start()
    {
        base.Start();
        _upVector = Vector3.UnitY;
    }

    public override void Update()
    {
        ProcessInput();
        UpdateTransform();
    }

    private void ProcessInput()
    {
        _translation = Vector3.Zero;
        _yaw = 0;
        _pitch = 0;

        // Move with keyboard (WASD + Q/E)
        if (Input.IsKeyDown(Keys.W) || Input.IsKeyDown(Keys.Up))
            _translation.Z = -KeyboardMovementSpeed.Z;
        else if (Input.IsKeyDown(Keys.S) || Input.IsKeyDown(Keys.Down))
            _translation.Z = KeyboardMovementSpeed.Z;

        if (Input.IsKeyDown(Keys.A) || Input.IsKeyDown(Keys.Left))
            _translation.X = -KeyboardMovementSpeed.X;
        else if (Input.IsKeyDown(Keys.D) || Input.IsKeyDown(Keys.Right))
            _translation.X = KeyboardMovementSpeed.X;

        if (Input.IsKeyDown(Keys.Q))
            _translation.Y = -KeyboardMovementSpeed.Y;
        else if (Input.IsKeyDown(Keys.E))
            _translation.Y = KeyboardMovementSpeed.Y;

        // Shift for speed boost
        if (Input.IsKeyDown(Keys.LeftShift) || Input.IsKeyDown(Keys.RightShift))
            _translation *= SpeedFactor;

        // Right-click + drag to look
        if (Input.IsMouseButtonDown(MouseButton.Right))
        {
            Input.LockMousePosition();
            Game.IsMouseVisible = false;

            _yaw = -Input.MouseDelta.X;// * MouseRotationSpeed.X;
            _pitch = -Input.MouseDelta.Y;// * MouseRotationSpeed.Y;
        }
        else
        {
            Input.UnlockMousePosition();
            Game.IsMouseVisible = true;
        }
    }

    private void UpdateTransform()
    {
        var dt = (float)Game.UpdateTime.Elapsed.TotalSeconds;
        _translation *= dt;
        // Note: _yaw and _pitch come from Input.MouseDelta which is already
        // a per-frame displacement, so they must NOT be scaled by dt.

        var rotation = Matrix.RotationQuaternion(Entity.Transform.Rotation);

        // Enforce global up-vector
        var right = Vector3.Cross(rotation.Forward, _upVector);
        var up = Vector3.Cross(right, rotation.Forward);
        right.Normalize();
        up.Normalize();

        // Clamp pitch
        var currentPitch = MathUtil.PiOverTwo - (float)Math.Acos(Vector3.Dot(rotation.Forward, _upVector));
        _pitch = MathUtil.Clamp(currentPitch + _pitch, -MaximumPitch, MaximumPitch) - currentPitch;

        // Apply movement in local space
        Entity.Transform.Position += Vector3.TransformCoordinate(_translation, rotation);

        // Yaw around global up, pitch in local space
        Entity.Transform.Rotation *= Quaternion.RotationAxis(right, _pitch) * Quaternion.RotationAxis(_upVector, _yaw);
    }
}
