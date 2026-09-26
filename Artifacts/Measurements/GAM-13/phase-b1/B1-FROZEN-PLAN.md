# GAM-13 Phase B1 frozen experiment plan

Frozen before any B1 arm is run.

## Authority and preflight

- Live authority: Linear issue [GAM-13](https://linear.app/alignerr-cmj/issue/GAM-13/m34-calibrate-squat-load-response-sticking-behavior-and-physical), Phase B1 protocol, read 2026-09-26.
- Master authority: `docs/master-spec/POWERLIFTING_SIMULATOR_MASTER_SPEC_V1/ROADMAP/71_M3_SQUAT_IMPLEMENTATION_PLAN.md` and the repository constitution/protocol.
- Production baseline: accepted Phase-A commit `7f29d73424e37254e3b924d50d01cda46b0aa0ae`, reachable and equal to preflight HEAD.
- Branch: `work/gam-13-squat-load-calibration`.
- Worktrees: one (`E:/Data/Projects/PowerliftingSimulator`). Tracked worktree clean. Pre-existing untracked Phase-A R2-R7 evidence is preserved.
- Production physics, control, and rule sources are identical to the Phase-A baseline at plan freeze.
- Unity executable: `D:/Dev/Unity/6000.3.22f1/Editor/Unity.exe`; executable product version `6000.3.22f1_1c726e1fb402`.
- GAM-49 authority: the accepted shared surface-rule landmark provider remains the `SquatDepthLandmarkProvider` → observation snapshot → P2/P3 path. The joint-center value remains diagnostic-only. The committed GAM-49 Gate 4 receipt records the fresh-process requalification.

This commit contains only this frozen plan, the test-only measurement harness, and the one-arm process runner. No production source or defaults are in scope. Each arm is launched by a separate Unity process and has a separate output directory. The runner omits `-quit`, waits for fresh Test Framework XML, and records the exact Unity PID and process result.

## Shared collection rules

- Use `SquatPhysicalPrototype` with the production scene, production masses, contacts, saddle, solver, joint profiles, control layers, and rules.
- Advance only with the authoritative 0.01 s runtime step and ordinary production joint drives/contact. Do not write transforms, velocities, forces, or hidden support.
- Held-bottom qualification uses `HoldReferencePhaseForQualification`; it changes only the reference clock/state and does not place any body. Stand for 500 ticks (5 s), approach phase 0→1 at 1.0 phase/s over 100 ticks (1 s), then hold the descending canonical phase-1 bottom for 500 ticks (5 s). Capture every post-physics sample. “Settled” means both-foot contact and support are retained for every final 100 bottom samples and 95th-percentile horizontal COM speed in that window is at most 0.05 m/s.
- B1-B observes at most 2,200 setup ticks (22 s). Stop the same process immediately when the production diagnostic reports a consecutive run equal to its own `RequiredRunLength`; this prevents the next production lifecycle tick from issuing Squat. A no-start arm runs the full declared window.
- CSV values are invariant-culture SI values. The full production predicate diagnostic is exported from `AttemptOrchestrator.StartWindowDiagnostics`; no start predicate is reimplemented.
- Surface depths are read from the shared GAM-49 snapshot provider. Joint-center depth is recorded only as a diagnostic.
- `ModeledDemand` is named modeled drive demand. Solver torque is named solver-constraint torque. Neither is called measured drive torque or actuator saturation.

## B1-A arms and event rules

1. `A0`: 25 kg, full-production, held canonical bottom.
2. `A1`: 60 kg, full-production, held canonical bottom.
3. `A2`: 60 kg, full-production canonical attempt lifecycle, with the ordinary Drive intent issued once on the first reversal/ascent state. Continue capturing the physically running scene for 200 ticks after attempt-record finalization (unless the 2,200-tick cap is reached), so delayed support/contact loss remains observable.

All arms capture bilateral hip target/reference/actual/error/limit/demand/solver diagnostics; abdomen and thorax diagnostics; surface and joint-center depth; bar state; bilateral support/contact and COM/capture margins; foot slip; balance offsets; saddle state/separation; hip/knee anchor separations; and inferred left/right pelvis-origin disagreement.

Predeclared report sentinels (diagnostic only): hip limit proximity first crosses `0.98`; COM-to-support margin first becomes non-positive; either foot first loses contact; and the first transition into `REVERSAL`. A2 also reports the production P3 failure onset and latch ticks from the attempt record. These sentinels do not change production thresholds.

Interpretation:

- Static/deep-phase candidate: A1 meets the declared final-window settle condition and its maximum bilateral hip limit proximity is at least `0.95`.
- Dynamic/reversal candidate: A1 meets the declared final-window settle condition and its maximum hip proximity is at most `0.96`, while A2 crosses `0.98` at or after its reversal transition.
- If A1 is unsupported/unsettled, the two arms do not meet one of these separated conditions, or A2 does not resolve its event sequence, retain both candidates and stop B1-A unresolved.
- Do not widen a limit or infer a sticking region. No `v_max1 → v_min → v_max2` interpretation is part of B1.

## B1-B arms and predeclared comparisons

1. `B0-60`: 60 kg, full-production control.
2. `B1-140-P`: 140 kg, full-production.
3. `B2-140-S0`: 140 kg, `SpineCalibrationEnabled=false`; balance remains production-enabled.
4. `B3-140-B0`: 140 kg, `BalanceCorrectionsEnabled=false`; spine feed-forward remains production-enabled.
5. `B4-140-S0B0`: 140 kg, both diagnostic layers disabled.

The two booleans are set only on the in-memory diagnostic adapter for that fresh process. Production defaults are never edited or saved.

For every setup sample, retain the production diagnostic object and export every public field. A sidecar joins its tick to bar/pelvis heights, support/contact state, capture and COM-support margins, COM displacement, and left/right slip. Summaries predeclare these comparisons:

- maximum consecutive full-predicate run and per-constituent pass fractions;
- best signed abdomen/thorax margins and the fractions within the actual ±10° trunk bounds;
- bar linear, vertical, and angular stillness fractions;
- support and bilateral-contact duration;
- minimum capture and COM-support margins;
- initial-to-final COM AP/ML drift and maximum AP/ML displacement from the first sample;
- maximum slip speed and accumulated slip;
- minimum posture-guard scale and maximum raw ankle, hip, and trunk authority usage.

For a frozen, single-run comparison, a *material trunk restoration* means the lesser of the two best trunk margins improves by at least 5° over B1-140-P and either both-trunk ±10° occupancy improves by at least 0.10 or a persisted run is newly reached. An arm is *physically credible at persistence* only if both feet and support remain present, the minimum capture and COM-support margins are non-negative, horizontal COM displacement from its initial 100-sample mean is below 0.25 m, and neither pelvis nor bar has dropped more than 0.15 m from its initial 100-sample mean. These are experiment stop/comparison sentinels only; they do not enter production rules.

If a required run is reached with a failed credibility sentinel, stop that arm immediately and classify `START_READINESS_AUTHORITY_CONTRADICTION`. A Squat command is never the success criterion. Classify a spine candidate only when S0 meets the material-restoration criterion and B0 does not; a balance candidate only when B0 meets it and S0 does not; an interaction candidate only when the combined arm meets it and neither single-layer arm does. If no arm meets it and all diagnostic layers remain within the declared material thresholds, reject these layers as sufficient and proceed only to `SUPPORT_CONTACT_SADDLE_STATIC_EQUILIBRIUM_FEASIBILITY`.

## Stop and commit sequence

1. Commit this plan and the B1-only measurement harness before executing any arm.
2. Run A0, A1, A2 in separate Unity processes; validate and commit only B1-A raw CSVs, summaries, runner receipts, XML, and logs.
3. Run B0-60, B1-140-P, B2-140-S0, B3-140-B0, B4-140-S0B0 in separate Unity processes; validate and commit only B1-B evidence.
4. Write and commit a classification ADR with `ESTABLISHED`, `SUPPORTED`, `REJECTED`, and `UNRESOLVED`, preserving the stated claim ceiling.
5. Authorize Phase C only if one bounded physical domain is identified. Otherwise stop B1 unresolved.

The diagnostic artifacts do not promote correlations to causes without the specified contrast. No GAM-14 work is included.
