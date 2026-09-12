using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Foundation.Unity;
using PowerliftingSimulator.Squat;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.Tests
{
    /// <summary>
    /// Decides how many rotational freedoms the physical elbow/forearm complex
    /// actually needs to reproduce the accepted GAM-10 bar-support setup.
    ///
    /// H9C fed the physical elbow the full GAM-10 forearm bone quaternion and
    /// found it unreachable. That quaternion is a preview construction, not a
    /// statement about which joint owns which rotation, so this measures the
    /// accepted task-space invariants instead: where the hand sits, where the
    /// elbow sits, and which way the palm faces.
    ///
    /// MODEL_A is the current 1-DOF flexion hinge. MODEL_B adds forearm axial
    /// pronation and nothing else. MODEL_C is a free 3-DOF ball, kept as a
    /// diagnostic error floor and never as a shipping candidate.
    /// </summary>
    public sealed class ElbowForearmDofReconciliationTests
    {
        private const string PhysicalScene = "SquatPhysicalPrototype";
        private const string MeasurementDirectory = "Artifacts/Measurements/GAM-11";

        private static readonly float[] Phases = { 0f, 0.25f, 0.55f, 1f };

        private FoundationBootstrap _bootstrap;
        private PhysicalAthleteRig _rig;
        private SquatPhysicalPrototypeController _controller;
        private SquatReferenceRigCalibration _calibration;

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
                Object.DestroyImmediate(_bootstrap.gameObject);
            _bootstrap = null;
            _rig = null;
            _controller = null;
            _calibration = null;
            yield return null;
            LogAssert.ignoreFailingMessages = false;
        }

        private IEnumerator LoadScene()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(PhysicalScene, LoadSceneMode.Single);
            while (!load.isDone)
                yield return null;
            yield return null;

            _bootstrap = Object.FindFirstObjectByType<FoundationBootstrap>();
            _rig = Object.FindFirstObjectByType<PhysicalAthleteRig>();
            _controller = Object.FindFirstObjectByType<SquatPhysicalPrototypeController>();
            for (int frame = 0; frame < 8 && !_controller.IsInitialized; frame++)
                yield return null;
            Assert.That(_controller.IsInitialized, Is.True, _controller.StartupFailure);
            _bootstrap.enabled = false;

            Animator reference = _rig.ReferenceAnimator;
            _calibration = SquatReferenceRigCalibration.Build(
                reference, reference.transform.root,
                "Assets/Scenes/Prototype/SquatPhysicalPrototype.unity");
        }

        // ---- side wiring -------------------------------------------------

        private readonly struct Side
        {
            public Side(bool isLeft)
            {
                IsLeft = isLeft;
                Prefix = isLeft ? "left" : "right";
            }

            public bool IsLeft { get; }
            public string Prefix { get; }
            public string UpperArm => Prefix + "_upper_arm";
            public string Forearm => Prefix + "_forearm";
            public string Hand => Prefix + "_hand";
        }

        private static readonly Side[] Sides = { new Side(true), new Side(false) };

        private SquatReferenceBoneFrame UpperArmBone(Side side) =>
            side.IsLeft ? _calibration.LeftUpperArm : _calibration.RightUpperArm;

        private SquatReferenceBoneFrame ForearmBone(Side side) =>
            side.IsLeft ? _calibration.LeftForearm : _calibration.RightForearm;

        private SquatReferenceBoneFrame HandBone(Side side) =>
            side.IsLeft ? _calibration.LeftHand : _calibration.RightHand;

        private static SquatReferenceUpperLimbSide Limb(SquatReferenceUpperLimbSolution arms, Side side) =>
            side.IsLeft ? arms.Left : arms.Right;

        private Quaternion ToBody(string segmentId, Quaternion boneRotation) =>
            boneRotation * Quaternion.Inverse(_rig.Segments[segmentId].BodyToReferenceBoneRotation);

        private Quaternion ToBone(string segmentId, Quaternion bodyRotation) =>
            bodyRotation * _rig.Segments[segmentId].BodyToReferenceBoneRotation;

        /// <summary>
        /// Forward kinematics on the physical manifold: the child body
        /// orientation a logical joint target actually produces.
        /// </summary>
        private Quaternion ChildBodyFromLogical(
            string childId, Quaternion parentBodyRotation, Quaternion logical)
        {
            PoweredJointController.PoweredJointRuntime joint = _rig.PoweredController.GetJoint(childId);
            Quaternion relative = joint.NeutralParentToChild *
                (joint.JointSpace * logical * Quaternion.Inverse(joint.JointSpace));
            return parentBodyRotation * relative;
        }

        private Quaternion LogicalFromChildBody(
            string childId, Quaternion parentBodyRotation, Quaternion childBodyRotation)
        {
            PoweredJointController.PoweredJointRuntime joint = _rig.PoweredController.GetJoint(childId);
            Quaternion relative = Quaternion.Inverse(parentBodyRotation) * childBodyRotation;
            Quaternion neutralDelta = Quaternion.Inverse(joint.NeutralParentToChild) * relative;
            return Quaternion.Inverse(joint.JointSpace) * neutralDelta * joint.JointSpace;
        }

        /// <summary>
        /// Elbow-to-wrist offset in the forearm bone's own frame, so a
        /// candidate forearm orientation places the hand bone.
        /// </summary>
        private Vector3 ForearmToHandInBoneFrame(Side side) =>
            Quaternion.Inverse(ForearmBone(side).BindRotation) *
            (HandBone(side).BindPosition - ForearmBone(side).BindPosition);

        /// <summary>
        /// The forearm long axis in joint-space coordinates. Derived from the
        /// bind geometry of the two bones the joint connects, never from a
        /// hard-coded world direction.
        /// </summary>
        private Vector3 PronationAxisInJointSpace(Side side)
        {
            PoweredJointController.PoweredJointRuntime joint = _rig.PoweredController.GetJoint(side.Forearm);
            Quaternion bodyBind = Quaternion.Inverse(_rig.Segments[side.Forearm].BodyToReferenceBoneRotation);
            Vector3 longAxisInBody = (bodyBind * ForearmToHandInBoneFrame(side)).normalized;
            return (Quaternion.Inverse(joint.JointSpace) * longAxisInBody).normalized;
        }

        private static Quaternion Logical(float flexionDeg, float pronationDeg, Vector3 pronationAxis) =>
            Quaternion.AngleAxis(flexionDeg, Vector3.right) *
            Quaternion.AngleAxis(pronationDeg, pronationAxis);

        // ---- accepted task-space authority (section 8) --------------------

        private readonly struct TaskTarget
        {
            public TaskTarget(
                Vector3 shoulder, Vector3 elbow, Vector3 hand,
                Quaternion handRotation, Vector3 palmNormal, Vector3 barCenter,
                Quaternion upperArmBone, Quaternion forearmBone, float palmBarSurfaceErrorM,
                Vector3 anterior)
            {
                Anterior = anterior;
                Shoulder = shoulder;
                Elbow = elbow;
                Hand = hand;
                HandRotation = handRotation;
                PalmNormal = palmNormal;
                BarCenter = barCenter;
                UpperArmBone = upperArmBone;
                ForearmBone = forearmBone;
                PalmBarSurfaceErrorM = palmBarSurfaceErrorM;
            }

            public Vector3 Shoulder { get; }
            public Vector3 Elbow { get; }
            public Vector3 Hand { get; }
            public Quaternion HandRotation { get; }
            public Vector3 PalmNormal { get; }
            public Vector3 BarCenter { get; }
            public Quaternion UpperArmBone { get; }
            public Quaternion ForearmBone { get; }
            public float PalmBarSurfaceErrorM { get; }

            /// <summary>Front of the body, which is the side the hand travels
            /// toward when the elbow flexes.</summary>
            public Vector3 Anterior { get; }
            public Vector3 ForearmDirection => (Hand - Elbow).normalized;
            public Vector3 UpperArmDirection => (Elbow - Shoulder).normalized;
        }

        private TaskTarget Accepted(float phase, SquatPhaseDirection direction, Side side)
        {
            SquatReferenceKinematicSolution solution = SquatReferenceKinematics.Solve(
                _calibration,
                SquatReferenceProfile.CanonicalPowerliftingSquatV1.Evaluate(phase, direction),
                _calibration.LeftFoot.PlantarAnchorWorld,
                _calibration.RightFoot.PlantarAnchorWorld);
            Assert.That(solution.IsValid, Is.True, solution.RejectionReason);

            SquatReferenceUpperLimbSolution arms = SquatReferenceUpperLimb.Solve(_calibration, solution);
            SquatReferenceUpperLimbSide limb = Limb(arms, side);
            Vector3 palmNormal = limb.HandBoneRotation * (side.IsLeft ? Vector3.left : Vector3.right);
            return new TaskTarget(
                limb.ShoulderCenter, limb.ElbowCenter, limb.HandCenter,
                limb.HandBoneRotation, palmNormal, arms.BarCenter,
                limb.UpperArmBoneRotation, limb.ForearmBoneRotation, limb.PalmBarSurfaceErrorM,
                solution.UpperChestFrameRotation * _calibration.GameForward);
        }

        // ---- candidate evaluation ----------------------------------------

        private readonly struct Candidate
        {
            public Candidate(
                float flexionDeg, float pronationDeg,
                float handPositionErrorM, float forearmDirectionErrorDeg,
                float handOrientationErrorDeg, float palmNormalErrorDeg,
                float wristResidualFlexionDeg, float wristResidualSecondaryDeg)
            {
                FlexionDeg = flexionDeg;
                PronationDeg = pronationDeg;
                HandPositionErrorM = handPositionErrorM;
                ForearmDirectionErrorDeg = forearmDirectionErrorDeg;
                HandOrientationErrorDeg = handOrientationErrorDeg;
                PalmNormalErrorDeg = palmNormalErrorDeg;
                WristResidualFlexionDeg = wristResidualFlexionDeg;
                WristResidualSecondaryDeg = wristResidualSecondaryDeg;
            }

            public float FlexionDeg { get; }
            public float PronationDeg { get; }
            public float HandPositionErrorM { get; }
            public float ForearmDirectionErrorDeg { get; }
            public float HandOrientationErrorDeg { get; }
            public float PalmNormalErrorDeg { get; }
            public float WristResidualFlexionDeg { get; }
            public float WristResidualSecondaryDeg { get; }
        }

        /// <summary>
        /// Places the forearm on the candidate joint manifold and measures the
        /// accepted task-space invariants against it. The wrist absorbs the
        /// orientation residual; how much it is asked to absorb is reported so
        /// it can be read against its own limits.
        /// </summary>
        private Candidate Evaluate(
            Side side, TaskTarget accepted, float flexionDeg, float pronationDeg, Vector3 pronationAxis,
            Quaternion? upperArmBodyOverride = null)
        {
            Quaternion upperArmBody = upperArmBodyOverride ?? ToBody(side.UpperArm, accepted.UpperArmBone);
            Quaternion forearmBody = ChildBodyFromLogical(
                side.Forearm, upperArmBody, Logical(flexionDeg, pronationDeg, pronationAxis));
            Quaternion forearmBone = ToBone(side.Forearm, forearmBody);

            Vector3 hand = accepted.Elbow + forearmBone * ForearmToHandInBoneFrame(side);
            Vector3 forearmDirection = (hand - accepted.Elbow).normalized;

            // The wrist can rotate the hand but cannot move the hand bone, so
            // orientation error is what the wrist would have to absorb.
            Quaternion desiredHandBody = ToBody(side.Hand, accepted.HandRotation);
            Quaternion wristLogical = LogicalFromChildBody(side.Hand, forearmBody, desiredHandBody);
            Vector3 wristDeg = AxisAngleDegrees(wristLogical);

            return new Candidate(
                flexionDeg, pronationDeg,
                Vector3.Distance(hand, accepted.Hand),
                Vector3.Angle(forearmDirection, accepted.ForearmDirection),
                Quaternion.Angle(forearmBone, accepted.ForearmBone),
                Vector3.Angle(
                    forearmBone * Quaternion.Inverse(accepted.ForearmBone) * accepted.PalmNormal,
                    accepted.PalmNormal),
                wristDeg.x,
                new Vector2(wristDeg.y, wristDeg.z).magnitude);
        }

        private static Vector3 AxisAngleDegrees(Quaternion rotation)
        {
            if (rotation.w < 0f)
                rotation = new Quaternion(-rotation.x, -rotation.y, -rotation.z, -rotation.w);
            var axis = new Vector3(rotation.x, rotation.y, rotation.z);
            float magnitude = axis.magnitude;
            if (magnitude <= 1e-7f)
                return Vector3.zero;
            float angleDeg = 2f * Mathf.Atan2(magnitude, Mathf.Clamp(rotation.w, -1f, 1f)) * Mathf.Rad2Deg;
            return axis * (angleDeg / magnitude);
        }

        private Candidate BestCandidate(
            Side side, TaskTarget accepted, bool allowPronation, Vector3 pronationAxis)
        {
            PhysicalJointRecipe recipe = Recipe(side.Forearm);
            Candidate best = default;
            bool haveBest = false;

            // Coarse sweep then two refinement passes. The objective is hand
            // placement first and hand orientation second, because the hand at
            // the bar is the accepted invariant and the palm follows it.
            float flexionLow = recipe.LowDegrees;
            float flexionHigh = recipe.HighDegrees;
            float pronationLow = allowPronation ? -PronationSweepDeg : 0f;
            float pronationHigh = allowPronation ? PronationSweepDeg : 0f;
            float flexionStep = 1f;
            float pronationStep = allowPronation ? 2f : 1f;

            for (int pass = 0; pass < 3; pass++)
            {
                for (float flexion = flexionLow; flexion <= flexionHigh + 1e-4f; flexion += flexionStep)
                {
                    for (float pronation = pronationLow; pronation <= pronationHigh + 1e-4f; pronation += pronationStep)
                    {
                        Candidate candidate = Evaluate(side, accepted, flexion, pronation, pronationAxis);
                        if (!haveBest || Cost(candidate) < Cost(best))
                        {
                            best = candidate;
                            haveBest = true;
                        }
                    }
                }
                flexionLow = Mathf.Max(recipe.LowDegrees, best.FlexionDeg - flexionStep);
                flexionHigh = Mathf.Min(recipe.HighDegrees, best.FlexionDeg + flexionStep);
                flexionStep *= 0.1f;
                if (allowPronation)
                {
                    pronationLow = Mathf.Max(-PronationSweepDeg, best.PronationDeg - pronationStep);
                    pronationHigh = Mathf.Min(PronationSweepDeg, best.PronationDeg + pronationStep);
                    pronationStep *= 0.1f;
                }
            }
            return best;
        }

        /// <summary>
        /// Hand placement in metres dominates; hand orientation in degrees is
        /// worth a millimetre per degree, so a model cannot buy position with
        /// a grossly wrong palm.
        /// </summary>
        private static float Cost(Candidate candidate) =>
            candidate.HandPositionErrorM + candidate.HandOrientationErrorDeg * 0.001f;

        private const float PronationSweepDeg = 90f;

        private static PhysicalJointRecipe Recipe(string childId)
        {
            foreach (PhysicalJointRecipe recipe in PhysicalAthleteDefinition.Joints)
                if (recipe.ChildId == childId)
                    return recipe;
            throw new AssertionException($"No joint recipe for '{childId}'.");
        }

        // ---- section 7: is the hinge frame itself correct? ----------------

        [UnityTest]
        public IEnumerator ELBOW_HINGE_SIGN_PARITY()
        {
            yield return LoadScene();

            var report = new StringBuilder();
            report.AppendLine("side,command_deg,included_angle_deg,anterior_swing_deg,anatomical_sense");
            var flexionSign = new System.Collections.Generic.Dictionary<string, int>();

            foreach (Side side in Sides)
            {
                TaskTarget accepted = Accepted(0f, SquatPhaseDirection.Descent, side);
                Quaternion upperArmBody = ToBody(side.UpperArm, accepted.UpperArmBone);
                Vector3 pronationAxis = PronationAxisInJointSpace(side);
                foreach (float command in new[] { -10f, -5f, 0f, 5f, 10f })
                {
                    Vector3 forearm = ForearmDirection(side, upperArmBody, command, pronationAxis);
                    float included = Vector3.Angle(accepted.UpperArmDirection, forearm);

                    // Elbow flexion carries the hand to the front of the body.
                    // Measured perpendicular to the humerus, so the humerus's
                    // own inclination cannot be mistaken for elbow travel.
                    Vector3 swing = Vector3.ProjectOnPlane(forearm, accepted.UpperArmDirection);
                    float anteriorSwing = Vector3.Dot(swing, accepted.Anterior) * Mathf.Rad2Deg;
                    string sense = Mathf.Abs(anteriorSwing) < 0.05f ? "NEUTRAL"
                        : anteriorSwing > 0f ? "FLEXION" : "EXTENSION";
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0},{1:F1},{2:F4},{3:F4},{4}", side.Prefix, command, included, anteriorSwing, sense));
                    if (Mathf.Approximately(command, 10f))
                        flexionSign[side.Prefix] = anteriorSwing > 0f ? 1 : -1;
                }
            }

            WriteMeasurement("GAM11-elbow-hinge-sign-parity.csv", report.ToString());
            Debug.Log($"[ELBOW SIGN] left flexion sign {flexionSign["left"]:+0;-0}, " +
                      $"right flexion sign {flexionSign["right"]:+0;-0}");

            PhysicalJointRecipe elbow = Recipe("left_forearm");
            foreach (Side side in Sides)
            {
                Assert.That(flexionSign[side.Prefix], Is.EqualTo(1),
                    $"A positive {side.Forearm} command extends instead of flexing, so the " +
                    $"anatomical flexion range lies outside the authored " +
                    $"[{elbow.LowDegrees}, {elbow.HighDegrees}] limit.");
            }
            Assert.That(flexionSign["left"], Is.EqualTo(flexionSign["right"]),
                "The elbows disagree about which sign is flexion, so the hinge frames are not " +
                "anatomical mirrors of each other.");
            yield return null;
        }

        private Vector3 ForearmDirection(
            Side side, Quaternion upperArmBody, float flexionDeg, Vector3 pronationAxis)
        {
            Quaternion forearmBody = ChildBodyFromLogical(
                side.Forearm, upperArmBody, Logical(flexionDeg, 0f, pronationAxis));
            return (ToBone(side.Forearm, forearmBody) * ForearmToHandInBoneFrame(side)).normalized;
        }

        // ---- sections 9-17: model comparison ------------------------------

        [UnityTest]
        public IEnumerator ELBOW_FOREARM_REDUCED_MODEL_FEASIBILITY()
        {
            yield return LoadScene();

            var report = new StringBuilder();
            report.AppendLine("model,side,phase,direction,flexion_deg,pronation_deg," +
                              "flexion_limit_margin_deg,hand_position_error_m,elbow_position_error_m," +
                              "forearm_direction_error_deg,hand_orientation_error_deg,palm_normal_error_deg," +
                              "wrist_residual_flexion_deg,wrist_residual_secondary_deg," +
                              "wrist_flexion_margin_deg,wrist_secondary_margin_deg");

            PhysicalJointRecipe elbow = Recipe("left_forearm");
            PhysicalJointRecipe wrist = Recipe("left_hand");
            var worst = new System.Collections.Generic.Dictionary<string, Candidate>();

            foreach (string model in new[] { "MODEL_A", "MODEL_B", "MODEL_C" })
            {
                foreach (Side side in Sides)
                {
                    Vector3 pronationAxis = PronationAxisInJointSpace(side);
                    foreach (SquatPhaseDirection direction in
                        new[] { SquatPhaseDirection.Descent, SquatPhaseDirection.Ascent })
                    {
                        foreach (float phase in Phases)
                        {
                            TaskTarget accepted = Accepted(phase, direction, side);
                            Candidate candidate = model switch
                            {
                                "MODEL_A" => BestCandidate(side, accepted, allowPronation: false, pronationAxis),
                                "MODEL_B" => BestCandidate(side, accepted, allowPronation: true, pronationAxis),
                                _ => FreeBallCandidate(side, accepted)
                            };

                            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                                "{0},{1},{2:F2},{3},{4:F3},{5:F3},{6:F3},{7:F5},{8:F5},{9:F3},{10:F3},{11:F3}," +
                                "{12:F3},{13:F3},{14:F3},{15:F3}",
                                model, side.Prefix, phase, direction,
                                candidate.FlexionDeg, candidate.PronationDeg,
                                Mathf.Min(candidate.FlexionDeg - elbow.LowDegrees,
                                          elbow.HighDegrees - candidate.FlexionDeg),
                                candidate.HandPositionErrorM, 0f,
                                candidate.ForearmDirectionErrorDeg,
                                candidate.HandOrientationErrorDeg,
                                candidate.PalmNormalErrorDeg,
                                candidate.WristResidualFlexionDeg,
                                candidate.WristResidualSecondaryDeg,
                                wrist.HighDegrees - Mathf.Abs(candidate.WristResidualFlexionDeg),
                                wrist.SecondaryLimitDegrees - Mathf.Abs(candidate.WristResidualSecondaryDeg)));

                            if (!worst.TryGetValue(model, out Candidate current) ||
                                Cost(candidate) > Cost(current))
                                worst[model] = candidate;
                        }
                    }
                }
            }

            WriteMeasurement("GAM11-elbow-forearm-model-feasibility.csv", report.ToString());
            foreach (string model in new[] { "MODEL_A", "MODEL_B", "MODEL_C" })
            {
                Candidate w = worst[model];
                Debug.Log($"[{model}] worst: hand {w.HandPositionErrorM * 1000f:F2} mm, " +
                          $"forearm dir {w.ForearmDirectionErrorDeg:F2} deg, " +
                          $"hand orient {w.HandOrientationErrorDeg:F2} deg, " +
                          $"palm {w.PalmNormalErrorDeg:F2} deg, " +
                          $"flexion {w.FlexionDeg:F2} deg, pronation {w.PronationDeg:F2} deg, " +
                          $"wrist residual {w.WristResidualFlexionDeg:F2}/{w.WristResidualSecondaryDeg:F2} deg");
            }
            yield return null;
        }

        /// <summary>
        /// MODEL_C: every relative rotation allowed. Diagnostic error floor
        /// only; a free ball elbow is not a shipping candidate.
        /// </summary>
        private Candidate FreeBallCandidate(Side side, TaskTarget accepted)
        {
            Quaternion upperArmBody = ToBody(side.UpperArm, accepted.UpperArmBone);
            Quaternion forearmBody = ToBody(side.Forearm, accepted.ForearmBone);
            Vector3 logicalDeg = AxisAngleDegrees(
                LogicalFromChildBody(side.Forearm, upperArmBody, forearmBody));

            Quaternion forearmBone = ToBone(side.Forearm, forearmBody);
            Vector3 hand = accepted.Elbow + forearmBone * ForearmToHandInBoneFrame(side);
            Quaternion desiredHandBody = ToBody(side.Hand, accepted.HandRotation);
            Vector3 wristDeg = AxisAngleDegrees(
                LogicalFromChildBody(side.Hand, forearmBody, desiredHandBody));
            return new Candidate(
                logicalDeg.x, new Vector2(logicalDeg.y, logicalDeg.z).magnitude,
                Vector3.Distance(hand, accepted.Hand),
                Vector3.Angle((hand - accepted.Elbow).normalized, accepted.ForearmDirection),
                Quaternion.Angle(forearmBone, accepted.ForearmBone),
                0f, wristDeg.x, new Vector2(wristDeg.y, wristDeg.z).magnitude);
        }

        /// <summary>
        /// Rebuilds the joint-space basis the rig would derive for a candidate
        /// world hinge axis, so an axis can be judged before production is
        /// changed to carry it.
        /// </summary>
        private Quaternion JointSpaceForAxis(Side side, Vector3 axisWorld)
        {
            Quaternion bodyBind = _rig.Segments[side.Forearm].Body.rotation;
            Vector3 right = (Quaternion.Inverse(bodyBind) * axisWorld.normalized).normalized;
            Vector3 secondaryWorld = Mathf.Abs(Vector3.Dot(axisWorld.normalized, Vector3.up)) < 0.9f
                ? Vector3.up
                : Vector3.forward;
            Vector3 secondary = Quaternion.Inverse(bodyBind) * secondaryWorld;
            Vector3 forward = Vector3.Cross(right, secondary).normalized;
            Vector3 up = Vector3.Cross(forward, right).normalized;
            return Quaternion.LookRotation(forward, up);
        }

        /// <summary>
        /// How much anterior hand travel one degree of hinge command buys at
        /// the accepted setup pose, and how much of the travel is wasted on
        /// the sideways swing an elbow does not have.
        /// </summary>
        [UnityTest]
        public IEnumerator ELBOW_HINGE_AXIS_CANDIDATE_SWEEP()
        {
            yield return LoadScene();

            var candidates = new System.Collections.Generic.List<(string Name, Vector3 Left, Vector3 Right)>
            {
                ("production_forward", Vector3.forward, Vector3.forward),
                ("world_up", Vector3.up, Vector3.up),
                ("world_up_mirrored", Vector3.up, Vector3.down),
                ("world_right", Vector3.right, Vector3.right),
                ("bind_derived", Vector3.zero, Vector3.zero),
                ("bind_derived_negated", Vector3.zero, Vector3.zero)
            };

            var report = new StringBuilder();
            report.AppendLine("candidate,side,axis_x,axis_y,axis_z,anterior_swing_per_deg," +
                              "lateral_swing_per_deg,flexion_fraction");

            foreach ((string name, Vector3 left, Vector3 right) in candidates)
            {
                foreach (Side side in Sides)
                {
                    Vector3 axis = side.IsLeft ? left : right;
                    if (name.StartsWith("bind_derived"))
                    {
                        axis = BindDerivedFlexionAxis(side);
                        if (name.EndsWith("negated"))
                            axis = -axis;
                    }

                    TaskTarget accepted = Accepted(0f, SquatPhaseDirection.Descent, side);
                    Quaternion upperArmBody = ToBody(side.UpperArm, accepted.UpperArmBone);
                    Quaternion jointSpace = JointSpaceForAxis(side, axis);

                    Vector3 atZero = ForearmDirectionForFrame(side, upperArmBody, jointSpace, 0f);
                    Vector3 atTen = ForearmDirectionForFrame(side, upperArmBody, jointSpace, 10f);
                    Vector3 lateral = Vector3.Cross(accepted.UpperArmDirection, accepted.Anterior).normalized;
                    Vector3 travel = atTen - atZero;
                    float anterior = Vector3.Dot(travel, accepted.Anterior) / 10f;
                    float side1 = Vector3.Dot(travel, lateral) / 10f;

                    report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0},{1},{2:F3},{3:F3},{4:F3},{5:F5},{6:F5},{7:F3}",
                        name, side.Prefix, axis.x, axis.y, axis.z, anterior, side1,
                        Mathf.Abs(anterior) / Mathf.Max(1e-6f, Mathf.Abs(anterior) + Mathf.Abs(side1))));
                    Debug.Log($"[AXIS SWEEP] {name} {side.Prefix}: anterior/deg={anterior:F5} " +
                              $"lateral/deg={side1:F5}");
                }
            }

            WriteMeasurement("GAM11-elbow-hinge-axis-sweep.csv", report.ToString());
            yield return null;
        }

        /// <summary>
        /// The anatomical elbow flexion axis at bind: perpendicular to both the
        /// humerus long axis and the anterior direction the hand must travel
        /// toward. It mirrors between sides on its own, because the humerus
        /// points the opposite way.
        /// </summary>
        private Vector3 BindDerivedFlexionAxis(Side side)
        {
            Vector3 humerus = (ForearmBone(side).BindPosition - UpperArmBone(side).BindPosition).normalized;
            return Vector3.Cross(_calibration.GameForward, humerus).normalized;
        }

        private Vector3 ForearmDirectionForFrame(
            Side side, Quaternion upperArmBody, Quaternion jointSpace, float flexionDeg)
        {
            PoweredJointController.PoweredJointRuntime joint = _rig.PoweredController.GetJoint(side.Forearm);
            Quaternion logical = Quaternion.AngleAxis(flexionDeg, Vector3.right);
            Quaternion relative = joint.NeutralParentToChild *
                (jointSpace * logical * Quaternion.Inverse(jointSpace));
            Quaternion forearmBody = upperArmBody * relative;
            return (ToBone(side.Forearm, forearmBody) * ForearmToHandInBoneFrame(side)).normalized;
        }

        /// <summary>
        /// The hypothesis the model sweep points at: the accepted hand, elbow
        /// and shoulder positions come from the preview's own two-bone solve,
        /// so they are reachable by a shoulder plus a one-axis elbow by
        /// construction. What is unreachable is the preview's separate choice
        /// of humerus roll and its rule that the forearm orientation follows
        /// the hand. Those put a swing into the elbow that no hinge can make.
        ///
        /// This measures what the chain can do when the humerus roll is chosen
        /// to align the elbow's own hinge axis with the arm plane, which is
        /// what a real shoulder does, instead of being fixed for appearance.
        /// </summary>
        [UnityTest]
        public IEnumerator ELBOW_REACHABLE_WHEN_HUMERUS_ROLL_ALIGNS_THE_HINGE()
        {
            yield return LoadScene();

            var report = new StringBuilder();
            report.AppendLine("side,phase,direction,flexion_deg,flexion_limit_margin_deg," +
                              "hand_position_error_m,forearm_direction_error_deg,shoulder_roll_change_deg");
            PhysicalJointRecipe elbow = Recipe("left_forearm");
            float worstHand = 0f;
            float worstDirection = 0f;
            float worstFlexion = 0f;
            float worstMargin = float.PositiveInfinity;

            foreach (Side side in Sides)
            {
                foreach (float phase in Phases)
                {
                    TaskTarget accepted = Accepted(phase, SquatPhaseDirection.Descent, side);
                    Vector3 humerus = accepted.UpperArmDirection;
                    Vector3 forearm = accepted.ForearmDirection;
                    Vector3 planeNormal = Vector3.Cross(humerus, forearm).normalized;

                    // Orient the humerus so it points along the accepted upper
                    // arm and its elbow hinge axis is the arm-plane normal.
                    Quaternion upperArmBody = HumerusBodyRotation(side, humerus, planeNormal);
                    Vector3 pronationAxis = PronationAxisInJointSpace(side);

                    // The included angle is unsigned; which sign of the hinge
                    // coordinate reaches it is a property of the frame, so try
                    // both and keep the one that places the hand.
                    float magnitude = Vector3.Angle(humerus, forearm);
                    Candidate positive = Evaluate(side, accepted, magnitude, 0f, pronationAxis, upperArmBody);
                    Candidate negative = Evaluate(side, accepted, -magnitude, 0f, pronationAxis, upperArmBody);
                    Candidate candidate = positive.HandPositionErrorM <= negative.HandPositionErrorM
                        ? positive
                        : negative;
                    float flexion = candidate.FlexionDeg;
                    float margin = Mathf.Min(flexion - elbow.LowDegrees, elbow.HighDegrees - flexion);
                    float rollChange = Quaternion.Angle(
                        upperArmBody, ToBody(side.UpperArm, accepted.UpperArmBone));

                    report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0},{1:F2},Descent,{2:F3},{3:F3},{4:F5},{5:F3},{6:F3}",
                        side.Prefix, phase, flexion, margin,
                        candidate.HandPositionErrorM, candidate.ForearmDirectionErrorDeg, rollChange));

                    worstHand = Mathf.Max(worstHand, candidate.HandPositionErrorM);
                    worstDirection = Mathf.Max(worstDirection, candidate.ForearmDirectionErrorDeg);
                    worstFlexion = Mathf.Max(worstFlexion, flexion);
                    worstMargin = Mathf.Min(worstMargin, margin);
                }
            }

            WriteMeasurement("GAM11-elbow-humerus-roll-alignment.csv", report.ToString());
            Debug.Log($"[ROLL ALIGNED] worst hand {worstHand * 1000f:F2} mm, " +
                      $"forearm dir {worstDirection:F3} deg, max flexion {worstFlexion:F2} deg, " +
                      $"min limit margin {worstMargin:F2} deg");
            yield return null;
        }

        /// <summary>
        /// The upper arm body orientation that points the humerus along
        /// <paramref name="humerus"/> and puts its elbow hinge axis on
        /// <paramref name="hingeAxis"/>.
        /// </summary>
        private Quaternion HumerusBodyRotation(Side side, Vector3 humerus, Vector3 hingeAxis)
        {
            PoweredJointController.PoweredJointRuntime joint = _rig.PoweredController.GetJoint(side.Forearm);

            // A world vector reaches body coordinates through the segment's own
            // body-to-bone calibration, so the humerus long axis and the elbow
            // hinge axis are both expressed in the upper arm's frame.
            Vector3 longAxisInBone = Quaternion.Inverse(UpperArmBone(side).BindRotation) *
                (ForearmBone(side).BindPosition - UpperArmBone(side).BindPosition);
            Vector3 longAxisInBody =
                (_rig.Segments[side.UpperArm].BodyToReferenceBoneRotation * longAxisInBone).normalized;
            Vector3 hingeInBody = (joint.NeutralParentToChild * joint.Joint.axis.normalized).normalized;

            Quaternion fromBody = Quaternion.LookRotation(
                Vector3.Cross(longAxisInBody, hingeInBody).normalized, longAxisInBody);
            Quaternion toWorld = Quaternion.LookRotation(
                Vector3.Cross(humerus, hingeAxis).normalized, humerus);
            return toWorld * Quaternion.Inverse(fromBody);
        }

        private static void WriteMeasurement(string filename, string content)
        {
            Directory.CreateDirectory(Path.GetFullPath(MeasurementDirectory));
            File.WriteAllText(Path.Combine(Path.GetFullPath(MeasurementDirectory), filename), content);
        }
    }
}
