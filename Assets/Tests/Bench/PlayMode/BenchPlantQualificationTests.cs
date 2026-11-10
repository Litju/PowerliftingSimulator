using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Bench.Unity;
using UnityEngine;
using UnityEngine.TestTools;
using H = PowerliftingSimulator.Bench.BenchTestHarness;

namespace PowerliftingSimulator.Bench
{
    /// <summary>
    /// GAM-69 Gate A (plant) and Gate B (support and grasp), on the real
    /// solver, fresh scene per case. Each assertion is a measured physical
    /// balance or an A/B control, never a controller phase.
    /// </summary>
    public sealed class BenchPlantQualificationTests
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;
        private const float G = 9.81f;

        [UnityTest]
        public IEnumerator GATE_A_PLANT_IS_MEASURED_FEASIBLE_AND_INSIDE_IPF_GEOMETRY()
        {
            yield return H.LoadBench();
            BenchPressPrototypeController bench = H.Bench;
            BenchPlantModel m = bench.Model;
            var b = new StringBuilder();
            b.AppendLine(m.Describe());
            b.AppendLine($"tick0 max penetration {bench.TickZeroMaxPenetrationM * 1000f:F2} mm, grip separation {bench.TickZeroMaxGripSeparationM * 1000f:F3} mm");
            b.AppendLine("joint sign probe (+10 deg logical X from the setup pose; supine world frame):");
            AppendSignProbe(b, bench);
            b.AppendLine("arm joint limit occupancy along the press path (max |twist|/limit, max swing-cone/limit):");
            AppendLimitOccupancy(b, m);
            H.Write("gateA-plant.txt", b.ToString());

            Assert.That(BenchSetupSolver.IsRejected(m), Is.False, string.Join("\n", m.Notes));
            Assert.That(m.Equipment.Violations(), Is.Empty);
            // The lift-off is not judged. A residual here is a kinematic
            // inconsistency of the commanded references against the locked
            // grip; its mechanical cost (internal drive load, saturation,
            // grip drift) is measured directly in Gate B. Bound it at 1 cm.
            // Its physical acceptance is in Gate C: the bar clears the hooks
            // and reaches lockout without drive saturation. The reference may
            // deviate from the planned bar line by a few centimetres because
            // the wrist is held (a moving wrist reference cannot be realized by
            // two hands locked to one bar: measured wrist demand 4.4).
            Assert.That(m.LiftPath.MaxResidualM, Is.LessThan(0.040), "lift-off path not reachable by the measured arms");
            // Judged press path: reference consistency within 5 mm; its
            // mechanical effect (grip drift, demand) is measured in Gate C.
            Assert.That(m.PressPath.MaxResidualM, Is.LessThan(0.005), "press path not reachable by the measured arms");
            Assert.That(m.ForefingerSpacingM, Is.LessThanOrEqualTo((float)BenchRulebook.MaxForefingerSpacingM));
            // 4.2(8) on the collider proxies, exactly as the referee observes it:
            // elbow underside (anchor - forearm radius) level with or below the
            // top of the shoulder joint (anchor + upper-arm radius).
            float elbowAboveShoulder = -m.TouchElbowBelowShoulderM;
            Assert.That(elbowAboveShoulder - BenchPressPrototypeController.ElbowUndersideRadiusM,
                Is.LessThanOrEqualTo(BenchPressPrototypeController.ShoulderTopRadiusM),
                "the touch pose cannot bring the elbows to the 4.2(8) depth");
            Assert.That(bench.TickZeroMaxPenetrationM, Is.LessThan(0.004f));
            Assert.That(Mathf.Abs(m.HeadGapM), Is.LessThan(0.002f));
            Assert.That(Mathf.Abs(m.LeftSoleGapM), Is.LessThan(0.003f));
            Assert.That(Mathf.Abs(m.RightSoleGapM), Is.LessThan(0.003f));

            // Geometric contact detector control: pairs known to be apart
            // read a real separation, pairs known to touch read ~0.
            var segs = bench.AthleteRig.Segments;
            Collider floor = bench.AthleteRig.PlatformCollider, pad = bench.Equipment.Pad;
            float headToFloor = BenchGeometry.Gap(segs["head_neck"].Collider, floor);
            float footToPad = BenchGeometry.Gap(segs["left_foot"].Collider, pad);
            float handToFloor = BenchGeometry.Gap(segs["right_hand"].Collider, floor);
            float footToFloor = BenchGeometry.VerticalClearance(segs["left_foot"].Collider, floor);
            float headToPad = BenchGeometry.VerticalClearance(segs["head_neck"].Collider, pad);
            // A region lifted clear reads its clearance (or the search cap).
            float handClearance = BenchGeometry.VerticalClearance(segs["right_hand"].Collider, floor);
            Assert.That(handClearance, Is.EqualTo(BenchGeometry.ClearanceSearchM), "clearance search should saturate for a raised hand");
            string gapReport = string.Format(C, "gap control: head-floor {0:F3} m, foot-pad {1:F3} m, hand-floor {2:F3} m | foot-floor {3:F4} m, head-pad {4:F4} m",
                headToFloor, footToPad, handToFloor, footToFloor, headToPad);
            H.Write("gateA-gap-control.txt", gapReport);
            Assert.That(headToFloor, Is.GreaterThan(0.25f), gapReport);
            Assert.That(footToPad, Is.GreaterThan(0.02f), gapReport);
            Assert.That(handToFloor, Is.GreaterThan(0.5f), gapReport);
            Assert.That(BenchGeometry.VerticalClearance(segs["pelvis"].Collider, pad), Is.LessThanOrEqualTo((float)BenchRulebook.ContactGapToleranceM), gapReport);
            Assert.That(footToFloor, Is.LessThanOrEqualTo((float)BenchRulebook.ContactGapToleranceM), gapReport);
            Assert.That(headToPad, Is.LessThanOrEqualTo((float)BenchRulebook.ContactGapToleranceM), gapReport);
        }

        [UnityTest]
        public IEnumerator GATE_B_RACKED_SUPPORT_REACTIONS_CARRY_THE_WHOLE_WEIGHT()
        {
            yield return H.LoadBench();
            yield return H.WaitTicks(200);
            ulong now = H.Tick;
            List<BenchTelemetrySample> w = H.Window(now - 100, now);
            float athleteWeight = H.Bench.AthleteRig.TotalMassKg * G;
            float barWeight = H.Bench.Barbell.Body.mass * G;

            double supportY = MeanSupportY(H.Bench);
            double head = H.Mean(w, s => s.HeadN), shoulders = H.Mean(w, s => s.ShouldersN), buttocks = H.Mean(w, s => s.ButtocksN);
            double lf = H.Mean(w, s => s.LeftFootN), rf = H.Mean(w, s => s.RightFootN), rack = H.Mean(w, s => s.BarRackN);
            string report = string.Format(C,
                "RACKED t={0}: head {1:F1} N, shoulders {2:F1} N, buttocks {3:F1} N, abdomen {4:F1} N, feet L {5:F1} R {6:F1} N, " +
                "bar rests {7:F1} N; vertical environment reaction on athlete+bar {8:F1} N vs weight {9:F1} N ({10:F2}%)",
                now, head, shoulders, buttocks, H.Mean(w, s => s.AbdomenN), lf, rf, rack, supportY, athleteWeight + barWeight,
                100.0 * (supportY - athleteWeight - barWeight) / (athleteWeight + barWeight));
            H.Write("gateB-racked.txt", report);
            H.WriteTelemetry("gateB-racked.csv");

            Assert.That(head, Is.GreaterThan(BenchRulebook.ContactForceFloorN), report);
            Assert.That(shoulders, Is.GreaterThan(BenchRulebook.ContactForceFloorN), report);
            Assert.That(buttocks, Is.GreaterThan(BenchRulebook.ContactForceFloorN), report);
            Assert.That(lf, Is.GreaterThan(BenchRulebook.ContactForceFloorN), report);
            Assert.That(rf, Is.GreaterThan(BenchRulebook.ContactForceFloorN), report);
            Assert.That(H.Bench.LastObservation.LeftFootFlat && H.Bench.LastObservation.RightFootFlat, report);
            Assert.That(rack, Is.GreaterThan(0.5 * barWeight), "racked bar must be carried mostly by the rests\n" + report);
            Assert.That(System.Math.Abs(supportY - athleteWeight - barWeight) / (athleteWeight + barWeight), Is.LessThan(0.05), report);
        }

        [UnityTest]
        public IEnumerator GATE_B_LIFTED_25KG_BAR_IS_CARRIED_THROUGH_BOTH_ARMS()
        {
            yield return H.LoadBench();
            yield return H.WaitTicks(100);
            H.Bench.RequestLiftOff();
            yield return H.WaitFor(() => H.Bench.Controller.Phase == BenchMotionPhase.Holding, 600, "lift-off to lockout");
            yield return H.WaitTicks(150);
            ulong now = H.Tick;
            List<BenchTelemetrySample> w = H.Window(now - 100, now);
            Rigidbody bar = H.Bench.Barbell.Body;
            float barWeight = bar.mass * G;

            // Joint.currentForce contract, measured by BenchJointForceFrameControlTests
            // with rotated bodies: world frame, force on the joint's own body (the
            // hand). The hands' force on the bar is the reaction, -(L + R).
            Vector3 rawSum = Vector3.zero, localSum = Vector3.zero;
            foreach (BenchTelemetrySample s in w)
                rawSum += s.LeftHandForceRaw + s.RightHandForceRaw;
            rawSum /= w.Count;
            Transform lh = H.Bench.AthleteRig.Segments["left_hand"].Body.transform;
            Transform rh = H.Bench.AthleteRig.Segments["right_hand"].Body.transform;
            localSum = lh.TransformDirection(H.Bench.LeftGrip.SolverForceRaw) + rh.TransformDirection(H.Bench.RightGrip.SolverForceRaw);
            Vector3 leftRaw = MeanV(w, s => s.LeftHandForceRaw), rightRaw = MeanV(w, s => s.RightHandForceRaw);
            Vector3 barAcc = MeanV(w, s => s.BarAcceleration);
            Vector3 contact = MeanV(w, s => s.BarContactForceWorld);
            double supportY = MeanSupportY(H.Bench);
            float athleteWeight = H.Bench.AthleteRig.TotalMassKg * G;

            var b = new StringBuilder();
            b.AppendLine(string.Format(C, "HOLD at lockout, ticks {0}-{1}, bar mass {2:F2} kg (weight {3:F1} N)", now - 100, now, bar.mass, barWeight));
            b.AppendLine(string.Format(C, "grip force raw mean L {0} R {1} sum {2} |sum| {3:F1} N", leftRaw.ToString("F1"), rightRaw.ToString("F1"), rawSum.ToString("F1"), rawSum.magnitude));
            b.AppendLine(string.Format(C, "grip force as hand-local->world (last tick) sum {0}", localSum.ToString("F1")));
            b.AppendLine(string.Format(C, "bar contacts (rests, chest, safeties...) mean {0} N; rests {1:F2} N chest {2:F2} N safety {3:F2} N",
                contact.ToString("F2"), H.Mean(w, s => s.BarRackN), H.Mean(w, s => s.BarChestN), H.Mean(w, s => s.BarSafetyN)));
            b.AppendLine(string.Format(C, "bar mean acceleration {0} m/s2, speed now {1:F4} m/s, angular speed {2:F4} rad/s",
                barAcc.ToString("F3"), bar.linearVelocity.magnitude, bar.angularVelocity.magnitude));
            b.AppendLine(string.Format(C, "bar FBD residual |m a - (m g + F_hands->bar + F_contact)|, F_hands->bar = -(L+R): {0:F2} N",
                (bar.mass * barAcc - (bar.mass * Physics.gravity - rawSum + contact)).magnitude));
            b.AppendLine(string.Format(C, "rejected alternative (raw read as force on the bar): {0:F2} N",
                (bar.mass * barAcc - (bar.mass * Physics.gravity + rawSum + contact)).magnitude));
            b.AppendLine(string.Format(C, "bar rotation from identity {0:F2} deg", Quaternion.Angle(Quaternion.identity, bar.rotation)));
            b.AppendLine(string.Format(C, "athlete+bar vertical environment reaction {0:F1} N vs weight {1:F1} N", supportY, athleteWeight + barWeight));
            b.AppendLine(string.Format(C, "elbows L {0:F2} R {1:F2} deg; grip drift max L {2:F3} R {3:F3} mm; slip max {4:F3} mm",
                H.Mean(w, s => s.LeftElbowFlexDeg), H.Mean(w, s => s.RightElbowFlexDeg),
                w.Max(s => s.LeftGripSeparationM) * 1000f, w.Max(s => s.RightGripSeparationM) * 1000f,
                w.Max(s => Mathf.Max(s.LeftSlipM, s.RightSlipM)) * 1000f));
            AppendArmEffort(b, w);
            AppendHiddenAttachmentAudit(b, H.Bench, out int foreignJoints);
            H.Write("gateB-hold.txt", b.ToString());
            H.WriteTelemetry("gateB-hold.csv");

            double fbdContract = (bar.mass * barAcc - (bar.mass * Physics.gravity - rawSum + contact)).magnitude;
            Assert.That(H.Mean(w, s => s.BarRackN) + H.Mean(w, s => s.BarChestN) + H.Mean(w, s => s.BarSafetyN), Is.LessThan(1.0),
                "held bar touches the rests, chest or safeties\n" + b);
            // Strict: the measured contract, no choice of sign or frame after the fact.
            Assert.That(fbdContract, Is.LessThan(0.05 * barWeight), "bar free-body diagram does not close with the measured hand forces\n" + b);
            Assert.That(rawSum.y, Is.LessThan(0f), "the bar must pull the hands down\n" + b);
            Assert.That(rawSum.magnitude, Is.EqualTo(barWeight).Within(0.05 * barWeight), "hands do not carry the bar weight\n" + b);
            Assert.That(leftRaw.magnitude, Is.GreaterThan(0.3f * barWeight), "left arm not carrying its share\n" + b);
            Assert.That(rightRaw.magnitude, Is.GreaterThan(0.3f * barWeight), "right arm not carrying its share\n" + b);
            // Motionless is a windowed property (the referee's Start already
            // required 30 consecutive still ticks); judge the window mean.
            Assert.That(w.Average(s => s.BarVelocity.magnitude), Is.LessThan((float)BenchRulebook.MotionlessSpeedMps), b.ToString());
            Assert.That(H.Bench.LastObservation.ElbowsLocked, b.ToString());
            Assert.That(w.Max(s => Mathf.Max(s.LeftGripSeparationM, s.RightGripSeparationM)), Is.LessThan(0.001f), "grip constraint drifts: overconstraint\n" + b);
            Assert.That(w.Max(s => s.ArmDemand.Max()), Is.LessThan(PoweredJointController.ModeledDemandSaturationThreshold), "an arm drive saturates at 25 kg\n" + b);
            Assert.That(foreignJoints, Is.EqualTo(0), "the bar has a joint other than the two grips\n" + b);
            Assert.That(System.Math.Abs(supportY - athleteWeight - barWeight) / (athleteWeight + barWeight), Is.LessThan(0.05), b.ToString());
        }

        [UnityTest]
        public IEnumerator GATE_B_AB_WITHOUT_ARM_ACTUATORS_THE_BAR_IS_NOT_HELD()
        {
            yield return H.LoadBench();
            yield return H.WaitTicks(100);
            H.Bench.RequestLiftOff();
            yield return H.WaitFor(() => H.Bench.Controller.Phase == BenchMotionPhase.Holding, 600, "lift-off to lockout");
            yield return H.WaitTicks(80);
            float heldY = H.Bench.Barbell.Body.position.y;
            ulong cut = H.Tick;
            H.Bench.Controller.ArmStrengthScale = 0f;
            yield return H.WaitTicks(80);
            float fellY = H.Bench.Barbell.Body.position.y;
            string report = string.Format(C, "arms at 0 capacity from tick {0}: bar y {1:F3} -> {2:F3} m in 0.8 s; chest {3:F1} N safety {4:F1} N",
                cut, heldY, fellY, H.Bench.LastObservation.BarChestForceN, H.Bench.LastObservation.BarSafetyForceN);
            H.Write("gateB-ab-no-actuators.txt", report);
            Assert.That(heldY - fellY, Is.GreaterThan(0.10f), "the bar stayed up without arm actuators: a hidden support exists\n" + report);
        }

        [UnityTest]
        public IEnumerator GATE_B_AB_ONE_HAND_CARRIES_THE_WHOLE_BAR_UNTIL_IT_TIPS()
        {
            yield return H.LoadBench();
            yield return H.WaitTicks(100);
            H.Bench.RequestLiftOff();
            yield return H.WaitFor(() => H.Bench.Controller.Phase == BenchMotionPhase.Holding, 600, "lift-off to lockout");
            yield return H.WaitTicks(80);
            ulong before = H.Tick;
            Vector3 twoHandLeft = MeanV(H.Window(before - 40, before), s => s.LeftHandForceRaw);
            H.Bench.RightGrip.Release();
            yield return H.WaitTicks(3);
            ulong after = H.Tick;
            Vector3 oneHandLeft = MeanV(H.Window(after - 1, after), s => s.LeftHandForceRaw);
            yield return H.WaitTicks(30);
            float tilt = Vector3.Angle(H.Bench.Barbell.Body.rotation * Vector3.right, Vector3.right);
            float barWeight = H.Bench.Barbell.Body.mass * G;
            string report = string.Format(C,
                "left grip force two hands {0} N (|{1:F1}|) -> right released: {2} N (|{3:F1}|); bar weight {4:F1} N; bar axis tilt after 0.3 s {5:F1} deg",
                twoHandLeft.ToString("F1"), twoHandLeft.magnitude, oneHandLeft.ToString("F1"), oneHandLeft.magnitude, barWeight, tilt);
            H.Write("gateB-ab-one-hand.txt", report);
            // Load path: with two hands the left carries about half the bar's
            // weight; the instant the right lets go, the left carries all of it.
            Assert.That(-twoHandLeft.y, Is.EqualTo(0.5f * barWeight).Within(0.2f * barWeight), "two-hand load share\n" + report);
            // The released bar starts to swing about the remaining hand, so that
            // hand is not fully weight-bearing while the bar accelerates.
            Assert.That(-oneHandLeft.y, Is.GreaterThan(1.4f * -twoHandLeft.y), "releasing one hand did not move its load to the other\n" + report);
            Assert.That(-oneHandLeft.y, Is.GreaterThan(0.70f * barWeight), "releasing one hand did not move its load to the other\n" + report);
            // Any tipping within 0.3 s shows nothing else holds the free end.
            Assert.That(tilt, Is.GreaterThan(2.5f), "a one-handed 25 kg bar should tip: something else is holding it\n" + report);
        }

        // ---------------- helpers ----------------

        private static Vector3 MeanV(List<BenchTelemetrySample> w, System.Func<BenchTelemetrySample, Vector3> f)
        {
            Vector3 sum = Vector3.zero;
            foreach (BenchTelemetrySample s in w)
                sum += f(s);
            return w.Count == 0 ? Vector3.zero : sum / w.Count;
        }

        /// <summary>Vertical force the environment (floor, bench, rack, safeties) applies to athlete and bar, last tick.</summary>
        private static double MeanSupportY(BenchPressPrototypeController bench)
        {
            BenchSurface[] env = { BenchSurface.Floor, BenchSurface.BenchPad, BenchSurface.BenchFrame, BenchSurface.BarRest, BenchSurface.Safety, BenchSurface.Upright };
            double sum = 0d;
            foreach (string id in bench.AthleteRig.Segments.Keys)
            {
                BenchContactSensor sensor = bench.Sensor(id);
                foreach (BenchSurface s in env)
                    sum += sensor.ForceVector(s).y;
            }
            BenchContactSensor bar = bench.Sensor("bar");
            foreach (BenchSurface s in env)
                sum += bar.ForceVector(s).y;
            return sum;
        }

        private static void AppendArmEffort(StringBuilder b, List<BenchTelemetrySample> w)
        {
            for (int i = 0; i < 6; i++)
            {
                b.AppendLine(string.Format(C, "  {0,-16} cmd {1,7:F2} real {2,7:F2} deg  demand max {3:F3}  solver |tau| mean {4:F2} N m",
                    BenchSetupSolver.ArmJointIds[i], H.Mean(w, s => s.ArmCommandedDeg[i]), H.Mean(w, s => s.ArmRealizedDeg[i]),
                    w.Max(s => s.ArmDemand[i]), H.Mean(w, s => s.ArmSolverTorqueNm[i])));
            }
        }

        private static void AppendHiddenAttachmentAudit(StringBuilder b, BenchPressPrototypeController bench, out int foreign)
        {
            Rigidbody bar = bench.Barbell.Body;
            foreign = 0;
            foreach (Joint joint in Object.FindObjectsByType<Joint>(FindObjectsSortMode.None))
            {
                bool touchesBar = joint.connectedBody == bar || joint.GetComponent<Rigidbody>() == bar;
                if (!touchesBar)
                    continue;
                bool isGrip = joint == bench.LeftGrip.Joint || joint == bench.RightGrip.Joint;
                if (!isGrip)
                    foreign++;
                b.AppendLine($"  bar joint: {joint.GetType().Name} on {joint.gameObject.name} -> {(joint.connectedBody ? joint.connectedBody.name : "WORLD")} {(isGrip ? "(grip)" : "(FOREIGN)")}");
            }
            b.AppendLine($"  bar isKinematic={bar.isKinematic} useGravity={bar.useGravity} mass={bar.mass:F2}");
            if (bar.isKinematic || !bar.useGravity)
                foreign++;
        }

        private static void AppendSignProbe(StringBuilder b, BenchPressPrototypeController bench)
        {
            BenchKinematics kin = bench.Solver.Kinematics;
            Dictionary<string, Pose> basePose = kin.Solve(bench.Model.PelvisWorld, bench.Model.SetupLogical);
            foreach (string id in new[] { "left_upper_arm", "left_forearm", "left_hand", "right_upper_arm", "right_forearm", "right_hand", "left_thigh", "left_shank", "left_foot", "head_neck" })
            {
                var logical = new Dictionary<string, Quaternion>(bench.Model.SetupLogical);
                logical[id] = (logical.TryGetValue(id, out Quaternion q) ? q : Quaternion.identity) * Quaternion.AngleAxis(10f, Vector3.right);
                Dictionary<string, Pose> moved = kin.Solve(bench.Model.PelvisWorld, logical);
                Vector3 axisWorld = basePose[kin.Joint(id).ParentId].rotation * kin.Joint(id).NeutralParentToChild * kin.Joint(id).JointSpace * Vector3.right;
                Vector3 d = moved[id].position - basePose[id].position;
                b.AppendLine(string.Format(C, "  {0,-15} axis {1} child COM moves {2} mm", id, axisWorld.ToString("F2"), (d * 1000f).ToString("F1")));
            }
        }

        private static void AppendLimitOccupancy(StringBuilder b, BenchPlantModel m)
        {
            for (int j = 0; j < 6; j++)
            {
                PhysicalJointRecipe recipe = PhysicalAthleteDefinition.Joints.First(r => r.ChildId == BenchSetupSolver.ArmJointIds[j]);
                float twist = 0f, swing = 0f;
                foreach (BenchArmSample sample in m.PressPath.Samples.Concat(m.LiftPath.Samples))
                {
                    float x = PoweredJointController.SignedTwistRadians(sample.Arm[j], Vector3.right) * Mathf.Rad2Deg;
                    twist = Mathf.Max(twist, x >= 0f ? x / Mathf.Max(0.001f, recipe.HighDegrees) : x / Mathf.Min(-0.001f, recipe.LowDegrees));
                    if (recipe.Kind == PhysicalJointKind.Ball)
                    {
                        swing = Mathf.Max(swing, BenchKinematics.SwingConeDegrees(sample.Arm[j]) / recipe.SecondaryLimitDegrees);
                    }
                }
                b.AppendLine(string.Format(C, "  {0,-16} twist {1:F2} swing {2:F2}", BenchSetupSolver.ArmJointIds[j], twist, swing));
            }
        }
    }
}
