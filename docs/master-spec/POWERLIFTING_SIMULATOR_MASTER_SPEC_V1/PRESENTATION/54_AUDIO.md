# Audio

**Document ID:** `PSMS-PRES-54`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `PRESENTATION/51_CHARACTER_PRESENTATION.md`, `PRESENTATION/52_CAMERA_AND_BROADCAST.md`, `GAME/44_REPLAY_SYSTEM.md`

## Repository verification

- Inventory existing M2 clips/mixer/events and licenses.
- Verify current Unity audio configuration and standalone latency.
- Conduct loudness, command intelligibility, and accessibility review.

## Purpose

Make load, contact, effort, command, venue, failure, and success readable through a layered powerlifting soundscape while
never making audio the only source of gameplay information.

## Audio buses

- Master
- Music
- Venue/Crowd
- Referee/Voice
- Athlete
- Equipment/Contacts
- UI
- Replay/Analysis

Each has independent settings and accessibility subtitles/visual equivalents.

## Event sources

Audio responds to authoritative events/snapshots:

- plate loading/rack contact;
- footsteps/walkout;
- bar/back or grip engagement;
- referee commands;
- breathing/brace/strain scalar;
- bar/plate floor contact and collision impulse band;
- sticking/failure onset;
- lockout;
- referee lights/result;
- crowd/reaction;
- UI.

It does not poll render animation names as truth.

## Lift identities

### Squat

Rack/plate rattle, footsteps, breath/brace, controlled clothing/foot contact, bar oscillation cue, heavy ascent strain,
rack impact, balance/failure event.

### Bench

Bench/setup cloth contact, grip/bar knurl cue, unrack hook contact, controlled descent, chest touch (subtle), pause tension,
press strain, rack, safe failure/spotter.

### Deadlift

Grip/knurl, brace, subtle pre-tension/rattle, floor break/plate release, bar-leg/clothing contact, heavy pull strain,
lockout hold, Down, controlled plate impact or failure drop.

## Parameterization

- Load selects layered plate/rack/body impact intensity within bounds.
- Relative collision speed/impulse proxy selects contact sample, but is not exposed as measured force.
- Modeled demand and attempt fatigue drive breathing/strain.
- Bar velocity/phase drives subtle continuous effort layers.
- Random variation is seeded per attempt and cosmetic.

## Spatial audio

Equipment/contact sounds originate at bar/rack/platform/bench. Referee command is spatially placed but mixed for clarity.
Crowd is ambient. Athlete breath/strain follows the visible root/head. UI remains nonspatial.

## Music

Low-key menu/career music; restrained pre-attempt tension; music ducks for commands and active lift; result sting after
judgment. Music must not conceal command timing or contact cues.

## Failure safety

Do not glorify injury. Use controlled equipment/body sounds and immediate result/safety cues. Avoid bone-breaking or
medical sound design.

## Replay

Replay re-triggers event-aligned audio at normal speed. Slow motion can pitch/filter presentation layers, but command and
result remain intelligible; analysis mode can mute crowd/music.

## Technical rules

- Pool AudioSources.
- No runtime clip loads during an active attempt.
- Use mixer snapshots for menu/attempt/replay/result.
- Event IDs and timestamps are recorded.
- Collision sound cooldown/aggregation prevents manifold spam.
- Command clips/subtitles derive from same event.
- Platform latency is tested in standalone.

## Tests

Every command has subtitle/visual; command event/audio synchronization; collision aggregation; load layering; no sound from
presentation-only phantom contacts; reset stops loops; pause/mixer; missing clip fallback; accessibility; performance/
voice count; replay alignment.

## Scope

**SHIP_V1:** complete three-lift, venue, UI, command, crowd, replay soundscape.  
**LATER:** commentary, richer crowds, athlete voices.  
**RESEARCH:** procedural contact/strain synthesis.  
**OUT_OF_SCOPE:** audio as rule authority or biological measurement.
