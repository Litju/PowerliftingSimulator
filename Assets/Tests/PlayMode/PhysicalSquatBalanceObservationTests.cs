using System;
using System.Collections;
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
    /// Phase 5A/5D. Establishes what the physical athlete's balance state
    /// actually is, measured from plantar contacts and solver impulses, before
    /// any controller is designed around it.
    /// </summary>
    public sealed class PhysicalSquatBalanceObservationTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const string MeasurementDirectory = "Artifacts/Measurements";

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

        [UnityTest]
        public IEnumerator B1_SUPPORT_POLYGON_COMES_FROM_PLANTAR_CONTACTS()
        {
            _controller.SetLoad(0f);
            FoundationRuntime runtime = _bootstrap.Runtime;
            SquatBalanceObserver balance = _controller.Adapter.Balance;

            for (int tick = 0; tick < 30; tick++)
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);

            Assert.That(balance.HasSupport, Is.True, "No plantar contact was recorded after settling.");
            Assert.That(balance.SupportContactCount, Is.GreaterThan(0));

            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[B1 SUPPORT] contacts={0} ap=[{1:F4},{2:F4}] len={3:F4} ml=[{4:F4},{5:F4}] width={6:F4} planeY={7:F4} cop={8} impulse={9:F4}",
                balance.SupportContactCount, balance.SupportApMin, balance.SupportApMax, balance.SupportApLength,
                balance.SupportMlMin, balance.SupportMlMax, balance.SupportMlWidth, balance.SupportPlaneY,
                balance.HasCopEstimate ? balance.CopEstimate.ToString("F4") : "none", balance.TotalNormalImpulse));

            // A bilateral stance has to produce a polygon with real extent in
            // both axes, otherwise the support estimate is degenerate and no
            // balance law built on it can mean anything.
            Assert.That(balance.SupportApLength, Is.GreaterThan(0.05f),
                "The anteroposterior support length is degenerate; foot contacts are collapsing to a line or a point.");
            Assert.That(balance.SupportMlWidth, Is.GreaterThan(0.05f),
                "The mediolateral support width is degenerate; only one foot appears to be carrying contact.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator B2_COP_ESTIMATE_LIES_INSIDE_THE_SUPPORT_POLYGON()
        {
            _controller.SetLoad(0f);
            FoundationRuntime runtime = _bootstrap.Runtime;
            SquatBalanceObserver balance = _controller.Adapter.Balance;

            for (int tick = 0; tick < 30; tick++)
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);

            Assert.That(balance.HasCopEstimate, Is.True,
                "The engine contact impulses carried no measurable normal component, so no COP could be estimated.");

            // The impulse-weighted centre of pressure is a convex combination
            // of the contact points, so it cannot fall outside their bounds.
            // If it does, the impulse weighting is wrong.
            const float tolerance = 1e-3f;
            Assert.That(balance.CopEstimate.z, Is.InRange(balance.SupportApMin - tolerance, balance.SupportApMax + tolerance));
            Assert.That(balance.CopEstimate.x, Is.InRange(balance.SupportMlMin - tolerance, balance.SupportMlMax + tolerance));
            yield return null;
        }

        [UnityTest]
        public IEnumerator B3_REDUCED_ORDER_STATE_IS_SELF_CONSISTENT()
        {
            _controller.SetLoad(0f);
            FoundationRuntime runtime = _bootstrap.Runtime;
            SquatBalanceObserver balance = _controller.Adapter.Balance;

            for (int tick = 0; tick < 30; tick++)
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);

            Assert.That(balance.SystemMassKg, Is.GreaterThan(50f), "The mass model is missing bodies.");
            Assert.That(balance.ComHeightM, Is.GreaterThan(0.4f), "The COM height above the support plane is implausible.");

            float expectedOmega = Mathf.Sqrt(SquatBalanceObserver.GravityMagnitudeMps2 / balance.ComHeightM);
            Assert.That(balance.Omega, Is.EqualTo(expectedOmega).Within(1e-4f));

            float expectedCapture = balance.SystemCom.z + balance.SystemComVelocity.z / balance.Omega;
            Assert.That(balance.CaptureAp, Is.EqualTo(expectedCapture).Within(1e-4f));

            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[B3 REDUCED ORDER] mass={0:F2} com=({1:F4},{2:F4},{3:F4}) comVel=({4:F4},{5:F4},{6:F4}) h={7:F4} omega={8:F4} captureAp={9:F4}",
                balance.SystemMassKg, balance.SystemCom.x, balance.SystemCom.y, balance.SystemCom.z,
                balance.SystemComVelocity.x, balance.SystemComVelocity.y, balance.SystemComVelocity.z,
                balance.ComHeightM, balance.Omega, balance.CaptureAp));
            yield return null;
        }

        [UnityTest]
        public IEnumerator B4_STATIC_FEASIBILITY_NO_BAR_NO_BALANCE()
        {
            // Phase 5D. Balance corrections off, no bar, s_q held at 0. The
            // question this answers is whether the athlete starts inside its
            // own support polygon, which decides whether a balance controller
            // is even the right thing to build.
            _controller.SetLoad(0f);
            SquatPhysicalAdapter adapter = _controller.Adapter;
            adapter.BalanceCorrectionsEnabled = false;
            SquatBalanceObserver balance = adapter.Balance;
            FoundationRuntime runtime = _bootstrap.Runtime;

            var trace = new StringBuilder();
            trace.AppendLine("tick,com_ap,com_vel_ap,capture_ap,cop_ap,support_ap_min,support_ap_max," +
                             "margin_front,margin_rear,ankle_target_deg,ankle_actual_deg,ankle_demand," +
                             "foot_pitch_deg,contacts,slip_mps");

            bool insideAtStart = false;
            bool capturedStart = false;
            float startComAp = 0f;
            float startMarginFront = 0f;
            float startMarginRear = 0f;

            const int totalTicks = 300;
            for (int tick = 0; tick < totalTicks; tick++)
            {
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                if (_controller.LeftFootContact != null)
                    _controller.LeftFootContact.PhysicsTickUpdate((float)SimulationConstants.FixedDeltaTimeSeconds);
                if (_controller.RightFootContact != null)
                    _controller.RightFootContact.PhysicsTickUpdate((float)SimulationConstants.FixedDeltaTimeSeconds);

                // The first tick still carries the spawn transient, so classify
                // on the first settled step that reports real contact.
                if (!capturedStart && tick >= 5 && balance.HasSupport)
                {
                    capturedStart = true;
                    startComAp = balance.SystemCom.z;
                    startMarginFront = balance.ComApMarginFront;
                    startMarginRear = balance.ComApMarginRear;
                    insideAtStart = startMarginFront > 0f && startMarginRear > 0f;
                }

                if (tick % 10 == 0 || tick == totalTicks - 1)
                    trace.AppendLine(SampleRow(tick, balance));
            }

            Directory.CreateDirectory(Path.GetFullPath(MeasurementDirectory));
            File.WriteAllText(
                Path.Combine(Path.GetFullPath(MeasurementDirectory), "GAM11-static-feasibility-unloaded.csv"),
                trace.ToString());

            string status = insideAtStart
                ? "BALANCE_CONTROLLER_REQUIRED"
                : "PHYSICAL_SUBSTRATE_OR_CALIBRATION_BLOCK";
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[B4 STATIC FEASIBILITY] STATUS={0} startComAp={1:F4} marginFront={2:F4} marginRear={3:F4}{4}{5}",
                status, startComAp, startMarginFront, startMarginRear, Environment.NewLine, trace));

            Assert.That(capturedStart, Is.True, "The athlete never registered plantar contact.");
            Assert.That(insideAtStart, Is.True, string.Format(CultureInfo.InvariantCulture,
                "STATUS=PHYSICAL_SUBSTRATE_OR_CALIBRATION_BLOCK. The centre of mass projects outside the plantar support polygon at rest: com_ap={0:F4}, front margin={1:F4} m, rear margin={2:F4} m. No bounded joint-target balance law can fix a stance that is already outside its own base of support.",
                startComAp, startMarginFront, startMarginRear));
            yield return null;
        }

        private string SampleRow(int tick, SquatBalanceObserver balance)
        {
            PoweredJointDiagnostic ankle = _rig.PoweredController.GetJoint("left_foot").Diagnostic;
            float ankleTargetDeg = SignedXDegrees(ankle.AppliedTarget);
            float ankleActualDeg = SignedXDegrees(ankle.ActualRelative);
            float footPitchDeg = 0f;
            if (_rig.Segments.TryGetValue("left_foot", out PhysicalAthleteRig.SegmentRuntime foot) && foot.Body != null)
            {
                Vector3 forward = foot.Body.rotation * Vector3.forward;
                footPitchDeg = Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            }
            float slip = _controller.LeftFootContact != null ? _controller.LeftFootContact.SlipSpeed : 0f;

            return string.Format(CultureInfo.InvariantCulture,
                "{0},{1:F4},{2:F4},{3:F4},{4},{5:F4},{6:F4},{7:F4},{8:F4},{9:F2},{10:F2},{11:F3},{12:F2},{13},{14:F4}",
                tick, balance.SystemCom.z, balance.SystemComVelocity.z, balance.CaptureAp,
                balance.HasCopEstimate ? balance.CopEstimate.z.ToString("F4", CultureInfo.InvariantCulture) : "nan",
                balance.SupportApMin, balance.SupportApMax,
                balance.ComApMarginFront, balance.ComApMarginRear,
                ankleTargetDeg, ankleActualDeg, ankle.ModeledDemand,
                footPitchDeg, balance.SupportContactCount, slip);
        }

        private static float SignedXDegrees(Quaternion rotation)
        {
            if (rotation.w < 0f)
                rotation = new Quaternion(-rotation.x, -rotation.y, -rotation.z, -rotation.w);
            float magnitude = Mathf.Sqrt(rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z);
            if (magnitude <= 1e-6f)
                return 0f;
            float angle = 2f * Mathf.Atan2(magnitude, Mathf.Clamp(rotation.w, -1f, 1f));
            return rotation.x * (angle / magnitude) * Mathf.Rad2Deg;
        }
    }
}
