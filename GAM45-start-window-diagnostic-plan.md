# GAM-45 start-window diagnostic plan

## Authority

GAM-45, Linear issue `GAM-45`, on `work/gam-13-squat-load-calibration` at
the issue start head. The immutable production start predicate remains the
GAM-12 contract: motionless available bar, available established support,
available feet, bilateral knee lockout, bilateral erect hips, and erect
abdomen/thorax for the existing persistence.

## Probe arms

Each arm runs in a separate Unity process and loads a fresh
`SquatPhysicalPrototype` scene at 25 kg:

* `P0`: accepted production/default control.
* `P1`: 5x plant impedance, F1 spine-only feed-forward, S1 saddle.
* `P2`: 5x plant impedance, F2 feed-forward, S1 saddle.
* `P2_NO_LOWER_CHAIN`: P2 with only F2 ankle/knee/hip standing compensation
  disabled as a diagnostic intervention; F2 spine, plant, saddle, balance
  controller, solver, capacity, lifecycle, rules, and tolerances remain fixed.

The harness reads `SquatAttemptOrchestrator.StartWindowDiagnostics`, which is
the production predicate breakdown. It does not reimplement the predicate.
Each per-tick row includes the predicate result, persistence run, threshold
margins, posture/support observations, control state, standing biases, COM
speed, and 2-D capture margin.

## Decision gates

1. Establish whether each arm reaches the required three-sample candidate run
   and whether the lifecycle enters `START_WINDOW`.
2. Classify the observed failing predicate(s) from the production breakdown.
3. If P1 qualifies and P2 does not, compare P2 with the single lower-chain
   standing-compensation isolation arm.
4. Do not change tolerances, rules, capacity, saddle definitions, stiffness,
   gains, direct forces/torques, velocities, or transforms.

No production correction is pre-authorized. A correction can be tested only
after the evidence uniquely supports a harness/observation boundary defect or
one bounded integration correction.
