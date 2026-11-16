namespace PowerliftingSimulator.Deadlift.Unity
{
    /// <summary>
    /// GAM-70: the qualified deadlift initial condition shipped to the game -
    /// the B2 sumo pronated candidate accepted by the independent A2 table
    /// (DeadliftSetupFeasibilityTests / DeadliftSetupAcceptance), solver joint
    /// margin 3 deg. The PlayMode suite re-verifies this exact vector against
    /// the acceptance table before every physical run.
    /// </summary>
    public static class DeadliftQualifiedSetup
    {
        public const float BarKg = 75f;

        public static readonly double[] B2 = { 0.44873253253505496,-0.33476850625538729,0.35469749710446047,0.60197627522658725,0.095005754191138964,8.1474500898589467E-86,-2.1297258359899325,-0.64981731689105693,-0.63155432879916118,2.0765302440179285,-0.17968501923679961,-2.1297325168325476,0.64980317747623928,0.63154202482236887,2.07652312414234,-0.17967646160892861,0.22568810829812166,1.7143159013914762,-0.5061553689509265,0.01354031767628883,-0.034157440961880739,0.11136849893022829,0.00025151355487624031,-0.22573335661705871,-1.7143017984473989,-0.50616384793256242,0.013510471990099304,0.034374300362708721,-0.11136254286841699,0.00018716187426342558,-0.012886743009090037 };

        /// <summary>Resting height of the bar axis on its 24-facet plates, facet flat (Gate A1).</summary>
        public const float BarAxisRestY = 0.223075f;

        public static DeadliftSetupSpec SumoPronated() => new DeadliftSetupSpec
        {
            Label = "B2 sumo, pronated",
            BarAxisY = BarAxisRestY, BarZ = 0.02f, BarRadius = DeadliftSetupSolver.AnchorBarRadius,
            StanceHalfWidth = 0.35f, ToeOutDeg = 42f, GripHalfWidth = 0.235f,
            TorsoAboveHorizontalDeg = 42f, NeutralSpineTotalFlexionDeg = 25f,
            UseClearances = true, RandomStarts = 0, IncludeStandingSeed = true, ArmStraightnessWeight = 3f,
            CorrectedHandFrame = true, Grip = DeadliftGripStyle.Pronated, Technique = DeadliftTechnique.Sumo,
            FreeStance = false, FreeForeAft = true, Support = DeadliftSupportMode.GrippedGroundedBar, BarMassKg = BarKg,
            SupportMarginM = 0.01f, BarLimbClearanceM = 0f, RequiredJointMarginDeg = 3f
        };
    }
}
