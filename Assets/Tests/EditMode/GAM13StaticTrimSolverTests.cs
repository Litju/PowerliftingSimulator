using NUnit.Framework;
using PowerliftingSimulator.Squat;

namespace PowerliftingSimulator.Tests
{
    public sealed class GAM13StaticTrimSolverTests
    {
        [Test]
        public void Bounded_solver_finds_deterministic_five_channel_trim()
        {
            var solver = new SquatStaticTrimSolver();
            var options = new SquatStaticTrimSolver.Options
            {
                MaximumIterations = 12,
                ResidualTolerance = 1e-4,
                MaximumStep = 4.0
            };
            double[] target = { 2.0, -3.0, 4.0, -5.0, 6.0 };
            double[] initial = { 10.0, 10.0, -10.0, 10.0, -10.0 };

            SquatStaticTrimSolver.Result first = solver.Solve(
                candidate => Residual(candidate, target), initial, options);
            SquatStaticTrimSolver.Result second = solver.Solve(
                candidate => Residual(candidate, target), initial, options);

            Assert.That(first.Converged, Is.True,
                $"candidate=[{string.Join(",", first.Candidate)}] norm={first.FinalNorm} iterations={first.Iterations} evaluations={first.Evaluations}");
            Assert.That(first.FinalNorm, Is.LessThan(1e-4));
            Assert.That(first.Candidate, Is.EqualTo(target).Within(1e-4));
            Assert.That(first.Candidate, Is.EqualTo(second.Candidate).Within(1e-12));
            Assert.That(first.Residual, Is.EqualTo(second.Residual).Within(1e-12));
        }

        [Test]
        public void Solver_enforces_the_twelve_degree_investigation_bound()
        {
            var solver = new SquatStaticTrimSolver();
            var options = new SquatStaticTrimSolver.Options
            {
                MaximumIterations = 12,
                ResidualTolerance = 1e-8,
                LowerBound = -12.0,
                UpperBound = 12.0
            };

            SquatStaticTrimSolver.Result result = solver.Solve(
                candidate => Residual(candidate, new[] { 20.0, -20.0, 20.0, -20.0, 20.0 }),
                new double[SquatStaticTrimSolver.Dimension],
                options);

            for (int index = 0; index < result.Candidate.Length; index++)
                Assert.That(result.Candidate[index], Is.InRange(-12.0, 12.0));
            Assert.That(result.Converged, Is.False);
        }

        private static double[] Residual(double[] candidate, double[] target)
        {
            var residual = new double[candidate.Length];
            for (int index = 0; index < candidate.Length; index++)
                residual[index] = candidate[index] - target[index];
            return residual;
        }
    }
}
