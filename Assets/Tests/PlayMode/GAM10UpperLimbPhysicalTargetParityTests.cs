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
    /// Pins the physical upper limbs to the accepted GAM-10 bar-support
    /// reference.
    ///
    /// The physical adapter used to command Quaternion.identity to every arm
    /// joint on every tick, which put the physical athlete in a T-pose while
    /// the accepted reference held the bar. Nothing caught it because no test
    /// ever compared an arm target against the reference. These do.
    /// </summary>
    public sealed class GAM10UpperLimbPhysicalTargetParityTests
    {
        private const string PhysicalScene = "SquatPhysicalPrototype";
        private const string MeasurementPath =
            "Artifacts/Measurements/GAM11-upper-limb-reference-parity.csv";

        private static readonly float[] QualificationPhases = { 0f, 0.25f, 0.55f, 1f };

        private static readonly string[] UpperLimbJointIds =
        {
            "left_upper_arm", "right_upper_arm",
            "left_forearm", "right_forearm",
            "left_hand", "right_hand"
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
                Object.DestroyImmediate(_bootstrap.gameObject);
            _bootstrap = null;
            _rig = null;
            _controller = null;
            yield return null;
            LogAssert.ignoreFailingMessages = false;
        }

        private IEnumerator LoadPhysicalScene()
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
        }

        private SquatReferenceRigCalibration BuildCalibration()
        {
            Animator reference = _rig.ReferenceAnimator;
            Assert.That(reference, Is.Not.Null, "The physical rig has no canonical reference animator.");
            return SquatReferenceRigCalibration.Build(
                reference,
                reference.transform.root,
                "Assets/Scenes/Prototype/SquatPhysicalPrototype.unity");
        }

        /// <summary>
        /// Maps a desired reference bone rotation onto the physical body it
        /// drives, exactly as the adapter's calibrated mapping does.
        /// </summary>
        private Quaternion PhysicalBodyRotation(string segmentId, Quaternion referenceBoneRotation) =>
            referenceBoneRotation * Quaternion.Inverse(_rig.Segments[segmentId].BodyToReferenceBoneRotation);

        private Quaternion LogicalJointTarget(
            string childId,
            Quaternion parentBodyRotation,
            Quaternion childBodyRotation)
        {
            PoweredJointController.PoweredJointRuntime joint = _rig.PoweredController.GetJoint(childId);
            Quaternion relative = Quaternion.Inverse(parentBodyRotation) * childBodyRotation;
            Quaternion neutralDelta = Quaternion.Inverse(joint.NeutralParentToChild) * relative;
            return Quaternion.Inverse(joint.JointSpace) * neutralDelta * joint.JointSpace;
        }

        /// <summary>
        /// The six accepted upper-limb logical targets at one reference phase,
        /// derived from the GAM-10 task-space authority through the physical
        /// projection rather than read back from the adapter. Ordered to match
        /// <see cref="UpperLimbJointIds"/>.
        /// </summary>
        private Quaternion[] ExpectedUpperLimbTargets(
            SquatReferenceRigCalibration calibration,
            float phase,
            SquatPhaseDirection direction)
        {
            SquatReferenceProfile profile = SquatReferenceProfile.CanonicalPowerliftingSquatV1;
            SquatReferenceKinematicSolution solution = SquatReferenceKinematics.Solve(
                calibration,
                profile.Evaluate(phase, direction),
                calibration.LeftFoot.PlantarAnchorWorld,
                calibration.RightFoot.PlantarAnchorWorld);
            Assert.That(solution.IsValid, Is.True, solution.RejectionReason);

            SquatReferenceUpperLimbSolution arms = SquatReferenceUpperLimb.Solve(calibration, solution);
            Quaternion thorax = PhysicalBodyRotation(
                "thorax",
                solution.ChestFrameRotation * calibration.Chest.BoneFromAnatomicalFrame);
            PhysicalUpperLimbTargets left = SquatPhysicalUpperLimbProjection.Project(
                _rig, calibration, arms.Left, thorax, isLeft: true);
            PhysicalUpperLimbTargets right = SquatPhysicalUpperLimbProjection.Project(
                _rig, calibration, arms.Right, thorax, isLeft: false);

            return new[]
            {
                left.UpperArm, right.UpperArm,
                left.Forearm, right.Forearm,
                left.Hand, right.Hand
            };
        }

        /// <summary>
        /// Splits a logical joint target into its calibrated joint-space
        /// components, so a target can be read against the anatomical limits
        /// the joint actually carries instead of as one opaque angle.
        /// </summary>
        private static Vector3 JointSpaceDegrees(Quaternion target)
        {
            if (target.w < 0f)
                target = new Quaternion(-target.x, -target.y, -target.z, -target.w);
            var axis = new Vector3(target.x, target.y, target.z);
            float magnitude = axis.magnitude;
            if (magnitude <= 1e-6f)
                return Vector3.zero;
            float angleDeg = 2f * Mathf.Atan2(magnitude, Mathf.Clamp(target.w, -1f, 1f)) * Mathf.Rad2Deg;
            return axis * (angleDeg / magnitude);
        }

        private static PhysicalJointRecipe RecipeFor(string childId)
        {
            foreach (PhysicalJointRecipe recipe in PhysicalAthleteDefinition.Joints)
            {
                if (recipe.ChildId == childId)
                    return recipe;
            }
            throw new AssertionException($"No joint recipe for '{childId}'.");
        }

        [UnityTest]
        public IEnumerator GAM10_UPPER_LIMB_PHYSICAL_TARGET_PARITY()
        {
            yield return LoadPhysicalScene();
            SquatReferenceRigCalibration calibration = BuildCalibration();
            SquatPhysicalAdapter adapter = _controller.Adapter;

            var report = new StringBuilder();
            report.AppendLine("phase,direction,joint,expected_deg,actual_deg,parity_error_deg," +
                              "flexion_deg,abduction_deg,axial_deg,limit_low_deg,limit_high_deg,limit_secondary_deg");
            float worstErrorDeg = 0f;

            foreach (SquatPhaseDirection direction in
                new[] { SquatPhaseDirection.Descent, SquatPhaseDirection.Ascent })
            {
                foreach (float phase in QualificationPhases)
                {
                    Quaternion[] expected = ExpectedUpperLimbTargets(calibration, phase, direction);
                    for (int index = 0; index < UpperLimbJointIds.Length; index++)
                    {
                        string jointId = UpperLimbJointIds[index];
                        Quaternion actual = adapter.ReferenceLogicalTarget(jointId, phase, direction);
                        float errorDeg = Quaternion.Angle(expected[index], actual);
                        worstErrorDeg = Mathf.Max(worstErrorDeg, errorDeg);
                        Vector3 jointSpaceDeg = JointSpaceDegrees(actual);
                        PhysicalJointRecipe recipe = RecipeFor(jointId);
                        report.AppendLine(string.Format(
                            CultureInfo.InvariantCulture,
                            "{0:F2},{1},{2},{3:F4},{4:F4},{5:F6},{6:F3},{7:F3},{8:F3},{9:F1},{10:F1},{11:F1}",
                            phase, direction, jointId,
                            Quaternion.Angle(Quaternion.identity, expected[index]),
                            Quaternion.Angle(Quaternion.identity, actual),
                            errorDeg,
                            jointSpaceDeg.x, jointSpaceDeg.y, jointSpaceDeg.z,
                            recipe.LowDegrees, recipe.HighDegrees, recipe.SecondaryLimitDegrees));

                        Assert.That(errorDeg, Is.LessThanOrEqualTo(SquatReferenceRigCalibration.JointAxisToleranceDeg),
                            $"{jointId} at s_q={phase:F2} {direction} is {errorDeg:F4} deg from the accepted " +
                            "GAM-10 reference target. The physical adapter is not carrying the reference.");
                    }
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(MeasurementPath)));
            File.WriteAllText(Path.GetFullPath(MeasurementPath), report.ToString());
            Debug.Log($"[UPPER LIMB PARITY] worst parity error {worstErrorDeg:F6} deg across " +
                      $"{QualificationPhases.Length * 2 * UpperLimbJointIds.Length} joint samples; " +
                      $"wrote {MeasurementPath}");
            yield return null;
        }

        /// <summary>
        /// The regression that would have caught the T-pose. At the canonical
        /// setup pose the accepted reference does not hold the arm chain at its
        /// neutral, so a physical target that is identity across the whole
        /// chain means the reference is not reaching the arms.
        ///
        /// The reference is the authority for which joints must be off-neutral:
        /// a joint whose accepted target genuinely is identity is not asserted
        /// against.
        /// </summary>
        [UnityTest]
        public IEnumerator PHYSICAL_SETUP_MUST_NOT_FALL_BACK_TO_IDENTITY_ARM_TARGETS()
        {
            yield return LoadPhysicalScene();
            SquatReferenceRigCalibration calibration = BuildCalibration();
            SquatPhysicalAdapter adapter = _controller.Adapter;

            const float identityToleranceDeg = 1f;
            Quaternion[] expected = ExpectedUpperLimbTargets(calibration, 0f, SquatPhaseDirection.Descent);
            int offNeutralJoints = 0;

            for (int index = 0; index < UpperLimbJointIds.Length; index++)
            {
                string jointId = UpperLimbJointIds[index];
                float referenceOffNeutralDeg = Quaternion.Angle(Quaternion.identity, expected[index]);
                if (referenceOffNeutralDeg <= identityToleranceDeg)
                    continue;

                offNeutralJoints++;
                float actualOffNeutralDeg = Quaternion.Angle(
                    Quaternion.identity,
                    adapter.ReferenceLogicalTarget(jointId, 0f, SquatPhaseDirection.Descent));
                Assert.That(actualOffNeutralDeg, Is.GreaterThan(identityToleranceDeg),
                    $"{jointId} is commanded at its neutral identity while the accepted GAM-10 setup " +
                    $"puts it {referenceOffNeutralDeg:F2} deg off neutral. The arm chain has fallen " +
                    "back to a T-pose.");
            }

            Assert.That(offNeutralJoints, Is.GreaterThan(0),
                "The accepted GAM-10 setup left every upper-limb joint at neutral, so this regression " +
                "cannot detect a T-pose. The reference authority has changed.");
            Debug.Log($"[ANTI T-POSE] {offNeutralJoints} of {UpperLimbJointIds.Length} upper-limb joints " +
                      "carry an off-neutral accepted setup target and all are commanded off neutral.");
            yield return null;
        }

        /// <summary>
        /// Left and right are not required to hold equal raw quaternions:
        /// their joint frames differ. Each side must match its own reference,
        /// and the two must be off-neutral by the same amount, which is what
        /// an anatomically mirrored bilateral grip means.
        /// </summary>
        [UnityTest]
        public IEnumerator UPPER_LIMB_ANATOMICAL_MIRROR_PARITY()
        {
            yield return LoadPhysicalScene();
            SquatReferenceRigCalibration calibration = BuildCalibration();
            SquatPhysicalAdapter adapter = _controller.Adapter;

            const float mirrorToleranceDeg = 2f;
            foreach (float phase in QualificationPhases)
            {
                for (int index = 0; index < UpperLimbJointIds.Length; index += 2)
                {
                    float leftDeg = Quaternion.Angle(
                        Quaternion.identity,
                        adapter.ReferenceLogicalTarget(UpperLimbJointIds[index], phase, SquatPhaseDirection.Descent));
                    float rightDeg = Quaternion.Angle(
                        Quaternion.identity,
                        adapter.ReferenceLogicalTarget(UpperLimbJointIds[index + 1], phase, SquatPhaseDirection.Descent));
                    Assert.That(Mathf.Abs(leftDeg - rightDeg), Is.LessThanOrEqualTo(mirrorToleranceDeg),
                        $"{UpperLimbJointIds[index]} ({leftDeg:F2} deg) and " +
                        $"{UpperLimbJointIds[index + 1]} ({rightDeg:F2} deg) are not a mirrored pair " +
                        $"at s_q={phase:F2}.");
                }
            }
            yield return null;
        }
    }
}
