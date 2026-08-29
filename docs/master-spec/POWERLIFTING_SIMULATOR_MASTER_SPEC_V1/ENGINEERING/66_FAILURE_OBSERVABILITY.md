# Failure Observability

**Document ID:** `PSMS-ENG-66`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `SQUAT/16_SQUAT_FAILURE_MODEL.md`, `BENCH/26_BENCH_FAILURE_MODEL.md`, `DEADLIFT/36_DEADLIFT_FAILURE_MODEL.md`, `ENGINEERING/61_STATE_SNAPSHOTS_AND_REPLAY.md`

## Repository verification

- Inspect current logs/debug overlays/failure enums and migration needs.
- Verify Player.log/crash/evidence paths in standalone.
- Run intentional technical-fault injections.

## Purpose

Make every architecture fault, initialization fault, physical failure, rule failure, control failure, and presentation
fault distinguishable and reproducible. “The athlete fell” is not enough.

## Taxonomy

### Technical/architecture

- duplicate physical writer;
- duplicate/missing physics step;
- Animator/transform contamination;
- invalid frame/units;
- NaN/Inf;
- unsupported configuration;
- stale reset state;
- snapshot/schema fault.

### Initialization/geometry

- bar/body/equipment overlap;
- bad joint anchors;
- missing bones/landmarks;
- invalid mass/inertia;
- grip/saddle created misaligned;
- rack/platform clearance fault.

### Numerical/solver

- persistent excessive penetration;
- joint/constraint divergence;
- projection invoked;
- iteration budget exhausted with critical residual;
- tunneling;
- overload/catch-up fault.

### Physical athlete/lift

Lift-specific detector reason from PSMS-SQ/BP/DL-16/26/36: balance, collapse, stall, grip, bar drift/reversal, posture,
lockout, etc.

### Rule

Command/depth/touch/elbow/lockout/contact/Down/Rack violations.

### Player control

Illegal/early/missing intent, abort, unable to recover before physical failure.

### Presentation

Visible follower/IK/camera/UI/audio discrepancy that does not alter truth.

## Failure record

```text
FailureRecord
  failure_id
  attempt_id
  lift
  tick/time/state
  category
  primary_code
  secondary_codes[]
  severity
  latched
  pre_failure_snapshot_range
  decisive_snapshot_hash
  inputs/commands
  configuration/ruleset/calibration IDs
  threshold values and counters
  body/bar/contact/joint diagnostics
  safety_action + start_tick
  player_message
  developer_message
```

## Precedence

1. Technical/initialization faults supersede athlete interpretation.
2. Physical failure is recorded independently from rule failure.
3. Rule violations remain even after physical failure.
4. Player-control reason is explanatory only when traceable.
5. Presentation faults never change result.
6. First irreversible event is primary; secondary causes retain timestamps.

## Event ledger

All transitions, commands, contacts of interest, warning/hard detector changes, failure latch, snapshot publication,
judgment, safety action, and reset are totally ordered by tick plus sequence number.

## Diagnostic levels

- **Release normal:** concise player reason and diagnostic code.
- **Analysis:** decisive replay, metric/event explanation.
- **Development:** complete frame/joint/contact/demand/solver/writer data and debug drawing.
- **Research:** optional full contact/segment export.

No release log dumps sensitive paths or excessive personal data.

## Debug views

- physical/reference/visible skeleton overlay;
- colliders/contacts/penetration;
- joint axes/limits/targets/errors/demand;
- COM/support/depth/touch/lockout;
- bar path/endpoints/floor clearance/proximity;
- input/state/command/rule guards;
- pipeline stages/writers;
- solver/settings/performance;
- event timeline and snapshot hash.

## Automatic bundles

On technical fault or failed qualification scenario, write a bounded evidence bundle:

- config/version report;
- last N snapshots/events;
- failure record;
- Player.log excerpt;
- screenshot;
- optional short replay;
- test/scenario ID;
- checksum.

## Safety ordering

A safety controller can change physical behavior after a failure only when:

1. failure is latched;
2. decisive snapshot/event is published;
3. rule processor has recorded the pre-safety state;
4. safety action receives explicit start tick;
5. replay includes the transition.

## Tests

Each taxonomy class; compound precedence; initialization vs controller regression; first irreversible event; event total
order; safety next-tick; bundle completeness; player/developer language; snapshot hash; no unlatch; release privacy.

## Scope

**SHIP_V1:** taxonomy, records, debug/evidence bundles, safety ordering.  
**LATER:** crash upload/telemetry dashboard with consent.  
**RESEARCH:** automated root-cause ranking.  
**OUT_OF_SCOPE:** hiding technical faults as athlete failures.
