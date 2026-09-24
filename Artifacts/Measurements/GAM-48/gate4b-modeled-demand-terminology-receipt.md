# GAM-48 Gate 4b — modeled demand and solver-torque terminology

Unity: 6000.3.22f1
Fresh supported C0/HOLD processes: 20260923-221554

Use these evidence labels: MODELED_DRIVE_DEMAND, MODELED_DRIVE_DEMAND_HIGH, and ENGINE_SOLVER_CONSTRAINT_TORQUE_JOINT_SPACE_NM.

The controller's modeled demand is the magnitude of Kp times errorRad plus Kd times (targetAngularVelocity minus actualAngularVelocity), divided by max(maximumForceNm, 0.001). The MODELED_DRIVE_DEMAND_HIGH flag uses the existing 0.95 threshold. It is a command-side model; it does not prove that PhysX reached its internal drive-force clamp.

For the settled supported C0/HOLD comparison, maximum MODELED_DRIVE_DEMAND was 0.266502559 in both processes and MODELED_DRIVE_DEMAND_HIGH=false. This says only that the modeled demand stayed below the configured reporting threshold in that window.

ConfigurableJoint.currentTorque, exposed as SolverTorqueJointSpaceNm, is an engine solver/constraint diagnostic. It includes the constraint solution and is not direct joint drive torque or actuator utilization.

No drive configuration, solver parameter, or production diagnostic value was changed.
