# GAM-13 full evidence report

Date: 2026-09-17  
Status: **BLOCKED — Stage-A standing qualification is not passed**  
Mission: GAM13_END_TO_END_SQUAT_LOAD_RESPONSE_STICKING_AND_PHYSICAL_FAILURE_CALIBRATION  
Branch: work/gam-13-squat-load-calibration  
Production candidate at the start of the visual run: 425ed2d592f82dafec65e0cda23ceb715a0be852  
Unity: 6000.3.22f1  
Graphics device: Intel(R) Iris(R) Xe Graphics, Direct3D 12, driver 32.0.101.7088  
Claim class: GAME_ENGINE_CONTROL_CALIBRATION

## Executive verdict

The candidate does not qualify for GAM-13 continuation. The 25 kg standing
case is stable. The default candidate falls out of a credible standing/setup
state at 60, 140, 170, and 300 kg in the deterministic Stage-A hold. The
visual captures show the failure directly: the 60 kg athlete folds and loses
support, while the 300 kg athlete is already deeply folded at 1 s and is on the
ground by 5 s.

This is a setup/balance-control failure before a meaningful squat attempt. It
is not evidence of a valid actuator-capacity ceiling, sticking threshold, or
physical-failure calibration. Capacity calibration therefore remains blocked.

## What was actually tested

The Stage-A fixture loads a fresh SquatPhysicalPrototype scene for each
canonical load and holds SquatState.SETUP for 600 fixed ticks. The first 100
ticks are settling; the remaining 500 ticks are measured. The fixture uses the
production physical saddle, dynamic feet, production balance loop, and the
fixed candidate plant.

The standing gates are:

| Gate | Limit |
|---|---:|
| Pelvis height | > 0.90 m |
| Absolute trunk pitch | < 0.70 rad |
| Capture margin | > 0.01 m |
| COM speed | < 0.25 m/s |
| Absolute foot pitch | < 12 deg |
| Sustained drive saturation | < 5% |
| Canonical posture error | < 10 deg |
| Joint-limit proximity | < 0.95 |
| Saddle separation | < 0.05 m |
| Support/contact and finite-state checks | must remain valid |

The qualification measurements below are from
[stage-a-standing-baseline.csv](../Measurements/GAM-13/stage-a-standing-baseline.csv),
the default seed candidate at fixed impedance 1x.

## Canonical-load results

| Load | Result | Min pelvis (m) | Max trunk (rad) | Min capture margin (m) | Max COM speed (m/s) | Min contacts |
|---:|---|---:|---:|---:|---:|---:|
| 25 kg | **PASS** | 0.9777 | 0.1454 | 0.1077 | 0.0053 | 8 |
| 60 kg | **FAIL** | 0.0974 | 1.7124 | -1.0386 | 1.7019 | 0 |
| 140 kg | **FAIL** | 0.0949 | 1.6177 | -1.6618 | 2.4301 | 6 |
| 170 kg | **FAIL** | 0.1000 | 1.9153 | -1.6496 | 2.5225 | 0 |
| 300 kg | **FAIL** | 0.0980 | 2.9208 | -1.2673 | 2.2796 | 0 |

The result is not being inferred from a single metric. At the failed loads,
pelvis height, trunk posture, capture margin, COM speed, contact persistence,
or several of these gates fail together. The traces remained finite; this was
not a NaN/Inf failure.

## Direct visual evidence

The graphics test ran with a real D3D12 device and **without -nographics**.
It passed 1/1 and wrote twelve 1280x720 RGB PNGs plus a telemetry sidecar.
The test result is preserved in
[visual-test-results.xml](../Evidence/GAM-13/stage-a-visual/visual-test-results.xml).

### 25 kg — stable standing at 5 s

![25 kg standing at 5 seconds](../Evidence/GAM-13/stage-a-visual/load-025kg-t0500.png)

The athlete is upright with both feet planted and the bar held across the
shoulders. The matching visual telemetry row is: pelvis 0.9778 m, trunk
pitch 0.1399 rad, capture margin 0.1077 m, 8 contacts, support True.

### 60 kg — visible collapse sequence

![60 kg at 1 second](../Evidence/GAM-13/stage-a-visual/load-060kg-t0100.png)

![60 kg at 2.5 seconds](../Evidence/GAM-13/stage-a-visual/load-060kg-t0250.png)

![60 kg at 5 seconds](../Evidence/GAM-13/stage-a-visual/load-060kg-t0500.png)

At 1 s the athlete is already outside the posture gate: trunk pitch is
0.8786 rad and posture error is 23.66 deg. At 2.5 s the frame shows the
athlete and bar airborne with zero support contacts; the measured capture
margin is -0.1388 m. At 5 s the athlete is on the ground with pelvis height
0.1021 m and trunk pitch 1.5791 rad.

### 300 kg — immediate deep fold and fall

![300 kg at 1 second](../Evidence/GAM-13/stage-a-visual/load-300kg-t0100.png)

![300 kg at 5 seconds](../Evidence/GAM-13/stage-a-visual/load-300kg-t0500.png)

At 1 s the athlete is visibly folded under the bar: pelvis height is already
0.7258 m, trunk pitch is 2.8097 rad, posture error is 77.64 deg, and capture
margin is -0.4855 m. At 2.5 s the measured support count is zero and pelvis
height is 0.3369 m; by 5 s the pelvis is 0.1050 m from the ground.

The remaining captured frames are available in the same directory:

| Load | Initial | 1 s | 2.5 s | 5 s |
|---:|---|---|---|---|
| 25 kg | [PNG](../Evidence/GAM-13/stage-a-visual/load-025kg-t0000.png) | [PNG](../Evidence/GAM-13/stage-a-visual/load-025kg-t0100.png) | [PNG](../Evidence/GAM-13/stage-a-visual/load-025kg-t0250.png) | [PNG](../Evidence/GAM-13/stage-a-visual/load-025kg-t0500.png) |
| 60 kg | [PNG](../Evidence/GAM-13/stage-a-visual/load-060kg-t0000.png) | [PNG](../Evidence/GAM-13/stage-a-visual/load-060kg-t0100.png) | [PNG](../Evidence/GAM-13/stage-a-visual/load-060kg-t0250.png) | [PNG](../Evidence/GAM-13/stage-a-visual/load-060kg-t0500.png) |
| 300 kg | [PNG](../Evidence/GAM-13/stage-a-visual/load-300kg-t0000.png) | [PNG](../Evidence/GAM-13/stage-a-visual/load-300kg-t0100.png) | [PNG](../Evidence/GAM-13/stage-a-visual/load-300kg-t0250.png) | [PNG](../Evidence/GAM-13/stage-a-visual/load-300kg-t0500.png) |

The complete frame-by-frame telemetry is in
[stage-a-visual-telemetry.csv](../Evidence/GAM-13/stage-a-visual/stage-a-visual-telemetry.csv).
The t=0 trunk value is NaN only because the observation snapshot has not
been produced before the first physics tick; all evaluated later samples are
finite.

## Discriminating control experiments

The candidate was not rejected after one failed run. The following bounded
diagnostics were completed on the same fixed plant:

| Diagnostic | Result |
|---|---|
| Fixed impedance 2x | 60 and 300 kg still fail; 140/170 kg pass selected diagnostics |
| Fixed impedance 3x | 60 and 300 kg still fail; 140/170 kg pass selected diagnostics |
| Fixed impedance 4x and 8x | 60 and 300 kg still fail |
| COP tracking gains 0.1 through 4 at 60 kg | every tested candidate fails |
| Posture-guard disabled | fails; guard removal is not the repair |
| Capture-point blend diagnostic | does not rescue 60/300 kg |
| Standing spine-bias grid | does not rescue 60 kg |
| Lower-chain bias candidate | does not restore 60/300 kg support |

The measured balance-plant target-offset-to-COP slopes at the 3x diagnostic
were approximately 0.462, 0.340, 0.275, 0.262, and 0.186 m/rad at
25, 60, 140, 170, and 300 kg. This rejects reusing the old 0.20446 m/rad
constant after the impedance change and shows that a single inverse constant
is not qualified.

The diagnostic measurements are preserved in
[Artifacts/Measurements/GAM-13](../Measurements/GAM-13/), including the
balance identification, equilibrium identification, and gain-search CSVs.

## Changes and invariants

The candidate changes were deliberately bounded:

- equilibrium compensation is a smooth, bounded target-space feedforward;
- impedance is fixed across loads;
- athlete capacity is independent of external bar load;
- dynamic balance remains feedback-only;
- P1/P2/P3/P4 truth ownership was not changed;
- no load-threshold script, per-load impedance, per-load balance gain, or
  load-proportional capacity model was added.

The candidate identifiers are:

~~~text
GAM13_SQUAT_EQUILIBRIUM_FEEDFORWARD_V1
GAM13_SQUAT_ATHLETE_CAPACITY_V1
~~~

The architecture review and decision record are:

- [GAM-13-heavy-load-control-architecture.md](../Research/GAM-13-heavy-load-control-architecture.md)
- [ADR-GAM13-heavy-load-standing-control.md](../Decisions/ADR-GAM13-heavy-load-standing-control.md)

## Verification status

| Check | Result |
|---|---|
| Focused GAM-13 EditMode contract tests | 3/3 passed |
| Graphics evidence capture | 1/1 passed; 12 PNGs written |
| git diff --check | passed |
| MasterSpec hashes/dependencies | passed; no master-spec hash changed |
| Full final EditMode/PlayMode/performance acceptance | not claimed |
| GAM-12 post-change lifecycle rerun | not completed because Stage-A is blocked |
| Capacity/sticking/failure/held-out qualification | not started or not claimed |
| PR/merge | not created |
| Linear transition | not made; GAM-13 remains In Progress |

## Claim ceiling and next action

The strongest defensible conclusion is:

> On the fixed candidate plant, bounded load-general equilibrium compensation
> and load-independent athlete capacity do not currently establish and hold
> upright supported setup at the canonical heavy loads. The failure occurs
> before a meaningful attempt and is visually confirmed as a physical
> posture/support collapse.

The work must remain blocked at Stage A. The next authorized investigation is
whole-body equilibrium/balance or a rig/contact/topology defect. Capacity
calibration should not resume from the collapsed traces. No final capacity,
sticking, supra-max failure, held-out-load, PR, merge, or Linear-completion
claim is supported by this evidence.

