# GAM-48 Gate 4b — validation receipt

Branch: work/gam-13-squat-load-calibration
Base: 8480a37bb4fd63af8e0cdf353726a52744aa8f41
Unity: 6000.3.22f1

| Gate | Result |
|---|---:|
| Unity clean import/compile | PASS; Tundra build success |
| Powered-joint EditMode tests | 3/3 PASS |
| Full EditMode suite | 227/227 PASS |
| GAM-48 observation authority PlayMode | 4/4 PASS |
| Squat lifecycle integration | 1/1 PASS |
| Observation trace integration | 1/1 PASS |
| Current-head GAM-10 reference qualification, graphics-enabled | 4/4 PASS |
| GAM-48 reference-solver parity-only check | 1/1 PASS |
| Fresh-process Gate 3 baseline repeatability and HOLD_1.00_FULL/C0_FULL equivalence | PASS |
| Fresh-process Gate 4b C0_FULL runtime decomposition | 1/1 PASS |
| Fresh-process Gate 4b HOLD_1.00_FULL runtime decomposition | 1/1 PASS |
| Fresh-process Gate 4b C0/HOLD numeric equivalence | PASS; maximum reported depth/layer delta 0 m |
| Master Spec verification | PASS |
| PowerShell fresh-process harness syntax | PASS |

The graphics-disabled reference probe is not counted as the current-head visual qualification; the full GAM-10 qualification was rerun with graphics enabled and passed 4/4.

Fresh-process raw XML/logs and per-arm decomposition, target-composition, and applied-target artifacts are retained under the Gate 4b validation and fresh-process run directories. No tuning or production-physics parameter change was made.
