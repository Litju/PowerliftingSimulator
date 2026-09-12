namespace PowerliftingSimulator.Athlete
{
    /// <summary>
    /// The solver budget the physical athlete's bodies run at, and the single
    /// authority for it.
    ///
    /// PhysX solves a joint drive iteratively, so an authored positionSpring
    /// is only realised once the solver has converged on it. At the engine
    /// default of six position iterations the athlete's drives deliver a
    /// fraction of what they are authored to: the ankle measured 0.202 of its
    /// spring, the hip 0.308, the abdomen 0.540. Twenty-eight iterations
    /// brings seven of the eight powered families to between 0.96 and 1.00
    /// (Artifacts/Measurements/GAM-11/GAM11-h9-solver-profile-selection.csv).
    ///
    /// Twenty-eight rather than thirty-two because thirty-two qualifies no
    /// additional family and costs more. The one family it does not fix, the
    /// wrist, is not fixed by any iteration count; that is recorded with the
    /// contract tests.
    ///
    /// These are per-body settings, deliberately. The project's global
    /// DynamicsManager values are untouched, so the barbell, plates and
    /// platform keep the engine default and only the athlete pays for its own
    /// convergence.
    /// </summary>
    public static class PhysicalAthleteSolverProfile
    {
        public const string CalibrationVersion = "GAM11_ATHLETE_SOLVER_PROFILE_V1";
        public const string SourceClass = "GAME_PHYSICS_CALIBRATION";

        /// <summary>
        /// Position iterations. This is what realises drive stiffness; it is
        /// the number the actuator realization contract is measured against.
        /// </summary>
        public const int PositionIterations = 28;

        /// <summary>
        /// Velocity iterations, left at the project's qualified value. The
        /// sweep found it worth about one percent of realised stiffness
        /// between one and eight, so it is not where the convergence problem
        /// was and it is not being changed to chase it.
        /// </summary>
        public const int VelocityIterations = 1;

        /// <summary>
        /// The band a powered family's realised static stiffness has to sit
        /// in, as a fraction of its authored spring.
        /// </summary>
        public const float RealizationToleranceLow = 0.95f;
        public const float RealizationToleranceHigh = 1.05f;

        public static void Apply(UnityEngine.Rigidbody body)
        {
            if (body == null)
                return;
            body.solverIterations = PositionIterations;
            body.solverVelocityIterations = VelocityIterations;
        }
    }
}
