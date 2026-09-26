using System.Globalization;
using System.IO;
using System.Text;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Squat;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;

namespace PowerliftingSimulator.Tests
{
    internal sealed class GAM13V2QualificationTrace
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private readonly StringBuilder _csv = new StringBuilder(48 * 1024);
        private int _lastOutcomeStart = -1;

        public GAM13V2QualificationTrace()
        {
            _csv.AppendLine(
                "load_kg,tick,state,phase,bar_y_m,bar_vy_mps,surface_depth_m," +
                "com_x_m,com_y_m,com_z_m,com_vx_mps,com_vy_mps,com_vz_mps," +
                "support_center_x_m,support_center_z_m,com_margin_front_m,com_margin_rear_m," +
                "com_margin_left_m,com_margin_right_m,left_foot_contact,right_foot_contact," +
                "left_foot_slip_mps,right_foot_slip_mps,ankle_target_rad,ankle_actual_error_rad," +
                "knee_target_rad,knee_actual_error_rad,hip_target_rad,hip_actual_error_rad," +
                "trunk_target_rad,trunk_actual_error_rad,balance_ap_rad,balance_ml_rad," +
                "max_modeled_drive_demand,max_joint_limit_proximity,max_joint_anchor_separation_m," +
                "saddle_separation_m,rule_outcome,physical_outcome");
        }

        public void Append(
            float loadKg,
            SquatPhysicalPrototypeController controller,
            SquatObservationSnapshot snapshot,
            float maximumLimitProximity,
            float maximumJointAnchorSeparationM,
            string ruleOutcome,
            string physicalOutcome)
        {
            SquatSupportObservation support = snapshot.Support;
            Vector3Value com = support.SystemComWorldMeters;
            Vector3Value velocity = support.SystemComVelocityWorldMetersPerSecond;
            Vector3Value center = support.SupportCenterWorldMeters;
            SquatBalanceCorrectionV2 correction = controller.Adapter.BalanceCorrectionV2;
            SquatJointTargetError ankle = Joint(controller, "left_foot", "right_foot");
            SquatJointTargetError knee = Joint(controller, "left_shank", "right_shank");
            SquatJointTargetError hip = Joint(controller, "left_thigh", "right_thigh");
            SquatJointTargetError trunk = Trunk(controller);
            float saddleSeparation = controller.Saddle != null
                ? controller.Saddle.SaddleSeparationMeters
                : float.NaN;

            Value(loadKg); Value(snapshot.SimulationTick.ToString(Invariant));
            Value(snapshot.State.ToString()); Value(snapshot.Sq);
            Value(snapshot.Bar.PositionWorldMeters.Y); Value(snapshot.Bar.LinearVelocityWorldMetersPerSecond.Y);
            Value(snapshot.Depth.WorstSideDepthM);
            Value(com.X); Value(com.Y); Value(com.Z);
            Value(velocity.X); Value(velocity.Y); Value(velocity.Z);
            Value(center.X); Value(center.Z);
            Value(support.ComToSupportApFrontMarginM); Value(support.ComToSupportApRearMarginM);
            Value(support.ComToSupportMlLeftMarginM); Value(support.ComToSupportMlRightMarginM);
            Value(Bool(snapshot.LeftFoot.IsInContact)); Value(Bool(snapshot.RightFoot.IsInContact));
            Value(snapshot.LeftFoot.SlipSpeedMetersPerSecond); Value(snapshot.RightFoot.SlipSpeedMetersPerSecond);
            Value(ankle.TargetRad); Value(ankle.ActualErrorRad);
            Value(knee.TargetRad); Value(knee.ActualErrorRad);
            Value(hip.TargetRad); Value(hip.ActualErrorRad);
            Value(trunk.TargetRad); Value(trunk.ActualErrorRad);
            Value(correction.AppliedApRad); Value(correction.AppliedMlRad);
            Value(snapshot.MaximumModeledDemand); Value(maximumLimitProximity);
            Value(maximumJointAnchorSeparationM); Value(saddleSeparation);
            _lastOutcomeStart = _csv.Length;
            Value(ruleOutcome); Value(physicalOutcome);
            _csv.AppendLine();
        }

        public void SetFinalOutcomes(string ruleOutcome, string physicalOutcome)
        {
            if (_lastOutcomeStart < 0)
                return;
            _csv.Remove(_lastOutcomeStart, _csv.Length - _lastOutcomeStart);
            _csv.Append(',').Append(ruleOutcome).Append(',').Append(physicalOutcome).AppendLine();
        }

        public void Save(string path)
        {
            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(fullPath, _csv.ToString(), new UTF8Encoding(false));
        }

        private void Value(float value)
        {
            Value(float.IsFinite(value) ? value.ToString("R", Invariant) : "NA");
        }

        private void Value(string value)
        {
            if (_csv.Length > 0 && _csv[_csv.Length - 1] != '\n')
                _csv.Append(',');
            _csv.Append(value);
        }

        private static string Bool(bool value) => value ? "true" : "false";

        private static SquatJointTargetError Joint(
            SquatPhysicalPrototypeController controller,
            string leftId,
            string rightId)
        {
            PhysicalAthleteRig rig = controller.AthleteRig;
            PoweredJointController.PoweredJointRuntime left = rig.PoweredController.GetJoint(leftId);
            PoweredJointController.PoweredJointRuntime right = rig.PoweredController.GetJoint(rightId);
            float leftTarget = Target(controller.Adapter, leftId);
            float rightTarget = Target(controller.Adapter, rightId);
            float leftError = left != null && left.HasPostPhysicsDiagnostic ? left.PostPhysicsDiagnostic.ErrorRad.x : float.NaN;
            float rightError = right != null && right.HasPostPhysicsDiagnostic ? right.PostPhysicsDiagnostic.ErrorRad.x : float.NaN;
            float error = float.IsFinite(leftError) && float.IsFinite(rightError)
                ? (Mathf.Abs(leftError) >= Mathf.Abs(rightError) ? leftError : rightError)
                : float.IsFinite(leftError) ? leftError : rightError;
            float target = float.IsFinite(leftTarget) && float.IsFinite(rightTarget)
                ? 0.5f * (leftTarget + rightTarget)
                : float.IsFinite(leftTarget) ? leftTarget : rightTarget;
            return new SquatJointTargetError(target, error);
        }

        private static SquatJointTargetError Trunk(SquatPhysicalPrototypeController controller)
        {
            PhysicalAthleteRig rig = controller.AthleteRig;
            PoweredJointController.PoweredJointRuntime abdomen = rig.PoweredController.GetJoint("abdomen");
            PoweredJointController.PoweredJointRuntime thorax = rig.PoweredController.GetJoint("thorax");
            float abdomenTarget = Target(controller.Adapter, "abdomen");
            float thoraxTarget = Target(controller.Adapter, "thorax");
            float abdomenError = abdomen.HasPostPhysicsDiagnostic ? abdomen.PostPhysicsDiagnostic.ErrorRad.x : float.NaN;
            float thoraxError = thorax.HasPostPhysicsDiagnostic ? thorax.PostPhysicsDiagnostic.ErrorRad.x : float.NaN;
            return new SquatJointTargetError(
                abdomenTarget + thoraxTarget,
                float.IsFinite(abdomenError) && float.IsFinite(thoraxError) ? abdomenError + thoraxError : float.NaN);
        }

        private static float Target(SquatPhysicalAdapter adapter, string jointId) =>
            adapter.TryGetTargetComposition(jointId, out SquatPhysicalAdapter.JointTargetComposition composition)
                ? PoweredJointController.SignedTwistRadians(composition.Final, Vector3.right)
                : float.NaN;

        private readonly struct SquatJointTargetError
        {
            public SquatJointTargetError(float targetRad, float actualErrorRad)
            {
                TargetRad = targetRad;
                ActualErrorRad = actualErrorRad;
            }

            public float TargetRad { get; }
            public float ActualErrorRad { get; }
        }
    }
}
