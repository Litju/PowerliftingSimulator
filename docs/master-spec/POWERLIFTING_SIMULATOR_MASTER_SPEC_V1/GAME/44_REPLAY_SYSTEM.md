# Replay System

**Document ID:** `PSMS-GAME-44`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `GAME/43_SPORTS_SCIENCE_ANALYSIS.md`, `PRESENTATION/52_CAMERA_AND_BROADCAST.md`, `03_COORDINATES_UNITS_NUMERICS.md`

## Repository verification

- Inspect current replay/attempt trace implementation and any M1 snapshots.
- Verify visible rig reconstruction from recorded physical poses.
- Measure storage size and interpolation artifacts on all three lifts.

## PURPOSE


Provide exact, inspectable playback of what the simulation recorded, including athlete/bar state, commands, phases,
rules, failures, and analysis overlays, without nondeterministic re-simulation.


    ## INPUTS


Finalized attempt snapshot stream, event stream, schema/calibration/ruleset IDs, presentation camera script, optional
analysis channels.


    ## OUTPUTS


Scrubbable replay timeline, normal/slow/pause/frame-step playback, camera cuts, ghost overlays, exportable replay record,
and evidence captures.


    ## STATE


Replay clock independent from game/physics time; snapshot index pair and interpolation alpha; playback rate; selected
camera; overlay selection; event cursor. No active rigid-body simulation is required.


    ## UNITS


Recorded SI/radian state; replay clock s; playback rate dimensionless.


    ## COORDINATE CONVENTION


Snapshots retain canonical frames. Replay reconstructs world physical poses then applies visible bind offsets. Camera
operations do not alter data.


    ## EQUATIONS


For replay time `t`, find snapshots `k,k+1` with `t_k≤t≤t_{k+1}`:

\[
\alpha=(t-t_k)/(t_{k+1}-t_k),
\quad \mathbf x(t)=\operatorname{lerp}(\mathbf x_k,\mathbf x_{k+1},\alpha),
\]

and `slerp` orientations. Discrete events use the last event at or before `t`; rule results are never interpolated.


    ## ASSUMPTIONS


100 Hz snapshots plus interpolation are visually sufficient. State playback is more trustworthy than resimulation across
versions/platforms.


    ## APPROXIMATIONS


Interpolated contact shapes may appear to pass through a transition between samples; event markers preserve exact tick.
Audio can be re-triggered from recorded events but is not sample-perfect.


    ## GAME CALIBRATIONS


Rates: 0.1×, 0.25×, 0.5×, 1×, 2×; frame step one recorded tick. Camera cuts align to setup, command, critical position,
sticking/failure, lockout, judgment. Ghost/reference opacity is presentation calibration.


    ## NUMERICAL IMPLEMENTATION


Replay scene disables simulation for replay bodies or uses kinematic presentation-only proxies. Decode snapshots into
preallocated buffers. Interpolate only continuous state. Validate trace hash/schema before load; fail closed on unsupported
versions or migrate explicitly.


    ## PSEUDOCODE

    ```text
    ReplayUpdate(real_dt):
    replay_time = clamp(replay_time + real_dt * rate, 0, trace.duration)
    k0, k1, alpha = trace.Bracket(replay_time)
    pose = interpolate_continuous(trace[k0], trace[k1], alpha)
    visible_athlete.ApplyRecordedPose(pose.athlete)
    visible_bar.ApplyRecordedPose(pose.bar)
    event_cursor.ApplyDiscreteEventsUpTo(replay_time)
    camera_director.Evaluate(replay_time, trace.events)
    overlays.Render(trace, replay_time)
    ```

    ## UNITY MAPPING


Dedicated replay scene/prefab with visible rig and bar render proxy; `PlayableDirector` optional for camera/audio, but
recorded timeline remains authority. Cinemachine cameras consume event/timeline state. No `PhysicsScene.Simulate`.


    ## FAILURE MODES


Resimulation; interpolation of result/events; trace/schema mismatch silently accepted; replay modifies career result;
visible skeleton uses animation instead of recorded physical pose; camera coordinate contaminates metrics; missing frame
at failure due to safety overwrite.


    ## OBSERVABILITY


Replay HUD shows trace hash, tick/time, snapshot bracket, rate, event markers, calibration/ruleset, and quality.
Development mode can compare decoded pose to original golden screenshot.


    ## TELEMETRY


Replay load/decode time, memory, dropped frames, selected rate/camera, export status. Viewer behavior is not mixed into
attempt metrics.


    ## TESTS


Exact endpoints/events; random seek; reverse seek; frame step; orientation shortest path; result immutability; no physics
step; schema rejection/migration; screenshot golden poses; long trace memory/performance.


    ## MUTATION TESTS


Resimulate input; interpolate judgment; run Animator clip instead of state; permit replay to write save result; omit hash;
use render transforms as source.


    ## PERFORMANCE CONSIDERATIONS


Decode/pose application under 1 ms target on reference desktop; bounded memory; optional delta compression after V1.


    ## CLAIM CLASSIFICATION


Replay is a visualization of recorded simulation state. It is not a reconstruction of real human movement.


    ## SHIP_V1 / LATER / RESEARCH / OUT_OF_SCOPE


**SHIP_V1:** state playback, seek/rates/cameras/overlays.  
**LATER:** compressed sharing/video export.  
**RESEARCH:** deterministic resim comparison only.  
**OUT_OF_SCOPE:** replay as authoritative physics re-execution.
