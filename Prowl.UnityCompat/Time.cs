// Unity-style Time forwarding to Prowl.Runtime.Time.

namespace UnityEngine;

/// <summary>Minimal Unity-compatible time facade over <see cref="Prowl.Runtime.Time"/>.</summary>
public static class Time
{
    public static float deltaTime => Prowl.Runtime.Time.DeltaTime;
    public static float unscaledDeltaTime => Prowl.Runtime.Time.UnscaledDeltaTime;
    public static float smoothDeltaTime => Prowl.Runtime.Time.SmoothDeltaTime;
    public static float fixedDeltaTime
    {
        get => Prowl.Runtime.Time.FixedDeltaTime;
        set => Prowl.Runtime.Time.FixedDeltaTime = value;
    }

    /// <summary>Time since the active scene's clock started.</summary>
    public static float time => Prowl.Runtime.Time.TimeSinceStartup;

    /// <summary>Unscaled time since the engine started.</summary>
    public static float unscaledTime => Prowl.Runtime.Time.UnscaledTotalTime;

    /// <summary>Unscaled time since the engine started (Unity-style name).</summary>
    public static float realtimeSinceStartup => Prowl.Runtime.Time.UnscaledTotalTime;

    public static float timeScale
    {
        get => Prowl.Runtime.Time.TimeScale;
        set => Prowl.Runtime.Time.TimeScale = value;
    }

    /// <summary>Unity-style frame counter; Prowl's underlying counter is a long.</summary>
    public static int frameCount => (int)Prowl.Runtime.Time.FrameCount;

    /// <summary>The engine's full-width frame counter, without truncation.</summary>
    public static long frameCountLong => Prowl.Runtime.Time.FrameCount;
}
