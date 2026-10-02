# GAM-43 — loaded-standing architecture benchmark plan

Status: frozen before Phase 1 candidate execution  
Issue: GAM-43  · Parent: GAM-13  · Branch: `work/gam-13-squat-load-calibration`  
Start HEAD: `7dadc71eff9a6d442ea74741af36e8bc55ddcfe6`  
Unity: `6000.3.22f1`  · one worktree  · claim class: game/engine control calibration

## Scope

This is an experiment only. It will select a loaded-standing architecture or
record `WINNER=NONE`. No production winner implementation, capacity tuning,
sticking/failure calibration, GAM-14 work, or GAM-13 PR is in scope.

The fixed plant for every candidate is athlete load-bearing stiffness `5x`
GAM-7, load-bearing damping `sqrt(5)x` GAM-7, current Saddle V2 topology,
athlete solver `28/1`, bar solver `12/6`, unchanged capacity, balance control,
bar mass/inertia, squat rules, and P1/P2/P3/P4 semantics.

Every run uses a fresh `SquatPhysicalPrototype` scene. Each candidate is run in
an independent Unity process. No candidate reads another candidate's state.

## Candidate definitions

### Feed-forward candidates

All three candidates are one bounded smooth phase-by-load law evaluated from
physical load and phase. There are no canonical-load equality branches, per-load
gains, per-load impedance, or per-load outcome rules. The existing `+/-12 deg`
preload bound remains hard.

* `F0` — current feed-forward at the start HEAD: the existing
  `GAM13_SQUAT_EQUILIBRIUM_FEEDFORWARD_V1` surface, spine-only at runtime;
  ankle/knee/hip standing preload is zero.
* `F1` — 5x spine-only re-identification. At phase zero, abdomen/thorax
  standing values are the frozen diagnostic trim values from
  `Artifacts/Measurements/GAM-13/trim-hold-validation-impedance-5x.csv`,
  interpolated over load knots `[0, 25, 60, 140, 170, 300]` with smoothstep:

  ```text
  abdomen = [0.83, 3.4557111793, 4.8284150722, 4.6966275932,
             5.8672913100, 7.7701211322] deg
  thorax  = [0.92, 3.3476651683, 2.3169222261, 5.8474472453,
             6.1630915138, 7.1453508792] deg
  ```

  The standing knot blends smoothly to the frozen F0 phase surface by phase
  `0.25`; phase `>= 0.25` is F0. Lower-chain standing compensation remains
  zero.
* `F2` — 5x whole-body re-identification. It uses the F1 spine law and the
  following frozen phase-zero lower-chain values from the same diagnostic
  table, with smoothstep load interpolation and a smooth fade to zero by phase
  `0.25`:

  ```text
  ankle   = [0, -1.8063042634, -2.1074897118, 0.4350906376,
             1.4862751944, 3.4342191243] deg
  knee    = [0,  1.0260491747,  1.6714558729, -0.2708576668,
            -1.3010310077, -3.3363286815] deg
  hip     = [0, -0.5826498604,  1.4604533976, 2.6608095306,
             3.3683454750, 4.7420170119] deg
  ```

These feed-forward definitions are test-side/reversible experiment seams; the
existing default path remains F0.

### Saddle candidates

At the frozen F* plant, compare exactly:

* `S0`: `50,000 N/m`, `3,000 N*s/m`, `0.050 m` linear limit.
* `S1`: `50,000 N/m`, `3,000 N*s/m`, `0.100 m` linear limit.
* `S2`: `75,000 N/m`, `3,000*sqrt(75/50) = 3,674.234614 N*s/m`,
  `0.050 m` linear limit.

All other Saddle V2 values remain unchanged. No `S3` is authorized.

## Loads and execution

| Phase | Development / canonical loads | Holdouts | Repeats |
|---|---|---|---:|
| Baseline reference | `25 / 60 / 140 / 170 / 300` | none | existing 3x reference |
| Phase 1 feed-forward | `25 / 60 / 140 / 170` | `100 / 155` | 1 per arm/load |
| Phase 2 saddle | `25 / 60 / 140 / 170 / 300` | `230 / 270` | 1 per arm/load |
| Phase 3 integrated | `25 / 60 / 140 / 170 / 300` | `100 / 155 / 230 / 270` | 3 canonical; 1 holdout |

Phase 1 does not use 300 kg to select F*. Phase 2 freezes F* before any S arm
and freezes S* before Phase 3.

## Metrics

At every standing load/repeat record finite status, pelvis/trunk upright state,
canonical-pose error, target/actual elastic deflection, 2-D capture-hull signed
margin, AP capture margins, forward/rearward COP authority, COM speed,
ankle/hip/trunk balance-command usage and bounds, modeled drive demand and
saturation fraction, anatomical/game joint-limit proximity, saddle separation,
linear/angular saddle-limit occupancy, saddle force/torque diagnostics,
support/contact state, and the fixed plant/candidate identity.

For the 25 kg lifecycle also record legal depth, physical lockout, lifecycle
events, P2/P3/terminal semantics, trace status, and P3 result. Raw per-tick
evidence is retained for the selected integrated candidate and diagnostic guard
comparison.

## Hard gates

Standing gates apply to every development and holdout load except that 300 kg
only requires a finite valid loaded setup in the Phase-2/Phase-3 300 kg row:

* all recorded values finite;
* upright and support retained;
* canonical-pose error `< 10 deg`;
* signed 2-D capture-hull margin `> 0.01 m`;
* no sustained modeled drive saturation: saturated measured ticks `< 5%`;
* no critical joint-limit dependency: worst anatomical/game joint proximity
  `< 0.95`;
* saddle attached, unbroken, and valid; no finite saddle limit occupancy
  `>= 0.95` in the measured standing window;
* one fixed 5x athlete plant across all loads, with no per-load gains,
  impedance, or outcomes.

Operational upright is the existing Stage-A predicate: minimum pelvis height
`> 0.90 m` and maximum absolute trunk pitch `< 0.70 rad`; support requires a
support state and at least one plantar contact throughout the measured window.

The 25 kg lifecycle must have legal depth, physical lockout, `P3 =
NO_PHYSICAL_FAILURE`, and accepted P2/P3/terminal semantics. A lifecycle
failure is not hidden by a standing score.

## Winner rule and stop conditions

1. A candidate must pass every applicable hard gate.
2. It must pass every holdout assigned to its phase.
3. Eligible candidates are ranked by strongest worst-case headroom across pose,
   capture, drive, balance, joint-limit, and saddle constraints.
4. A materially tied result selects the simpler architecture/smaller production
   change.
5. No weighted average may hide a failed or near-failed constraint.
6. If no candidate is eligible at a phase, record `WINNER=NONE`, write the ADR,
   and stop without opening the next phase.

Deterministic repeats must agree on all gate-driving metrics and raw hashes.
Any Unity/project-setting churn not belonging to the experiment is restored
before evidence is staged.

## Validation and commits

Before each commit: fresh Unity result XML is parseable, required artifacts are
present, `git diff --check` passes, and only named experiment files are staged.
Required semantic commits are benchmark plan, Phase-1 evidence, Phase-2
evidence, integrated benchmark, and ADR/experiment seal. Push is non-force only
after the coherent benchmark seal; no production winner is implemented.
