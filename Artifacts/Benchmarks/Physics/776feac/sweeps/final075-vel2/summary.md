# Physics Benchmark V1 summary

Bench root: `Artifacts/Benchmarks/Physics/776feac/sweeps/final075-vel2`

| case | run | gated | pass | fail |
|---|---|---:|---:|---:|
| B12_standing_000kg | 20261002-022138 | 9 | 9 | 0 |
| B12_standing_025kg | 20261002-022138 | 21 | 20 | 1 |
| B12_standing_060kg | 20261002-022138 | 21 | 20 | 1 |
| B12_standing_140kg | 20261002-022138 | 21 | 20 | 1 |
| B12_standing_170kg | 20261002-022138 | 9 | 8 | 1 |
| B12_standing_300kg | 20261002-022138 | 9 | 8 | 1 |
| B13_oracle_static_torque | 20261002-022138 | 25 | 25 | 0 |

**Earliest failing layer: 5 (constraint convergence).**

| sev | layer | case | metric | config | expected | observed | tol |
|---|---:|---|---|---|---:|---:|---:|
| critical | 5 | B12_standing_170kg | bar_solver_velocity_iterations | load=170;saddle_replica_parity;dt=0.01;pos_iter=255;vel_iter=2 | 6 | 2 | 1e-06 Absolute |
| critical | 5 | B12_standing_300kg | bar_solver_velocity_iterations | load=300;saddle_replica_parity;dt=0.01;pos_iter=255;vel_iter=2 | 6 | 2 | 1e-06 Absolute |
| critical | 7 | B12_standing_025kg | bar_solver_velocity_iterations | load=25;saddle_replica_parity;dt=0.01;pos_iter=255;vel_iter=2 | 6 | 2 | 1e-06 Absolute |
| critical | 7 | B12_standing_060kg | bar_solver_velocity_iterations | load=60;saddle_replica_parity;dt=0.01;pos_iter=255;vel_iter=2 | 6 | 2 | 1e-06 Absolute |
| critical | 7 | B12_standing_140kg | bar_solver_velocity_iterations | load=140;saddle_replica_parity;dt=0.01;pos_iter=255;vel_iter=2 | 6 | 2 | 1e-06 Absolute |
