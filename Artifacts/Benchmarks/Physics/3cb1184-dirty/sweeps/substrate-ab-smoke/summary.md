# Physics Benchmark V1 summary

Bench root: `Artifacts/Benchmarks/Physics/3cb1184-dirty/sweeps/substrate-ab-smoke`

| case | run | gated | pass | fail |
|---|---|---:|---:|---:|
| B16_substrate_ab_PGS | 20261001-183752-ab-smoke | 31 | 14 | 17 |

**Earliest failing layer: 5 (constraint convergence).**

| sev | layer | case | metric | config | expected | observed | tol |
|---|---:|---|---|---|---:|---:|---:|
| critical | 5 | B16_substrate_ab_PGS | CJ_ankle_static_error_rad | PGS;CJ;load=0;rep=0;dt=0.01;pos_iter=28;vel_iter=1 | 0.000582668 | -0.00978724 | 0.002 Absolute |
| critical | 5 | B16_substrate_ab_PGS | CJ_knee_static_error_rad | PGS;CJ;load=0;rep=0;dt=0.01;pos_iter=28;vel_iter=1 | 0.0355733 | 0.0581987 | 0.002 Absolute |
| major | 5 | B16_substrate_ab_PGS | CJ_hip_static_error_rad | PGS;CJ;load=0;rep=0;dt=0.01;pos_iter=28;vel_iter=1 | -0.0155364 | -0.0190951 | 0.002 Absolute |
| critical | 5 | B16_substrate_ab_PGS | CJ_ankle_static_error_rad | PGS;CJ;load=25;rep=0;dt=0.01;pos_iter=28;vel_iter=1 | -0.00757804 | -0.0188976 | 0.002 Absolute |
| critical | 5 | B16_substrate_ab_PGS | CJ_knee_static_error_rad | PGS;CJ;load=25;rep=0;dt=0.01;pos_iter=28;vel_iter=1 | 0.0391887 | 0.063967 | 0.002 Absolute |
| major | 5 | B16_substrate_ab_PGS | CJ_hip_static_error_rad | PGS;CJ;load=25;rep=0;dt=0.01;pos_iter=28;vel_iter=1 | -0.0276061 | -0.0333024 | 0.002 Absolute |
| critical | 5 | B16_substrate_ab_PGS | CJ_ankle_static_error_rad | PGS;CJ;load=60;rep=0;dt=0.01;pos_iter=28;vel_iter=1 | -0.0218091 | -0.0368065 | 0.002 Absolute |
| critical | 5 | B16_substrate_ab_PGS | CJ_knee_static_error_rad | PGS;CJ;load=60;rep=0;dt=0.01;pos_iter=28;vel_iter=1 | 0.0427041 | 0.0734443 | 0.0021352 Absolute |
| critical | 5 | B16_substrate_ab_PGS | CJ_hip_static_error_rad | PGS;CJ;load=60;rep=0;dt=0.01;pos_iter=28;vel_iter=1 | -0.0456722 | -0.0531257 | 0.00228361 Absolute |
| critical | 5 | B16_substrate_ab_PGS | CJ_ankle_static_error_rad | PGS;CJ;load=140;rep=0;dt=0.01;pos_iter=28;vel_iter=1 | -0.0771738 | -0.106931 | 0.00385869 Absolute |
| critical | 5 | B16_substrate_ab_PGS | CJ_knee_static_error_rad | PGS;CJ;load=140;rep=0;dt=0.01;pos_iter=28;vel_iter=1 | 0.0380072 | 0.0735083 | 0.002 Absolute |
| major | 5 | B16_substrate_ab_PGS | CJ_hip_static_error_rad | PGS;CJ;load=140;rep=0;dt=0.01;pos_iter=28;vel_iter=1 | -0.0956854 | -0.108481 | 0.00478427 Absolute |
| critical | 5 | B16_substrate_ab_PGS | CJ_ankle_static_error_rad | PGS;CJ;load=170;rep=0;dt=0.01;pos_iter=28;vel_iter=1 | -0.114106 | -0.152792 | 0.00570528 Absolute |
| critical | 5 | B16_substrate_ab_PGS | CJ_knee_static_error_rad | PGS;CJ;load=170;rep=0;dt=0.01;pos_iter=28;vel_iter=1 | 0.0271511 | 0.0584208 | 0.002 Absolute |
| major | 5 | B16_substrate_ab_PGS | CJ_hip_static_error_rad | PGS;CJ;load=170;rep=0;dt=0.01;pos_iter=28;vel_iter=1 | -0.119991 | -0.135421 | 0.00599956 Absolute |
| major | 5 | B16_substrate_ab_PGS | AB_ankle_static_error_rad | PGS;AB;load=170;rep=0;dt=0.01;pos_iter=28;vel_iter=1 | -0.114106 | -0.124189 | 0.00570528 Absolute |
| major | 5 | B16_substrate_ab_PGS | AB_knee_static_error_rad | PGS;AB;load=170;rep=0;dt=0.01;pos_iter=28;vel_iter=1 | 0.0271511 | 0.0220144 | 0.002 Absolute |
