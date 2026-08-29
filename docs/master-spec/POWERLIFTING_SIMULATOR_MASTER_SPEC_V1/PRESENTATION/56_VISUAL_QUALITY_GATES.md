# Visual Quality Gates

**Document ID:** `PSMS-PRES-56`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `PRESENTATION/50_RENDER_ARCHITECTURE.md`, `PRESENTATION/51_CHARACTER_PRESENTATION.md`, `PRESENTATION/52_CAMERA_AND_BROADCAST.md`

## Repository verification

- Inspect and index the seven historical M2 screenshots and current capture scripts.
- Define exact per-lift visual tolerance values during implementation.
- Verify standalone capture automation and metadata.

## Gate philosophy

Automated tests cannot certify a believable human powerlifter. Every milestone therefore includes deterministic visual
evidence reviewed against explicit gates. A green test suite with broken hands, feet, spine, bar, or camera is a fail.

## Required capture configuration

- Windows standalone, release-like quality.
- 1920×1080 minimum; supported alternate aspect ratio capture.
- Fixed athlete/profile/load/calibration/ruleset/build.
- No editor-only components except a separate debug-evidence capture.
- Metadata sidecar: commit/tree, scene, tick/time, state, load, camera, graphics settings, screenshot hash.

## Cross-cutting gates

### Character

- visible body follows physical pose;
- no mesh explosions or detached limbs;
- feet contact platform/bench floor plausibly;
- hands align within declared bounds;
- head/neck remain believable;
- no Animator snapping at physics activation/reset;
- failure pose reads as physical.

### Bar/equipment

- bar is not floating, teleporting, clipping catastrophically, or visibly double-simulated;
- plates/collars symmetric and correctly loaded;
- hooks/floor/bench contacts read clearly;
- render flex does not detach authoritative geometry;
- rack/bench/platform scale is plausible.

### Contacts

- no visible deep penetration at setup or active lift;
- minor collider approximation does not break silhouette;
- foot skating below good-lift threshold;
- bench body contact matches rule state;
- bar/chest/back/leg contact matches lift.

### Motion

- unloaded/light motion is stable, not robotic jitter;
- heavy motion is slower/strained without solver buzzing;
- failure emerges smoothly enough to read;
- no one-frame jumps at phase transitions;
- snapshot interpolation removes visible cadence stutter.

### Camera/UI

- active athlete/bar/critical geometry framed;
- prompts/commands/lights synchronized;
- no occlusion at depth/touch/lockout;
- readable text at target resolution;
- no debug overlay in release captures.

### Lighting/materials

- stable exposure;
- athlete/bar silhouette readable;
- shadows anchor contacts;
- no severe aliasing, z-fighting, clipping, or blown highlights;
- palette/contrast passes accessibility review.

## Required lift capture matrix

### Squat

Setup, unrack, walkout, command, quarter descent, legal bottom (side), reversal, heavy sticking, lockout, Rack, forward
failure, backward failure, shallow no-lift, bar/posture loss.

### Bench

Setup/arch, grip, unrack, Start, descent, touch, pause, Press, heavy sticking, bilateral lockout, Rack, early press, off-
chest failure, one-arm imbalance, grip loss/safety.

### Deadlift

Setup, grip/brace/slack, floor break, mid-shin, below knee, knee pass, heavy sticking, hip extension, lockout, Down,
ground contact, cannot break floor, bar drift, grip failure, early drop.

## Video gates

At least one real-time and one 0.25× replay per good/heavy/failure scenario. Review frame pacing, contact, interpolation,
camera, audio sync, and state transitions. Still images alone cannot expose jitter.

## Objective tolerances

- visual hand-target error within PSMS-51 bound;
- successful bar tilt/foot slide/contact penetration within lift-calibrated visual gates;
- no frame > release worst-case budget during capture except documented loading transition;
- no NaN/Inf/physics fault in capture log;
- screenshot resolution/hash/metadata present.

These do not replace expert visual review.

## Review process

1. Engineer self-review against checklist.
2. Automated evidence index generation.
3. Owner visual review.
4. Any rejected frame links to scenario/tick/trace.
5. Fix and recapture; do not crop/hide the defect.
6. Freeze accepted evidence hashes in milestone receipt.

## Mutation/negative evidence

Capture known broken variants during test development—wrong joint frame, infinite drive, hand IK off, spawn penetration,
foot pin, bar teleport—to prove the gate/reviewer can distinguish them. Negative captures are development evidence only.

## Release gate

Release fails on any critical character/bar/contact/camera defect, missing required scenario, unreviewed evidence,
or discrepancy between screenshot metadata and qualified build.

## Scope

**SHIP_V1:** full capture matrix and owner review.  
**LATER:** automated pose/image anomaly checks.  
**RESEARCH:** perceptual metric models.  
**OUT_OF_SCOPE:** replacing human visual acceptance with unit tests.
