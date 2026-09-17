# GAM-13 completion receipt

MISSION
`GAM13_END_TO_END_SQUAT_LOAD_RESPONSE_STICKING_AND_PHYSICAL_FAILURE_CALIBRATION`

STATUS
`BLOCKED`

START_HEAD
`e512d141e64fe33dfa186c549738c529e16ba3c3`

BRANCH
`work/gam-13-squat-load-calibration`

CHECKPOINT
`checkpoint/gam13-pre-heavy-load-control-reidentification` at `e512d14`

## Blocking gate

Stage-A standing passed at 25 kg but did not pass at 60 kg or 300 kg. The
measured failure is setup collapse/backward balance loss before a meaningful
attempt, with finite values and no actuator-capacity ceiling binding. Evidence
is in the Stage-A measurements and
`Artifacts/Research/GAM-13-heavy-load-control-architecture.md`.

## Candidate versions

`EQUILIBRIUM_FEEDFORWARD_CANDIDATE=GAM13_SQUAT_EQUILIBRIUM_FEEDFORWARD_V1`

`ATHLETE_CAPACITY_CANDIDATE=GAM13_SQUAT_ATHLETE_CAPACITY_V1`

`BALANCE_CALIBRATION=NOT_FROZEN`

`FAILURE_CALIBRATION=NOT_STARTED`

## Stage-A outcomes

| Load | Result | Evidence |
|---:|---|---|
| 25 kg | PASS | fixed-plant hold; 25 kg baseline preserved in the candidate run |
| 60 kg | FAIL | persistent setup/posture/balance collapse |
| 140 kg | PASS in selected 2x/3x diagnostics | not an exit gate because 60/300 fail |
| 170 kg | PASS in selected 2x/3x diagnostics | not an exit gate because 60/300 fail |
| 300 kg | FAIL | persistent setup/posture/balance collapse |

## Tests and validation

`GAM13HeavyLoadControlContractTests`: focused EditMode 3/3 PASS.  
Stage-A explicit candidate fixture: executed; 25 PASS, 60/300 FAIL.  
MasterSpec hashes: not changed; verifier was green before candidate work.  
Full EditMode/PlayMode/graphics/performance: not claimed as final acceptance.  
PR/merge: not created.  
Linear: GAM-13 remains In Progress; GAM-14 remains Backlog.

## Invariants

`P1_CHANGED=NO`  
`P2_CHANGED=NO`  
`P3_SEMANTICS_CHANGED=NO`  
`P4_CHANGED=NO`  
`LOAD_THRESHOLD_SCRIPT=NO`  
`PER_LOAD_IMPEDANCE=NO`  
`PER_LOAD_BALANCE_GAIN=NO`  
`CLAIM_CEILING=GAME_ENGINE_CONTROL_CALIBRATION`

No final capacity outcomes, sticking evidence, supra-max failure evidence,
held-out results, PR number, merge SHA, or Linear completion is reported because
the Stage-A exit gate was not satisfied.
