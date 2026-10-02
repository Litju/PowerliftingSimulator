# Physics Benchmark V1 summary

Bench root: `Artifacts/Benchmarks/Physics/776feac/sweeps/final075-pos56`

| case | run | gated | pass | fail |
|---|---|---:|---:|---:|
| B13_oracle_static_torque | 20261002-021213 | 155 | 129 | 26 |
| B13_static_matrix_000kg | 20261002-021213 | 66 | 66 | 0 |
| B13_static_matrix_025kg | 20261002-021213 | 84 | 84 | 0 |
| B13_static_matrix_060kg | 20261002-021213 | 84 | 84 | 0 |
| B13_static_matrix_140kg | 20261002-021213 | 84 | 84 | 0 |
| B13_static_matrix_170kg | 20261002-021213 | 12 | 12 | 0 |
| B13_static_matrix_300kg | 20261002-021213 | 12 | 12 | 0 |

**Earliest failing layer: 5 (constraint convergence).**

| sev | layer | case | metric | config | expected | observed | tol |
|---|---:|---|---|---|---:|---:|---:|
| critical | 5 | B13_oracle_static_torque | foot_pair_sum_unity_vs_oracle_torque_nm | B13_000kg_s0.00_production;stage=settled;load=0;phase=0.00;production | 56.4491 | 91.5003 | 10 Absolute |
| critical | 5 | B13_oracle_static_torque | foot_pair_sum_unity_vs_oracle_torque_nm | B13_000kg_s0.10_production;stage=settled;load=0;phase=0.10;production | 56.0309 | 92.3445 | 10 Absolute |
| critical | 5 | B13_oracle_static_torque | foot_pair_sum_unity_vs_oracle_torque_nm | B13_000kg_s0.25_production;stage=settled;load=0;phase=0.25;production | 54.3585 | 92.3466 | 10 Absolute |
| critical | 5 | B13_oracle_static_torque | foot_pair_sum_unity_vs_oracle_torque_nm | B13_000kg_s0.55_production;stage=settled;load=0;phase=0.55;production | 44.5917 | 80.0112 | 10 Absolute |
| minor | 5 | B13_oracle_static_torque | shank_pair_sum_unity_vs_oracle_torque_nm | B13_000kg_s0.55_production;stage=settled;load=0;phase=0.55;production | -166.644 | -184.276 | 16.6644 Absolute |
| critical | 5 | B13_oracle_static_torque | foot_pair_sum_unity_vs_oracle_torque_nm | B13_000kg_s0.75_production;stage=settled;load=0;phase=0.75;production | 41.7634 | 75.4131 | 10 Absolute |
| critical | 5 | B13_oracle_static_torque | foot_pair_sum_unity_vs_oracle_torque_nm | B13_000kg_s1.00_production;stage=settled;load=0;phase=1.00;production | 40.5896 | 72.2913 | 10 Absolute |
| minor | 5 | B13_oracle_static_torque | abdomen_unity_vs_oracle_torque_nm | B13_000kg_s1.00_production;stage=settled;load=0;phase=1.00;production | -82.9273 | -93.4058 | 10 Absolute |
| major | 5 | B13_oracle_static_torque | foot_pair_sum_unity_vs_oracle_torque_nm | B13_025kg_s0.00_production;stage=settled;load=25;phase=0.00;production | 42.7858 | 68.5696 | 10 Absolute |
| major | 5 | B13_oracle_static_torque | foot_pair_sum_unity_vs_oracle_torque_nm | B13_025kg_s0.10_production;stage=settled;load=25;phase=0.10;production | 43.1158 | 70.704 | 10 Absolute |
| major | 5 | B13_oracle_static_torque | foot_pair_sum_unity_vs_oracle_torque_nm | B13_025kg_s0.25_production;stage=settled;load=25;phase=0.25;production | 42.5055 | 71.801 | 10 Absolute |
| major | 5 | B13_oracle_static_torque | foot_pair_sum_unity_vs_oracle_torque_nm | B13_025kg_s0.55_production;stage=settled;load=25;phase=0.55;production | 33.9669 | 60.3189 | 10 Absolute |
| major | 5 | B13_oracle_static_torque | foot_pair_sum_unity_vs_oracle_torque_nm | B13_025kg_s0.75_production;stage=settled;load=25;phase=0.75;production | 31.5825 | 56.4492 | 10 Absolute |
| major | 5 | B13_oracle_static_torque | foot_pair_sum_unity_vs_oracle_torque_nm | B13_025kg_s1.00_production;stage=settled;load=25;phase=1.00;production | 31.498 | 55.1381 | 10 Absolute |
| major | 5 | B13_oracle_static_torque | foot_pair_sum_unity_vs_oracle_torque_nm | B13_060kg_s0.00_production;stage=settled;load=60;phase=0.00;production | 23.1193 | 41.8868 | 10 Absolute |
| minor | 5 | B13_oracle_static_torque | abdomen_unity_vs_oracle_torque_nm | B13_060kg_s0.00_production;stage=settled;load=60;phase=0.00;production | 88.8513 | 100.053 | 10 Absolute |
| major | 5 | B13_oracle_static_torque | foot_pair_sum_unity_vs_oracle_torque_nm | B13_060kg_s0.10_production;stage=settled;load=60;phase=0.10;production | 24.438 | 45.7979 | 10 Absolute |
| major | 5 | B13_oracle_static_torque | foot_pair_sum_unity_vs_oracle_torque_nm | B13_060kg_s0.25_production;stage=settled;load=60;phase=0.25;production | 25.1709 | 49.221 | 10 Absolute |
| major | 5 | B13_oracle_static_torque | foot_pair_sum_unity_vs_oracle_torque_nm | B13_060kg_s0.55_production;stage=settled;load=60;phase=0.55;production | 17.7836 | 39.3154 | 10 Absolute |
| major | 5 | B13_oracle_static_torque | foot_pair_sum_unity_vs_oracle_torque_nm | B13_060kg_s0.75_production;stage=settled;load=60;phase=0.75;production | 15.8043 | 35.9788 | 10 Absolute |
| major | 5 | B13_oracle_static_torque | foot_pair_sum_unity_vs_oracle_torque_nm | B13_060kg_s1.00_production;stage=settled;load=60;phase=1.00;production | 17.1108 | 36.6871 | 10 Absolute |
| minor | 5 | B13_oracle_static_torque | abdomen_unity_vs_oracle_torque_nm | B13_140kg_s0.10_production;stage=settled;load=140;phase=0.10;production | 131.282 | 145.868 | 13.1282 Absolute |
| minor | 5 | B13_oracle_static_torque | foot_pair_sum_unity_vs_oracle_torque_nm | B13_140kg_s0.25_production;stage=settled;load=140;phase=0.25;production | -17.9495 | -7.64918 | 10 Absolute |
| minor | 5 | B13_oracle_static_torque | abdomen_unity_vs_oracle_torque_nm | B13_170kg_s0.10_production;stage=settled;load=170;phase=0.10;production | 161.092 | 178.305 | 16.1092 Absolute |
| minor | 5 | B13_oracle_static_torque | foot_pair_sum_unity_vs_oracle_torque_nm | B13_300kg_s0.25_production;stage=settled;load=300;phase=0.25;production | -116.252 | -128.774 | 11.6252 Absolute |
| minor | 5 | B13_oracle_static_torque | abdomen_unity_vs_oracle_torque_nm | B13_300kg_s0.25_production;stage=settled;load=300;phase=0.25;production | 98.3937 | 109.876 | 10 Absolute |
