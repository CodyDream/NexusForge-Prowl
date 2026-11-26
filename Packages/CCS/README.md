# CCS — Cinematic Camera System for Prowl

A small priority-driven camera director. Virtual cameras describe shots; one Brain picks the
highest-priority enabled shot, blends to it, and drives a Camera.

## Setup

1. Mount `Packages/CCS` in the editor (**Window → General → Packages**) so `Assets/CCS` compiles.
2. Put a `Camera` on the object you want to drive and add **Cinematic Camera Brain** to the same
   object. If the Brain is elsewhere, it drives the scene's main Camera instead.
3. Create empty rig objects and add **Cinematic Virtual Camera** to each. Give them different
   `Priority` values; the highest enabled one wins (ties prefer the older camera).
4. On each virtual camera set `BlendTime`, `FOV`, and either:
   - assign `FollowTarget` / `LookAtTarget` GameObjects, or
   - leave the targets empty and pose the rig object itself.
   `PositionOffset` is a world-space offset added to the followed position.
5. Optionally add **Cinematic Noise** and/or **Cinematic Impulse** to a virtual camera's object,
   and leave that camera's `EnableNoise` / `EnableImpulse` flags on. Components on the Brain object
   itself act as global shake. Call `GetComponent<CCS.CinematicImpulse>().AddImpulse()` from game
   code to trigger a decaying shake (`AddImpulse(float strength)` takes 0..1).

## Behaviour

- Virtual cameras compute a damped desired position, rotation and FOV in `Update`; damping smooth
  times are in seconds (zero follows the target exactly).
- The Brain blends from the previous output pose to the new shot in `LateUpdate` using exponential
  smoothing; a `BlendTime` of zero cuts immediately. Noise and impulse are applied after the blend.
- Only public fields are saved by Prowl, so all editable settings above survive scenes and play mode.
  Runtime blend state is hidden from serialization.
