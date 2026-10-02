# GAM-13 saddle/contact causality audit receipt

MISSION
`GAM13_SADDLE_CONTACT_AND_CAUSALITY_AUDIT`

STATUS
`BLOCKED`

START_HEAD
`9a809609ed8b195e7f2b292e84773972e263243b`

CHECKPOINT
`gam13-audit-start-9a809609`

INSTRUMENTATION_COMMIT
`08e3e93` — deterministic post-physics causal CSV seam, engine joint force/torque
diagnostics, saddle limit occupancy, bar/thorax callback capture, and support
convex-hull comparison.

AUTHORITY

- Repository branch and expected HEAD were verified.
- Linear `GAM-13` was fetched live: In Progress; canonical probes remain
  25/60/140/170/300 kg.
- GAM-14 remains Backlog; no Linear state was changed.

RUNTIME_GATE

`NOT_EXECUTED`: the host has no Unity Editor executable. The project is pinned
to Unity `6000.3.22f1`; the only local `unity.exe` is the Hub/package CLI and
rejects `-batchmode`. Therefore the new PlayMode telemetry and the required
orthogonal solver runs were not run and no fresh first-onset order is claimed.

OBSERVED_PRIOR_EVIDENCE

The current-HEAD trim-refoundation evidence (the 9a provenance correction did
not change measured physics rows) records 25 kg standing as PASS, while 60 and
300 kg lose support/upright validity. It records finite values and no qualified
capacity conclusion. Aggregate rows do not establish first onset or isolate a
saddle intervention.

CAUSAL_CLASSIFICATION

- `ESTABLISHED_CAUSES=NONE`.
- `CAUSAL_ORDER_25=NO_FAILURE_OBSERVED` in the prior qualified standing gate.
- `CAUSAL_ORDER_60=NOT_OBSERVABLE_FROM_AVAILABLE_AGGREGATES`; posture/support
  are supported leading hypotheses, not an established ordering.
- `CAUSAL_ORDER_300=NOT_OBSERVABLE_FROM_AVAILABLE_AGGREGATES`; posture/capture
  and saddle separation co-occur in aggregate evidence, so causality is not
  assigned.
- `SOLVER_FINDING=NOT_EXECUTED`; the prior 24/8 comparison is explicitly
  excluded because it changed position and velocity iterations together.
- `BAR_BACK_LOAD_PATH=SOURCE_SUPPORTED_DYNAMIC_BAR_TO_FINITE_CONFIGURABLEJOINT_TO_THORAX`;
  the bar is dynamic and the saddle joint is finite. Actual bar/thorax contact
  callbacks remain unobserved. The current saddle sets connected-body
  collision disabled, so callback absence must be measured as topology, not
  silently treated as contact.
- `SUPPORT_GEOMETRY_FINDING=Current diagnostic is an AABB over plantar contact
  points; the committed audit adds a diagnostic convex hull but does not alter
  control.`
- `WRENCH_FEASIBILITY=NOT_OBSERVABLE`; captured engine contact impulses, when
  available, do not identify the admissible contact-wrench cone.

DECISION

`SADDLE_V2_IMPLEMENTED=NO`. Saddle causality is not established; no saddle
redesign, capacity tuning, balance redesign, broad impedance sweep, or
load-outcome branch was promoted.

NEXT_ISOLATED_SUBSYSTEM

Run the committed audit with the pinned Unity Editor. If the fresh first-onset
order again puts posture/tracking or capture/support ahead of saddle limit
occupancy, isolate the load-bearing posture/capture-support controller next.
Only a bounded saddle intervention that changes the predicted downstream
failure while the rest of the plant is fixed can establish saddle causality.

INVARIANTS

`P1_CHANGED=NO` · `P2_CHANGED=NO` · `P3_SEMANTICS_CHANGED=NO` · `P4_CHANGED=NO`
· `CAPACITY_TUNED=NO` · `LOAD_THRESHOLD_SCRIPT=NO` · `ADD_FORCE_OR_TORQUE=NO`
· `KINEMATIC_SUPPORT=NO`

GAM13_STATE=`IN_PROGRESS`

GAM14_STATE=`BACKLOG`

PR_CREATED=`NO`

MERGE_PERFORMED=`NO`
