# GAM-12 Phase 2A semantic repair receipt

MISSION
`GAM12_PHASE2A_RULE_SEMANTIC_BOUNDARY_REPAIR_AND_ADVERSARIAL_VALIDATION`

START_HEAD
`5181b757d1d4010cf8ef9f47d6f5352d2f5ea09e`

CHECKPOINT
`checkpoint/gam12-pre-p2a-semantic-repair` at `5181b757d1d4010cf8ef9f47d6f5352d2f5ea09e`; created and pushed before implementation.

STATUS
`PASS_WITH_LIMITATIONS`

P2_HEAD
`5181b757d1d4010cf8ef9f47d6f5352d2f5ea09e`

SUPPORT_SLIP_BASELINE_POLICY
At the exact `SquatCommandIssued` trace sample, capture independent left/right cumulative P1 slip baselines. Judge only the non-negative command-relative deltas through the pre-Rack window. A cumulative value moving backward is invalid/incomplete evidence; it is never treated as negative movement.

SUPPORT_RULE_WINDOW
`[SquatCommandIssued, RackCommandIssued)` — the command sample is eligible, the Rack sample and all later samples are outside the support proxy window. The proxy remains a `GAME_SIMPLIFICATION`; cumulative slip is not exact referee-visible step classification.

ATTEMPT_COMMENCEMENT_POLICY
Attempt commencement is the first copied observation where either calibrated logical knee scalar leaves the versioned bilateral locked-knee tolerance. This is the bounded game proxy for the official knee-unlock semantic.

BAR_MOTION_COMMENCEMENT_POLICY
Bar velocity/displacement onset remains a separate physical-descent-motion proxy for later depth/movement analysis. Bar settling or displacement with both knees locked cannot independently create `EARLY_DESCENT`.

COMMAND_TRACE_COVERAGE_POLICY
`SquatCommandIssued`, `RackCommandIssued`, and `RerackStarted` must each have an exact snapshot within inclusive first/last trace coverage. The start-evidence, Squat-to-Rack, and Rack-to-Rerack windows must be continuously tick-covered.

TRACE_GAP_POLICY
An unexplained tick gap in any required adjudication window returns `INCOMPLETE_ATTEMPT` with `UNDETERMINED` outcome and no decision.

LOCKOUT_SUPPORT_SEPARATION
Final lockout/posture contains only bilateral knees, hip/trunk erect bounds, and controlled bar stillness. Support/contact/slip legality is evaluated separately, so support failure cannot create `FAILED_LOCKOUT` by itself.

SAME_TICK_COMMAND_POLICY
`attemptCommencementTick == SquatCommandTick` is not early. This is explicitly versioned as `GAME_TEMPORAL_DISCRETIZATION_POLICY` and preserves the existing deterministic convention.

GOOD_LIFT_CLAIM_CEILING
`GOOD_LIFT` means good under `IPF_2026_V3_SQUAT_GAME_V1`; it does not claim fully observed official IPF adjudication. Bar placement, hand/finger contact, spotter assistance, supportive elbow/arm contact, dumping, front-rack exit, and exact foot-step semantics remain limited or unobservable.

PRE_SQUAT_SLIP_TEST
PASS — `PRE_SQUAT_CUMULATIVE_SLIP_DOES_NOT_CREATE_SUPPORT_VIOLATION`.

POST_SQUAT_SLIP_TEST
PASS — `POST_SQUAT_INCREMENTAL_SLIP_CAN_CREATE_SUPPORT_VIOLATION`, including command-relative measured delta.

PRE_DESCENT_SUPPORT_TEST
PASS — `POST_SQUAT_PRE_DESCENT_FOOT_MOVEMENT_IS_JUDGED`.

BAR_SETTLING_TEST
PASS — `BAR_SETTLING_WITH_LOCKED_KNEES_DOES_NOT_CREATE_EARLY_DESCENT`.

KNEE_UNLOCK_TEST
PASS — pre-Squat unlock creates `EARLY_DESCENT`; same-tick unlock remains not early.

COMMAND_COVERAGE_TESTS
PASS — Rack-after-trace-end, Rerack-after-trace-end, Squat-before-trace-start, and fully covered command timeline cases.

TRACE_GAP_TEST
PASS — `TRACE_GAP_IN_RULE_WINDOW_IS_INCOMPLETE`.

LOCKOUT_SUPPORT_TEST
PASS — valid lockout plus support failure, valid support plus failed lockout, coexistence, and primary-reason isolation.

REAL_TRACE_INTEGRATION_TEST
PASS — real frozen P1 trace remains `INCOMPLETE_ATTEMPT`, `UNDETERMINED`, and `HasDecision == false`; repeated evaluation, trace count/freeze, and Unity state remained unchanged.

P2_FOCUSED
PASS — focused P2/P2A EditMode `41/41`.

FULL_EDITMODE
PASS — full EditMode `125/125`.

P2_INTEGRATION
PASS — strengthened real-trace PlayMode integration `1/1`.

MASTER_SPEC
PASS — `Verify-MasterSpec.ps1`; 68 files, hashes PASS, dependencies PASS.

CODE_REVIEW
PASS — Standards axis found no Unity leakage, P1/P1A churn, physics changes, unnecessary abstraction, or unbounded allocation path; mission/spec axis found all five semantic repairs and required adversarial/integration assertions present.

OTHER_REGRESSION_IF_RUN
NOT_RUN — full PlayMode was not required for this pure rule-domain change; the targeted P2 integration passed.

PRODUCTION_PHYSICS_CHANGED
NO.

P1_SCHEMA_CHANGED
NO.

PHYSICAL_FAILURE_CLASSIFIER_IMPLEMENTED
NO — Phase 3 remains out of scope.

SAFETY_IMPLEMENTED
NO.

HEAVY_LOAD_CALIBRATION_IMPLEMENTED
NO.

KNOWN_LIMITATIONS
Support remains a bounded contact/slip game proxy and is not exact bilateral referee-visible step/rocking classification. Same-machine Unity 6000.3.22f1 qualification is not a cross-platform PhysX determinism claim. The repository continues to defer automatic attempt-end orchestration and unobservable official rule items.

NEXT_PHASE
`GAM12_PHASE3_DETERMINISTIC_SQUAT_FAILURE_DETECTOR_AND_PRECEDENCE` — authorized after this receipt; do not implement in P2A.

LINEAR
GAM-12 remains In Progress. Post one concise P2A result update after the final commit/push; do not alter GAM-13.

FINAL_HEAD_EXTERNAL_RECORD_NOTE
The final commit SHA is recorded externally after commit and push and is not embedded self-referentially in this receipt.
