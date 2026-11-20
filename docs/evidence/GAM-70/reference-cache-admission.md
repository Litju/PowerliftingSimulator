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
