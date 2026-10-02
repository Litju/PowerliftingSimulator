# Physics Benchmark V1 summary

Bench root: `Artifacts/Benchmarks/Physics/776feac/sweeps/final075-pos128`

| case | run | gated | pass | fail |
|---|---|---:|---:|---:|
| B13_oracle_static_torque | 20261002-021550 | 155 | 153 | 2 |
| B13_static_matrix_000kg | 20261002-021550 | 66 | 66 | 0 |
| B13_static_matrix_025kg | 20261002-021550 | 84 | 84 | 0 |
| B13_static_matrix_060kg | 20261002-021550 | 84 | 84 | 0 |
| B13_static_matrix_140kg | 20261002-021550 | 84 | 84 | 0 |
| B13_static_matrix_170kg | 20261002-021550 | 12 | 12 | 0 |
| B13_static_matrix_300kg | 20261002-021550 | 12 | 12 | 0 |

**Earliest failing layer: 5 (constraint convergence).**

| sev | layer | case | metric | config | expected | observed | tol |
|---|---:|---|---|---|---:|---:|---:|
| minor | 5 | B13_oracle_static_torque | foot_pair_sum_unity_vs_oracle_torque_nm | B13_140kg_s0.25_production;stage=settled;load=140;phase=0.25;production | -17.7035 | -6.99368 | 10 Absolute |
| minor | 5 | B13_oracle_static_torque | foot_pair_sum_unity_vs_oracle_torque_nm | B13_170kg_s0.25_production;stage=settled;load=170;phase=0.25;production | -34.2036 | -22.4701 | 10 Absolute |
