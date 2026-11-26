// Runtime driver for the imported ShipWakeSim.compute shader.
//
// The shader simulates a persistent wake map by reading the previous frame's texture and writing the
// current one. This component owns that ping-pong, the ShipData structured buffer and all uniforms;
// the water material samples ResultTexture.

using System;
using System.Diagnostics.CodeAnalysis;

using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace UnityEngine;

/// <summary>
/// CPU-side mirror of the HLSL ShipData struct. Floats only, so it can be uploaded verbatim with
/// ComputeBuffer.SetData: three float2 positions/directions plus speed, beam, length, bowFoam,
/// sternFoam, isActive and pad0 (52 bytes).
/// </summary>
public struct ShipData
{
    public Float2 BowPos;
    public Float2 SternPos;
    public Float2 Forward;
    public float Speed;
    public float Beam;
    public float Length;
    public float BowFoam;
    public float SternFoam;
    public float IsActive;
    public float Pad0;
}

/// <summary>
/// Drives the CSMain kernel of ShipWakeSim.compute once per frame, ping-ponging the wake textures so
/// the next frame decays last frame's height and foam before injecting the ships again.
/// </summary>
public sealed class ShipWakeDriver : Component
{
    /// <summary>Stride of <see cref="ShipData"/> in bytes, matching the HLSL struct.</summary>
    public const int ShipDataStride = 52;

    /// <summary>numthreads(8, 8, 1) in CSMain.</summary>
    public const int KernelGroupSize = 8;

    // ---------------------------------------------------------------- resources

    /// <summary>The ComputeShader imported from ShipWakeSim.compute.</summary>
    public ComputeShader? Compute;

    /// <summary>Last frame's wake map, sampled as _PrevWakeMap.</summary>
    public RenderTexture? _PrevWakeMap;

    /// <summary>This frame's wake map, written as _CurrWakeMap and exposed through <see cref="ResultTexture"/>.</summary>
    public RenderTexture? _CurrWakeMap;

    /// <summary>StructuredBuffer&lt;ShipData&gt; bound as _Ships.</summary>
    public ComputeBuffer? _Ships;

    // ---------------------------------------------------------------- shader parameters

    public Float2 _WorldOffsetUV = Float2.Zero;
    public Float2 _WakeCenterXZ = Float2.Zero;
    public int _TexResolution = 256;
    public int _ActiveShipCount;

    public float _WakeAreaSize = 512f;
    public float _WaveAmplitude = 1f;
    public float _WaveDecayRate = 0.5f;
    public float _FoamDecayRate = 0.9f;
    public float _DeltaTime;

    /// <summary>Write _DeltaTime from the engine clock every frame.</summary>
    public bool AutoTime = true;

    /// <summary>Swap the wake textures after each dispatch, so the output becomes next frame's input.</summary>
    public bool PingPong = true;

    private int _csMain = -1;
    private bool _ownsShips;
    private long _lastDispatchFrame = -1;

    /// <summary>The wake map written by the most recent dispatch. Reads a fresh value after each swap.</summary>
    public RenderTexture? ResultTexture => _CurrWakeMap;

    // ComputeBuffer is a plain IDisposable, not an EngineObject, so validity is a reference check
    // plus its own IsDisposed flag rather than IsValid().
    private bool HasShips => _Ships is not null && !_Ships.IsDisposed;

    public override void OnEnable()
    {
        base.OnEnable();
        Dispatch();
        _lastDispatchFrame = Prowl.Runtime.Time.FrameCount;
    }

    public override void Update()
    {
        // OnEnable already dispatched on this frame.
        if (_lastDispatchFrame == Prowl.Runtime.Time.FrameCount) return;
        Dispatch();
        _lastDispatchFrame = Prowl.Runtime.Time.FrameCount;
    }

    public override void OnDisable()
    {
        // Only release a buffer this component created; an assigned one belongs to its caller.
        if (_ownsShips && HasShips) _Ships.Dispose();
        _ownsShips = false;
        base.OnDisable();
    }

    /// <summary>Sets a float uniform of CSMain by its exact shader name.</summary>
    public void SetParam(string name, float value)
    {
        if (Compute.IsValid()) Compute.SetFloat(name, value);
    }

    /// <summary>Sets an int uniform of CSMain by its exact shader name.</summary>
    public void SetParam(string name, int value)
    {
        if (Compute.IsValid()) Compute.SetInt(name, value);
    }

    /// <summary>
    /// Fills the structured buffer from an array of ships and updates _ActiveShipCount. Reuses the
    /// existing buffer when its capacity is large enough, so this is safe to call every frame.
    /// </summary>
    public void UploadShips(ReadOnlySpan<ShipData> ships, int capacity = -1)
    {
        int needed = Math.Max(1, capacity < ships.Length ? ships.Length : capacity);
        ComputeBuffer? buffer = _Ships;
        if (buffer is null || buffer.IsDisposed || buffer.Count < needed)
        {
            if (_ownsShips && buffer is not null && !buffer.IsDisposed) buffer.Dispose();
            buffer = new ComputeBuffer(needed, ShipDataStride);
            _Ships = buffer;
            _ownsShips = true;
        }

        buffer.SetData(ships, 0);
        _ActiveShipCount = ships.Length;
    }

    /// <summary>Runs CSMain over ceil(resolution / 8) squared thread groups.</summary>
    public void Dispatch()
    {
        if (!TryPrepare(out ComputeShader shader, out ComputeBuffer ships)) return;

        if (AutoTime) _DeltaTime = UnityEngine.Time.deltaTime;

        shader.SetVector("_WorldOffsetUV", _WorldOffsetUV);
        shader.SetVector("_WakeCenterXZ", _WakeCenterXZ);
        shader.SetFloat("_WakeAreaSize", _WakeAreaSize);
        shader.SetFloat("_DeltaTime", _DeltaTime);
        shader.SetFloat("_WaveAmplitude", _WaveAmplitude);
        shader.SetFloat("_WaveDecayRate", _WaveDecayRate);
        shader.SetFloat("_FoamDecayRate", _FoamDecayRate);
        shader.SetInt("_TexResolution", _TexResolution);
        shader.SetInt("_ActiveShipCount", Math.Clamp(_ActiveShipCount, 0, ships.Count));

        shader.SetTexture(_csMain, "_PrevWakeMap", _PrevWakeMap);
        shader.SetTexture(_csMain, "_CurrWakeMap", _CurrWakeMap);
        shader.SetBuffer(_csMain, "_Ships", ships);

        int groups = (_TexResolution + KernelGroupSize - 1) / KernelGroupSize;
        shader.Dispatch(_csMain, groups, groups, 1);

        if (PingPong)
        {
            RenderTexture? previous = _PrevWakeMap;
            _PrevWakeMap = _CurrWakeMap;
            _CurrWakeMap = previous;
        }
    }

    private bool TryPrepare([NotNullWhen(true)] out ComputeShader? shader, [NotNullWhen(true)] out ComputeBuffer? ships)
    {
        shader = null;
        ships = null;

        if (Compute.IsNotValid() || !Compute.HasKernel("CSMain"))
        {
            Warn("Setup", "ShipWakeDriver needs an imported ShipWakeSim.compute ComputeShader with a CSMain kernel.");
            return false;
        }

        if (_PrevWakeMap.IsNotValid() || _CurrWakeMap.IsNotValid())
        {
            Warn("Textures", "ShipWakeDriver needs both ping-pong wake textures assigned.");
            return false;
        }

        ComputeBuffer? buffer = _Ships;
        if (buffer is null || buffer.IsDisposed)
        {
            Warn("Ships", "ShipWakeDriver needs a _Ships ComputeBuffer; call UploadShips or assign a buffer of ShipData.");
            return false;
        }

        if (_TexResolution < 1)
        {
            Warn("Resolution", "ShipWakeDriver needs a positive _TexResolution.");
            return false;
        }

        ComputeShader validShader = Compute;
        ComputeBuffer validShips = buffer;
        _csMain = validShader.FindKernel("CSMain");
        shader = validShader;
        ships = validShips;
        return true;
    }

    private static void Warn(string id, string message)
        => Prowl.Runtime.Debug.LogWarningOnce($"ShipWakeDriver.{id}", message);
}
