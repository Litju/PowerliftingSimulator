# GAM-45 start-window comparison

Authority: Linear `GAM-45`. Final probe artifacts were produced from fresh
Unity processes on `work/gam-13-squat-load-calibration` after the observability
and signed-motion diagnostic commits.

## Result

| Arm | 3-tick run | Longest run | Start window | Command | End | Classification |
|---|---:|---:|---|---|---|---|
| P0 default | yes | 6 | yes | tick 52 | physical lockout / complete | none |
| P1 5x + F1 + S1 | yes | 3 | yes | tick 33 | physical lockout / complete | none |
| P2 5x + F2 + S1 | no | 2 | no | none | `START_WINDOW` / `SETUP` at 2200 ticks | `START_BAR_MOTION` |
| P2 lower-chain isolation | yes | 3 | yes | tick 33 | physical lockout / complete | none |

P2 has no support, foot-availability, or posture failure. Across its 2200
diagnostic ticks, support availability/presence and all knee, hip, abdomen, and
thorax pass predicates remain valid. The only failing group is bar motion.

The four longest valid P2 runs break at these ticks; margins are threshold
minus observed magnitude:

| Break tick | Linear margin (m/s) | Vertical margin (m/s) | Angular margin (rad/s) |
|---:|---:|---:|---:|
| 35 | -0.008172771 | -0.0124963354 | -0.08002533 |
| 88 | 0.00770202 | -0.00186586194 | -0.06806667 |
| 126 | 0.00422073156 | -0.003043821 | -0.08539106 |
| 152 | 0.00517723151 | -0.00342467986 | -0.08290003 |

P2 posture margins remain positive over the run: knee minimum `0.0572537554`
rad, hip minimum `0.139587089` rad, and trunk minimum `0.117295742` rad.
The minimum 2-D capture margin is `0.08284136 m`.

## Closed-loop motion evidence

P2 shows a persistent motion cycle rather than a one-time settling miss:

* bar linear speed: `0.003232791..0.7001889 m/s`;
* signed vertical bar speed: `-0.699452043..0.077553615 m/s`, with 301 zero
  crossings;
* angular speed magnitude: `0.00443041231..1.36399651 rad/s`;
* signed bar angular zero crossings: X 153, Y 144, Z 277;
* raw ankle demand: `-3.67582965..2.908341 rad`, while the applied bounded
  target is `-0.2618..0.2618 rad`;
* hip strategy blend: `0..1`;
* COM horizontal speed: `0.000538821565..0.07106796 m/s`;
* COP AP zero crossings: 154; capture-ML zero crossings: 150;
* capture margin remains positive, `0.08284136..0.134317845 m`.

The cycle repeatedly breaks otherwise valid one- and two-tick candidate runs.
It is not a knee, hip, trunk, support, or observation-availability blocker.

## Causal isolation

`P2_NO_LOWER_CHAIN` keeps 5x impedance, F2 spine, S1, balance control, solver,
capacity, rules, and tolerances unchanged. It disables only F2 ankle/knee/hip
standing compensation. It restores a three-tick run and the same tick-33
command chronology as P1, with a complete physical-lockout lifecycle.

This establishes the lower-chain standing compensation as the cause of the
P2 start-window blocker. The isolation arm is diagnostic evidence, not a
production winner or tolerance change.

Evidence files:

* `Artifacts/Measurements/GAM-45/start-window-P0.csv` and `-summary.csv`;
* `Artifacts/Measurements/GAM-45/start-window-P1.csv` and `-summary.csv`;
* `Artifacts/Measurements/GAM-45/start-window-P2.csv` and `-summary.csv`;
* `Artifacts/Measurements/GAM-45/start-window-P2_NO_LOWER_CHAIN.csv` and
  `-summary.csv`;
* `Artifacts/Measurements/GAM-45/start-window-comparison.csv`;
* `Artifacts/Evidence/GAM-45/*-final2.xml`.

Focused lifecycle validation also passed in
`Artifacts/Evidence/GAM-45/GAM12-lifecycle-editmode.xml` and
`Artifacts/Evidence/GAM-45/GAM12-lifecycle-playmode.xml`; the PlayMode receipt
contains three deterministic 25 kg repeats with physical lockout,
`P2=EVALUABLE/NO_LIFT`, `P3=NO_PHYSICAL_FAILURE`, and trace-covered terminal
context.
