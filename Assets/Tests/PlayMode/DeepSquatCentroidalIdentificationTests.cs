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
    /// Phase 5H12, sections 7, 10, 11 and 12.
    ///
    /// H12's first pass established that the posture limit guard withdraws the
    /// balance controller's ankle authority at depth. That says why the
    /// controller stops acting. It does not say whether the model the
    /// controller is built on would have been right had it kept acting, and it
    /// does not say whether the standing-identified plant gain still holds
    /// down there. These measure both, plus the trunk against its own
    /// reference rather than an absolute angle, and audit the legacy capture
    /// point against full centroidal dynamics.
    ///
    /// Everything here is diagnostic. No control value is changed.
    /// </summary>
    public sealed class DeepSquatCentroidalIdentificationTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const string MeasurementDirectory = "Artifacts/Measurements/GAM-11";
        private const float ApproachPhaseRate = 0.30f;
        private const float GravityMagnitude = 9.81f;

        private FoundationBootstrap _bootstrap;
        private PhysicalAthleteRig _rig;
        private SquatPhysicalPrototypeController _controller;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(QualificationScene, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
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
            if (_controller != null) _controller.enabled = false;
            if (_rig != null) _rig.enabled = false;
            if (_bootstrap != null && _bootstrap.Runtime != null && _bootstrap.Runtime.IsInitialized)
            {
                AsyncOperation unload = _bootstrap.Runtime.Shutdown();
                while (unload != null && !unload.isDone)
                    yield return null;
            }
            if (_bootstrap != null)
                UnityEngine.Object.DestroyImmediate(_bootstrap.gameObject);
            _bootstrap = null; _rig = null; _controller = null;
            yield return null;
            LogAssert.ignoreFailingMessages = false;
        }

        // ==================================================================
        // The planar centroidal relation, derived under this project's axes.
        //
        // Axes: +X right, +Y up, +Z anterior. Gravity is (0, -g, 0), so the
        // sagittal plane is Y-Z and sagittal angular momentum is about +X.
        //
        // Newton about the system centre of mass c, with the ground reaction
        // force F applied at the centre of pressure p on the support plane:
        //
        //     m * c_ddot = F + m * (0, -g, 0)
        //     =>  F = m * (c_ddot_x, c_ddot_y + g, c_ddot_z)
        //
        // Euler about c (gravity exerts no moment about the centre of mass):
        //
        //     Hdot = (p - c) x F
        //     Hdot_x = (p_y - c_y) * F_z - (p_z - c_z) * F_y
        //
        // Writing h = c_y - p_y for the centre-of-mass height above the
        // support plane, so that (p_y - c_y) = -h, and solving for p_z:
        //
        //     p_z = c_z - ( h * c_ddot_z ) / ( c_ddot_y + g )
        //               - Hdot_x / ( m * ( c_ddot_y + g ) )
        //
        // Three nested models fall straight out of that one expression:
        //
        //   LIPM                 c_ddot_y = 0, Hdot_x = 0
        //                        p_z = c_z - h * c_ddot_z / g
        //   variable height,     Hdot_x = 0
        //   zero angular mom.    p_z = c_z - h * c_ddot_z / (c_ddot_y + g)
        //   full centroidal      the whole expression
        //
        // Sign check, asserted by CENTROIDAL_SIGN_CONVENTION_UNIT_TEST below:
        // a rotation about +X carries +Y toward +Z, so a trunk pitching
        // forward carries positive H_x. Generating positive Hdot_x therefore
        // requires the centre of pressure behind the centre of mass, which is
        // what the negative sign on the Hdot term produces.
        // ==================================================================

        private static float PredictCopAp(
            float comAp, float comHeight, float comAccelAp, float comAccelVertical,
            float angularMomentumRateX, float totalMass, bool includeVertical, bool includeAngular)
        {
            float denominator = includeVertical ? comAccelVertical + GravityMagnitude : GravityMagnitude;
            if (Mathf.Abs(denominator) < 0.05f)
                return float.NaN;
            float cop = comAp - comHeight * comAccelAp / denominator;
            if (includeAngular)
                cop -= angularMomentumRateX / (totalMass * denominator);
            return cop;
        }

        [Test]
        public void CENTROIDAL_SIGN_CONVENTION_UNIT_TEST()
        {
            // Static: the centre of pressure sits under the centre of mass.
            Assert.That(PredictCopAp(0.1f, 1.0f, 0f, 0f, 0f, 100f, true, true),
                Is.EqualTo(0.1f).Within(1e-5f), "A static system must put the COP under the COM.");

            // Accelerating forward needs the COP behind the COM.
            float forward = PredictCopAp(0f, 1.0f, 1.0f, 0f, 0f, 100f, true, true);
            Assert.That(forward, Is.LessThan(0f), "Forward COM acceleration requires the COP behind the COM.");

            // Building forward pitch (positive Hdot_x) also needs the COP behind.
            float pitching = PredictCopAp(0f, 1.0f, 0f, 0f, 50f, 100f, true, true);
            Assert.That(pitching, Is.LessThan(0f),
                "Positive sagittal angular-momentum rate requires the COP behind the COM.");

            // Rotation about +X carries +Y toward +Z, which is forward pitch.
            Vector3 rotated = Quaternion.AngleAxis(10f, Vector3.right) * Vector3.up;
            Assert.That(rotated.z, Is.GreaterThan(0f),
                "A positive rotation about +X must carry the up axis anteriorly.");

            // Upward acceleration stiffens the plant: the same forward COM
            // acceleration needs less COP offset.
            float heavyG = PredictCopAp(0f, 1.0f, 1.0f, 5f, 0f, 100f, true, true);
            Assert.That(Mathf.Abs(heavyG), Is.LessThan(Mathf.Abs(forward)),
                "Upward COM acceleration must reduce the required COP excursion.");
        }

        // ------------------------------------------------------------------
        // C1. Sections 11 and 12 on the production descent.
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator C1_CENTROIDAL_VERSUS_REDUCED_ORDER_ON_DESCENT()
        {
            var csv = new StringBuilder();
            csv.AppendLine("load_kg,tick,sq,com_ap,com_height,com_vel_ap,com_acc_ap,com_acc_vert," +
                           "h_x,hdot_x,total_mass,engine_cop_ap,lipm_cop_ap,vhip_cop_ap,full_cop_ap," +
                           "lipm_residual,vhip_residual,full_residual," +
                           "vertical_contribution,angular_contribution," +
                           "capture_ap,support_min,support_max,contacts,guard_scale");

            var report = new StringBuilder();
            report.AppendLine("GAM-11 PHASE 5H12 C1 CENTROIDAL VERSUS REDUCED-ORDER COP PREDICTION");
            report.AppendLine("Residual is predicted minus engine-measured centre of pressure, in metres.");
            report.AppendLine("Contributions are the metres of COP offset attributable to each term.");
            report.AppendLine();

            foreach (float load in new[] { 0f, 25f })
                yield return RunDescent(load, csv, report);

            WriteMeasurement("GAM11-5h12-c1-centroidal-residuals.csv", csv.ToString());
            WriteMeasurement("GAM11-5h12-c1-centroidal-residuals.txt", report.ToString());
            Debug.Log(report.ToString());
            yield return null;
        }

        private IEnumerator RunDescent(float loadKg, StringBuilder csv, StringBuilder report)
        {
            _controller.SetLoad(loadKg);
            SquatPhysicalAdapter adapter = _controller.Adapter;
            FoundationRuntime runtime = _bootstrap.Runtime;
            SquatBalanceObserver balance = adapter.Balance;
            SquatPredictiveBalanceController bc = adapter.BalanceController;
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;

            adapter.Preload.Enabled = true;
            adapter.BalanceCorrectionsEnabled = true;

            for (int i = 0; i < 60; i++)
            {
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFeet(dt);
            }

            adapter.StartSquat();

            Vector3 previousComVelocity = Vector3.zero;
            float previousHx = 0f;
            bool hasPrevious = false;

            var windows = new Dictionary<string, List<float[]>>
            {
                { "mid_descent", new List<float[]>() },
                { "near_parallel", new List<float[]>() },
                { "bottom", new List<float[]>() },
                { "reversal", new List<float[]>() }
            };

            for (int tick = 0; tick < 400; tick++)
            {
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFeet(dt);

                ComputeCentroidal(out Vector3 com, out Vector3 comVelocity, out float hx, out float totalMass);
                if (!hasPrevious)
                {
                    previousComVelocity = comVelocity;
                    previousHx = hx;
                    hasPrevious = true;
                    continue;
                }

                Vector3 comAccel = (comVelocity - previousComVelocity) / dt;
                float hdotX = (hx - previousHx) / dt;
                previousComVelocity = comVelocity;
                previousHx = hx;

                float supportPlaneY = balance.SupportPlaneY;
                float height = Mathf.Max(0.05f, com.y - supportPlaneY);
                float engineCop = balance.HasCopEstimate ? balance.CopEstimate.z : float.NaN;

                float lipm = PredictCopAp(com.z, height, comAccel.z, comAccel.y, hdotX, totalMass, false, false);
                float vhip = PredictCopAp(com.z, height, comAccel.z, comAccel.y, hdotX, totalMass, true, false);
                float full = PredictCopAp(com.z, height, comAccel.z, comAccel.y, hdotX, totalMass, true, true);

                float verticalContribution = vhip - lipm;
                float angularContribution = full - vhip;

                float lipmResidual = lipm - engineCop;
                float vhipResidual = vhip - engineCop;
                float fullResidual = full - engineCop;

                string window = tick < 150 ? "mid_descent"
                    : tick < 260 ? "near_parallel"
                    : tick < 340 ? "bottom" : "reversal";
                if (balance.HasCopEstimate && float.IsFinite(fullResidual))
                {
                    windows[window].Add(new[]
                    {
                        Mathf.Abs(lipmResidual), Mathf.Abs(vhipResidual), Mathf.Abs(fullResidual),
                        verticalContribution, angularContribution
                    });
                }

                if (tick % 5 == 0)
                {
                    csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0:F0},{1},{2:F4},{3:F5},{4:F5},{5:F5},{6:F4},{7:F4},{8:F4},{9:F3},{10:F2}," +
                        "{11:F5},{12:F5},{13:F5},{14:F5},{15:F5},{16:F5},{17:F5},{18:F5},{19:F5}," +
                        "{20:F5},{21:F5},{22:F5},{23},{24:F4}",
                        loadKg, tick, adapter.Sq, com.z, height, comVelocity.z, comAccel.z, comAccel.y,
                        hx, hdotX, totalMass,
                        engineCop, lipm, vhip, full,
                        lipmResidual, vhipResidual, fullResidual,
                        verticalContribution, angularContribution,
                        balance.CaptureAp, balance.SupportApMin, balance.SupportApMax,
                        balance.SupportContactCount, bc.PostureGuardScale));
                }
            }

            report.AppendLine("--- load " + loadKg.ToString("F0", CultureInfo.InvariantCulture) + " kg ---");
            report.AppendLine("window          n    |lipm|   |vhip|   |full|   vertTerm   angTerm");
            foreach (string key in new[] { "mid_descent", "near_parallel", "bottom", "reversal" })
            {
                List<float[]> rows = windows[key];
                if (rows.Count == 0)
                {
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-14}  {1,3}   (no COP estimate)", key, 0));
                    continue;
                }
                float l = 0f, v = 0f, f = 0f, vc = 0f, ac = 0f;
                foreach (float[] r in rows) { l += r[0]; v += r[1]; f += r[2]; vc += Mathf.Abs(r[3]); ac += Mathf.Abs(r[4]); }
                int n = rows.Count;
                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,-14}  {1,3}   {2,6:F4}   {3,6:F4}   {4,6:F4}   {5,8:F4}   {6,7:F4}",
                    key, n, l / n, v / n, f / n, vc / n, ac / n));
            }
            report.AppendLine();
            yield return null;
        }

        /// <summary>
        /// System centre of mass, its velocity, and sagittal angular momentum
        /// about it, over every dynamic body including the bar when loaded.
        /// </summary>
        private void ComputeCentroidal(out Vector3 com, out Vector3 comVelocity, out float angularMomentumX, out float totalMass)
        {
            Vector3 weighted = Vector3.zero;
            Vector3 momentum = Vector3.zero;
            totalMass = 0f;

            var bodies = new List<Rigidbody>();
            foreach (KeyValuePair<string, PhysicalAthleteRig.SegmentRuntime> segment in _rig.Segments)
            {
                if (segment.Value.Body != null)
                    bodies.Add(segment.Value.Body);
            }
            if (_controller.Saddle != null && _controller.Saddle.Barbell != null &&
                _controller.Saddle.Barbell.Body != null &&
                _controller.Saddle.Barbell.Body.gameObject.activeInHierarchy)
            {
                bodies.Add(_controller.Saddle.Barbell.Body);
            }

            foreach (Rigidbody body in bodies)
            {
                weighted += body.worldCenterOfMass * body.mass;
                momentum += body.linearVelocity * body.mass;
                totalMass += body.mass;
            }
            com = weighted / Mathf.Max(totalMass, 1e-6f);
            comVelocity = momentum / Mathf.Max(totalMass, 1e-6f);

            Vector3 angular = Vector3.zero;
            foreach (Rigidbody body in bodies)
            {
                angular += Vector3.Cross(body.worldCenterOfMass - com, body.mass * body.linearVelocity);
                Quaternion principal = body.rotation * body.inertiaTensorRotation;
                Vector3 omegaLocal = Quaternion.Inverse(principal) * body.angularVelocity;
                angular += principal * Vector3.Scale(body.inertiaTensor, omegaLocal);
            }
            angularMomentumX = angular.x;
        }

        // ------------------------------------------------------------------
        // C2. Section 7. Phase-local ankle target to centre-of-pressure gain.
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator C2_PHASE_LOCAL_ANKLE_TARGET_TO_COP_GAIN()
        {
            var report = new StringBuilder();
            report.AppendLine("GAM-11 PHASE 5H12 C2 PHASE-LOCAL ANKLE TARGET TO COP SENSITIVITY");
            report.AppendLine("Production constant: MeasuredTargetToCopMPerRad = " +
                              SquatPredictiveBalanceController.MeasuredTargetToCopMPerRad.ToString("F5", CultureInfo.InvariantCulture));
            report.AppendLine("The ankle balance offset is overridden directly, so the perturbation is a");
            report.AppendLine("known target change rather than a controller output. All other targets fixed.");
            report.AppendLine();
            report.AppendLine("s_q    n   gain_m_per_rad   R^2      copRange_m   status");

            var csv = new StringBuilder();
            csv.AppendLine("phase,offset_deg,ankle_actual_deg,cop_ap,com_ap,support_min,support_max,contacts,settled");

            foreach (float phase in new[] { 0.00f, 0.55f, 0.80f, 1.00f })
                yield return ProbeGainAtPhase(phase, report, csv);

            WriteMeasurement("GAM11-5h12-c2-target-to-cop-gain.txt", report.ToString());
            WriteMeasurement("GAM11-5h12-c2-target-to-cop-gain.csv", csv.ToString());
            Debug.Log(report.ToString());
            yield return null;
        }

        private IEnumerator ProbeGainAtPhase(float phase, StringBuilder report, StringBuilder csv)
        {
            var offsets = new[] { -3f, -2f, -1f, 0f, 1f, 2f, 3f };
            var xs = new List<float>();
            var ys = new List<float>();
            float copMin = float.PositiveInfinity, copMax = float.NegativeInfinity;
            bool anyUnsettled = false;

            foreach (float offsetDeg in offsets)
            {
                _controller.SetLoad(0f);
                SquatPhysicalAdapter adapter = _controller.Adapter;
                FoundationRuntime runtime = _bootstrap.Runtime;
                SquatBalanceObserver balance = adapter.Balance;
                float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;

                adapter.Preload.Enabled = true;
                adapter.BalanceCorrectionsEnabled = true;
                adapter.AnkleSagittalOffsetOverrideRad = null;

                adapter.HoldReferencePhaseForQualification(0f, SquatPhaseDirection.None, SquatState.SETUP);
                for (int i = 0; i < 60; i++)
                {
                    runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                    TickFeet(dt);
                }

                SquatState state = phase >= 0.999f ? SquatState.BOTTOM : SquatState.DESCENT;
                float current = 0f;
                int guard = 0;
                while (current < phase - 1e-4f && guard++ < 4000)
                {
                    current = Mathf.Min(phase, current + ApproachPhaseRate * dt);
                    adapter.HoldReferencePhaseForQualification(current, SquatPhaseDirection.Descent, state, ApproachPhaseRate);
                    runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                    TickFeet(dt);
                }
                adapter.HoldReferencePhaseForQualification(phase, SquatPhaseDirection.Descent, state, 0f);

                // Let the closed loop settle first. Replacing the controller's
                // ankle offset outright opens the loop, and this plant has no
                // open-loop static equilibrium at any phase, standing
                // included, so a sustained override just measures a fall.
                for (int i = 0; i < 120; i++)
                {
                    runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                    TickFeet(dt);
                }

                float comSpeedSettled = new Vector2(balance.SystemComVelocity.x, balance.SystemComVelocity.z).magnitude;
                bool settledBefore = balance.HasSupport && balance.HasCopEstimate && comSpeedSettled < 0.05f;

                // Freeze the ankle offset at the value the controller had just
                // reached, plus the probe delta, for a short window. The freeze
                // is identical across probes, so the COP difference between
                // them is attributable to the delta and not to the freeze.
                float settledOffset = adapter.BalanceController.AnkleSagittalOffsetRad;
                adapter.AnkleSagittalOffsetOverrideRad = settledOffset + offsetDeg * Mathf.Deg2Rad;
                for (int i = 0; i < 10; i++)
                {
                    runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                    TickFeet(dt);
                }

                float comSpeed = new Vector2(balance.SystemComVelocity.x, balance.SystemComVelocity.z).magnitude;
                bool settled = settledBefore && balance.HasSupport && balance.HasCopEstimate && comSpeed < 0.25f;
                if (!settled) anyUnsettled = true;

                float cop = balance.HasCopEstimate ? balance.CopEstimate.z : float.NaN;
                float ankleActual = -TwistX(_rig.PoweredController.GetJoint("left_foot").Diagnostic.ActualRelative);

                csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:F2},{1:F1},{2:F3},{3:F5},{4:F5},{5:F5},{6:F5},{7},{8}",
                    phase, offsetDeg, ankleActual, cop, balance.SystemCom.z,
                    balance.SupportApMin, balance.SupportApMax, balance.SupportContactCount, settled ? 1 : 0));

                if (settled && float.IsFinite(cop))
                {
                    xs.Add(offsetDeg * Mathf.Deg2Rad);
                    ys.Add(cop);
                    copMin = Mathf.Min(copMin, cop);
                    copMax = Mathf.Max(copMax, cop);
                }
                adapter.AnkleSagittalOffsetOverrideRad = null;
                yield return null;
            }

            if (xs.Count < 3)
            {
                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,4:F2}  {1,3}   {2,14}   {3,6}   {4,10}   GAIN_NOT_IDENTIFIABLE_QUASISTATICALLY",
                    phase, xs.Count, "-", "-", "-"));
                yield break;
            }

            LinearFit(xs, ys, out float slope, out float rSquared);
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0,4:F2}  {1,3}   {2,14:F5}   {3,6:F4}   {4,10:F5}   {5}",
                phase, xs.Count, slope, rSquared, copMax - copMin,
                anyUnsettled ? "PARTIAL_SOME_PROBES_UNSETTLED" : "OK"));
            yield return null;
        }

        private static void LinearFit(List<float> xs, List<float> ys, out float slope, out float rSquared)
        {
            int n = xs.Count;
            float mx = 0f, my = 0f;
            for (int i = 0; i < n; i++) { mx += xs[i]; my += ys[i]; }
            mx /= n; my /= n;
            float sxy = 0f, sxx = 0f, syy = 0f;
            for (int i = 0; i < n; i++)
            {
                float dx = xs[i] - mx, dy = ys[i] - my;
                sxy += dx * dy; sxx += dx * dx; syy += dy * dy;
            }
            slope = sxx > 1e-12f ? sxy / sxx : float.NaN;
            rSquared = (sxx > 1e-12f && syy > 1e-12f) ? (sxy * sxy) / (sxx * syy) : float.NaN;
        }

        // ------------------------------------------------------------------
        // C3. Section 10. Trunk against its own reference, not a threshold.
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator C3_TRUNK_REFERENCE_TRACKING_ERROR()
        {
            _controller.SetLoad(0f);
            SquatPhysicalAdapter adapter = _controller.Adapter;
            FoundationRuntime runtime = _bootstrap.Runtime;
            SquatBalanceObserver balance = adapter.Balance;
            PoweredJointController powered = _rig.PoweredController;
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;

            adapter.Preload.Enabled = true;
            adapter.BalanceCorrectionsEnabled = true;

            for (int i = 0; i < 60; i++)
            {
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFeet(dt);
            }

            var csv = new StringBuilder();
            csv.AppendLine("tick,sq,reference_trunk_JOINT_flexion_deg,actual_WORLD_trunk_pitch_deg,mixed_frame_difference_deg," +
                           "actual_trunk_ang_vel_deg_s,abdomen_target_deg,abdomen_actual_deg,abdomen_error_deg," +
                           "thorax_target_deg,thorax_actual_deg,thorax_error_deg,com_ap,guard_scale");

            adapter.StartSquat();
            Rigidbody pelvis = _rig.Segments["pelvis"].Body;
            Rigidbody thorax = _rig.Segments["thorax"].Body;
            float previousActual = WorldTrunkPitch(pelvis, thorax);
            int referenceDepartureTick = -1;

            for (int tick = 0; tick < 400; tick++)
            {
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFeet(dt);

                // Careful: these two are in different frames. The reference
                // pose exposes trunk flexion as a JOINT angle relative to the
                // pelvis, while the actual here is the WORLD pelvis-to-thorax
                // inclination, which also carries the hip's rotation. Their
                // difference is therefore not a tracking error, and is logged
                // as a mixed-frame difference so nobody reads it as one. The
                // true world-frame comparison needs the reference world trunk
                // pitch, which R1 derives in the preview scene; the true
                // joint-frame tracking errors are the abdomen and thorax
                // columns below.
                SquatReferencePose pose = adapter.ReferenceAnatomicalPose(adapter.Sq, SquatPhaseDirection.Descent);
                float referencePitch = pose.TrunkFlexionRad * Mathf.Rad2Deg;
                float actualPitch = WorldTrunkPitch(pelvis, thorax);
                float error = actualPitch - referencePitch;
                float angularVelocity = (actualPitch - previousActual) / dt;
                previousActual = actualPitch;

                if (referenceDepartureTick < 0 && Mathf.Abs(error) > 25f)
                    referenceDepartureTick = tick;

                if (tick % 5 == 0)
                {
                    PoweredJointDiagnostic abdomen = powered.GetJoint("abdomen").Diagnostic;
                    PoweredJointDiagnostic thoraxJoint = powered.GetJoint("thorax").Diagnostic;
                    csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0},{1:F4},{2:F3},{3:F3},{4:F3},{5:F2},{6:F3},{7:F3},{8:F3},{9:F3},{10:F3},{11:F3},{12:F5},{13:F4}",
                        tick, adapter.Sq, referencePitch, actualPitch, error, angularVelocity,
                        TwistX(powered.GetJoint("abdomen").AppliedTarget), TwistX(abdomen.ActualRelative),
                        Quaternion.Angle(powered.GetJoint("abdomen").AppliedTarget, abdomen.ActualRelative),
                        TwistX(powered.GetJoint("thorax").AppliedTarget), TwistX(thoraxJoint.ActualRelative),
                        Quaternion.Angle(powered.GetJoint("thorax").AppliedTarget, thoraxJoint.ActualRelative),
                        balance.SystemCom.z, adapter.BalanceController.PostureGuardScale));
                }
            }

            WriteMeasurement("GAM11-5h12-c3-trunk-reference-error.csv", csv.ToString());
            Debug.Log("[C3] trunk reference departure tick (>25 deg) = " +
                      referenceDepartureTick.ToString(CultureInfo.InvariantCulture));
            yield return null;
        }

        /// <summary>
        /// World trunk pitch from the pelvis-to-thorax vector, positive
        /// anterior. Measured from geometry so it carries no build-time frame
        /// offset.
        /// </summary>
        private static float WorldTrunkPitch(Rigidbody pelvis, Rigidbody thorax)
        {
            Vector3 axis = thorax.position - pelvis.position;
            return axis.sqrMagnitude < 1e-8f ? 0f : Mathf.Atan2(axis.z, axis.y) * Mathf.Rad2Deg;
        }

        private void TickFeet(float dt)
        {
            if (_controller.LeftFootContact != null)
                _controller.LeftFootContact.PhysicsTickUpdate(dt);
            if (_controller.RightFootContact != null)
                _controller.RightFootContact.PhysicsTickUpdate(dt);
        }

        private static float TwistX(Quaternion rotation)
        {
            var vector = new Vector3(rotation.x, rotation.y, rotation.z);
            Vector3 projection = Vector3.Project(vector, Vector3.right);
            var twist = new Quaternion(projection.x, projection.y, projection.z, rotation.w);
            float magnitude = Mathf.Sqrt(twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w);
            if (magnitude < 1e-6f) return 0f;
            twist = new Quaternion(twist.x / magnitude, twist.y / magnitude, twist.z / magnitude, twist.w / magnitude);
            twist.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            return angle * Mathf.Sign(Vector3.Dot(axis, Vector3.right));
        }

        private static void WriteMeasurement(string filename, string content)
        {
            string directory = Path.GetFullPath(MeasurementDirectory);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, filename), content);
        }
    }
}
