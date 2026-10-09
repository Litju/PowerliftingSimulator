# GAM-60 one-step gate

**Base:** `1975cf91ea664fecc2370c2e79a11f5f3a28f964`

**Runtime:** Unity `6000.3.22f1`; **load:** 25 kg

**Result:** `NOT QUALIFIED`; the bounded circuit breaker stopped at `SHIFT_TO_LEFT_STANCE`.

The qualification test loaded three fresh scenes. In each run the right foot stayed planted, and the sequence timed out after 400 ticks in `SHIFT_TO_LEFT_STANCE`. The transient target moved plantar load left: at the last shift sample the left/right completed normal impulses were `7.911 / 4.348 N·s`. The measured system COM x changed from `-0.000008 m` to `-0.000023 m` (a `0.000015 m` leftward change), far short of the 25 mm transfer gate. The balance path therefore did not prove the COM transfer required before right-foot swing.

All three aborts reached `ABORTED` through `ABORT_RECOVERY` within 20 ticks. Both feet remained loaded, COM/support telemetry stayed finite, and the test observed no total support loss. The right foot did not clear, move posterior, touch down, or accept a new load because swing was never entered. The unchanged squat-start predicate was not evaluated.

The runtime A/B joint baseline is in [one-step-25kg-joint-baseline.csv](one-step-25kg-joint-baseline.csv): A is each `PoweredJointDiagnostic.RequestedTarget`, B is `AppliedTarget`, with actual joint angle, error, solver torque, drive demand, and limit proximity. Across the attempted shift, the maximum target delta from that baseline was `2.865°` at both ankles, `0.428°` at both hips, and `0°` at knees, abdomen, and thorax. Commands remained finite and within the existing recipe and target-rate bounds; no trunk threshold was introduced.

[one-step-25kg.csv](one-step-25kg.csv) contains per-tick COM, contact impulse, support, clearance, displacement, and command-delta evidence. [GAM60-tests.xml](GAM60-tests.xml) records the three-run gate result.

The blocker is specific to the existing balance path: it redistributed support impulse but did not move the measured COM toward the left stance foot. Further work would need a different balance/weight-transfer mechanism; this candidate stops here for the GAM-57 rack-path decision.
