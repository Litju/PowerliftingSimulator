using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Squat;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;

namespace PowerliftingSimulator.Tests
{
    internal sealed class GAM48Gate4bRecorder
    {
        private static readonly string[] DepthStages =
        {
            "NOMINAL_TARGET",
            "GRAVITY_TARGET",
            "BALANCE_TARGET",
            "FINAL_TARGET",
            "APPLIED_TARGET",
            "ACTUAL"
        };

        private readonly float[] _leftDepthSums = new float[DepthStages.Length];
        private readonly float[] _rightDepthSums = new float[DepthStages.Length];
        private readonly float[] _leftDepthMins = new float[DepthStages.Length];
        private readonly float[] _leftDepthMaxes = new float[DepthStages.Length];
        private readonly float[] _rightDepthMins = new float[DepthStages.Length];
        private readonly float[] _rightDepthMaxes = new float[DepthStages.Length];
        private readonly float[] _worstDepthMins = new float[DepthStages.Length];
        private readonly float[] _worstDepthMaxes = new float[DepthStages.Length];
        private readonly StringBuilder _compositionCsv = new StringBuilder();
        private readonly StringBuilder _appliedCsv = new StringBuilder();
        private readonly float _referenceJointCenterLeft;
        private readonly float _referenceJointCenterRight;
        private readonly float _referenceSurfaceLeft;
        private readonly float _referenceSurfaceRight;
        private int _sampleCount;
        private float _maximumModeledDriveDemand;

        public GAM48Gate4bRecorder(SquatPhysicalAdapter adapter)
        {
            for (int index = 0; index < DepthStages.Length; index++)
            {
                _leftDepthMins[index] = float.PositiveInfinity;
                _rightDepthMins[index] = float.PositiveInfinity;
                _worstDepthMins[index] = float.PositiveInfinity;
                _leftDepthMaxes[index] = float.NegativeInfinity;
                _rightDepthMaxes[index] = float.NegativeInfinity;
                _worstDepthMaxes[index] = float.NegativeInfinity;
            }

            SquatReferenceRigCalibration calibration = adapter.ReferenceCalibration;
            Assert.That(calibration, Is.Not.Null, "The runtime adapter did not expose its GAM-10 calibration.");

            SquatReferenceKinematicSolution solution = SquatReferenceKinematics.Solve(
                calibration,
                adapter.ReferenceAnatomicalPose(1f, SquatPhaseDirection.Descent),
                adapter.LeftReferencePlantarAnchorWorld,
                adapter.RightReferencePlantarAnchorWorld);
            Assert.That(solution.IsValid, Is.True, solution.RejectionReason);

            var provider = new SquatDepthLandmarkProvider(calibration);
            Assert.That(provider.TryEvaluate(
                solution.LeftLeg.HipCenter,
                solution.RightLeg.HipCenter,
                solution.PelvisFrameRotation,
                solution.LeftLeg.KneeCenter,
                solution.LeftLeg.ShankFrameRotation,
                solution.RightLeg.KneeCenter,
                solution.RightLeg.ShankFrameRotation,
                out SquatRuleLandmarkSet reference), Is.True);
            _referenceJointCenterLeft = reference.JointCenterDepthDiagnostic.LeftDepthM;
            _referenceJointCenterRight = reference.JointCenterDepthDiagnostic.RightDepthM;
            _referenceSurfaceLeft = reference.Depth.LeftDepthM;
            _referenceSurfaceRight = reference.Depth.RightDepthM;

            _compositionCsv.AppendLine(
                "sample,joint_id,nominal_x,nominal_y,nominal_z,nominal_w,gravity_bias_x,gravity_bias_y,gravity_bias_z,gravity_bias_w,balance_offset_x,balance_offset_y,balance_offset_z,balance_offset_w,final_x,final_y,final_z,final_w");
            _appliedCsv.AppendLine(
                "sample,joint_id,final_x,final_y,final_z,final_w,applied_x,applied_y,applied_z,applied_w,final_to_applied_error_rad,max_target_rate_rad_s");
        }

        public float MaximumModeledDriveDemand => _maximumModeledDriveDemand;

        public void Capture(
            int sample,
            SquatPhysicalAdapter adapter,
            PhysicalAthleteRig rig,
            SquatObservationSnapshot actual,
            float maximumModeledDriveDemand)
        {
            var targets = new Dictionary<string, Quaternion>[5];
            for (int index = 0; index < targets.Length; index++)
                targets[index] = new Dictionary<string, Quaternion>(rig.PoweredController.Joints.Count);

            foreach (PoweredJointController.PoweredJointRuntime joint in rig.PoweredController.Joints)
            {
                SquatPhysicalAdapter.JointTargetComposition composition;
                if (adapter.TryGetTargetComposition(joint.Id, out composition))
                {
                    _compositionCsv.Append(sample.ToString(CultureInfo.InvariantCulture));
                    _compositionCsv.Append(',');
                    _compositionCsv.Append(joint.Id);
                    AppendQuaternion(_compositionCsv, composition.Nominal);
                    AppendQuaternion(_compositionCsv, composition.GravityBias);
                    AppendQuaternion(_compositionCsv, composition.BalanceOffset);
                    AppendQuaternion(_compositionCsv, composition.Final);
                    _compositionCsv.AppendLine();
                }
                else
                {
                    Assert.That(joint.Id, Is.EqualTo("head_neck"),
                        "Only the passive head/neck joint may omit a squat target composition.");
                    composition = new SquatPhysicalAdapter.JointTargetComposition(
                        Quaternion.identity,
                        Quaternion.identity,
                        Quaternion.identity,
                        Quaternion.identity);
                }

                targets[0].Add(joint.Id, composition.Nominal);
                targets[1].Add(joint.Id, composition.Nominal * composition.GravityBias);
                targets[2].Add(joint.Id, composition.Nominal * composition.BalanceOffset);
                targets[3].Add(joint.Id, composition.Final);
                targets[4].Add(joint.Id, joint.AppliedTarget);

                float appliedError = Quaternion.Angle(composition.Final, joint.AppliedTarget) * Mathf.Deg2Rad;
                float maximumRate = joint.Profile.HasValue ? joint.Profile.Value.MaxTargetRateRadS : 0f;
                _appliedCsv.Append(sample.ToString(CultureInfo.InvariantCulture));
                _appliedCsv.Append(',');
                _appliedCsv.Append(joint.Id);
                AppendQuaternion(_appliedCsv, composition.Final);
                AppendQuaternion(_appliedCsv, joint.AppliedTarget);
                _appliedCsv.Append(',');
                _appliedCsv.Append(F(appliedError));
                _appliedCsv.Append(',');
                _appliedCsv.AppendLine(F(maximumRate));
            }

            Quaternion pelvisRotation = adapter.ReferencePelvisBodyRotation(1f, SquatPhaseDirection.Descent);
            if (_sampleCount == 0)
            {
                BodyState[] before = CaptureBodyStates(rig);
                SquatRuleLandmarkSet repeatedA = SquatPhysicalTargetForwardKinematics.ReconstructLandmarks(
                    rig, adapter.ReferenceCalibration, targets[0], pelvisRotation);
                SquatRuleLandmarkSet repeatedB = SquatPhysicalTargetForwardKinematics.ReconstructLandmarks(
                    rig, adapter.ReferenceCalibration, targets[0], pelvisRotation);
                Assert.That(Mathf.Abs(repeatedA.Depth.LeftDepthM - repeatedB.Depth.LeftDepthM), Is.LessThanOrEqualTo(1e-7f));
                Assert.That(Mathf.Abs(repeatedA.Depth.RightDepthM - repeatedB.Depth.RightDepthM), Is.LessThanOrEqualTo(1e-7f));
                Assert.That(Mathf.Abs(repeatedA.JointCenterDepthDiagnostic.LeftDepthM - repeatedB.JointCenterDepthDiagnostic.LeftDepthM), Is.LessThanOrEqualTo(1e-7f));
                Assert.That(Mathf.Abs(repeatedA.JointCenterDepthDiagnostic.RightDepthM - repeatedB.JointCenterDepthDiagnostic.RightDepthM), Is.LessThanOrEqualTo(1e-7f));
                AssertBodyStatesUnchanged(rig, before);
            }

            for (int index = 0; index < 5; index++)
            {
                SquatRuleLandmarkSet landmarks = SquatPhysicalTargetForwardKinematics.ReconstructLandmarks(
                    rig,
                    adapter.ReferenceCalibration,
                    targets[index],
                    pelvisRotation);
                AddDepth(index, landmarks.Depth.LeftDepthM, landmarks.Depth.RightDepthM);
            }

            AddDepth(5, actual.Depth.LeftDepthM, actual.Depth.RightDepthM);
            _maximumModeledDriveDemand = Mathf.Max(_maximumModeledDriveDemand, maximumModeledDriveDemand);
            _sampleCount++;
        }

        public void Write(string label, bool supportRetained, bool finiteValidControl)
        {
            Assert.That(_sampleCount, Is.EqualTo(50), "Gate 4b requires the complete settled report window.");

            string directory = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "Artifacts",
                "Measurements",
                "GAM-48"));
            Directory.CreateDirectory(directory);

            var report = new StringBuilder();
            report.AppendLine("MISSION=GAM48_GATE4B_RUNTIME_COMMAND_SPACE_DECOMPOSITION");
            report.AppendLine("ARM=" + label);
            report.AppendLine("FRESH_UNITY_PROCESS_REQUIRED=true");
            report.AppendLine("LOAD_KG=25");
            report.AppendLine("PHASE=1");
            report.AppendLine("SQUAT_PHASE_DIRECTION=DESCENT");
            report.AppendLine("SETTLE_WINDOW_TICKS=200");
            report.AppendLine("SETTLED_REPORT_SAMPLES=" + _sampleCount.ToString(CultureInfo.InvariantCulture));
            report.AppendLine("ROOT_CARRIER=RUNTIME_ADAPTER_GAM10_PHYSICAL_PELVIS_ROTATION");
            report.AppendLine("ROOT_TRANSLATION=ZERO_DEPTH_DIFFERENCES_TRANSLATION_INVARIANT");
            report.AppendLine("LANDMARK_AUTHORITY=GAM10_SHARED_PELVIS_AND_SHANK_FRAME_SURFACE_PROXY");
            report.AppendLine("DEPTH_METRIC=hip-crease surface proxy Y minus corresponding knee-top surface proxy Y");
            report.AppendLine("JOINT_CENTER_CHANNEL=DIAGNOSTIC_ONLY");
            report.AppendLine("GAME_JUDGMENT_WORST_SIDE_M=" + F(-SquatDepthGeometry.GAME_JUDGMENT_MARGIN_M));
            report.AppendLine("NEUTRAL_ANCHOR_TOLERANCE_M=" + F(PhysicalAthleteDefinition.AnchorToleranceMeters));
            report.AppendLine("FRESH_PROCESS_NUMERIC_TOLERANCE_M=0.00001");
            report.AppendLine("MATERIAL_CONTRIBUTION_THRESHOLD_M=0.005");
            report.AppendLine("MODELED_DRIVE_DEMAND_FORMULA=(Kp*error+Kd*velocity_error).magnitude/max(maximumForce,0.001 N*m)");
            report.AppendLine("SUPPORT_RETAINED=" + B(supportRetained));
            report.AppendLine("FINITE_VALID_CONTROL=" + B(finiteValidControl));
            report.AppendLine("MODELED_DRIVE_DEMAND=" + F(_maximumModeledDriveDemand));
            report.AppendLine("MODELED_DRIVE_DEMAND_HIGH=" +
                B(_maximumModeledDriveDemand >= PoweredJointController.ModeledDemandSaturationThreshold));
            report.AppendLine("MODELED_DRIVE_DEMAND_THRESHOLD=" +
                F(PoweredJointController.ModeledDemandSaturationThreshold));

            report.AppendLine("D_REF_JOINT_CENTER_DIAGNOSTIC_LEFT_M=" + F(_referenceJointCenterLeft));
            report.AppendLine("D_REF_JOINT_CENTER_DIAGNOSTIC_RIGHT_M=" + F(_referenceJointCenterRight));
            AppendDepth(report, "D_REF_SURFACE_RULE_PROXY", _referenceSurfaceLeft, _referenceSurfaceRight,
                _referenceSurfaceLeft, _referenceSurfaceLeft, _referenceSurfaceRight, _referenceSurfaceRight,
                Mathf.Max(_referenceSurfaceLeft, _referenceSurfaceRight), Mathf.Max(_referenceSurfaceLeft, _referenceSurfaceRight));
            for (int index = 0; index < DepthStages.Length; index++)
            {
                AppendDepth(report, "D_" + DepthStages[index], MeanLeft(index), MeanRight(index),
                    _leftDepthMins[index], _leftDepthMaxes[index],
                    _rightDepthMins[index], _rightDepthMaxes[index],
                    _worstDepthMins[index], _worstDepthMaxes[index]);
            }
            AppendDelta(report, "NOMINAL_MAPPING_ERROR", MeanLeft(0) - _referenceSurfaceLeft, MeanRight(0) - _referenceSurfaceRight);
            AppendDelta(report, "GRAVITY_COMPOSITION_DISPLACEMENT", MeanLeft(1) - MeanLeft(0), MeanRight(1) - MeanRight(0));
            AppendDelta(report, "BALANCE_COMPOSITION_DISPLACEMENT", MeanLeft(2) - MeanLeft(0), MeanRight(2) - MeanRight(0));
            AppendDelta(report, "FULL_COMPOSITION_DISPLACEMENT", MeanLeft(3) - MeanLeft(0), MeanRight(3) - MeanRight(0));
            AppendDelta(report, "RATE_LIMIT_DISPLACEMENT", MeanLeft(4) - MeanLeft(3), MeanRight(4) - MeanRight(3));
            AppendDelta(report, "PHYSICAL_REALIZATION_ERROR", MeanLeft(5) - MeanLeft(4), MeanRight(5) - MeanRight(4));
            report.AppendLine("ACTUAL_SETTLED_MEAN_WORST_SIDE_DEPTH_M=" + F(Mathf.Max(MeanLeft(5), MeanRight(5))));
            report.AppendLine("CLAIM_CEILING=GAM10_CALIBRATED_SURFACE_PROXY_AND_ENGINE_RUNTIME_OBSERVATION");

            File.WriteAllText(
                Path.Combine(directory, "gate4b-runtime-" + label + "-decomposition.md"),
                report.ToString());
            File.WriteAllText(
                Path.Combine(directory, "gate4b-runtime-" + label + "-target-composition.csv"),
                _compositionCsv.ToString());
            File.WriteAllText(
                Path.Combine(directory, "gate4b-runtime-" + label + "-applied-target.csv"),
                _appliedCsv.ToString());
        }

        private float MeanLeft(int index) => _leftDepthSums[index] / _sampleCount;
        private float MeanRight(int index) => _rightDepthSums[index] / _sampleCount;

        private void AddDepth(int index, float left, float right)
        {
            float worst = Mathf.Max(left, right);
            _leftDepthSums[index] += left;
            _rightDepthSums[index] += right;
            _leftDepthMins[index] = Mathf.Min(_leftDepthMins[index], left);
            _leftDepthMaxes[index] = Mathf.Max(_leftDepthMaxes[index], left);
            _rightDepthMins[index] = Mathf.Min(_rightDepthMins[index], right);
            _rightDepthMaxes[index] = Mathf.Max(_rightDepthMaxes[index], right);
            _worstDepthMins[index] = Mathf.Min(_worstDepthMins[index], worst);
            _worstDepthMaxes[index] = Mathf.Max(_worstDepthMaxes[index], worst);
        }

        private static void AppendDepth(
            StringBuilder output,
            string name,
            float left,
            float right,
            float leftMin,
            float leftMax,
            float rightMin,
            float rightMax,
            float worstMin,
            float worstMax)
        {
            output.AppendLine(name + "_LEFT_DEPTH_M=" + F(left));
            output.AppendLine(name + "_RIGHT_DEPTH_M=" + F(right));
            output.AppendLine(name + "_WORST_DEPTH_M=" + F(Mathf.Max(left, right)));
            output.AppendLine(name + "_SAMPLE_MIN_LEFT_DEPTH_M=" + F(leftMin));
            output.AppendLine(name + "_SAMPLE_MAX_LEFT_DEPTH_M=" + F(leftMax));
            output.AppendLine(name + "_SAMPLE_MIN_RIGHT_DEPTH_M=" + F(rightMin));
            output.AppendLine(name + "_SAMPLE_MAX_RIGHT_DEPTH_M=" + F(rightMax));
            output.AppendLine(name + "_SAMPLE_MIN_WORST_DEPTH_M=" + F(worstMin));
            output.AppendLine(name + "_SAMPLE_MAX_WORST_DEPTH_M=" + F(worstMax));
            output.AppendLine(name + "_GAME_JUDGMENT_QUALIFIED=" + B(Mathf.Max(left, right) <= -SquatDepthGeometry.GAME_JUDGMENT_MARGIN_M));
        }

        private static void AppendDelta(StringBuilder output, string name, float left, float right)
        {
            output.AppendLine(name + "_LEFT_M=" + F(left));
            output.AppendLine(name + "_RIGHT_M=" + F(right));
            output.AppendLine(name + "_WORST_M=" + F(Mathf.Max(left, right)));
        }

        private static void AppendQuaternion(StringBuilder output, Quaternion value)
        {
            output.Append(',');
            output.Append(F(value.x));
            output.Append(',');
            output.Append(F(value.y));
            output.Append(',');
            output.Append(F(value.z));
            output.Append(',');
            output.Append(F(value.w));
        }

        private static BodyState[] CaptureBodyStates(PhysicalAthleteRig rig)
        {
            var states = new BodyState[rig.Segments.Count];
            int index = 0;
            foreach (PhysicalAthleteRig.SegmentRuntime segment in rig.Segments.Values)
            {
                states[index++] = new BodyState(
                    segment.Body,
                    segment.Body.position,
                    segment.Body.rotation,
                    segment.Body.linearVelocity,
                    segment.Body.angularVelocity,
                    segment.Body.IsSleeping());
            }
            return states;
        }

        private static void AssertBodyStatesUnchanged(PhysicalAthleteRig rig, BodyState[] states)
        {
            Assert.That(rig.Segments.Count, Is.EqualTo(states.Length));
            foreach (BodyState state in states)
            {
                Assert.That(state.Body.position, Is.EqualTo(state.Position));
                Assert.That(Quaternion.Angle(state.Body.rotation, state.Rotation), Is.EqualTo(0f));
                Assert.That(state.Body.linearVelocity, Is.EqualTo(state.LinearVelocity));
                Assert.That(state.Body.angularVelocity, Is.EqualTo(state.AngularVelocity));
                Assert.That(state.Body.IsSleeping(), Is.EqualTo(state.Sleeping));
            }
        }

        private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
        private static string B(bool value) => value ? "true" : "false";

        private readonly struct BodyState
        {
            public BodyState(
                Rigidbody body,
                Vector3 position,
                Quaternion rotation,
                Vector3 linearVelocity,
                Vector3 angularVelocity,
                bool sleeping)
            {
                Body = body;
                Position = position;
                Rotation = rotation;
                LinearVelocity = linearVelocity;
                AngularVelocity = angularVelocity;
                Sleeping = sleeping;
            }

            public Rigidbody Body { get; }
            public Vector3 Position { get; }
            public Quaternion Rotation { get; }
            public Vector3 LinearVelocity { get; }
            public Vector3 AngularVelocity { get; }
            public bool Sleeping { get; }
        }
    }
}
