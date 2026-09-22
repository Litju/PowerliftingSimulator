# GAM-48 Gate 2 — attempt/start/P2/P3 authority receipt

Authority: Linear `GAM-48`
Production commit under test: Gate 1 `735e034`

## Contract

- The qualifying contiguous candidate run is copied into the frozen attempt
  trace before the Squat command is issued.
- `StartWindowBeginTick..StartWindowEndTick` is that same candidate run, and
  P2 evaluates exactly those samples immediately before the command tick.
- P3 receives `AttemptStartTick = SquatCommandTick` plus an explicit standing
  reference tick. Samples before the command cannot establish physical descent,
  bottom, ascent, or lockout.
- P2 rule judgment and P3 physical completeness remain independent outputs.

## Validation

- `GAM12SquatAttemptLifecycleTests`: 18/18 PASS.
- `P3_EXPLICIT_ATTEMPT_CONTEXT_EXCLUDES_PRE_COMMAND_SETTLING`: PASS; the
  unbounded trace contains pre-command downward motion, while bounded P3
  reports no physical descent or bottom.
- `GAM48_GATE2_QUALIFIER_WINDOW_IS_THE_P2_WINDOW`: PASS in a fresh Unity
  PlayMode process; it asserts trace start = qualified-window start, window end
  = command tick − 1, and explicit P3 boundary/reference ownership.
- Existing lifecycle and frozen-trace contracts remain green.

## Claim ceiling

This receipt establishes simulation lifecycle and observation authority only. It
does not establish legal depth, balance causality, control sufficiency, or any
biological force claim.
