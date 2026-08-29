# Camera and Broadcast

**Document ID:** `PSMS-PRES-52`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `PRESENTATION/50_RENDER_ARCHITECTURE.md`, `GAME/40_MEET_SYSTEM.md`

## Repository verification

- Inspect existing Cinemachine brains/virtual cameras and M2 shot sequence.
- Capture framing evidence at all lift extrema and supported aspect ratios.
- Verify current Cinemachine package API/version.

## Purpose

Present each attempt like a readable powerlifting broadcast: show the human, bar, commands, legal positions, struggle,
failure, and judgment without hiding rule-critical information or disturbing physics.

## Camera authority

Cameras observe immutable presentation state. They never parent the physical athlete/bar, alter simulation time, provide
camera-relative truth, or drive control signs.

## Camera set

### Shared

- `WideVenue`: establishes platform/bench/rack, crowd, scoreboard.
- `HeadJudge`: frontal competition-style view.
- `SideJudge`: side view for bar path and squat depth/bench touch.
- `ThreeQuarterHero`: primary dramatic gameplay camera.
- `AnalysisOrbit`: post-attempt free/orbit camera.
- `ReplayClose`: event-driven close-up.

### Lift-specific defaults

**Squat:** three-quarter rear/side gameplay view that keeps feet, knee/hip depth, trunk, and bar visible; side replay for
depth/sticking.

**Bench:** three-quarter head/side gameplay view showing both arms, bar endpoints, chest touch, butt/feet as feasible;
side/low replay for elbow depth and bar path.

**Deadlift:** three-quarter front/side showing feet, grips, bar/legs, knees/hips, and lockout; side replay for bar proximity
and knee pass.

No single camera is used to compute a rule. Rule overlays project authoritative landmarks for explanation only.

## Shot state machine

```text
VenueEstablish
 -> AthleteSetup
 -> RefereeReady
 -> GameplayShot
 -> CriticalMomentHold
 -> LockoutOrFailure
 -> RefereeLights
 -> Reaction
 -> Replay/Analysis
```

Gameplay shot changes are limited during active motion to prevent control disruption. Most dramatic cuts occur before the
start command, after lockout/failure, or in replay.

## Camera motion

- Cinemachine body/aim follows a smoothed presentation target derived from interpolated root/bar state.
- Camera damping is presentation-only.
- FOV changes are slow and bounded.
- Camera shake comes from recorded rack/contact/failure events, not arbitrary noise.
- Avoid clipping into athlete/equipment using camera collision, but do not move game objects.
- Motion sickness options: reduced shake, fixed camera, wider FOV.

## Broadcast overlays

- athlete name/profile and attempt number;
- load and lift;
- attempt clock;
- next/received command;
- good/no-lift lights;
- total/placing;
- optional replay metrics;
- source/limitation details in analysis, not cluttered live.

## Referee/crowd presentation

V1 can use stylized/static/limited animated referees and crowd. The command source is the meet/rule domain. Light panel
uses the frozen result. Crowd reaction is seeded presentation based on result/load/records and never changes judgment.

## Replay camera

Recorded events generate edit points:

- command;
- deepest/touch/floor break;
- reversal/Press/knee pass;
- sticking peak;
- lockout/failure;
- judgment.

Slow motion defaults to the decisive interval and can display ghost/reference/path overlays.

## Cinematic restrictions

- no camera cut within ±0.15 s of a rule-critical event in gameplay unless verified readable;
- no occluding VFX at depth/touch/lockout;
- no depth-of-field blur that hides bar/body landmarks;
- no extreme lens distortion;
- no camera-dependent UI input sign.

## Tests

All lift phases remain framed; both bar endpoints for bench; feet/depth for squat; bar/knee/hip for deadlift; no camera
mutation of physics; collision avoidance; FOV/aspect ratios; ultrawide/16:9; command/light synchronization; fixed-camera
accessibility; replay seek/cuts; screenshot comparison.

## Performance

Cinemachine blending and camera collision stay inside presentation budget. At most the required camera renders per frame;
replay picture-in-picture is later unless budgeted.

## Scope

**SHIP_V1:** lift-specific gameplay cameras, broadcast flow, replay/analysis cameras, accessibility settings.  
**LATER:** commentators, more venue cameras, automated highlight edit.  
**RESEARCH:** learned camera direction.  
**OUT_OF_SCOPE:** cameras as simulation sensors or rule authority.
