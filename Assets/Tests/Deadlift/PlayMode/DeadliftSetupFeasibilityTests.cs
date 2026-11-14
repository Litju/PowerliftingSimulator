using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Deadlift.Unity;
using UnityEngine;
using UnityEngine.TestTools;
using F = PowerliftingSimulator.Deadlift.IsolatedBarWorld;

namespace PowerliftingSimulator.Deadlift
{
    /// <summary>
    /// GAM-70 Gate A2 bounded A/B qualification ladder. Every pose is scored by
    /// the independent <see cref="DeadliftSetupAcceptance"/> table; a solver
    /// residual is never acceptance.
    ///
    /// Control A: the legacy ladder (DeadliftSetupQualificationTests,
    /// DeadliftSetupObservationAuditTests), unchanged.
    /// B0: the legacy solver with ONLY the hand observation corrected (true
    /// index-to-little MCP line), with the grip style held explicit: pronated is
    /// the legacy target set; supinated is what the uncommitted 2026-11-12
    /// "correction" silently switched to.
    /// B1: corrected hard-constraint solver, conventional candidate (0.15 m
    /// stance half-width, 12 deg toe-out, grip outside the knees), feet-to-bar
    /// fore-aft free, gripped-grounded-bar support.
    /// </summary>
    public sealed class DeadliftSetupFeasibilityTests
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;
        private DeadliftPlantWorld _world;

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (_world != null)
            {
                yield return _world.Teardown();
                _world = null;
            }
        }

        private static DeadliftSetupSpec LegacyRung(string label, bool balance, bool torso, bool spine, bool arm, bool clear, float margin, int starts, DeadliftGripStyle grip)
            => new DeadliftSetupSpec
            {
                Label = label, UseBalance = balance, UseTorsoAngle = torso, UseNeutralSpine = spine, UseArmPriors = arm,
                UseClearances = clear, LimitMarginDeg = margin, RequireKnuckleLineAlongShaft = true, RandomStarts = starts,
                CorrectedHandFrame = true, Grip = grip
            };

        internal static DeadliftSetupSpec Candidate(string label, DeadliftGripStyle grip, DeadliftTechnique technique,
            float stanceHalfWidth, float toeOutDeg, float gripHalfWidth)
            => new DeadliftSetupSpec
            {
                Label = label,
                BarAxisY = DeadliftSetupAcceptance.BarAxisRestY, BarZ = 0.02f, BarRadius = DeadliftSetupSolver.AnchorBarRadius,
                StanceHalfWidth = stanceHalfWidth, ToeOutDeg = toeOutDeg, GripHalfWidth = gripHalfWidth,
                TorsoAboveHorizontalDeg = 42f, NeutralSpineTotalFlexionDeg = 25f,
                UseClearances = true, RandomStarts = 0, IncludeStandingSeed = true, ArmStraightnessWeight = 3f,
                CorrectedHandFrame = true, Grip = grip, Technique = technique,
                FreeStance = false, FreeForeAft = true, Support = DeadliftSupportMode.GrippedGroundedBar, BarMassKg = 75f,
                SupportMarginM = 0.01f, BarLimbClearanceM = 0f, RequiredJointMarginDeg = 1f
            };

        internal static DeadliftSetupSpec ConventionalPronated() =>
            Candidate("B1 conventional, pronated", DeadliftGripStyle.Pronated, DeadliftTechnique.Conventional, 0.15f, 12f, 0.30f);

        private static float Row(DeadliftSetupAcceptance a, string startsWith)
            => (float)(a.Rows.FirstOrDefault(r => r.Quantity.StartsWith(startsWith))?.Measured ?? double.NaN);

        private static float MinMargin(DeadliftSetupAcceptance a, string contains)
            => (float)a.Rows.Where(r => r.Quantity.Contains(contains) && r.Class == ConstraintClass.Hard).Select(r => r.Margin).DefaultIfEmpty(double.NaN).Min();

        private static string Line(string label, DeadliftPlantModel m, DeadliftSetupAcceptance a)
        {
            JointMargin J(string id) => m.Margins.First(x => x.Joint == id);
            string f = string.Join(", ", a.Failures.Select(r => r.Quantity).Take(14));
            return string.Format(C,
                "{0} | {1}/{2} | {3:F1}/{4:F1} | {5:F1}/{6:F1} | {7:F1}/{8:F1}, {9:F1}/{10:F1} | {11:F1}/{12:F1}, {13:F1}/{14:F1} | {15:F1}/{16:F1} | {17:F1} | {18:F1} | {19:F2}/{20:F2} | {21:F1} | {22:F1}/{23:F1} | {24} fails: {25}",
                label, a.GripStyleMeasuredLeft, a.GripStyleMeasuredRight,
                Row(a, "left legacy proxy"), Row(a, "right legacy proxy"),
                Row(a, "left TRUE"), Row(a, "right TRUE"),
                J("left_hand").TwistDeg, J("left_hand").SwingConeDeg, J("right_hand").TwistDeg, J("right_hand").SwingConeDeg,
                J("left_upper_arm").TwistDeg, J("left_upper_arm").SwingConeDeg, J("right_upper_arm").TwistDeg, J("right_upper_arm").SwingConeDeg,
                Mathf.Min(J("left_thigh").TwistMarginDeg, J("right_thigh").TwistMarginDeg), Mathf.Min(J("left_thigh").SwingMarginDeg, J("right_thigh").SwingMarginDeg),
                Mathf.Min(J("left_shank").TwistMarginDeg, J("right_shank").TwistMarginDeg),
                Mathf.Min(J("left_foot").TwistMarginDeg, J("right_foot").TwistMarginDeg),
                Row(a, "left sole lowest") * 1000f, Row(a, "left sole corners level") * 1000f,
                MinMargin(a, "bar shaft vs") * 1000f - 0.5f,
                a.ModeAMarginM * 1000f, a.ModeBMarginM * 1000f,
                a.Failures.Count(), f);
        }

        private const string Header =
            "candidate | grip measured L/R | proxy deg L/R | TRUE MCP deg L/R | wrist prim/cone L, R | shoulder prim/cone L, R | hip min margin prim/cone | knee min margin | ankle min margin | L sole min y / spread mm | min bar-leg gap mm | COP margin mm modeA/modeB | counted failures";

        /// <summary>
        /// B0: does correcting only the hand observation change the legacy result?
        /// The grip style is held explicit so a style switch cannot masquerade as
        /// an observation fix.
        /// </summary>
        [Timeout(3600000)]
        [UnityTest]
        public IEnumerator A2_B0_LEGACY_LADDER_WITH_ONLY_THE_TRUE_MCP_LINE_SEPARATES_OBSERVATION_FROM_GRIP_STYLE()
        {
            yield return DeadliftPlantWorld.Load();
            _world = DeadliftPlantWorld.Current;
            var table = new StringBuilder();
            var details = new StringBuilder();
            table.AppendLine(Header);
            var results = new Dictionary<string, (DeadliftPlantModel m, DeadliftSetupAcceptance a)>();
            foreach (DeadliftGripStyle grip in new[] { DeadliftGripStyle.Pronated, DeadliftGripStyle.Supinated })
            {
                var warm = new List<double[]>();
                foreach (DeadliftSetupSpec spec in new[]
                         {
                             LegacyRung("B0 R0 hard + TRUE MCP, " + grip, false, false, false, false, false, 0f, 80, grip),
                             LegacyRung("B0 R2 R0 + COM midfoot + neutral spine, " + grip, true, false, true, false, false, 0f, 20, grip),
                             LegacyRung("B0 R4 R2 + arm priors, torso, 4 deg, clearances, " + grip, true, true, true, true, true, 4f, 20, grip)
                         })
                {
                    spec.WarmStarts.AddRange(warm);
                    DeadliftPlantModel m = _world.SolveSetup(spec);
                    warm.Add(m.Solution);
                    DeadliftSetupAcceptance a = DeadliftSetupAcceptance.Check(_world.Rig, _world.Bar, _world.Solver, m);
                    results[spec.Label] = (m, a);
                    table.AppendLine(Line(spec.Label, m, a));
                    details.AppendLine("=== " + spec.Label + " ===");
                    details.AppendLine(m.Describe());
                    details.AppendLine(a.Table());
                }
            }
            F.WriteEvidence("gateA2-B0-table.txt", table.ToString());
            F.WriteEvidence("gateA2-B0-details.txt", details.ToString());
            Debug.Log(table.ToString());
            Assert.That(results.Count, Is.EqualTo(6), table.ToString());
            // Measured facts (2026-11-12): with ONLY the true MCP line, the pronated
            // overhand hand aligns (0.0 deg) with wrists far from their limits, so
            // the legacy wrist binding came from the proxy observation; with a
            // neutral spine the legacy stance still drives the shins into the bar
            // for both grip styles, so that residual failure is leg/reach, not hand.
            var r0p = results["B0 R0 hard + TRUE MCP, Pronated"];
            JointMargin Wrist(DeadliftPlantModel m, string id) => m.Margins.First(x => x.Joint == id);
            Assert.That(Row(r0p.a, "left TRUE"), Is.LessThan(1f), table.ToString());
            Assert.That(Row(r0p.a, "right TRUE"), Is.LessThan(1f), table.ToString());
            foreach (string w in new[] { "left_hand", "right_hand" })
            {
                Assert.That(Wrist(r0p.m, w).TwistMarginDeg, Is.GreaterThan(5f), table.ToString());
                Assert.That(Wrist(r0p.m, w).SwingMarginDeg, Is.GreaterThan(5f), table.ToString());
            }
            foreach (string style in new[] { "Pronated", "Supinated" })
                Assert.That(MinMargin(results["B0 R2 R0 + COM midfoot + neutral spine, " + style].a, "bar shaft vs") - 0.0005f, Is.LessThan(-0.05f), table.ToString());
            yield return null;
        }

        /// <summary>
        /// B1: the conventional candidate under the corrected hard constraints and
        /// the independent acceptance table. This is the A2 kinematic acceptance.
        /// </summary>
        [Timeout(3600000)]
        [UnityTest]
        public IEnumerator A2_B1_CONVENTIONAL_CANDIDATE_IS_REJECTED_BY_ARM_KNEE_INTERFERENCE()
        {
            yield return DeadliftPlantWorld.Load();
            _world = DeadliftPlantWorld.Current;
            var feasibility = new DeadliftSetupFeasibilitySolver(_world.Rig);
            var table = new StringBuilder(Header + "\n");
            var details = new StringBuilder();
            DeadliftSetupAcceptance pronated = null;

            // Seeds. The first B1 pronated solve (2026-11-12 run p1) converged to
            // a non-physical local minimum (pelvis below the floor) from the
            // generic seeds, which disproves nothing. The pronated arm solution of
            // B0 R0 and the converged B1s legs/trunk are added as warm starts;
            // warm starts change where the search begins, never what is accepted.
            DeadliftPlantModel armSource = _world.SolveSetup(LegacyRung("seed: B0 R0 pronated arms", false, false, false, false, false, 0f, 80, DeadliftGripStyle.Pronated));
            DeadliftSetupSpec sup = Candidate("B1s conventional, supinated", DeadliftGripStyle.Supinated, DeadliftTechnique.Conventional, 0.15f, 12f, 0.30f);
            DeadliftSetupSpec pro = ConventionalPronated();
            var order = new List<DeadliftSetupSpec> { sup, pro };
            double[] supSolution = null;
            foreach (DeadliftSetupSpec spec in order)
            {
                if (spec == pro && supSolution != null)
                {
                    double[] hybrid = (double[])supSolution.Clone();
                    for (int i = 16; i < 30; i++) hybrid[i] = armSource.Solution[i];
                    spec.WarmStarts.Add(supSolution);
                    spec.WarmStarts.Add(hybrid);
                    spec.WarmStarts.Add(armSource.Solution);
                }
                float t0 = Time.realtimeSinceStartup;
                DeadliftPlantModel m = feasibility.Solve(spec);
                DeadliftSetupAcceptance a = DeadliftSetupAcceptance.Check(_world.Rig, _world.Bar, feasibility.Measurements, m);
                if (spec.Grip == DeadliftGripStyle.Pronated) { pronated = a; s_b1Pronated = m.Solution; }
                else supSolution = m.Solution;
                table.AppendLine(Line(spec.Label, m, a));
                details.AppendLine("=== " + spec.Label + $" (solve {Time.realtimeSinceStartup - t0:F0} s) ===");
                details.AppendLine(m.Describe());
                details.AppendLine(m.Feasibility.Describe());
                details.AppendLine("solution " + string.Join(",", m.Solution.Select(v => v.ToString("R", C))));
                details.AppendLine(a.Table());
            }
            F.WriteEvidence("gateA2-B1-table.txt", table.ToString());
            F.WriteEvidence("gateA2-B1-details.txt", details.ToString());
            Debug.Log(table.ToString());
            // Measured 2026-11-12 (runs p3/p4): rejected. Joints, reach, support and
            // lift-off pass; the hands cannot be outside the knees without the
            // forearms penetrating the shanks. Pins that finding; the global
            // workspace bound (DeadliftReachBoundTests) shows the zero-abduction
            // family has at most 2 deg of joint margin anywhere.
            Assert.That(pronated.Passed, Is.False, "the conventional candidate unexpectedly passed; re-examine the reach bound\n" + table + details);
            Assert.That(pronated.Failures.Any(r => r.Quantity.Contains("lateral of knee surface") || r.Quantity.Contains("arm/leg") || r.Quantity.Contains("body-body")),
                Is.True, "the conventional candidate failed for a different reason than measured\n" + table + details);
            yield return null;
        }

        internal static double[] s_b1Pronated;

        /// <summary>
        /// Sumo candidate (Escamilla et al. 2000, PMID 10912892, sumo means:
        /// ~70 cm stance, ~42 deg toe-out, ~47 cm grip inside the knees). The
        /// numbers define a coherent candidate; they are not avatar limits.
        /// </summary>
        internal static DeadliftSetupSpec SumoPronated() =>
            Candidate("B2 sumo, pronated", DeadliftGripStyle.Pronated, DeadliftTechnique.Sumo, 0.35f, 42f, 0.235f);

        /// <summary>B2: one coherent wide-stance candidate, run because B1 fails for a measured geometric reason.</summary>
        [Timeout(3600000)]
        [UnityTest]
        public IEnumerator A2_B2_SUMO_CANDIDATE_IS_ACCEPTED_OR_NAMES_THE_EXACT_PHYSICAL_LIMIT()
        {
            yield return DeadliftPlantWorld.Load();
            _world = DeadliftPlantWorld.Current;
            var feasibility = new DeadliftSetupFeasibilitySolver(_world.Rig);
            DeadliftSetupSpec spec = SumoPronated();
            if (s_b1Pronated != null)
            {
                spec.WarmStarts.Add(s_b1Pronated);
                // Symmetric hip abduction / external rotation variants of the
                // B1 legs (axis-angle Y and Z components, both sign pairings).
                foreach (int component in new[] { 1, 2 })
                    foreach (double sign in new[] { -1d, 1d })
                    {
                        double[] seed = (double[])s_b1Pronated.Clone();
                        seed[6 + component] += sign * 0.5;
                        seed[11 + component] -= sign * 0.5;
                        spec.WarmStarts.Add(seed);
                    }
            }
            // Margin experiment: the 1 deg solve leaves hip flexion exactly on its
            // requested margin (active). Re-solving with 3 deg on every joint,
            // warm-started, tests whether that stop is forced or only preferred.
            string table = Header + "\n", details = string.Empty;
            DeadliftSetupAcceptance accepted = null;
            double[] previous = null;
            foreach (float margin in new[] { 1f, 3f })
            {
                DeadliftSetupSpec s = margin == 1f ? spec : SumoPronated();
                s.Label = $"B2 sumo, pronated, solver joint margin {margin:F0} deg";
                s.RequiredJointMarginDeg = margin;
                if (previous != null) s.WarmStarts.Add(previous);
                float t0 = Time.realtimeSinceStartup;
                DeadliftPlantModel m = feasibility.Solve(s);
                previous = m.Solution;
                DeadliftSetupAcceptance a = DeadliftSetupAcceptance.Check(_world.Rig, _world.Bar, feasibility.Measurements, m);
                table += Line(s.Label, m, a) + "\n";
                details += $"=== {s.Label} (solve {Time.realtimeSinceStartup - t0:F0} s, warm starts {s.WarmStarts.Count}) ===\n" +
                           m.Describe() + "\n" + m.Feasibility.Describe() + "\nsolution " +
                           string.Join(",", m.Solution.Select(v => v.ToString("R", C))) + "\n" + a.Table() + "\n";
                if (a.Passed) { accepted = a; s_b2Sumo = m.Solution; }
            }
            F.WriteEvidence("gateA2-B2-table.txt", table);
            F.WriteEvidence("gateA2-B2-details.txt", details);
            Debug.Log(table);
            Assert.That(accepted, Is.Not.Null, "independent A2 acceptance of the sumo pronated candidate\n" + table + details);
            yield return null;
        }

        internal static double[] s_b2Sumo;
    }
}
