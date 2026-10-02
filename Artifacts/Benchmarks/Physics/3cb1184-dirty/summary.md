# Physics Benchmark V1 summary

Bench root: `Artifacts/Benchmarks/Physics/3cb1184-dirty`

| case | run | gated | pass | fail |
|---|---|---:|---:|---:|
| B01_free_fall | 20261001-182720-corrected | 11 | 11 | 0 |
| B02_constant_torque | 20261001-182720-corrected | 10 | 10 | 0 |
| B03_pendulum | 20261001-182720-corrected | 7 | 7 | 0 |
| B04_pd_static_known_load | 20261001-182720-corrected | 24 | 23 | 1 |
| B05_pd_step_response | 20261001-182720-corrected | 12 | 11 | 1 |
| B06_maximum_force_saturation | 20261001-182720-corrected | 8 | 5 | 3 |
| B07_target_sign_frame | 20261001-182720-corrected | 12 | 11 | 1 |
| B08_friction | 20261001-182720-corrected | 6 | 3 | 3 |
| B09_drop_contact | 20261001-182720-corrected | 8 | 8 | 0 |
| B10_saddle_load_path | 20261001-182720-corrected | 30 | 24 | 6 |
| B11_driven_chain | 20261001-182720-corrected | 53 | 14 | 39 |
| B12_standing_000kg | 20261001-183105-athlete-baseline | 9 | 9 | 0 |
| B12_standing_025kg | 20261001-183105-athlete-baseline | 21 | 21 | 0 |
| B12_standing_060kg | 20261001-183105-athlete-baseline | 21 | 21 | 0 |
| B12_standing_140kg | 20261001-183105-athlete-baseline | 21 | 21 | 0 |
| B12_standing_170kg | 20261001-183105-athlete-baseline | 21 | 19 | 2 |
| B12_standing_300kg | 20261001-183105-athlete-baseline | 21 | 15 | 6 |
| B13_oracle_static_torque | 20261001-183105-athlete-baseline | 120 | 36 | 84 |
| B13_static_matrix_000kg | 20261001-183105-athlete-baseline | 66 | 63 | 3 |
| B13_static_matrix_025kg | 20261001-183105-athlete-baseline | 84 | 80 | 4 |
| B13_static_matrix_060kg | 20261001-183105-athlete-baseline | 84 | 79 | 5 |
| B13_static_matrix_140kg | 20261001-183105-athlete-baseline | 84 | 63 | 21 |
| B13_static_matrix_170kg | 20261001-183105-athlete-baseline | 84 | 54 | 30 |
| B13_static_matrix_300kg | 20261001-183105-athlete-baseline | 84 | 32 | 52 |

**Earliest failing layer: 1 (units / coordinate frames / equations).**

| sev | layer | case | metric | config | expected | observed | tol |
|---|---:|---|---|---|---:|---:|---:|
| critical | 1 | B13_oracle_static_torque | abdomen_unity_vs_oracle_torque_nm | B12_standing_000kg;stage=settled;load=0;phase=0.00;production | 9.41331 | 41.7303 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | left_foot_unity_vs_oracle_torque_nm | B12_standing_000kg;stage=settled;load=0;phase=0.00;production | 33.189 | 168.665 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | right_foot_unity_vs_oracle_torque_nm | B12_standing_000kg;stage=settled;load=0;phase=0.00;production | 33.2235 | 168.983 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | abdomen_unity_vs_oracle_torque_nm | B12_standing_025kg;stage=settled;load=25;phase=0.00;production | 41.4841 | 113.558 | 10 Absolute |
| minor | 1 | B13_oracle_static_torque | right_thigh_unity_vs_oracle_torque_nm | B12_standing_025kg;stage=settled;load=25;phase=0.00;production | -9.20433 | -19.5949 | 10 Absolute |
| minor | 1 | B13_oracle_static_torque | left_shank_unity_vs_oracle_torque_nm | B12_standing_025kg;stage=settled;load=25;phase=0.00;production | -4.2174 | -16.175 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | left_foot_unity_vs_oracle_torque_nm | B12_standing_025kg;stage=settled;load=25;phase=0.00;production | 31.3502 | 137.149 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | right_foot_unity_vs_oracle_torque_nm | B12_standing_025kg;stage=settled;load=25;phase=0.00;production | 31.3809 | 137.743 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | abdomen_unity_vs_oracle_torque_nm | B12_standing_060kg;stage=settled;load=60;phase=0.00;production | 85.6057 | 224.804 | 10 Absolute |
| major | 1 | B13_oracle_static_torque | right_thigh_unity_vs_oracle_torque_nm | B12_standing_060kg;stage=settled;load=60;phase=0.00;production | -25.4923 | -49.3908 | 10 Absolute |
| major | 1 | B13_oracle_static_torque | left_shank_unity_vs_oracle_torque_nm | B12_standing_060kg;stage=settled;load=60;phase=0.00;production | -20.4176 | -40.8165 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | left_foot_unity_vs_oracle_torque_nm | B12_standing_060kg;stage=settled;load=60;phase=0.00;production | 23.7705 | 104.086 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | right_foot_unity_vs_oracle_torque_nm | B12_standing_060kg;stage=settled;load=60;phase=0.00;production | 23.7969 | 105.483 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | abdomen_unity_vs_oracle_torque_nm | B12_standing_300kg;stage=settled;load=300;phase=0.00;production | 1197.56 | -234.178 | 119.756 Absolute |
| critical | 1 | B13_oracle_static_torque | thorax_unity_vs_oracle_torque_nm | B12_standing_300kg;stage=settled;load=300;phase=0.00;production | 864.512 | -56.4562 | 86.4512 Absolute |
| critical | 1 | B13_oracle_static_torque | left_thigh_unity_vs_oracle_torque_nm | B12_standing_300kg;stage=settled;load=300;phase=0.00;production | -755.868 | 75.6458 | 75.5868 Absolute |
| critical | 1 | B13_oracle_static_torque | right_thigh_unity_vs_oracle_torque_nm | B12_standing_300kg;stage=settled;load=300;phase=0.00;production | -759.446 | 96.1796 | 75.9446 Absolute |
| critical | 1 | B13_oracle_static_torque | left_shank_unity_vs_oracle_torque_nm | B12_standing_300kg;stage=settled;load=300;phase=0.00;production | -1549.28 | 81.6201 | 154.928 Absolute |
| critical | 1 | B13_oracle_static_torque | right_shank_unity_vs_oracle_torque_nm | B12_standing_300kg;stage=settled;load=300;phase=0.00;production | -1553.54 | 49.7481 | 155.354 Absolute |
| critical | 1 | B13_oracle_static_torque | left_foot_unity_vs_oracle_torque_nm | B12_standing_300kg;stage=settled;load=300;phase=0.00;production | -2405.1 | -445.785 | 240.51 Absolute |
| critical | 1 | B13_oracle_static_torque | right_foot_unity_vs_oracle_torque_nm | B12_standing_300kg;stage=settled;load=300;phase=0.00;production | -2416.59 | -408.157 | 241.659 Absolute |
| critical | 1 | B13_oracle_static_torque | abdomen_unity_vs_oracle_torque_nm | B13_000kg_s0.00_production;stage=settled;load=0;phase=0.00;production | 9.41034 | 41.6667 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | left_foot_unity_vs_oracle_torque_nm | B13_000kg_s0.00_production;stage=settled;load=0;phase=0.00;production | 33.2174 | 169.131 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | right_foot_unity_vs_oracle_torque_nm | B13_000kg_s0.00_production;stage=settled;load=0;phase=0.00;production | 33.251 | 169.549 | 10 Absolute |
| minor | 1 | B13_oracle_static_torque | left_shank_unity_vs_oracle_torque_nm | B13_000kg_s0.10_production;stage=settled;load=0;phase=0.10;production | -10.0335 | -23.9822 | 10 Absolute |
| major | 1 | B13_oracle_static_torque | right_shank_unity_vs_oracle_torque_nm | B13_000kg_s0.10_production;stage=settled;load=0;phase=0.10;production | -10.2489 | -25.9562 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | left_foot_unity_vs_oracle_torque_nm | B13_000kg_s0.10_production;stage=settled;load=0;phase=0.10;production | 32.5675 | 163.794 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | right_foot_unity_vs_oracle_torque_nm | B13_000kg_s0.10_production;stage=settled;load=0;phase=0.10;production | 32.5941 | 163.502 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | abdomen_unity_vs_oracle_torque_nm | B13_000kg_s0.25_production;stage=settled;load=0;phase=0.25;production | -30.946 | -69.2987 | 10 Absolute |
| minor | 1 | B13_oracle_static_torque | left_thigh_unity_vs_oracle_torque_nm | B13_000kg_s0.25_production;stage=settled;load=0;phase=0.25;production | 26.6166 | 37.4441 | 10 Absolute |
| major | 1 | B13_oracle_static_torque | right_thigh_unity_vs_oracle_torque_nm | B13_000kg_s0.25_production;stage=settled;load=0;phase=0.25;production | 26.7163 | 43.2375 | 10 Absolute |
| major | 1 | B13_oracle_static_torque | left_shank_unity_vs_oracle_torque_nm | B13_000kg_s0.25_production;stage=settled;load=0;phase=0.25;production | -34.9552 | -64.7875 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | right_shank_unity_vs_oracle_torque_nm | B13_000kg_s0.25_production;stage=settled;load=0;phase=0.25;production | -35.2686 | -68.7232 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | left_foot_unity_vs_oracle_torque_nm | B13_000kg_s0.25_production;stage=settled;load=0;phase=0.25;production | 30.9554 | 154.616 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | right_foot_unity_vs_oracle_torque_nm | B13_000kg_s0.25_production;stage=settled;load=0;phase=0.25;production | 30.9637 | 153.518 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | abdomen_unity_vs_oracle_torque_nm | B13_000kg_s0.55_production;stage=settled;load=0;phase=0.55;production | -73.273 | -188.799 | 10 Absolute |
| major | 1 | B13_oracle_static_torque | left_thigh_unity_vs_oracle_torque_nm | B13_000kg_s0.55_production;stage=settled;load=0;phase=0.55;production | 53.5281 | 78.9845 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | right_thigh_unity_vs_oracle_torque_nm | B13_000kg_s0.55_production;stage=settled;load=0;phase=0.55;production | 53.6438 | 93.481 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | left_shank_unity_vs_oracle_torque_nm | B13_000kg_s0.55_production;stage=settled;load=0;phase=0.55;production | -85.4642 | -151.871 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | right_shank_unity_vs_oracle_torque_nm | B13_000kg_s0.55_production;stage=settled;load=0;phase=0.55;production | -85.7432 | -161.809 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | left_foot_unity_vs_oracle_torque_nm | B13_000kg_s0.55_production;stage=settled;load=0;phase=0.55;production | 24.2047 | 122.092 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | right_foot_unity_vs_oracle_torque_nm | B13_000kg_s0.55_production;stage=settled;load=0;phase=0.55;production | 24.2391 | 119.646 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | abdomen_unity_vs_oracle_torque_nm | B13_000kg_s0.75_production;stage=settled;load=0;phase=0.75;production | -80.1648 | -208.331 | 10 Absolute |
| major | 1 | B13_oracle_static_torque | left_thigh_unity_vs_oracle_torque_nm | B13_000kg_s0.75_production;stage=settled;load=0;phase=0.75;production | 57.9612 | 84.9305 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | right_thigh_unity_vs_oracle_torque_nm | B13_000kg_s0.75_production;stage=settled;load=0;phase=0.75;production | 58.1055 | 103.592 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | left_shank_unity_vs_oracle_torque_nm | B13_000kg_s0.75_production;stage=settled;load=0;phase=0.75;production | -97.4856 | -171.5 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | right_shank_unity_vs_oracle_torque_nm | B13_000kg_s0.75_production;stage=settled;load=0;phase=0.75;production | -97.6786 | -184.21 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | left_foot_unity_vs_oracle_torque_nm | B13_000kg_s0.75_production;stage=settled;load=0;phase=0.75;production | 22.6227 | 114.105 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | right_foot_unity_vs_oracle_torque_nm | B13_000kg_s0.75_production;stage=settled;load=0;phase=0.75;production | 22.6204 | 110.967 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | abdomen_unity_vs_oracle_torque_nm | B13_000kg_s1.00_production;stage=settled;load=0;phase=1.00;production | -83.7269 | -221.789 | 10 Absolute |
| major | 1 | B13_oracle_static_torque | left_thigh_unity_vs_oracle_torque_nm | B13_000kg_s1.00_production;stage=settled;load=0;phase=1.00;production | 60.2877 | 89.6961 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | right_thigh_unity_vs_oracle_torque_nm | B13_000kg_s1.00_production;stage=settled;load=0;phase=1.00;production | 60.3872 | 106.365 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | left_shank_unity_vs_oracle_torque_nm | B13_000kg_s1.00_production;stage=settled;load=0;phase=1.00;production | -106.81 | -189.843 | 10.681 Absolute |
| critical | 1 | B13_oracle_static_torque | right_shank_unity_vs_oracle_torque_nm | B13_000kg_s1.00_production;stage=settled;load=0;phase=1.00;production | -106.753 | -198.654 | 10.6753 Absolute |
| critical | 1 | B13_oracle_static_torque | left_foot_unity_vs_oracle_torque_nm | B13_000kg_s1.00_production;stage=settled;load=0;phase=1.00;production | 22.5673 | 114.612 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | right_foot_unity_vs_oracle_torque_nm | B13_000kg_s1.00_production;stage=settled;load=0;phase=1.00;production | 22.6152 | 109.892 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | abdomen_unity_vs_oracle_torque_nm | B13_025kg_s0.00_production;stage=settled;load=25;phase=0.00;production | 41.4817 | 113.164 | 10 Absolute |
| minor | 1 | B13_oracle_static_torque | right_thigh_unity_vs_oracle_torque_nm | B13_025kg_s0.00_production;stage=settled;load=25;phase=0.00;production | -9.2326 | -19.9047 | 10 Absolute |
| minor | 1 | B13_oracle_static_torque | left_shank_unity_vs_oracle_torque_nm | B13_025kg_s0.00_production;stage=settled;load=25;phase=0.00;production | -4.18472 | -15.8205 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | left_foot_unity_vs_oracle_torque_nm | B13_025kg_s0.00_production;stage=settled;load=25;phase=0.00;production | 31.3998 | 138.602 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | right_foot_unity_vs_oracle_torque_nm | B13_025kg_s0.00_production;stage=settled;load=25;phase=0.00;production | 31.4552 | 139.816 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | abdomen_unity_vs_oracle_torque_nm | B13_025kg_s0.10_production;stage=settled;load=25;phase=0.10;production | 15.2236 | 54.4403 | 10 Absolute |
| major | 1 | B13_oracle_static_torque | left_shank_unity_vs_oracle_torque_nm | B13_025kg_s0.10_production;stage=settled;load=25;phase=0.10;production | -23.2001 | -39.2526 | 10 Absolute |
| minor | 1 | B13_oracle_static_torque | right_shank_unity_vs_oracle_torque_nm | B13_025kg_s0.10_production;stage=settled;load=25;phase=0.10;production | -23.3865 | -36.9988 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | left_foot_unity_vs_oracle_torque_nm | B13_025kg_s0.10_production;stage=settled;load=25;phase=0.10;production | 31.1029 | 134.59 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | right_foot_unity_vs_oracle_torque_nm | B13_025kg_s0.10_production;stage=settled;load=25;phase=0.10;production | 31.1563 | 135.275 | 10 Absolute |
| minor | 1 | B13_oracle_static_torque | abdomen_unity_vs_oracle_torque_nm | B13_025kg_s0.25_production;stage=settled;load=25;phase=0.25;production | -25.3114 | -37.6942 | 10 Absolute |
| minor | 1 | B13_oracle_static_torque | left_thigh_unity_vs_oracle_torque_nm | B13_025kg_s0.25_production;stage=settled;load=25;phase=0.25;production | 29.3352 | 39.5581 | 10 Absolute |
| minor | 1 | B13_oracle_static_torque | right_thigh_unity_vs_oracle_torque_nm | B13_025kg_s0.25_production;stage=settled;load=25;phase=0.25;production | 29.4467 | 42.4284 | 10 Absolute |
| major | 1 | B13_oracle_static_torque | left_shank_unity_vs_oracle_torque_nm | B13_025kg_s0.25_production;stage=settled;load=25;phase=0.25;production | -52.9838 | -78.5089 | 10 Absolute |
| major | 1 | B13_oracle_static_torque | right_shank_unity_vs_oracle_torque_nm | B13_025kg_s0.25_production;stage=settled;load=25;phase=0.25;production | -53.476 | -80.4604 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | left_foot_unity_vs_oracle_torque_nm | B13_025kg_s0.25_production;stage=settled;load=25;phase=0.25;production | 29.9053 | 126.481 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | right_foot_unity_vs_oracle_torque_nm | B13_025kg_s0.25_production;stage=settled;load=25;phase=0.25;production | 29.9459 | 125.941 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | abdomen_unity_vs_oracle_torque_nm | B13_060kg_s0.00_production;stage=settled;load=60;phase=0.00;production | 85.6638 | 225.922 | 10 Absolute |
| major | 1 | B13_oracle_static_torque | right_thigh_unity_vs_oracle_torque_nm | B13_060kg_s0.00_production;stage=settled;load=60;phase=0.00;production | -25.5075 | -49.5235 | 10 Absolute |
| major | 1 | B13_oracle_static_torque | left_shank_unity_vs_oracle_torque_nm | B13_060kg_s0.00_production;stage=settled;load=60;phase=0.00;production | -20.6541 | -41.7648 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | left_foot_unity_vs_oracle_torque_nm | B13_060kg_s0.00_production;stage=settled;load=60;phase=0.00;production | 23.3834 | 98.2003 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | right_foot_unity_vs_oracle_torque_nm | B13_060kg_s0.00_production;stage=settled;load=60;phase=0.00;production | 23.3882 | 99.1375 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | abdomen_unity_vs_oracle_torque_nm | B13_060kg_s0.10_production;stage=settled;load=60;phase=0.10;production | 43.5203 | 130.828 | 10 Absolute |
| minor | 1 | B13_oracle_static_torque | right_thigh_unity_vs_oracle_torque_nm | B13_060kg_s0.10_production;stage=settled;load=60;phase=0.10;production | -1.59774 | -12.2006 | 10 Absolute |
| major | 1 | B13_oracle_static_torque | left_shank_unity_vs_oracle_torque_nm | B13_060kg_s0.10_production;stage=settled;load=60;phase=0.10;production | -43.9097 | -69.3693 | 10 Absolute |
| minor | 1 | B13_oracle_static_torque | right_shank_unity_vs_oracle_torque_nm | B13_060kg_s0.10_production;stage=settled;load=60;phase=0.10;production | -43.7859 | -58.103 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | left_foot_unity_vs_oracle_torque_nm | B13_060kg_s0.10_production;stage=settled;load=60;phase=0.10;production | 23.7345 | 96.7889 | 10 Absolute |
| critical | 1 | B13_oracle_static_torque | right_foot_unity_vs_oracle_torque_nm | B13_060kg_s0.10_production;stage=settled;load=60;phase=0.10;production | 23.7612 | 96.3419 | 10 Absolute |
| critical | 3 | B07_target_sign_frame | hinge_relative_velocity_rad_s | production targetAngularVelocity=-w;dt=0.01;pos_iter=28;vel_iter=1 | 1 | -1 | 0.01 Absolute |
| minor | 3 | B13_static_matrix_300kg | tail_max_limit_proximity | load=300;phase=0.00;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 1 | 0.95 UpperBound |
| minor | 3 | B13_static_matrix_300kg | tail_max_limit_proximity | load=300;phase=0.10;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.971824 | 0.95 UpperBound |
| minor | 3 | B13_static_matrix_300kg | tail_max_limit_proximity | load=300;phase=0.55;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 1 | 0.95 UpperBound |
| minor | 3 | B13_static_matrix_300kg | tail_max_limit_proximity | load=300;phase=1.00;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.987191 | 0.95 UpperBound |
| minor | 4 | B04_pd_static_known_load | static_error_rad | trunk;demand=0.10;K=7850;m=13.25;dt=0.01;pos_iter=28;vel_iter=1 | 0.00496809 | 0.00532169 | 0.05 Relative |
| minor | 4 | B05_pd_step_response | overshoot_fraction | hip_family_inertia_5;K=3150;D=168.0;I=5.000;wn_dt=0.251;zeta=0.669;dt=0.01;pos_iter=28;vel_iter=1 | 0.0590002 | 0.0358187 | 0.02 Absolute |
| critical | 4 | B06_maximum_force_saturation | swing_droop_vs_production_demand_model_rad | swing45deg;tau_g/maxForce=1.25;dt=0.01;pos_iter=28;vel_iter=1 | 0.0276103 | 0.00215619 | 0.05 Relative |
| critical | 4 | B06_maximum_force_saturation | swing_droop_vs_production_demand_model_rad | swing45deg;tau_g/maxForce=1.60;dt=0.01;pos_iter=28;vel_iter=1 | 0.0493415 | 0.016472 | 0.05 Relative |
| critical | 4 | B06_maximum_force_saturation | swing_droop_vs_production_demand_model_rad | swing45deg;tau_g/maxForce=2.00;dt=0.01;pos_iter=28;vel_iter=1 | 0.0648638 | 0.0385627 | 0.05 Relative |
| critical | 5 | B11_driven_chain | ankle_static_error_rad | load=0;production;dt=0.01;pos_iter=28;vel_iter=1 | 0.000582668 | -0.00978724 | 0.002 Absolute |
| critical | 5 | B11_driven_chain | knee_static_error_rad | load=0;production;dt=0.01;pos_iter=28;vel_iter=1 | 0.0355733 | 0.0581987 | 0.002 Absolute |
| major | 5 | B11_driven_chain | hip_static_error_rad | load=0;production;dt=0.01;pos_iter=28;vel_iter=1 | -0.0155364 | -0.0190951 | 0.002 Absolute |
| critical | 5 | B11_driven_chain | settled_max_angular_speed_rad_s | load=0;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0508491 | 0.01 UpperBound |
| critical | 5 | B11_driven_chain | ankle_static_error_rad | load=25;production;dt=0.01;pos_iter=28;vel_iter=1 | -0.00757804 | -0.0188976 | 0.002 Absolute |
| critical | 5 | B11_driven_chain | knee_static_error_rad | load=25;production;dt=0.01;pos_iter=28;vel_iter=1 | 0.0391887 | 0.063967 | 0.002 Absolute |
| major | 5 | B11_driven_chain | hip_static_error_rad | load=25;production;dt=0.01;pos_iter=28;vel_iter=1 | -0.0276061 | -0.0333024 | 0.002 Absolute |
| critical | 5 | B11_driven_chain | settled_max_angular_speed_rad_s | load=25;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0406617 | 0.01 UpperBound |
| critical | 5 | B11_driven_chain | ankle_static_error_rad | load=60;production;dt=0.01;pos_iter=28;vel_iter=1 | -0.0218091 | -0.0368065 | 0.002 Absolute |
| critical | 5 | B11_driven_chain | knee_static_error_rad | load=60;production;dt=0.01;pos_iter=28;vel_iter=1 | 0.0427041 | 0.0734443 | 0.0021352 Absolute |
| critical | 5 | B11_driven_chain | hip_static_error_rad | load=60;production;dt=0.01;pos_iter=28;vel_iter=1 | -0.0456722 | -0.0531257 | 0.00228361 Absolute |
| critical | 5 | B11_driven_chain | settled_max_angular_speed_rad_s | load=60;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0504098 | 0.01 UpperBound |
| critical | 5 | B11_driven_chain | ankle_static_error_rad | load=140;production;dt=0.01;pos_iter=28;vel_iter=1 | -0.0771738 | -0.106931 | 0.00385869 Absolute |
| critical | 5 | B11_driven_chain | knee_static_error_rad | load=140;production;dt=0.01;pos_iter=28;vel_iter=1 | 0.0380072 | 0.0735083 | 0.002 Absolute |
| major | 5 | B11_driven_chain | hip_static_error_rad | load=140;production;dt=0.01;pos_iter=28;vel_iter=1 | -0.0956854 | -0.108481 | 0.00478427 Absolute |
| critical | 5 | B11_driven_chain | settled_max_angular_speed_rad_s | load=140;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0742552 | 0.01 UpperBound |
| critical | 5 | B11_driven_chain | ankle_static_error_rad | load=170;production;dt=0.01;pos_iter=28;vel_iter=1 | -0.114106 | -0.152792 | 0.00570528 Absolute |
| critical | 5 | B11_driven_chain | knee_static_error_rad | load=170;production;dt=0.01;pos_iter=28;vel_iter=1 | 0.0271511 | 0.0584208 | 0.002 Absolute |
| major | 5 | B11_driven_chain | hip_static_error_rad | load=170;production;dt=0.01;pos_iter=28;vel_iter=1 | -0.119991 | -0.135421 | 0.00599956 Absolute |
| critical | 5 | B11_driven_chain | settled_max_angular_speed_rad_s | load=170;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0973546 | 0.01 UpperBound |
| major | 5 | B11_driven_chain | max_anchor_separation_m | load=300;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.00500834 | 0.002 UpperBound |
| critical | 5 | B11_driven_chain | settled_max_angular_speed_rad_s | load=300;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.283717 | 0.01 UpperBound |
| critical | 5 | B12_standing_300kg | max_anchor_separation_m | load=300;standing_hold_10s;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0346822 | 0.01 UpperBound |
| minor | 5 | B13_static_matrix_140kg | max_anchor_separation_m | load=140;phase=0.55;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0114932 | 0.01 UpperBound |
| minor | 5 | B13_static_matrix_140kg | max_anchor_separation_m | load=140;phase=0.75;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0147077 | 0.01 UpperBound |
| major | 5 | B13_static_matrix_140kg | max_anchor_separation_m | load=140;phase=1.00;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0166425 | 0.01 UpperBound |
| minor | 5 | B13_static_matrix_170kg | max_anchor_separation_m | load=170;phase=0.10;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0133026 | 0.01 UpperBound |
| minor | 5 | B13_static_matrix_170kg | max_anchor_separation_m | load=170;phase=0.25;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0123275 | 0.01 UpperBound |
| minor | 5 | B13_static_matrix_170kg | max_anchor_separation_m | load=170;phase=0.55;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0128636 | 0.01 UpperBound |
| major | 5 | B13_static_matrix_170kg | max_anchor_separation_m | load=170;phase=0.75;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0165379 | 0.01 UpperBound |
| major | 5 | B13_static_matrix_170kg | max_anchor_separation_m | load=170;phase=1.00;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0166234 | 0.01 UpperBound |
| critical | 5 | B13_static_matrix_300kg | max_anchor_separation_m | load=300;phase=0.00;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0386048 | 0.01 UpperBound |
| major | 5 | B13_static_matrix_300kg | max_anchor_separation_m | load=300;phase=0.10;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.026495 | 0.01 UpperBound |
| major | 5 | B13_static_matrix_300kg | max_anchor_separation_m | load=300;phase=0.25;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0219172 | 0.01 UpperBound |
| major | 5 | B13_static_matrix_300kg | max_anchor_separation_m | load=300;phase=0.55;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0279941 | 0.01 UpperBound |
| major | 5 | B13_static_matrix_300kg | max_anchor_separation_m | load=300;phase=0.75;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.021253 | 0.01 UpperBound |
| major | 5 | B13_static_matrix_300kg | max_anchor_separation_m | load=300;phase=1.00;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0245318 | 0.01 UpperBound |
| critical | 6 | B08_friction | slide_velocity_after_1s_mps | tan/mu=1.25;m=2;dt=0.01;pos_iter=28;vel_iter=1 | 1.53206 | 0.000388489 | 0.05 Relative |
| critical | 6 | B08_friction | slide_velocity_after_1s_mps | tan/mu=1.25;m=400;dt=0.01;pos_iter=28;vel_iter=1 | 1.53206 | 0.000388497 | 0.05 Relative |
| critical | 6 | B08_friction | slide_velocity_after_1s_mps | F/(mu m g)=1.10;dt=0.01;pos_iter=28;vel_iter=1 | 0.981001 | -0.000548437 | 0.1 Relative |
| critical | 6 | B12_standing_300kg | foot_travel_m | load=300;standing_hold_10s;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 1.12314 | 0.005 UpperBound |
| major | 6 | B13_static_matrix_000kg | foot_travel_m | load=0;phase=0.55;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.00936331 | 0.005 UpperBound |
| major | 6 | B13_static_matrix_000kg | foot_travel_m | load=0;phase=0.75;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0115472 | 0.005 UpperBound |
| major | 6 | B13_static_matrix_000kg | foot_travel_m | load=0;phase=1.00;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0111154 | 0.005 UpperBound |
| minor | 6 | B13_static_matrix_025kg | foot_travel_m | load=25;phase=0.55;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.00723698 | 0.005 UpperBound |
| major | 6 | B13_static_matrix_025kg | foot_travel_m | load=25;phase=0.75;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.00949149 | 0.005 UpperBound |
| major | 6 | B13_static_matrix_025kg | foot_travel_m | load=25;phase=1.00;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.00835809 | 0.005 UpperBound |
| minor | 6 | B13_static_matrix_060kg | foot_travel_m | load=60;phase=0.75;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.00633712 | 0.005 UpperBound |
| major | 6 | B13_static_matrix_140kg | foot_travel_m | load=140;phase=0.55;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0103718 | 0.005 UpperBound |
| critical | 6 | B13_static_matrix_140kg | foot_travel_m | load=140;phase=0.75;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0176251 | 0.005 UpperBound |
| critical | 6 | B13_static_matrix_140kg | foot_travel_m | load=140;phase=1.00;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0211631 | 0.005 UpperBound |
| critical | 6 | B13_static_matrix_170kg | foot_travel_m | load=170;phase=0.10;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0254333 | 0.005 UpperBound |
| critical | 6 | B13_static_matrix_170kg | foot_travel_m | load=170;phase=0.25;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.023975 | 0.005 UpperBound |
| major | 6 | B13_static_matrix_170kg | foot_travel_m | load=170;phase=0.55;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0141537 | 0.005 UpperBound |
| critical | 6 | B13_static_matrix_170kg | foot_travel_m | load=170;phase=0.75;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0238285 | 0.005 UpperBound |
| major | 6 | B13_static_matrix_170kg | foot_travel_m | load=170;phase=1.00;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0118367 | 0.005 UpperBound |
| critical | 6 | B13_static_matrix_300kg | foot_travel_m | load=300;phase=0.00;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.889493 | 0.005 UpperBound |
| critical | 6 | B13_static_matrix_300kg | foot_travel_m | load=300;phase=0.10;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.66944 | 0.005 UpperBound |
| critical | 6 | B13_static_matrix_300kg | foot_travel_m | load=300;phase=0.25;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 1.06994 | 0.005 UpperBound |
| critical | 6 | B13_static_matrix_300kg | foot_travel_m | load=300;phase=0.55;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.22468 | 0.005 UpperBound |
| critical | 6 | B13_static_matrix_300kg | foot_travel_m | load=300;phase=0.75;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 1.81453 | 0.005 UpperBound |
| critical | 6 | B13_static_matrix_300kg | foot_travel_m | load=300;phase=1.00;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 1.90719 | 0.005 UpperBound |
| critical | 7 | B10_saddle_load_path | saddle_reported_force_n | load=25;dynamic_thorax=21.6;dt=0.01;pos_iter=28;vel_iter=1 | 245.25 | 0 | 0.02 Relative |
| critical | 7 | B10_saddle_load_path | saddle_reported_force_n | load=60;dynamic_thorax=21.6;dt=0.01;pos_iter=28;vel_iter=1 | 588.6 | 0 | 0.02 Relative |
| critical | 7 | B10_saddle_load_path | saddle_reported_force_n | load=140;dynamic_thorax=21.6;dt=0.01;pos_iter=28;vel_iter=1 | 1373.4 | 0 | 0.02 Relative |
| critical | 7 | B10_saddle_load_path | saddle_reported_force_n | load=170;dynamic_thorax=21.6;dt=0.01;pos_iter=28;vel_iter=1 | 1667.7 | 0 | 0.02 Relative |
| critical | 7 | B10_saddle_load_path | saddle_reported_force_n | load=300;dynamic_thorax=21.6;dt=0.01;pos_iter=28;vel_iter=1 | 2943 | 0 | 0.02 Relative |
| minor | 7 | B10_saddle_load_path | static_sag_dynamic_carrier_m | load=300;dynamic_thorax=21.6;dt=0.01;pos_iter=28;vel_iter=1 | 0.005886 | 0.00618422 | 0.05 Relative |
| critical | 8 | B11_driven_chain | ankle_vs_refined_reference_rad | load=0;production;dt=0.01;pos_iter=28;vel_iter=1 | 0.000280766 | -0.00978724 | 0.002 Absolute |
| critical | 8 | B11_driven_chain | knee_vs_refined_reference_rad | load=0;production;dt=0.01;pos_iter=28;vel_iter=1 | 0.0361176 | 0.0581987 | 0.002 Absolute |
| major | 8 | B11_driven_chain | hip_vs_refined_reference_rad | load=0;production;dt=0.01;pos_iter=28;vel_iter=1 | -0.0156422 | -0.0190951 | 0.002 Absolute |
| critical | 8 | B11_driven_chain | ankle_vs_refined_reference_rad | load=25;production;dt=0.01;pos_iter=28;vel_iter=1 | -0.00794498 | -0.0188976 | 0.002 Absolute |
| critical | 8 | B11_driven_chain | knee_vs_refined_reference_rad | load=25;production;dt=0.01;pos_iter=28;vel_iter=1 | 0.0399032 | 0.063967 | 0.002 Absolute |
| major | 8 | B11_driven_chain | hip_vs_refined_reference_rad | load=25;production;dt=0.01;pos_iter=28;vel_iter=1 | -0.0277558 | -0.0333024 | 0.002 Absolute |
| critical | 8 | B11_driven_chain | ankle_vs_refined_reference_rad | load=60;production;dt=0.01;pos_iter=28;vel_iter=1 | -0.0222542 | -0.0368065 | 0.002 Absolute |
| critical | 8 | B11_driven_chain | knee_vs_refined_reference_rad | load=60;production;dt=0.01;pos_iter=28;vel_iter=1 | 0.0436747 | 0.0734443 | 0.00218374 Absolute |
| critical | 8 | B11_driven_chain | hip_vs_refined_reference_rad | load=60;production;dt=0.01;pos_iter=28;vel_iter=1 | -0.0458571 | -0.0531257 | 0.00229285 Absolute |
| critical | 8 | B11_driven_chain | ankle_vs_refined_reference_rad | load=140;production;dt=0.01;pos_iter=28;vel_iter=1 | -0.0787398 | -0.106931 | 0.00393699 Absolute |
| critical | 8 | B11_driven_chain | knee_vs_refined_reference_rad | load=140;production;dt=0.01;pos_iter=28;vel_iter=1 | 0.0390045 | 0.0735083 | 0.002 Absolute |
| major | 8 | B11_driven_chain | hip_vs_refined_reference_rad | load=140;production;dt=0.01;pos_iter=28;vel_iter=1 | -0.0962343 | -0.108481 | 0.00481171 Absolute |
| critical | 8 | B11_driven_chain | ankle_vs_refined_reference_rad | load=170;production;dt=0.01;pos_iter=28;vel_iter=1 | -0.126975 | -0.152792 | 0.00634877 Absolute |
| critical | 8 | B11_driven_chain | knee_vs_refined_reference_rad | load=170;production;dt=0.01;pos_iter=28;vel_iter=1 | 0.0225962 | 0.0584208 | 0.002 Absolute |
| major | 8 | B11_driven_chain | hip_vs_refined_reference_rad | load=170;production;dt=0.01;pos_iter=28;vel_iter=1 | -0.12408 | -0.135421 | 0.00620401 Absolute |
| critical | 8 | B11_driven_chain | ankle_vs_refined_reference_rad | load=300;production;dt=0.01;pos_iter=28;vel_iter=1 | -0.416272 | -0.507044 | 0.0208136 Absolute |
| critical | 8 | B11_driven_chain | knee_vs_refined_reference_rad | load=300;production;dt=0.01;pos_iter=28;vel_iter=1 | -0.101932 | -0.148403 | 0.00509658 Absolute |
| major | 9 | B12_standing_170kg | tail_com_speed_mps | load=170;standing_hold_10s;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.03032 | 0.02 UpperBound |
| major | 9 | B12_standing_170kg | tail_bar_speed_mps | load=170;standing_hold_10s;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0356232 | 0.02 UpperBound |
| critical | 9 | B12_standing_300kg | feet_in_contact_after_0p2s | load=300;standing_hold_10s;dt=0.01;pos_iter=28;vel_iter=1 | 1 | 0 | 0 Absolute |
| critical | 9 | B12_standing_300kg | pelvis_drop_m | load=300;standing_hold_10s;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.795923 | 0.05 UpperBound |
| minor | 9 | B12_standing_300kg | tail_support_margin_m | load=300;standing_hold_10s;dt=0.01;pos_iter=28;vel_iter=1 | -0.02 | -1.21015 | -0.02 LowerBound |
| minor | 9 | B12_standing_300kg | tail_nominal_tracking_error_rad | load=300;standing_hold_10s;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.119213 | 0.1 UpperBound |
| minor | 9 | B13_static_matrix_025kg | tail_bar_speed_mps | load=25;phase=0.55;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0231692 | 0.02 UpperBound |
| minor | 9 | B13_static_matrix_060kg | tail_bar_speed_mps | load=60;phase=0.55;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0206627 | 0.02 UpperBound |
| minor | 9 | B13_static_matrix_060kg | tail_nominal_tracking_error_rad | load=60;phase=0.75;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.112867 | 0.1 UpperBound |
| minor | 9 | B13_static_matrix_060kg | tail_bar_speed_mps | load=60;phase=0.75;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0209588 | 0.02 UpperBound |
| minor | 9 | B13_static_matrix_060kg | tail_nominal_tracking_error_rad | load=60;phase=1.00;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.123314 | 0.1 UpperBound |
| minor | 9 | B13_static_matrix_140kg | tail_bar_speed_mps | load=140;phase=0.00;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0207912 | 0.02 UpperBound |
| minor | 9 | B13_static_matrix_140kg | tail_com_speed_mps | load=140;phase=0.10;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0234148 | 0.02 UpperBound |
| minor | 9 | B13_static_matrix_140kg | tail_bar_speed_mps | load=140;phase=0.10;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0280853 | 0.02 UpperBound |
| minor | 9 | B13_static_matrix_140kg | tail_com_speed_mps | load=140;phase=0.25;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0286212 | 0.02 UpperBound |
| major | 9 | B13_static_matrix_140kg | tail_bar_speed_mps | load=140;phase=0.25;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0332648 | 0.02 UpperBound |
| minor | 9 | B13_static_matrix_140kg | pelvis_drop_m | load=140;phase=0.55;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0570585 | 0.05 UpperBound |
| major | 9 | B13_static_matrix_140kg | tail_nominal_tracking_error_rad | load=140;phase=0.55;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.16327 | 0.1 UpperBound |
| minor | 9 | B13_static_matrix_140kg | tail_bar_speed_mps | load=140;phase=0.55;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0236552 | 0.02 UpperBound |
| minor | 9 | B13_static_matrix_140kg | pelvis_drop_m | load=140;phase=0.75;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0701948 | 0.05 UpperBound |
| major | 9 | B13_static_matrix_140kg | tail_nominal_tracking_error_rad | load=140;phase=0.75;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.18804 | 0.1 UpperBound |
| minor | 9 | B13_static_matrix_140kg | tail_com_speed_mps | load=140;phase=0.75;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.022079 | 0.02 UpperBound |
| minor | 9 | B13_static_matrix_140kg | tail_bar_speed_mps | load=140;phase=0.75;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0258378 | 0.02 UpperBound |
| major | 9 | B13_static_matrix_140kg | pelvis_drop_m | load=140;phase=1.00;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0810987 | 0.05 UpperBound |
| major | 9 | B13_static_matrix_140kg | tail_nominal_tracking_error_rad | load=140;phase=1.00;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.209224 | 0.1 UpperBound |
| minor | 9 | B13_static_matrix_140kg | tail_bar_speed_mps | load=140;phase=1.00;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0203845 | 0.02 UpperBound |
| minor | 9 | B13_static_matrix_170kg | tail_com_speed_mps | load=170;phase=0.00;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.023606 | 0.02 UpperBound |
| major | 9 | B13_static_matrix_170kg | tail_bar_speed_mps | load=170;phase=0.00;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0307596 | 0.02 UpperBound |
| minor | 9 | B13_static_matrix_170kg | tail_support_margin_m | load=170;phase=0.10;production;dt=0.01;pos_iter=28;vel_iter=1 | -0.02 | -0.118767 | -0.02 LowerBound |
| minor | 9 | B13_static_matrix_170kg | tail_nominal_tracking_error_rad | load=170;phase=0.10;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.148029 | 0.1 UpperBound |
| critical | 9 | B13_static_matrix_170kg | tail_com_speed_mps | load=170;phase=0.10;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.385804 | 0.02 UpperBound |
| critical | 9 | B13_static_matrix_170kg | tail_bar_speed_mps | load=170;phase=0.10;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.430936 | 0.02 UpperBound |
| minor | 9 | B13_static_matrix_170kg | tail_support_margin_m | load=170;phase=0.25;production;dt=0.01;pos_iter=28;vel_iter=1 | -0.02 | -0.168944 | -0.02 LowerBound |
| major | 9 | B13_static_matrix_170kg | tail_nominal_tracking_error_rad | load=170;phase=0.25;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.198397 | 0.1 UpperBound |
| critical | 9 | B13_static_matrix_170kg | tail_com_speed_mps | load=170;phase=0.25;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.558878 | 0.02 UpperBound |
| critical | 9 | B13_static_matrix_170kg | tail_bar_speed_mps | load=170;phase=0.25;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.630206 | 0.02 UpperBound |
| minor | 9 | B13_static_matrix_170kg | pelvis_drop_m | load=170;phase=0.55;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0669699 | 0.05 UpperBound |
| major | 9 | B13_static_matrix_170kg | tail_nominal_tracking_error_rad | load=170;phase=0.55;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.179322 | 0.1 UpperBound |
| minor | 9 | B13_static_matrix_170kg | tail_com_speed_mps | load=170;phase=0.55;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.021857 | 0.02 UpperBound |
| minor | 9 | B13_static_matrix_170kg | tail_bar_speed_mps | load=170;phase=0.55;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0260361 | 0.02 UpperBound |
| major | 9 | B13_static_matrix_170kg | pelvis_drop_m | load=170;phase=0.75;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0819672 | 0.05 UpperBound |
| major | 9 | B13_static_matrix_170kg | tail_nominal_tracking_error_rad | load=170;phase=0.75;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.204523 | 0.1 UpperBound |
| minor | 9 | B13_static_matrix_170kg | tail_com_speed_mps | load=170;phase=0.75;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.020824 | 0.02 UpperBound |
| minor | 9 | B13_static_matrix_170kg | tail_bar_speed_mps | load=170;phase=0.75;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0251499 | 0.02 UpperBound |
| major | 9 | B13_static_matrix_170kg | pelvis_drop_m | load=170;phase=1.00;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0962358 | 0.05 UpperBound |
| major | 9 | B13_static_matrix_170kg | tail_nominal_tracking_error_rad | load=170;phase=1.00;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.236272 | 0.1 UpperBound |
| critical | 9 | B13_static_matrix_300kg | feet_in_contact_after_0p2s | load=300;phase=0.00;production;dt=0.01;pos_iter=28;vel_iter=1 | 1 | 0 | 0 Absolute |
| critical | 9 | B13_static_matrix_300kg | pelvis_drop_m | load=300;phase=0.00;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.804313 | 0.05 UpperBound |
| minor | 9 | B13_static_matrix_300kg | tail_support_margin_m | load=300;phase=0.00;production;dt=0.01;pos_iter=28;vel_iter=1 | -0.02 | -1.22476 | -0.02 LowerBound |
| critical | 9 | B13_static_matrix_300kg | tail_nominal_tracking_error_rad | load=300;phase=0.00;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.837652 | 0.1 UpperBound |
| critical | 9 | B13_static_matrix_300kg | tail_com_speed_mps | load=300;phase=0.00;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 1.77063 | 0.02 UpperBound |
| critical | 9 | B13_static_matrix_300kg | tail_bar_speed_mps | load=300;phase=0.00;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 3.00926 | 0.02 UpperBound |
| critical | 9 | B13_static_matrix_300kg | feet_in_contact_after_0p2s | load=300;phase=0.10;production;dt=0.01;pos_iter=28;vel_iter=1 | 1 | 0 | 0 Absolute |
| critical | 9 | B13_static_matrix_300kg | pelvis_drop_m | load=300;phase=0.10;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.841027 | 0.05 UpperBound |
| minor | 9 | B13_static_matrix_300kg | tail_support_margin_m | load=300;phase=0.10;production;dt=0.01;pos_iter=28;vel_iter=1 | -0.02 | -1.21211 | -0.02 LowerBound |
| critical | 9 | B13_static_matrix_300kg | tail_nominal_tracking_error_rad | load=300;phase=0.10;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.693743 | 0.1 UpperBound |
| critical | 9 | B13_static_matrix_300kg | tail_com_speed_mps | load=300;phase=0.10;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 2.10817 | 0.02 UpperBound |
| critical | 9 | B13_static_matrix_300kg | tail_bar_speed_mps | load=300;phase=0.10;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 4.28367 | 0.02 UpperBound |
| critical | 9 | B13_static_matrix_300kg | feet_in_contact_after_0p2s | load=300;phase=0.25;production;dt=0.01;pos_iter=28;vel_iter=1 | 1 | 0 | 0 Absolute |
| critical | 9 | B13_static_matrix_300kg | pelvis_drop_m | load=300;phase=0.25;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.650646 | 0.05 UpperBound |
| critical | 9 | B13_static_matrix_300kg | tail_support_margin_m | load=300;phase=0.25;production;dt=0.01;pos_iter=28;vel_iter=1 | -0.02 | NA | -0.02 LowerBound |
| critical | 9 | B13_static_matrix_300kg | tail_nominal_tracking_error_rad | load=300;phase=0.25;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.42272 | 0.1 UpperBound |
| critical | 9 | B13_static_matrix_300kg | tail_com_speed_mps | load=300;phase=0.25;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 2.11889 | 0.02 UpperBound |
| critical | 9 | B13_static_matrix_300kg | tail_bar_speed_mps | load=300;phase=0.25;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 4.21684 | 0.02 UpperBound |
| critical | 9 | B13_static_matrix_300kg | feet_in_contact_after_0p2s | load=300;phase=0.55;production;dt=0.01;pos_iter=28;vel_iter=1 | 1 | 0 | 0 Absolute |
| critical | 9 | B13_static_matrix_300kg | pelvis_drop_m | load=300;phase=0.55;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.418531 | 0.05 UpperBound |
| minor | 9 | B13_static_matrix_300kg | tail_support_margin_m | load=300;phase=0.55;production;dt=0.01;pos_iter=28;vel_iter=1 | -0.02 | -0.616748 | -0.02 LowerBound |
| critical | 9 | B13_static_matrix_300kg | tail_nominal_tracking_error_rad | load=300;phase=0.55;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.591983 | 0.1 UpperBound |
| critical | 9 | B13_static_matrix_300kg | tail_com_speed_mps | load=300;phase=0.55;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 1.61875 | 0.02 UpperBound |
| critical | 9 | B13_static_matrix_300kg | tail_bar_speed_mps | load=300;phase=0.55;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 2.42372 | 0.02 UpperBound |
| critical | 9 | B13_static_matrix_300kg | feet_in_contact_after_0p2s | load=300;phase=0.75;production;dt=0.01;pos_iter=28;vel_iter=1 | 1 | 0 | 0 Absolute |
| minor | 9 | B13_static_matrix_300kg | pelvis_drop_m | load=300;phase=0.75;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0749808 | 0.05 UpperBound |
| critical | 9 | B13_static_matrix_300kg | tail_support_margin_m | load=300;phase=0.75;production;dt=0.01;pos_iter=28;vel_iter=1 | -0.02 | NA | -0.02 LowerBound |
| critical | 9 | B13_static_matrix_300kg | tail_nominal_tracking_error_rad | load=300;phase=0.75;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.373021 | 0.1 UpperBound |
| critical | 9 | B13_static_matrix_300kg | tail_com_speed_mps | load=300;phase=0.75;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 1.87067 | 0.02 UpperBound |
| critical | 9 | B13_static_matrix_300kg | tail_bar_speed_mps | load=300;phase=0.75;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 2.96924 | 0.02 UpperBound |
| critical | 9 | B13_static_matrix_300kg | feet_in_contact_after_0p2s | load=300;phase=1.00;production;dt=0.01;pos_iter=28;vel_iter=1 | 1 | 0 | 0 Absolute |
| minor | 9 | B13_static_matrix_300kg | pelvis_drop_m | load=300;phase=1.00;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.0729258 | 0.05 UpperBound |
| critical | 9 | B13_static_matrix_300kg | tail_support_margin_m | load=300;phase=1.00;production;dt=0.01;pos_iter=28;vel_iter=1 | -0.02 | NA | -0.02 LowerBound |
| critical | 9 | B13_static_matrix_300kg | tail_nominal_tracking_error_rad | load=300;phase=1.00;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.304099 | 0.1 UpperBound |
| critical | 9 | B13_static_matrix_300kg | tail_com_speed_mps | load=300;phase=1.00;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.557707 | 0.02 UpperBound |
| critical | 9 | B13_static_matrix_300kg | tail_bar_speed_mps | load=300;phase=1.00;production;dt=0.01;pos_iter=28;vel_iter=1 | 0 | 0.392307 | 0.02 UpperBound |
