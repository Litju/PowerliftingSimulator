# GAM-12 Phase 2 rule processor receipt

MISSION
`GAM12_PHASE2_DETERMINISTIC_SQUAT_RULE_PROCESSOR_AND_ATTEMPT_JUDGMENT`

START_HEAD
`6c3ed347830346405618325da3d92b4f2c406337`

CHECKPOINT
`checkpoint/gam12-pre-p2-rule-processor` at `6c3ed347830346405618325da3d92b4f2c406337`; created and pushed before implementation.

STATUS
`PASS_WITH_LIMITATIONS`

FINAL_HEAD_EXTERNAL_RECORD_NOTE
The final commit SHA is recorded externally after commit and push and is not embedded self-referentially in this receipt.

RULEBOOK_ORGANIZATION
International Powerlifting Federation (IPF)

RULEBOOK_TITLE
IPF Technical Rule Book

RULEBOOK_VERSION
Version 3

RULEBOOK_EFFECTIVE_DATE
2026-03-01

RULEBOOK_RETRIEVED_DATE
2026-09-14

RULEBOOK_SHA256_IF_AVAILABLE
`NOT_DOWNLOADED` — the current official PDF was verified live; no local copy was retained.

RULEBOOK_SQUAT_SECTION
Section 4.1 Squat; official PDF printed page 20 (web PDF index P19 / one-based PDF p.20).

RULEBOOK_DISQUALIFICATION_SECTION
Section 4.1.1 Causes for Disqualification of a Squat; official PDF printed page 21 (web PDF index P20 / one-based PDF p.21).

RULESET_ID
`IPF_2026_V3_SQUAT_GAME_V1`

RULE_IMPLEMENTATION_VERSION
`GAM12_P2_RULE_PROCESSOR_V1`

RULE_TOLERANCE_VERSION
`GAM12_P2_RULE_TOLERANCES_V1`

RULE_MAPPING_ARTIFACT
`Artifacts/Research/GAM-12-P2-IPF-squat-rule-map.md`

PROCESSOR_TYPE
`PowerliftingSimulator.Squat.SquatRuleProcessor`

COMMAND_TIMELINE_TYPE
`PowerliftingSimulator.Squat.SquatRuleCommandTimeline`

JUDGMENT_TYPE
`PowerliftingSimulator.Squat.SquatAttemptJudgment`

VIOLATION_TYPE
`PowerliftingSimulator.Squat.SquatRuleViolationRecord`

PURE_DOMAIN
YES — `PowerliftingSimulator.Squat` assembly remains `noEngineReferences`.

UNITY_DEPENDENCY
NONE in rule-domain implementation.

TRACE_SCHEMA_CONSUMED
`GAM12_SQUAT_TRACE_V1` / `GAM12_SQUAT_OBSERVATION_SNAPSHOT_V1`

FROZEN_TRACE_REQUIRED
YES — recording, unfrozen, empty, noncanonical, or non-100 Hz traces are refused explicitly.

OFFICIAL_RULES_MAPPED
17 explicit mapping entries covering 4.1(1–6) and 4.1.1(1–9), including unobservable and out-of-scope rules.

SOURCE_DIRECT_COUNT
1

RULE_PROXY_COUNT
6

GAME_SIMPLIFICATION_COUNT
2

NOT_OBSERVABLE_COUNT
5

OUT_OF_SCOPE_COUNT
3

GOOD_LIFT_TEST
PASS — frozen valid start, command order, bilateral depth, one descent, direct ascent/bar stillness, final lockout, Rack order, and support proxy.

BILATERAL_DEPTH_TEST
PASS — shallow both sides, one hip high, exact margin, and just-inside boundary.

ONE_HIP_HIGH_TEST
PASS — one shallow bilateral landmark side gives `INSUFFICIENT_DEPTH`.

EARLY_DESCENT_TEST
PASS — raw knee/bar onset before Squat gives `EARLY_DESCENT`; same-tick onset is not early by the explicit boundary policy.

DOUBLE_DESCENT_TEST
PASS — persistent raw bar reversal before ascent establishment gives `DOUBLE_DESCENT`.

DOWNWARD_MOVEMENT_TEST
PASS — persistent whole-bar downward motion after direct ascent establishment gives `DOWNWARD_MOVEMENT`.

START_POSITION_TEST
PASS — pre-Squat persistence checks bilateral knee/hip/trunk, bar motion, support, and feet.

LOCKOUT_TEST
PASS — final physical observations, not legacy adapter booleans, determine `FAILED_LOCKOUT`.

EARLY_RACK_TEST
PASS — explicit `RerackStarted` before `RackCommandIssued` gives `EARLY_RACK`.

SUPPORT_TEST
PASS — bounded pre-Rack contact/slip proxy records `SUPPORT_VIOLATION`; permitted post-Rack movement is ignored.

MULTI_VIOLATION_TEST
PASS — violations are retained and sorted by first tick then explicit precedence rank.

MISSING_EVIDENCE_TEST
PASS — missing bar or mandatory command metadata yields no canonical competition decision.

PHASE_CONFLICT_TEST
PASS — state/Sq disagreement cannot override raw physical landmarks or bar motion.

LOAD_MUTATION_TEST
PASS — changing trace load metadata alone does not change rule outcome.

RULEBOOK_COVERAGE_TEST
PASS — every metadata mapping has a nonempty locator, predicate/disposition, and allowed V1 disposition.

RULES_IMPLEMENTED
`EARLY_DESCENT`, `INSUFFICIENT_DEPTH`, `DOUBLE_DESCENT`, `DOWNWARD_MOVEMENT`, `FAILED_START_POSITION`, `FAILED_LOCKOUT`, `EARLY_RACK`, and bounded `SUPPORT_VIOLATION`.

PHYSICAL_FAILURE_CLASSIFIER_IMPLEMENTED
NO — Phase 3 remains out of scope.

SAFETY_IMPLEMENTED
NO.

HEAVY_LOAD_CALIBRATION_IMPLEMENTED
NO.

EDITMODE
PASS — focused P2 rule suite 26/26; full EditMode suite 110/110.

P2_INTEGRATION
PASS — bounded PlayMode `RULE_INTEGRATION_FIXTURE` consumed a real frozen 25 kg P1 trace 1/1, repeated judgment, and trace/state immutability checks passed.

GAM11_REGRESSION_IF_RUN
NOT_RUN_NOT_REQUIRED — no GAM-11/P1 production dependency was modified; existing P1 tests remain included in the full EditMode gate.

MASTER_SPEC
PASS — `Verify-MasterSpec.ps1`: 68 files, hashes PASS, dependencies PASS.

FULL_PLAYMODE_IF_REQUIRED
NOT_REQUIRED — domain-only P2 change; focused P2 integration was run.

RUNTIME_ATTEMPT_END_ORCHESTRATION
`DEFERRED_TO_GAM12_INTEGRATION_PHASE` — judgment accepts only an explicitly frozen trace; no Unity auto-finalization was added.

KNOWN_SIMPLIFICATIONS
Support/foot movement uses only the P1 contact/slip proxy; exact step direction and judge-visible rocking are not claimed. Exact bar placement, hand/thumb/finger contact, spotter assistance, supportive elbow/upper-arm contact, bar dumping, rack geometry, front-rack exit, and safety Replace timing remain explicit unobservable/out-of-scope mappings. The processor is one canonical deterministic judgment; referee lights/presentation are not implemented.

KNOWN_LIMITATIONS
Competition judgment requires a real bar and usable P1 depth, joint, support, and foot evidence. Unloaded 0 kg traces without a bar remain non-competition-judgeable. A missing command producer is represented by the pure timeline seam and is not inferred from state or intent.

NEXT_PHASE
`GAM12_PHASE3_DETERMINISTIC_SQUAT_FAILURE_DETECTOR_AND_PRECEDENCE`
