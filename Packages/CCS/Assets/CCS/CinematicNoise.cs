// Clean-room implementation for the Prowl CCS package.
// Uses Prowl.Runtime.Noise, which returns 0..1 with 0.5 as neutral.

using System;

using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Vector;

namespace CCS;

/// <summary>
/// Procedural handheld wobble. Add it to a virtual camera (or to the Brain for a global wobble)
/// and the Brain applies its offsets to the final camera pose.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("CCS/Cinematic Noise")]
[ExecutionOrder(-200)]
public sealed class CinematicNoise : Component
{
    /// <summary>Positional wobble in world units.</summary>
    public float PositionAmplitude = 0.15f;

    /// <summary>How quickly the positional wobble changes.</summary>
    public float PositionFrequency = 0.8f;

    /// <summary>Rotational wobble in degrees.</summary>
    public float RotationAmplitude = 1.5f;

    /// <summary>How quickly the rotational wobble changes.</summary>
    public float RotationFrequency = 1.1f;

    /// <summary>Different seeds give different, repeatable wobble patterns.</summary>
    public int Seed;

    public Float3 PositionOffset { get; private set; }
    public Quaternion RotationOffset { get; private set; }

    public override void OnEnable()
    {
        // A default Quaternion value is not necessarily an identity rotation.
        RotationOffset = Quaternion.Identity;
        PositionOffset = Float3.Zero;
    }

    public override void Update()
    {
        float time = Time.UnscaledTotalTime;
        float pt = time * PositionFrequency;
        float rt = time * RotationFrequency;

        Float3 shift = new Float3(
            Sample(pt, 11.3f, Seed),
            Sample(pt, 47.9f, Seed + 1),
            Sample(pt, 91.7f, Seed + 2));

        Float3 tilt = new Float3(
            Sample(rt, 17.1f, Seed + 3),
            Sample(rt, 63.5f, Seed + 4),
            Sample(rt, 109.2f, Seed + 5));

        PositionOffset = shift * PositionAmplitude;
        RotationOffset = Quaternion.FromEuler(tilt * RotationAmplitude);
    }

    /// <summary>Perlin noise remapped to -1..1 so zero means no offset.</summary>
    private static float Sample(float time, float channel, int seed)
        => (Noise.Perlin3D(time, channel, 0f, seed) - 0.5f) * 2f;
}
