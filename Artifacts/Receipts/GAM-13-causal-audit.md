# GAM-13 causal audit receipt

Supersedes `GAM-13-causal-audit-blocked.md` (runtime gate now executed).
Details: `Artifacts/Reports/GAM-13-causal-audit.md`. Predicates:
`Artifacts/Research/GAM-13-causal-onset-predicates.md` (frozen in `fa34178`
before execution). Saddle V2 design:
`Artifacts/Research/GAM-13-saddle-v2-contact-topology.md`.

```text
MISSION=GAM13_CAUSAL_AUDIT_RUNTIME_EXECUTION
STATUS=PASS

START_HEAD=ae79f14 (local; descends from remote 9a809609ed8b195e7f2b292e84773972e263243b)
FINAL_HEAD=the commit adding this receipt (parent 42d0ca7)
REMOTE_HEAD=FINAL_HEAD after non-force push

CAUSAL_ORDER_25=NO_FAILURE (Stage-A PASS; bar/arm contact and closed-chain ankle
  target error are present but non-discriminating)
CAUSAL_ORDER_60=SADDLE_CONTACT (bar/limb clamp from tick 1, ESTABLISHED)
  -> lateral bar/trunk roll -> mediolateral capture exit @80 (AP proxy @118)
  -> support loss @202; concurrent spine sag (guard @18, posture >=10 deg @42)
  that stands on its own once contact is removed
CAUSAL_ORDER_300=POSTURE (guard withdrawal @12, trunk >=0.70 rad @33)
  -> [saddle linear limit @18, co-occurring, not causal per I2]
  -> drive high @45 (no saturation) -> rearward capture exit @66 with COP
  13.6 cm inside the hull -> support loss @138

SOLVER_CLASSIFICATION=SOLVER_QUANTITATIVE
  28/4 bit-identical to 28/1 (island uses the bar's 6 velocity iterations);
  28/8 <= 5 ticks; 32/1, 40/1 keep the leading order and class; 60 kg
  support loss 202 -> 171 -> 146

BAR_BACK_LOAD_PATH=CONFIGURABLEJOINT (complete vertical path) + unintended
  hand/forearm/upper-arm contact in V1; no bar/thorax contact
BAR_THORAX_PHYSICAL_CONTACT=NONE (enableCollision=false; 0 callbacks; shaft
  overlaps thorax box ~2 cm at spawn, hangs 1-4 mm off it once settled)
SADDLE_JOINT_LOAD_SHARE=V1 pre-gross medians 1.03 (0.90-1.16) @25, 0.95 @140,
  0.96 @170, 0.98 @300, 60 kg unbounded (clamp); V2 1.00 at all loads

MASS_RATIO_25=1.157 (bar/thorax; bar/athlete 0.25)
MASS_RATIO_60=2.778 (0.60)
MASS_RATIO_140=6.481 (1.40)
MASS_RATIO_170=7.870 (1.70)
MASS_RATIO_300=13.889 (3.00)
  massScale=connectedMassScale=1; thorax 21.6 kg; athlete 100 kg

AABB_VS_HULL=AP exits: hull, 2-D AABB and AP proxy agree within 1.1 mm through
  the exit; V1 60 kg exit is mediolateral: 2-D AABB ~ hull (0.8 mm) but the
  production AP-only proxy misses it by up to 8.4 cm and lags 38 ticks
WRENCH_FEASIBILITY=NECESSARY_CONDITIONS_FEASIBLE before failure (validated
  centroidal reconstruction, V1 25/140/170/300 and V2 all loads); V1 60 kg
  NOT_OBSERVABLE (reconstruction fails validation); per-contact sufficiency
  NOT_OBSERVABLE (PhysX reports normal impulses only; no tangential or
  torsional contact impulses)

OBSERVED=Stage-A baseline reproduced exactly; saddle filter enumerated 0 bar
  colliders (0/135 pairs ignored); hands clamp plates 6-8 kN at 60 kg;
  currentForce reports hard-limit rows only; COP 8.4-13.3 cm inside the hull
  at every capture exit with guard scale 0; no load-bearing drive saturation
SUPPORTED_HYPOTHESES=140-300 kg failure is posture-first (spring-limited spine
  under load with a <=25 kg equilibrium feed-forward, guard withdraws sagittal
  ankle authority, unused COP authority); spawn bar drop (bar 5 cm above the
  anchor) may contribute at 300 kg (untested); 60 kg residual 11-16 deg spine
  sag is an equilibrium-feed-forward gap
ESTABLISHED_CAUSES=bar/non-thorax limb contact (unfiltered saddle collision
  topology) causes the 60 kg capture departure and support loss (I1; V2
  bit-identical)
REJECTED_HYPOTHESES=saddle hard-limit engagement causes the 300 kg collapse
  (I2); bar/limb contact causes the 300 kg collapse (I1); athlete solver
  iterations change the causal order (isolation arms); load-bearing drive
  saturation leads failure (never fires); physical support-wrench limit
  precedes capture departure (COP interior, necessary conditions hold)

SADDLE_CAUSALITY=ESTABLISHED (contact topology, 60 kg)
SADDLE_V2_IMPLEMENTED=YES (GAM13_SADDLE_BAR_LIMB_FILTER_V2: filter enumerates
  the bar Rigidbody's colliders; joint, limits, drives, break, mass scaling,
  enableCollision unchanged)
SADDLE_V2_CAUSAL_EFFECT=60 kg capture/support failure removed (upright plateau,
  Stage-A fails only the 10 deg posture contract at 12.2 deg); 25 kg
  unchanged; 140/170/300 kg unchanged posture-first collapse

TRIM_STATUS=V2 from scratch, +/-12 deg: 25 feasible, 60 feasible (new), 140/170/300
  infeasible with biases at the bound
25KG_REGRESSION=NONE (Stage-A PASS; GAM-12 lifecycle unchanged relative to the
  squat command, identical P2/P3/terminal; start qualified 71 ticks earlier)

TESTS=EditMode 222/222; default PlayMode 182/182 pre and post (8 GPU fixtures
  with a graphics device); causal fixtures 4/4; static trim 1/1
MASTER_SPEC=PASS (68 files, hashes, dependencies)

COMMITS=fa34178 test(gam13): extend causal audit telemetry and freeze onset predicates
        6cd52b9 test(gam13): isolate solver, load path and support geometry
        42d0ca7 fix(gam13): filter bar/limb contact in the squat saddle
        (receipt commit) test(gam13): qualify saddle contact intervention

GAM13_STATE=IN_PROGRESS
GAM14_STATE=BACKLOG
PR_CREATED=NO
MERGE_PERFORMED=NO
LINEAR_UPDATE=NOT_POSTED (Linear connector not authorized in this session)

NEXT_GATE=Posture/equilibrium isolation on the V2 plant: predeclare single-property
  posture-side interventions (spine equilibrium feed-forward beyond 25 kg,
  posture-guard withdrawal) to establish or reject the posture-first chain at
  140-300 kg, and decide whether the capture contract must include the
  mediolateral margin. No capacity, sticking, failure calibration or GAM-14.
```

## Invariants

`P1_CHANGED=NO` · `P2_CHANGED=NO` · `P3_SEMANTICS_CHANGED=NO` ·
`P4_CHANGED=NO` · `CAPACITY_TUNED=NO` · `BALANCE_REDESIGNED=NO` ·
`PER_LOAD_GAINS=NO` · `ADD_FORCE_OR_TORQUE=NO` · `VELOCITY_WRITES=NO` ·
`TRANSFORM_PINNING=NO` · `SCRIPTED_OUTCOMES=NO` · `ONE_PHYSICAL_WRITER=YES`

Regenerated historical GAM-11 evidence (120 files differ on the V2 plant; all
live assertions pass) was restored, not re-committed.
