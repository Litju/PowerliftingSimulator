# Claims and Provenance

**Document ID:** `PSMS-ENG-65`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `00_READ_ME_FIRST.md`, `03_COORDINATES_UNITS_NUMERICS.md`, `GAME/43_SPORTS_SCIENCE_ANALYSIS.md`

## Repository verification

- Replace design-time source summaries with exact chapter/page/section locators during implementation.
- Verify all current official web sources and asset licenses at release.
- Audit every user-facing metric, tooltip, store claim, screenshot, and press copy.

## Evidence register

The design distinguishes five evidence layers.

| Class | Meaning | Examples in this bundle |
|---|---|---|
| `SOURCE_DIRECT` | A value or rule copied from a named source with a locator. | IPF bar dimensions; Winter segment mass fractions. |
| `SOURCE_DERIVED` | A deterministic calculation from direct source values. | Total loaded bar mass; whole-body COM. |
| `ENGINEERING_DERIVED` | A quantity computed from engine state under an explicit model. | Modeled joint-drive demand; state-derived bar acceleration. |
| `GAME_CALIBRATION` | A deliberate parameter selected for playability or numerical behavior. | Drive gains; fatigue rate; referee settle window. |
| `PROVISIONAL` | A proposed value that must pass an implementation fixture. | Solver iterations; grip compliance; visual tolerances. |
| `NOT_OBSERVABLE` | A quantity that the game does not have evidence to claim. | True muscle force, tendon force, biological joint loading. |

### Book sources used

1. Jason Gregory, *Game Engine Architecture*, 4th ed., Volumes I and II, CRC Press, 2026: runtime architecture, game-loop phasing, animation/physics integration, collision and rigid-body integration, game objects, debugging and performance.
2. Ian Millington, *Game Physics Engine Development*, 2nd ed., CRC Press, 2010: rigid-body inertia, contacts, friction, penetration resolution, iterative solver limitations and stability.
3. Christer Ericson, *Real-Time Collision Detection*, Morgan Kaufmann/CRC Press, 2005: geometric and floating-point robustness, tolerances, thick primitives and coherent queries.
4. Stephen J. Thomas, Joseph A. Zeni and David A. Winter, *Winter's Biomechanics and Motor Control of Human Movement*, 5th ed., Wiley, 2023: anthropometry, multisegment COM, kinematics, filtering, work/power, balance and simulation claim limits.
5. Karl J. Åström and Richard M. Murray, *Feedback Systems*, 2nd ed., Princeton University Press, 2021: reference tracking, damping, saturation, discrete implementation and system/control co-design.
6. R. C. Hibbeler, *Engineering Mechanics: Dynamics*, 14th ed., Pearson, 2016; Joaquim A. Batlle and Ana Barjau Condomines, *Rigid Body Dynamics*, CUP, 2022: Newton–Euler mechanics, momentum, work/energy and frame discipline.
7. Kevin M. Lynch and Frank C. Park, *Modern Robotics*, CUP, 2017: rigid transforms, twists/wrenches, constrained dynamics and the boundary between useful control concepts and research-grade whole-body control.
8. Richard L. Lieber, *Skeletal Muscle Structure, Function, and Plasticity*, 3rd ed., 2010: force–length/velocity concepts and the limits of translating muscle physiology into a game actuator.
9. Franz Lanzinger, *3D Game Development with Unity*, 2022, and Penny de Byl, *Holistic Game Development with Unity*, 2012: Unity production workflow, testing, release, presentation and the integration of design, art and programming.

### Web and primary-rule sources verified on 2026-08-28

- International Powerlifting Federation, *Technical Rule Book*, version 3, effective 2026-03-01.
- International Powerlifting Federation, IPF GL Points materials.
- Unity 6 official Manual and Scripting API: ConfigurableJoint, JointDrive, Physics.Simulate, Rigidbody interpolation, collision detection modes, fixed timestep and Input System update modes.
- Quaternius, *Universal Base Characters*, August 2025, official page: humanoid rig, superhero proportions and CC0 distribution. The exact imported project asset remains repository-verification work.
- Peer-reviewed powerlifting biomechanics indexed by PubMed, including Escamilla et al. (2000) on conventional versus sumo deadlift, van den Tillaar and Ettema (2010) on the bench sticking period, van den Tillaar et al. (2020) on squat bar placement, Gundersen et al. (2025) on deadlift variants, and the 2025 systematic review of load/fatigue effects across the three lifts.

### Evidence ceiling

The books and studies constrain motion, geometry, terminology and analysis choices. They do **not** validate this game's actuator parameters as human torque, establish individual injury risk, or convert a PhysX constraint drive into a measured biological muscle model. Every such boundary is explicit in `ENGINEERING/65_CLAIMS_PROVENANCE.md`.


## Claim object

Every scientific/rule/engineering metric or user-facing explanatory statement is traceable through:

```text
ClaimDefinition
  id
  display_name
  definition
  equation_or_algorithm
  units
  coordinate_frame
  source_class
  source_locator[]
  assumptions[]
  limitations[]
  calibration_version
  validation_status
  allowed_phrasing[]
  forbidden_phrasing[]
  owner
```

## Source hierarchy

1. Current official primary rules/Unity documentation for current behavior.
2. Uploaded textbooks for mechanics, collision, architecture, control, biomechanics, and claim limits.
3. Peer-reviewed primary research/systematic reviews for lift-specific context.
4. Project measurements and deterministic runtime experiments.
5. Engineering inference.
6. Game calibration.
7. Presentation.

A lower class cannot be promoted by confident language.

## Book-derived anchors

- Winter/Thomas/Zeni: segment anthropometry, multisegment COM, kinematics/kinetics/work/power and their limitations,
  signal processing, model assumptions and validation.
- Gregory: engine subsystem boundaries, game loop/timing, animation/physics ordering, gameplay objects, debugging and
  production architecture.
- Millington/Ericson: rigid-body/contact concepts, contact generation/resolution, friction, numerical/geometric robustness.
- Åström/Murray: feedback, saturation, anti-windup, discrete implementation, architecture and interaction.
- Hibbeler/Batlle/Barjau: Newton–Euler, free-body reasoning, inertia, work/energy, reference frames.
- Lynch/Park: frame/rigid-motion/control vocabulary used only as engineering reference; shipping design is not a robot.
- Unity texts: practical workflow context, subordinate to current official Unity documentation.

## Web-derived anchors

- Current IPF Technical Rules Book and GL Points page.
- Current Unity 6 documentation for ConfigurableJoint, physics simulation, timestep, solver, collision, input and
  interpolation.
- Quaternius current asset/package page and license, plus the exact local package.
- Peer-reviewed powerlifting biomechanics and sticking-region research.

## Scientific ceiling enforcement

The compiler/test layer cannot understand all language, so enforcement combines:

- typed source-class fields;
- metric registry;
- allow-listed UI formatters;
- source/definition links;
- forbidden-term/claim lints in analysis copy;
- test fixtures;
- human release audit.

`NOT_OBSERVABLE` metrics have no numeric value path.

## Runtime observations versus documentation

Unity documentation describes public semantics, not the exact internal impulse/torque applied in every configuration.
Project experiments can establish observed behavior for the frozen version/configuration, labeled
`ENGINE_RUNTIME_OBSERVATION`. `ConfigurableJoint.currentTorque` remains a solver diagnostic and cannot become modeled
athlete torque.

## Rules versioning

Every judgment stores the exact ruleset ID/effective date and proxy implementation. A current-rule update requires:

- source verification;
- rule/proxy diff;
- tests;
- migration/compatibility decision;
- release note;
- no retroactive mutation of old records/replays.

## Research use

Peer-reviewed research constrains plausible design and terminology. A study on a particular population/technique/load
does not justify universal values. Systematic reviews and multiple sources inform qualitative direction; game coefficients
remain calibration until validated.

## Citation/locator format

Store title, authors/organization, edition/year, chapter/section/page or official document section, DOI/document version,
access date for web, and a short supported proposition. Do not store pirated-distribution URLs in shipped documentation;
use publisher/official locators.

## Provenance tests

Every displayed metric resolves to a claim; every claim has unit/frame/source; source locator present for direct/derived;
no forbidden phrase; `NOT_OBSERVABLE` has no formatter; version mismatch visible; rule update golden tests; no citation
supports a stronger claim than its text.

## Release statement

The product may say:

> A physics-based powerlifting simulation using an anthropometrically informed articulated athlete, physical equipment
> and contacts, force-limited powered joints, biomechanically informed reference motions, and competition-rule analysis.

It may not call itself clinically validated, a force plate, a musculoskeletal simulator, or a predictor of real athlete
performance without future evidence.

## Scope

**SHIP_V1:** complete source/claim registry and enforcement.  
**LATER:** automated documentation pages and source update tooling.  
**RESEARCH:** external validation studies.  
**OUT_OF_SCOPE:** overstated scientific marketing.
