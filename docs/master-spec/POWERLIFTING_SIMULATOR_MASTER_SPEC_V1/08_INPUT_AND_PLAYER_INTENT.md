# Input and Player Intent

**Document ID:** `PSMS-08`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `01_PRODUCT_CONSTITUTION.md`, `03_COORDINATES_UNITS_NUMERICS.md`

## Repository verification

- Inspect current Input Actions, control schemes, and package update mode.
- Verify keyboard and gamepad bindings against existing UI prompts.
- Measure input-to-fixed-tick latency in a standalone build.

## PURPOSE


Translate human input into buffered, fixed-step intent. The player commands bracing, yielding, driving, balance, grip,
and competition actions—not bone rotations, raw forces, or bar velocity.


    ## INPUTS


Unity Input System events/polls; keyboard/gamepad controls; UI focus; current lift state; real-time timestamps.


    ## OUTPUTS


Immutable `PlayerIntentFrame` consumed once per physics tick; edge events; held analog values; tutorial prompts; rejected
input reasons.


    ## STATE


A render/input-rate buffer stores timestamped action edges and latest continuous values. Each simulation tick resamples
one intent frame. Edge events are consumed once; held values persist. Pause/menu input is separated from attempt input.


    ## UNITS


Analog intent normalized `[-1,1]` or `[0,1]`; timestamps s; buffer age s. No input unit is force, torque, angle, or velocity.


    ## COORDINATE CONVENTION


Balance input positive means athlete-right (`+X`) for frontal correction and athlete-forward (`+Z`) when a lift exposes
AP correction. UI remaps based on camera only visually; simulation intent remains athlete-local.


    ## EQUATIONS


Continuous smoothing where used:

\[
u_k=u_{k-1}+\operatorname{clamp}(u_{\mathrm{raw}}-u_{k-1},
-r_{\downarrow}h,r_{\uparrow}h).
\]

A bounded brace/drive command may feed activation as a first-order response in the capacity model. No equation maps
input directly to a physical force.


    ## ASSUMPTIONS


- A compact action vocabulary is more playable than direct per-joint control.
- Timing and sustained quality create skill.
- Keyboard and gamepad can express the full V1 game.


    ## APPROXIMATIONS


A single balance axis may be context-sensitive per lift. Fine-grained left/right correction is optional and must not turn
the game into a keyboard anatomy exercise. Haptic output is presentation feedback, not physical truth.


    ## GAME CALIBRATIONS


Canonical actions:

| Action | Keyboard | Gamepad | Meaning |
|---|---|---|---|
| Brace | Space | LT | increase trunk/full-body preparatory intent |
| Yield | S/Down | left stick down | permit/command controlled descent |
| Drive | W/Up | RT | express ascent/pull/press intent |
| Balance | A/D | left stick X | bounded technique correction |
| Grip/Slack | Shift | RB | lift-specific hand/pre-tension intent |
| Confirm/Rack | Enter | A | state-authorized command |
| Abort/Pause | Escape | Menu | leave/pause safely |

Lift-specific files define when each action is legal and what reference/capacity parameter it affects.


    ## NUMERICAL IMPLEMENTATION


Use Input System dynamic update for device acquisition, then timestamp and resample into the fixed simulation. Never
read edge-triggered actions independently from multiple FixedUpdate callbacks. Maintain a small ring buffer larger than
the maximum catch-up window. In deterministic tests, inject intent frames directly without hardware.


    ## PSEUDOCODE

    ```text
    DynamicInputUpdate(now):
    for action_event in input_system.events:
        intent_buffer.PushEdge(action_event.name, action_event.phase, now)
    intent_buffer.SetContinuous("brace", read_brace())
    intent_buffer.SetContinuous("yield", read_yield())
    intent_buffer.SetContinuous("drive", read_drive())
    intent_buffer.SetContinuous("balance", read_balance())
    intent_buffer.SetContinuous("grip", read_grip())

SampleForTick(tick_start, tick_end):
    edges = intent_buffer.ConsumeEdges(tick_start, tick_end)
    continuous = intent_buffer.LatestContinuousAt(tick_end)
    return PlayerIntentFrame(edges, rate_limit(continuous), tick_end)
    ```

    ## UNITY MAPPING


One `InputActionAsset` with `Gameplay`, `UI`, and optional `Debug` maps. `PlayerInput` or generated C# wrappers may
acquire device state, but a project-owned `IntentBuffer` is the only interface the lift controllers consume.
Input update mode and action-map switching are explicit build configuration.


    ## FAILURE MODES


Lost edges at low frame rate; double-consumed command; render-frame-dependent control; input read in several physics
components; camera-relative sign inversion; UI steals attempt input; stale held input after reset; direct AddForce or
bone target from input; hidden autoplay.


    ## OBSERVABILITY


Overlay shows raw action, timestamp, buffered edge count, sampled fixed-frame intent, legality gate, and resulting
lift-controller parameter. Replay records sampled intent frames optionally for analysis but replays state, not input.


    ## TELEMETRY


Command timing, brace/yield/drive traces, balance corrections, rejected early commands, device scheme, and input-buffer
latency. Do not record personally identifying device data.


    ## TESTS


- Edge occurs exactly once across multiple physics catch-up ticks.
- Held values persist and rate-limit identically at different render frame rates.
- UI map disables gameplay actions.
- Reset clears stale values.
- Injected intent produces deterministic phase transitions.
- No lift controller references Input System directly.
- No input path calls transform/force/bar velocity APIs.


    ## MUTATION TESTS


Read `Keyboard.current` in every joint; use `Update` delta time in control; direct `AddTorque`; skip buffering; leave
gameplay map active in menu; consume command twice; reverse balance sign; retain Drive after reset.


    ## PERFORMANCE CONSIDERATIONS


Tiny ring buffers and value structs; zero allocation per tick; generated action wrappers; avoid event fan-out in the fixed
loop.


    ## CLAIM CLASSIFICATION


Input semantics: `GAME_DESIGN`. Timing traces: `SOURCE_DIRECT` runtime observation. Physical interpretation:
`GAMEPLAY_APPROXIMATION`. No claim that key presses reproduce neural control.


    ## SHIP_V1 / LATER / RESEARCH / OUT_OF_SCOPE


**SHIP_V1:** buffered intent, keyboard/gamepad, lift-specific semantics, tutorial prompts.  
**LATER:** remapping, accessibility assists, haptics.  
**RESEARCH:** learned control assistance.  
**OUT_OF_SCOPE:** direct joint/bone/force controls as the primary game.
