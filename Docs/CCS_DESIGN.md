# Cinematic Camera System (CCS) — Design Document

**Status:** Design proposal (clean-room). No implementation code ships with this document.
**Applies to:** Prowl runtime + editor. "Prowl Camera" below means Prowl's existing real camera component that actually renders a view; CCS only computes and writes a pose into it.

This document specifies a camera system built from first principles for Prowl. It does not port or reproduce any existing third-party camera system. All names, math, and behavior below are defined here so the system can be implemented without external references.

---

## 1. Goals

- **G1 — Declarative camera authoring.** Level designers place virtual cameras as scene components and set a priority; the system automatically decides which one is "live" with no scripting.
- **G2 — Automatic, smooth blending.** When the live camera changes, position, rotation, and FOV interpolate smoothly over a configurable duration and curve — no hard cuts unless requested.
- **G3 — Reusable follow/look-at behaviors.** `CinematicFollow` and `CinematicLookAt` are standalone components that can be attached to any virtual camera (or even other objects) with configurable smooth time and max speed.
- **G4 — Frame-rate independence.** All damping and blending use exponential (frame-rate-independent) math, not per-frame lerp constants.
- **G5 — Procedural life.** Layered Perlin-style noise adds subtle handheld motion; one-shot impulses add impact shakes (explosions, hits, landings).
- **G6 — Simple runtime API.** Gameplay code can request a camera change and fire an impulse with one line, without knowing internal state.
- **G7 — Deterministic, testable core.** Given the same inputs and delta time, the brain produces the same pose. Core math lives in pure functions that unit tests can call without a scene.
- **G8 — Editor-friendly.** Everything is inspectable and authorable through standard component inspector fields, with a clear setup checklist.

---

## 2. Non-Goals

- **NG1** — Not a full cutscene sequencer / timeline editor. Triggering cameras at times is the game/animation system's job.
- **NG2** — No spline/dolly paths, no camera collision/obstacle avoidance in v1 (see Phase 4).
- **NG3** — No 2D/orthographic camera modes; v1 targets perspective gameplay cameras.
- **NG4** — No post-processing, depth-of-field, or lens effects; CCS only outputs position, rotation, and FOV.
- **NG5** — No network replication of camera state (camera is client-local).
- **NG6** — No save/load of transient blend/noise/impulse state; these are re-derived at runtime.
- **NG7** — Not a replacement for the Prowl Camera itself; CCS is a pose provider, never the renderer.

---

## 3. Core Concepts & Glossary

| Term | Meaning |
|---|---|
| **Virtual camera (vcam)** | A data component (`CinematicVirtualCamera`) describing a desired pose. It does not render. |
| **Live vcam** | The enabled vcam with the highest priority (ties broken deterministically). |
| **Brain** | `CinematicCameraBrain`: selects the live vcam, blends between old and new, applies noise + impulses, writes to the real camera. |
| **Blend** | A timed transition from one vcam's pose to another's, driven by a blend hint and curve. |
| **Pose** | `CameraPose`: position (`Vector3`), rotation (`Quaternion`), field of view (`float`). |
| **Damping** | Frame-rate-independent exponential approach toward a target (see §7). |
| **Noise** | Layered Perlin-style offsets added to the final pose after blending. |
| **Impulse** | A one-shot, decaying shake contribution from an event at a world position. |

Design rule: **the brain owns the final transform; vcams only describe desired state.** A vcam never writes to the real camera directly.

---

## 4. Public Types, Fields & API Signatures (C# pseudocode)

All types live in namespace `Prowl.CinematicCamera`. Types are ordinary Prowl components unless noted. Signatures are illustrative; exact Prowl base-class/attribute names should follow existing conventions in `Prowl.Runtime`.

### 4.1 `CameraPose` (value type)

```csharp
public struct CameraPose
{
    public Vector3 Position;
    public Quaternion Rotation;
    public float FieldOfView;

    public static CameraPose Lerp(CameraPose a, CameraPose b, float t);
}
```

`Lerp` uses `Vector3.Lerp`, `Quaternion.Slerp` (shortest path), and scalar lerp for FOV.

### 4.2 `CinematicBlendHint` (enum)

```csharp
public enum CinematicBlendHint
{
    Linear,        // straight-line position interpolation (default)
    Spherical,     // slerp position around blend anchor — useful for orbit-style moves
    Freeze,        // hold outgoing position, rotate toward incoming (cut-like but soft)
   None           // hard cut: instantly adopt incoming pose
}
```

### 4.3 `CinematicEase` (enum + helper)

```csharp
public enum CinematicEase { Linear, SmoothStep, CubicInOut, EaseOut }

public static class CinematicEasing
{
    public static float Evaluate(CinematicEase ease, float t01);
}
```

### 4.4 `CinematicFollow` — reusable follow behavior

```csharp
[DisallowMultipleComponent]
public sealed class CinematicFollow : MonoBehaviour
{
    [Header("Target")]
    public Transform Target;              // object to follow; null = no effect
    public Vector3 Offset;                // local-space offset from target
    public bool OffsetInWorldSpace = false;

    [Header("Damping")]
    [Min(0f)] public float SmoothTime = 0.3f;   // time constant, seconds (see §7)
    [Min(0f)] public float MaxSpeed = 0f;       // 0 = unlimited
    public bool FollowPosition = true;
    public bool FollowRotation = false;         // optional orientation copy

    // Pure/evaluable core: returns damped position given current, target, velocity state, dt.
    public Vector3 Evaluate(Vector3 current, Vector3 desired, ref Vector3 velocity, float dt);
}
```

### 4.5 `CinematicLookAt` — reusable look-at behavior

```csharp
[DisallowMultipleComponent]
public sealed class CinematicLookAt : MonoBehaviour
{
    [Header("Target")]
    public Transform Target;                 // object to look at; null = no effect
    public Vector3 TargetOffset;             // offset applied to target position
    public Vector3 Up = Vector3.Up;          // up vector for look rotation

    [Header("Damping")]
    [Min(0f)] public float SmoothTime = 0.25f;
    [Min(0f)] public float MaxDegreesPerSecond = 0f; // 0 = unlimited
    public bool DampRotation = true;

    // Returns damped look quaternion given current, desired, angular velocity state, dt.
    public Quaternion Evaluate(Quaternion current, Quaternion desired,
                               ref Quaternion angularVelocity, float dt);
}
```

Notes: `Evaluate` methods are pure(with a `ref` velocity state) so tests can call them with invented state. When `DampRotation == false`, `Evaluate` returns `desired` directly.

### 4.6 `CinematicNoise` — layered Perlin-style noise

```csharp
[Serializable]
public sealed class NoiseLayer
{
    public bool Enabled = true;
    public string Name = "Layer";
    public float Frequency = 1.0f;     // cycles per second for time; per world unit for position seed
    [Min(0f)] public float PositionAmplitude = 0f;  // meters of positional offset
    [Min(0f)] public float RotationAmplitude = 0f;  // degrees of rotational offset (pitch/yaw/roll)
    [Min(0f)] public float FOVAmplitude = 0f;       // degrees of FOV wobble
    [Range(0f, 1f)] public float BlendWeight = 1f;  // contribution of this layer
}

[DisallowMultipleComponent]
public sealed class CinematicNoise : MonoBehaviour
{
    public bool Enabled = true;
    public Vector3 PositionScale = Vector3.One;   // per-axis multiplier on positional noise
    public Vector3 RotationScale = Vector3.One;   // per-axis multiplier (x=pitch,y=yaw,z=roll)
    public float TimeScale = 1f;
    public List<NoiseLayer> Layers = new();

    // Deterministic sample: pure function of (seed, time, layer) — no global RNG state.
    public static (Vector3 pos, Vector3 rotEuler, float fov) SampleLayer(
        int seed, float time, NoiseLayer layer);

    // Aggregates all enabled layers with their blend weights.
    public (Vector3 pos, Quaternion rot, float fov) Evaluate(float time, Vector3 worldAnchor);
}
```

Noise is deterministic: each layer uses `seed` mixed with `time * Frequency` and `worldAnchor` to sample a smooth value-noise/Perlin-style function (`Noise1D`/`Noise2D` helper). No use of a global random generator, so blends and tests stay reproducible.

### 4.7 `CinematicImpulse` — one-shot impulses

```csharp
[Serializable]
public struct ImpulseShape
{
    [Min(0f)] public float Duration;        // total lifetime, seconds
    [Min(0f)] public float RiseTime;        // 0 = immediate attack
    public CinematicEase Decay;             // curve applied over the lifetime
    [Min(0f)] public float PositionStrength; // meters at full intensity
    [Min(0f)] public float RotationStrength; // degrees at full intensity
    [Min(0f)] public float FOVStrength;      // degrees at full intensity
    public NoiseLayer Shake;                // layered noise used as the shake signal
}

public readonly struct ImpulseHandle
{
    public int Id { get; }
    public bool IsValid { get; }
    public void Stop();                     // cancel early (fade handled by brain)
}

public static class CinematicImpulse
{
    // Fire an impulse at a world position. Returns a handle for optional early stop.
    public static ImpulseHandle Play(
        ImpulseShape shape,
        Vector3 worldPosition,
        float intensity = 1f,
        float radius = 0f,                 // 0 = global (no falloff)
        int seed = 0);

    // Fire using a named preset registered in a CinematicImpulseSettings asset.
    public static ImpulseHandle PlayPreset(
        string presetName, Vector3 worldPosition, float intensity = 1f, float radius = 0f);

    // Query / maintenance (normally called by the brain, exposed for debugging).
    public static int ActiveCount { get; }
    public static void Update(float dt);
    public static void ClearAll();
}
```

Distance falloff (when `radius > 0`): `intensityScale = 1 - clamp(distance/radius, 0, 1)`, optionally squared. Impulses are stored in a static list of active records with `Id`, remaining time, shape, seed, and origin. The brain samples active impulses each frame and adds their contribution to the post-blend pose.

### 4.8 `CinematicVirtualCamera` — the pose authoring component

```csharp
[DisallowMultipleComponent]
public sealed class CinematicVirtualCamera : MonoBehaviour
{
    [Header("State")]
    public bool Enabled = true;
    public int Priority = 0;                // higher wins; ties resolved by InstanceId (stable)
    public CinematicBlendHint BlendHint = CinematicBlendHint.Linear;

    [Header("Blend Override (optional)")]
    public bool OverrideBlendDuration = false;
    [Min(0f)] public float BlendDuration = 0.8f;  // used when this vcam becomes live
    public CinematicEase BlendCurve = CinematicEase.SmoothStep;

    [Header("Pose Source")]
    public CameraPoseSource PoseSource = CameraPoseSource.Manual;

    [Header("Manual Pose")]
    public Vector3 ManualPosition;
    public Vector3 ManualEulerAngles;

    [Header("Follow (used when PoseSource == FollowTarget)")]
    public CinematicFollow Follow;          // reference to a follow behavior on this object
    public CinematicLookAt LookAt;          // reference to a look-at behavior on this object

    [Header("FOV")]
    public float FieldOfView = 60f;

    [Header("Effects")]
    public CinematicNoise Noise;            // optional; null = no noise
    public bool ReceiveImpulses = true;     // if false, impulses ignored when this vcam is live

    // Computes the desired pose for this frame from its configured source.
    public CameraPose GetDesiredPose(float dt);

    // Convenience: priority comparison used by the brain (stable tie-break).
    public int EffectivePriority { get; }
}
```

```csharp
public enum CameraPoseSource { Manual, FollowTarget, ManualPositionLookAtTarget }
```

- `Manual` — pose is `ManualPosition` + `ManualEulerAngles`.
- `FollowTarget` — position from `Follow`, rotation from `LookAt` (if present), otherwise manual rotation.
- `ManualPositionLookAtTarget` — fixed position, rotation from `LookAt`.

### 4.9 `CinematicCameraBrain` — the orchestrator

```csharp
[DisallowMultipleComponent]
public sealed class CinematicCameraBrain : MonoBehaviour
{
    // Singleton-ish access used by gameplay code.
    public static CinematicCameraBrain Instance { get; private set; }

    [Header("Output")]
    public Camera TargetCamera;             // the real Prowl Camera to drive; auto-resolved if null

    [Header("Default Blending")]
    [Min(0f)] public float DefaultBlendDuration = 0.8f;
    public CinematicEase DefaultBlendCurve = CinematicEase.SmoothStep;
    public CinematicBlendHint DefaultBlendHint = CinematicBlendHint.Linear;

    [Header("Global Effects")]
    public CinematicNoise GlobalNoise;      // optional noise applied to ALL poses
    public bool ImpulsesEnabled = true;

    // Runtime read-only introspection.
    public CinematicVirtualCamera LiveCamera { get; }
    public bool IsBlending { get; }
    public CameraPose CurrentPose { get; }

    // Public control API for scripting.
    public void SetLiveCamera(CinematicVirtualCamera vcam);           // manual override, blends
    public void ClearOverride();                                     // return to priority-based selection
    public void CutTo(CinematicVirtualCamera vcam);                   // instant, no blend
    public void FireImpulse(ImpulseShape shape, Vector3 position,
                            float intensity = 1f, float radius = 0f); // convenience wrapper

    // Unity/Prowl lifecycle — called every frame.
    private void Awake();
    private void LateUpdate();  // runs after animation/movement so poses are current
}
```

`Awake` registers `Instance` (first brain wins; extra brains log a warning). `LateUpdate` runs the frame flow in §6. The brain writes the final pose into `TargetCamera.transform` and `TargetCamera.fieldOfView` (name adjusted to Prowl's real camera field names at implementation time).

---

## 5. Ownership & Data Flow (high level)

```
CinematicFollow ─┐
CinematicLookAt ─┼─> CinematicVirtualCamera.GetDesiredPose() ─┐
noise settings ──┘                                            │
                                                              ├─> Brain: SELECT live vcam
impulses (static) ────────────────────────────────────────────┤   BLEND outgoing -> incoming
                                                              │   ADD noise + impulses
                                                              └─> WRITE into real Prowl Camera
```

Key invariants:
1. Exactly one brain drives the real camera per frame.
2. A vcam is passive: it never writes to the real camera.
3. Noise/impulse contributions are added **after** blending so they do not get double-damped.
4. State needed for continuity (blend progress, damping velocities, impulse timers) lives in the brain and behavior components, never in serialized scene data that designers must manage.

---

## 6. State Flow Per Frame

Executed in `CinematicCameraBrain.LateUpdate()` with delta time `dt`:

1. **Resolve target.** If `TargetCamera` is null, find the active camera tagged as the main Prowl Camera; cache it.
2. **Advance impulses.** `CinematicImpulse.Update(dt)` — decrement remaining lifetimes, remove expired entries.
3. **Select live vcam.**
   - If a manual override is set (`SetLiveCamera`), use it.
   - Otherwise scan all active `CinematicVirtualCamera` components with `Enabled == true`, choose highest `Priority`; tie-break by stable component instance ID.
   - If no candidate exists, keep the previous pose and return (or use an emergency fallback pose at origin — configurable, default: hold last pose).
4. **Detect change.** If the newly selected vcam differs from `LiveCamera`:
   - Set `previousPose = CurrentPose` (the pose actually shown last frame).
   - Set `incomingPose = newLive.GetDesiredPose(dt)`.
   - Determine blend parameters: use incoming vcam's override if set, else brain defaults. If hint is `None`, set blend duration to 0 (hard cut).
   - Reset blend progress `t = 0`.
5. **Compute incoming pose.** Each frame call `newLive.GetDesiredPose(dt)`. Follow/look-at damping continues to update inside the behavior components (they hold their own velocity state), so the incoming target is current.
6. **Compute blended base pose.**
   - If not blending: `basePose = incomingPose`.
   - If blending: `t += dt / blendDuration`, clamp to 1; `w = Ease(curve, t)`; `basePose = Blend(previousPose, incomingPose, w, hint)`. When `t >= 1`, blending ends and `LiveCamera` becomes fully incoming.
7. **Add noise.** `noisePose = GlobalNoise?.Evaluate(time, basePose.Position)` plus `LiveCamera.Noise?.Evaluate(...)` (summed, clamped). Add positional offset to `basePose.Position`; add rotational Euler offset (composed on top of `basePose.Rotation`); add FOV offset.
8. **Add impulses.** For each active impulse whose vcam has `ReceiveImpulses`, sample its shake signal at current time, scale by intensity × distance falloff × decay envelope, and add to position/rotation/FOV.
9. **Write output.** Assign `TargetCamera.transform.position = finalPose.Position`, `.rotation = finalPose.Rotation`, `.fieldOfView = finalPose.FieldOfView`. Store `CurrentPose = finalPose` for next frame's blend source.
10. **Expose introspection.** Update `LiveCamera`, `IsBlending` for editor/gameplay queries.

Ordering note: because step 4 blends from `CurrentPose` (which already includes last frame's noise/impulses), a cut or blend never snaps due to effects being dropped. Effects are recomputed each frame on top, so they remain continuous.

---

## 7. Blending Math

### 7.1 Frame-rate-independent exponential damping (core of §4.4/§4.5)

The standard approach uses an exponential approach with a smooth time (time constant) `τ = SmoothTime`. Rather than a fixed per-frame lerp factor, compute a blend factor `α` from `dt`:

```
α = 1 - exp(-dt / τ)            // τ > 0; clamp dt to a sane max (e.g. 0.1s)
current = current + (target - current) * α
```

This is identical at 30 fps and 144 fps because `exp` accounts for elapsed time. As `dt → 0`, `α → 0` (no movement); as `dt` grows, `α → 1`.

With a **max speed** cap, compute the exponential step first, then clamp the displacement magnitude (or angular displacement) to `MaxSpeed * dt`:

```
delta = (target - current) * α
maxDelta = MaxSpeed * dt
if |delta| > maxDelta: delta = normalize(delta) * maxDelta
current = current + delta
```

Rotation uses the same formula in quaternion space: slerp toward `desired` by `α`, then clamp angle via `Quaternion.Angle` against `MaxDegreesPerSecond * dt`.

Degenerate cases:
- `SmoothTime <= 0` → `α = 1` (instant snap).
- `dt <= 0` → `α = 0` (no movement).
- Very large `dt` (tab refocus) → clamp `dt` before computing `α` to prevent a jump.

Reference implementations (conceptual, not shipped):

```csharp
// Vector3 exponential damp with optional speed cap.
public static Vector3 DampVector(Vector3 current, Vector3 target,
                                 float smoothTime, float maxSpeed, float dt)
{
    if (dt <= 0f) return current;
    if (smoothTime <= 0f) return target;
    float alpha = 1f - MathF.Exp(-dt / smoothTime);
    Vector3 delta = (target - current) * alpha;
    if (maxSpeed > 0f)
    {
        float maxDelta = maxSpeed * dt;
        if (delta.sqrMagnitude > maxDelta * maxDelta)
            delta = delta.normalized * maxDelta;
    }
    return current + delta;
}
```

### 7.2 Blending between virtual cameras

Let `w` be an eased blend weight in `[0, 1]` derived from `t = elapsed / duration`:

```
t = clamp(elapsed / duration, 0, 1)
w = Ease(curve, t)
```

Easing functions on `t ∈ [0,1]`:
- Linear: `w = t`
- SmoothStep: `w = t*t*(3 - 2*t)`
- CubicInOut: `w = t < 0.5 ? 4*t^3 : 1 - pow(-2*t + 2, 3)/2`
- EaseOut: `w = 1 - (1 - t)^3`

Base pose blend by hint:
- **Linear** (default): `CameraPose.Lerp(previousPose, incomingPose, w)` — position lerp, rotation slerp, FOV lerp.
- **Spherical**: interpolate position along an arc between the two positions around the outgoing rotation's up axis, then slerp rotation; useful for orbital transitions. (Optional; can be stubbed to Linear in Phase 1.)
- **Freeze**: hold `previousPose.Position` fixed; slerp rotation from previous to incoming by `w`; lerp FOV. Gives a "turn then translate" feel.
- **None**: `w` forced to 1 instantly (hard cut).

### 7.3 Noise + impulse composition

Noise and impulses are **additive offsets on top of the blended base pose**, not part of the blend:

```
final.position = base.position + noisePos + impulsePos
final.rotation = base.rotation * Quaternion.Euler(noiseEuler + impulseEuler)
final.fov      = clamp(base.fov + noiseFov + impulseFov, MinFov, MaxFov)
```

Clamp offsets to sane maxima (configurable, e.g. ≤ 0.5 m position, ≤ 5° rotation for noise) to prevent authoring accidents.

Perlin-style sample used by `NoiseLayer`:

```
value(t) = smoothstep-interpolated hash of floor(t) and floor(t)+1
signal  = value(t * Frequency + seedBias)
offset  = (signal * 2 - 1) * Amplitude * BlendWeight
```

Three decorrelated signals (different seed biases / axis permutations) produce x/y/z positional and pitch/yaw/roll rotational offsets.

Impulse envelope:

```
if time < RiseTime: e = time / RiseTime
else: e = Ease(shape.Decay, 1 - (time - RiseTime)/(Duration - RiseTime))
contribution = ShakeSignal(time, seed) * strength * intensity * falloff * e
```

---

## 8. Editor Setup — Component Checklist

For each virtual camera:

1. Add a GameObject in the scene (e.g. `Vcam_Main`, `Vcam_Intro`, `Vcam_Action`).
2. Add **`CinematicVirtualCamera`**.
   - Set `Enabled` (usually true).
   - Set `Priority` (e.g. main = 0, intro = 20, action = 10). Higher = live.
   - Choose `BlendHint` (start with `Linear`).
   - If you want a custom transition, tick `OverrideBlendDuration`, set `BlendDuration` and `BlendCurve`.
3. Choose a **pose source**:
   - *Static shot:* `Manual`, fill `ManualPosition` + `ManualEulerAngles`.
   - *Follow a player:* `FollowTarget`, add **`CinematicFollow`** (drag it into the `Follow` field), set `Target`, `Offset`, `SmoothTime`, `MaxSpeed`.
   - *Track a target with the camera:* add **`CinematicLookAt`** (drag into `LookAt`), set `Target`, `SmoothTime`, `MaxDegreesPerSecond`. Combine with `Follow` for a third-person rig.
4. Set `FieldOfView` (default 60).
5. *(Optional)* Add **`CinematicNoise`** for handheld feel; configure layers with small `PositionAmplitude` / `RotationAmplitude` and `BlendWeight`.
6. *(Optional)* Leave `ReceiveImpulses` on to let explosions/impacts shake this camera.

For the brain:

7. Add **`CinematicCameraBrain`** to a persistent GameObject (one per scene).
8. Assign `TargetCamera` to the real Prowl Camera component (or leave null to auto-resolve the main camera).
9. Set `DefaultBlendDuration` / `DefaultBlendCurve` / `DefaultBlendHint`.
10. *(Optional)* Add a **`CinematicNoise`** to the brain for global noise applied to every shot.
11. Ensure `ImpulsesEnabled` is on if you want scripted shakes.

For gameplay code:

12. To switch shots: `CinematicCameraBrain.Instance.SetLiveCamera(vcamReference)` (blends) or `.CutTo(vcamReference)` (instant). Call `ClearOverride()` to return to priority-based selection.
13. To shake: `CinematicCameraBrain.Instance.FireImpulse(shape, worldPosition, intensity, radius)` or `CinematicImpulse.PlayPreset("Explosion", pos)`.

Verification in editor:
- Press Play: the highest-priority enabled vcam should drive the view.
- Raise another vcam's priority at runtime: the view should blend over `BlendDuration`.
- Toggle a noise layer: subtle motion should appear/disappear.
- Fire an impulse from a debug hotkey: a decaying shake should occur.

---

## 9. Phased Implementation Plan

### Phase 1 — Vertical slice (prove the core loop)
Deliver the smallest end-to-end system that demonstrates selection + blend + output.
- Implement `CameraPose`, `CinematicBlendHint`, `CinematicEase`/`CinematicEasing`.
- Implement `CinematicVirtualCamera` with **Manual** pose source only, priority, blend hint/override.
- Implement `CinematicCameraBrain` with: priority selection (incl. stable tie-break), Linear blend with SmoothStep easing, `LateUpdate` write to the real camera, `SetLiveCamera`/`CutTo`/`ClearOverride`.
- No noise, no impulses, no follow/look-at yet (interfaces reserved but stubbed).
- Unit tests: exponential damping equivalence across dt, blend weight endpoints, priority selection/tie-break, hard cut.
- Demo: two vcams in a test scene; a key toggles priority; visible smooth blend.
- **Exit criterion:** designer can place two vcams and see a smooth, frame-rate-independent blend with no code changes.

### Phase 2 — Follow, look-at & damping
- Implement `CinematicFollow` and `CinematicLookAt` with `Evaluate`, `SmoothTime`, `MaxSpeed`/`MaxDegreesPerSecond`.
- Wire `CameraPoseSource.FollowTarget` and `ManualPositionLookAtTarget` into `GetDesiredPose`.
- Add unit tests for damping with speed cap, zero smooth time, large dt clamp.
- **Exit criterion:** a third-person follow camera tracks a moving target smoothly and blends into it from a static shot.

### Phase 3 — Noise & impulses
- Implement `NoiseLayer` / `CinematicNoise` layered sampling with deterministic seeds and blend weights.
- Implement `CinematicImpulse`, `ImpulseShape`, `ImpulseHandle`, presets, falloff, decay envelopes.
- Wire additive composition into the brain (§7.3) with clamps.
- Add a small preset asset/registry for named impulses.
- **Exit criterion:** handheld noise is visible and deterministic; a scripted explosion impulse shakes and decays; both survive a blend.

### Phase 4 — Polish & production hardening
- Implement `Spherical` and `Freeze` blend hints.
- Optional camera collision / obstacle avoidance hook (kept as a separate provider so core stays pure — NG2 lifts here if desired).
- Editor UX: gizmos for vcam pose/frustum, an "active vcam" overlay, blend progress debug view, impulse counters.
- Performance: cache vcam list, avoid per-frame allocations, add a fixed maximum impulse count.
- Integration docs and a sample scene shipped with Prowl samples.
- **Exit criterion:** system is used in at least one real Prowl sample/game scene with documented settings.

---

## 10. Appendix A — Quick API cheat sheet

```csharp
// Switch shots with a blend:
CinematicCameraBrain.Instance.SetLiveCamera(introVcam);

// Instant cut:
CinematicCameraBrain.Instance.CutTo(actionVcam);

// Back to priority rules:
CinematicCameraBrain.Instance.ClearOverride();

// Impact shake:
CinematicImpulse.PlayPreset("Explosion", transform.position, intensity: 1.2f, radius: 12f);

// Query state:
var live = CinematicCameraBrain.Instance.LiveCamera;
bool blending = CinematicCameraBrain.Instance.IsBlending;
```

## 11. Appendix B — Implementation notes & open questions

- **Execution order:** the brain must run after all movement/animation that changes targets. Use Prowl's equivalent of a late update / explicit execution-order attribute; document the required order.
- **Auto-resolve target camera:** define what counts as "the main camera" in Prowl terms (tag, component flag, or a scene singleton) before implementing step 1 of §6.
- **Exact field names:** adapt `Camera.fieldOfView` and transform writes to Prowl's real camera API at implementation time; CCS only depends on "set position/rotation/FOV."
- **Determinism:** keep all noise/impulse sampling as pure functions of `(seed, time, inputs)`; do not use a global RNG. This preserves G7.
- **Clamping policy:** finalize min/max FOV and maximum effect offsets as project settings so designers cannot create nausea-inducing shots accidentally.
- **Multiple brains:** enforce one per scene via `Awake` registration; log a warning and keep the first instance (NG-only scenario).
