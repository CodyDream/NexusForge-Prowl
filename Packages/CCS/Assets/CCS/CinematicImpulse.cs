// Clean-room implementation for the Prowl CCS package.

using System;

using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Vector;

namespace CCS;

/// <summary>
/// A decaying shake. Call <see cref="AddImpulse()"/> from gameplay code; the Brain reads the
/// resulting offsets after it has blended virtual cameras and applies them to the final pose.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("CCS/Cinematic Impulse")]
[ExecutionOrder(-200)]
public sealed class CinematicImpulse : Component
{
    /// <summary>How long a full-strength impulse takes to fade, in seconds.</summary>
    public float DecayTime = 0.35f;

    /// <summary>Positional shake in world units at full strength.</summary>
    public float PositionStrength = 0.4f;

    /// <summary>Rotational shake in degrees at full strength.</summary>
    public float RotationStrength = 6f;

    /// <summary>Roughly how many wobble cycles happen per second.</summary>
    public float ShakeFrequency = 12f;

    /// <summary>Different seeds give different, repeatable shake patterns.</summary>
    public int Seed = 17;

    public Float3 PositionOffset { get; private set; }
    public Quaternion RotationOffset { get; private set; }

    [SerializeIgnore, NonSerialized] private float _energy;
    [SerializeIgnore, NonSerialized] private float _phase;

    public override void OnEnable()
    {
        _energy = 0f;
        _phase = 0f;
        ClearOffsets();
    }

    /// <summary>Starts a full-strength decaying shake.</summary>
    public void AddImpulse() => AddImpulse(1f);

    /// <summary>Starts (or restarts) a shake. Strength is clamped to 0..1; overlapping impulses keep the stronger one.</summary>
    public void AddImpulse(float strength)
    {
        float value = MathF.Clamp01(strength);
        if (value <= 0f) return;
        _energy = MathF.Max(_energy, value);
        _phase = 0f;
    }

    public override void Update()
    {
        if (_energy <= 0.0001f)
        {
            ClearOffsets();
            return;
        }

        float dt = MathF.Max(Time.UnscaledDeltaTime, 0f);
        _phase += dt;

        // e^(-3) is about five percent, so the visible shake ends near DecayTime.
        _energy *= MathF.Exp(-dt / MathF.Max(DecayTime / 3f, 0.001f));
        if (_energy <= 0.0001f)
        {
            ClearOffsets();
            return;
        }

        float time = _phase * MathF.Max(ShakeFrequency, 0.01f);
        Float3 wobble = new Float3(
            Sample(time, 11.3f, Seed),
            Sample(time, 47.9f, Seed + 1),
            Sample(time, 91.7f, Seed + 2));

        PositionOffset = wobble * (PositionStrength * _energy);
        RotationOffset = Quaternion.FromEuler(wobble * (RotationStrength * _energy));
    }

    private void ClearOffsets()
    {
        PositionOffset = Float3.Zero;
        RotationOffset = Quaternion.Identity;
    }

    private static float Sample(float time, float channel, int seed)
        => (Noise.Perlin3D(time, channel, 0f, seed) - 0.5f) * 2f;
}
