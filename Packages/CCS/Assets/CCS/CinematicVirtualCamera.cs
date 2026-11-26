// Clean-room implementation for the Prowl CCS package.

using System;

using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Vector;

namespace CCS;

/// <summary>
/// One shot: a desired pose the Brain can blend to. Place these on empty rig objects, give them
/// priorities, and optionally attach CinematicNoise / CinematicImpulse to the same object.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("CCS/Cinematic Virtual Camera")]
[ExecutionOrder(-100)]
public sealed class CinematicVirtualCamera : Component
{
    /// <summary>Higher priority wins. Equal priorities prefer the older camera (lower InstanceID).</summary>
    public int Priority;

    /// <summary>Seconds the Brain spends blending to this camera. Zero cuts immediately.</summary>
    public float BlendTime = 1f;

    /// <summary>Where the camera sits: this object's position plus PositionOffset, or the target's position plus the offset.</summary>
    public GameObject? FollowTarget;

    /// <summary>What the camera looks at. When empty the rig object's own rotation is used.</summary>
    public GameObject? LookAtTarget;

    /// <summary>World-space offset added to the followed position.</summary>
    public Float3 PositionOffset = Float3.Zero;

    /// <summary>Damping smooth time in seconds for position. Zero follows the target exactly.</summary>
    public float PositionDamping;

    /// <summary>Damping smooth time in seconds for rotation. Zero follows the target exactly.</summary>
    public float RotationDamping;

    /// <summary>Damping smooth time in seconds for field of view.</summary>
    public float FOVDamping;

    /// <summary>Desired field of view in degrees, clamped to 1..179.</summary>
    public float FOV = 60f;

    /// <summary>Let the Brain apply CinematicNoise from this object.</summary>
    public bool EnableNoise = true;

    /// <summary>Let the Brain apply CinematicImpulse from this object.</summary>
    public bool EnableImpulse = true;

    /// <summary>The damped pose, before noise and impulse. Read by the Brain in LateUpdate.</summary>
    public Float3 DesiredPosition { get; private set; }

    public Quaternion DesiredRotation { get; private set; }

    public float DesiredFOV { get; private set; } = 60f;

    [SerializeIgnore, NonSerialized] private Float3 _posePosition;
    [SerializeIgnore, NonSerialized] private Quaternion _poseRotation;
    [SerializeIgnore, NonSerialized] private float _poseFov;
    [SerializeIgnore, NonSerialized] private bool _initialized;

    public override void OnEnable()
    {
        _poseRotation = Quaternion.Identity;
        _initialized = false;
    }

    public override void Update()
    {
        float dt = MathF.Max(Time.DeltaTime, 0f);

        Float3 basePosition = Transform.Position + PositionOffset;
        GameObject? follow = FollowTarget;
        if (follow.IsValid())
            basePosition = follow.Transform.Position + PositionOffset;

        Quaternion baseRotation = Transform.Rotation;
        GameObject? lookAt = LookAtTarget;
        if (lookAt.IsValid())
        {
            Float3 toTarget = lookAt.Transform.Position - basePosition;
            if (Float3.LengthSquared(toTarget) > 1e-8f)
                baseRotation = Quaternion.LookRotation(Float3.Normalize(toTarget), Float3.UnitY);
        }

        float baseFov = MathF.Clamp(FOV, 1f, 179f);

        if (!_initialized)
        {
            _posePosition = basePosition;
            _poseRotation = baseRotation;
            _poseFov = baseFov;
            _initialized = true;
        }
        else
        {
            _posePosition += (basePosition - _posePosition) * DampingAlpha(PositionDamping, dt);
            _poseRotation = Quaternion.Slerp(_poseRotation, baseRotation, DampingAlpha(RotationDamping, dt));
            _poseFov += (baseFov - _poseFov) * DampingAlpha(FOVDamping, dt);
        }

        DesiredPosition = _posePosition;
        DesiredRotation = Quaternion.NormalizeSafe(_poseRotation);
        DesiredFOV = _poseFov;
    }

    /// <summary>Frame-rate independent exponential smoothing factor.</summary>
    private static float DampingAlpha(float smoothTime, float dt)
    {
        if (smoothTime <= 0f) return 1f;
        if (dt <= 0f) return 0f;
        return MathF.Clamp01(1f - MathF.Exp(-dt / MathF.Max(smoothTime, 0.0001f)));
    }
}
