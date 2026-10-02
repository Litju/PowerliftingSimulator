# GAM-13 posture / equilibrium fix receipt

Details: `Artifacts/Reports/GAM-13-posture-equilibrium.md`. Arms and rules:
`Artifacts/Research/GAM-13-posture-equilibrium-isolation.md` (committed
before execution in `579cce6`, amended before the 3x follow-up arms in
`87be4dd`). Evidence: `Artifacts/Measurements/GAM-13/posture-equilibrium/`
(gates in `qualification-gates.md`).

```text
MISSION=GAM13_POSTURE_EQUILIBRIUM_FIX
STATUS=BLOCKED

START_HEAD=9d331e9c03a673db348a7a3a6762c3e04108b83e
FINAL_HEAD=the commit adding this receipt
REMOTE_HEAD=FINAL_HEAD after non-force push

GUARD_RESULT=NOT_CAUSAL. Guard never scaling balance (1x): NO_CHANGE at
  25/60/300 kg, DELAYS at 140 kg (capture 76 -> 100) and 170 kg (73 -> 85,
  support loss 192 -> 210); gross posture fold unmoved (51/45/34 ->
  53/45/33). On 3x (K3G) 25-170 kg stay qualified and 300 kg still fails
  (capture 43 -> 93). Not kept in production.
EQUILIBRIUM_RESULT=Runtime standing bias is spine-only (ankle/knee/hip 0;
  abdomen/thorax 3.93/3.67, 7.4/6.6, 11.2/10.3, 11.8/11.0, 12/12 deg at
  25/60/140/170/300) against a V2 trim needing knee -6.9..-12 and hip
  +9..+12 at 140-300 kg. Class LOWER_CHAIN_MISSING + BOUND_REACHED +
  STIFFNESS_INSUFFICIENT_1X. V2 trim targets on 1x (E1) fail 60/140/170/300.
  3x trim targets on 3x (C3) are not needed at 25-170 and do not fix 300
  (170 kg then fails the posture contract at 11.30 deg). No production
  equilibrium law is warranted (step 7 condition not met).
3X_IMPEDANCE_RESULT=PRESERVES_25_60=YES, FIXES_140_170=YES,
  IMPROVES_300=YES (first onset 34 -> 43, support loss 139 -> 196) but not
  fixed. Qualified 25/60/140/170 in three fresh bit-identical repeats and in
  the official Stage-A fixture. The 25 kg lifecycle on 3x loses legal depth
  (P2 adds INSUFFICIENT_DEPTH). Not promoted.

FINAL_PLANT=unchanged: GAM7_CONFIGURABLE_JOINT_LOCAL_V1 (1x), Saddle V2,
  athlete 28/1, bar 12/6
FINAL_EQUILIBRIUM_MODEL=unchanged: GAM13_SQUAT_EQUILIBRIUM_FEEDFORWARD_V1
  with the 5H16 standing knots, +/-12 deg hard limit
FINAL_GUARD_BEHAVIOR=unchanged: production posture guard (4/8 deg, rate
  term, unexpected-margin limit half)

25=PASS
60=FAIL
140=FAIL
170=FAIL
300=FAIL
  (production. Candidate fixed 3x plant: 25/60/140/170 PASS, 300 FAIL)

25KG_LIFECYCLE=PASS (production unchanged; accepted GAM-12 table x3)

ML_CAPTURE_CHANGE_REQUIRED=YES. Replace the AP-only Stage-A capture
  predicate min(CaptureMarginFront, CaptureMarginRear) > 0.01 m with the
  capture point's signed margin to the plantar contact hull
  (SquatSupportGeometry.Measure) > 0.01 m in the Stage-A fixture and the
  causal accumulator. The AP proxy missed the V1 60 kg lateral exit by up to
  8.4 cm / 38 ticks; it agrees with the hull within 0.5 mm on every run
  qualified here. This mission's qualification already required the hull
  margin. Balance control is not changed.

ESTABLISHED_CAUSES=1x load-bearing joint stiffness causes the 140/170 kg
  standing failure and the 60 kg posture-contract failure (single property,
  one fixed 3x plant for every load, everything else fixed: 60 kg 12.17 ->
  1.81 deg, 140/170 kg collapse -> qualified).
  Physically, the loaded spine alone is statically unstable from 140 kg at
  1x (K - G indefinite), so no bias can hold it. The Stage-A posture error is
  taken against nominal x gravity bias, so at equilibrium it reads tau/k and
  cannot meet 10 deg at 1x from 140 kg. The hip+spine static margin has the
  sign of every upright/fold outcome here (10/10 load-plant pairs); at 300 kg
  on 3x the rear capture exit precedes the fold.
REJECTED_HYPOTHESES=the posture guard causes the heavy capture failure (G1,
  K3G); missing lower-chain equilibrium alone explains 1x (E1 fails);
  equilibrium compensation is independently necessary on 3x (K3 qualifies
  25-170 without it; C3 does not fix 300); the +/-12 deg bound binds on 3x
  (T3 needs <= 9.4 deg to 230 kg and <= 11.5 deg at 300 kg, yet 300 fails)
REMAINING_BLOCKER=300 kg on the fixed 3x plant: the trunk-on-thigh
  (hip+spine) chain needs >= 3.32x GAM-7 (4.50x with the knees; 3.0x reaches
  its limit at about 268 kg). The latched balance COM sits 0.3 cm ahead of
  the rearmost admissible COP in the canonical pose. Any stiffer plant also
  needs the 1x-identified phase feed-forward re-identified to keep 25 kg
  legal depth.

TESTS=EditMode 222/222; default PlayMode 182/182 (174 headless + 8 GPU,
  18 explicit skipped); GAM-12 25 kg lifecycle x3 unchanged; official Stage-A
  production 25 PASS / 60-300 FAIL, 3x 25-170 PASS / 300 FAIL; causal arms
  A0, G1, E1, K3 x3, K3G, C3; T3 static trim; candidate lifecycle production
  and 3x x3
MASTER_SPEC=PASS (68 files, hashes, dependencies)

COMMITS=579cce6 docs(gam13): predeclare posture and equilibrium isolation arms
        87be4dd docs(gam13): predeclare 3x guard and 3x trim arms for 300 kg
        75a90af test(gam13): isolate posture guard
        08d3240 test(gam13): isolate heavy equilibrium demand
        e2e11f2 test(gam13): isolate 300 kg on the fixed 3x plant
        (receipt commit) test(gam13): seal standing-family evidence

GAM13=IN_PROGRESS
GAM14=BACKLOG
PR_CREATED=NO
MERGE_PERFORMED=NO
LINEAR_UPDATE=NOT_POSTED (Linear connector not authorized in this session)

NEXT_GATE=Owner decision on the loaded-standing plant: (a) authorize one
  further fixed load-bearing impedance >= 3.32x (4.5x clears every chain),
  qualified at all five loads, with the phase feed-forward re-identified on
  it so the 25 kg lifecycle keeps legal depth; or (b) re-scope canonical
  standing below ~268 kg on 3x, with the same re-identification; or (c) a
  separate balance set-point / whole-body lean mission for heavy loads. Then
  make the 2-D Stage-A capture contract change. No dynamic-model
  refoundation, capacity, sticking or failure calibration, and no GAM-14,
  until standing qualifies at all five loads.
```

## Invariants

`SADDLE_V2_CHANGED=NO` · `CAPACITY_TUNED=NO` · `SQUAT_RULES_CHANGED=NO` ·
`FAILURE_DEFINITIONS_CHANGED=NO` · `P1/P2/P3/P4_CHANGED=NO` ·
`BAR_MASS_INERTIA_CHANGED=NO` · `SOLVER_CHANGED=NO` ·
`PRODUCTION_CODE_CHANGED=NO` · `PER_LOAD_GAINS=NO (diagnostic trim targets
only, test-side)` · `IMPEDANCE_SWEEP=NO (one fixed 3x plant)` ·
`ADD_FORCE_OR_TORQUE=NO` · `VELOCITY_WRITES=NO` · `TRANSFORM_PINNING=NO` ·
`KINEMATIC_SUPPORT=NO`

The minimum stable factors come analytically from the 1x static margin, not
from runs. The default suite's regenerated historical evidence (295 files)
was restored, not committed.
