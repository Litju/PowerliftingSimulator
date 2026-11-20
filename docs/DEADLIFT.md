# Physical deadlift (GAM-70)

The deadlift is a physically simulated 75 kg sumo pull on the canonical athlete. Every joint is driven by `PoweredJointController`, the only joint-command writer. Contacts, grips and the referee are physical. No body, bar or force is written after tick 0. Entry point: `DeadliftStationController` (scene `DeadliftPrototype`) composing `DeadliftAttemptRuntime` on the foundation's authoritative 100 Hz clock.

## Control phases

| Phase | Owner | Ends when |
| --- | --- | --- |
| Setup | B2 initial condition placed before tick 0. Both grips are welded (`BenchGrip`) and the ankle-roll DOF is enabled. | tick 0 |
| Grounded pretension | `DeadliftPretensionLoop` ramps the hand load to 0.75 × half the bar weight per hand over ticks 100–200. The ankle balance regulator holds the soles. | pretension reached |
| Preparation (`PrepareForPull`) | It starts once Drive is held and the plant is measured ready: pretension at target, and bar and athlete+bar COM below the rulebook motionless speed (20 mm/s). Plates loaded, both feet flat, support margin ≥ 40 mm. The shoulders are then set by 2°, then the hand load and trunk brace are checked. | all conditions hold, then Pulling |
| Pulling | `DeadliftPullController` ramps the bar-break extension while the plates unload. Releasing Drive or ESC aborts and re-arms. | both plates below 2 N on two ticks with the bar rising |
| Ascent below the knee | The prepared ascent reference is admitted at separation (below). | bar rise ≥ knee passage |
| Ascent above the knee | The post-knee continuation into the suspended close-bar endpoint is admitted at knee passage. It adds contact-consistent COP targets, the planned COM and a suspended-bar preload. | progress 1 → Lockout |
| Lockout | The referee watches. It gives Down after 0.3 s motionless with knees locked, an erect torso and shoulders back. | Down |
| Lowering | The bar brakes at the measured knee waypoint, checks the motionless bar plus bilateral and active support, then blends into the stock lower reference. | plates ≥ 600 N |
| Returned | The grips are released after supported rest and a completed referee decision. | — |

The progress rate is bounded by Drive × 0.30 /s and by an acceleration of 0.6 /s². It also stops in time at lockout and at the admitted-reference horizon. The lift holds when support margin or joint demand runs out.

## Reference generation, admission and caching

- **Offline.** `DeadliftTransitionTests.GAM70_GENERATE_CLOSE_BAR_REFERENCE` (Explicit) runs the full closed-chain solvers on the actual model at the qualified attempt. It writes `Assets/Resources/Deadlift/GAM70CloseBarReference.json`. The artifact is bit-exact and contains the anchor states, every key, the endpoint sample and the stock lift path.
- **While loading.** `DeadliftReferenceAdmission.Prepare` compares the fingerprint, re-checks each reference against the generator's contract and factorizes per-key Gauss–Newton gains (about 0.4–0.5 s).
- **At the events.** An identical measured anchor reuses the keys. Otherwise each key gets a bounded fixed-gain correction of the same objective, two keys per tick, and each interval is re-checked. The rejection rule is in `DeadliftReferenceAdmission`.
- **Invalidation.** The fingerprint (`DeadliftCloseBarReference.ComputeFingerprint`) covers tick and substeps, segment masses, inertia, colliders and friction, joint frames, limits and actuator profiles, the ankle-roll limit, bar load and geometry, the B2 vector and the setup spec. `ContractVersion` must be bumped whenever the reference objective, its sampling or the admission contract changes. After any such change, rerun the generator and commit the artifact. A mismatch refuses to arm the station with a clear message. It never falls back to stock or to a live solve.
- **Live solve.** The original live-solve path (`UseReferenceCache = false`) remains the generator and a regression control. It is not used by the shipped station.

## Contact and support model

- **Foot support.** Feet are boxes on the platform with measured per-tick contact impulse, COP and active patch (`DeadliftFootPressureSensor`).
- **Monitor.** `DeadliftSupportMonitor` reports the capture point against the potential (loaded soles) and active (contact patch) polygons.
- **Bar.** The bar rests on two plate aggregates. Shaft–leg contact is sensed and judged (`ThighSupport`).
- **Collision approximation.** The bar does not collide with the limbs that grasp it, a declared approximation.

## Player input

| Key | Effect |
| --- | --- |
| W / Up | Drive, through the foundation intent pipeline. Drive is the raw command source; no override is used in the shipped path. |
| ESC | Aborts before liftoff. |
| R | Reloads the station once. |
| V | Toggles the side view. |

- **Pressing W.** W is an intent that is applied at once. The pull itself starts when the measured preparation completes, and the HUD names the condition being waited for.
- **Releasing W.** While lifting, the progress rate decelerates to zero within the acceleration bound and the bar is held where it is. Pressing again continues. There is no scripted continuation.

## Referee integration

`DeadliftReferee` sees only completed physical observations (`DeadliftAttemptObserver`). It gives Down, records every fault with its tick, measurement and IPF rule reference, and completes the attempt. The controller obeys Down; the referee never reads controller intent.

## Known limitations

- **Onset marginality.** About 47 ticks after separation, the prefix reference's pelvis motion changes slope sharply. This happens where the 24 mm planned shin clearance becomes active. Whether support holds there depends on sub-millimetre state. Six of eight player Drive onsets complete (1, 228, 260, 284, 300, 400); 320 and 350 lose support. Two A/B fixes were negative (see `docs/evidence/GAM-70/reference-cache-admission.md`).
- **Rejected admission.** A reference rejected after separation returns the bar without Down. That path is only qualified as "reported, no lift, no suspension". The hand-load reference keeps carrying the bar near the floor, so the grips are not released.
- **Allocation.** The managed heap grows about 38 MB during a lift from dictionary-based kinematics. No gen-0 collection was observed during the lift.

## Handoff contract for game history and competition

The physical referee is the only authority on validity. A history or progression system consumes a read-only attempt record built once the referee completes:

| Field | Source |
| --- | --- |
| Athlete id, session id, attempt number | game layer (new) |
| Lift type, bar load (kg) | `deadlift`, `DeadliftQualifiedSetup.BarKg` / `PhysicalBarbell` load |
| Start tick, terminal tick and phase | `Pull.SeparationTick`, `Runtime.Tick` at completion, `Pull.Phase` |
| Decision | `Referee.GoodLift`, `Referee.Completed` |
| Faults | `Referee.Faults` (fault, tick, time, detail, `RuleReference`) |
| Down tick, max bar rise | `Referee.DownTick`, `Referee.MaxBarRiseM` |
| Rulebook version | `DeadliftRulebook.Source` |
| Simulation and reference version | `PhysicalAthleteDefinition.ProfileId`, `DeadliftCloseBarReference.ContractVersion`, `Runtime.ModelFingerprint` |
| Admission | `AscentAdmission`/`PostKneeAdmission` outcome (Exact/Corrected), `ReferenceRejection` |

Engineering diagnostics (tick timings, contact traces) stay out of player history.
