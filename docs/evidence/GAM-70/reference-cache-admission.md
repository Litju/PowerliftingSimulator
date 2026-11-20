# GAM-70 offline close-bar reference and runtime admission

Unity 6000.3.22f1, Windows, batch PlayMode. Raw logs are in `D:\Dev\UnityLogs\GAM70\final` and the ignored `Logs/GAM70`.

## What was removed from live gameplay

Before this change, the close-bar station froze the simulation (`TicksSuspended`) twice while it solved references from the measured plant:

| Work | Where | Measured cost |
| --- | --- | --- |
| Prepared ascent through knee passage: 58 Levenberg–Marquardt keys, 130 iterations each, central-difference Jacobian | bar separation, tick 316 | 7.4–22.6 s across 40 logged runs (8.7 s in the generator run) |
| Suspended close-bar endpoint (600 iterations) and the 52-key post-knee continuation | knee passage, tick 538 | further seconds at knee passage (frame-sliced, suspended) |
| Stock lift path (homotopy, 300 iterations per key) | station load | 15.1–15.7 s |

## Ownership after the change

- **Offline generation.** `GAM70_GENERATE_CLOSE_BAR_REFERENCE` runs the full solvers on the actual athlete model. It uses the qualified B2 attempt with Drive held from tick 284, and measured PULL at 301. It writes `Assets/Resources/Deadlift/GAM70CloseBarReference.json`. The artifact holds bit-exact anchor states (pelvis, bar and planted feet, welded grip frames, realized joint rotations, plus the moving lift state for the continuation). It also holds all 59 ascent keys, all 53 post-knee keys, the suspended endpoint sample and the stock path's solver vectors. It records contract `GAM-70/close-bar-reference/v1`. The fingerprint is a SHA-256 over the declared model: tick and substeps, segment masses, mass properties, collision geometry and friction, joint frames, limits and actuator profiles, ankle-roll limit, bar load and colliders, the B2 vector and the stance and grip spec. The generator asserts the original receipts: PULL 301, knee 538, DOWN 762, GOOD LIFT, 558.8 N minimum airborne foot load, 95.66 mm mean lockout thigh gap. It also asserts a bit-exact JSON round trip and a bit-exact rebuild of the stock path.
- **Preparation while loading.** `DeadliftReferenceAdmission.Prepare` refuses the artifact on any contract or fingerprint mismatch. It rebuilds both references on their recorded anchors and re-checks them against the generator's own contract: closure under 1 mm and 0.3 deg at every interpolated sample, nearest ascent gap above 20 mm, and positive conservative swept clearance. It then factorizes one Gauss–Newton gain per key from the same objective and finite-difference step. Measured load: artifact 3–25 ms, stock path restore 2–9 ms, validation and gains 400–1056 ms.
- **Admission at the physical events.** At separation and at knee passage the measured plant is captured as an anchor.
  - *Identical anchor:* the generated keys are used unchanged. The contract was already verified while loading.
  - *Different anchor:* every key is corrected from its generated configuration by at most 12 fixed-gain Gauss–Newton steps of the same objective, evaluated on the measured anchor. A step is kept only while it lowers that objective. Each interval is checked against the same contract as soon as both of its keys exist, two keys per physics tick.
  - *Horizon:* the pull controller never commands progress beyond the admitted horizon, and it stops short within its existing acceleration bound. On the first tick the keys covering the moving reference's stopping distance are admitted.
  - *Failure:* any failed key or interval rejects the family. The runtime records the reason and returns the bar without Down.
- **Real-time execution** is unchanged: interpolation, feedback and `PoweredJointController` as the only joint writer.

## Receipts

| Case | Admission | Result |
| --- | --- | --- |
| Onset 301, cached | Exact, Exact | PULL 301, knee 538, DOWN 762, GOOD LIFT, release 1466, 558.8 N, 95.66 mm. Never suspended. Max post-physics work 12.2 ms (separation tick) |
| Onset 302, cached | Corrected over 29 + 31 ticks (≤ 1.6 ms/tick), max correction 0.26°/1.0 mm and 0.51°/0.9 mm | Support loss at 779, no DOWN (live solve: support loss at 780) |
| Onset 305, cached | Corrected (≤ 1.9 ms/tick) | GOOD LIFT, DOWN 771, release, 95.82 mm (live solve: bar-leg contact at 452) |
| Onset 320, cached | Corrected, max 1.02°/6.8 mm | Support loss at 552 (live solve: bar-leg contact at 465) |
| Onset 350, cached | Corrected, max 1.18°/9.4 mm | Support loss at 574 (live solve: support loss at 445) |
| Foreign fingerprint | refused while loading | Initialize throws with the fingerprint reason |
| Self-consistent family turned 120° about the vertical | passes loading, rejected at separation (tick 316): planned gap −100 mm | bar returned without Down, DownwardMovement fault, no suspension |

Every corrected family met the same contract as the generated one: ascent min gap 23.8–24.0 mm, swept clearance 21.7–22.1 mm, closure ≤ 0.32 mm / 0.05°.

## Onset sensitivity: causal trace

The live-solve traces at onsets 301, 302, 305, 320 and 350 were aligned at bar separation. At separation the states agree within 1–2 mm (350: pelvis +2.4 mm in z), hand loads agree within 2 N, and the planned pelvis paths agree within 0.5 mm. The runs bifurcate about 47 ticks after separation, at progress 0.065–0.075. In that window the prefix reference moves the pelvis back at up to 180 mm/s and the realized pelvis lags 10–18 mm. Foot COP moves 2 cm toward the toes and the ankle regulator's requested ankle angle steps by about −1.6° in one tick.

- At onset 301 the ankle X torque falls to about 0 Nm and the foot stays flat.
- At onsets 305 and 320 the heel lifts by 17 mm. The loaded sole patch collapses to the toe edge, and the ankle X and lumbar drives saturate (450 Nm, then 556–666 of 390 Nm).

Sub-millimetre differences at separation decide the outcome, so the onset tick is not the cause. The margin of this window is.

Negative A/B: feeding the prefix's planned COM velocity to the ankle regulator, as the post-knee continuation does, made all five onsets fail earlier (296–496). That variant was removed.

## Measured readiness and player Drive onsets

The tick-283 Drive gate is replaced by measured readiness. Preparation begins when two conditions hold:

- the grip pretension has reached its target;
- the bar and the athlete-plus-bar COM are below the rulebook motionless speed (20 mm/s).

The existing plate, foot, balance, hand-load, shoulder and trunk conditions follow. In the B2 attempt readiness first holds at tick 227. `GAM70_CACHED_CLOSE_BAR_DRIVE_ONSETS` gives real Drive at varied ticks, with no acceptance delay, the cached references and the runtime unchanged:

| Drive from | PULL | DOWN | Result |
| ---: | ---: | ---: | --- |
| 1 (held from load) | 245 | 711 | GOOD LIFT, release, 95.94 mm, 555.7 N |
| 228 | 245 | 711 | GOOD LIFT, release |
| 260 | 277 | 743 | GOOD LIFT, release, 95.85 mm |
| 284 (qualified) | 301 | 762 | GOOD LIFT, exact admission, 95.66 mm, 558.8 N |
| 300 | 317 | 783 | GOOD LIFT, release, 95.78 mm |
| 320 | 337 | — | support loss at 549 (before knee passage) |
| 350 | 368 | — | support loss at 635 (after knee passage) |
| 400 | 418 | 883 | GOOD LIFT, release, 95.74 mm |

Six of eight player onsets complete judged lifts. None of the eight runs was suspended, and the attempt's post-physics work peaked at 9.6 ms. The two failures are the known prefix-window support marginality described above, not a readiness or timing gate.

Also negative: bounding the lift rate by the measured inverted-pendulum COP capacity at each key boundary of the piecewise-linear reference turned onset 301 into a support loss (tick 589). It left 302 and 305 unchanged, and that variant was removed.

The real-keyboard station test (`CLOSE_BAR_STATION_USES_HELD_KEYBOARD_DRIVE`) presses W at the Ready cue (tick 227). Drive reaches the simulation at 228 and preparation starts on that tick. Then PULL 244, DOWN 711, GOOD LIFT and supported release follow, with `IntentOverride01` null throughout. Load takes 520 ms. The largest render frame after separation is 18.8 ms. No gen-0 collection occurred during the lift, while the managed heap grew 38 MB. `R_RESTARTS_THE_STATION_ONCE_WITH_A_FRESH_CLOCK_AND_INPUT` reloads exactly once and delivers held W to the new clock.

## Lat engagement at lockout (opt-in, not shipped)

The owner observed that the shipped close-bar lockout still holds the bar well in front of the thighs. The lockout itself is planned that way (92.5 mm). In the endpoint solve a 30-weight preference keeps the bar's fore-aft position over the midfoot, the hips are held behind it, and no term pulls the straight arms back.

`DeadliftLiftPath.LatEngagement` lets shoulder extension draw the welded bar back to the thighs at lockout. `LatBarShiftWeight` replaces that preference, and the forearms keep a 4 mm margin to the abdomen. The shoulder static-capacity and COM-in-support bounds are unchanged.

- **Isolated hold** (`GAM70_SUSPENDED_LOCKOUT_ENDPOINT`, probe `lat-cop`, weight 0): planned gap 41.8 mm, bar 59 mm further back. It holds 300 of 300 ticks, with realized thigh gaps of 42.3/41.8 mm, bar speed 0.13 mm/s, no foot below 50 N, no drive ceiling crossed and zero bar-on-leg force.
- **Complete lifts** at onset 301 with the post-knee continuation aimed at the engaged lockout:

| Lockout bar-shift weight | Lockout thigh gap | Outcome |
| ---: | ---: | --- |
| 0 | about 40 mm | capture point 1.1 mm past the heel edge at tick 721 (progress 0.996), both feet about 850 N |
| 10 | 43.8 mm | same at tick 730, after reaching lockout |
| 18 | 63.0 mm | foot unloaded at tick 823, before Down |
| 30 (shipped) | 95.66 mm | GOOD LIFT |

On arrival the bar keeps travelling back past the plan (z −0.131 vs −0.102 m), and the capture point drifts backward about 1 mm per tick. Pulling the bar in moves the athlete+bar COM behind the midfoot unless the hips come forward. The station therefore keeps the qualified v1 reference. Shipping the engaged lockout needs a COM-preserving hips-through endpoint and arrival, then re-qualification.

## Contract v2: hips-through reference (shipped)

The owner rejected the 95.66 mm close-bar lockout. Contract `GAM-70/close-bar-reference/v2` regenerates the artifact. Its post-knee continuation ends at the hips-through endpoint (mechanism C, `suspended-lockout-endpoint.md`). The bar keeps its supportable fore-aft place. The pelvis advances about 53 mm through the planted leg chains.

| Item | v2 artifact |
| --- | --- |
| Fingerprint | `3cb5fbebf48241d5197f733f043b7a94ea14c57ea944e357d15fa185ca46f506` (model unchanged) |
| Ascent prefix | 59 keys, unchanged from v1. Planned min gap 23.80 mm, conservative swept clearance 22.21 mm, closure 0.304 mm / 0.002° |
| Post-knee continuation | 53 keys with a 74-joint lockout sample. Planned min gap 44.57 mm, swept 43.71 mm, closure 0.525 mm / 0.001° |
| Regeneration | Bit-identical to the committed file (`cmp`) |

### Receipts at onset 301

The live solve (`GAM70_RUNTIME_CLOSE_BAR_ATTEMPT`) and the cached admission (`GAM70_CACHED_CLOSE_BAR_ATTEMPT_RUNS_WITHOUT_SUSPENSION`) give identical results.

- Admission is exact/exact, with no suspension. Peak attempt post-physics work is 13–15 ms at the separation tick.
- PULL 301, separation 316, knee passage 538, DOWN 790, GOOD LIFT.
- Judged return at 1450 with supported grip release.
- Minimum airborne foot load 559.1 N.
- Mean lockout shaft-to-thigh-collider gap **42.38 mm**, against 95.66 mm for v1.

The displaced-family negative control still rejects at separation: planned gap −35.44 mm, bar returned, no Down, no suspension.

### Realized contact contract, split at DOWN

The realized sweep is sampled at every 5 ms substep (`DeadliftRealizedClearanceSweep`).

- **Ascent and lockout, through DOWN.** The v1 acceptance holds. The conservative key-travel bound is **+10.04 mm**: the nearest gap minus half of both capsules' full substep travel. The minimum sampled gap is 10.89 mm at tick 364.
- **Return.** The bar slides down the shins. The minimum is **0.74 mm at tick 1097**, in Lowering, with the bar descending at 0.31 m/s. There the key-travel bound reaches −0.23 mm. The bound treats the bar's whole tangential descent along the near-vertical shin as approach, about 1.5 mm per substep. Around the minimum the sampled gap changed by only 0.02–0.05 mm per substep.
- **Return contract.** The key-travel bound is reported, not asserted. The return must instead meet three conditions:
  - zero reported bar-on-leg force at every tick (measured 0.0 N);
  - no sampled penetration;
  - positive clearance one substep ahead at the sampled closing rate (measured **0.719 mm**).
- **What was not relaxed.** The no-contact requirement for ascent and lockout is unchanged. The return still fails on any solver-reported shaft–leg contact.

### Onset robustness under v2

Both matrices used the cached references: `GAM70_CACHED_CLOSE_BAR_ONSETS` with `GAM70_ONSETS=301,302,305,320,350`, and `GAM70_CACHED_CLOSE_BAR_DRIVE_ONSETS`. No run was suspended.

| Case | v1 (95.66 mm) | v2 (shipped) |
| --- | --- | --- |
| PULL 301 | GOOD, DOWN 762 | GOOD, DOWN 790, 42.38 mm |
| PULL 302 | support loss 779 | support loss 564 (knee 544) |
| PULL 305 | GOOD, DOWN 771 | support loss 567 (knee 547) |
| PULL 320 | support loss 552 | support loss 545 (before knee) |
| PULL 350 | support loss 574 | support loss 573 (before knee) |
| Drive 1 / 228 | GOOD, DOWN 711 | GOOD, DOWN 738, 42.42 mm, 561.2 N |
| Drive 260 | GOOD, DOWN 743 | support loss 538 (knee 519) |
| Drive 284 | GOOD, DOWN 762 | GOOD, DOWN 790 |
| Drive 300 | GOOD, DOWN 783 | support loss 575 (knee 559) |
| Drive 320 | support loss 549 (before knee) | support loss 549 (before knee) |
| Drive 350 | support loss 635 | support loss 630 (knee 610) |
| Drive 400 | GOOD, DOWN 883 | support loss 680 (knee 660) |

v2 completes 3 of 8 Drive onsets and 1 of 5 PULL onsets. v1 completed 6 of 8 and 2 of 5.

- **Pre-knee failures** (PULL 320 and 350, Drive 320) are the documented prefix-window marginality. They are unchanged because the prefix is unchanged.
- **Every other v2 failure is new.** Each is a single-foot support loss 16–24 ticks after the post-knee continuation is admitted with a **corrected** (non-identical) anchor.

### Causal trace of the post-knee failures

Traces are matched at knee passage. Onset 301 (exact admission) passes; onsets 302, 305 and Drive 260 (corrected admission) fail.

1. **Every onset receives the same hand-off transient.** When the continuation takes over, the pelvis forward velocity rises from about 37 to 93–97 mm/s in 6 ticks. The pelvis pitch rate swings from +0.18 to −0.21 rad/s.
   - The requested joint-target rates step within one tick. At onset 301 the knee-extension target falls from 44 to 1 deg/s, the hip from 28 to 10 deg/s and the abdomen from 46 to 0 deg/s. They recover over about 10 ticks.
   - The cause is in `DeadliftPreparedAscentPlan.Target`. The continuation's start correction is computed against the ascent target frozen at the start progress, so the target value stays continuous while its rate steps.
2. **The outcomes diverge in the braking that follows.**
   - At 301 and at Drive 1 and 228, the pelvis settles at 35–55 mm/s.
   - At 302, 305 and Drive 260, it brakes to 0 mm/s or below within 6 ticks. One foot's COP then runs to its toe edge. At 302 the left COP moves about 120 mm toward the toes between ticks 550 and 554. The loaded patch collapses to the 329 mm toe edge, and the heel rises 42 mm by tick 560.
   - No actuator crosses its ceiling before the patch collapses. The left ankle-X demand exceeds its ceiling only afterwards (0.99 at tick 556, 2.97 at tick 560). The whole-body capture point stays more than 100 mm inside the support polygon until the foot is lost.
3. **The ankle-roll loop runs in a standing limit cycle.**
   - Throughout the successful attempt, the across-sole roll request alternates every tick by about ±2°, from before PULL through the lowering. The joint-target rate limiter is active on 60–90 % of ticks.
   - In the failing runs the oscillation grows to the 5.5° clamp in the ticks before the COP excursion. With 42° toe-out, an across-sole COP shift has a world fore-aft component, so the roll oscillation moves each foot's COP fore-aft.
   - A closed-loop regression gives a same-tick across-sole roll authority of about 4–7 mm/deg in both windows, against the identified −10.7 mm/deg at H = 0. Closed-loop data cannot separate the loop gain from the plant, so this does not identify the cause.

### Bounded correction tried: rate-continuous hand-off (negative, reverted)

In this variant the continuation blended from the *moving* ascent target instead of its frozen value. It used the same 0.035-progress smoothstep, with the ascent target extrapolated at its last interval's rate. No key, gain, limit or artifact changed.

| Matrix | Frozen entry (shipped) | Rate-continuous entry |
| --- | --- | --- |
| PULL onsets passing | 301 | 305 (DOWN 799, 42.30 mm) |
| Drive onsets passing | 1, 228, 284 | 1, 228, 350 (DOWN 862, 42.26 mm) |

The count is unchanged while the passing onsets swap. At 301 the rate-continuous entry carried more forward hip momentum. The pelvis reached 416 mm/s at tick 584, both COPs pinned at the heel edge, and the capture margin reached 0 at tick 585. The frozen entry fails by braking (toe pivot), and the continuous entry fails by forward hip momentum (heel pivot).

The hand-off rate step is therefore real, but it is not the controlling variable. In the post-knee window the pelvis momentum runs open loop. Only the ankle COP loops (along-sole and roll) regulate it, and their per-foot authority is what runs out. The change was reverted.

### Open: what would close it

1. Identify the across-sole roll plant open-loop at the post-knee pose with the existing frozen-step method (as `DeadliftAnkleRollTopologyTests` did at H = 0). Derive the roll-loop law from that measurement rather than by tuning.
2. Rerun both matrices, keeping onset 301 and the post-knee mechanics as controls.
3. The pre-knee prefix kink at the 24 mm clearance activation is a separate mechanism and is unchanged.
