# Deadlift Product Specification

**Document ID:** `PSMS-DL-30`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `01_PRODUCT_CONSTITUTION.md`, `02_GAME_ARCHITECTURE.md`, `08_INPUT_AND_PLAYER_INTENT.md`

## Repository verification

- Compare the existing M1/M2 deadlift behavior and scenes against this frozen state machine.
- Preserve any qualified rule logic that is consistent with the current rulebook; document deviations.
- Verify all UI control labels and presentation sequences in the repository.

## Product promise

The player will set over a grounded bar, create tension, break it from the floor, keep it close, extend knees and hips to a controlled lockout, then return it on command. The lift must look recognizably human, respond to load, expose a readable grind,
obey competition-style commands, and fail for physical or control reasons that can be explained after the attempt.

## V1 acceptance sentence

Conventional deadlift with a real grounded-to-free-bar transition, optional pull-slack gameplay, close physical bar path, load-dependent floor/sticking/lockout failure, and controlled return.

## Player actions

`Brace, Grip/Pull Slack, Drive/Pull, Balance/Bar-Close, Confirm/Down`. These actions alter lift-specific reference intent, activation, stabilization, or state
authorization. They never directly set a bone, force, torque, or bar velocity.

## Mechanical identity

- **Contact topology:** both feet–platform, both hands–bar, bar/plates–platform until floor break.
- **Load path:** grounded bar → compliant bilateral grips → arms/shoulders/trunk/hips/knees → both feet; after floor break the bar becomes a free carried rigid body.
- **Critical landmarks:** bar/plate floor clearance, bar-shin/thigh distance, knee and hip extension proxies, front-deltoid/bar relation, foot midline, hand grip states.
- **Reference intent:** conventional setup with bar over midfoot → brace and finite pre-tension → floor break → coordinated knee/hip extension with close bar path → hips through to erect lockout → controlled descent after Down.
- **Rule contract:** no start command; bar may be raised when ready; stand erect with knees locked and shoulders in legal completion geometry; hold motionless for Down; no downward movement before completion; maintain hand control on return.
- **Failure vocabulary:** cannot break floor, floor-break stall, below-knee or knee-level stall, bar drift, hip-extension stall, grip slip/failure, downward reversal, failed lockout, early drop, Down-command violation.

## Attempt state machine

`SETUP` → `GRIP` → `BRACE` → `PULL_SLACK` → `PULL` → `FLOOR_BREAK` → `INITIAL_ASCENT` → `KNEE_PASS` → `STICKING` → `HIP_EXTENSION` → `LOCKOUT` → `DOWN_COMMAND` → `DESCENT` → `GROUND_CONTACT` → `COMPLETE` → `FAILURE`

Every transition has one owner: the Deadlift controller. Rules may veto or record a transition but do not move
the athlete. Presentation observes immutable state and cannot create a legal transition.

## Difficulty

Difficulty is produced by the interaction of:

1. attempt load and bar inertia;
2. finite joint/grip capacity;
3. the geometry-dependent moment demands of this lift;
4. player timing and sustained intent;
5. reference-tracking and balance error;
6. attempt fatigue and loss of technical reserve;
7. command/rule compliance.

There is no random success roll and no binary `load > max` selector. Randomness may vary crowd or cosmetic reaction
only.

## Modes

- **Technique practice:** generous command timing and analysis hints; physical model unchanged.
- **Training attempt:** selected load, optional retry, full telemetry.
- **Meet attempt:** strict commands, attempt clock, lights, no rewind.
- **Calibration/debug:** deterministic scripted intent and controlled perturbations; never exposed as normal gameplay.

## Success truth

A successful physical completion and a valid rules result are independent facts. The final result is a good lift only
when both are true. A player can physically finish but receive a no-lift, or physically fail before a rule judgment.

## Presentation requirements

At all times the athlete and bar dominate the frame. The HUD communicates the next legal action, load, command,
and result; engineering telemetry remains in analysis/debug screens. The lift must have specific audio, camera, strain,
and failure presentation defined in the presentation documents.

## Non-goals

Sumo in v1, biological tendon slack, bar whip physics, straps, mixed-grip injury modeling, reuse of squat phase or balance logic.


## Deadlift-specific product decisions

- V1 is conventional deadlift only. Sumo is a future independent `SumoDeadlift` domain, not a stance parameter injected into this one.
- The bar begins supported by the floor. The floor-break transition is contact-observed; no script toggles gravity or sets bar velocity.
- `PULL_SLACK` represents preparatory tension and player timing. It raises bounded activation/grip/reference readiness but does not claim to simulate literal tendon or bar slack.
- The arms are treated primarily as force-transmission links through finite grip couplings; elbow flexion is not a desired pulling strategy.
- The return phase is physical and controlled after `DOWN_COMMAND`; releasing the bar early is a rule/control failure.


## Content boundary

The V1 domain supplies one canonical technique profile plus calibrated light, moderate, heavy, and supra-max scenarios.
Technique variants are content only when they preserve the frozen mechanical contract and have their own tests.

## Ship gate

This lift ships only when:

- deterministic physical/rule tests pass;
- light/moderate success, heavy grind, and supra-max failure are reproducible;
- no hidden support or duplicate authority exists;
- canonical screenshots/video pass visual inspection;
- reset is repeatable;
- Windows standalone smoke and performance gates pass;
- all displayed metrics have definitions and claim classifications.
