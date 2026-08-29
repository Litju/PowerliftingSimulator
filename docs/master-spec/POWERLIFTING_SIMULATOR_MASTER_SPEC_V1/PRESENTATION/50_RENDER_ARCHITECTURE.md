# Render Architecture

**Document ID:** `PSMS-PRES-50`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `02_GAME_ARCHITECTURE.md`, `04_HUMANOID_PHYSICAL_ATHLETE.md`, `ENGINEERING/60_RUNTIME_EXECUTION_ORDER.md`

## Repository verification

- Inspect the existing URP renderer assets, quality levels, camera stack, post-processing, and M2 scenes.
- Measure standalone GPU/CPU frame timing at target settings.
- Verify the visible rig follower order against current Animator/Animation Rigging components.

## PURPOSE


Render a polished human-centered powerlifting game from recorded physical state while preserving strict simulation/
presentation ownership and a stable 60 fps target.


    ## INPUTS


Interpolated physical snapshots, visible-rig bind map, bar/equipment state, venue scene, presentation state, cameras,
lights, VFX, UI, graphics settings.


    ## OUTPUTS


Final URP frame, shadows/lighting, skinned athlete, equipment, venue, overlays, screenshots/video evidence.


    ## STATE


Previous/current physical snapshots; render alpha; visible rig pose; presentation state; camera blend; quality profile;
material/VFX/audio event state. Render state is disposable and cannot alter physics/rules.


    ## UNITS


World m/rad; display pixels; luminance/exposure are presentation parameters; frame time ms.


    ## COORDINATE CONVENTION


Visible bones are reconstructed in world/model space from physical body poses and fixed bind offsets. Camera/view/screen
spaces are one-way outputs. No screen coordinate enters rule or physics calculations.


    ## EQUATIONS


Visible bone pose for mapped segment `i`:

\[
T^W_{V_i}(t)=\operatorname{Interp}(T^W_{B_i,k},T^W_{B_i,k+1},\alpha)
\,T^{B_i}_{V_i,\mathrm{bind}}.
\]

Skinning follows the imported bind-pose hierarchy. Optional visual corrections compose after physical following:

\[
T_{final}=T_{physicalFollower}\,T_{visualCorrection},
\]

where corrections are bounded, presentation-only, and reset each frame.

Frame budget at 60 fps:

\[
T_{frame}\le16.67\text{ ms}
\]

with explicit CPU/GPU subsystem budgets in PSMS-63.


    ## ASSUMPTIONS


Unity URP 17.3 and the existing M2 presentation foundation remain. The actual Quaternius humanoid is the hero asset.
Snapshot interpolation is sufficient to hide a 100 Hz fixed/render cadence mismatch.


    ## APPROXIMATIONS


Skin/cloth deformation and muscle bulging are presentation effects. Render-only bar flex is allowed. Motion blur and
camera shake may suggest impact but cannot obscure rule-critical frames.


    ## GAME CALIBRATIONS


Initial target: 1080p60 on reference desktop; scalable 720p/1080p quality profiles. One main broadcast camera plus limited
overlay/UI camera strategy. Main directional/key lighting, venue practicals, baked/mixed lighting where useful, modest
post-processing, stable exposure, and conservative motion blur.


    ## NUMERICAL IMPLEMENTATION


Apply visible physical pose in `LateUpdate` or a dedicated render phase after all physics snapshots are available.
Do not enable Animator writeback on the visible follower except for a controlled post-physics Playable/rig layer that
operates on render-only bones. Reuse materials, GPU instance static venue/equipment where practical, and avoid runtime
material instantiation. Camera and VFX consume events, not poll physical transforms inconsistently.


    ## PSEUDOCODE

    ```text
    RenderFrame(alpha):
    pose = snapshot_interpolator.Interpolate(previous, current, alpha)
    visible_athlete.ApplyPhysicalFollowerPose(pose.athlete)
    visible_bar.ApplyPoseAndVisualFlex(pose.bar)
    visual_rig.ApplyBoundedHandFingerFaceCorrections(pose)
    presentation_state.ApplyEventsUpTo(pose.time)
    camera_director.Evaluate(pose, presentation_state)
    render_pipeline.Render()
    ```

    ## UNITY MAPPING


URP renderer/volume assets; SkinnedMeshRenderer; MaterialPropertyBlock; Cinemachine cameras; optional Animation Rigging
on visible rig only; VFX/ParticleSystem; Canvas/UIToolkit according to existing M2 stack. Physics proxies are hidden in
release but available in debug views.


    ## FAILURE MODES


Animator overwrites physical pose; one-frame lag; interpolation feeds physics; root/child double transform; hand IK drags
bar; skin explosions; clipping through bench/bar; motion blur hides command/depth/touch; auto exposure pumps; VFX allocates;
camera sees hidden proxy; quality setting changes physical timestep.


    ## OBSERVABILITY


Render debug HUD: snapshot ticks/alpha, CPU/GPU frame time, draw calls, triangles, skinning cost, camera state, visible/
physical landmark error, quality profile. Toggle physical colliders/reference ghost.


    ## TELEMETRY


p50/p95/p99 CPU/GPU/frame time, batches/draw calls, visible triangles, memory, pose-application time, camera blend,
screenshot metadata.


    ## TESTS


Visible/physical landmark match; no physical mutation after render; interpolation endpoints; camera/screen invariance of
rules; material instance count; quality profile does not change physics; screenshot golden scenes; render pipeline smoke.


    ## MUTATION TESTS


Enable Animator on physical rig; apply IK before physics and write back; use render transform for telemetry; quality changes
fixedDeltaTime; move bar with visual flex; instantiate materials per frame.


    ## PERFORMANCE CONSIDERATIONS


Target budgets in PSMS-63. Keep one hero skinned mesh/rig, optimized shadow casters, bounded post effects, no allocations
in pose application. Profile GPU and CPU separately.


    ## CLAIM CLASSIFICATION


Rendered strain, muscle/cloth/bar flex: `PRESENTATION_ONLY`. Physical pose is visualization of engine state, not validated
human motion capture.


    ## SHIP_V1 / LATER / RESEARCH / OUT_OF_SCOPE


**SHIP_V1:** URP, physical follower, snapshot interpolation, scalable 1080p60 presentation.  
**LATER:** richer crowds, advanced skin/muscle shaders.  
**RESEARCH:** neural deformation.  
**OUT_OF_SCOPE:** photorealism at the expense of game completion/performance.
