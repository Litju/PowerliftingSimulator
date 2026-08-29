# Product Constitution

**Document ID:** `01`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `00_READ_ME_FIRST.md`

## Repository verification

- None.

## Product identity

**Working product:** a single-player, physics-based powerlifting game centered on squat, bench press and conventional deadlift.

**Player fantasy:** be the lifter; select attempts; execute technique under load; obey commands; manage a meet; improve an athlete; study performance.

**Product promise:** a visible human, a physical bar and understandable causality. A light lift should look and feel controlled. A near-maximal lift should move more slowly, expose technical deviations and demand better timing. A supra-maximal lift should fail because finite authority cannot realize the requested motion—not because a hidden threshold plays a failure animation.

## Non-negotiable product hierarchy

1. **Game readability**
2. **Visible human quality**
3. **Physical coherence**
4. **Rule coherence**
5. **Scientific honesty**
6. **Depth of simulation**

A deeper model that harms the first five is rejected.

## Audience

- powerlifting and strength-sport players;
- sports-science and biomechanics enthusiasts;
- simulation-game players who value causal systems;
- coaches and lifters as an entertainment audience, not as clinical users.

## Core loop

```text
Choose athlete/load
→ warm up or enter meet
→ set up and receive commands
→ control lift-specific intent
→ succeed, grind or fail physically
→ receive referee result
→ inspect replay and concise analysis
→ progress athlete/career
→ choose the next attempt
```

## Difficulty contract

Difficulty comes from four coupled sources:

- **load ratio:** selected load relative to calibrated athlete capacity;
- **technical geometry:** moment arms, balance, bar path and joint coordination;
- **player execution:** brace, descent/drive timing, correction and command discipline;
- **state:** fatigue, confidence/readiness only where mapped to explicit game variables.

Difficulty must not come from invisible random failure rolls. Small stochastic presentation variation is allowed, but attempt truth is deterministic for the same initial state and input trace within a qualified build/tolerance.

## Science and claims

Permitted headline:

> “A physics-based powerlifting simulation using an anthropometrically informed articulated athlete, physical equipment and contacts, force-limited powered joints, biomechanically informed reference motions, and competition-rule analysis.”

Forbidden without later validation:

- “accurate muscle forces”;
- “true joint loading”;
- “validated nervous-system behavior”;
- “injury risk”;
- “individual training prescription”;
- “force-plate equivalent”;
- “medical or rehabilitation guidance.”

## Rules identity

The default competition mode is a **federation-style ruleset derived from the IPF Technical Rule Book, version 3, effective 1 March 2026**. The game must:

- version the rule profile;
- identify every simplification;
- avoid implying official federation endorsement;
- support later fictional/arcade profiles without changing physical truth;
- use IPF GL Points only through a versioned coefficient provider.

## Content scope

### SHIP_V1

- one canonical male superhero-proportioned humanoid, with athlete customization limited to gameplay attributes and safe cosmetics;
- squat, bench press and conventional deadlift;
- training/free-play and a complete nine-attempt meet;
- recorded-state replay and post-attempt analysis;
- one competition venue and one training venue;
- keyboard and gamepad;
- Windows desktop;
- career-lite progression sufficient to sustain a product loop.

### LATER

- multiple bodies/proportions after revalidation;
- sumo deadlift as a distinct domain;
- alternative squat styles and wider customization;
- additional venues, local records, accessibility modes and asynchronous comparison.

### RESEARCH

- richer angle/velocity capacity curves;
- validated motion-capture reference sets;
- learned motion priors;
- advanced physical animation or optimal-control experiments.

### OUT_OF_SCOPE

- clinical or musculoskeletal simulation;
- multiplayer authoritative physics for V1;
- VR;
- injury simulation;
- equipment deformation affecting truth;
- open-world gym management.

## Licensing constitution

The intended legal separation is:

1. **source repository:** All Rights Reserved / proprietary; no permission to copy, modify, redistribute or create derivatives without written authorization;
2. **compiled game:** a player EULA that permits installation and play;
3. **third-party assets:** retained under their own licenses and provenance records;
4. **research/books/rules:** cited as sources; no copyrighted book content is redistributed.

The exact Quaternius imported file and license evidence must be captured from the repository before release. The official current Universal Base Characters page identifies the pack as CC0, but that does not prove which historical asset file was imported.

## Acceptance

This constitution passes when every implementation choice can answer:

- Does it improve the player’s experience of being the lifter?
- Does the human remain visually central?
- Does the physical outcome remain causal and finite?
- Can a failure be diagnosed?
- Is the scientific claim no stronger than the evidence?
- Can an indie team ship and maintain it?
