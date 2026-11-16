using System;
using System.Collections;
using System.Collections.Generic;
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
    /// GAM-70 Gate 5: command-history confound of the post-separation failure.
    /// Every Gate 4 case released Drive at tick 600 (after separation at 396);
    /// the pull controller keeps the Separated phase, and its COM governor can
    /// suppress the applied extension independently of the player's Drive.
    /// This harness runs fresh-plant A/B cases that differ in one command
    /// input only, and records per tick: requested and applied Drive, governor
    /// state and COM shift, the hip/knee/trunk extension commands, the realized
    /// state of every powered leg/trunk channel, athlete and bar linear and
    /// angular momentum about the world origin, and the exact contact wrenches
    /// (foot soles, plates, any other floor or bar contact). It is read-only:
    /// physics, gains, limits, load schedule, contact geometry and controller
    /// are those of the Gate 4 fixed-ownership N0 case.
    /// </summary>
    public sealed partial class DeadliftSetupDynamicTests
    {
        private static readonly string[] DynJoints =
            { "left_thigh", "right_thigh", "left_shank", "right_shank", "abdomen", "thorax", "left_foot", "right_foot" };

        private sealed class DynamicsTrace
        {
            public readonly StringBuilder Rows = new StringBuilder();
            public readonly Dictionary<string, DeadliftWrenchSensor> Wrench = new Dictionary<string, DeadliftWrenchSensor>(StringComparer.Ordinal);
            public DeadliftWrenchSensor Bar;
            public readonly List<double[]> Command = new List<double[]>();
            public readonly List<double[]> Plant = new List<double[]>();
            public readonly List<bool> Governor = new List<bool>();
            public readonly List<double> Applied = new List<double>();
            public readonly List<double> Requested = new List<double>();
        }

        private static string DynHeader()
        {
            var h = new StringBuilder("case,tick,phase,intent,applied,gov,comShift,hipExt,kneeExt,trunkExt,");
            foreach (string j in DynJoints)
                foreach (string k in new[] { "req", "app", "act", "vel", "dem", "frac", "cap", "prox", "mod" })
                    h.Append(j).Append('_').Append(k).Append(',');
            foreach (string b in new[] { "A", "B" })
                h.Append($"{b}m,{b}cx,{b}cy,{b}cz,{b}Px,{b}Py,{b}Pz,{b}Lx,{b}Ly,{b}Lz,");
            foreach (string w in new[] { "fL", "fR", "fO", "pl", "bA", "aB" })
                h.Append($"{w}Fx,{w}Fy,{w}Fz,{w}Mx,{w}My,{w}Mz,{w}n,");
            h.Append("gLFx,gLFy,gLFz,gRFx,gRFy,gRFz,gLTx,gLTy,gLTz,gRTx,gRTy,gRTz,gLpx,gLpy,gLpz,gRpx,gRpy,gRpz,");
            h.Append("patchL0x,patchL1x,patchL0z,patchL1z,patchR0x,patchR1x,patchR0z,patchR1z,");
            foreach (PhysicalSegmentRecipe r in PhysicalAthleteDefinition.Segments)
                h.Append("w_").Append(r.Id).Append(',');
            h.Append("end\n");
            return h.ToString();
        }

        private void InstallDynamicsTrace(DynamicsTrace trace)
        {
            Collider platform = _world.Rig.PlatformCollider;
            var barColliders = new HashSet<Collider>(_world.Bar.Body.GetComponentsInChildren<Collider>(true));
            var athleteColliders = new HashSet<Collider>(_world.Rig.Segments.Values.Select(x => x.Collider));
            foreach (KeyValuePair<string, PhysicalAthleteRig.SegmentRuntime> e in _world.Rig.Segments)
            {
                DeadliftWrenchSensor s = e.Value.Body.gameObject.AddComponent<DeadliftWrenchSensor>();
                s.Ground = platform;
                s.Classify = cp => cp.otherCollider == platform ? "floor" : barColliders.Contains(cp.otherCollider) ? "bar" : null;
                trace.Wrench[e.Key] = s;
            }
            DeadliftWrenchSensor bar = _world.Bar.Body.gameObject.AddComponent<DeadliftWrenchSensor>();
            bar.Ground = platform;
            bar.Classify = cp => cp.otherCollider == platform ? "floor" : athleteColliders.Contains(cp.otherCollider) ? "athlete" : null;
            trace.Bar = bar;
        }

        private static void Momentum(IEnumerable<Rigidbody> bodies, out float mass, out Vector3 com, out Vector3 p, out Vector3 l)
        {
            mass = 0f; com = Vector3.zero; p = Vector3.zero; l = Vector3.zero;
            foreach (Rigidbody b in bodies)
            {
                Vector3 c = b.worldCenterOfMass;
                mass += b.mass;
                com += b.mass * c;
                p += b.mass * b.linearVelocity;
                Quaternion r = b.rotation * b.inertiaTensorRotation;
                Vector3 wl = Quaternion.Inverse(r) * b.angularVelocity;
                Vector3 spin = r * Vector3.Scale(b.inertiaTensor, wl);
                l += Vector3.Cross(c, b.mass * b.linearVelocity) + spin;
            }
            com /= Mathf.Max(mass, 1e-6f);
        }

        private static void AppendWrench(List<string> row, DeadliftWrenchSensor.Wrench w, double dt)
        {
            row.Add(Own(w.Impulse.x / dt)); row.Add(Own(w.Impulse.y / dt)); row.Add(Own(w.Impulse.z / dt));
            row.Add(Own(w.OriginMoment.x / dt)); row.Add(Own(w.OriginMoment.y / dt)); row.Add(Own(w.OriginMoment.z / dt));
            row.Add(w.Points.ToString(CultureInfo.InvariantCulture));
        }

        private void RecordDynamics(OwnershipCase c, DynamicsTrace trace, int tick)
        {
            double dt = SimulationConstants.FixedDeltaTimeSeconds;
            foreach (DeadliftWrenchSensor s in trace.Wrench.Values)
                s.CompleteTick();
            trace.Bar.CompleteTick();

            var row = new List<string>
            {
                c.Name, tick.ToString(CultureInfo.InvariantCulture), _pull.Phase.ToString(), Own(_pull.Intent01), Own(_pull.Applied01),
                _pull.GovernorActive ? "1" : "0", Own(_pull.ComShiftM), Own(_pull.HipExtensionDeg), Own(_pull.KneeExtensionDeg), Own(_pull.TrunkExtensionDeg)
            };
            var command = new List<double> { _pull.Applied01, _pull.HipExtensionDeg, _pull.KneeExtensionDeg, _pull.TrunkExtensionDeg };
            foreach (string id in DynJoints)
            {
                PoweredJointDiagnostic d = _world.Rig.PoweredController.GetJoint(id).PostPhysicsDiagnostic;
                row.Add(Own(d.PrimaryRequestedAngleDeg)); row.Add(Own(d.PrimaryAppliedAngleDeg)); row.Add(Own(d.PrimaryActualAngleDeg));
                row.Add(Own(d.ActualAngularVelocityRadS.x)); row.Add(Own(d.TwistDriveDemandNm)); row.Add(Own(d.TwistDriveDemandFraction));
                row.Add(Own(d.MaximumForceNm)); row.Add(Own(d.LimitProximity)); row.Add(Own(d.ModeledDemand));
                command.Add(d.PrimaryRequestedAngleDeg);
            }

            Momentum(_world.Rig.Segments.Values.Select(x => x.Body), out float ma, out Vector3 ca, out Vector3 pa, out Vector3 la);
            Momentum(new[] { _world.Bar.Body }, out float mb, out Vector3 cb, out Vector3 pb, out Vector3 lb);
            foreach (var v in new[] { (ma, ca, pa, la), (mb, cb, pb, lb) })
            {
                row.Add(Own(v.Item1));
                row.Add(Own(v.Item2.x)); row.Add(Own(v.Item2.y)); row.Add(Own(v.Item2.z));
                row.Add(Own(v.Item3.x)); row.Add(Own(v.Item3.y)); row.Add(Own(v.Item3.z));
                row.Add(Own(v.Item4.x)); row.Add(Own(v.Item4.y)); row.Add(Own(v.Item4.z));
            }

            DeadliftWrenchSensor.Wrench footL = trace.Wrench["left_foot"].Get("floor");
            DeadliftWrenchSensor.Wrench footR = trace.Wrench["right_foot"].Get("floor");
            var otherFloor = default(DeadliftWrenchSensor.Wrench);
            var barOnAthlete = default(DeadliftWrenchSensor.Wrench);
            foreach (KeyValuePair<string, DeadliftWrenchSensor> e in trace.Wrench)
            {
                if (!e.Key.EndsWith("_foot", StringComparison.Ordinal))
                    otherFloor += e.Value.Get("floor");
                barOnAthlete += e.Value.Get("bar");
            }
            AppendWrench(row, footL, dt);
            AppendWrench(row, footR, dt);
            AppendWrench(row, otherFloor, dt);
            AppendWrench(row, trace.Bar.Get("floor"), dt);
            AppendWrench(row, barOnAthlete, dt);
            AppendWrench(row, trace.Bar.Get("athlete"), dt);

            Vector3 gLF = _world.LeftGrip != null && _world.LeftGrip.IsAttached ? _world.LeftGrip.SolverForceRaw : Vector3.zero;
            Vector3 gRF = _world.RightGrip != null && _world.RightGrip.IsAttached ? _world.RightGrip.SolverForceRaw : Vector3.zero;
            Vector3 gLT = _world.LeftGrip != null && _world.LeftGrip.IsAttached ? _world.LeftGrip.SolverTorqueRaw : Vector3.zero;
            Vector3 gRT = _world.RightGrip != null && _world.RightGrip.IsAttached ? _world.RightGrip.SolverTorqueRaw : Vector3.zero;
            Vector3 gLp = _world.Rig.Segments["left_hand"].Body.transform.TransformPoint(_world.LeftGrip != null ? _world.LeftGrip.HandAnchorLocal : Vector3.zero);
            Vector3 gRp = _world.Rig.Segments["right_hand"].Body.transform.TransformPoint(_world.RightGrip != null ? _world.RightGrip.HandAnchorLocal : Vector3.zero);
            foreach (Vector3 v in new[] { gLF, gRF, gLT, gRT, gLp, gRp })
            {
                row.Add(Own(v.x)); row.Add(Own(v.y)); row.Add(Own(v.z));
            }
            foreach (DeadliftFootCopSensor s in new[] { _copL, _copR })
            {
                bool has = s.Points > 0;
                row.Add(has ? Own(s.PatchMin.x) : "nan"); row.Add(has ? Own(s.PatchMax.x) : "nan");
                row.Add(has ? Own(s.PatchMin.y) : "nan"); row.Add(has ? Own(s.PatchMax.y) : "nan");
            }
            foreach (PhysicalSegmentRecipe r in PhysicalAthleteDefinition.Segments)
                row.Add(Own(_world.Rig.Segments[r.Id].Body.angularVelocity.x));
            DeadliftHandLoadReference hl = _world.Hold.HandLoadReference;
            row.Add(hl != null ? Own(hl.HandPullN) : Own(_groundedRef.HandPullN));
            row.Add(Own(_world.Hold.HandLoadFilteredN));
            row.Add(Own(MeasuredPerHandUpN()));
            row.Add(hl != null ? Own(hl.MaxDemandFraction) : "nan");
            row.Add(_world.Hold.ReferenceOwner.ToString());
            row.Add("0");
            trace.Rows.AppendLine(string.Join(",", row));

            trace.Command.Add(command.ToArray());
            trace.Plant.Add(new double[]
            {
                _world.Bar.Body.position.y, _world.Bar.Body.position.z, ca.x, ca.y, ca.z,
                footL.Impulse.y / dt, footR.Impulse.y / dt, pa.z, la.x
            });
            trace.Governor.Add(_pull.GovernorActive);
            trace.Applied.Add(_pull.Applied01);
            trace.Requested.Add(_pull.Intent01);
        }

        private IEnumerator RunTraced(OwnershipCase c, DynamicsTrace trace)
        {
            trace.Rows.Append(DynHeader());
            c.OnPrepared = _ => InstallDynamicsTrace(trace);
            c.PerTick = (cc, i) => RecordDynamics(cc, trace, i);
            yield return RunOwnership(c);
            F.WriteEvidence("gate5-dynamics-" + c.Name + ".csv", trace.Rows.ToString());
        }

        private static int FirstDivergence(List<double[]> a, List<double[]> b, double tol)
        {
            int n = Math.Min(a.Count, b.Count);
            for (int i = 0; i < n; i++)
                for (int k = 0; k < a[i].Length; k++)
                    if (Math.Abs(a[i][k] - b[i][k]) > tol)
                        return i;
            return -1;
        }

        private static string GovernorHistory(DynamicsTrace t)
        {
            var b = new StringBuilder();
            bool prev = false;
            for (int i = 0; i < t.Governor.Count; i++)
                if (t.Governor[i] != prev)
                {
                    b.Append(string.Format(CultureInfo.InvariantCulture, "{0}@{1} (applied {2:F3}) ", t.Governor[i] ? "closed" : "open", i + 1, t.Applied[i]));
                    prev = t.Governor[i];
                }
            int zero = t.Applied.FindIndex(x => x <= 1e-6);
            int firstPull = t.Applied.FindIndex(x => x > 0);
            int zeroAfterPull = firstPull < 0 ? -1 : t.Applied.FindIndex(firstPull, x => x <= 1e-6);
            b.Append(string.Format(CultureInfo.InvariantCulture, "| applied max {0:F3} | first applied = 0 after the pull: tick {1}",
                t.Applied.DefaultIfEmpty(0).Max(), zeroAfterPull < 0 ? "never" : (zeroAfterPull + 1).ToString(CultureInfo.InvariantCulture)));
            return b.ToString();
        }

        private static string DivergenceLine(string label, OwnershipCase a, DynamicsTrace ta, OwnershipCase b, DynamicsTrace tb)
        {
            int dc = FirstDivergence(ta.Command, tb.Command, 1e-6);
            int dp = FirstDivergence(ta.Plant, tb.Plant, 1e-6);
            int dr = -1;
            for (int i = 0; i < Math.Min(ta.Requested.Count, tb.Requested.Count) && dr < 0; i++)
                if (Math.Abs(ta.Requested[i] - tb.Requested[i]) > 1e-6) dr = i;
            var s = new StringBuilder();
            s.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0}: first requested-Drive divergence tick {1} | first applied-command divergence tick {2} | first plant divergence tick {3} | compared ticks {4}/{5}",
                label, dr < 0 ? "none" : (dr + 1).ToString(CultureInfo.InvariantCulture),
                dc < 0 ? "none" : (dc + 1).ToString(CultureInfo.InvariantCulture),
                dp < 0 ? "none" : (dp + 1).ToString(CultureInfo.InvariantCulture), ta.Command.Count, tb.Command.Count));
            if (dc >= 0)
                s.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  at applied divergence: {0} applied {1:F4} gov {2} hip/knee/trunk {3:F3}/{4:F3}/{5:F3} | {6} applied {7:F4} gov {8} hip/knee/trunk {9:F3}/{10:F3}/{11:F3}",
                    a.Name, ta.Command[dc][0], ta.Governor[dc], ta.Command[dc][1], ta.Command[dc][2], ta.Command[dc][3],
                    b.Name, tb.Command[dc][0], tb.Governor[dc], tb.Command[dc][1], tb.Command[dc][2], tb.Command[dc][3]));
            return s.ToString();
        }

        private static string OutcomeLine(OwnershipCase c, DynamicsTrace t)
        {
            int zeroL = -1, zeroR = -1;
            for (int i = 0; i < t.Plant.Count; i++)
            {
                if (zeroL < 0 && t.Plant[i][5] <= 0d && c.SeparationTick != 0 && i + 1 > (int)c.SeparationTick) zeroL = i + 1;
                if (zeroR < 0 && t.Plant[i][6] <= 0d && c.SeparationTick != 0 && i + 1 > (int)c.SeparationTick) zeroR = i + 1;
            }
            int warn = -1;
            for (int i = 0; i < t.Plant.Count && warn < 0; i++)
                if (c.SeparationTick != 0 && i + 1 > (int)c.SeparationTick && Math.Min(t.Plant[i][5], t.Plant[i][6]) < 20d) warn = i + 1;
            return string.Format(CultureInfo.InvariantCulture,
                "{0,-22} sep {1} | first TRUE zero-normal contact loss L {2} R {3} | diagnostic <20 N warning {4} | bar max {5:F4} m | min foot {6:F1} N | COM dev {7:F1} mm | event {8} | ticks {9}",
                c.Name, c.SeparationTick, zeroL < 0 ? "none" : zeroL.ToString(CultureInfo.InvariantCulture),
                zeroR < 0 ? "none" : zeroR.ToString(CultureInfo.InvariantCulture), warn < 0 ? "none" : warn.ToString(CultureInfo.InvariantCulture),
                c.BarYMax, c.MinFoot, c.MaxComDev * 1000f, c.Event, t.Plant.Count);
        }

        /// <summary>
        /// Gate 5 command-history A/B: identical fresh plants (fixed ownership
        /// N0) that differ only in the player's Drive after separation.
        /// A releases Drive at tick 600 (the Gate 4 history); B holds full
        /// intent throughout. A third, diagnostic-only plant C holds Drive with
        /// the COM governor disabled, isolating the governor from the Drive
        /// history. C is never a production configuration.
        /// </summary>
        [UnityTest, Timeout(2400000), Explicit("GAM-70 Gate 5 command-history A/B; run on demand")]
        public IEnumerator GATE5_DRIVE_RELEASE_AB_ISOLATES_THE_COMMAND_HISTORY()
        {
            var a = new OwnershipCase { Name = "A-release-600", Ticks = 900, MaxIntent = 1f, AbortAtTick = 600 };
            var ta = new DynamicsTrace();
            yield return RunTraced(a, ta);
            var b = new OwnershipCase { Name = "B-held", Ticks = 900, MaxIntent = 1f, AbortAtTick = -1 };
            var tb = new DynamicsTrace();
            yield return RunTraced(b, tb);
            var cDiag = new OwnershipCase { Name = "C-held-governor-off", Ticks = 900, MaxIntent = 1f, AbortAtTick = -1, GovernorEnabled = false };
            var tc = new DynamicsTrace();
            yield return RunTraced(cDiag, tc);

            var report = new StringBuilder();
            report.AppendLine("GAM-70 Gate 5 command-history A/B (fresh plants, fixed-ownership N0 schedule, same physics/gains/limits/contacts)");
            report.AppendLine(DivergenceLine("A vs B (Drive release only)", a, ta, b, tb));
            report.AppendLine(DivergenceLine("B vs C (governor only, diagnostic)", b, tb, cDiag, tc));
            foreach (var x in new[] { (a, ta), (b, tb), (cDiag, tc) })
            {
                report.AppendLine(OutcomeLine(x.Item1, x.Item2));
                report.AppendLine("  governor: " + GovernorHistory(x.Item2));
            }
            F.WriteEvidence("gate5-command-history-ab.txt", report.ToString());

            Assert.That(a.SeparationTick, Is.GreaterThan(0ul), "A never separated\n" + report);
            Assert.That(b.SeparationTick, Is.EqualTo(a.SeparationTick), "A and B did not separate at the same tick\n" + report);
        }

        /// <summary>
        /// Gate 5 moving-reference A/B on fresh plants, full Drive held, the
        /// production COM governor on. Only the reference ownership during the
        /// pull changes:
        ///   P0: Gate 4 N0 path (pretension servo runs through the pull, airborne schedule);
        ///   P1: the pretension integral stops when the pull begins (schedule kept);
        ///   P2: measured hand-load reference, posture-fixed C4 statics, no schedule;
        ///   P3: as P2, re-solved at the current posture every tick, regulator
        ///       without the quiet-standing COM position anchor.
        /// </summary>
        [UnityTest, Timeout(3000000), Explicit("GAM-70 Gate 5 moving-reference A/B; run on demand")]
        public IEnumerator GATE5_MOVING_REFERENCE_AB()
        {
            string only = Environment.GetEnvironmentVariable("GAM70_G5_CASES");
            var cases = new List<(OwnershipCase c, DynamicsTrace t)>
            {
                (new OwnershipCase { Name = "P0-control", AbortAtTick = -1 }, new DynamicsTrace()),
                (new OwnershipCase { Name = "P1-stop-pretension", AbortAtTick = -1, StopPretensionOnPull = true }, new DynamicsTrace()),
                (new OwnershipCase { Name = "P2-handload-fixed", AbortAtTick = -1, HandLoad = 1 }, new DynamicsTrace()),
                (new OwnershipCase { Name = "P3-handload-moving", AbortAtTick = -1, HandLoad = 2, MovingRegulator = true }, new DynamicsTrace()),
                (new OwnershipCase { Name = "P4-handload-moving-govoff", AbortAtTick = -1, HandLoad = 2, MovingRegulator = true, GovernorEnabled = false }, new DynamicsTrace()),
            };
            var report = new StringBuilder();
            report.AppendLine("GAM-70 Gate 5 moving-reference A/B (fresh plants, Drive held, same physics/gains/limits/contacts)");
            foreach (var x in cases)
            {
                if (!string.IsNullOrEmpty(only) && !only.Split(',').Any(k => x.c.Name.StartsWith(k, StringComparison.Ordinal)))
                    continue;
                x.c.Ticks = 900;
                x.c.MaxIntent = 1f;
                yield return RunTraced(x.c, x.t);
                report.AppendLine(OutcomeLine(x.c, x.t));
                report.AppendLine("  governor: " + GovernorHistory(x.t));
            }
            F.WriteEvidence("gate5-moving-reference-ab" + (string.IsNullOrEmpty(only) ? "" : "-" + only.Replace(',', '_')) + ".txt", report.ToString());
            Assert.Pass(report.ToString());
        }

        /// <summary>
        /// Executable regression (normal PlayMode): from the player's pull on,
        /// the measured hand-load reference (re-solved at the current posture)
        /// owns the static reference; the pretension servo stops; the bar
        /// separates from both plates and stays airborne with both feet flat
        /// and loaded, the ankle regulator unsaturated and every ankle channel
        /// well inside its ceiling. Reintroducing the pretension servo during
        /// the pull, or the posture-fixed airborne schedule, fails this test
        /// (Gate 5 P0/P1: support lost at ticks 686-695).
        /// </summary>
        [UnityTest, Timeout(900000)]
        public IEnumerator GATE5_HANDLOAD_REFERENCE_SUSTAINS_A_BILATERAL_BAR_BREAK()
        {
            var c = new OwnershipCase { Name = "regression-handload", Ticks = 700, MaxIntent = 1f, AbortAtTick = -1, HandLoad = 2, MovingRegulator = true };
            var t = new DynamicsTrace();
            int pretensionAtClaim = -1, maxRegSat = 0, flatLost = 0;
            float minFootAfter = float.MaxValue, maxPlateAfter = 0f, maxAnkleX = 0f, maxAnkleZ = 0f, maxDemand = 0f;
            Action<OwnershipCase, int> trace = null;
            c.PerTick = (cc, i) =>
            {
                trace?.Invoke(cc, i);
                if (pretensionAtClaim < 0 && _world.Hold.HandLoadClaimed)
                    pretensionAtClaim = _pretension.UpdateCount;
                if (cc.SeparationTick == 0 || i <= (int)cc.SeparationTick + 5)
                    return;
                var tk = _ticks[_ticks.Count - 1];
                minFootAfter = Mathf.Min(minFootAfter, (float)Math.Min(tk.LeftFootN, tk.RightFootN));
                maxPlateAfter = Mathf.Max(maxPlateAfter, (float)(tk.PlateLN + tk.PlateRN));
                if (Mathf.Abs(_regulator.OffsetDegFor("left_foot")) >= _regulator.MaxOffsetDeg - 1e-3f ||
                    Mathf.Abs(_regulator.OffsetDegFor("right_foot")) >= _regulator.MaxOffsetDeg - 1e-3f) maxRegSat++;
                if (tk.FootPtsL == 0 || tk.FootPtsR == 0) flatLost++;
                foreach (string id in new[] { "left_foot", "right_foot" })
                {
                    PoweredJointDiagnostic d = _world.Rig.PoweredController.GetJoint(id).PostPhysicsDiagnostic;
                    maxAnkleX = Mathf.Max(maxAnkleX, d.TwistDriveDemandFraction);
                    maxAnkleZ = Mathf.Max(maxAnkleZ, d.SecondaryDriveDemandFraction);
                }
                maxDemand = Mathf.Max(maxDemand, _world.Hold.HandLoadReference.MaxDemandFraction);
            };
            trace = (cc, i) => RecordDynamics(cc, t, i);
            t.Rows.Append(DynHeader());
            c.OnPrepared = _ => InstallDynamicsTrace(t);
            yield return RunOwnership(c);

            string report = OutcomeLine(c, t) + string.Format(CultureInfo.InvariantCulture,
                "\nafter separation (+5 ticks): min foot {0:F1} N, max plates {1:F1} N, regulator saturated ticks {2}, zero-point foot ticks {3}, ankle demand X {4:F3} Z {5:F3}, reference max demand {6:F3}, bar rise {7:F1} mm, pretension updates after claim {8}",
                minFootAfter, maxPlateAfter, maxRegSat, flatLost, maxAnkleX, maxAnkleZ, maxDemand, (c.BarYMax - c.BarY0) * 1000f,
                c.PretensionUpdatesAtEnd - pretensionAtClaim);
            F.WriteEvidence("gate5-handload-regression.txt", report);

            string why = "\n" + report;
            Assert.That(c.SeparationTick, Is.GreaterThan(0ul), "no measured separation" + why);
            Assert.That(c.OwnerAtEnd, Is.EqualTo("PullHandLoad"), "the hand-load reference does not own the static reference" + why);
            Assert.That(c.PretensionUpdatesAtEnd, Is.EqualTo(pretensionAtClaim), "the pretension servo kept running during the pull" + why);
            Assert.That(c.Event, Does.StartWith("plate-unloaded"), "the run ended in a support or bar-limb event" + why);
            Assert.That(maxPlateAfter, Is.LessThan(DeadliftPullController.SeparationPlateN), "the bar re-grounded on the plates" + why);
            Assert.That(minFootAfter, Is.GreaterThan(400f), "a foot lost load after the bar break" + why);
            Assert.That(flatLost, Is.EqualTo(0), "a foot lost every contact point after the bar break" + why);
            Assert.That(maxRegSat, Is.EqualTo(0), "the ankle regulator saturated after the bar break" + why);
            Assert.That(maxAnkleX, Is.LessThan(0.5f), "ankle X demand too high after the bar break" + why);
            Assert.That(maxAnkleZ, Is.LessThan(0.5f), "ankle roll demand too high after the bar break" + why);
            Assert.That(maxDemand, Is.LessThan(1f), "the applied reference exceeds a joint ceiling" + why);
            Assert.That(c.MaxComDev, Is.LessThan(0.02f), "the COM moved more than 20 mm" + why);
        }
    }
}
