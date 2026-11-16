using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Deadlift.Unity;
using PowerliftingSimulator.Foundation;
using UnityEngine;
using UnityEngine.TestTools;
using F = PowerliftingSimulator.Deadlift.IsolatedBarWorld;

namespace PowerliftingSimulator.Deadlift
{
    /// <summary>
    /// GAM-70 Gate 6: the moving phase. After the qualified bilateral bar
    /// break (Gate 5), the player's Drive advances a solved ascent path; the
    /// measured hand-load reference (re-solved at the current posture) and the
    /// ankle regulator stay as qualified; progress is held, never forced,
    /// when the capture-point support margin or a joint ceiling would be
    /// violated. Everything else is the Gate 5 plant.
    /// </summary>
    public sealed partial class DeadliftSetupDynamicTests
    {
        private static string LiftLine(OwnershipCase c, DynamicsTrace t)
        {
            float sMax = 0f; int held = 0; string firstHeld = "none";
            double minMargin = double.PositiveInfinity;
            string[] lines = t.Rows.ToString().Split('\n');
            string[] head = lines[0].Split(',');
            int iS = Array.IndexOf(head, "s"), iH = Array.IndexOf(head, "held"), iM = Array.IndexOf(head, "margin");
            for (int k = 1; k < lines.Length; k++)
            {
                string[] v = lines[k].Split(',');
                if (v.Length <= iM) continue;
                float s = float.Parse(v[iS], CultureInfo.InvariantCulture);
                sMax = Mathf.Max(sMax, s);
                if (v[iH] != "-") { held++; if (firstHeld == "none") firstHeld = v[1] + ":" + v[iH]; }
                if (double.TryParse(v[iM], NumberStyles.Float, CultureInfo.InvariantCulture, out double m) && s > 0f)
                    minMargin = Math.Min(minMargin, m);
            }
            return string.Format(CultureInfo.InvariantCulture,
                "{0} | phase at end {1} | lift progress max {2:F3} | held ticks {3} (first {4}) | min capture-point margin while lifting {5:F1} mm | bar rise max {6:F1} mm | bar-limb contact {7}",
                OutcomeLine(c, t), c.PhaseAtEnd, sMax, held, firstHeld, minMargin * 1000d, (c.BarYMax - c.BarY0) * 1000f, c.BarLimb);
        }

        [UnityTest, Timeout(1200000), Explicit("GAM-70 Gate 6 ascent path solve only; run on demand")]
        public IEnumerator GATE6_ASCENT_PATH_SOLVE()
        {
            _ankleRoll = true;
            yield return Prepare(true, true, false);
            float t0 = Time.realtimeSinceStartup;
            var path = new DeadliftLiftPath(_world.Rig, _setupMeasurements, _world.Model, _world.Bar.Body.mass);
            path.Solve();
            string report = path.Describe() + string.Format(CultureInfo.InvariantCulture, "solve time {0:F1} s\n", Time.realtimeSinceStartup - t0);
            F.WriteEvidence("gate6-path.txt", report);
            _ankleRoll = false;
            Assert.Pass(report);
        }

        /// <summary>Gate 6/7 qualified configuration: P3 hand-load reference, moving regulator (no quiet-stance anchor, integral leak, athlete+bar COM), solved ascent.</summary>
        private static OwnershipCase QualifiedLiftCase(string name, int ticks, DeadliftLiftPath path = null) => new OwnershipCase
        {
            Name = name, Ticks = ticks, MaxIntent = 1f, AbortAtTick = -1, HandLoad = 2, MovingRegulator = true,
            Lift = true, LiftRate = 0.30f, LeakSeconds = 0.10f, SystemCom = true, Path = path
        };

        private sealed class AttemptRecord
        {
            public DeadliftReferee Referee;
            public DeadliftAttemptObserver Observer;
            public ulong LockoutTick, ReturnedTick, ReleaseTick;
            public float MinFootAfterBreak = float.MaxValue, MinMarginLifting = float.MaxValue, MaxJointDemand;
            public string MaxJointDemandId = string.Empty, MaxJointDemandPhase = string.Empty;
            public int MaxJointDemandTick;
            public int ZeroFootTicks, SaturatedTicks;
            public float BarRestY, BarPeakY, LandingVy;
            public readonly StringBuilder Log = new StringBuilder();
        }

        /// <summary>
        /// One complete attempt on the authoritative plant: Drive held for the
        /// ascent, the referee's Down from the measured finished and motionless
        /// position, controlled lowering along the same path, grips released
        /// only once the bar rests on the platform.
        /// </summary>
        private IEnumerator RunFullAttempt(OwnershipCase c, DynamicsTrace t, AttemptRecord a, int releaseHoldTicks = 100)
        {
            t.Rows.Append(DynHeader());
            c.OnPrepared = _ =>
            {
                InstallDynamicsTrace(t);
                a.BarRestY = _world.Bar.Body.worldCenterOfMass.y;
                a.Referee = new DeadliftReferee(a.BarRestY, _world.Bar.Body.mass * -Physics.gravity.y, SimulationConstants.FixedDeltaTimeSeconds);
                a.Observer = new DeadliftAttemptObserver(_world.Rig, _world.Bar.Body, () => PlateLeftN() + PlateRightN(),
                    () => _ticks.Count > 0 ? (float)_ticks[_ticks.Count - 1].BarLegN : 0f,
                    () => _world.LeftGrip != null && _world.LeftGrip.IsAttached, () => _world.RightGrip != null && _world.RightGrip.IsAttached);
            };
            c.PerTick = (cc, i) =>
            {
                RecordDynamics(cc, t, i);
                DeadliftObservation o = a.Observer.Observe((ulong)i, i * SimulationConstants.FixedDeltaTimeSeconds);
                a.Referee.Observe(o);
                a.BarPeakY = Mathf.Max(a.BarPeakY, (float)o.BarY);
                if (a.Referee.DownGiven)
                    _pull.CommandDown((ulong)i);
                if (_pull.Phase == DeadliftPullPhase.Lockout && a.LockoutTick == 0) a.LockoutTick = (ulong)i;
                if (_pull.Phase == DeadliftPullPhase.Returned && a.ReturnedTick == 0)
                {
                    a.ReturnedTick = (ulong)i;
                    a.LandingVy = (float)o.BarVy;
                }
                if (a.ReturnedTick != 0 && a.ReleaseTick == 0 && a.Referee.Completed)
                {
                    a.ReleaseTick = (ulong)i;
                    _world.LeftGrip?.Release();
                    _world.RightGrip?.Release();
                }
                if (cc.SeparationTick != 0 && i > (int)cc.SeparationTick + 5 && a.ReturnedTick == 0)
                {
                    var tk = _ticks[_ticks.Count - 1];
                    a.MinFootAfterBreak = Mathf.Min(a.MinFootAfterBreak, (float)Math.Min(tk.LeftFootN, tk.RightFootN));
                    if (tk.FootPtsL == 0 || tk.FootPtsR == 0) a.ZeroFootTicks++;
                    if (_pull.Phase == DeadliftPullPhase.Lifting && c.Monitor != null)
                        a.MinMarginLifting = Mathf.Min(a.MinMarginLifting, c.Monitor.MarginM);
                    bool saturated = false;
                    foreach (PoweredJointController.PoweredJointRuntime j in _world.Rig.PoweredController.Joints)
                        saturated |= j.HasPostPhysicsDiagnostic && j.PostPhysicsDiagnostic.ModeledDemand > 1f;
                    if (saturated) a.SaturatedTicks++;
                    foreach (PoweredJointController.PoweredJointRuntime j in _world.Rig.PoweredController.Joints)
                        if (j.HasPostPhysicsDiagnostic && j.PostPhysicsDiagnostic.ModeledDemand > a.MaxJointDemand)
                        {
                            a.MaxJointDemand = j.PostPhysicsDiagnostic.ModeledDemand;
                            a.MaxJointDemandId = j.Id;
                            a.MaxJointDemandTick = i;
                            a.MaxJointDemandPhase = _pull.Phase.ToString();
                        }
                }
                if (i % 50 == 0 || (a.Referee.DownGiven && a.Referee.DownTick == (ulong)i))
                    a.Log.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "t{0,5} {1,-9} s {2:F3} bar {3:F4} m vy {4:+0.000;-0.000} | plates {5,6:F1} N | knee {6:F1}/{7:F1} deg torso {8:F1} deg shoulders ahead {9:F3} m | feet travel {10:F3}/{11:F3} m | referee {12}",
                        i, _pull.Phase, _pull.LiftProgress01, o.BarY, o.BarVy, o.PlateN, o.KneeFlexionLDeg, o.KneeFlexionRDeg, o.TorsoDeg,
                        o.ShoulderAheadOfHipM, o.FootTravelLM, o.FootTravelRM, a.Referee.State));
                if (a.ReleaseTick != 0 && i >= (int)a.ReleaseTick + releaseHoldTicks)
                    cc.Ticks = i;   // end the attempt after the post-release hold
            };
            yield return RunOwnership(c);
            F.WriteEvidence("gate5-dynamics-" + c.Name + ".csv", t.Rows.ToString());
        }

        private static string AttemptSummary(OwnershipCase c, AttemptRecord a) => string.Format(CultureInfo.InvariantCulture,
            "{0}: separation {1} | lockout {2} | Down {3} | returned {4} (landing {5:F3} m/s) | grips released {6} | bar peak rise {7:F1} mm | " +
            "min foot after break {8:F1} N, zero-point foot ticks {9} | min capture margin lifting {10:F1} mm | max joint demand {11:F3} ({12} @{14} {15}), ticks with a joint above its ceiling {16} | {13}",
            c.Name, c.SeparationTick, a.LockoutTick, a.Referee.DownGiven ? a.Referee.DownTick.ToString(CultureInfo.InvariantCulture) : "not given",
            a.ReturnedTick, a.LandingVy, a.ReleaseTick, (a.BarPeakY - a.BarRestY) * 1000f, a.MinFootAfterBreak, a.ZeroFootTicks,
            a.MinMarginLifting * 1000f, a.MaxJointDemand, a.MaxJointDemandId, a.Referee.Summary(), a.MaxJointDemandTick, a.MaxJointDemandPhase, a.SaturatedTicks);

        /// <summary>
        /// Executable regression (normal PlayMode): one complete deadlift attempt
        /// with the player's Drive held on the authoritative plant is a good lift
        /// judged only from measured bodies: bilateral bar break, ascent through
        /// knee passage to a motionless finished position, the referee's Down,
        /// controlled lowering, the bar resting on the plates, then the grips
        /// released. Finite-actuator saturation is measured and bounded, not hidden.
        /// </summary>
        [UnityTest, Timeout(1800000)]
        public IEnumerator GATE7_PLAYER_DEADLIFT_ATTEMPT_IS_A_GOOD_LIFT()
        {
            var c = QualifiedLiftCase("regression-attempt", 2000);
            var t = new DynamicsTrace();
            var a = new AttemptRecord();
            yield return RunFullAttempt(c, t, a, 50);
            string report = AttemptSummary(c, a);
            F.WriteEvidence("gate7-attempt-regression.txt", report + "\n" + a.Log);
            string why = "\n" + report;
            Assert.That(c.SeparationTick, Is.GreaterThan(0ul), "no bilateral bar break" + why);
            Assert.That(a.LockoutTick, Is.GreaterThan(c.SeparationTick), "the lift path was not completed" + why);
            Assert.That(a.Referee.DownGiven, Is.True, "no Down: the finished position was not motionless" + why);
            Assert.That(a.ReturnedTick, Is.GreaterThan(a.Referee.DownTick), "the bar was not returned to the platform" + why);
            Assert.That(a.ReleaseTick, Is.GreaterThanOrEqualTo(a.ReturnedTick), "grips released before the bar rested" + why);
            Assert.That(a.Referee.GoodLift, Is.True, "the referee did not award the lift" + why);
            Assert.That((a.BarPeakY - a.BarRestY), Is.GreaterThan(0.75f), "the bar did not reach a lockout height" + why);
            Assert.That(a.ZeroFootTicks, Is.EqualTo(0), "a foot lost every contact point" + why);
            Assert.That(a.MinFootAfterBreak, Is.GreaterThan(300f), "a foot was unloaded during the attempt" + why);
            Assert.That(a.MinMarginLifting, Is.GreaterThan(0.05f), "capture-point support margin below 50 mm during the ascent" + why);
            Assert.That(Mathf.Abs(a.LandingVy), Is.LessThan(0.1f), "uncontrolled landing" + why);
            Assert.That(a.SaturatedTicks, Is.LessThanOrEqualTo(60), "joint saturation beyond the measured transient bound" + why);
        }

        [UnityTest, Timeout(3000000), Explicit("GAM-70 Gate 7 complete attempt experiment; run on demand")]
        public IEnumerator GATE7_COMPLETE_ATTEMPT_DOWN_LOWERING_AND_DECISION()
        {
            var c = QualifiedLiftCase("A1-full-attempt", 2200);
            var t = new DynamicsTrace();
            var a = new AttemptRecord();
            yield return RunFullAttempt(c, t, a);
            string report = "GAM-70 Gate 7 complete attempt (fresh plant, Drive held throughout)\n" + c.PathReport + AttemptSummary(c, a) + "\n" + a.Log;
            F.WriteEvidence("gate7-attempt.txt", report);
            Assert.Pass(report);
        }

        [UnityTest, Timeout(3000000), Explicit("GAM-70 Gate 6 ascent experiment; run on demand")]
        public IEnumerator GATE6_PLAYER_DRIVE_ADVANCES_THE_SOLVED_ASCENT()
        {
            float rate = float.TryParse(Environment.GetEnvironmentVariable("GAM70_G6_RATE"), NumberStyles.Float, CultureInfo.InvariantCulture, out float r) ? r : 0.30f;
            float leak = float.TryParse(Environment.GetEnvironmentVariable("GAM70_G6_LEAK"), NumberStyles.Float, CultureInfo.InvariantCulture, out float lk) ? lk : 0.10f;
            var c = new OwnershipCase { Name = "L1-leak", Ticks = 1200, MaxIntent = 1f, AbortAtTick = -1, HandLoad = 2, MovingRegulator = true, Lift = true, LiftRate = rate, LeakSeconds = leak };
            var t = new DynamicsTrace();
            yield return RunTraced(c, t);
            // A/B: the only difference is the athlete+bar COM observation of the regulator in the moving phase.
            var c2 = new OwnershipCase { Name = "L2-leak-syscom", Ticks = 1200, MaxIntent = 1f, AbortAtTick = -1, HandLoad = 2, MovingRegulator = true, Lift = true, LiftRate = rate, LeakSeconds = leak, SystemCom = true, Path = c.Path };
            var t2 = new DynamicsTrace();
            yield return RunTraced(c2, t2);
            // Discriminating case: the same plant (L2) with Drive released once s reaches the hold point, then held
            // motionless. A failure while static isolates the standing-posture balance loop from the motion.
            float holdAt = float.TryParse(Environment.GetEnvironmentVariable("GAM70_G6_HOLD_AT"), NumberStyles.Float, CultureInfo.InvariantCulture, out float h) ? h : 0.70f;
            var c3 = new OwnershipCase { Name = "L3-hold-" + holdAt.ToString("F2", CultureInfo.InvariantCulture), Ticks = 1000, MaxIntent = 1f, AbortAtTick = -1, HandLoad = 2, MovingRegulator = true, Lift = true, LiftRate = rate, LeakSeconds = leak, SystemCom = true, Path = c.Path };
            var t3 = new DynamicsTrace();
            Action<OwnershipCase, int> rec3 = (cc, i) => RecordDynamics(cc, t3, i);
            t3.Rows.Append(DynHeader());
            c3.OnPrepared = _ => InstallDynamicsTrace(t3);
            c3.PerTick = (cc, i) =>
            {
                rec3(cc, i);
                if (_pull.LiftProgress01 >= holdAt)
                    _pull.IntentOverride01 = 0f;
            };
            yield return RunOwnership(c3);
            F.WriteEvidence("gate5-dynamics-" + c3.Name + ".csv", t3.Rows.ToString());
            var report = new StringBuilder();
            report.AppendLine(LiftLine(c3, t3) + " | Drive released at s >= " + holdAt.ToString("F2", CultureInfo.InvariantCulture));
            report.AppendLine("GAM-70 Gate 6 ascent A/B (fresh plants, Drive held, P3 reference, lift rate " + rate.ToString("F2", CultureInfo.InvariantCulture) + " /s; moving-phase integral leak " + leak.ToString("F2", CultureInfo.InvariantCulture) + " s; L2 = L1 + athlete+bar COM observation)");
            report.AppendLine(c.PathReport);
            report.AppendLine(LiftLine(c, t));
            report.AppendLine(LiftLine(c2, t2));
            F.WriteEvidence("gate6-ascent.txt", report.ToString());
            Assert.Pass(report.ToString());
        }
    }
}
