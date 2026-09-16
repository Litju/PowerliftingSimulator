# GAM-12 P3 — executable failure model map

Model: `GAM12_P3_FAILURE_MODEL_V1`
Calibration: `GAM12_P3_FAILURE_CALIBRATION_PROVISIONAL_V1`
Precedence: `GAM12_P3_FIRST_IRREVERSIBLE_PRECEDENCE_V1`

All predicates consume `SquatObservationSnapshot` values in the frozen
`GAM12_SQUAT_TRACE_V1` stream. `SquatState`, `Sq`, phase direction, rule
judgment, camera, and load do not select physical truth. A failure event stores
the direct/derived channels, measured values, thresholds, onset context, latch
context, and claim class `GAME_ENGINE_PHYSICAL_FAILURE_CLASSIFICATION`.

| Failure | Required physical evidence | Persistence / onset | Latch and limitation |
|---|---|---|---|
| `BALANCE_LOSS` | Support bounds and modeled COM position/velocity; outward AP/ML velocity | Boundary + outward velocity begins the directional run; 12 consecutive ticks | Latches on confirmation; forward/back/left/right detail; support bounds are engine-derived game bounds |
| `DESCENT_COLLAPSE` | Excessive raw bar or pelvis downward speed plus support loss, hard trunk bound, or critical joint-limit loss | First conjunction sample; 3 consecutive ticks | Active Drive + saturated modeled demand is corroborating only; demand alone never fails |
| `FAILED_REVERSAL` | Raw physical bottom, bilateral legal-depth proxy, Drive intent, and absent upward bar/pelvis recovery | Onset is first Drive attempt after legal bottom | Latches at the 35-tick no-recovery deadline; timeout/recovery remain GAM-13 calibration dependencies |
| `MID_ASCENT_STALL` | Raw ascent established, low raw ascent velocity, high modeled demand, no-progress displacement, no recovery | Onset is first candidate sample; 35-tick dwell | Recoverable low-speed sticking resets; terminal no-progress latches; no filtered future data |
| `BAR_REVERSAL` | Raw whole-bar position/velocity after ascent establishment | First persistent downward run; 2 ticks plus cumulative drop | Latches beyond direct noise boundary; separate from rule `DOWNWARD_MOVEMENT` |
| `POSTURE_OR_BAR_LOSS` | Hard trunk game bound, critical joint-limit proximity, saddle break, coupling loss, or separation | Trunk/joint runs use 2 ticks; direct saddle break/coupling and qualified separation use 1 tick | Detail is retained; bounds are game/engine boundaries, never injury limits |
| `FAILED_LOCKOUT` | Ascent established, then raw bilateral knee/hip/trunk posture, bar stillness, and standing-reference height never satisfy lockout | Onset is ascent establishment; bounded completion timer is 60 ticks | Latches at deadline; does not reuse adapter lockout state or include support legality |

## Result policy

- `EVALUABLE / NO_PHYSICAL_FAILURE` requires complete core channels and a
  physically observed descent, bottom, ascent, and direct lockout.
- `EVALUABLE / PHYSICAL_FAILURE` requires a supported latched event; later
  distinct events are deterministic secondary causes and cannot replace the
  primary.
- `INCOMPLETE_ATTEMPT` means the available complete channels do not yet prove
  a full attempt or a failure.
- `INSUFFICIENT_EVIDENCE` means at least one required core channel is missing;
  missing is never zero and never `NO_PHYSICAL_FAILURE`.
- An active or empty trace is `INVALID_TRACE` at the frozen-trace API boundary.

## Explicit exclusions

The detector has no Unity references, no force/torque/transform writes, no
safety actuation, no rule-processor edits, no P1 schema edits, and no load
threshold. The current 0 kg configuration remains insufficient for full
bar-dependent no-failure claims because the canonical bar is unavailable.
The accepted 25 kg real trace is evaluated honestly as incomplete with no
fabricated physical failure; completing its attempt lifecycle belongs to later
GAM-12 integration/closeout.
