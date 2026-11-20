using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using PowerliftingSimulator.Athlete;
using UnityEngine;

namespace PowerliftingSimulator.Deadlift.Unity
{
    /// <summary>
    /// GAM-70 runtime admission of the offline close-bar reference family.
    ///
    /// Preparation (before the attempt is armed): the artifact's fingerprint is
    /// compared with the live declared model; each recorded reference is rebuilt
    /// on its recorded anchor state and re-checked against the generator's own
    /// closure and swept-clearance contract; the Gauss-Newton gain of the
    /// closed-chain objective is factorized once per key (central differences,
    /// the solver's own step).
    ///
    /// Admission (at bar separation and at knee passage, inside one physics
    /// tick): the measured plant is captured as an anchor. An identical anchor
    /// takes the generated keys unchanged. Otherwise every key is corrected
    /// from its generated configuration by a bounded fixed-gain Gauss-Newton
    /// iteration of the same objective, evaluated on the measured anchor; a
    /// step is taken only while it lowers that objective. The corrected family
    /// is admitted only if it meets the same contract the generator enforced
    /// (planted feet and welded hands closed within 1 mm and 0.3 deg at every
    /// interpolated sample, nearest shaft-leg gap above 20 mm for the ascent,
    /// positive conservative swept clearance). A rejected state is reported and
    /// never forced onto the cache.
    /// </summary>
    public sealed class DeadliftReferenceAdmission
    {
        /// <summary>Bound on the correction iterations per key (one objective evaluation each).</summary>
        public const int MaxCorrectionIterations = 12;
        private const double FiniteDifferenceStep = 1e-5;   // BenchKinematics.LevenbergMarquardt's step
        private const double GainDamping = 1e-6;

        public enum Outcome { Exact, Corrected, Rejected }

        public sealed class Result
        {
            public Outcome Outcome;
            public string Reason = string.Empty;
            public DeadliftPreparedAscentPlan Plan;
            public double Milliseconds;
            public int Evaluations;
            public float MaxCorrectionDeg, MaxCorrectionM;
            public float MinGapM, SweptGapM, ClosureM, ClosureDeg;

            public string Describe() => string.Format(CultureInfo.InvariantCulture,
                "{0}{1}: {2:F2} ms, {3} objective evaluations, max correction {4:F3} deg / {5:F2} mm, min gap {6:F2} mm, swept {7:F2} mm, closure {8:F3} mm / {9:F3} deg",
                Outcome, string.IsNullOrEmpty(Reason) ? string.Empty : " (" + Reason + ")", Milliseconds, Evaluations,
                MaxCorrectionDeg, MaxCorrectionM * 1000f, MinGapM * 1000f, SweptGapM * 1000f, ClosureM * 1000f, ClosureDeg);
        }

        internal sealed class Family
        {
            public DeadliftCloseBarReference.Plan Record;
            public DeadliftPreparedAscentPlan Generated;
            public double[][,] Gains;
            public (float MinGap, float SweptGap, float PositionError, float RotationError) Check;
        }

        private readonly Family _prefix, _postKnee;

        public DeadliftCloseBarReference Reference { get; }
        public string Fingerprint { get; }
        public double PreparationMilliseconds { get; }
        public Result PrefixResult { get; private set; }
        public Result PostKneeResult { get; private set; }

        private DeadliftReferenceAdmission(DeadliftCloseBarReference reference, string fingerprint, Family prefix, Family postKnee, double ms)
        {
            Reference = reference;
            Fingerprint = fingerprint;
            _prefix = prefix;
            _postKnee = postKnee;
            PreparationMilliseconds = ms;
        }

        /// <summary>Validates the artifact against the live model and factorizes its gains. Null with a reason on a miss.</summary>
        public static DeadliftReferenceAdmission Prepare(DeadliftAttemptRuntime runtime, PhysicalAthleteRig rig,
            DeadliftCloseBarReference reference, out string failure)
        {
            var clock = Stopwatch.StartNew();
            failure = null;
            if (reference == null)
            {
                failure = "no reference artifact";
                return null;
            }
            if (reference.Contract != DeadliftCloseBarReference.ContractVersion)
            {
                failure = $"artifact contract {reference.Contract} is not {DeadliftCloseBarReference.ContractVersion}";
                return null;
            }
            string fingerprint = runtime.ModelFingerprint ?? DeadliftCloseBarReference.ComputeFingerprint(rig, runtime.Bar);
            if (Environment.GetEnvironmentVariable("GAM70_FINGERPRINT_DUMP") == "1")
            {
                DeadliftCloseBarReference.ComputeFingerprint(rig, runtime.Bar, out string dump);
                UnityEngine.Debug.Log("GAM-70 fingerprint dump " + fingerprint + ": " + dump);
            }
            if (!string.Equals(fingerprint, reference.Fingerprint, StringComparison.Ordinal))
            {
                DeadliftCloseBarReference.ComputeFingerprint(rig, runtime.Bar, out string source);
                UnityEngine.Debug.LogWarning("GAM-70 live model fingerprint source: " + source);
                failure = $"model fingerprint {fingerprint.Substring(0, 12)} differs from the artifact's {Short(reference.Fingerprint)}: regenerate the reference";
                return null;
            }
            if (reference.JointIds == null || !rig.PoweredController.Joints.Select(j => j.Id).SequenceEqual(reference.JointIds))
            {
                failure = "artifact joint order differs from the live powered joints";
                return null;
            }

            Family prefix = Rebuild(runtime, rig, reference.Prefix, false, out failure);
            if (prefix == null)
                return null;
            Family postKnee = Rebuild(runtime, rig, reference.PostKnee, true, out failure);
            if (postKnee == null)
                return null;
            return new DeadliftReferenceAdmission(reference, fingerprint, prefix, postKnee, clock.Elapsed.TotalMilliseconds);
        }

        private static string Short(string s) => string.IsNullOrEmpty(s) ? "(none)" : s.Substring(0, Math.Min(12, s.Length));

        private static Family Rebuild(DeadliftAttemptRuntime runtime, PhysicalAthleteRig rig, DeadliftCloseBarReference.Plan record,
            bool continuation, out string failure)
        {
            failure = null;
            DeadliftPreparedAscentPlan plan;
            try
            {
                plan = Construct(runtime, rig, continuation, record.Lockout, record.State);
                plan.UseKeys(record.Keys);
            }
            catch (Exception e)
            {
                failure = (continuation ? "post-knee" : "ascent") + " reference does not fit the live model: " + e.Message;
                return null;
            }
            var check = plan.MeasureInterpolation();
            if (!Meets(check, continuation, out string why))
            {
                failure = (continuation ? "post-knee" : "ascent") + " reference fails its contract on the live model: " + why;
                return null;
            }

            var gains = new double[plan.KeyCount][,];
            for (int i = 1; i < plan.KeyCount; i++)
                gains[i] = Gain(plan, i, plan.Key(i));
            return new Family { Record = record, Generated = plan, Gains = gains, Check = check };
        }

        private static DeadliftPreparedAscentPlan Construct(DeadliftAttemptRuntime runtime, PhysicalAthleteRig rig, bool continuation,
            DeadliftPreparedAscentPlan.Sample lockout, DeadliftPreparedAscentPlan.Anchor state) =>
            // The ascent prefix is the shipped close-lockout configuration: through knee, not complete, not centered.
            new DeadliftPreparedAscentPlan(runtime, rig, throughKnee: true, complete: false, centered: false,
                endProgress: DeadliftPreparedRejoin.EndProgress, incremental: true, lockout: continuation ? lockout : null, state: state);

        /// <summary>The generator's acceptance contract (DeadliftPreparedRejoin / DeadliftPreparedCloseLockout).</summary>
        internal static bool Meets((float MinGap, float SweptGap, float PositionError, float RotationError) check, bool continuation, out string why)
        {
            why = null;
            if (!continuation && !(check.MinGap > 0.020f))
                why = $"nearest planned shaft gap {check.MinGap * 1000f:F2} mm is not above 20 mm";
            else if (!(check.SweptGap > 0f))
                why = $"conservative swept clearance {check.SweptGap * 1000f:F2} mm is not positive";
            else if (!(check.PositionError < 0.001f))
                why = $"chain closure {check.PositionError * 1000f:F3} mm is not below 1 mm";
            else if (!(check.RotationError < 0.3f))
                why = $"chain closure {check.RotationError:F3} deg is not below 0.3 deg";
            return why == null;
        }

        private static double[,] Gain(DeadliftPreparedAscentPlan plan, int index, DeadliftPreparedAscentPlan.Sample seed)
        {
            int n = plan.ParameterCount;
            var nominal = plan.NominalAt(index);
            var p = new double[n];
            List<double> r0 = plan.AnchoredResiduals(seed, p, index, nominal);
            int m = r0.Count;
            var jac = new double[m, n];
            for (int j = 0; j < n; j++)
            {
                p[j] = FiniteDifferenceStep;
                List<double> rp = plan.AnchoredResiduals(seed, p, index, nominal);
                p[j] = -FiniteDifferenceStep;
                List<double> rm = plan.AnchoredResiduals(seed, p, index, nominal);
                p[j] = 0d;
                for (int i = 0; i < m; i++)
                    jac[i, j] = (rp[i] - rm[i]) / (2d * FiniteDifferenceStep);
            }
            var a = new double[n, n];
            for (int x = 0; x < n; x++)
                for (int y = 0; y < n; y++)
                {
                    double sum = 0d;
                    for (int i = 0; i < m; i++)
                        sum += jac[i, x] * jac[i, y];
                    a[x, y] = sum;
                }
            for (int x = 0; x < n; x++)
                a[x, x] += GainDamping * (1d + a[x, x]);
            double[,] inverse = Invert(a);
            var gain = new double[n, m];
            for (int x = 0; x < n; x++)
                for (int i = 0; i < m; i++)
                {
                    double sum = 0d;
                    for (int y = 0; y < n; y++)
                        sum += inverse[x, y] * jac[i, y];
                    gain[x, i] = sum;
                }
            return gain;
        }

        private static double[,] Invert(double[,] a)
        {
            int n = a.GetLength(0);
            var m = (double[,])a.Clone();
            var inv = new double[n, n];
            for (int i = 0; i < n; i++)
                inv[i, i] = 1d;
            for (int col = 0; col < n; col++)
            {
                int pivot = col;
                for (int row = col + 1; row < n; row++)
                    if (Math.Abs(m[row, col]) > Math.Abs(m[pivot, col]))
                        pivot = row;
                if (Math.Abs(m[pivot, col]) < 1e-14)
                    throw new InvalidOperationException("The reference objective is singular at a generated key.");
                for (int k = 0; k < n; k++)
                {
                    (m[col, k], m[pivot, k]) = (m[pivot, k], m[col, k]);
                    (inv[col, k], inv[pivot, k]) = (inv[pivot, k], inv[col, k]);
                }
                double d = m[col, col];
                for (int k = 0; k < n; k++)
                {
                    m[col, k] /= d;
                    inv[col, k] /= d;
                }
                for (int row = 0; row < n; row++)
                {
                    if (row == col)
                        continue;
                    double f = m[row, col];
                    if (f == 0d)
                        continue;
                    for (int k = 0; k < n; k++)
                    {
                        m[row, k] -= f * m[col, k];
                        inv[row, k] -= f * inv[col, k];
                    }
                }
            }
            return inv;
        }

        /// <summary>
        /// One admission in progress. Keys are corrected in order and each interval is
        /// checked against the contract as soon as both of its keys exist, a bounded
        /// number of keys per physics tick. <see cref="AdmittedProgress"/> is the
        /// furthest lift progress whose reference is corrected and checked; the pull
        /// controller never commands progress beyond it, and stops short of it within
        /// its existing acceleration bound. Any failed key or interval rejects the
        /// whole family.
        /// </summary>
        public sealed class Session
        {
            private readonly Family _family;
            private readonly bool _continuation;
            private readonly Stopwatch _clock = new Stopwatch();
            private DeadliftPreparedAscentPlan.InterpolationCheck _check = DeadliftPreparedAscentPlan.InterpolationCheck.Empty;
            private int _nextKey;

            public Result Result { get; } = new Result();
            public DeadliftPreparedAscentPlan Plan => Result.Plan;
            public bool Complete { get; private set; }
            public bool Rejected => Result.Outcome == Outcome.Rejected;
            public int Ticks { get; private set; }
            public double MaxTickMilliseconds { get; private set; }

            /// <summary>Furthest progress the corrected and checked reference covers with a full interval to spare.</summary>
            public float AdmittedProgress => Complete ? 1f : _check.Intervals < 2 ? Plan.KeyProgress(0) : Plan.KeyProgress(_check.Intervals - 1);

            internal Session(DeadliftAttemptRuntime runtime, PhysicalAthleteRig rig, Family family, bool continuation)
            {
                _family = family;
                _continuation = continuation;
                _clock.Start();
                DeadliftPreparedAscentPlan live;
                try
                {
                    live = Construct(runtime, rig, continuation, family.Record.Lockout, null);
                }
                catch (Exception e)
                {
                    Result.Outcome = Outcome.Rejected;
                    Result.Reason = e.Message;
                    Finish();
                    return;
                }
                Result.Plan = live;

                if (live.State.IdenticalTo(family.Record.State))
                {
                    // An identical anchor carries the identical keys the preparation stage already checked.
                    live.UseKeys(family.Record.Keys);
                    Result.Outcome = Outcome.Exact;
                    _check = InterpolationCheckOf(family.Check, live.IntervalCount);
                    Complete = true;
                    Record();
                    Finish();
                    return;
                }
                if (live.KeyCount != family.Generated.KeyCount)
                {
                    Result.Outcome = Outcome.Rejected;
                    Result.Reason = $"the measured {(continuation ? "knee-passage progress" : "plant")} needs {live.KeyCount} reference keys, the qualified family has {family.Generated.KeyCount}";
                    Finish();
                    return;
                }
                Result.Outcome = Outcome.Corrected;
                // Key 0 is the measured plant itself, exactly as the generator's first key.
                live.SetKey(0, live.AnchoredPose(MeasuredSeed(live), new double[live.ParameterCount], live.KeyProgress(0)));
                _nextKey = 1;
                _clock.Stop();
            }

            private static DeadliftPreparedAscentPlan.InterpolationCheck InterpolationCheckOf(
                (float MinGap, float SweptGap, float PositionError, float RotationError) c, int intervals) =>
                new DeadliftPreparedAscentPlan.InterpolationCheck
                {
                    MinGap = c.MinGap, MinBound = c.SweptGap, MaxClosure = c.PositionError, MaxRotation = c.RotationError, Intervals = intervals
                };

            /// <summary>Corrects and checks up to <paramref name="keys"/> more keys. Returns true when finished (admitted or rejected).</summary>
            public bool Advance(int keys)
            {
                if (Complete || Rejected)
                    return true;
                long start = _clock.ElapsedTicks;
                _clock.Start();
                DeadliftPreparedAscentPlan live = Result.Plan;
                for (int k = 0; k < keys && _nextKey < live.KeyCount; k++, _nextKey++)
                {
                    CorrectKey(live, _family, _nextKey, Result);
                    live.MeasureInterval(_nextKey - 1, ref _check);
                    if (!Meets(_check.Result, _continuation, out string why))
                    {
                        Result.Outcome = Outcome.Rejected;
                        Result.Reason = why;
                        break;
                    }
                }
                if (!Rejected && _nextKey == live.KeyCount)
                    Complete = true;
                _clock.Stop();
                Ticks++;
                MaxTickMilliseconds = Math.Max(MaxTickMilliseconds, (_clock.ElapsedTicks - start) * 1000d / Stopwatch.Frequency);
                Record();
                if (Complete || Rejected)
                    Finish();
                return Complete || Rejected;
            }

            /// <summary>Advances until the checked reference covers <paramref name="progress"/> (or finishes).</summary>
            public void AdvanceTo(float progress)
            {
                while (!Complete && !Rejected && AdmittedProgress < progress)
                    Advance(1);
            }

            private void Record()
            {
                Result.MinGapM = _check.MinGap;
                Result.SweptGapM = _check.MinBound;
                Result.ClosureM = _check.MaxClosure;
                Result.ClosureDeg = _check.MaxRotation;
            }

            private void Finish()
            {
                _clock.Stop();
                Result.Milliseconds = _clock.Elapsed.TotalMilliseconds;
            }
        }

        /// <summary>Begins admitting the ascent reference on the plant measured at bar separation.</summary>
        public Session BeginAscent(DeadliftAttemptRuntime runtime, PhysicalAthleteRig rig)
        {
            var session = new Session(runtime, rig, _prefix, false);
            PrefixResult = session.Result;
            return session;
        }

        /// <summary>Begins admitting the post-knee continuation on the plant measured at knee passage.</summary>
        public Session BeginPostKnee(DeadliftAttemptRuntime runtime, PhysicalAthleteRig rig)
        {
            var session = new Session(runtime, rig, _postKnee, true);
            PostKneeResult = session.Result;
            return session;
        }

        /// <summary>
        /// Lift progress the admitted reference must cover before it is installed: the current
        /// progress plus the distance the acceleration-bounded progress rate needs to stop.
        /// </summary>
        public static float StoppingHorizon(DeadliftPullController pull)
        {
            float v = Mathf.Max(0f, pull.LiftRate01PerSecond);
            return Mathf.Min(1f, pull.LiftProgress01 + v * v / (2f * pull.LiftAccelPerSecond2));
        }

        private static void CorrectKey(DeadliftPreparedAscentPlan live, Family family, int index, Result result)
        {
            int n = live.ParameterCount;
            DeadliftPreparedAscentPlan.Sample seed = family.Generated.Key(index);
            double[,] gain = family.Gains[index];
            var nominal = live.NominalAt(index);
            var x = new double[n];
            List<double> r = live.AnchoredResiduals(seed, x, index, nominal);
            result.Evaluations++;
            double cost = Cost(r);
            for (int iteration = 0; iteration < MaxCorrectionIterations; iteration++)
            {
                var candidate = new double[n];
                int m = r.Count;
                for (int a = 0; a < n; a++)
                {
                    double sum = 0d;
                    for (int i = 0; i < m; i++)
                        sum += gain[a, i] * r[i];
                    candidate[a] = x[a] - sum;
                }
                List<double> rc = live.AnchoredResiduals(seed, candidate, index, nominal);
                result.Evaluations++;
                double candidateCost = Cost(rc);
                if (!(candidateCost < cost))
                    break;
                x = candidate;
                r = rc;
                cost = candidateCost;
            }
            DeadliftPreparedAscentPlan.Sample corrected = live.AnchoredPose(seed, x, live.KeyProgress(index));
            result.MaxCorrectionM = Mathf.Max(result.MaxCorrectionM, Vector3.Distance(corrected.Root.position, seed.Root.position));
            foreach (KeyValuePair<string, Quaternion> q in corrected.Logical)
                if (seed.Logical.TryGetValue(q.Key, out Quaternion s))
                    result.MaxCorrectionDeg = Mathf.Max(result.MaxCorrectionDeg, Quaternion.Angle(s, q.Value));
            live.SetKey(index, corrected);
        }

        private static DeadliftPreparedAscentPlan.Sample MeasuredSeed(DeadliftPreparedAscentPlan live)
        {
            var logical = new Dictionary<string, Quaternion>(StringComparer.Ordinal);
            for (int i = 0; i < live.State.JointIds.Length; i++)
                logical[live.State.JointIds[i]] = live.State.Measured[i];
            return new DeadliftPreparedAscentPlan.Sample { Root = live.State.Root, Logical = logical };
        }

        private static double Cost(List<double> r)
        {
            double sum = 0d;
            foreach (double v in r)
                sum += v * v;
            return sum;
        }
    }
}
