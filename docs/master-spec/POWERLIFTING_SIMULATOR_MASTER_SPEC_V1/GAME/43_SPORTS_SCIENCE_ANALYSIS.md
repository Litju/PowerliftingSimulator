# Sports-Science-Style Analysis

**Document ID:** `PSMS-GAME-43`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `SQUAT/17_SQUAT_TELEMETRY.md`, `BENCH/27_BENCH_TELEMETRY.md`, `DEADLIFT/37_DEADLIFT_TELEMETRY.md`

## Repository verification

- Inspect existing analysis UI and trace processors for unsupported claims.
- Verify all equations/definitions against the final schemas and book/source register.
- Run claim-policy tests against every metric and generated explanation.

## Product role

The analysis layer makes the game legible and scientifically honest. It converts recorded simulation state into
well-defined kinematic, temporal, geometric, and modeled-control summaries. It is **not** a force-plate, motion-capture,
musculoskeletal, clinical, or coaching-validation system.

## Analysis pipeline

```text
Immutable 100 Hz Trace
  -> schema/unit/frame validation
  -> event segmentation by lift-specific state
  -> optional post-attempt filtering
  -> lift-specific metric processors
  -> quality/provenance classification
  -> explanatory summary
  -> charts/replay overlays
```

Each processor is deterministic, versioned, and tested with golden signals.

## Supported V1 categories

### Direct/near-direct simulation observations

- load;
- bar/segment positions and orientations;
- direct Rigidbody linear/angular velocities;
- contact presence;
- joint target/actual orientation;
- attempt state and command timing;
- physical/rule outcome.

### Defensible derived metrics

- bar displacement/path;
- phase and repetition duration;
- depth/touch/lockout geometry;
- mean and peak velocity;
- filtered acceleration with quality flag;
- bilateral bar/hand asymmetry;
- system COM and support margin;
- sticking-region timing/location;
- gravitational potential-energy change of the bar;
- modeled joint command power when explicitly based on conceptual command torque.

### Explicitly not observable/claimable

- true muscle/tendon force;
- individual muscle activation;
- true net joint moment or internal joint force;
- true GRF/COP;
- spinal compression/shear;
- validated CNS behavior;
- injury risk;
- clinical interpretation;
- individual physiological prediction;
- total metabolic cost or real training adaptation.

## Source classes

Every field is one of:

- `SOURCE_DIRECT`
- `SOURCE_DERIVED`
- `ENGINE_RUNTIME_OBSERVATION`
- `ENGINEERING_DERIVED`
- `RULE_DERIVED_GAME_PROXY`
- `BIOMECHANICALLY_INFORMED_GAME_CALIBRATION`
- `PROVISIONAL`
- `PRESENTATION_ONLY`
- `NOT_OBSERVABLE`

The schema also records producer, algorithm, version, units, coordinate frame, filter, and quality.

## Metric language

Correct examples:

- “Simulated bar peak vertical velocity: 0.42 m/s.”
- “Modeled hip-drive demand reached 1.08× configured capacity.”
- “The deepest simulated hip-crease proxy was 12 mm below the knee-top proxy.”
- “A low-velocity interval occurred 0.31–0.58 s after reversal.”

Forbidden examples:

- “Your glutes produced 3,200 N.”
- “Ground reaction force was 2.6× body weight.”
- “Your spine was at injury risk.”
- “This predicts your real 1RM.”
- “The athlete’s nervous system failed.”

## Filtering contract

Rules and physical state machines use raw direct state plus explicit persistence. HUD smoothing is display-only.
Post-attempt noncausal filtering is permitted and stored with:

- input sampling rate;
- filter family/order;
- cutoff;
- pass count;
- edge method;
- algorithm version;
- latency (`0` only because postprocessing is noncausal; it is not usable online);
- quality status.

Acceleration is omitted when event duration or edge support is insufficient.

## Analysis panels

1. **Attempt summary:** load, result, reason, total duration, best phase metrics.
2. **Bar path:** world and athlete-local trajectory, velocity trace, phase markers.
3. **Technique geometry:** lift-specific depth/touch/lockout, joint-angle proxies, COM/support or bar proximity.
4. **Effort model:** configured capacity, modeled demand, saturation dwell, attempt fatigue.
5. **Rules:** commands, legal predicates, violations.
6. **Comparison:** prior attempt overlay only when calibration/ruleset/athlete versions are compatible.
7. **Claim info:** definitions/source class/limitations available from every metric.

## Explanatory engine

Advice is rule-based and traceable:

```text
Observation: bar drifted 74 mm forward of the deadlift close-path reference after knee pass.
Consequence in model: hip/trunk geometric moment-arm proxy and demand rose.
Game suggestion: apply Bar-Close intent earlier while maintaining Drive.
Claim boundary: game-model explanation, not real-world medical or technique prescription.
```

No generative text is allowed to invent values. A text generator, if added later, can only verbalize structured processor
outputs under a claim policy.

## Work and power

- Bar gravitational work estimate: `m_bar g Δy`.
- Modeled joint command power: `τ_concept × ω_joint`.
- Integrate signed modeled power for a modeled work estimate only with the same label.
- Net joint power cannot be equated with individual muscle power; cocontraction and energy transfer are unobserved.
- External work beyond the bar requires interface forces not available in V1.

## Quality states

`VALID`, `VALID_WITH_LIMITATIONS`, `PROVISIONAL`, `NOT_AVAILABLE`, `INVALID_TRACE`.

Reasons include missing events, short trace, reset discontinuity, physics fault, filter edge contamination, calibration
mismatch, and unsupported metric.

## Tests

Golden bar paths; unit/frame errors; event segmentation; filter phase/edges; missing data; sign conventions; power/work
labels; comparison compatibility; no `NOT_OBSERVABLE` value can be emitted; UI significant digits; rule processor remains
raw-state-only; explanatory text must reference source fields.

## Scope

**SHIP_V1:** lift-specific analysis panels, definitions, provenance, honest limitations.  
**LATER:** compatible-attempt trends and coach-style summaries.  
**RESEARCH:** external validation against lab measurements.  
**OUT_OF_SCOPE:** real athlete diagnosis, prescription, injury or clinical claims.
