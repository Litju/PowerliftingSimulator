using System;

namespace PowerliftingSimulator.Squat
{
    /// <summary>
    /// Deterministic bounded solver for the five symmetric standing-bias
    /// channels: ankle, knee, hip, abdomen, and thorax. The evaluator owns
    /// the physical experiment; this type only solves its measured residual.
    /// </summary>
    public sealed class SquatStaticTrimSolver
    {
        public const int Dimension = 5;

        public sealed class Options
        {
            public int MaximumIterations { get; set; } = 8;
            public double FiniteDifferenceStep { get; set; } = 0.5;
            public double InitialDamping { get; set; } = 0.1;
            public double DampingIncrease { get; set; } = 10.0;
            public int MaximumDampingRetries { get; set; } = 4;
            public double MaximumStep { get; set; } = 3.0;
            public double ResidualTolerance { get; set; } = 0.25;
            public double ImprovementTolerance { get; set; } = 1e-4;
            public double LowerBound { get; set; } = -12.0;
            public double UpperBound { get; set; } = 12.0;
        }

        public sealed class Result
        {
            internal Result(
                double[] candidate,
                double[] residual,
                double initialNorm,
                double finalNorm,
                int iterations,
                int evaluations,
                int rejectedSteps,
                bool converged)
            {
                Candidate = (double[])candidate.Clone();
                Residual = (double[])residual.Clone();
                InitialNorm = initialNorm;
                FinalNorm = finalNorm;
                Iterations = iterations;
                Evaluations = evaluations;
                RejectedSteps = rejectedSteps;
                Converged = converged;
            }

            public double[] Candidate { get; }
            public double[] Residual { get; }
            public double InitialNorm { get; }
            public double FinalNorm { get; }
            public int Iterations { get; }
            public int Evaluations { get; }
            public int RejectedSteps { get; }
            public bool Converged { get; }
        }

        public Result Solve(
            Func<double[], double[]> evaluate,
            double[] initialCandidate,
            Options options = null)
        {
            if (evaluate == null)
                throw new ArgumentNullException(nameof(evaluate));
            if (initialCandidate == null || initialCandidate.Length != Dimension)
                throw new ArgumentException("A five-channel candidate is required.", nameof(initialCandidate));

            options = options ?? new Options();
            ValidateOptions(options);

            double[] candidate = ClampCandidate(initialCandidate, options);
            double[] residual = Evaluate(evaluate, candidate, out int evaluations);
            double initialNorm = Rms(residual);
            double norm = initialNorm;
            int rejectedSteps = 0;
            int completedIterations = 0;
            double damping = options.InitialDamping;

            for (int iteration = 0; iteration < options.MaximumIterations; iteration++)
            {
                completedIterations = iteration + 1;
                if (norm <= options.ResidualTolerance)
                    break;

                double[,] jacobian = BuildJacobian(
                    evaluate,
                    candidate,
                    residual,
                    options,
                    ref evaluations);
                bool accepted = false;

                for (int retry = 0; retry <= options.MaximumDampingRetries; retry++)
                {
                    double[] step = SolveDampedLeastSquares(jacobian, residual, damping);
                    LimitStep(step, options.MaximumStep);

                    double[] trial = new double[Dimension];
                    bool moved = false;
                    for (int index = 0; index < Dimension; index++)
                    {
                        trial[index] = Clamp(
                            candidate[index] + step[index],
                            options.LowerBound,
                            options.UpperBound);
                        moved |= Math.Abs(trial[index] - candidate[index]) > 1e-10;
                    }

                    if (!moved)
                    {
                        damping *= options.DampingIncrease;
                        rejectedSteps++;
                        continue;
                    }

                    double[] trialResidual = Evaluate(evaluate, trial, out int trialEvaluations);
                    evaluations += trialEvaluations;
                    double trialNorm = Rms(trialResidual);
                    if (trialNorm + options.ImprovementTolerance < norm)
                    {
                        candidate = trial;
                        residual = trialResidual;
                        norm = trialNorm;
                        damping = Math.Max(options.InitialDamping, damping / options.DampingIncrease);
                        accepted = true;
                        break;
                    }

                    damping *= options.DampingIncrease;
                    rejectedSteps++;
                }

                if (!accepted)
                    break;
            }

            return new Result(
                candidate,
                residual,
                initialNorm,
                norm,
                completedIterations,
                evaluations,
                rejectedSteps,
                norm <= options.ResidualTolerance);
        }

        private static double[,] BuildJacobian(
            Func<double[], double[]> evaluate,
            double[] candidate,
            double[] residual,
            Options options,
            ref int evaluations)
        {
            int residualCount = residual.Length;
            double[,] jacobian = new double[residualCount, Dimension];
            for (int column = 0; column < Dimension; column++)
            {
                double[] perturbed = (double[])candidate.Clone();
                double requested = candidate[column] + options.FiniteDifferenceStep;
                perturbed[column] = Clamp(requested, options.LowerBound, options.UpperBound);
                double actualStep = perturbed[column] - candidate[column];
                if (Math.Abs(actualStep) <= 1e-10)
                {
                    requested = candidate[column] - options.FiniteDifferenceStep;
                    perturbed[column] = Clamp(requested, options.LowerBound, options.UpperBound);
                    actualStep = perturbed[column] - candidate[column];
                }

                if (Math.Abs(actualStep) <= 1e-10)
                    continue;

                double[] perturbedResidual = Evaluate(evaluate, perturbed, out int perturbationEvaluations);
                evaluations += perturbationEvaluations;
                for (int row = 0; row < residualCount; row++)
                    jacobian[row, column] = (perturbedResidual[row] - residual[row]) / actualStep;
            }

            return jacobian;
        }

        private static double[] SolveDampedLeastSquares(double[,] jacobian, double[] residual, double damping)
        {
            int residualCount = residual.Length;
            int rows = residualCount + Dimension;
            double[,] augmented = new double[rows, Dimension];
            double[] rightHandSide = new double[rows];
            for (int row = 0; row < residualCount; row++)
            {
                rightHandSide[row] = -residual[row];
                for (int column = 0; column < Dimension; column++)
                    augmented[row, column] = jacobian[row, column];
            }

            double ridge = Math.Sqrt(Math.Max(0.0, damping));
            for (int index = 0; index < Dimension; index++)
                augmented[residualCount + index, index] = ridge;

            // ponytail: dense QR is bounded to this five-channel offline solve;
            // replace it only if the trim dimension grows materially.
            return SolveByHouseholderQr(augmented, rightHandSide, rows, Dimension);
        }

        private static double[] SolveByHouseholderQr(double[,] matrix, double[] rightHandSide, int rows, int columns)
        {
            double[] b = (double[])rightHandSide.Clone();
            for (int diagonal = 0; diagonal < columns; diagonal++)
            {
                double norm = 0.0;
                for (int row = diagonal; row < rows; row++)
                    norm = Hypot(norm, matrix[row, diagonal]);
                if (norm <= 1e-12)
                    continue;

                double alpha = matrix[diagonal, diagonal] <= 0.0 ? norm : -norm;
                double first = matrix[diagonal, diagonal] - alpha;
                matrix[diagonal, diagonal] = alpha;
                if (Math.Abs(first) <= 1e-12)
                    continue;

                for (int row = diagonal + 1; row < rows; row++)
                    matrix[row, diagonal] /= first;

                double vSquared = 1.0;
                for (int row = diagonal + 1; row < rows; row++)
                    vSquared += matrix[row, diagonal] * matrix[row, diagonal];
                double beta = 2.0 / vSquared;

                for (int column = diagonal + 1; column < columns; column++)
                {
                    double projection = matrix[diagonal, column];
                    for (int row = diagonal + 1; row < rows; row++)
                        projection += matrix[row, diagonal] * matrix[row, column];
                    projection *= beta;
                    matrix[diagonal, column] -= projection;
                    for (int row = diagonal + 1; row < rows; row++)
                        matrix[row, column] -= matrix[row, diagonal] * projection;
                }

                double bProjection = b[diagonal];
                for (int row = diagonal + 1; row < rows; row++)
                    bProjection += matrix[row, diagonal] * b[row];
                bProjection *= beta;
                b[diagonal] -= bProjection;
                for (int row = diagonal + 1; row < rows; row++)
                    b[row] -= matrix[row, diagonal] * bProjection;
            }

            double[] solution = new double[columns];
            for (int row = columns - 1; row >= 0; row--)
            {
                double diagonal = matrix[row, row];
                if (Math.Abs(diagonal) <= 1e-12)
                    continue;
                double value = b[row];
                for (int column = row + 1; column < columns; column++)
                    value -= matrix[row, column] * solution[column];
                solution[row] = value / diagonal;
            }

            return solution;
        }

        private static double[] Evaluate(
            Func<double[], double[]> evaluate,
            double[] candidate,
            out int evaluations)
        {
            evaluations = 1;
            double[] residual = evaluate((double[])candidate.Clone());
            if (residual == null || residual.Length == 0)
                throw new InvalidOperationException("Static-trim residual must be non-empty.");
            for (int index = 0; index < residual.Length; index++)
            {
                if (double.IsNaN(residual[index]) || double.IsInfinity(residual[index]))
                    throw new InvalidOperationException("Static-trim residual must be finite.");
            }
            return residual;
        }

        private static double[] ClampCandidate(double[] candidate, Options options)
        {
            var result = new double[Dimension];
            for (int index = 0; index < Dimension; index++)
                result[index] = Clamp(candidate[index], options.LowerBound, options.UpperBound);
            return result;
        }

        private static void LimitStep(double[] step, double maximumStep)
        {
            double largest = 0.0;
            for (int index = 0; index < step.Length; index++)
                largest = Math.Max(largest, Math.Abs(step[index]));
            if (largest <= maximumStep || largest <= 1e-12)
                return;
            double scale = maximumStep / largest;
            for (int index = 0; index < step.Length; index++)
                step[index] *= scale;
        }

        private static double Rms(double[] values)
        {
            double sum = 0.0;
            for (int index = 0; index < values.Length; index++)
                sum += values[index] * values[index];
            return Math.Sqrt(sum / values.Length);
        }

        private static double Clamp(double value, double lower, double upper) =>
            Math.Max(lower, Math.Min(upper, value));

        private static double Hypot(double a, double b)
        {
            a = Math.Abs(a);
            b = Math.Abs(b);
            if (a < b)
            {
                double swap = a;
                a = b;
                b = swap;
            }
            return a <= 1e-12 ? b : a * Math.Sqrt(1.0 + (b / a) * (b / a));
        }

        private static void ValidateOptions(Options options)
        {
            if (options.MaximumIterations <= 0 ||
                options.FiniteDifferenceStep <= 0.0 ||
                options.InitialDamping < 0.0 ||
                options.DampingIncrease <= 1.0 ||
                options.MaximumDampingRetries < 0 ||
                options.MaximumStep <= 0.0 ||
                options.ResidualTolerance < 0.0 ||
                options.ImprovementTolerance < 0.0 ||
                options.LowerBound >= options.UpperBound)
                throw new ArgumentOutOfRangeException(nameof(options));
        }
    }
}
