# GAM-13 V2-3B Tick-0 Substrate Receipt

Unity `6000.3.22f1`; branch `work/gam-13-squat-load-calibration`; authoritative local PhysicsScene; fixed step `0.01 s`.

| Setup | Tick-0 check | Athlete | Bar | Saddle |
|---|---|---|---|---|
| Unloaded | PASS, validation at tick 0 | 16 dynamic bodies, 15 joints, 99.999992 kg; both soles at the platform plane | Inactive; authored 25 kg model remains finite | Absent |
| 25 kg | PASS, validation at tick 0 | Same topology/mass; both soles at the platform plane | Active dynamic/gravity body, exactly 25 kg; one Rigidbody; compound inertia differs from its model by `1.35e-6 kg·m²` | Initial anchor error `0 m`; separation `0 m`; translation occupancy `0`; relative rotation `0°`; unbroken; engine force/torque `0` |

Both receipts report **zero active initial collider penetration**, finite zero startup velocities, primed standing targets, and exact authored pose/zero-velocity restoration after one owned step and reset. Foot/platform gaps are `0 m` on both sides. The active 25 kg bar uses explicit compound COM/inertia with automatic Rigidbody COM/inertia disabled. The first simulation step was held until the tick-0 validator released it.

There are four explicitly suppressed direct athlete-joint overlap pairs (pelvis–abdomen, pelvis–left/right thigh, abdomen–thorax), maximum `101.6 mm`; the loaded setup also has four bar/non-thorax setup overlaps suppressed by the existing collision filter (forearms/shoulders and hands/collars). These pairs cannot generate contact impulses; all other active pairs pass the penetration gate. Pair names/depths, the full 16-segment mass/COM/collider/inertia table, 15 joint frames/drives, platform material, bar, and saddle state are in the detailed [unloaded receipt](../Measurements/GAM-13/v2-3b-substrate/tick-0-unloaded-final-gated.json) and [25 kg receipt](../Measurements/GAM-13/v2-3b-substrate/tick-0-25kg-final-gated.json).

The fresh-process PlayMode receipt/reset checks passed 1/1 for [unloaded](../Measurements/GAM-13/v2-3b-substrate/tick0-playmode-unloaded-results.xml) and [25 kg](../Measurements/GAM-13/v2-3b-substrate/tick0-playmode-25kg-results.xml).

The unloaded **standing** gate is a separate failure: see [Gate A trace](../Measurements/GAM-13/v2-3b-substrate/standing/000kg/20260927-gate-a-final/qualification-trace.csv). Gate B and all later phases were not run.
