# GAM-13 causal audit — qualification gates

Unity 6000.3.22f1 (`1c726e1fb402`), main PC, batch mode. Raw XML/logs are
machine-local under `D:\Dev\UnityLogs\GAM13\causal\`.

## Instrumentation (`fa34178`)

| Gate | Result |
|---|---|
| Compile | PASS (Tundra build success, no CS errors) |
| `GAM13SupportGeometryTests` (EditMode) | 5/5 |
| `GAM13_CAUSAL_AUDIT_BASELINE_AND_TOPOLOGY` | PASS; Stage-A summaries equal `trim-refoundation-stage-a-baseline.csv` at all five loads |
| `GAM13_CAUSAL_AUDIT_VELOCITY_ISOLATION` | PASS |
| `GAM13_CAUSAL_AUDIT_POSITION_ISOLATION` | PASS |
| `GAM13_CAUSAL_AUDIT_INTERVENTION` I1, I2 | PASS |
| MasterSpec | PASS (68 files, hashes, dependencies) |

## Production candidate (`42d0ca7`, saddle limb filter V2)

| Gate | Pre-fix (`6cd52b9`) | Post-fix |
|---|---|---|
| Full EditMode | — | **222/222** |
| Default PlayMode, `-nographics` | 174 pass, 8 fail, 17 explicit skipped | 174 pass, 8 fail, 17 explicit skipped (same 8) |
| The 8 failures | graphics-device only (RenderTexture / visual evidence) | same |
| Those 8 fixtures with a GPU | **8/8** | **8/8** |
| Default PlayMode total | **182/182** | **182/182** |
| V2 vs I1 traces, 25/60/300 kg | — | bit-identical (all fields but labels) |
| 25 kg Stage-A standing | PASS (posture 3.998°) | PASS (posture 4.031°) |
| MasterSpec | PASS | PASS |

## Canonical GAM-12 25 kg lifecycle

`GAM12SquatAttemptLifecycleIntegrationTests`, three fresh runs each,
identical within each build:

```text
PRE : START_WINDOW=120-122 SQUAT=123 DESCENT=152 BOTTOM=499 ASCENT=518 LOCKOUT=861 TRACE_COUNT=746
      P2=EVALUABLE/NO_LIFT (FAILED_START_POSITION,SUPPORT_VIOLATION) P3=EVALUABLE/NO_PHYSICAL_FAILURE TERMINAL=PHYSICAL_LOCKOUT
POST: START_WINDOW=49-51  SQUAT=52  DESCENT=80  BOTTOM=427 ASCENT=447 LOCKOUT=791 TRACE_COUNT=747
      P2=EVALUABLE/NO_LIFT (FAILED_START_POSITION,SUPPORT_VIOLATION) P3=EVALUABLE/NO_PHYSICAL_FAILURE TERMINAL=PHYSICAL_LOCKOUT
```

Relative to the squat command the attempt is unchanged within one tick
(descent +29/+28, bottom +376/+375, ascent +395/+395, lockout +738/+739) and
every P2/P3/terminal outcome is identical. Only start qualification is
reached 71 ticks earlier. No light-load regression.

## Regenerated historical evidence

The default suite rewrites 201–298 tracked GAM-11 evidence files on every
run. 120 of them differ numerically between the pre- and post-fix plants
(expected: the squat scene's contact topology changed); all of their live
assertions pass. They were restored, not re-committed.
