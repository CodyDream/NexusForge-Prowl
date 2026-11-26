// Runtime driver for the imported OceanFFT.compute shader.
//
// The shader declares five kernels and nine image/uniform resources but ships with no C# side. This
// component owns the dispatch order and the OceanParams cbuffer values; the water material only needs
// to read the resulting displacement/normal/foam textures.
//
// EngineObjects (assets and render textures) are always checked with IsValid()/IsNotValid(), never with
// a null comparison or a null-forgiving operator: a destroyed asset is still a live reference.

using System;
using System.Diagnostics.CodeAnalysis;

using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace UnityEngine;

/// <summary>
/// Drives the five kernels of OceanFFT.compute: spectrum synthesis, time evolution, two FFT passes and
/// the normal/foam generation pass. Assign a Prowl <see cref="ComputeShader"/> imported from that file
/// plus its render textures, then let Update run.
/// </summary>
public sealed class OceanFFTDriver : Component
{
    /// <summary>Size of one side of the heightfield, the N constant in the shader.</summary>
    public const int DefaultResolution = 256;

    /// <summary>numthreads of GenerateSpectrum, TimeEvolution and GenerateNormalsAndFoam.</summary>
    public const int SmallKernelGroupSize = 16;

    /// <summary>numthreads of the FFT passes (one thread handles one of two array halves).</summary>
    public const int FFTGroupSize = 128;

    // ---------------------------------------------------------------- resources

    /// <summary>The ComputeShader imported from OceanFFT.compute.</summary>
    public ComputeShader? Compute;

    public RenderTexture? _H0Texture;
    public RenderTexture? _TempDx;
    public RenderTexture? _TempDy;
    public RenderTexture? _TempDz;
    public RenderTexture? _OceanDisplacement;
    public RenderTexture? _OceanNormal;
    public RenderTexture? _OceanFoam;

    /// <summary>Previous frame's foam, sampled by GenerateNormalsAndFoam. May be a Texture2D or a RenderTexture.</summary>
    public Texture? _FoamHistory;

    // ---------------------------------------------------------------- OceanParams

    public float _LengthScale = DefaultResolution;
    public float _WindSpeed = 12f;
    public float _WindDirX = 1f;
    public float _WindDirZ = 0f;
    public float _WaveAmplitude = 1f;
    public float _ChopScale = 1f;
    public float _FoamThreshold = 0.5f;
    public float _FoamIntensity = 1f;
    public float _FoamDecayRate = 0.5f;
    public float _FoamGrowthRate = 2f;
    public float _DeltaTime;
    public float _Time;

    /// <summary>Write _DeltaTime and _Time from the engine clock every frame.</summary>
    public bool AutoTime = true;

    /// <summary>Heightfield resolution. Defaults to the shader's N.</summary>
    public int Resolution = DefaultResolution;

    // ---------------------------------------------------------------- state

    private int _generateSpectrum = -1;
    private int _timeEvolution = -1;
    private int _fftHorizontal = -1;
    private int _fftVertical = -1;
    private int _generateNormalsAndFoam = -1;
    private long _lastDispatchFrame = -1;

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

    /// <summary>
    /// Sets a member of the shader's OceanParams cbuffer. Names are the exact cbuffer member names
    /// (for example "_WindSpeed"); these are written to every kernel by ComputeShader.SetFloat.
    /// </summary>
    public void SetParam(string cbufferMember, float value)
    {
        if (Compute.IsValid()) Compute.SetFloat(cbufferMember, value);
    }

    /// <summary>Runs the whole ocean pipeline once, in the shader's required order.</summary>
    public void Dispatch()
    {
        if (!TryPrepare(out ComputeShader shader)) return;

        if (AutoTime)
        {
            _DeltaTime = UnityEngine.Time.deltaTime;
            _Time = UnityEngine.Time.time;
        }

        SetParams();

        // Every kernel is a separate ComputeRun dispatch, so writes are visible to the next one.
        int wideGroups = Math.Max(1, (Resolution + SmallKernelGroupSize - 1) / SmallKernelGroupSize);
        BindTextures(shader, _generateSpectrum);
        shader.Dispatch(_generateSpectrum, wideGroups, wideGroups, 1);

        BindTextures(shader, _timeEvolution);
        shader.Dispatch(_timeEvolution, wideGroups, wideGroups, 1);

        // Each FFT group covers one complete 256-element row/column with its 128 threads.
        BindTextures(shader, _fftHorizontal);
        shader.Dispatch(_fftHorizontal, Math.Max(1, Resolution), 1, 1);

        BindTextures(shader, _fftVertical);
        shader.Dispatch(_fftVertical, Math.Max(1, Resolution), 1, 1);

        BindTextures(shader, _generateNormalsAndFoam);
        shader.Dispatch(_generateNormalsAndFoam, wideGroups, wideGroups, 1);
    }

    private void SetParams()
    {
        // Exact OceanParams member names, written to every kernel by ComputeShader.SetFloat.
        SetParam("_LengthScale", _LengthScale);
        SetParam("_WindSpeed", _WindSpeed);
        SetParam("_WindDirX", _WindDirX);
        SetParam("_WindDirZ", _WindDirZ);
        SetParam("_WaveAmplitude", _WaveAmplitude);
        SetParam("_ChopScale", _ChopScale);
        SetParam("_FoamThreshold", _FoamThreshold);
        SetParam("_FoamIntensity", _FoamIntensity);
        SetParam("_FoamDecayRate", _FoamDecayRate);
        SetParam("_FoamGrowthRate", _FoamGrowthRate);
        SetParam("_DeltaTime", _DeltaTime);
        SetParam("_Time", _Time);
    }

    private void BindTextures(ComputeShader shader, int kernel)
    {
        if (_H0Texture.IsValid()) shader.SetTexture(kernel, "_H0Texture", _H0Texture);
        if (_TempDx.IsValid()) shader.SetTexture(kernel, "_TempDx", _TempDx);
        if (_TempDy.IsValid()) shader.SetTexture(kernel, "_TempDy", _TempDy);
        if (_TempDz.IsValid()) shader.SetTexture(kernel, "_TempDz", _TempDz);
        if (_OceanDisplacement.IsValid()) shader.SetTexture(kernel, "_OceanDisplacement", _OceanDisplacement);
        if (_OceanNormal.IsValid()) shader.SetTexture(kernel, "_OceanNormal", _OceanNormal);
        if (_OceanFoam.IsValid()) shader.SetTexture(kernel, "_OceanFoam", _OceanFoam);
        if (_FoamHistory.IsValid()) shader.SetTexture(kernel, "_FoamHistory", _FoamHistory);
    }

    /// <summary>
    /// Validates everything once per frame. Missing resources report a single warning and stop the
    /// pipeline rather than throwing from FindKernel or dispatching with a dead asset.
    /// </summary>
    private bool TryPrepare([NotNullWhen(true)] out ComputeShader? shader)
    {
        shader = null;

        if (Compute.IsNotValid() || !Compute.HasKernel("GenerateSpectrum") || !Compute.HasKernel("TimeEvolution") ||
            !Compute.HasKernel("FFTHorizontal") || !Compute.HasKernel("FFTVertical") ||
            !Compute.HasKernel("GenerateNormalsAndFoam"))
        {
            Warn("Setup", "OceanFFTDriver needs an imported OceanFFT.compute ComputeShader with all five kernels.");
            return false;
        }

        if (_H0Texture.IsNotValid() || _TempDx.IsNotValid() || _TempDy.IsNotValid() || _TempDz.IsNotValid() ||
            _OceanDisplacement.IsNotValid() || _OceanNormal.IsNotValid() || _OceanFoam.IsNotValid() ||
            _FoamHistory.IsNotValid())
        {
            Warn("Textures", "OceanFFTDriver is missing one or more of its heightfield, displacement, normal, foam or foam-history textures.");
            return false;
        }

        if (Resolution < SmallKernelGroupSize)
        {
            Warn("Resolution", $"OceanFFTDriver Resolution must be at least {SmallKernelGroupSize}.");
            return false;
        }

        ComputeShader valid = Compute;
        _generateSpectrum = valid.FindKernel("GenerateSpectrum");
        _timeEvolution = valid.FindKernel("TimeEvolution");
        _fftHorizontal = valid.FindKernel("FFTHorizontal");
        _fftVertical = valid.FindKernel("FFTVertical");
        _generateNormalsAndFoam = valid.FindKernel("GenerateNormalsAndFoam");
        shader = valid;
        return true;
    }

    private static void Warn(string id, string message)
        => Prowl.Runtime.Debug.LogWarningOnce($"OceanFFTDriver.{id}", message);
}
