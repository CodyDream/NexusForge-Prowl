// Clean-room implementation for the Prowl CCS package.

using System;

using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Vector;

namespace CCS;

/// <summary>
/// Finds the enabled virtual camera with the highest priority, blends from the previously active
/// pose to its desired pose with exponential smoothing, adds noise/impulse offsets, and writes the
/// result to a Camera every frame. Put it on the camera you want to drive, or let it use the
/// scene's main camera.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("CCS/Cinematic Camera Brain")]
[ExecutionOrder(1000)]
public sealed class CinematicCameraBrain : Component
{
    /// <summary>The virtual camera currently being blended to. Not serialized: it is runtime state.</summary>
    public CinematicVirtualCamera? ActiveCamera => _active;

    [SerializeIgnore, NonSerialized] private CinematicVirtualCamera? _active;
    [SerializeIgnore, NonSerialized] private Float3 _position;
    [SerializeIgnore, NonSerialized] private Quaternion _rotation;
    [SerializeIgnore, NonSerialized] private float _fov;
    [SerializeIgnore, NonSerialized] private float _blendElapsed;
    [SerializeIgnore, NonSerialized] private bool _hasPose;

    public override void OnEnable()
    {
        _active = null;
        _hasPose = false;
        _blendElapsed = 0f;
    }

    public override void LateUpdate()
    {
        if (!IsInWorld) return;

        CinematicVirtualCamera? best = SelectBestCamera();
        if (best.IsNotValid()) return;

        bool switched;
        if (_active.IsNotValid())
        {
            _active = best;
            _hasPose = false;
            switched = true;
        }
        else
        {
            switched = !ReferenceEquals(_active, best);
            if (switched) _active = best;
        }

        Float3 targetPosition = best.DesiredPosition;
        Quaternion targetRotation = best.DesiredRotation;
        float targetFov = best.DesiredFOV;

        float blendTime = MathF.Max(best.BlendTime, 0f);
        if (!_hasPose || blendTime <= 0f)
        {
            // First frame, or a zero-length blend: cut to the new shot.
            _position = targetPosition;
            _rotation = targetRotation;
            _fov = targetFov;
            _hasPose = true;
            _blendElapsed = blendTime;
        }
        else
        {
            if (switched) _blendElapsed = 0f;
            float dt = MathF.Max(Time.DeltaTime, 0f);
            _blendElapsed += dt;

            if (_blendElapsed >= blendTime)
            {
                // The blend is over: follow the virtual camera exactly from here on.
                _position = targetPosition;
                _rotation = targetRotation;
                _fov = targetFov;
                _blendElapsed = blendTime;
            }
            else
            {
                float alpha = MathF.Clamp01(1f - MathF.Exp(-3f * dt / MathF.Max(blendTime, 0.0001f)));
                _position += (targetPosition - _position) * alpha;
                _rotation = Quaternion.Slerp(_rotation, targetRotation, alpha);
                _fov += (targetFov - _fov) * alpha;
            }
        }

        // Keep the offsets out of the blend state: they are a per-frame final touch, not part of the shot.
        ApplyOffsets(best, out Float3 finalPosition, out Quaternion finalRotation);
        WriteToCamera(finalPosition, finalRotation);
    }

    private CinematicVirtualCamera? SelectBestCamera()
    {
        CinematicVirtualCamera? best = null;
        foreach (CinematicVirtualCamera candidate in Scene.GetAll<CinematicVirtualCamera>())
        {
            if (candidate.IsNotValid() || candidate.IsDestroyQueued) continue;
            if (best.IsNotValid())
            {
                best = candidate;
                continue;
            }
            if (candidate.Priority > best.Priority ||
                (candidate.Priority == best.Priority && candidate.InstanceID < best.InstanceID))
                best = candidate;
        }
        return best;
    }

    /// <summary>
    /// Noise and impulse are applied after the blend so a shake is not smoothed away during a cut.
    /// Components on the active virtual camera are gated by that camera's flags; components on the
    /// Brain itself are global and always on while they are enabled.
    /// </summary>
    private void ApplyOffsets(CinematicVirtualCamera source, out Float3 finalPosition, out Quaternion finalRotation)
    {
        finalPosition = _position;
        finalRotation = _rotation;

        if (source.EnableNoise)
        {
            CinematicNoise? noise = source.GetComponent<CinematicNoise>();
            if (noise.IsValid() && noise.EnabledInHierarchy)
            {
                finalPosition += noise.PositionOffset;
                finalRotation = noise.RotationOffset * finalRotation;
            }
        }

        if (source.EnableImpulse)
        {
            CinematicImpulse? impulse = source.GetComponent<CinematicImpulse>();
            if (impulse.IsValid() && impulse.EnabledInHierarchy)
            {
                finalPosition += impulse.PositionOffset;
                finalRotation = impulse.RotationOffset * finalRotation;
            }
        }

        CinematicNoise? globalNoise = GetComponent<CinematicNoise>();
        if (globalNoise.IsValid() && globalNoise.EnabledInHierarchy)
        {
            finalPosition += globalNoise.PositionOffset;
            finalRotation = globalNoise.RotationOffset * finalRotation;
        }

        CinematicImpulse? globalImpulse = GetComponent<CinematicImpulse>();
        if (globalImpulse.IsValid() && globalImpulse.EnabledInHierarchy)
        {
            finalPosition += globalImpulse.PositionOffset;
            finalRotation = globalImpulse.RotationOffset * finalRotation;
        }

        finalRotation = Quaternion.NormalizeSafe(finalRotation);
    }

    private void WriteToCamera(Float3 position, Quaternion rotation)
    {
        Camera? output = ResolveOutputCamera();
        if (output.IsNotValid()) return;

        Transform cameraTransform = output.Transform;
        cameraTransform.Position = position;
        cameraTransform.Rotation = rotation;

        if (output.ProjectionMode == Camera.ProjectionType.Perspective)
            output.FieldOfView = MathF.Clamp(_fov, 1f, 179f);
    }

    private Camera? ResolveOutputCamera()
    {
        Camera? own = GetComponent<Camera>();
        if (own.IsValid() && own.EnabledInHierarchy) return own;

        Camera? main = Scene.MainCamera;
        if (main.IsValid()) return main;

        Camera? fallback = Camera.Main;
        return fallback.IsValid() ? fallback : null;
    }
}
