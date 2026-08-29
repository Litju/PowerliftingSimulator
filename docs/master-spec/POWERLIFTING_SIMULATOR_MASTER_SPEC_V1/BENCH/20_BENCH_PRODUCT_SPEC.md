# Bench Press Product Specification

**Document ID:** `PSMS-BP-20`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `01_PRODUCT_CONSTITUTION.md`, `02_GAME_ARCHITECTURE.md`, `08_INPUT_AND_PLAYER_INTENT.md`

## Repository verification

- Compare the existing M1/M2 bench press behavior and scenes against this frozen state machine.
- Preserve any qualified rule logic that is consistent with the current rulebook; document deviations.
- Verify all UI control labels and presentation sequences in the repository.

## Product promise

The player will create a stable competition setup, receive the bar, lower it under control, hold it motionless on the chest, press through the sticking region and lock both arms. The lift must look recognizably human, respond to load, expose a readable grind,
obey competition-style commands, and fail for physical or control reasons that can be explained after the attempt.

## V1 acceptance sentence

A stable physically carried bar with two compliant grips, legal touch/pause/press, load-dependent off-chest or midrange failure, and convincing visible hands.

## Player actions

`Brace/Setup, Grip, Yield/Lower, Drive/Press, Balance, Confirm/Rack`. These actions alter lift-specific reference intent, activation, stabilization, or state
authorization. They never directly set a bone, force, torque, or bar velocity.

## Mechanical identity

- **Contact topology:** head/upper back/shoulders/glutes–bench, both feet–floor, left and right hands–bar.
- **Load path:** bar → compliant bilateral grips → hands/forearms/upper arms → shoulder girdle/torso → bench and feet.
- **Critical landmarks:** bar centerline and endpoints, chest/abdomen touch volume, elbow and shoulder joint proxies, head/shoulder/glute/foot contacts, wrist alignment.
- **Reference intent:** authored arch and scapular posture → stable start over shoulders → controlled down-and-slightly-forward path → chest touch/pause → up-and-slightly-back press → bilateral elbow lockout.
- **Rule contract:** head/shoulders/butt and feet satisfy game contact rules; Start before descent; valid chest/abdomen touch; at bottom both elbow proxies reach at least shoulder-depth criterion; bar motionless; Press before ascent; no whole-bar downward reversal; both elbows locked; Rack command.
- **Failure vocabulary:** invalid setup/contact, early descent, invalid touch, early press, off-chest failure, mid-range stall, one-arm imbalance, downward reversal, failed lockout, early rack.

## Attempt state machine

`SETUP` → `ARCH_POSITION` → `GRIP` → `UNRACK` → `START_POSITION` → `START_COMMAND` → `DESCENT` → `CHEST_TOUCH` → `PAUSE` → `PRESS_COMMAND` → `PRESS` → `STICKING` → `LOCKOUT` → `RACK_COMMAND` → `RERACK` → `COMPLETE` → `FAILURE`

Every transition has one owner: the Bench Press controller. Rules may veto or record a transition but do not move
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

Detailed scapulothoracic anatomy, shoulder injury prediction, shirted bench, heaving technique taxonomy, reuse of squat balance/control.


## Bench-specific product decisions

- The bench setup is a meaningful pre-lift state: arch, grip width, foot stability, and unrack quality affect the attempt.
- Two compliant physical grip adapters carry the bar. They allow shaft rotation and small axial/radial compliance so the closed chain is not mathematically rigid.
- The bar must actually touch the authored chest/abdomen contact volume and become motionless before `PRESS_COMMAND`.
- Leg drive modifies whole-body reference stabilization and upper-torso pressure; it does not apply a secret vertical force to the bar.
- Shoulder mechanics are deliberately simplified and labeled. Visual scapular posture is authored; V1 does not model a scapulothoracic joint.


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
