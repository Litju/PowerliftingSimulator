using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Equipment;
using PowerliftingSimulator.Foundation;
using UnityEngine;
using UnityEngine.TestTools;
using F = PowerliftingSimulator.Deadlift.IsolatedBarWorld;

namespace PowerliftingSimulator.Deadlift
{
    /// <summary>
    /// GAM-70 Gate A1: a dynamically simulated 75 kg barbell resting on the
    /// platform through its two 450 mm plates, on the real solver. Every
    /// assertion is a measured physical balance or an A/B control; nothing
    /// moves the bar except gravity and the solver's contacts.
    ///
    /// 75 kg is the lightest load with full-diameter plates (a 25 kg plate per
    /// side); the 25 kg bar of the accepted squat and bench has no plates and
    /// serves as the no-plate control.
    /// </summary>
    public sealed class DeadliftGroundSupportQualificationTests
    {
        private const float DeadliftLoadKg = 75f;
        private const float ClearanceM = 0.020f;
        private IsolatedBarWorld _world;

        private static float G => Mathf.Abs(Physics.gravity.y);
        private static readonly float Dt = (float)F.TickSeconds;

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (_world != null)
            {
                yield return _world.Teardown();
                _world = null;
            }
        }

        // ------------------------------------------------------------------
        // Mass, centre of mass, inertia and collider geometry

        [UnityTest]
        public IEnumerator A1_BAR_MASS_COM_INERTIA_AND_COLLIDERS_ARE_WHAT_THE_SOLVER_RECEIVES()
        {
            _world = F.Create(DeadliftLoadKg, new Vector3(0f, 1.0f, 0f));
            Rigidbody b = _world.Body;
            var report = new StringBuilder();

            DeadliftBarReference.Inertia(out double ix, out double iy);
            report.AppendLine($"Rigidbody mass {b.mass} kg, COM {b.centerOfMass.ToString("F6")}, inertia {b.inertiaTensor.ToString("F5")}, inertiaRotation {b.inertiaTensorRotation.eulerAngles.ToString("F3")}");
            report.AppendLine($"closed form: Ixx {ix:F5}, Iyy=Izz {iy:F5} kg m2");
            report.AppendLine($"gravity {Physics.gravity}, linearDamping {b.linearDamping}, angularDamping {b.angularDamping}, solver {b.solverIterations}/{b.solverVelocityIterations}, collision {b.collisionDetectionMode}, interpolation {b.interpolation}, sleepThreshold {b.sleepThreshold}");

            Assert.That(b.mass, Is.EqualTo(DeadliftLoadKg).Within(1e-4f));
            Assert.That(b.centerOfMass.magnitude, Is.LessThan(1e-5f), "symmetric load: COM on the bar centre\n" + report);
            Assert.That(b.inertiaTensor.x, Is.EqualTo((float)ix).Within((float)ix * 2e-4f), report.ToString());
            Assert.That(b.inertiaTensor.y, Is.EqualTo((float)iy).Within((float)iy * 2e-4f), report.ToString());
            Assert.That(b.inertiaTensor.z, Is.EqualTo((float)iy).Within((float)iy * 2e-4f), report.ToString());
            Assert.That(Quaternion.Angle(b.inertiaTensorRotation, Quaternion.identity), Is.LessThan(0.01f), report.ToString());

            // Ownership: one dynamic rigid body, no joints, no constraints, gravity on.
            Assert.That(b.GetComponentsInChildren<Rigidbody>(true), Has.Length.EqualTo(1));
            Assert.That(b.GetComponentsInChildren<Joint>(true), Is.Empty, "the bar must hang from nothing");
            Assert.That(b.isKinematic, Is.False);
            Assert.That(b.useGravity, Is.True);
            Assert.That(b.constraints, Is.EqualTo(RigidbodyConstraints.None));

            // Colliders: shaft, 2 sleeves, 2 shoulders, 2 collars, 2 plate aggregates.
            Collider[] colliders = b.GetComponentsInChildren<Collider>(true);
            report.AppendLine("colliders:");
            foreach (Collider c in colliders)
                report.AppendLine($"  {c.name,-26} {c.GetType().Name,-16} active {c.gameObject.activeInHierarchy} trigger {c.isTrigger} attachedRigidbody {(c.attachedRigidbody == b)}");
            Assert.That(colliders, Has.Length.EqualTo(9));
            Assert.That(colliders.All(c => c.attachedRigidbody == b && !c.isTrigger), Is.True);

            // Plate geometry: a 24-sided convex prism, vertices on the 225 mm radius,
            // between x = 0.705 and 0.760 m on the right sleeve and mirrored on the left.
            foreach ((MeshCollider plate, float sign) in new[] { (_world.LeftPlate, -1f), (_world.RightPlate, 1f) })
            {
                Assert.That(plate, Is.Not.Null);
                Assert.That(plate.convex, Is.True);
                Assert.That(plate.gameObject.activeInHierarchy, Is.True);
                Mesh mesh = plate.sharedMesh;
                Matrix4x4 m = b.transform.worldToLocalMatrix * plate.transform.localToWorldMatrix;
                float rMax = 0f, rMin = float.MaxValue, xMin = float.MaxValue, xMax = float.MinValue;
                int rim = 0;
                foreach (Vector3 v in mesh.vertices)
                {
                    Vector3 w = m.MultiplyPoint3x4(v);
                    float radius = new Vector2(w.y, w.z).magnitude;
                    if (radius > 1e-4f)
                    {
                        rim++;
                        rMax = Mathf.Max(rMax, radius);
                        rMin = Mathf.Min(rMin, radius);
                    }
                    xMin = Mathf.Min(xMin, w.x);
                    xMax = Mathf.Max(xMax, w.x);
                }
                int sides = rim / 2;
                double apothem = DeadliftBarReference.Apothem(sides);
                report.AppendLine($"{plate.name}: {sides}-gon rim radius {rMin:F5}..{rMax:F5} m, x {xMin:F5}..{xMax:F5} m, apothem {apothem:F5} m, sagitta {(DeadliftBarReference.PlateRadius - apothem) * 1000:F3} mm");
                Assert.That(sides, Is.EqualTo(24), report.ToString());
                Assert.That(rMax, Is.EqualTo(0.225f).Within(1e-4f), report.ToString());
                Assert.That(rMin, Is.EqualTo(0.225f).Within(1e-4f), report.ToString());
                float innerX = sign > 0f ? xMin : -xMax;
                float outerX = sign > 0f ? xMax : -xMin;
                Assert.That(innerX, Is.EqualTo(0.705f).Within(1e-4f), report.ToString());
                Assert.That(outerX, Is.EqualTo(0.760f).Within(1e-4f), report.ToString());
            }

            // Dynamic identification of the same properties, free of gravity and contact.
            b.useGravity = false;
            var runtime = _world.Bootstrap.Runtime;
            float substep = (float)SimulationConstants.PhysicsSubstepSeconds;
            float angularDecay = Mathf.Pow(1f - b.angularDamping * substep, 2);
            float linearDecay = Mathf.Pow(1f - b.linearDamping * substep, 2);
            report.AppendLine($"identification decay over one tick (damping): angular {angularDecay:F6}, linear {linearDecay:F6}");

            Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
            float[] principal = { (float)ix, (float)iy, (float)iy };
            for (int a = 0; a < 3; a++)
            {
                runtime.Reset();
                b.useGravity = false;
                const float torqueImpulse = 1.0f;
                b.AddTorque(axes[a] * torqueImpulse, ForceMode.Impulse);
                runtime.StepOne();
                float measured = Vector3.Dot(b.angularVelocity, axes[a]);
                float expected = torqueImpulse / principal[a] * angularDecay;
                Vector3 offAxis = b.angularVelocity - axes[a] * measured;
                report.AppendLine($"torque impulse about {axes[a]}: omega {measured:F6} (expected {expected:F6}, off-axis {offAxis.magnitude:E2}) rad/s");
                Assert.That(measured, Is.EqualTo(expected).Within(expected * 0.01f), report.ToString());
                Assert.That(offAxis.magnitude, Is.LessThan(expected * 0.01f), report.ToString());
            }

            runtime.Reset();
            b.useGravity = false;
            b.AddForce(Vector3.forward * 10f, ForceMode.Impulse);
            runtime.StepOne();
            float expectedV = 10f / DeadliftLoadKg * linearDecay;
            report.AppendLine($"linear impulse 10 N s at the centre: v {b.linearVelocity.z:F6} m/s (expected {expectedV:F6}), omega {b.angularVelocity.magnitude:E2}");
            Assert.That(b.linearVelocity.z, Is.EqualTo(expectedV).Within(expectedV * 0.005f), report.ToString());
            Assert.That(b.angularVelocity.magnitude, Is.LessThan(1e-4f), "an impulse through the centre of mass must not spin the bar\n" + report);

            runtime.Reset();
            b.useGravity = false;
            b.AddForceAtPosition(Vector3.forward * 10f, b.position + Vector3.right * 0.5f, ForceMode.Impulse);
            runtime.StepOne();
            float expectedWy = -0.5f * 10f / (float)iy * angularDecay;
            report.AppendLine($"linear impulse 10 N s at x=+0.5 m: omega_y {b.angularVelocity.y:F6} (expected {expectedWy:F6}) rad/s");
            Assert.That(b.angularVelocity.y, Is.EqualTo(expectedWy).Within(Mathf.Abs(expectedWy) * 0.01f), report.ToString());

            F.WriteEvidence("gateA1-mass-com-inertia.txt", report.ToString());
            yield return null;
        }

        // ------------------------------------------------------------------
        // Release from a measured clearance, landing, resting, free fall

        [UnityTest]
        public IEnumerator A1_75KG_RELEASED_FROM_20MM_LANDS_ON_BOTH_PLATES_AND_RESTS_WITH_THE_WEIGHT_CARRIED_BY_THEM()
        {
            float roll0 = F.FacetFlatRollDeg();
            float startY = F.AxisYForClearance(ClearanceM, roll0);
            _world = F.Create(DeadliftLoadKg, new Vector3(0f, startY, 0f), initialRollDeg: roll0);
            float gap0 = _world.LowestPlateVertexY() - F.PlatformTopY;
            Assert.That(gap0, Is.EqualTo(ClearanceM).Within(2e-4f), "the release clearance is measured, not assumed");

            _world.Step(400);
            List<BarSample> s = _world.Samples;
            Assert.That(s, Has.Count.EqualTo(400));
            float m = DeadliftLoadKg;
            var report = new StringBuilder();
            report.AppendLine($"release: axis y {startY:F5} m, lowest plate vertex {gap0 * 1000:F3} mm above the platform top (y {F.PlatformTopY} m); gravity {G}; mass {m} kg");

            int firstContact = s.FindIndex(x => x.LeftImpulse.magnitude + x.RightImpulse.magnitude + x.OtherImpulse.magnitude > 0f);
            WriteTrace("gateA1-75kg-release20mm-trace.csv", s);
            Assert.That(firstContact, Is.GreaterThan(5), "no contact during the free fall\n" + report);

            // Free fall: v^2 = 2 g (y0 - y), the only forces are gravity and the tiny linear damping.
            double worstFall = 0;
            for (int k = 0; k < firstContact; k++)
            {
                double fallen = startY - s[k].Position.y;
                double v2 = s[k].Velocity.y * s[k].Velocity.y;
                worstFall = Math.Max(worstFall, Math.Abs(v2 - 2 * G * fallen) / (2 * G));
                Assert.That(s[k].LeftImpulse.magnitude + s[k].RightImpulse.magnitude + s[k].OtherImpulse.magnitude, Is.Zero);
            }
            double impactSpeed = -s[firstContact - 1].Velocity.y;
            report.AppendLine($"free fall over {firstContact} ticks: max |v^2/2g - fall| {worstFall * 1000:F3} mm, speed before first contact tick {impactSpeed:F4} m/s (sqrt(2 g c) = {Math.Sqrt(2 * G * ClearanceM):F4})");
            Assert.That(worstFall, Is.LessThan(0.0015), report.ToString());

            // Tick-wise impulse-momentum balance of the bar: m dv = J_contact - m g dt (vertical).
            double worstResidual = 0;
            int worstTick = -1;
            double residualAtRest = 0;
            for (int k = 1; k < s.Count; k++)
            {
                double dv = s[k].Velocity.y - s[k - 1].Velocity.y;
                double jy = s[k].LeftImpulse.y + s[k].RightImpulse.y + s[k].OtherImpulse.y;
                double residual = m * dv - (jy - m * G * Dt);
                if (Math.Abs(residual) > worstResidual)
                {
                    worstResidual = Math.Abs(residual);
                    worstTick = k;
                }
                if (k >= 300)
                    residualAtRest = Math.Max(residualAtRest, Math.Abs(residual));
            }
            report.AppendLine($"momentum residual |m dv - (J - m g dt)|: worst {worstResidual:F4} N s at tick {worstTick}, worst at rest {residualAtRest:F4} N s (m g dt = {m * G * Dt:F3} N s, landing momentum m v = {m * impactSpeed:F2} N s)");

            // Rest.
            List<BarSample> rest = s.Skip(300).ToList();
            double nLeft = rest.Average(x => x.LeftImpulse.y) / Dt;
            double nRight = rest.Average(x => x.RightImpulse.y) / Dt;
            double nOther = rest.Average(x => x.OtherImpulse.magnitude) / Dt;
            double weight = m * G;
            Vector3 meanPos = new Vector3(rest.Average(x => x.Position.x), rest.Average(x => x.Position.y), rest.Average(x => x.Position.z));
            float restY = meanPos.y;
            float yRange = rest.Max(x => x.Position.y) - rest.Min(x => x.Position.y);
            float lowestVertex = rest.Average(x => x.LowestPlateVertexY) - F.PlatformTopY;
            float minVertexEver = s.Min(x => x.LowestPlateVertexY) - F.PlatformTopY;
            float tilt = rest.Max(x => F.AxisTiltDeg(x.Rotation));
            float roll = F.RollAboutAxisDeg(rest[rest.Count - 1].Rotation);
            float vMax = rest.Max(x => x.Velocity.magnitude);
            float wMax = rest.Max(x => x.AngularVelocity.magnitude);
            double bounce = s.Skip(firstContact).Max(x => (double)x.Velocity.y);
            double apothem = DeadliftBarReference.Apothem(24);
            report.AppendLine($"rest (ticks 300-399): axis y {restY:F5} m (24-gon flat face {apothem:F5}, true 450 mm circle {DeadliftBarReference.PlateRadius:F5}), y range {yRange * 1e6:F1} um, x {meanPos.x * 1000:F3} mm z {meanPos.z * 1000:F3} mm");
            report.AppendLine($"rest: lowest plate vertex {lowestVertex * 1000:F3} mm above the platform; deepest vertex over the run {minVertexEver * 1000:F3} mm; roll about axis {roll:F2} deg; axis tilt {tilt:F4} deg; |v| max {vMax:E2} m/s; |w| max {wMax:E2} rad/s");
            report.AppendLine($"rest reactions: left plate {nLeft:F3} N, right plate {nRight:F3} N, other bar colliders {nOther:F3} N; sum {nLeft + nRight:F3} N vs weight {weight:F3} N ({100 * (nLeft + nRight - weight) / weight:F3} %)");
            report.AppendLine($"rest contact points: left {rest.Average(x => x.LeftPoints):F2}, right {rest.Average(x => x.RightPoints):F2}; colliders ever touching the ground: {string.Join(", ", _world.Sensor.TouchedGroundEver)}");
            report.AppendLine($"max upward velocity after first contact (bounce): {bounce * 1000:F3} mm/s");
            F.WriteEvidence("gateA1-75kg-release20mm.txt", report.ToString());

            Assert.That(worstResidual, Is.LessThan(0.5), "contact impulses do not close the bar's momentum balance\n" + report);
            Assert.That(nLeft + nRight, Is.EqualTo(weight).Within(0.01 * weight), report.ToString());
            Assert.That(nLeft, Is.GreaterThan(0.45 * weight).And.LessThan(0.55 * weight), "both plates carry the load\n" + report);
            Assert.That(nRight, Is.GreaterThan(0.45 * weight).And.LessThan(0.55 * weight), report.ToString());
            Assert.That(nOther, Is.LessThan(0.002 * weight), "only the plates touch the platform\n" + report);
            Assert.That(_world.Sensor.TouchedGroundEver.OrderBy(x => x).ToArray(),
                Is.EqualTo(new[] { "LeftPlateAggregateCollider", "RightPlateAggregateCollider" }), report.ToString());
            Assert.That(restY, Is.EqualTo((float)apothem).Within(0.0003f), "facet-flat rest height: " + report);
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(roll, F.FacetFlatRollDeg())), Is.LessThan(0.5f), "the bar left its facet: " + report);
            Assert.That(minVertexEver, Is.GreaterThan(-0.002f), "plate vertices penetrate the platform\n" + report);
            Assert.That(lowestVertex, Is.GreaterThan(-0.001f).And.LessThan(0.003f), report.ToString());
            Assert.That(yRange, Is.LessThan(5e-5f), "resting height is not stable\n" + report);
            Assert.That(tilt, Is.LessThan(0.2f), report.ToString());
            Assert.That(Mathf.Abs(meanPos.x) + Mathf.Abs(meanPos.z), Is.LessThan(0.005f), "the bar drifted horizontally\n" + report);
            Assert.That(vMax, Is.LessThan(2e-3f), report.ToString());
            Assert.That(wMax, Is.LessThan(2e-2f), report.ToString());
            Assert.That(bounce, Is.LessThan(0.02), "restitution should be near zero\n" + report);
            AssertPositionFollowsVelocity(s, 0.0005f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator A1_75KG_SEPARATES_WHEN_TOSSED_AND_RECONTACTS_WITHOUT_PENETRATION_OR_STICKING()
        {
            float roll0 = F.FacetFlatRollDeg();
            float startY = F.AxisYForClearance(ClearanceM, roll0);
            _world = F.Create(DeadliftLoadKg, new Vector3(0f, startY, 0f), initialRollDeg: roll0);
            _world.Step(300);
            int settledCount = _world.Samples.Count;
            float restBefore = _world.Samples[settledCount - 1].Position.y;

            // The toss is an initial condition (a velocity written once at rest),
            // not a force applied during the measured motion.
            const float toss = 0.8f;
            _world.Body.linearVelocity = new Vector3(0f, toss, 0f);
            _world.Step(250);

            List<BarSample> s = _world.Samples.Skip(settledCount).ToList();
            float apex = s.Max(x => x.Position.y);
            float rise = apex - restBefore;
            double predicted = toss * toss / (2 * G);
            int lift = s.FindIndex(x => x.LeftImpulse.magnitude + x.RightImpulse.magnitude + x.OtherImpulse.magnitude == 0f);
            int recontact = lift < 0 ? -1 : s.FindIndex(lift, x => x.LeftImpulse.magnitude + x.RightImpulse.magnitude > 0f);
            double flightImpulse = lift < 0 || recontact < 0 ? double.NaN
                : s.Skip(lift).Take(recontact - lift).Sum(x => (double)(x.LeftImpulse.magnitude + x.RightImpulse.magnitude + x.OtherImpulse.magnitude));
            WriteTrace("gateA1-75kg-toss-trace.csv", s);
            List<BarSample> rest = s.Skip(150).ToList();
            float restAfter = rest.Average(x => x.Position.y);
            float minVertex = s.Min(x => x.LowestPlateVertexY) - F.PlatformTopY;
            double nSum = rest.Average(x => x.LeftImpulse.y + x.RightImpulse.y) / Dt;
            double bounceAfterRecontact = recontact < 0 ? double.NaN : s.Skip(recontact + 3).Max(x => (double)x.Velocity.y);
            float drift = new Vector2(rest.Average(x => x.Position.x) - 0f, rest.Average(x => x.Position.z)).magnitude;
            var report = new StringBuilder();
            report.AppendLine($"toss {toss} m/s from rest at y {restBefore:F5} m: apex rise {rise * 1000:F3} mm (ballistic {predicted * 1000:F3} mm), flight ticks {recontact - lift}, ground impulse during flight {flightImpulse:E2} N s");
            report.AppendLine($"after recontact: max upward velocity {bounceAfterRecontact * 1000:F3} mm/s, deepest plate vertex {minVertex * 1000:F3} mm, rest y {restAfter:F5} m (before {restBefore:F5}), sum plate reaction {nSum:F3} N vs weight {DeadliftLoadKg * G:F3} N, horizontal drift {drift * 1000:F3} mm");
            F.WriteEvidence("gateA1-75kg-toss-recontact.txt", report.ToString());

            Assert.That(lift, Is.GreaterThanOrEqualTo(0), "the bar never separated\n" + report);
            Assert.That(recontact, Is.GreaterThan(lift), "the bar never came back\n" + report);
            Assert.That(rise, Is.EqualTo((float)predicted).Within((float)predicted * 0.05f + 0.001f), "the flight is not ballistic\n" + report);
            Assert.That(flightImpulse, Is.LessThan(1e-4), "ground reaction while airborne: a sticky or phantom support\n" + report);
            Assert.That(minVertex, Is.GreaterThan(-0.002f), report.ToString());
            Assert.That(bounceAfterRecontact, Is.LessThan(0.02), report.ToString());
            Assert.That(restAfter, Is.EqualTo(restBefore).Within(0.0005f), "the support height changes between landings\n" + report);
            Assert.That(nSum, Is.EqualTo(DeadliftLoadKg * G).Within(0.01 * DeadliftLoadKg * G), report.ToString());
            Assert.That(drift, Is.LessThan(0.005f), report.ToString());
            AssertPositionFollowsVelocity(s, 0.0005f);
            yield return null;
        }

        // ------------------------------------------------------------------
        // A/B controls

        [UnityTest]
        public IEnumerator A1_AB_THE_25KG_NO_PLATE_BAR_RESTS_ON_ITS_COLLARS_NOT_ON_PLATES()
        {
            _world = F.Create(25f, new Vector3(0f, 0.060f + ClearanceM, 0f));
            Assert.That(_world.LeftPlate.gameObject.activeSelf, Is.False, "the 25 kg bar has no plate collider");
            _world.Step(300);
            List<BarSample> rest = _world.Samples.Skip(200).ToList();
            double nOther = rest.Average(x => x.OtherImpulse.y) / Dt;
            double nPlates = rest.Average(x => x.LeftImpulse.magnitude + x.RightImpulse.magnitude) / Dt;
            float restY = rest.Average(x => x.Position.y);
            double weight = 25 * G;
            var report = new StringBuilder();
            report.AppendLine($"25 kg control: rest axis y {restY:F5} m (collar half-height 0.06), reaction {nOther:F3} N vs weight {weight:F3} N, plate reaction {nPlates:E2} N");
            report.AppendLine($"colliders touching the ground: {string.Join(", ", _world.Sensor.TouchedGroundEver)}; tilt {rest.Max(x => F.AxisTiltDeg(x.Rotation)):F4} deg; |v| max {rest.Max(x => x.Velocity.magnitude):E2}");
            F.WriteEvidence("gateA1-ab-25kg-no-plate.txt", report.ToString());

            Assert.That(restY, Is.EqualTo(0.060f).Within(0.0015f), report.ToString());
            Assert.That(nOther, Is.EqualTo(weight).Within(0.01 * weight), report.ToString());
            Assert.That(nPlates, Is.Zero);
            Assert.That(_world.Sensor.TouchedGroundEver.OrderBy(x => x).ToArray(),
                Is.EqualTo(new[] { "LeftCollarCollider", "RightCollarCollider" }), report.ToString());
            yield return null;
        }

        [UnityTest]
        public IEnumerator A1_AB_WITHOUT_ITS_PLATE_COLLIDERS_THE_75KG_BAR_SINKS_TO_THE_COLLARS()
        {
            float startY = (float)DeadliftBarReference.PlateRadius + ClearanceM;
            _world = F.Create(DeadliftLoadKg, new Vector3(0f, startY, 0f));
            // Negative control only: remove the plate colliders and watch the support height collapse.
            _world.LeftPlate.gameObject.SetActive(false);
            _world.RightPlate.gameObject.SetActive(false);
            Physics.SyncTransforms();
            _world.Step(300);
            List<BarSample> rest = _world.Samples.Skip(200).ToList();
            float restY = rest.Average(x => x.Position.y);
            double nOther = rest.Average(x => x.OtherImpulse.y) / Dt;
            var report = $"plate colliders disabled, 75 kg: rest axis y {restY:F5} m (collars 0.060), reaction {nOther:F3} N vs weight {DeadliftLoadKg * G:F3} N; touching: {string.Join(", ", _world.Sensor.TouchedGroundEver)}";
            F.WriteEvidence("gateA1-ab-no-plate-colliders.txt", report);

            Assert.That(restY, Is.LessThan(0.10f), "the 0.223 m support height must come from the plate colliders\n" + report);
            Assert.That(restY, Is.EqualTo(0.060f).Within(0.0015f), report);
            yield return null;
        }

        [UnityTest]
        public IEnumerator A1_AB_WITHOUT_THE_PLATFORM_THE_BAR_FREE_FALLS_SO_NOTHING_ELSE_HOLDS_IT_UP()
        {
            float startY = (float)DeadliftBarReference.PlateRadius + ClearanceM;
            _world = F.Create(DeadliftLoadKg, new Vector3(0f, startY, 0f), true, false);
            _world.Step(100);
            BarSample last = _world.Samples[_world.Samples.Count - 1];
            double t = 100 * F.TickSeconds;
            double predictedDrop = 0.5 * G * t * t;
            double drop = startY - last.Position.y;
            var report = $"no platform: after {t:F2} s the bar fell {drop:F3} m (ballistic {predictedDrop:F3} m), v {last.Velocity.y:F3} m/s";
            F.WriteEvidence("gateA1-ab-no-platform.txt", report);
            Assert.That(drop, Is.EqualTo(predictedDrop).Within(0.02 * predictedDrop), report);
            Assert.That(_world.Samples.All(x => x.LeftImpulse.magnitude + x.RightImpulse.magnitude + x.OtherImpulse.magnitude == 0f), Is.True, report);
            yield return null;
        }

        [UnityTest]
        public IEnumerator A1_AB_SLEEPING_ENABLED_GIVES_THE_SAME_RESTING_POSE_AS_THE_AWAKE_OBSERVATION_BAR()
        {
            float roll0 = F.FacetFlatRollDeg();
            float startY = F.AxisYForClearance(ClearanceM, roll0);
            _world = F.Create(DeadliftLoadKg, new Vector3(0f, startY, 0f), observationAwake: true, initialRollDeg: roll0);
            _world.Step(400);
            BarSample awake = _world.Samples[_world.Samples.Count - 1];
            yield return _world.Teardown();
            _world = null;

            _world = F.Create(DeadliftLoadKg, new Vector3(0f, startY, 0f), observationAwake: false, initialRollDeg: roll0);
            Assert.That(_world.Body.sleepThreshold, Is.EqualTo(0.005f).Within(1e-6f), "the production sleep threshold");
            _world.Step(400);
            BarSample asleep = _world.Samples[_world.Samples.Count - 1];
            var report = $"awake: y {awake.Position.y:F6} roll {F.RollAboutAxisDeg(awake.Rotation):F3}; production sleep threshold: y {asleep.Position.y:F6} roll {F.RollAboutAxisDeg(asleep.Rotation):F3}, sleeping {_world.Body.IsSleeping()}";
            F.WriteEvidence("gateA1-ab-sleep.txt", report);
            Assert.That(asleep.Position.y, Is.EqualTo(awake.Position.y).Within(0.0003f), report);
                    }

        // ------------------------------------------------------------------

        /// <summary>
        /// Anti-pinning audit: the position change over a tick must be what the
        /// reported velocities integrate to; a transform written by script would not be.
        /// </summary>
        [UnityTest]
        public IEnumerator A1_AB_VERTEX_DOWN_ROLLS_AWAY_AND_FACET_FLAT_STAYS_PUT_ON_THE_ACCEPTED_24_FACET_PLATES()
        {
            var table = new StringBuilder("sides | start roll deg | final axis y | y-0.225 mm | max |roll change| deg | z drift mm | final |v| mm/s | deepest vertex mm | momentum residual N s | reaction/weight" + System.Environment.NewLine);
            foreach (int sides in new[] { 24, 120 })
            {
                foreach (float roll0 in new[] { 0f, F.FacetFlatRollDeg(sides) })
                {
                    float startY = F.AxisYForClearance(ClearanceM, roll0, sides);
                    _world = F.Create(DeadliftLoadKg, new Vector3(0f, startY, 0f), plateSides: sides, initialRollDeg: roll0);
                    _world.Step(800);
                    List<BarSample> s = _world.Samples;
                    double residual = 0;
                    for (int k = 1; k < s.Count; k++)
                        residual = Math.Max(residual, Math.Abs(DeadliftLoadKg * (s[k].Velocity.y - s[k - 1].Velocity.y) -
                            (s[k].LeftImpulse.y + s[k].RightImpulse.y + s[k].OtherImpulse.y - DeadliftLoadKg * G * Dt)));
                    List<BarSample> tail = s.Skip(700).ToList();
                    double reaction = tail.Average(x => x.LeftImpulse.y + x.RightImpulse.y) / Dt;
                    float finalY = tail.Average(x => x.Position.y);
                    table.AppendLine(string.Join(" | ", sides, roll0.ToString("F2"), finalY.ToString("F5"), ((finalY - 0.225f) * 1000f).ToString("F3"),
                        s.Max(x => Mathf.Abs(Mathf.DeltaAngle(F.RollAboutAxisDeg(x.Rotation), roll0))).ToString("F2"),
                        (tail.Average(x => x.Position.z) * 1000f).ToString("F2"),
                        (tail.Max(x => x.Velocity.magnitude) * 1000f).ToString("F3"), ((s.Min(x => x.LowestPlateVertexY) - F.PlatformTopY) * 1000f).ToString("F3"),
                        residual.ToString("F3"), (reaction / (DeadliftLoadKg * G)).ToString("F4")));
                    WriteTrace("gateA1-facet-" + sides + "-roll" + roll0.ToString("F1") + "-trace.csv", s);
                    bool stable = tail.Max(x => x.Velocity.magnitude) < 2e-3f && Mathf.Abs(tail.Average(x => x.Position.z)) < 0.002f;
                    if (roll0 > 0f && sides == 24)
                        Assert.That(stable, Is.True, "facet-flat start must stay put, sides " + sides + "\n" + table);
                    Assert.That(residual, Is.LessThan(0.5), "momentum balance, sides " + sides + "\n" + table);
                    yield return _world.Teardown();
                    _world = null;
                }
            }
            F.WriteEvidence("gateA1-plate-facet-ab.txt", table.ToString());
        }

        private static void WriteTrace(string name, IReadOnlyList<BarSample> s)
        {
            var b = new StringBuilder("tick,x,y,z,roll_deg,tilt_deg,vx,vy,vz,wx,wy,wz,Jleft_y,Jright_y,Jother_y,ptsL,ptsR,lowestVertexY" + System.Environment.NewLine);
            foreach (BarSample k in s)
                b.AppendLine(string.Join(",", k.Tick, k.Position.x, k.Position.y, k.Position.z, F.RollAboutAxisDeg(k.Rotation), F.AxisTiltDeg(k.Rotation),
                    k.Velocity.x, k.Velocity.y, k.Velocity.z, k.AngularVelocity.x, k.AngularVelocity.y, k.AngularVelocity.z,
                    k.LeftImpulse.y, k.RightImpulse.y, k.OtherImpulse.y, k.LeftPoints, k.RightPoints, k.LowestPlateVertexY));
            F.WriteEvidence(name, b.ToString());
        }

        private static void AssertPositionFollowsVelocity(IReadOnlyList<BarSample> s, float toleranceM)
        {
            float worst = 0f;
            for (int k = 1; k < s.Count; k++)
            {
                Vector3 expected = 0.5f * (s[k].Velocity + s[k - 1].Velocity) * Dt;
                // Trapezoid error is bounded by half the tick's velocity change times dt; only the excess counts.
                float excess = (s[k].Position - s[k - 1].Position - expected).magnitude - 0.5f * (s[k].Velocity - s[k - 1].Velocity).magnitude * Dt;
                worst = Mathf.Max(worst, excess);
            }
            Assert.That(worst, Is.LessThan(toleranceM), $"position change disagrees with integrated velocity by {worst * 1000f:F3} mm per tick: the bar is being moved by something other than the solver");
        }
    }
}
