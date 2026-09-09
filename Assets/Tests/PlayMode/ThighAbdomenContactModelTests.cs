using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Foundation.Unity;
using PowerliftingSimulator.Squat;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.Tests
{
    /// <summary>
    /// Phase 5H11. H10 proved the bilateral thigh/abdomen contact is the first
    /// cause of dynamic squat failure. It did not prove the contact is wrong.
    /// Deep human flexion really does bring thigh and abdomen together, so
    /// before anything is filtered or resized this has to separate three
    /// different claims that H10 left fused:
    ///
    ///   - the proxies are the wrong size,
    ///   - the proxies are in the wrong place,
    ///   - or the proxies are right and the contact is real.
    ///
    /// Nothing here writes to a production body. Every hypothetical pose is
    /// evaluated with Physics.ComputePenetration, which takes the pose as an
    /// argument and does not require the collider to be moved there.
    /// </summary>
    public sealed class ThighAbdomenContactModelTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const string MeasurementDirectory = "Artifacts/Measurements/GAM-11";
        private const int SettleTicks = 60;
        private const int SquatTicks = 400;

        private FoundationBootstrap _bootstrap;
        private PhysicalAthleteRig _rig;
        private SquatPhysicalPrototypeController _controller;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(QualificationScene, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null, "The qualification scene is missing from the project.");
            while (!load.isDone)
                yield return null;
            yield return null;

            _bootstrap = UnityEngine.Object.FindFirstObjectByType<FoundationBootstrap>();
            _rig = UnityEngine.Object.FindFirstObjectByType<PhysicalAthleteRig>();
            _controller = UnityEngine.Object.FindFirstObjectByType<SquatPhysicalPrototypeController>();
            Assert.That(_bootstrap, Is.Not.Null);
            Assert.That(_rig, Is.Not.Null);
            Assert.That(_controller, Is.Not.Null);
            for (int frame = 0; frame < 8 && !_controller.IsInitialized; frame++)
                yield return null;
            Assert.That(_controller.IsInitialized, Is.True, _controller.StartupFailure);
            _bootstrap.enabled = false;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            LogAssert.ignoreFailingMessages = true;
            if (_controller != null)
                _controller.enabled = false;
            if (_rig != null)
                _rig.enabled = false;
            if (_bootstrap != null && _bootstrap.Runtime != null && _bootstrap.Runtime.IsInitialized)
            {
                AsyncOperation unload = _bootstrap.Runtime.Shutdown();
                while (unload != null && !unload.isDone)
                    yield return null;
            }
            if (_bootstrap != null)
                UnityEngine.Object.DestroyImmediate(_bootstrap.gameObject);
            _bootstrap = null;
            _rig = null;
            _controller = null;
            yield return null;
            LogAssert.ignoreFailingMessages = false;
        }

        // ------------------------------------------------------------------
        // T1. What the plant actually built, read off the running objects.
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator T1_THIGH_ABDOMEN_RUNTIME_COLLIDER_SEMANTICS()
        {
            var report = new StringBuilder();
            report.AppendLine("GAM-11 PHASE 5H11 T1 RUNTIME COLLIDER SEMANTICS");
            report.AppendLine("Values read from the running production objects, not from recipe names.");
            report.AppendLine();

            foreach (string id in new[] { "left_thigh", "right_thigh", "abdomen", "pelvis" })
                report.AppendLine(DescribeCollider(id));

            report.AppendLine();
            report.AppendLine("--- collider geometry to mass property coupling ---");
            foreach (string id in new[] { "left_thigh", "right_thigh", "abdomen" })
            {
                PhysicalAthleteRig.SegmentRuntime segment = _rig.Segments[id];
                Vector3 authored = PhysicalAthleteDefinition.BoxInertia(segment.Body.mass, segment.DimensionsMeters);
                Vector3 actual = segment.Body.inertiaTensor;
                bool coupled = (authored - actual).magnitude < 1e-5f;
                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,-12} mass={1:F3} kg com={2} dimensions={3} BoxInertia(dimensions)={4} inertiaTensor={5} coupled={6}",
                    id, segment.Body.mass, V(segment.Body.centerOfMass), V(segment.DimensionsMeters),
                    V(authored), V(actual), coupled));
            }
            report.AppendLine();
            report.AppendLine("COLLIDER_GEOMETRY_COUPLED_TO_MASS_PROPERTIES = YES for inertiaTensor " +
                              "(same dimensions vector feeds AddCollider and BoxInertia); " +
                              "NO for mass (MassFraction) and NO for centre of mass (authored zero).");

            report.AppendLine();
            report.AppendLine("--- hip anchor and thigh capsule registration ---");
            foreach (string thighId in new[] { "left_thigh", "right_thigh" })
                report.AppendLine(DescribeHipRegistration(thighId));

            report.AppendLine();
            report.AppendLine("--- abdomen registration ---");
            report.AppendLine(DescribeAbdomenRegistration());

            WriteMeasurement("GAM11-5h11-t1-collider-semantics.txt", report.ToString());
            Debug.Log(report.ToString());
            yield return null;
        }

        // ------------------------------------------------------------------
        // T2. Contact is not overlap. PhysX generates contacts across a
        // contactOffset gap, so the tick a contact appears and the tick the
        // surfaces actually intersect are different measurements.
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator T2_CONTACT_VERSUS_GEOMETRIC_PENETRATION_TIMING()
        {
            _controller.SetLoad(0f);
            SquatPhysicalAdapter adapter = _controller.Adapter;
            adapter.BalanceCorrectionsEnabled = true;
            FoundationRuntime runtime = _bootstrap.Runtime;
            PoweredJointController powered = _rig.PoweredController;
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;

            Collider leftThigh = _rig.Segments["left_thigh"].Collider;
            Collider rightThigh = _rig.Segments["right_thigh"].Collider;
            Collider abdomen = _rig.Segments["abdomen"].Collider;

            // This fixture characterizes the contact, so it has to hold the
            // contact open. Once the qualified exception is in the production
            // policy this measurement would otherwise report nothing and the
            // evidence for the classification would quietly erase itself.
            Physics.IgnoreCollision(leftThigh, abdomen, false);
            Physics.IgnoreCollision(rightThigh, abdomen, false);

            PairProbe probe = PairProbe.Attach(_rig, new[] { "left_thigh", "right_thigh" }, "abdomen");

            for (int i = 0; i < SettleTicks; i++)
            {
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFootDetectors(dt);
            }

            var trace = new StringBuilder();
            trace.AppendLine("tick,hip_left_deg,hip_right_deg," +
                             "physx_contact,physx_impulse," +
                             "left_overlap,left_penetration_m,left_surface_gap_m," +
                             "right_overlap,right_penetration_m,right_surface_gap_m," +
                             "contact_offset_sum_m,hip_track_err_deg");

            int firstContactTick = -1;
            int firstOverlapTick = -1;
            int firstPositivePenetrationTick = -1;
            int firstLoadBearingTick = -1;
            float hipAtFirstContact = 0f;
            float hipAtFirstOverlap = 0f;
            float hipAtFirstLoadBearing = 0f;
            float contactOffsetSum = leftThigh.contactOffset + abdomen.contactOffset;

            adapter.StartSquat();
            for (int tick = 0; tick < SquatTicks; tick++)
            {
                probe.BeginTick();
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFootDetectors(dt);

                PoweredJointController.PoweredJointRuntime hipL = powered.GetJoint("left_thigh");
                PoweredJointController.PoweredJointRuntime hipR = powered.GetJoint("right_thigh");
                float hipLeftDeg = Mathf.Abs(SignedTwistDegrees(hipL.Diagnostic.ActualRelative, Vector3.right));
                float hipRightDeg = Mathf.Abs(SignedTwistDegrees(hipR.Diagnostic.ActualRelative, Vector3.right));
                float hipDeg = 0.5f * (hipLeftDeg + hipRightDeg);
                float trackErr = Quaternion.Angle(hipR.AppliedTarget, hipR.Diagnostic.ActualRelative);

                bool leftOverlap = MeasurePair(leftThigh, abdomen, out float leftDepth, out float leftGap);
                bool rightOverlap = MeasurePair(rightThigh, abdomen, out float rightDepth, out float rightGap);
                bool anyOverlap = leftOverlap || rightOverlap;
                float maxDepth = Mathf.Max(leftOverlap ? leftDepth : 0f, rightOverlap ? rightDepth : 0f);

                if (firstContactTick < 0 && probe.ContactThisTick)
                {
                    firstContactTick = tick;
                    hipAtFirstContact = hipDeg;
                }
                if (firstOverlapTick < 0 && anyOverlap)
                {
                    firstOverlapTick = tick;
                    hipAtFirstOverlap = hipDeg;
                }
                if (firstPositivePenetrationTick < 0 && maxDepth > 0.0005f)
                    firstPositivePenetrationTick = tick;
                if (firstLoadBearingTick < 0 && probe.ImpulseThisTick > 0.5f)
                {
                    firstLoadBearingTick = tick;
                    hipAtFirstLoadBearing = hipDeg;
                }

                trace.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0},{1:F2},{2:F2},{3},{4:F3},{5},{6:F5},{7:F5},{8},{9:F5},{10:F5},{11:F4},{12:F2}",
                    tick, hipLeftDeg, hipRightDeg,
                    probe.ContactThisTick ? 1 : 0, probe.ImpulseThisTick,
                    leftOverlap ? 1 : 0, leftDepth, leftGap,
                    rightOverlap ? 1 : 0, rightDepth, rightGap,
                    contactOffsetSum, trackErr));
            }

            var report = new StringBuilder();
            report.AppendLine("GAM-11 PHASE 5H11 T2 CONTACT VERSUS GEOMETRIC PENETRATION");
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "thigh contactOffset={0:F4} m  abdomen contactOffset={1:F4} m  sum={2:F4} m",
                leftThigh.contactOffset, abdomen.contactOffset, contactOffsetSum));
            report.AppendLine();
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "FIRST_CONTACT_TICK              = {0}   hip {1:F1} deg", firstContactTick, hipAtFirstContact));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "FIRST_GEOMETRIC_OVERLAP_TICK    = {0}   hip {1:F1} deg", firstOverlapTick, hipAtFirstOverlap));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "FIRST_POSITIVE_PENETRATION_TICK = {0}", firstPositivePenetrationTick));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "FIRST_LOAD_BEARING_CONTACT_TICK = {0}   hip {1:F1} deg", firstLoadBearingTick, hipAtFirstLoadBearing));
            report.AppendLine();
            int offsetLead = firstOverlapTick >= 0 && firstContactTick >= 0
                ? firstOverlapTick - firstContactTick
                : -1;
            report.AppendLine("CONTACT_OFFSET_CONTRIBUTION: the contact leads real overlap by " +
                              offsetLead.ToString(CultureInfo.InvariantCulture) + " ticks.");

            WriteMeasurement("GAM11-5h11-t2-contact-vs-penetration.csv", trace.ToString());
            WriteMeasurement("GAM11-5h11-t2-contact-vs-penetration.txt", report.ToString());
            Debug.Log(report.ToString());
            yield return null;
        }

        // ------------------------------------------------------------------
        // T3. The accepted reference path, swept as pure geometry. The dynamic
        // run never reaches the commanded depth because the contact stops it,
        // so the only way to ask what the reference actually requires is to
        // evaluate the poses it commands without simulating them.
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator T3_REFERENCE_PATH_PROXY_FEASIBILITY_SWEEP()
        {
            _controller.SetLoad(0f);
            SquatPhysicalAdapter adapter = _controller.Adapter;
            FoundationRuntime runtime = _bootstrap.Runtime;
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;

            for (int i = 0; i < SettleTicks; i++)
            {
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFootDetectors(dt);
            }

            // The settled standing pose is the geometric origin of the sweep.
            PhysicalAthleteRig.SegmentRuntime pelvis = _rig.Segments["pelvis"];
            PhysicalAthleteRig.SegmentRuntime abdomenSegment = _rig.Segments["abdomen"];
            Collider abdomen = abdomenSegment.Collider;

            Vector3 abdomenPos0 = abdomenSegment.Body.position;
            Quaternion abdomenRot0 = abdomenSegment.Body.rotation;
            Vector3 abdomenAnchor = JointAnchorWorld("abdomen");

            var trace = new StringBuilder();
            trace.AppendLine("phase_sq,hip_flexion_deg,trunk_flexion_deg,knee_flexion_deg," +
                             "left_overlap,left_penetration_m,left_surface_gap_m," +
                             "right_overlap,right_penetration_m,right_surface_gap_m," +
                             "contact_within_offset");

            float proxyContactOnsetHipDeg = float.NaN;
            float proxyPenetrationOnsetHipDeg = float.NaN;
            float penetrationAt111 = float.NaN;
            float maxHipCommanded = 0f;
            float contactOffsetSum = _rig.Segments["left_thigh"].Collider.contactOffset + abdomen.contactOffset;

            // Sample the accepted descent finely enough to resolve onset to a
            // degree. Direction is Descent throughout: this is the loaded path.
            for (int step = 0; step <= 200; step++)
            {
                float phase = step / 200f;
                SquatReferencePose pose = adapter.ReferenceAnatomicalPose(phase, SquatPhaseDirection.Descent);
                float hipDeg = pose.HipFlexionRad * Mathf.Rad2Deg;
                float trunkDeg = pose.TrunkFlexionRad * Mathf.Rad2Deg;
                float kneeDeg = pose.KneeFlexionRad * Mathf.Rad2Deg;
                maxHipCommanded = Mathf.Max(maxHipCommanded, hipDeg);

                // The abdomen carries the reference trunk flexion about its own
                // anchor; the thighs carry the reference hip flexion about
                // theirs. Both rotations are about the world frontal axis,
                // which is the axis those joints were built on.
                Quaternion trunkDelta = Quaternion.AngleAxis(trunkDeg, Vector3.right);
                Vector3 abdomenPos = abdomenAnchor + trunkDelta * (abdomenPos0 - abdomenAnchor);
                Quaternion abdomenRot = trunkDelta * abdomenRot0;

                bool leftOverlap = MeasureHypotheticalThigh(
                    "left_thigh", hipDeg, abdomen, abdomenPos, abdomenRot, out float leftDepth, out float leftGap);
                bool rightOverlap = MeasureHypotheticalThigh(
                    "right_thigh", hipDeg, abdomen, abdomenPos, abdomenRot, out float rightDepth, out float rightGap);

                float minGap = Mathf.Min(leftOverlap ? 0f : leftGap, rightOverlap ? 0f : rightGap);
                bool withinOffset = minGap <= contactOffsetSum;
                bool anyOverlap = leftOverlap || rightOverlap;
                float maxDepth = Mathf.Max(leftOverlap ? leftDepth : 0f, rightOverlap ? rightDepth : 0f);

                if (float.IsNaN(proxyContactOnsetHipDeg) && withinOffset)
                    proxyContactOnsetHipDeg = hipDeg;
                if (float.IsNaN(proxyPenetrationOnsetHipDeg) && anyOverlap)
                    proxyPenetrationOnsetHipDeg = hipDeg;
                if (hipDeg >= 110.5f && float.IsNaN(penetrationAt111))
                    penetrationAt111 = maxDepth;

                trace.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:F3},{1:F2},{2:F2},{3:F2},{4},{5:F5},{6:F5},{7},{8:F5},{9:F5},{10}",
                    phase, hipDeg, trunkDeg, kneeDeg,
                    leftOverlap ? 1 : 0, leftDepth, leftGap,
                    rightOverlap ? 1 : 0, rightDepth, rightGap,
                    withinOffset ? 1 : 0));
            }

            var report = new StringBuilder();
            report.AppendLine("GAM-11 PHASE 5H11 T3 REFERENCE PATH PROXY FEASIBILITY");
            report.AppendLine("Thighs rotated about the as-built hip anchors, abdomen about its own,");
            report.AppendLine("both by the angles the accepted GAM-10 reference commands. No body moved.");
            report.AppendLine();
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "MAX_HIP_FLEXION_COMMANDED       = {0:F1} deg", maxHipCommanded));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "PROXY_CONTACT_ONSET_HIP_DEG     = {0:F1} deg (surfaces within contactOffset {1:F4} m)",
                proxyContactOnsetHipDeg, contactOffsetSum));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "PROXY_PENETRATION_ONSET_HIP_DEG = {0:F1} deg (surfaces actually intersect)",
                proxyPenetrationOnsetHipDeg));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "PROXY_PENETRATION_AT_111_DEG    = {0:F4} m", penetrationAt111));

            WriteMeasurement("GAM11-5h11-t3-reference-path-sweep.csv", trace.ToString());
            WriteMeasurement("GAM11-5h11-t3-reference-path-sweep.txt", report.ToString());
            Debug.Log(report.ToString());
            yield return null;
        }

        // ------------------------------------------------------------------
        // T4. The repair, verified against the exact window H10 measured.
        // ------------------------------------------------------------------

        /// <summary>
        /// The claim under test is narrow and it is the only claim this phase
        /// is allowed to make: the thigh/abdomen contact is no longer what
        /// stops the hip. Whether the athlete completes a squat is a different
        /// question, measured separately and not required here.
        /// </summary>
        [UnityTest]
        public IEnumerator T4_OLD_H10_FIRST_CAUSE_REMOVED()
        {
            _controller.SetLoad(0f);
            SquatPhysicalAdapter adapter = _controller.Adapter;
            adapter.BalanceCorrectionsEnabled = true;
            FoundationRuntime runtime = _bootstrap.Runtime;
            PoweredJointController powered = _rig.PoweredController;
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;

            Collider leftThigh = _rig.Segments["left_thigh"].Collider;
            Collider rightThigh = _rig.Segments["right_thigh"].Collider;
            Collider abdomen = _rig.Segments["abdomen"].Collider;
            Assert.That(Physics.GetIgnoreCollision(leftThigh, abdomen), Is.True,
                "The production policy must already suppress left_thigh/abdomen before this runs.");
            Assert.That(Physics.GetIgnoreCollision(rightThigh, abdomen), Is.True,
                "The production policy must already suppress right_thigh/abdomen before this runs.");

            PairProbe probe = PairProbe.Attach(_rig, new[] { "left_thigh", "right_thigh" }, "abdomen");

            for (int i = 0; i < SettleTicks; i++)
            {
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFootDetectors(dt);
            }

            var trace = new StringBuilder();
            trace.AppendLine("tick,hip_target_deg,hip_actual_deg,hip_track_err_deg,hip_limit_prox,hip_demand," +
                             "thigh_abdomen_contact,thigh_abdomen_impulse,knee_actual_deg,pelvis_y");

            int contactTicks = 0;
            float worstWindowTrackingError = 0f;
            float deepestHipActual = 0f;
            float deepestHipTarget = 0f;

            adapter.StartSquat();
            for (int tick = 0; tick < SquatTicks; tick++)
            {
                probe.BeginTick();
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFootDetectors(dt);

                PoweredJointController.PoweredJointRuntime hip = powered.GetJoint("right_thigh");
                PoweredJointController.PoweredJointRuntime knee = powered.GetJoint("right_shank");
                float actual = SignedTwistDegrees(hip.Diagnostic.ActualRelative, Vector3.right);
                float target = SignedTwistDegrees(hip.AppliedTarget, Vector3.right);
                float trackErr = Quaternion.Angle(hip.AppliedTarget, hip.Diagnostic.ActualRelative);

                if (probe.ContactThisTick)
                    contactTicks++;
                // The window H10 measured the block in.
                if (tick >= 146 && tick <= 250)
                    worstWindowTrackingError = Mathf.Max(worstWindowTrackingError, trackErr);
                if (Mathf.Abs(actual) > Mathf.Abs(deepestHipActual))
                    deepestHipActual = actual;
                if (Mathf.Abs(target) > Mathf.Abs(deepestHipTarget))
                    deepestHipTarget = target;

                trace.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0},{1:F2},{2:F2},{3:F2},{4:F3},{5:F3},{6},{7:F3},{8:F2},{9:F4}",
                    tick, target, actual, trackErr, hip.Diagnostic.LimitProximity, hip.Diagnostic.ModeledDemand,
                    probe.ContactThisTick ? 1 : 0, probe.ImpulseThisTick,
                    SignedTwistDegrees(knee.Diagnostic.ActualRelative, Vector3.right),
                    _rig.Segments["pelvis"].Body.position.y));
            }

            string summary = string.Format(CultureInfo.InvariantCulture,
                "thighAbdomenContactTicks={0} worstTrackingErrorInH10Window={1:F2} deg " +
                "deepestHipTarget={2:F2} deg deepestHipActual={3:F2} deg deficit={4:F2} deg",
                contactTicks, worstWindowTrackingError, deepestHipTarget, deepestHipActual,
                Mathf.Abs(deepestHipTarget) - Mathf.Abs(deepestHipActual));

            WriteMeasurement("GAM11-5h11-t4-first-cause-removed.csv", trace.ToString());
            WriteMeasurement("GAM11-5h11-t4-first-cause-removed.txt",
                "GAM-11 PHASE 5H11 T4 OLD FIRST CAUSE REMOVED" + Environment.NewLine + summary + Environment.NewLine);
            Debug.Log("[T4 OLD H10 FIRST CAUSE REMOVED] " + summary);

            Assert.That(contactTicks, Is.Zero,
                "The thigh and abdomen still collide. " + summary);
            Assert.That(worstWindowTrackingError, Is.LessThan(20f),
                "The hip still falls more than twenty degrees behind inside the window H10 measured the block in. " + summary);
            yield return null;
        }

        // ------------------------------------------------------------------
        // helpers
        // ------------------------------------------------------------------

        /// <summary>
        /// Rotates the settled thigh body about its own hip anchor by the
        /// commanded flexion and measures it against a hypothetical abdomen.
        /// Flexion is negative about the world frontal axis, which is the sense
        /// that carries the knee anterior; the caller passes a positive angle.
        /// </summary>
        private bool MeasureHypotheticalThigh(
            string thighId,
            float hipFlexionDeg,
            Collider abdomen,
            Vector3 abdomenPos,
            Quaternion abdomenRot,
            out float penetration,
            out float surfaceGap)
        {
            PhysicalAthleteRig.SegmentRuntime thigh = _rig.Segments[thighId];
            Vector3 anchor = JointAnchorWorld(thighId);
            Quaternion delta = Quaternion.AngleAxis(-hipFlexionDeg, Vector3.right);
            Vector3 position = anchor + delta * (thigh.Body.position - anchor);
            Quaternion rotation = delta * thigh.Body.rotation;
            return MeasurePose(thigh.Collider, position, rotation, abdomen, abdomenPos, abdomenRot,
                out penetration, out surfaceGap);
        }

        private bool MeasurePair(Collider a, Collider b, out float penetration, out float surfaceGap) =>
            MeasurePose(a, a.transform.position, a.transform.rotation,
                b, b.transform.position, b.transform.rotation, out penetration, out surfaceGap);

        /// <summary>
        /// Overlap comes from PhysX, which is exact. Separation, which PhysX
        /// will not report, is computed analytically: the capsule is its
        /// segment plus a radius, so the surface gap is the segment-to-box
        /// distance minus the radius. The two agree at zero.
        /// </summary>
        private static bool MeasurePose(
            Collider a, Vector3 posA, Quaternion rotA,
            Collider b, Vector3 posB, Quaternion rotB,
            out float penetration,
            out float surfaceGap)
        {
            bool overlapping = Physics.ComputePenetration(
                a, posA, rotA, b, posB, rotB, out Vector3 _, out float distance);
            penetration = overlapping ? distance : 0f;
            surfaceGap = overlapping ? 0f : AnalyticSurfaceGap(a, posA, rotA, b, posB, rotB);
            return overlapping;
        }

        private static float AnalyticSurfaceGap(
            Collider a, Vector3 posA, Quaternion rotA,
            Collider b, Vector3 posB, Quaternion rotB)
        {
            if (!(a is CapsuleCollider capsule) || !(b is BoxCollider box))
                return float.NaN;

            Vector3 scaleA = a.transform.lossyScale;
            float radius = capsule.radius * Mathf.Max(Mathf.Abs(scaleA.x), Mathf.Abs(scaleA.z));
            float half = Mathf.Max(0f, capsule.height * 0.5f * Mathf.Abs(scaleA.y) - radius);
            Vector3 axis = rotA * Vector3.up;
            Vector3 centre = posA + rotA * Vector3.Scale(capsule.center, scaleA);
            Vector3 tip = centre + axis * half;
            Vector3 tail = centre - axis * half;

            Vector3 scaleB = b.transform.lossyScale;
            Vector3 boxHalf = 0.5f * Vector3.Scale(box.size, Abs(scaleB));
            Vector3 boxCentre = posB + rotB * Vector3.Scale(box.center, scaleB);
            Quaternion inverseB = Quaternion.Inverse(rotB);

            float best = float.PositiveInfinity;
            const int samples = 400;
            for (int index = 0; index <= samples; index++)
            {
                Vector3 point = Vector3.Lerp(tail, tip, index / (float)samples);
                Vector3 local = inverseB * (point - boxCentre);
                Vector3 clamped = new Vector3(
                    Mathf.Clamp(local.x, -boxHalf.x, boxHalf.x),
                    Mathf.Clamp(local.y, -boxHalf.y, boxHalf.y),
                    Mathf.Clamp(local.z, -boxHalf.z, boxHalf.z));
                best = Mathf.Min(best, (local - clamped).magnitude);
            }
            return Mathf.Max(0f, best - radius);
        }

        private static Vector3 Abs(Vector3 value) =>
            new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));

        private Vector3 JointAnchorWorld(string childId)
        {
            foreach (PhysicalAthleteRig.JointRuntime joint in _rig.Joints)
            {
                if (string.Equals(joint.Recipe.ChildId, childId, StringComparison.Ordinal))
                    return joint.Joint.transform.TransformPoint(joint.Joint.anchor);
            }
            throw new InvalidOperationException("No joint for child " + childId);
        }

        private string DescribeCollider(string id)
        {
            PhysicalAthleteRig.SegmentRuntime segment = _rig.Segments[id];
            Collider collider = segment.Collider;
            Vector3 scale = collider.transform.lossyScale;
            if (collider is CapsuleCollider capsule)
            {
                float worldRadius = capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
                float worldHeight = capsule.height * Mathf.Abs(scale.y);
                return string.Format(CultureInfo.InvariantCulture,
                    "{0,-12} Capsule radius={1:F4} height={2:F4} direction={3} center={4} lossyScale={5} " +
                    "worldRadius={6:F4} worldHeight={7:F4} worldCylinderHalf={8:F4} contactOffset={9:F4} recipeDims={10}",
                    id, capsule.radius, capsule.height, capsule.direction, V(capsule.center), V(scale),
                    worldRadius, worldHeight, Mathf.Max(0f, worldHeight * 0.5f - worldRadius),
                    collider.contactOffset, V(segment.DimensionsMeters));
            }
            if (collider is BoxCollider box)
            {
                Vector3 half = 0.5f * Vector3.Scale(box.size, Abs(scale));
                return string.Format(CultureInfo.InvariantCulture,
                    "{0,-12} Box size={1} center={2} lossyScale={3} worldHalfExtents={4} contactOffset={5:F4} recipeDims={6}",
                    id, V(box.size), V(box.center), V(scale), V(half), collider.contactOffset,
                    V(segment.DimensionsMeters));
            }
            return id + ": unexpected collider " + collider.GetType().Name;
        }

        private string DescribeHipRegistration(string thighId)
        {
            PhysicalAthleteRig.SegmentRuntime thigh = _rig.Segments[thighId];
            var capsule = (CapsuleCollider)thigh.Collider;
            Vector3 anchor = JointAnchorWorld(thighId);

            Transform referenceHip = _rig.ReferenceAnimator.GetBoneTransform(
                thighId.StartsWith("left", StringComparison.Ordinal)
                    ? HumanBodyBones.LeftUpperLeg
                    : HumanBodyBones.RightUpperLeg);
            Transform referenceKnee = _rig.ReferenceAnimator.GetBoneTransform(
                thighId.StartsWith("left", StringComparison.Ordinal)
                    ? HumanBodyBones.LeftLowerLeg
                    : HumanBodyBones.RightLowerLeg);

            Vector3 scale = thigh.Collider.transform.lossyScale;
            float radius = capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float halfHeight = capsule.height * 0.5f * Mathf.Abs(scale.y);
            Vector3 axis = thigh.Body.rotation * Vector3.up;
            Vector3 centre = thigh.Body.position + thigh.Body.rotation * capsule.center;
            Vector3 proximalCap = centre + axis * halfHeight;
            Vector3 proximalCylinder = centre + axis * Mathf.Max(0f, halfHeight - radius);

            float femurLength = Vector3.Distance(referenceHip.position, referenceKnee.position);
            // Positive means the capsule reaches past the hip anchor, up into
            // the pelvis and abdomen.
            float capAboveAnchor = Vector3.Dot(proximalCap - anchor, axis);
            float cylinderAboveAnchor = Vector3.Dot(proximalCylinder - anchor, axis);

            return string.Format(CultureInfo.InvariantCulture,
                "{0,-12} hipAnchor={1} referenceHipBone={2} anchorError={3:F5} m | femurLength={4:F4} " +
                "capsuleWorldHeight={5:F4} radius={6:F4} | bodyOrigin={7} bodyToAnchor={8:F4} m | " +
                "proximalCapAboveAnchor={9:F4} m proximalCylinderAboveAnchor={10:F4} m | axisDotFemur={11:F4}",
                thighId, V(anchor), V(referenceHip.position),
                Vector3.Distance(anchor, referenceHip.position),
                femurLength, capsule.height * Mathf.Abs(scale.y), radius,
                V(thigh.Body.position), Vector3.Distance(thigh.Body.position, anchor),
                capAboveAnchor, cylinderAboveAnchor,
                Vector3.Dot(axis, (referenceHip.position - referenceKnee.position).normalized));
        }

        private string DescribeAbdomenRegistration()
        {
            PhysicalAthleteRig.SegmentRuntime abdomen = _rig.Segments["abdomen"];
            var box = (BoxCollider)abdomen.Collider;
            Vector3 anchor = JointAnchorWorld("abdomen");
            Transform spine = _rig.ReferenceAnimator.GetBoneTransform(HumanBodyBones.Spine);
            Transform chest = _rig.ReferenceAnimator.GetBoneTransform(HumanBodyBones.Chest);
            Transform hips = _rig.ReferenceAnimator.GetBoneTransform(HumanBodyBones.Hips);

            Vector3 scale = abdomen.Collider.transform.lossyScale;
            Vector3 half = 0.5f * Vector3.Scale(box.size, Abs(scale));
            Vector3 centre = abdomen.Body.position + abdomen.Body.rotation * box.center;
            Vector3 down = abdomen.Body.rotation * Vector3.down;
            Vector3 lowestFace = centre + down * half.y;

            return string.Format(CultureInfo.InvariantCulture,
                "abdomen      anchor={0} referenceSpine={1} anchorError={2:F5} m | boxCentre={3} halfExtents={4} | " +
                "lowestFaceCentre={5} lowestFaceY={6:F4} | referenceHipsY={7:F4} referenceSpineY={8:F4} referenceChestY={9:F4} | " +
                "spineToChest={10:F4} m boxHeight={11:F4} m",
                V(anchor), V(spine.position), Vector3.Distance(anchor, spine.position),
                V(centre), V(half), V(lowestFace), lowestFace.y,
                hips.position.y, spine.position.y, chest.position.y,
                Vector3.Distance(spine.position, chest.position), box.size.y * Mathf.Abs(scale.y));
        }

        private void TickFootDetectors(float dt)
        {
            if (_controller.LeftFootContact != null)
                _controller.LeftFootContact.PhysicsTickUpdate(dt);
            if (_controller.RightFootContact != null)
                _controller.RightFootContact.PhysicsTickUpdate(dt);
        }

        private static string V(Vector3 value) =>
            string.Format(CultureInfo.InvariantCulture, "({0:F4},{1:F4},{2:F4})", value.x, value.y, value.z);

        private static float SignedTwistDegrees(Quaternion rotation, Vector3 axis)
        {
            var vector = new Vector3(rotation.x, rotation.y, rotation.z);
            Vector3 projection = Vector3.Project(vector, axis);
            var twist = new Quaternion(projection.x, projection.y, projection.z, rotation.w);
            float magnitude = Mathf.Sqrt(twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w);
            if (magnitude < 1e-6f)
                return 0f;
            twist = new Quaternion(twist.x / magnitude, twist.y / magnitude, twist.z / magnitude, twist.w / magnitude);
            twist.ToAngleAxis(out float angle, out Vector3 twistAxis);
            if (angle > 180f)
                angle -= 360f;
            return angle * Mathf.Sign(Vector3.Dot(twistAxis, axis));
        }

        private static void WriteMeasurement(string filename, string content)
        {
            string directory = Path.GetFullPath(MeasurementDirectory);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, filename), content);
        }

        /// <summary>
        /// Reports only the specific pair under test, so a foot on the platform
        /// cannot be mistaken for a thigh on an abdomen.
        /// </summary>
        private sealed class PairProbe : MonoBehaviour
        {
            private Rigidbody _partner;

            private static bool _contact;
            private static float _impulse;

            public bool ContactThisTick => _contact;
            public float ImpulseThisTick => _impulse;

            public static PairProbe Attach(PhysicalAthleteRig rig, IEnumerable<string> segmentIds, string partnerId)
            {
                Rigidbody partner = rig.Segments[partnerId].Body;
                PairProbe first = null;
                foreach (string id in segmentIds)
                {
                    PairProbe probe = rig.Segments[id].Body.gameObject.AddComponent<PairProbe>();
                    probe._partner = partner;
                    if (first == null)
                        first = probe;
                }
                return first;
            }

            public void BeginTick()
            {
                _contact = false;
                _impulse = 0f;
            }

            private void OnCollisionEnter(Collision collision) => Record(collision);

            private void OnCollisionStay(Collision collision) => Record(collision);

            private void Record(Collision collision)
            {
                if (_partner == null || collision.rigidbody != _partner)
                    return;
                _contact = true;
                _impulse = Mathf.Max(_impulse, collision.impulse.magnitude);
            }
        }
    }
}
