# GAM-13 Phase A — frozen untouched screening plan

Status: `FROZEN_BEFORE_SCREENING`

Issue: Linear `GAM-13` — M3.4 squat load response, sticking behavior, and physical failure

Run: `20260925-phase-a-untouched-r7`

## Baseline and scope

- Branch: `work/gam-13-squat-load-calibration`
- Production baseline: `2aff020` (`docs(gam49): accept squat surface landmark authority`)
- Unity: `6000.3.22f1`
- Unity editor path: `D:\Dev\Unity\6000.3.22f1\Editor\Unity.exe` (version `6000.3.22.1864302`).
- Frozen pre-screen `ProjectSettings.asset` SHA-256: `503f6edbd7468b3206b2dd6733a1c1a895f596ef5893f6757240d104f8ff0a5f`.
- Worktree at freeze: clean; this is the only worktree.
- Loads, in order: `25, 60, 140, 170, 300 kg`.
- One attempt per load, each launched by a separate new Unity editor process.
- The load is the only varying input. No production parameter, profile, physics setting, calibration, scene asset, or rule setting may be changed between probes.

## Frozen attempt procedure

Run the explicit GAM-13 single-load PlayMode screening test against the checked-in `SquatPhysicalPrototype` scene. Each process initializes a new Unity project process and the harness loads the production scene in single mode. Use the existing GAM-13 attempt harness without its optional configuration callback, apply the requested bar load, start the canonical attempt, and hold Drive at `1.0` only after the reference enters an ascent state. Keep the existing 100 Hz authoritative runtime and 0.01 s simulation step. Record the harness, capacity model, failure calibration, trace, and observation schema versions in every artifact.

The diagnostic observer is read-only. It copies each post-physics snapshot and reads the shared surface landmark provider, joint target composition/diagnostics, support/contact proxies, body/joint transforms, and engine solver diagnostics. It does not write body, target, joint, contact, rule, or controller state.

## Frozen measurement schema

All loads use schema `GAM13_PHASE_A_TRACE_V1`, one CSV artifact per load. Each attempt-level field is repeated on every trace row so a single file contains its result and raw bounded trace.

- Rule and legality: bilateral shared-provider surface depth, worst side, strict bilateral predicate, `-0.005 m` game predicate and signed margin (`threshold - worst depth`; positive means qualified), first strict/game-qualified ticks, minimum bilateral/worst depth and tick, P2 result/violations, and joint-center depths labeled diagnostic only.
- Bar and ascent: raw post-physics bar position/velocity, physical descent/bottom/ascent/lockout events, offline analyzer bottom/reversal and ascent metrics, identifiable `v_max1 → v_min → v_max2` events, sticking interval/duration/displacement only when resolvable, ascent duration, and stall/reversal evidence.
- Realization: GAM-10 reference target surface depth, rate-limited applied-target surface depth, actual shared-provider surface depth, applied-target-to-actual depth delta, and joint target composition/requested/applied/actual quaternion state, angular errors, modeled demand, and limit proximity by controlled joint/family.
- Contact and constraints: per-tick left/right contact and persistent-bilateral status, support bounds/state/margins, engine contact-point estimate explicitly labeled as a proxy (not measured COP), foot slip, hip/knee anchor separation, shared-provider inferred left/right pelvis-origin disagreement, and solver torque labeled only `ENGINE_SOLVER_DIAGNOSTIC`.
- Failure: P3 descent, bottom, legal-bottom, ascent, and lockout stages, physical failure class and onset/latch ticks, terminal reason, and a flagged window of at most 100 samples before failure onset or terminal tick.
- If the attempt harness reaches its 2200-tick bound without an attempt record, preserve the same per-tick schema as pre-terminal evidence, identify the start-window terminal context, and mark P2/P3 and attempt kinematics `NOT_EVALUATED_NO_ATTEMPT_RECORD`. Do not interpret this as a physical lift failure.

The per-joint trace includes all 15 physical joints, mapped to the eight configured families (ankle, knee, hip, trunk, neck, shoulder, elbow, wrist). Each available joint includes nominal/composed/requested/applied/actual quaternion state, scalar calibrated state when published by the squat snapshot, target-to-actual angular error, modeled demand, force capacity, activation, and limit proximity. Unavailable channels stay `NA`.

Reference and rate-limited applied-target surface depths are reconstructed through the shared GAM-49 provider and GAM-10 frame calibration. The reconstructed depth depends on the two thigh and two shank joint targets. A missing target on that depth chain makes the reconstruction unavailable. If an unrelated upper-body branch target is unavailable, its nominal reference (or identity when the reference API has no target for that branch) fills only the unused branch required by the common forward-kinematics routine; the artifact records each such fallback. This does not change the physical runtime or the rule observation.

Modeled demand is not actuator saturation. `ConfigurableJoint.currentTorque`/copied solver torque is not direct drive torque. No missing velocity event will be inferred from load category or synthesized when the trace does not resolve it.

## Screening and classification rules

Report observations first, using measured values and explicit unavailable states. Describe each load from its actual depth, support, completion/failure, velocity, and sticking trace. Classify the first defect only if the same frozen traces bound its causal domain. A correlation alone does not authorize parameter selection. If the traces leave multiple plausible domains, record the unresolved hypothesis set and stop without tuning.

## Stop boundary

This campaign ends after the untouched five-load ladder and Phase B causal classification. It does not perform calibration, qualification reruns, boundary repeats, or change production physics.

## Process-launch note

Earlier runner attempts are excluded from the official ladder: the initial launcher and compile probes collected no attempt evidence; a 25 kg capture with startup rows outside the immutable trace was rejected; one partial ladder proved the 140 kg start-window case but did not write its no-record artifact; and a five-load capture was superseded after review found the exporter was omitting available per-joint modeled demand/limit diagnostics for upper-body families. The exporter now reads those fields from `PoweredJointDiagnostic`, filters to immutable attempt ticks, and writes bounded pre-terminal evidence with P2/P3 explicitly not evaluated when no attempt record exists. The official complete screening run is `20260925-phase-a-untouched-r7`.

The exact-version editor generated `ProjectSettings/SceneTemplateSettings.json` and changed only the Standalone scripting define in `ProjectSettings/ProjectSettings.asset` from `SENTIS_ANALYTICS_ENABLED` to `APP_UI_EDITOR_ONLY` before the official ladder. Repository search found no code consumer for that define. The resulting settings file is frozen at the SHA-256 above for every official process; the runner checks the hash after each attempt. The fixed-step, physics, scene, rule, control, athlete, and equipment files are also hash-checked against the initial repository commit. This editor-authored compile setting is not a GAM-13 physics calibration.
