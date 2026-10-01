# Physics Benchmark V1 summary

Bench root: `Artifacts/Benchmarks/Physics/3cb1184-dirty/sweeps/athlete-pos255`

| case | run | gated | pass | fail |
|---|---|---:|---:|---:|
| B12_standing_000kg | 20261001-183553-pos255 | 9 | 9 | 0 |
| B12_standing_025kg | 20261001-183553-pos255 | 21 | 20 | 1 |
| B12_standing_060kg | 20261001-183553-pos255 | 21 | 20 | 1 |
| B12_standing_140kg | 20261001-183553-pos255 | 21 | 20 | 1 |
| B12_standing_170kg | 20261001-183553-pos255 | 21 | 20 | 1 |
| B12_standing_300kg | 20261001-183553-pos255 | 21 | 20 | 1 |
| B13_oracle_static_torque | 20261001-183553-pos255 | 48 | 48 | 0 |

**Earliest failing layer: 7 (bar / saddle load path).**

| sev | layer | case | metric | config | expected | observed | tol |
|---|---:|---|---|---|---:|---:|---:|
| critical | 7 | B12_standing_025kg | bar_solver_iterations | load=25;saddle_replica_parity;dt=0.01;pos_iter=255;vel_iter=1 | 12 | 255 | 1e-06 Absolute |
| critical | 7 | B12_standing_060kg | bar_solver_iterations | load=60;saddle_replica_parity;dt=0.01;pos_iter=255;vel_iter=1 | 12 | 255 | 1e-06 Absolute |
| critical | 7 | B12_standing_140kg | bar_solver_iterations | load=140;saddle_replica_parity;dt=0.01;pos_iter=255;vel_iter=1 | 12 | 255 | 1e-06 Absolute |
| critical | 7 | B12_standing_170kg | bar_solver_iterations | load=170;saddle_replica_parity;dt=0.01;pos_iter=255;vel_iter=1 | 12 | 255 | 1e-06 Absolute |
| critical | 7 | B12_standing_300kg | bar_solver_iterations | load=300;saddle_replica_parity;dt=0.01;pos_iter=255;vel_iter=1 | 12 | 255 | 1e-06 Absolute |
