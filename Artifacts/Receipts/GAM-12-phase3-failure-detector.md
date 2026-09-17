MISSION=GAM12_PHASE3_DETERMINISTIC_SQUAT_FAILURE_DETECTOR_AND_PRECEDENCE
START_HEAD=db190b8108a526a00cf9e447adbbb28943dcfd31
CHECKPOINT=checkpoint/gam12-pre-p3-failure-detector @ db190b8108a526a00cf9e447adbbb28943dcfd31
STATUS=PASS_WITH_LIMITATIONS

FAILURE_MODEL_VERSION=GAM12_P3_FAILURE_MODEL_V1
FAILURE_CALIBRATION_VERSION=GAM12_P3_FAILURE_CALIBRATION_PROVISIONAL_V1
FAILURE_PRECEDENCE_VERSION=GAM12_P3_FIRST_IRREVERSIBLE_PRECEDENCE_V1
TRACE_SCHEMA=GAM12_SQUAT_TRACE_V1

DETECTOR_TYPE=PowerliftingSimulator.Squat.SquatFailureDetector
RESULT_TYPE=PowerliftingSimulator.Squat.SquatFailureResult
FAILURE_RECORD_TYPE=PowerliftingSimulator.Squat.SquatFailureRecord
EVIDENCE_WINDOW_TYPE=PowerliftingSimulator.Squat.SquatFailureEvidenceWindow
SAFETY_HANDOFF_TYPE=PowerliftingSimulator.Squat.SquatFailureSafetyHandoff

FAILURE_KINDS=BALANCE_LOSS, DESCENT_COLLAPSE, FAILED_REVERSAL, MID_ASCENT_STALL, BAR_REVERSAL, POSTURE_OR_BAR_LOSS, FAILED_LOCKOUT
PRIMARY_PRECEDENCE_POLICY=FIRST_IRREVERSIBLE: earliest latch tick, then explicit versioned tie-break rank, then stable fallback
SECONDARY_POLICY=retain later distinct kind/detail/direction once; primary is immutable
ONSET_VS_LATCH_POLICY=onset records first physical conjunction or attempt; latch records persistence/timeout confirmation
LATCH_IRREVERSIBILITY=latched failure cannot clear after recovery; only explicit detector reset starts a new stream
PRE_FAILURE_WINDOW=fixed-capacity chronological circular buffer; default capacity 32; frozen with primary record

BALANCE_POLICY=support margin at or beyond -0.010 m plus outward modeled COM velocity at or above 0.030 m/s for 12 ticks; AP and ML directions
DESCENT_COLLAPSE_POLICY=raw excessive bar/pelvis downward motion plus persistent support loss or hard trunk/joint-limit loss; modeled demand/saturation is recorded corroboration only and speed/demand/Drive absence alone cannot fail
FAILED_REVERSAL_POLICY=raw physical bottom plus bilateral legal-depth proxy plus Drive intent plus no sustained upward bar/pelvis recovery through 35 ticks
STALL_POLICY=established raw ascent plus low raw upward velocity plus high modeled demand plus no progress and 35-tick terminal dwell; recovery cancels only before latch
BAR_REVERSAL_POLICY=established ascent plus raw whole-bar downward motion, cumulative displacement beyond named noise bound, and 2-tick persistence; distinct from rule DOWNWARD_MOVEMENT
POSTURE_BAR_POLICY=2-tick trunk/joint hard-bound persistence; direct saddle break/coupling and qualified separation detail; warning band is diagnostic only
FAILED_LOCKOUT_POLICY=established ascent plus raw bilateral posture, bar stillness, and standing-reference height not reached before 60-tick bounded completion policy; no support legality reuse

STICKING_VS_STALL_POLICY=low velocity/high demand is recoverable analysis until no-progress terminal dwell confirms no recovery; successful sticking fixture remains NO_PHYSICAL_FAILURE
CALIBRATION_STATUS=provisional synthetic/domain boundaries; existing qualified depth/ascent/lockout/saddle bounds reused where declared
GAM13_REQUIRED_FIELDS=reversal timeout/recovery, stall velocity/demand/no-progress/dwell/recovery, bar reversal boundaries, descent-collapse behavior-dependent boundaries, lockout completion timeout

MISSING_EVIDENCE_POLICY=explicit missing values cannot become zero, stationary, or NO_PHYSICAL_FAILURE; unsupported full decisions are INSUFFICIENT_EVIDENCE
0KG_POLICY=bar-dependent full-attempt claim remains INSUFFICIENT_EVIDENCE when canonical bar is unavailable; independently observable failure candidates remain supported
25KG_REGRESSION_POLICY=accepted real trace is frozen and evaluated deterministically with no fabricated physical failure; current bounded fixture is honest INCOMPLETE_ATTEMPT

PURE_DOMAIN=PASS; new runtime code is pure C# in PowerliftingSimulator.Squat with no Unity references
SAFETY_ACTUATION=NO
LOAD_THRESHOLD_SCRIPT=NO
PRODUCTION_PHYSICS_CHANGED=NO
P1_SCHEMA_CHANGED=NO
RULE_LAYER_CHANGED=NO

FOCUSED_P3=PASS; 40/40 completed EditMode tests
FULL_EDITMODE=PASS; 169/169 completed EditMode tests
P3_INTEGRATION=PASS; 1/1 completed 25 kg real frozen-trace PlayMode test
FOCUSED_P3_XML_SHA256=A17A0756708CFAA33A8616A41158EACAFB02B5821CD9B216B1212F38931F08F0
FULL_EDITMODE_XML_SHA256=AE5C87B6B71D5B742E0E900F61161D1FDAEBDED8BCFA5AF77853C9D0BCB174AC
P3_INTEGRATION_XML_SHA256=38D84A3BE11D4C9357A5E575184ED96A860898DAEACB71BE08F5E50289229001
MASTER_SPEC=PASS; Verify-MasterSpec.ps1: 68 files, hashes/dependencies PASS
OTHER_REGRESSION=full EditMode covered existing Foundation/Squat/P1/P2 tests; no shared production dependencies changed

KNOWN_LIMITATIONS=25 kg integration trace is intentionally bounded and incomplete rather than a full lifecycle closeout; P3 numeric behavior-dependent boundaries are not heavy-load validated; 0 kg remains bar-insufficient; no cross-platform PhysX claim
NEXT_PHASE=GAM12_PHASE4_FAILURE_RULE_TELEMETRY_LIFECYCLE_INTEGRATION_AND_CLOSEOUT
