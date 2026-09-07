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
    /// Phase 5H4. The balance gate passed an athlete folded double at the
    /// waist, so this audits the posture side of standing on its own.
    ///
    /// The reported numbers do not add up and that is the first thing to
    /// settle: about 39 deg of posture error at the abdomen alongside a
    /// modelled drive demand of only 0.33. On the trunk family, 800 Nm/rad
    /// against a 260 Nm ceiling, a genuine 39 deg of drive-space error would
    /// be worth roughly twice the ceiling. Both numbers cannot be describing
    /// the same angle.
    /// </summary>
    public sealed class PhysicalStandingPostureAuditTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const string MeasurementDirectory = "Artifacts/Measurements";

        private static readonly string[] AuditJoints =
        {
            "left_thigh", "right_thigh", "abdomen", "thorax",
            "left_shank", "right_shank", "left_foot", "right_foot"
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

        // ---------------------------------------------------------------
        // A1. The three baselines, with the full per-joint trace.
        //
        // A: nominal GAM-10 only, no preload, no balance.
        // B: nominal plus the corrected ankle balance, hip and trunk off.
        // C: whatever the production scene actually configures. Nothing in
        //    this case overrides a production default.
        // ---------------------------------------------------------------
        [UnityTest]
        public IEnumerator A1_POSTURE_AUTHORITY_AUDIT()
        {
            var summary = new StringBuilder();
            summary.AppendLine("configuration,joint,t_s," + AuditHeader());

            yield return AuditConfiguration("A_NOMINAL_ONLY", summary,
                balance: false, hipTrunk: false, preload: false, useProductionDefaults: false);
            yield return AuditConfiguration("B_ANKLE_BALANCE_ONLY", summary,
                balance: true, hipTrunk: false, preload: true, useProductionDefaults: false);
            yield return AuditConfiguration("C_PRODUCTION_CURRENT", summary,
                balance: false, hipTrunk: false, preload: false, useProductionDefaults: true);

            WriteMeasurement("GAM11-posture-authority-audit.csv", summary.ToString());
            Debug.Log("[A1 POSTURE AUTHORITY AUDIT]" + Environment.NewLine + summary);
            yield return null;
        }

        private IEnumerator AuditConfiguration(
            string label, StringBuilder summary,
            bool balance, bool hipTrunk, bool preload, bool useProductionDefaults)
        {
            yield return LoadFixture();
            SquatPhysicalAdapter adapter = _controller.Adapter;
            if (!useProductionDefaults)
            {
                _controller.SetLoad(0f);
                adapter.BalanceCorrectionsEnabled = balance;
                adapter.AnkleSagittalOffsetOverrideRad = null;
                adapter.Preload.Enabled = preload;
                adapter.Preload.CopyFrom(SquatEquilibriumPreload.QualifiedStanding());
                adapter.BalanceController.HipTrunkStrategyEnabled = hipTrunk;
            }

            // 3 s or until the athlete is unambiguously down.
            const int totalTicks = 300;
            int[] sampleTicks = { 0, 25, 60, 120, 200, 299 };
            int sampleIndex = 0;

            for (int tick = 0; tick <= totalTicks; tick++)
            {
                if (sampleIndex < sampleTicks.Length && tick == sampleTicks[sampleIndex])
                {
                    foreach (string jointId in AuditJoints)
                    {
                        summary.AppendLine(string.Format(CultureInfo.InvariantCulture,
                            "{0},{1},{2:F2},{3}", label, jointId,
                            tick * SimulationConstants.FixedDeltaTimeSeconds,
                            AuditRow(adapter, jointId)));
                    }
                    sampleIndex++;
                }
                if (tick < totalTicks)
                    Advance();
            }
        }

        private static string AuditHeader() =>
            "nominal_deg,preload_deg,balance_offset_deg,final_target_deg,actual_deg," +
            "posture_error_deg,drive_space_error_deg,drive_error_x_deg,drive_error_y_deg,drive_error_z_deg," +
            "applied_vs_nominal_deg,target_ang_vel_x,actual_ang_vel_x,low_limit,high_limit,limit_proximity," +
            "spring,damper,max_force_nm,activation,capacity_scale,modelled_demand," +
            "implied_spring_torque_nm,solver_torque_x_nm";

        private string AuditRow(SquatPhysicalAdapter adapter, string jointId)
        {
            PoweredJointController.PoweredJointRuntime joint = _rig.PoweredController.GetJoint(jointId);
            PoweredJointDiagnostic diagnostic = joint.Diagnostic;
            adapter.TryGetTargetComposition(jointId, out SquatPhysicalAdapter.JointTargetComposition composition);
            JointFamilyProfile profile = joint.Profile.HasValue ? joint.Profile.Value : default;

            // The whole point of the audit: the posture metric compares the
            // actual pose with the GAM-10 nominal, while the drive only ever
            // sees the applied target. If those two disagree the joint can be
            // tracking perfectly and still be in the wrong place.
            float postureError = Quaternion.Angle(composition.Nominal, diagnostic.ActualRelative);
            float driveSpaceError = Quaternion.Angle(diagnostic.AppliedTarget, diagnostic.ActualRelative);
            float appliedVsNominal = Quaternion.Angle(composition.Nominal, diagnostic.AppliedTarget);
            float impliedSpringTorque = profile.Spring * diagnostic.ErrorRad.magnitude;

            return string.Format(CultureInfo.InvariantCulture,
                "{0:F2},{1:F2},{2:F2},{3:F2},{4:F2},{5:F2},{6:F2},{7:F3},{8:F3},{9:F3},{10:F2}," +
                "{11:F3},{12:F3},{13:F1},{14:F1},{15:F3},{16:F0},{17:F0},{18:F1},{19:F2},{20:F2},{21:F3}," +
                "{22:F1},{23:F3}",
                SagittalDegrees(composition.Nominal), SagittalDegrees(composition.GravityBias),
                SagittalDegrees(composition.BalanceOffset), SagittalDegrees(composition.Final),
                SagittalDegrees(diagnostic.ActualRelative),
                postureError, driveSpaceError,
                diagnostic.ErrorRad.x * Mathf.Rad2Deg,
                diagnostic.ErrorRad.y * Mathf.Rad2Deg,
                diagnostic.ErrorRad.z * Mathf.Rad2Deg,
                appliedVsNominal,
                diagnostic.TargetAngularVelocityRadS.x, diagnostic.ActualAngularVelocityRadS.x,
                joint.Recipe.LowDegrees, joint.Recipe.HighDegrees, diagnostic.LimitProximity,
                profile.Spring, profile.Damper, diagnostic.MaximumForceNm,
                diagnostic.Activation, diagnostic.CapacityScale, diagnostic.ModeledDemand,
                impliedSpringTorque, diagnostic.SolverFlexionTorqueNm);
        }

        // ---------------------------------------------------------------
        // Shared harness. Every case reloads the scene, which is the only
        // reset that returns the bodies to their authored spawn pose.
        // ---------------------------------------------------------------
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
