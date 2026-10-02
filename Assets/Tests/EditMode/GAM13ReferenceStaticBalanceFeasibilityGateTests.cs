using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Squat;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;
using UnityEditor.SceneManagement;

namespace PowerliftingSimulator.Tests
{
    /// <summary>
    /// Test-only, no-dynamics feasibility gate for the current GAM-10 q_ref.
    /// It loads only the reference preview and performs rigid FK from the exact
    /// PhysicalAthleteDefinition recipes, joint frames, and planted foot boxes.
    /// It does not build a physical rig, invoke feedback, or advance physics.
    /// </summary>
    public sealed class GAM13ReferenceStaticBalanceFeasibilityGateTests
    {
        private const string ReferenceScene = "SquatReferencePreview";
        private const string MeasurementDirectory = "Artifacts/Measurements/GAM-13/reference-static-balance-gate";
        // GAM11's physical upper-limb bar-support task accepts a 25 mm target residual.
        private const float BarParityToleranceM = 0.025f;
        private const float MinimumStaticSupportReserveM = 0.02f;

        private static readonly float[] Phases = { 0.00f, 0.04f, 0.10f, 0.20f, 0.25f, 0.40f, 0.55f, 0.75f, 1.00f };
        private static readonly float[] LoadsKg = { 25f, 60f, 140f, 170f, 300f };

        private SquatReferencePreview _preview;
        private Animator _animator;
        private SquatReferenceRigCalibration _calibration;
        private SquatReferenceProfile _profile;
        private PhysicalMapping _mapping;
        private Vector3 _leftStandingFootAnchor;
        private Vector3 _rightStandingFootAnchor;

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Prototype/" + ReferenceScene + ".unity", OpenSceneMode.Single);

            _preview = UnityEngine.Object.FindFirstObjectByType<SquatReferencePreview>();
            Assert.That(_preview, Is.Not.Null);
            _animator = _preview.ReferenceAnimator;
            if (_animator == null)
                _animator = _preview.GetComponentInChildren<Animator>(true);
            Assert.That(_animator, Is.Not.Null);
            FieldInfo rootField = typeof(SquatReferencePreview).GetField("referenceRoot", BindingFlags.Instance | BindingFlags.NonPublic);
            Transform referenceRoot = (Transform)rootField.GetValue(_preview);
            if (referenceRoot == null)
                referenceRoot = _preview.transform;
            _calibration = SquatReferenceRigCalibration.Build(_animator, referenceRoot, SquatReferencePreview.AssetPath);
            float leftPlantY = Vector3.Dot(_calibration.LeftFoot.PlantarAnchorWorld, _calibration.GameUp);
            float rightPlantY = Vector3.Dot(_calibration.RightFoot.PlantarAnchorWorld, _calibration.GameUp);
            referenceRoot.position -= _calibration.GameUp * ((leftPlantY + rightPlantY) * 0.5f);
            _calibration = SquatReferenceRigCalibration.Build(_animator, referenceRoot, SquatReferencePreview.AssetPath);
            _leftStandingFootAnchor = ProjectToStandingPlane(_calibration.LeftFoot.PlantarAnchorWorld, _calibration.GameUp);
            _rightStandingFootAnchor = ProjectToStandingPlane(_calibration.RightFoot.PlantarAnchorWorld, _calibration.GameUp);
            _profile = SquatReferenceProfile.CanonicalPowerliftingSquatV1;
            Assert.That(_calibration, Is.Not.Null);
            _mapping = PhysicalMapping.Capture(_animator);
        }

        [Test]
        public void GAM13_REFERENCE_STATIC_BALANCE_FEASIBILITY_GATE()
        {
            var phaseRows = new List<PhaseResult>(Phases.Length);
            var jointRows = new StringBuilder("phase,joint_id,headroom_deg,low_deg,high_deg,secondary_limit_deg\n");

            foreach (float phase in Phases)
            {
                SquatReferencePose qRef = _profile.Evaluate(phase, SquatPhaseDirection.Descent);
                SquatReferenceKinematicSolution solution = SquatReferenceKinematics.Solve(
                    _calibration,
                    qRef,
                    _leftStandingFootAnchor,
                    _rightStandingFootAnchor);
                Assert.That(solution.IsValid, Is.True, $"Invalid canonical q_ref at phase {phase:F2}: {solution.RejectionReason}");

                IReadOnlyDictionary<string, Quaternion> desiredBodyRotations = DesiredBodyRotations(solution);
                IReadOnlyDictionary<string, Quaternion> logicalTargets = BuildLogicalTargets(qRef, solution, desiredBodyRotations);
                IReadOnlyDictionary<string, BodyPose> bodyPoses = ReconstructPhysicalForwardKinematics(
                    solution, logicalTargets, desiredBodyRotations["pelvis"]);
                Vector3 athleteCom = AthleteCenterOfMass(bodyPoses, out float athleteMassKg);
                Assert.That(athleteMassKg, Is.EqualTo(PhysicalAthleteDefinition.PrototypeBodyMassKg).Within(0.0001f));

                Vector3 referenceBar = SquatReferenceUpperLimb.Solve(_calibration, solution).BarCenter;
                BodyPose thorax = bodyPoses["thorax"];
                Vector3 saddleBar = thorax.Position + thorax.Rotation * SquatBarSaddle.ThoraxLocalAnchor;
                SupportRange support = PlantedFootSupport(bodyPoses);

                float minimumHeadroom = float.PositiveInfinity;
                string limitingJoint = string.Empty;
                foreach (PhysicalJointRecipe recipe in PhysicalAthleteDefinition.Joints)
                {
                    float margin = JointLimitHeadroomDegrees(logicalTargets[recipe.ChildId], recipe);
                    if (margin < minimumHeadroom)
                    {
                        minimumHeadroom = margin;
                        limitingJoint = recipe.ChildId;
                    }
                    jointRows.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0:F2},{1},{2:F5},{3:F3},{4:F3},{5:F3}", phase, recipe.ChildId, margin,
                        recipe.LowDegrees, recipe.HighDegrees, recipe.SecondaryLimitDegrees));
                }

                phaseRows.Add(new PhaseResult(
                    phase, referenceBar, saddleBar, athleteCom, support,
                    minimumHeadroom, limitingJoint));
            }

            var rows = new List<LoadResult>(Phases.Length * LoadsKg.Length);
            foreach (PhaseResult phase in phaseRows)
            {
                float supportCenterAp = 0.5f * (phase.Support.MinimumApWorld + phase.Support.MaximumApWorld);
                float athleteComAp = Vector3.Dot(phase.AthleteCom, _calibration.GameForward) - supportCenterAp;
                float referenceBarAp = Vector3.Dot(phase.ReferenceBar, _calibration.GameForward) - supportCenterAp;
                float saddleBarAp = Vector3.Dot(phase.SaddleBar, _calibration.GameForward) - supportCenterAp;
                float barParityAp = saddleBarAp - referenceBarAp;
                Vector3 barError = phase.SaddleBar - phase.ReferenceBar;
                float supportMin = phase.Support.MinimumApWorld - supportCenterAp;
                float supportMax = phase.Support.MaximumApWorld - supportCenterAp;

                foreach (float loadKg in LoadsKg)
                {
                    float combinedComAp = (athleteComAp * PhysicalAthleteDefinition.PrototypeBodyMassKg +
                        saddleBarAp * loadKg) /
                        (PhysicalAthleteDefinition.PrototypeBodyMassKg + loadKg);
                    rows.Add(new LoadResult(
                        phase, loadKg, referenceBarAp, saddleBarAp, barParityAp, barError.magnitude,
                        athleteComAp, supportMin, supportMax, combinedComAp,
                        supportMax - combinedComAp, combinedComAp - supportMin,
                        combinedComAp));
                }
            }

            string disposition = Decide(rows);
            WriteEvidence(rows, jointRows.ToString(), disposition);
            Debug.Log(BuildSummary(rows, disposition));

            Assert.That(phaseRows.Count, Is.EqualTo(Phases.Length));
            Assert.That(rows.Count, Is.EqualTo(Phases.Length * LoadsKg.Length));
        }

        private IReadOnlyDictionary<string, Quaternion> DesiredBodyRotations(SquatReferenceKinematicSolution solution)
        {
            var rotations = new Dictionary<string, Quaternion>(PhysicalAthleteDefinition.Segments.Count);
            SquatReferenceUpperLimbSolution arms = SquatReferenceUpperLimb.Solve(_calibration, solution);
            foreach (PhysicalSegmentRecipe segment in PhysicalAthleteDefinition.Segments)
            {
                Quaternion desiredBoneRotation;
                switch (segment.Id)
                {
                    case "pelvis": desiredBoneRotation = solution.PelvisBoneRotation; break;
                    case "abdomen": desiredBoneRotation = solution.SpineFrameRotation * _calibration.Spine.BoneFromAnatomicalFrame; break;
                    case "thorax": desiredBoneRotation = solution.ChestFrameRotation * _calibration.Chest.BoneFromAnatomicalFrame; break;
                    case "head_neck": desiredBoneRotation = solution.HeadFrameRotation * _calibration.Head.BoneFromAnatomicalFrame; break;
                    case "left_upper_arm": desiredBoneRotation = arms.Left.UpperArmBoneRotation; break;
                    case "right_upper_arm": desiredBoneRotation = arms.Right.UpperArmBoneRotation; break;
                    case "left_forearm": desiredBoneRotation = arms.Left.ForearmBoneRotation; break;
                    case "right_forearm": desiredBoneRotation = arms.Right.ForearmBoneRotation; break;
                    case "left_hand": desiredBoneRotation = arms.Left.HandBoneRotation; break;
                    case "right_hand": desiredBoneRotation = arms.Right.HandBoneRotation; break;
                    case "left_thigh": desiredBoneRotation = solution.LeftLeg.ThighBoneRotation; break;
                    case "right_thigh": desiredBoneRotation = solution.RightLeg.ThighBoneRotation; break;
                    case "left_shank": desiredBoneRotation = solution.LeftLeg.ShankBoneRotation; break;
                    case "right_shank": desiredBoneRotation = solution.RightLeg.ShankBoneRotation; break;
                    case "left_foot": desiredBoneRotation = solution.LeftLeg.FootBoneRotation; break;
                    case "right_foot": desiredBoneRotation = solution.RightLeg.FootBoneRotation; break;
                    default: throw new InvalidOperationException($"No canonical q_ref rotation mapping for '{segment.Id}'.");
                }
                rotations.Add(segment.Id, desiredBoneRotation *
                    Quaternion.Inverse(_mapping.BindBoneRotations[segment.Id]) *
                    _mapping.BindBodyRotations[segment.Id]);
            }
            return rotations;
        }

        private IReadOnlyDictionary<string, Quaternion> BuildLogicalTargets(
            SquatReferencePose qRef,
            SquatReferenceKinematicSolution solution,
            IReadOnlyDictionary<string, Quaternion> desiredBodyRotations)
        {
            var targets = new Dictionary<string, Quaternion>(PhysicalAthleteDefinition.Joints.Count);
            foreach (PhysicalJointRecipe recipe in PhysicalAthleteDefinition.Joints)
            {
                PhysicalJointMap joint = _mapping.Joints[recipe.ChildId];
                string parentId = _mapping.Segments[recipe.ChildId].ParentId;
                Quaternion desiredRelative = Quaternion.Inverse(desiredBodyRotations[parentId]) *
                    desiredBodyRotations[recipe.ChildId];
                targets.Add(recipe.ChildId, PoweredJointController.ToLogicalTargetRotation(
                    joint.NeutralParentToChild, joint.JointSpace, desiredRelative));
            }

            Quaternion abdomen = targets["abdomen"];
            Quaternion thorax = targets["thorax"];
            NormalizeTrunkDistribution(qRef.TrunkFlexionRad, ref abdomen, ref thorax);
            targets["abdomen"] = abdomen;
            targets["thorax"] = thorax;
            SquatReferenceUpperLimbSolution arms = SquatReferenceUpperLimb.Solve(_calibration, solution);
            // The production mapper projects the reference hands from the unnormalized q_ref thorax pose.
            Quaternion rawThorax = DesiredPhysicalBodyRotation("thorax", solution);
            SetArmTargets(targets, arms.Left, rawThorax, true);
            SetArmTargets(targets, arms.Right, rawThorax, false);
            return targets;
        }

        private Quaternion DesiredPhysicalBodyRotation(string segmentId, SquatReferenceKinematicSolution solution)
        {
            Quaternion boneRotation;
            switch (segmentId)
            {
                case "thorax": boneRotation = solution.ChestFrameRotation * _calibration.Chest.BoneFromAnatomicalFrame; break;
                default: throw new ArgumentOutOfRangeException(nameof(segmentId));
            }
            return boneRotation * Quaternion.Inverse(_mapping.BindBoneRotations[segmentId]) *
                _mapping.BindBodyRotations[segmentId];
        }

        private void SetArmTargets(
            IDictionary<string, Quaternion> targets,
            SquatReferenceUpperLimbSide limb,
            Quaternion desiredThoraxBodyRotation,
            bool isLeft)
        {
            string side = isLeft ? "left" : "right";
            string upperArmId = side + "_upper_arm";
            string forearmId = side + "_forearm";
            string handId = side + "_hand";
            PhysicalJointRecipe shoulderRecipe = _mapping.Joints[upperArmId].Recipe;
            PhysicalJointRecipe elbowRecipe = _mapping.Joints[forearmId].Recipe;

            Vector3 humerus = (limb.ElbowCenter - limb.ShoulderCenter).normalized;
            Vector3 forearmDirection = (limb.HandCenter - limb.ElbowCenter).normalized;
            Vector3 planeNormal = Vector3.Cross(humerus, forearmDirection).normalized;
            float magnitude = Vector3.Angle(humerus, forearmDirection);
            SquatReferenceBoneFrame upperArmBone = isLeft ? _calibration.LeftUpperArm : _calibration.RightUpperArm;
            SquatReferenceBoneFrame forearmBone = isLeft ? _calibration.LeftForearm : _calibration.RightForearm;
            SquatReferenceBoneFrame handBone = isLeft ? _calibration.LeftHand : _calibration.RightHand;
            Vector3 elbowToHand = Quaternion.Inverse(forearmBone.BindRotation) *
                (handBone.BindPosition - forearmBone.BindPosition);
            PhysicalJointMap elbowJoint = _mapping.Joints[forearmId];
            Vector3 longAxisInBone = Quaternion.Inverse(upperArmBone.BindRotation) *
                (forearmBone.BindPosition - upperArmBone.BindPosition);
            Vector3 longAxisInBody = (_mapping.BodyToReferenceBone[upperArmId] * longAxisInBone).normalized;
            Vector3 hingeInBody = (elbowJoint.NeutralParentToChild * elbowJoint.PrimaryAxisInChild).normalized;

            Quaternion bestUpperArm = Quaternion.identity;
            Quaternion bestForearm = Quaternion.identity;
            float bestScore = float.PositiveInfinity;
            bool found = false;
            foreach (float normalSign in new[] { 1f, -1f })
            {
                Quaternion candidateUpperArm = HumerusBodyRotation(
                    humerus, planeNormal * normalSign, longAxisInBody, hingeInBody);
                Quaternion shoulderLogical = LogicalFromChildBody(
                    _mapping.Joints[upperArmId], desiredThoraxBodyRotation, candidateUpperArm);
                float shoulderExcess = BallLimitExcessDegrees(shoulderLogical, shoulderRecipe);

                foreach (float flexion in new[] { magnitude, -magnitude })
                {
                    float elbowExcess = Mathf.Max(0f,
                        Mathf.Max(elbowRecipe.LowDegrees - flexion, flexion - elbowRecipe.HighDegrees));
                    Quaternion candidateForearm = ChildBodyFromLogical(
                        elbowJoint, candidateUpperArm, Quaternion.AngleAxis(flexion, Vector3.right));
                    Vector3 hand = limb.ElbowCenter +
                        (candidateForearm * _mapping.BodyToReferenceBone[forearmId]) * elbowToHand;
                    float residual = Vector3.Distance(hand, limb.HandCenter);
                    float reachPenalty = residual > 0.05f ? 10000f * residual : 0f;
                    float score = reachPenalty + residual * 1000f + shoulderExcess + elbowExcess * 10f;
                    if (!found || score < bestScore)
                    {
                        found = true;
                        bestScore = score;
                        bestUpperArm = candidateUpperArm;
                        bestForearm = candidateForearm;
                    }
                }
            }

            if (!found)
                throw new InvalidOperationException($"The physical {side} arm projection found no finite candidate.");

            Quaternion desiredHandBody = limb.HandBoneRotation *
                Quaternion.Inverse(_mapping.BodyToReferenceBone[handId]);
            Quaternion handLogical = LogicalFromChildBody(_mapping.Joints[handId], bestForearm, desiredHandBody);
            handLogical = ClampToBallLimits(handLogical, _mapping.Joints[handId].Recipe);
            targets[upperArmId] = LogicalFromChildBody(
                _mapping.Joints[upperArmId], desiredThoraxBodyRotation, bestUpperArm);
            targets[forearmId] = LogicalFromChildBody(elbowJoint, bestUpperArm, bestForearm);
            targets[handId] = handLogical;
        }

        private IReadOnlyDictionary<string, BodyPose> ReconstructPhysicalForwardKinematics(
            SquatReferenceKinematicSolution solution,
            IReadOnlyDictionary<string, Quaternion> logicalTargets,
            Quaternion pelvisBodyRotation)
        {
            var poses = new Dictionary<string, BodyPose>(PhysicalAthleteDefinition.Segments.Count);
            PhysicalSegmentRecipe pelvisRecipe = _mapping.Segments["pelvis"];
            poses.Add("pelvis", new BodyPose(
                solution.PelvisBonePosition + pelvisRecipe.FixedCenterOffsetMeters,
                pelvisBodyRotation));

            foreach (PhysicalSegmentRecipe segment in PhysicalAthleteDefinition.Segments)
            {
                if (segment.ParentId == null)
                    continue;
                PhysicalJointMap joint = _mapping.Joints[segment.Id];
                BodyPose parent = poses[segment.ParentId];
                Quaternion relative = PoweredJointController.ToParentChildRelativeRotation(
                    joint.NeutralParentToChild, joint.JointSpace, logicalTargets[segment.Id]);
                Quaternion childRotation = parent.Rotation * relative;
                Vector3 parentAnchor = parent.Position + parent.Rotation * joint.ParentAnchorLocal;
                Vector3 childPosition = parentAnchor - childRotation * joint.ChildAnchorLocal;
                poses.Add(segment.Id, new BodyPose(childPosition, childRotation));
            }
            return poses;
        }

        private Vector3 AthleteCenterOfMass(IReadOnlyDictionary<string, BodyPose> poses, out float totalMassKg)
        {
            Vector3 weighted = Vector3.zero;
            totalMassKg = 0f;
            foreach (PhysicalSegmentRecipe recipe in PhysicalAthleteDefinition.Segments)
            {
                float mass = PhysicalAthleteDefinition.PrototypeBodyMassKg * recipe.MassFraction;
                weighted += poses[recipe.Id].Position * mass;
                totalMassKg += mass;
            }
            return weighted / totalMassKg;
        }

        private SupportRange PlantedFootSupport(IReadOnlyDictionary<string, BodyPose> poses)
        {
            float min = float.PositiveInfinity;
            float max = float.NegativeInfinity;
            foreach (string footId in new[] { "left_foot", "right_foot" })
            {
                PhysicalSegmentRecipe foot = _mapping.Segments[footId];
                BodyPose pose = poses[footId];
                Assert.That(Quaternion.Angle(pose.Rotation, _calibration.PlantedFootFrameRotation),
                    Is.LessThan(0.01f), $"{footId} is not planted in the calibrated physical foot frame.");
                Vector3 axisX = pose.Rotation * Vector3.right;
                Vector3 axisY = pose.Rotation * Vector3.up;
                Vector3 axisZ = pose.Rotation * Vector3.forward;
                float extent = Mathf.Abs(Vector3.Dot(_calibration.GameForward, axisX)) * foot.DimensionsMeters.x * 0.5f +
                    Mathf.Abs(Vector3.Dot(_calibration.GameForward, axisY)) * foot.DimensionsMeters.y * 0.5f +
                    Mathf.Abs(Vector3.Dot(_calibration.GameForward, axisZ)) * foot.DimensionsMeters.z * 0.5f;
                float centerAp = Vector3.Dot(pose.Position, _calibration.GameForward);
                min = Mathf.Min(min, centerAp - extent);
                max = Mathf.Max(max, centerAp + extent);
            }
            return new SupportRange(min, max);
        }

        private static void NormalizeTrunkDistribution(
            float canonicalTrunkFlexionRad,
            ref Quaternion abdomen,
            ref Quaternion thorax)
        {
            float abdomenRad = SignedSagittalRadians(abdomen);
            float thoraxRad = SignedSagittalRadians(thorax);
            float participatingSum = abdomenRad + thoraxRad;
            if (Mathf.Abs(participatingSum) < 0.0017f || Mathf.Abs(canonicalTrunkFlexionRad) < 0.0017f)
                return;
            float scale = canonicalTrunkFlexionRad / participatingSum;
            if (!float.IsFinite(scale) || scale <= 0f)
                return;
            scale = Mathf.Min(scale, 4f);
            abdomen = Quaternion.SlerpUnclamped(Quaternion.identity, abdomen, scale);
            thorax = Quaternion.SlerpUnclamped(Quaternion.identity, thorax, scale);
        }

        private static float SignedSagittalRadians(Quaternion rotation)
        {
            rotation = Canonical(rotation);
            float magnitude = Mathf.Sqrt(rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z);
            return magnitude <= 1e-6f
                ? 0f
                : rotation.x * (2f * Mathf.Atan2(magnitude, Mathf.Clamp(rotation.w, -1f, 1f)) / magnitude);
        }

        private static float JointLimitHeadroomDegrees(Quaternion logical, PhysicalJointRecipe recipe)
        {
            logical = Canonical(logical);
            float x = PoweredJointController.SignedTwistRadians(logical, Vector3.right) * Mathf.Rad2Deg;
            float headroom = Mathf.Min(x - recipe.LowDegrees, recipe.HighDegrees - x);
            if (recipe.Kind == PhysicalJointKind.Ball)
            {
                Vector3 axis = new Vector3(logical.x, logical.y, logical.z);
                float magnitude = axis.magnitude;
                if (magnitude > 1e-7f)
                {
                    float angle = 2f * Mathf.Atan2(magnitude, Mathf.Clamp(logical.w, -1f, 1f)) * Mathf.Rad2Deg;
                    Vector3 degrees = axis * (angle / magnitude);
                    float secondary = new Vector2(degrees.y, degrees.z).magnitude;
                    headroom = Mathf.Min(headroom, recipe.SecondaryLimitDegrees - secondary);
                }
            }
            return headroom;
        }

        private static float BallLimitExcessDegrees(Quaternion logical, PhysicalJointRecipe recipe)
        {
            logical = Canonical(logical);
            Vector3 axis = new Vector3(logical.x, logical.y, logical.z);
            float magnitude = axis.magnitude;
            if (magnitude <= 1e-7f)
                return 0f;
            float angle = 2f * Mathf.Atan2(magnitude, Mathf.Clamp(logical.w, -1f, 1f)) * Mathf.Rad2Deg;
            Vector3 degrees = axis * (angle / magnitude);
            float primary = Mathf.Max(0f, Mathf.Max(recipe.LowDegrees - degrees.x, degrees.x - recipe.HighDegrees));
            float secondary = Mathf.Max(0f,
                new Vector2(degrees.y, degrees.z).magnitude - recipe.SecondaryLimitDegrees);
            return primary + secondary;
        }

        private static Quaternion ClampToBallLimits(Quaternion logical, PhysicalJointRecipe recipe)
        {
            logical = Canonical(logical);
            Vector3 axis = new Vector3(logical.x, logical.y, logical.z);
            float magnitude = axis.magnitude;
            if (magnitude <= 1e-7f)
                return Quaternion.identity;
            float angle = 2f * Mathf.Atan2(magnitude, Mathf.Clamp(logical.w, -1f, 1f)) * Mathf.Rad2Deg;
            Vector3 degrees = axis * (angle / magnitude);
            float x = Mathf.Clamp(degrees.x, recipe.LowDegrees, recipe.HighDegrees);
            Vector2 secondary = new Vector2(degrees.y, degrees.z);
            if (secondary.magnitude > recipe.SecondaryLimitDegrees)
                secondary = secondary.normalized * recipe.SecondaryLimitDegrees;
            Vector3 clamped = new Vector3(x, secondary.x, secondary.y);
            float clampedMagnitude = clamped.magnitude;
            return clampedMagnitude <= 1e-5f
                ? Quaternion.identity
                : Quaternion.AngleAxis(clampedMagnitude, clamped / clampedMagnitude);
        }

        private Quaternion LogicalFromChildBody(
            PhysicalJointMap joint,
            Quaternion parentBodyRotation,
            Quaternion childBodyRotation)
        {
            Quaternion relative = Quaternion.Inverse(parentBodyRotation) * childBodyRotation;
            Quaternion neutralDelta = Quaternion.Inverse(joint.NeutralParentToChild) * relative;
            return Quaternion.Inverse(joint.JointSpace) * neutralDelta * joint.JointSpace;
        }

        private Quaternion ChildBodyFromLogical(
            PhysicalJointMap joint,
            Quaternion parentBodyRotation,
            Quaternion logical)
        {
            Quaternion relative = PoweredJointController.ToParentChildRelativeRotation(
                joint.NeutralParentToChild, joint.JointSpace, logical);
            return parentBodyRotation * relative;
        }

        private static Vector3 ProjectToStandingPlane(Vector3 point, Vector3 up) =>
            point - Vector3.Dot(point, up) * up;

        private string Decide(IReadOnlyList<LoadResult> rows)
        {
            foreach (LoadResult row in rows)
                if (row.BarParityErrorWorldM > BarParityToleranceM)
                    return "A_BAR_REFERENCE_MAPPING_MISMATCH";

            foreach (LoadResult row in rows)
                if (Mathf.Min(row.FrontSupportMarginM, row.RearSupportMarginM) < MinimumStaticSupportReserveM)
                    return "Q_REF_STATIC_BALANCE_NOT_QUALIFIED";
            return "C_STATIC_BALANCE_FEASIBLE_PHASED_COM_REFERENCE_REQUIRED";
        }

        private void WriteEvidence(IReadOnlyList<LoadResult> rows, string jointRows, string disposition)
        {
            Directory.CreateDirectory(MeasurementDirectory);
            var csv = new StringBuilder("phase,load_kg,reference_bar_ap_relative_support_m,physical_saddle_bar_ap_relative_support_m," +
                "bar_parity_error_ap_m,bar_parity_error_world_m,athlete_com_ap_m,support_ap_min_m,support_ap_max_m," +
                "combined_com_ap_m,front_support_margin_m,rear_support_margin_m,required_static_cop_ap_m," +
                "min_joint_limit_headroom_deg,limiting_joint_id\n");
            foreach (LoadResult row in rows)
            {
                csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:F2},{1:F0},{2:F6},{3:F6},{4:F6},{5:F6},{6:F6},{7:F6},{8:F6},{9:F6},{10:F6},{11:F6},{12:F6},{13:F4},{14}",
                    row.Phase.Phase, row.LoadKg, row.ReferenceBarApM, row.SaddleBarApM, row.BarParityApM,
                    row.BarParityErrorWorldM, row.AthleteComApM, row.SupportMinM, row.SupportMaxM,
                    row.CombinedComApM, row.FrontSupportMarginM, row.RearSupportMarginM,
                    row.RequiredStaticCopApM, row.Phase.MinimumJointHeadroomDeg, row.Phase.LimitingJointId));
            }
            File.WriteAllText(Path.Combine(MeasurementDirectory, "gate.csv"), csv.ToString());
            File.WriteAllText(Path.Combine(MeasurementDirectory, "joint-headroom.csv"), jointRows);
            File.WriteAllText(Path.Combine(MeasurementDirectory, "gate-summary.md"), BuildSummary(rows, disposition));
        }

        private string BuildSummary(IReadOnlyList<LoadResult> rows, string disposition)
        {
            var text = new StringBuilder();
            text.AppendLine("# GAM-13 test-only reference static-balance feasibility gate");
            text.AppendLine();
            text.AppendLine("No physical scene, runtime controller, feedback, or physics simulation was used.");
            text.AppendLine("q_ref comes from the canonical GAM-10 profile and closed-chain kinematic solver.");
            text.AppendLine("Physical body FK uses PhysicalAthleteDefinition masses, COM origins, joint/bone anchors and frames; support uses planted foot-box geometry.");
            text.AppendLine($"Bar parity threshold: {BarParityToleranceM:F3} m (accepted physical bar-support task tolerance). Static support reserve: {MinimumStaticSupportReserveM:F3} m.");
            text.AppendLine();
            text.AppendLine($"**Disposition: `{disposition}`**");
            text.AppendLine();
            text.AppendLine("| Load | Phase | Reference bar AP | Saddle bar AP | Parity XYZ | Athlete COM AP | Combined COM AP | Support [min,max] | Front margin | Rear margin | Required COP AP | Min joint headroom |");
            text.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach (LoadResult row in rows)
            {
                text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "| {0:F0} kg | {1:F2} | {2:F4} m | {3:F4} m | {4:F4} m | {5:F4} m | {6:F4} m | [{7:F4}, {8:F4}] m | {9:F4} m | {10:F4} m | {11:F4} m | {12:F2}° ({13}) |",
                    row.LoadKg, row.Phase.Phase, row.ReferenceBarApM, row.SaddleBarApM, row.BarParityErrorWorldM,
                    row.AthleteComApM, row.CombinedComApM, row.SupportMinM, row.SupportMaxM,
                    row.FrontSupportMarginM, row.RearSupportMarginM, row.RequiredStaticCopApM,
                    row.Phase.MinimumJointHeadroomDeg, row.Phase.LimitingJointId));
            }
            text.AppendLine();
            text.AppendLine("Required static COP AP equals combined athlete+bar COM AP at zero horizontal acceleration.");
            text.AppendLine("Joint headroom is the minimum across all 15 mapped physical joints at each phase; see `joint-headroom.csv` for each joint.");
            return text.ToString();
        }

        private sealed class PhysicalMapping
        {
            public readonly Dictionary<string, PhysicalSegmentRecipe> Segments = new Dictionary<string, PhysicalSegmentRecipe>(StringComparer.Ordinal);
            public readonly Dictionary<string, Quaternion> BindBodyRotations = new Dictionary<string, Quaternion>(StringComparer.Ordinal);
            public readonly Dictionary<string, Quaternion> BindBoneRotations = new Dictionary<string, Quaternion>(StringComparer.Ordinal);
            public readonly Dictionary<string, Quaternion> BodyToReferenceBone = new Dictionary<string, Quaternion>(StringComparer.Ordinal);
            public readonly Dictionary<string, PhysicalJointMap> Joints = new Dictionary<string, PhysicalJointMap>(StringComparer.Ordinal);

            public static PhysicalMapping Capture(Animator animator)
            {
                PhysicalAthleteDefinition.ValidateDefinition();
                var mapping = new PhysicalMapping();
                var centers = new Dictionary<string, Vector3>(StringComparer.Ordinal);
                foreach (PhysicalSegmentRecipe recipe in PhysicalAthleteDefinition.Segments)
                {
                    mapping.Segments.Add(recipe.Id, recipe);
                    Transform proximal = Require(animator, recipe.ProximalBone);
                    Transform distal = Require(animator, recipe.DistalBone);
                    Vector3 axis = distal.position - proximal.position;
                    Vector3 center = recipe.ProximalBone == recipe.DistalBone
                        ? proximal.position
                        : Vector3.Lerp(proximal.position, distal.position, recipe.ComFraction);
                    center += recipe.FixedCenterOffsetMeters;
                    Quaternion bodyRotation = ResolveBodyRotation(recipe, axis);
                    Transform visibleBone = Require(animator, recipe.VisibleBone);
                    mapping.BindBodyRotations.Add(recipe.Id, bodyRotation);
                    mapping.BindBoneRotations.Add(recipe.Id, visibleBone.rotation);
                    mapping.BodyToReferenceBone.Add(recipe.Id,
                        Quaternion.Inverse(bodyRotation) * visibleBone.rotation);
                    centers.Add(recipe.Id, center);
                }

                foreach (PhysicalJointRecipe recipe in PhysicalAthleteDefinition.Joints)
                {
                    PhysicalSegmentRecipe child = mapping.Segments[recipe.ChildId];
                    PhysicalSegmentRecipe parent = mapping.Segments[child.ParentId];
                    Vector3 anchor = Require(animator, recipe.AnchorBone).position;
                    Quaternion childBind = mapping.BindBodyRotations[child.Id];
                    Quaternion parentBind = mapping.BindBodyRotations[parent.Id];
                    Vector3 childAnchorLocal = Quaternion.Inverse(childBind) * (anchor - centers[child.Id]);
                    Vector3 parentAnchorLocal = Quaternion.Inverse(parentBind) * (anchor - centers[parent.Id]);
                    Vector3 primaryWorld = ResolvePrimaryAxis(animator, recipe, child, parent);
                    Vector3 secondaryWorld = Mathf.Abs(Vector3.Dot(primaryWorld, Vector3.up)) < 0.9f
                        ? Vector3.up
                        : Vector3.forward;
                    secondaryWorld = (secondaryWorld - Vector3.Dot(secondaryWorld, primaryWorld) * primaryWorld).normalized;
                    Vector3 primaryLocal = Quaternion.Inverse(childBind) * primaryWorld;
                    Vector3 secondaryLocal = Quaternion.Inverse(childBind) * secondaryWorld;
                    Vector3 right = primaryLocal.normalized;
                    Vector3 forward = Vector3.Cross(right, secondaryLocal).normalized;
                    Vector3 up = Vector3.Cross(forward, right).normalized;
                    Quaternion jointSpace = Quaternion.LookRotation(forward, up);
                    mapping.Joints.Add(recipe.ChildId, new PhysicalJointMap(
                        recipe,
                        Quaternion.Inverse(parentBind) * childBind,
                        jointSpace,
                        primaryLocal.normalized,
                        parentAnchorLocal,
                        childAnchorLocal));
                }
                return mapping;
            }

            private static Quaternion ResolveBodyRotation(PhysicalSegmentRecipe recipe, Vector3 axis)
            {
                if (axis.sqrMagnitude <= 1e-8f || recipe.Id.EndsWith("_foot", StringComparison.Ordinal))
                    return Quaternion.identity;
                if (recipe.Id.EndsWith("_hand", StringComparison.Ordinal))
                    return Quaternion.FromToRotation(Vector3.right, axis.normalized);
                return Quaternion.FromToRotation(Vector3.up, axis.normalized);
            }

            private static Vector3 ResolvePrimaryAxis(
                Animator animator,
                PhysicalJointRecipe recipe,
                PhysicalSegmentRecipe child,
                PhysicalSegmentRecipe parent)
            {
                if (recipe.AxisSource == PhysicalJointAxisSource.World)
                    return recipe.PrimaryAxisWorld.normalized;
                Transform proximal = Require(animator, child.ProximalBone);
                Transform distal = Require(animator, child.DistalBone);
                Transform parentProximal = Require(animator, parent.ProximalBone);
                Vector3 parentLongAxis = (proximal.position - parentProximal.position).normalized;
                Vector3 forward = animator.transform.root.forward;
                Vector3 axis = Vector3.Cross(parentLongAxis, forward);
                Vector3 segmentLongAxis = (distal.position - proximal.position).normalized;
                Vector3 flexionTravel = Vector3.Cross(axis.normalized, segmentLongAxis);
                if (Vector3.Dot(flexionTravel, forward) < 0f)
                    axis = -axis;
                return axis.normalized;
            }

            private static Transform Require(Animator animator, HumanBodyBones bone)
            {
                Transform transform = animator.GetBoneTransform(bone);
                if (transform == null)
                    throw new InvalidOperationException($"Required physical reference bone {bone} is missing.");
                return transform;
            }
        }

        private readonly struct PhysicalJointMap
        {
            public PhysicalJointMap(
                PhysicalJointRecipe recipe,
                Quaternion neutralParentToChild,
                Quaternion jointSpace,
                Vector3 primaryAxisInChild,
                Vector3 parentAnchorLocal,
                Vector3 childAnchorLocal)
            {
                Recipe = recipe;
                NeutralParentToChild = neutralParentToChild;
                JointSpace = jointSpace;
                PrimaryAxisInChild = primaryAxisInChild;
                ParentAnchorLocal = parentAnchorLocal;
                ChildAnchorLocal = childAnchorLocal;
            }
            public PhysicalJointRecipe Recipe { get; }
            public Quaternion NeutralParentToChild { get; }
            public Quaternion JointSpace { get; }
            public Vector3 PrimaryAxisInChild { get; }
            public Vector3 ParentAnchorLocal { get; }
            public Vector3 ChildAnchorLocal { get; }
        }

        private readonly struct BodyPose
        {
            public BodyPose(Vector3 position, Quaternion rotation) { Position = position; Rotation = rotation; }
            public Vector3 Position { get; }
            public Quaternion Rotation { get; }
        }

        private readonly struct SupportRange
        {
            public SupportRange(float minimumApWorld, float maximumApWorld)
            { MinimumApWorld = minimumApWorld; MaximumApWorld = maximumApWorld; }
            public float MinimumApWorld { get; }
            public float MaximumApWorld { get; }
        }

        private sealed class PhaseResult
        {
            public PhaseResult(float phase, Vector3 referenceBar, Vector3 saddleBar, Vector3 athleteCom,
                SupportRange support, float minimumJointHeadroomDeg, string limitingJointId)
            {
                Phase = phase; ReferenceBar = referenceBar; SaddleBar = saddleBar; AthleteCom = athleteCom;
                Support = support; MinimumJointHeadroomDeg = minimumJointHeadroomDeg;
                LimitingJointId = limitingJointId;
            }
            public float Phase { get; }
            public Vector3 ReferenceBar { get; }
            public Vector3 SaddleBar { get; }
            public Vector3 AthleteCom { get; }
            public SupportRange Support { get; }
            public float MinimumJointHeadroomDeg { get; }
            public string LimitingJointId { get; }
        }

        private readonly struct LoadResult
        {
            public LoadResult(PhaseResult phase, float loadKg, float referenceBarApM, float saddleBarApM,
                float barParityApM, float barParityErrorWorldM, float athleteComApM, float supportMinM,
                float supportMaxM, float combinedComApM, float frontSupportMarginM, float rearSupportMarginM,
                float requiredStaticCopApM)
            {
                Phase = phase; LoadKg = loadKg; ReferenceBarApM = referenceBarApM; SaddleBarApM = saddleBarApM;
                BarParityApM = barParityApM; BarParityErrorWorldM = barParityErrorWorldM; AthleteComApM = athleteComApM;
                SupportMinM = supportMinM; SupportMaxM = supportMaxM; CombinedComApM = combinedComApM;
                FrontSupportMarginM = frontSupportMarginM; RearSupportMarginM = rearSupportMarginM;
                RequiredStaticCopApM = requiredStaticCopApM;
            }
            public PhaseResult Phase { get; }
            public float LoadKg { get; }
            public float ReferenceBarApM { get; }
            public float SaddleBarApM { get; }
            public float BarParityApM { get; }
            public float BarParityErrorWorldM { get; }
            public float AthleteComApM { get; }
            public float SupportMinM { get; }
            public float SupportMaxM { get; }
            public float CombinedComApM { get; }
            public float FrontSupportMarginM { get; }
            public float RearSupportMarginM { get; }
            public float RequiredStaticCopApM { get; }
        }

        private static Quaternion HumerusBodyRotation(
            Vector3 humerus, Vector3 hingeAxis, Vector3 longAxisInBody, Vector3 hingeInBody)
        {
            Quaternion fromBody = Quaternion.LookRotation(
                Vector3.Cross(longAxisInBody, hingeInBody).normalized, longAxisInBody);
            Quaternion toWorld = Quaternion.LookRotation(
                Vector3.Cross(humerus, hingeAxis).normalized, humerus);
            return toWorld * Quaternion.Inverse(fromBody);
        }

        private static Quaternion Canonical(Quaternion rotation) => rotation.w < 0f
            ? new Quaternion(-rotation.x, -rotation.y, -rotation.z, -rotation.w)
            : rotation;
    }
}
