# Gameplay and Difficulty

**Document ID:** `PSMS-GAME-41`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `01_PRODUCT_CONSTITUTION.md`, `08_INPUT_AND_PLAYER_INTENT.md`, `06_ATHLETE_CAPACITY_MODEL.md`, `GAME/40_MEET_SYSTEM.md`

## Repository verification

- Inspect current control scheme and tutorial/presentation systems.
- Run human playtests after physical qualification; do not tune only from scripted agents.
- Verify accessibility requirements for target storefront/platform.

## Design objective

Create a skill game in which the player reads the visible athlete/bar, times intent, manages balance and capacity, selects
loads intelligently, and learns competition procedure. Difficulty must not be manufactured by input lag, random failure,
opaque stat checks, or fake physics.

## Core player skill dimensions

| Dimension | Player expression | Physical/game consequence |
|---|---|---|
| setup | stance/grip/rack/brace choices | reference geometry, contact stability, available reserve |
| timing | commands, descent, reversal, press/pull | legal sequence and activation timing |
| rate control | Yield/Drive intensity | reference phase rate and tracking demand |
| balance/path | bounded correction | target-pose bias, bar/COM geometry |
| effort management | sustained Drive/Brace | activation and attempt fatigue |
| load selection | attempt declaration | probability of physically completing based on finite capability |
| meet strategy | attempt progression | total and placing |

## Difficulty axes

Difficulty is a named profile of **assistance and information**, not a different hidden physics engine.

1. **Command assistance:** prompt timing and grace visualization.
2. **Technique assistance:** auto-center reference bias within a bounded range.
3. **Input complexity:** analog versus simplified digital intent.
4. **Analysis visibility:** live velocity/path hints versus post-attempt only.
5. **Opponent strength:** seeded meet field.
6. **Economic/career pressure:** recovery/resources and attempt consequences.
7. **Rule strictness:** only through an explicit ruleset, never an undocumented slider.

The athlete's physical capacity is determined by profile/progression and attempt state, not by the difficulty menu.
An accessibility assist can add bounded reference stabilization, but it is visibly declared and excluded from ranked
challenge records if necessary.

## Recommended profiles

### Assisted

- strong command prompts;
- hold-to-brace automation after setup;
- small auto-balance reference correction;
- visual bar path/depth/touch cues;
- generous tutorial reset;
- same finite capacity and contacts.

### Standard

- full intent controls;
- normal command prompts;
- small natural controller stabilization only;
- post-attempt analysis;
- meet rules and attempt consequences.

### Simulation Challenge

- minimal prompts;
- no player-facing live metric overlays;
- strict command input;
- reduced assistance, not reduced numerical stability;
- deterministic leaderboard scenario configuration.

## Load-to-experience curve

Load bands are calibrated per athlete/profile from deterministic sweeps:

- **Technique:** high reserve; tracking dominates.
- **Working:** moderate demand; small errors recoverable.
- **Heavy:** visible slowing/sticking and little reserve.
- **Limit:** success requires strong timing/path and may fail.
- **Supra-max:** expected physical failure, but not guaranteed by a load comparison; the model still runs.

The game may estimate an attempt difficulty from prior simulated results, but it must label the estimate and uncertainty.
It cannot leak the deterministic outcome before the attempt.

## Feedback language

During a lift, feedback is perceptual:

- bar speed and direction;
- athlete strain animation/audio;
- visible balance/posture;
- referee commands;
- controller/haptic resistance cues where available.

After a lift, feedback can name:

- rule reason;
- physical failure reason;
- phase timing;
- bar path/velocity;
- modeled demand reserve;
- actionable game advice tied to observed variables.

No clinical or injury language.

## Failure fairness rules

- Every failure has a trace and detector reason.
- The same initial state and input trace produce the same scenario result within the deterministic test environment.
- Random cosmetic variation never changes physics/rules.
- Numerical faults are not presented as athlete weakness.
- A player can review the decisive moment.
- Difficulty profiles cannot silently change gravity, bar mass, rules, or solver quality.

## Tutorial progression

1. Commands and empty/light equipment.
2. Brace/Yield/Drive with ghost reference.
3. Legal squat depth.
4. Bench touch/pause/Press.
5. Deadlift grip/slack/floor break/Down.
6. Heavy sticking and recovery.
7. Meet attempt selection.
8. Full meet.

Tutorial assists are removed one at a time and each lesson has a deterministic mastery scenario.

## Balancing process

1. Qualify unloaded physical stability.
2. Calibrate light/moderate/heavy/supra-max load sweeps.
3. Tune input-rate ranges and assistance bounds.
4. Run novice/expert scripted traces.
5. Conduct human playtests focused on readability and fairness.
6. Lock scenario/calibration versions for release.

## Tests

Profile does not alter gravity/bar mass; accessibility correction remains bounded; same input/profile deterministic;
supra-max has no direct outcome branch; tutorial gates correspond to actual rule/physics variables; failure advice maps
to trace data; ranked records include assists/calibration IDs; no numerical fault is classified as player failure.

## Scope

**SHIP_V1:** three profiles, tutorials, deterministic load bands, fair failure explanations.  
**LATER:** adaptive tutorials and accessibility remapping.  
**RESEARCH:** player-skill modeling.  
**OUT_OF_SCOPE:** monetized stat boosts or opaque rubber-banding.
