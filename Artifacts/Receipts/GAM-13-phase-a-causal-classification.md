# GAM-13 Phase A causal-classification receipt

Status: `SCREENING_COMPLETE / PHASE_C_NOT_AUTHORIZED`

Issue: Linear `GAM-13`

Baseline: `2aff0202d7574a17414bd4524b8e2ff8f3359884`

Run: [fresh-process manifest](../Measurements/GAM-13/phase-a-untouched/run-20260925-phase-a-untouched-r7/run-manifest.json)

## Observation

- **25 kg:** P2 `GOOD_LIFT`; P3 `NO_PHYSICAL_FAILURE`; shared surface proxy game-qualified; persistent bilateral contact; physical lockout. Mean ascent speed `0.1534 m/s`. Sticking detector found no resolvable region.
- **60 kg:** P2 `INCOMPLETE_ATTEMPT/UNDETERMINED`, no listed rule violations. P3 `PHYSICAL_FAILURE/POSTURE_OR_BAR_LOSS`, secondary balance loss and trunk hard limit; failure onset/latch at ticks `437/438`. The surface proxy qualified from tick `358`; the offline bar minimum is tick `583`; no ascent or physical lockout was established. Bilateral support is lost later at tick `544`.
- **140 kg:** no attempt record after 2200 ticks in `START_WINDOW/SETUP`; no squat command, P2, or P3 result. Worst-side setup depth remains `+0.012210 m`, above the `-0.005 m` game threshold. Contact callbacks persist, while accumulated foot-slip proxies exceed `1 m` and the minimum rear support margin is `-1.206 m`.
- **170 kg:** same no-attempt start-window timeout. Worst-side setup depth is `+0.007552 m`; bilateral contact is lost at tick `165`; minimum rear support margin is `-1.225 m`.
- **300 kg:** same no-attempt start-window timeout. The raw surface proxy becomes game-qualified at tick `151` after setup contact loss at tick `132`, while the state remains `SETUP`. P2/P3 did not evaluate a squat. This is not a classified legal bottom or physical lift failure.

All five processes used the same production baseline and passed the frozen-settings hash check. Each trace has the same 952 columns. The complete ordered table and per-load receipts are in [ADR-GAM13-phase-a-untouched-screening.md](../../ADR-GAM13-phase-a-untouched-screening.md).

## Inference

The first successful load response is 25 kg. The next load, 60 kg, fails physically before ascent. The heavier 140/170/300 kg probes do not reach the squat command and show loaded setup/posture/support instability. The broad observed failure domain is loaded standing and postural support; setup depth-realization error, posterior support margin, slip, and contact loss vary with load in those traces.

## Unresolved causes

The evidence does not isolate a single production parameter family among loaded-standing equilibrium/preload, dynamic balance, plantar support/slip, trunk-limit tracking, bar/athlete coupling, and start-window readiness. Raw modeled demand is not measured actuator saturation, and solver constraint torque is not direct drive torque. A parameter change based only on these correlations would be speculative.

## Stop decision

Do not tune production physics from this screen. GAM-13 remains open; GAM-14 is not authorized. The next experiment must distinguish the start/posture/support hypotheses before a bounded parameter domain can be selected. No GAM-47/48/49 blocker was reopened; the shared GAM-49 surface provider remains the depth authority.
