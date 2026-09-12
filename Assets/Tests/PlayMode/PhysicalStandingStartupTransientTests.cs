using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Foundation.Unity;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.Tests
{
    /// <summary>
    /// Phase 5H6. The posture guard collapses to zero authority during
    /// startup, and it does so because the nominal-only athlete already
    /// swings about 25 deg away from the canonical standing pose before
    /// recovering on its own. Nothing is enabled here except the GAM-10
    /// standing target, so whatever the athlete does in this suite it does
    /// without a controller to blame.
    /// </summary>
    public sealed class PhysicalStandingStartupTransientTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const string MeasurementDirectory = "Artifacts/Measurements";

        /// <summary>
        /// The joints standing posture is judged on. Ankles are excluded:
        /// they are balance-actuated, and an authorised ankle offset is a
        /// deviation from the canonical ankle pose by construction. They are
        /// traced separately against their own target, limit and contact.
        /// </summary>
        private static readonly string[] ProtectedPostureJoints =
        {
            "left_thigh", "right_thigh", "abdomen", "thorax", "left_shank", "right_shank"
        };

        private FoundationBootstrap _bootstrap;
        private PhysicalAthleteRig _rig;
        private SquatPhysicalPrototypeController _controller;

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
        public IEnumerator N0_NOMINAL_ONLY_STARTUP_TRANSIENT()
        {
            yield return LoadFixture();
            _controller.SetLoad(0f);
            SquatPhysicalAdapter adapter = _controller.Adapter;
            adapter.BalanceCorrectionsEnabled = false;
            adapter.AnkleSagittalOffsetOverrideRad = null;
            adapter.Preload.Enabled = false;
            adapter.Preload.Clear();
            adapter.BalanceController.HipTrunkStrategyEnabled = false;
            adapter.BalanceController.PostureGuardEnabled = false;

            // Pre-simulation state. The drives are primed and their targets
            // snapped before the first step, so if a joint does not already
            // sit on its target here the transient begins as an initial
            // condition mismatch rather than as a response to anything.
            var initial = new StringBuilder();
            initial.AppendLine("joint,initial_actual_deg,nominal_target_deg,drive_space_error_deg," +
                               "initial_ang_vel_x,target_ang_vel_x,limit_proximity,max_force_nm");
            foreach (string jointId in ProtectedPostureJoints)
                initial.AppendLine(jointId + "," + InitialRow(adapter, jointId));
            initial.AppendLine("left_foot," + InitialRow(adapter, "left_foot"));
            initial.AppendLine("right_foot," + InitialRow(adapter, "right_foot"));

            var trace = new StringBuilder();
            trace.AppendLine("tick,t_s,hip_l_err,hip_r_err,abdomen_err,thorax_err,knee_l_err,knee_r_err," +
                             "protected_max_err,ankle_l_actual,ankle_l_track_err,ankle_l_limit_prox," +
                             "pelvis_y,pelvis_vy,com_ap,com_ap_vel,com_y_vel,cop_ap,fz_n," +
                             "left_contacts,right_contacts,foot_pitch_deg,abdomen_ang_vel,abdomen_demand");

            const int totalTicks = 500;
            var abdomenSeries = new float[totalTicks + 1];
            var hipSeries = new float[totalTicks + 1];
            var thoraxSeries = new float[totalTicks + 1];

            for (int tick = 0; tick <= totalTicks; tick++)
            {
                abdomenSeries[tick] = PostureErrorDeg(adapter, "abdomen");
                hipSeries[tick] = Mathf.Max(
                    PostureErrorDeg(adapter, "left_thigh"), PostureErrorDeg(adapter, "right_thigh"));
                thoraxSeries[tick] = PostureErrorDeg(adapter, "thorax");

                // Full rate through the transient, thinned once it is over.
                if (tick <= 200 || tick % 5 == 0)
                    trace.AppendLine(TransientRow(tick, adapter));
                if (tick < totalTicks)
                    Advance();
            }

            var report = new StringBuilder();
            report.AppendLine("# initial pre-simulation state");
            report.Append(initial);
            report.AppendLine("# transient trace");
            report.Append(trace);
            report.AppendLine("# mode identification");
            report.AppendLine("joint,peak_deg,time_to_peak_s,first_trough_s,second_peak_deg," +
                              "second_peak_s,apparent_period_s,decay_ratio,err_0p25,err_0p5,err_1,err_2,err_3,err_5");
            report.AppendLine("abdomen," + IdentifyMode(abdomenSeries));
            report.AppendLine("hip," + IdentifyMode(hipSeries));
            report.AppendLine("thorax," + IdentifyMode(thoraxSeries));

            WriteMeasurement("GAM11-n0-nominal-startup-transient.csv", report.ToString());
            Debug.Log("[N0 NOMINAL ONLY STARTUP]" + Environment.NewLine + report);

            // A joint angle returning toward its target does not mean the
            // athlete recovered. Read on its own, the abdomen trace looks
            // like a 26 deg excursion that settles back to 3 deg; read next
            // to the pelvis, the error falls because the body is lying on the
            // floor and gravity has stopped loading the trunk.
            //
            // That misreading is what sent two missions chasing a settle
            // transient that does not exist, so it is asserted rather than
            // left as a comment.
            Assert.That(_rig.Segments["pelvis"].Body.position.y, Is.LessThan(0.5f),
                "Nominal-only standing no longer falls. That would be a real change in the " +
                "plant and every conclusion drawn from this baseline needs revisiting.");
            Assert.That(adapter.Balance.SystemCom.z, Is.GreaterThan(0.5f),
                "The nominal-only athlete no longer departs forward. " +
                "Re-derive the baseline before trusting anything built on it.");
            yield return null;
        }

        private string InitialRow(SquatPhysicalAdapter adapter, string jointId)
        {
            PoweredJointDiagnostic diagnostic = _rig.PoweredController.GetJoint(jointId).Diagnostic;
            adapter.TryGetTargetComposition(jointId, out SquatPhysicalAdapter.JointTargetComposition composition);
            return string.Format(CultureInfo.InvariantCulture,
                "{0:F3},{1:F3},{2:F3},{3:F4},{4:F4},{5:F3},{6:F1}",
                SagittalDegrees(diagnostic.ActualRelative), SagittalDegrees(composition.Nominal),
                Quaternion.Angle(diagnostic.AppliedTarget, diagnostic.ActualRelative),
                diagnostic.ActualAngularVelocityRadS.x, diagnostic.TargetAngularVelocityRadS.x,
                diagnostic.LimitProximity, diagnostic.MaximumForceNm);
        }

        private float PostureErrorDeg(SquatPhysicalAdapter adapter, string jointId)
        {
            if (!adapter.TryGetTargetComposition(jointId, out SquatPhysicalAdapter.JointTargetComposition composition))
                return 0f;
            return Quaternion.Angle(
                composition.Nominal, _rig.PoweredController.GetJoint(jointId).Diagnostic.ActualRelative);
        }

        private string TransientRow(int tick, SquatPhysicalAdapter adapter)
        {
            SquatBalanceObserver balance = adapter.Balance;
            PoweredJointDiagnostic ankle = _rig.PoweredController.GetJoint("left_foot").Diagnostic;
            PoweredJointDiagnostic abdomen = _rig.PoweredController.GetJoint("abdomen").Diagnostic;
            Rigidbody pelvis = _rig.Segments["pelvis"].Body;
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;

            float protectedMax = 0f;
            foreach (string jointId in ProtectedPostureJoints)
                protectedMax = Mathf.Max(protectedMax, PostureErrorDeg(adapter, jointId));

            return string.Format(CultureInfo.InvariantCulture,
                "{0},{1:F3},{2:F3},{3:F3},{4:F3},{5:F3},{6:F3},{7:F3},{8:F3},{9:F3},{10:F3},{11:F3}," +
                "{12:F4},{13:F4},{14:F5},{15:F5},{16:F5},{17:F5},{18:F1},{19},{20},{21:F3},{22:F4},{23:F3}",
                tick, tick * SimulationConstants.FixedDeltaTimeSeconds,
                PostureErrorDeg(adapter, "left_thigh"), PostureErrorDeg(adapter, "right_thigh"),
                PostureErrorDeg(adapter, "abdomen"), PostureErrorDeg(adapter, "thorax"),
                PostureErrorDeg(adapter, "left_shank"), PostureErrorDeg(adapter, "right_shank"),
                protectedMax,
                SagittalDegrees(ankle.ActualRelative),
                Quaternion.Angle(ankle.AppliedTarget, ankle.ActualRelative),
                ankle.LimitProximity,
                pelvis.position.y, pelvis.linearVelocity.y,
                balance.SystemCom.z, balance.SystemComVelocity.z, balance.SystemComVelocity.y,
                balance.HasCopEstimate ? balance.CopEstimate.z : float.NaN,
                balance.TotalNormalImpulse / dt,
                _controller.LeftFootContact != null ? _controller.LeftFootContact.ContactCount : 0,
                _controller.RightFootContact != null ? _controller.RightFootContact.ContactCount : 0,
                FootPitchDegrees("left_foot"),
                abdomen.ActualAngularVelocityRadS.x, abdomen.ModeledDemand);
        }

        private float FootPitchDegrees(string segmentId)
        {
            if (!_rig.Segments.TryGetValue(segmentId, out PhysicalAthleteRig.SegmentRuntime foot) || foot.Body == null)
                return 0f;
            Vector3 forward = foot.Body.rotation * Vector3.forward;
            return Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Peak, time to peak, and whether the response actually oscillates.
        /// A second peak after a trough is what separates an underdamped mode
        /// from a gravity offset the drive is merely slow to pull back, and
        /// the two want completely different repairs.
        /// </summary>
        private static string IdentifyMode(float[] series)
        {
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
            int peakIndex = 0;
            for (int index = 1; index < series.Length; index++)
                if (series[index] > series[peakIndex])
                    peakIndex = index;

            int troughIndex = -1;
            for (int index = peakIndex + 1; index < series.Length - 1; index++)
            {
                if (series[index] <= series[index - 1] && series[index] <= series[index + 1])
                {
                    troughIndex = index;
                    break;
                }
            }

            int secondPeakIndex = -1;
            if (troughIndex > 0)
            {
                for (int index = troughIndex + 1; index < series.Length - 1; index++)
                {
                    if (series[index] >= series[index - 1] && series[index] >= series[index + 1])
                    {
                        secondPeakIndex = index;
                        break;
                    }
                }
            }

            float period = secondPeakIndex > 0 ? (secondPeakIndex - peakIndex) * dt : float.NaN;
            float decay = secondPeakIndex > 0 && series[peakIndex] > 1e-6f
                ? series[secondPeakIndex] / series[peakIndex]
                : float.NaN;

            return string.Format(CultureInfo.InvariantCulture,
                "{0:F3},{1:F3},{2:F3},{3:F3},{4:F3},{5:F3},{6:F4},{7:F3},{8:F3},{9:F3},{10:F3},{11:F3},{12:F3}",
                series[peakIndex], peakIndex * dt,
                troughIndex > 0 ? troughIndex * dt : float.NaN,
                secondPeakIndex > 0 ? series[secondPeakIndex] : float.NaN,
                secondPeakIndex > 0 ? secondPeakIndex * dt : float.NaN,
                period, decay,
                Sample(series, 25), Sample(series, 50), Sample(series, 100),
                Sample(series, 200), Sample(series, 300), Sample(series, 500));
        }

        private static float Sample(float[] series, int index) =>
            index < series.Length ? series[index] : float.NaN;

        private IEnumerator LoadFixture()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(QualificationScene, LoadSceneMode.Single);
            while (!load.isDone)
                yield return null;
            yield return null;

            _bootstrap = UnityEngine.Object.FindFirstObjectByType<FoundationBootstrap>();
            _rig = UnityEngine.Object.FindFirstObjectByType<PhysicalAthleteRig>();
            _controller = UnityEngine.Object.FindFirstObjectByType<SquatPhysicalPrototypeController>();
            for (int frame = 0; frame < 8 && !_controller.IsInitialized; frame++)
                yield return null;
            Assert.That(_controller.IsInitialized, Is.True, _controller.StartupFailure);
            _bootstrap.enabled = false;
        }

        private void Advance()
        {
            _bootstrap.Runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
            if (_controller.LeftFootContact != null)
                _controller.LeftFootContact.PhysicsTickUpdate(dt);
            if (_controller.RightFootContact != null)
                _controller.RightFootContact.PhysicsTickUpdate(dt);
        }

        private static float SagittalDegrees(Quaternion rotation)
        {
            if (rotation.w < 0f)
                rotation = new Quaternion(-rotation.x, -rotation.y, -rotation.z, -rotation.w);
            float magnitude = Mathf.Sqrt(rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z);
            if (magnitude <= 1e-6f)
                return 0f;
            float angle = 2f * Mathf.Atan2(magnitude, Mathf.Clamp(rotation.w, -1f, 1f));
            return rotation.x * (angle / magnitude) * Mathf.Rad2Deg;
        }

        private static void WriteMeasurement(string filename, string content)
        {
            Directory.CreateDirectory(Path.GetFullPath(MeasurementDirectory));
            File.WriteAllText(Path.Combine(Path.GetFullPath(MeasurementDirectory), filename), content);
        }
    }
}
