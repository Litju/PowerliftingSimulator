using System;
using System.Globalization;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Foundation;

namespace PowerliftingSimulator.PhysicsBenchmarks
{
    /// <summary>
    /// Production numerical settings, read from their single authorities, and
    /// the GAM-50 convergence sweep grid. Isolated benchmarks gate at
    /// production settings and characterise the sweep around them.
    /// </summary>
    public static class PhysicsBenchmarkSettings
    {
        public static float ProductionDt => (float)SimulationConstants.FixedDeltaTimeSeconds;
        public static int ProductionPositionIterations => PhysicalAthleteSolverProfile.PositionIterations;
        public static int ProductionVelocityIterations => PhysicalAthleteSolverProfile.VelocityIterations;

        public static readonly float[] SweepDt = { 0.020f, 0.010f, 0.005f };
        public static readonly int[] SweepPositionIterations = { 14, 28, 56 };
        public static readonly int[] SweepVelocityIterations = { 1, 2, 4 };

        public static IsolatedPhysicsWorld ProductionWorld(string name) =>
            new IsolatedPhysicsWorld(name, ProductionDt, ProductionPositionIterations, ProductionVelocityIterations);

        public static string Cfg(string label, IsolatedPhysicsWorld world) => label + ";" + world.Describe();

        public static string Inv(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        public const double G = 9.81;

        public static double Float32AccumulationBound(double magnitude, int steps) =>
            Math.Max(1e-6, magnitude * 1.2e-7 * Math.Max(1, steps) * 4.0);
    }
}
