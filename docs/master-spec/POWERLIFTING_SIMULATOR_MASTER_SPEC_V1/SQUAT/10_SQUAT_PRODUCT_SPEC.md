# Squat Product Specification

**Document ID:** `PSMS-SQ-10`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `01_PRODUCT_CONSTITUTION.md`, `02_GAME_ARCHITECTURE.md`, `08_INPUT_AND_PLAYER_INTENT.md`

## Repository verification

- Compare the existing M1/M2 squat behavior and scenes against this frozen state machine.
- Preserve any qualified rule logic that is consistent with the current rulebook; document deviations.
- Verify all UI control labels and presentation sequences in the repository.

## Product promise

The player will carry a loaded bar on the upper back, obey the referee, descend under control to legal depth, reverse and stand up without losing the bar or balance. The lift must look recognizably human, respond to load, expose a readable grind,
obey competition-style commands, and fail for physical or control reasons that can be explained after the attempt.

## V1 acceptance sentence

One visually acceptable physical squat from setup through legal lockout at unloaded/light/moderate loads, with heavy grind and supra-max failure emerging from finite capacity.

## Player actions

`Brace, Yield, Drive, Balance, Confirm/Rack`. These actions alter lift-specific reference intent, activation, stabilization, or state
authorization. They never directly set a bone, force, torque, or bar velocity.

## Mechanical identity

- **Contact topology:** left foot–platform, right foot–platform, bar–upper-back/trap saddle; hands are presentation guides in V1.
- **Load path:** bar → upper-back saddle/thorax-pelvis chain → hips/knees/ankles → both feet → platform.
- **Critical landmarks:** bilateral hip-crease points, top-of-knee reference points, foot support polygons, bar centerline, pelvis/thorax frames.
- **Reference intent:** standing → controlled ankle/knee/hip flexion with bounded trunk inclination → legal bottom → coordinated reversal → extension to locked standing.
- **Rule contract:** referee Squat command before descent; hip crease below top of knee; no double descent or meaningful downward bar reversal on ascent; knees and hips locked at completion; wait for Rack command.
- **Failure vocabulary:** shallow, forward/backward balance loss, descent collapse, failed reversal, mid-ascent stall, bar reversal, trunk/posture loss, failed lockout, early rack, command violation.

## Attempt state machine

`SETUP` → `UNRACK` → `WALKOUT` → `SETTLE` → `SQUAT_COMMAND` → `DESCENT` → `BOTTOM` → `REVERSAL` → `ASCENT` → `STICKING` → `LOCKOUT` → `RACK_COMMAND` → `RERACK` → `COMPLETE` → `FAILURE`

Every transition has one owner: the Squat controller. Rules may veto or record a transition but do not move
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

Sumo/stance taxonomy, true grf/cop, muscle forces, spinal tissue loading, safety-spotter ai, generic lift phases.


## Squat-specific product decisions

- V1 uses a powerlifting-style low/high-bar-neutral authored family centered on the actual asset; it does not expose a bar-placement selector yet.
- The walkout is a bounded physical sequence, not a navigation game. A stable three-step authored intent is allowed, but the feet and bar remain physical.
- The bar is retained by one squat-specific upper-back saddle/coupling with finite authority. Hands receive visual IK only in V1.
- The player wins the rep by coordinating controlled yielding, bracing, balance, and drive. Holding Drive early does not skip descent or commands.
- Legal depth comes from anatomy landmarks, never a knee-angle threshold.


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
