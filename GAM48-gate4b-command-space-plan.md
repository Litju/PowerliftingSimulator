# GAM-48 Gate 4b — runtime command-space plan

Status: FROZEN BEFORE IMPLEMENTATION
Authority: Linear GAM-48 Gate 4b and its 2026-09-23 owner review
Branch/base: work/gam-13-squat-load-calibration / 8480a37bb4fd63af8e0cdf353726a52744aa8f41
Unity: 6000.3.22f1
Scope: identify the 25 kg bottom depth discrepancy; make no production-physics or rule changes.

## Preflight

- Worktree: E:/Data/Projects/PowerliftingSimulator only; clean.
- Branch tracks origin/work/gam-13-squat-load-calibration; local and remote HEAD both equal 8480a37bb4fd63af8e0cdf353726a52744aa8f41.
- Linear GAM-48 is In Progress. Its owner review supersedes Gate 4's unique realization conclusion and requires the full runtime command-space decomposition.
- Codebase-memory MCP graph tools are unavailable in this session. Repository search fallback located and inspected the adapter, reference kinematics, powered-joint controller, physical rig/joint ownership, target composition, rate limiter, current Gate 4 tests/evidence, P3 detector/reporting, Gate 3 fresh-process harness, ADR, repository constitution, implementation protocol, and relevant Master Spec contracts.
- Master Spec authority remains frozen: world +Y up, SI units, PhysX owns body motion, PoweredJointController is the single drive writer, rule geometry is separate from reference geometry, and engine solver torque is not athlete drive torque.

## Measurement contract

Use Unity world frame W (+Y up, +Z forward, +X athlete-right), meters, and normalized Unity quaternions. Target orientations are logical J-frame rotations in radians; body positions are in meters. Positive depth delta means shallower. The bilateral depth is the larger of left and right, matching SquatDepthGeometry; legal is worst-side depth <= -0.005 m.

The production physical rule path measures the configured hip and knee ConfigurableJoint anchors through SquatPhysicalAdapter.TryGetRawDepthLandmarks, then calls SquatDepthGeometry. Target-state FK will reconstruct those same rule proxy landmarks from the actual physical joint anchors. D_ref for the decomposition will use the corresponding hip/knee joint-center landmarks in the current-head GAM-10 reference solution and the same SquatDepthGeometry. The already-qualified GAM-10 crease/top-offset depth will also be reported as D_ref_GAM10_CALIBRATED_LANDMARKS; it is a separate reference measurement, not silently substituted for the production rule proxy.

This split is necessary because the source currently uses calibrated crease/top offsets in the reference preview but joint anchors in the production physical rule path. If those reference metrics differ by more than the material threshold below, report the mismatch and do not infer a unique layer from a mixed-metric comparison.

The diagnostic must not call SquatReferenceKinematics.Solve to produce any physical target state. It reads exact runtime nominal/composition quaternions from SquatPhysicalAdapter and AppliedTarget from PoweredJointController. The only reference-solver result is the separately identified canonical D_ref measurement. FK uses the physical rig's joint ownership, saved neutral parent-child rotations, J-space basis, anchor/connected-anchor values, and the production logical-target conversion. The pelvis carrier is the adapter's already-produced GAM-10 physical-body rotation at phase 1; one identical carrier is used for every target arm. Root translation cancels in bilateral point-height differences.

For a logical target q_J, the pure inverse map reconstructs the child-parent orientation as q_N * q_Jspace * q_J * inverse(q_Jspace). Child position is located by making the production parent and child joint anchors coincide. The FK path returns values only and never writes transforms, velocities, forces, torques, targets, or controller state. Actual depth comes from the post-physics production observation for the same settled supported C0/HOLD run.

## Frozen experiment

1. Requalify current-head canonical GAM-10 reference depth and record both reference-landmark and rule-proxy measurements.
2. At phase 1, descent, load 25 kg, and the existing full C0/HOLD composition, capture the exact runtime quaternion state for:
   - nominal;
   - nominal × gravity bias;
   - nominal × balance offset;
   - nominal × gravity bias × balance offset;
   - PoweredJointController.AppliedTarget after its production rate limiter;
   - actual post-physics supported C0/HOLD.
3. Reconstruct every commanded state through physical-joint FK. For each of the final 50 samples of the existing 200-tick held window, calculate left, right, and worst-side depths for all target states and actual observations. Report mean left/right depths and worst-side=max(mean left, mean right); retain sample min/max as context.
4. Run C0 and HOLD_1.00_FULL in separate fresh Unity processes and compare all depth and decomposition values. Keep the Gate 3 equivalence requirement.
5. Write the target-composition table, signed layer deltas, actual applied-target/rate-limit comparison, and a P3/modeled-demand terminology receipt. Update the ADR only after validation.

## Predeclared tolerances and decisions

| Check | Frozen rule |
|---|---:|
| Neutral parent/child anchor closure | <= 0.0001 m, the physical rig anchor tolerance |
| Fresh-process numeric equivalence for each side, worst side, and layer delta | absolute <= 0.00001 m, the accepted Gate 3 tolerance |
| Runtime quaternion snapshot identity | shortest-arc error <= 0.00001 rad |
| Material depth contribution | absolute signed delta > 0.005 m; positive means shallowing |
| Reference-landmark versus rule-proxy metric parity | absolute difference <= 0.005 m; otherwise mark the metric mismatch and do not claim a unique causal layer from cross-metric legality |
| Legal depth | worst side <= -0.005 m |

Apply the issue's logic to matched rule-proxy metrics: legal reference plus material shallow nominal target supports PHYSICAL_ADAPTER_MAPPING; legal nominal plus material shallow final/applied target supports TARGET_COMPOSITION_GEOMETRY; legal final/applied plus material shallow actual supports PHYSICAL_REALIZATION. Report gravity, balance, full composition, rate-limit, and physical realization deltas independently. If multiple layers exceed 5 mm, keep attribution non-unique. If the reference itself is not legal under the matched rule-proxy metric, or the C0/HOLD comparison fails tolerance/support, report the unresolved hypothesis set and stop without a unique classification.

## Evidence semantics

- P3 physical-completion prerequisites are exactly physical descent, physical bottom, ascent established, and physical lockout. Report missing prerequisites as P3_COMPLETION_PREREQUISITES_MISSING and report P3_LEGAL_BOTTOM_SEEN separately. Terminal-context coverage stays a separate evidence-quality field.
- Report the normalized Kp*error + Kd*velocity-error signal over maximumForce as MODELED_DRIVE_DEMAND; its threshold flag is MODELED_DRIVE_DEMAND_HIGH. Neither field proves the PhysX drive-force clamp was reached.
- Label Joint.currentTorque as an engine solver/constraint diagnostic, not direct drive torque.
- Preserve production P3 behavior, drive configuration, diagnostics values, and rule thresholds.

## Frozen boundaries

Keep the 5x plant and sqrt(5) damping, LC1 anchors/formula, S1, F2 spine law, solver profile, actuator capacity, bar mass/inertia, balance gains/bounds, reference motion/angles, phase rate/dwell, squat rules, 5 mm legal margin, sticking/failure calibration, and no-direct-assistance rule unchanged. No spring/damper/capacity/balance/preload/reference tuning, integral control, hidden support, pinning, transform/velocity/force/torque writes, threshold changes, heavy-load calibration, GAM-14, or GAM-13 merge.

## Planned artifacts and gates

- This committed plan.
- Read-only runtime target reconstruction/decomposition tests using shared pure production mapping helpers.
- Layer-by-layer depth decomposition, target-composition table, and applied-target/rate-limit comparison.
- Corrected P3 completion/legal-bottom and modeled-demand terminology receipts.
- Updated ADR with exactly ESTABLISHED, SUPPORTED, REJECTED, and UNRESOLVED sections and the exact next authorized gate.
- Before each atomic commit: focused and affected squat EditMode tests; GAM-48 authority PlayMode tests; lifecycle and observation integration; current-head reference qualification; fresh-process C0/HOLD comparison when physical evidence is involved; Master Spec verification; git diff --check; exact-file staging review.
- Push only validated coherent commits non-force to the current branch. Restore unrelated Unity-generated churn.
