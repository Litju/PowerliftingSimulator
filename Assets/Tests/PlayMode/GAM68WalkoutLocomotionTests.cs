using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Equipment;
using PowerliftingSimulator.Foundation.Unity;
using PowerliftingSimulator.Squat;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.Tests
{
    /// <summary>
    /// GAM-68 authored-walkout locomotion contract.
    ///
    /// The old walkout translated every athlete body by the animation root
    /// delta and bent one knee, so the "planted" foot slid across the platform
    /// with the root. This fixture measures the actual foot colliders and
    /// rejects that behaviour instead of accepting a distance-only walkout:
    ///
    ///  * a foot under authored support stays fixed in world space;
    ///  * the swing foot leaves the platform, travels to its authored landing
    ///    target and returns to the support plane with the accepted sole
    ///    orientation;
    ///  * hip, knee and ankle anchors remain coincident and the measured
    ///    segment lengths do not change;
    ///  * the bar stays rigidly attached to the thorax;
    ///  * the physical handoff still transfers to the judged squat.
    ///
    /// The support side is derived from the existing authored phase contract
    /// (WALKOUT_STEP_1 supports the right foot and swings the left; STEP_2 is
    /// the mirror), not from the implementation under test, so the same
    /// fixture compiles and runs against the pre-repair baseline.
    /// </summary>
    public sealed class GAM68WalkoutLocomotionTests
    {
        private const int MaxWaitFrames = 2400;
        private const int MaxWalkoutFrames = 3600;
        private static readonly float PlatformTopM = PhysicalAthleteDefinition.PlatformSupportPlaneY;
        private const float PlantedDisplacementToleranceM = 0.0025f;
        private const float PlantedSpeedToleranceMps = 0.02f;
        private const float SwingClearanceM = 0.05f;
        private const float LandingPositionToleranceM = 0.004f;
        private const float LandingHeightToleranceM = 0.0005f;
        // The authored pose preserves the measured thigh/shank lengths to
        // 0.0002 mm; the kinematic ankle frame has measured sub-millimetre
        // alignment transients during fast unrack frames. One millimetre is
        // still two orders of magnitude below the rejected skating defect.
        private const float AnchorToleranceM = 0.001f;
        private const float SegmentLengthToleranceM = 0.0002f;
        private const float BarAttachmentPositionToleranceM = 0.0005f;
        private const float BarAttachmentAngularToleranceDeg = 0.05f;
        private const float HandoffPositionToleranceM = 0.001f;
        private const float HandoffBarAngularToleranceDeg = 0.1f;

        private InputTestFixture _input;
        private SquatPhysicalPrototypeController _controller;
        private FoundationBootstrap _bootstrap;
        private Vector3 _athleteForward;
        private readonly List<WalkoutFrame> _frames = new List<WalkoutFrame>(512);

        [SetUp]
        public void SetUp()
        {
            _frames.Clear();
            _input = new InputTestFixture();
            _input.Setup();
            InputSystem.AddDevice<Keyboard>();
            LogAssert.ignoreFailingMessages = true;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_controller != null)
                _controller.enabled = false;
            if (_bootstrap != null)
                _bootstrap.enabled = false;
            if (_bootstrap != null && _bootstrap.Runtime != null && _bootstrap.Runtime.IsInitialized)
            {
                AsyncOperation unload = _bootstrap.Runtime.Shutdown();
                while (unload != null && !unload.isDone)
                    yield return null;
            }
            if (_bootstrap != null)
                UnityEngine.Object.DestroyImmediate(_bootstrap.gameObject);
            _controller = null;
            _bootstrap = null;
            if (_input != null)
            {
                _input.TearDown();
                _input = null;
            }
            LogAssert.ignoreFailingMessages = false;
            yield return null;
        }

        [UnityTest]
        public IEnumerator TWO_STEP_WALKOUT_PLANTS_EACH_FOOT_AND_HANDS_OFF_WITHOUT_SKATING()
        {
            yield return LoadFreshGame();
            yield return WaitForPlayerReadiness();

            Assert.That(_controller.CurrentLoadKg, Is.EqualTo(25f));
            _athleteForward = MeasureAthleteForward(_controller.AthleteRig);
            Assert.That(_athleteForward.sqrMagnitude, Is.GreaterThan(0.5f),
                "The test could not measure the athlete's visible facing from the toe bones.");
            _controller.StartGameFromPlayerUi();
            Assert.That(_controller.PlayerGamePhase, Is.EqualTo(SquatPlayerGamePhase.SETUP));

            _controller.UnrackFromPlayerUi();
            Assert.That(_controller.PlayerGamePhase, Is.EqualTo(SquatPlayerGamePhase.UNRACK));

            var seenPhases = new HashSet<SquatPlayerGamePhase>();
            bool handoffReached = false;
            double startSeconds = Time.realtimeSinceStartupAsDouble;
            for (int frameIndex = 0; frameIndex < MaxWalkoutFrames; frameIndex++)
            {
                yield return null;
                SquatPlayerGamePhase phase = _controller.PlayerGamePhase;
                seenPhases.Add(phase);
                _frames.Add(CaptureFrame(frameIndex, Time.realtimeSinceStartupAsDouble - startSeconds, phase));
                if (phase == SquatPlayerGamePhase.JUDGED_SQUAT)
                {
                    handoffReached = true;
                    break;
                }
            }

            Assert.That(handoffReached, Is.True,
                $"The walkout never handed off to the judged squat; last phase={_controller.PlayerGamePhase}");
            Assert.That(seenPhases, Does.Contain(SquatPlayerGamePhase.UNRACK));
            Assert.That(seenPhases, Does.Contain(SquatPlayerGamePhase.WALKOUT_STEP_1));
            Assert.That(seenPhases, Does.Contain(SquatPlayerGamePhase.WALKOUT_STEP_2));
            Assert.That(seenPhases, Does.Contain(SquatPlayerGamePhase.SETTLE));
            Assert.That(_controller.AttemptOrchestrator.HasStarted, Is.True);

            var violations = new List<string>();
            AnalyzeWalkout(violations);
            AnalyzeHandoff(violations);
            WriteEvidence(violations);

            TestContext.WriteLine(BuildSummary(violations));
            Assert.That(violations, Is.Empty, string.Join("\n", violations));
        }

        private void AnalyzeWalkout(List<string> violations)
        {
            Assert.That(_frames.Count, Is.GreaterThan(30), "The walkout was not sampled densely enough.");

            WalkoutFrame first = _frames[0];
            int lastAuthoredIndex = _frames.Count - 1;
            while (lastAuthoredIndex > 0 && _frames[lastAuthoredIndex].Phase == SquatPlayerGamePhase.JUDGED_SQUAT)
                lastAuthoredIndex--;
            WalkoutFrame last = _frames[lastAuthoredIndex];
            Vector3 pelvisDelta = last.Pelvis - first.Pelvis;
            Vector3 walkPlane = new Vector3(pelvisDelta.x, 0f, pelvisDelta.z);
            float walkDistance = walkPlane.magnitude;
            Vector3 walkDirection = walkDistance > 1e-6f ? walkPlane / walkDistance : Vector3.forward;
            Vector3 lateral = Vector3.Cross(Vector3.up, walkDirection);

            if (walkDistance < 0.31f || walkDistance > 0.41f)
                violations.Add($"walkout displacement {walkDistance:F4} m is outside the accepted 0.31-0.41 m range");
            if (Mathf.Abs(pelvisDelta.y) > 0.06f)
                violations.Add($"pelvis rose/fell {Mathf.Abs(pelvisDelta.y) * 1000f:F1} mm beyond the authored walkout dip");
            if (Mathf.Abs(Vector3.Dot(pelvisDelta, lateral)) > 0.004f)
                violations.Add("the pelvis drifted laterally during the walkout");

            Vector3 expectedLeftLanding = first.LeftSole + walkDirection * walkDistance;
            Vector3 expectedRightLanding = first.RightSole + walkDirection * walkDistance;
            if (HorizontalDistance(last.LeftSole, expectedLeftLanding) > LandingPositionToleranceM)
                violations.Add(
                    $"left foot settled {HorizontalDistance(last.LeftSole, expectedLeftLanding) * 1000f:F1} mm from the authored landing target");
            if (HorizontalDistance(last.RightSole, expectedRightLanding) > LandingPositionToleranceM)
                violations.Add(
                    $"right foot settled {HorizontalDistance(last.RightSole, expectedRightLanding) * 1000f:F1} mm from the authored landing target");
            if (Mathf.Abs(last.LeftSole.y - PlatformTopM) > LandingHeightToleranceM ||
                Mathf.Abs(last.RightSole.y - PlatformTopM) > LandingHeightToleranceM)
                violations.Add("the settled stance is not on the platform support plane");
            float initialStagger = first.LeftSole.z - first.RightSole.z;
            float finalStagger = last.LeftSole.z - last.RightSole.z;
            if (Mathf.Abs(finalStagger - initialStagger) > 0.003f)
                violations.Add(
                    $"the settled stance changed its foot stagger by {(finalStagger - initialStagger) * 1000f:F1} mm");

            int firstStepIndex = FindFirstIndex(SquatPlayerGamePhase.WALKOUT_STEP_1);
            int firstStep2Index = FindFirstIndex(SquatPlayerGamePhase.WALKOUT_STEP_2);
            if (firstStepIndex < 0 || firstStep2Index < 0)
            {
                violations.Add("both authored walkout steps were not observed");
                return;
            }

            AnalyzePlantedFoot(violations, "right", false, SquatPlayerGamePhase.WALKOUT_STEP_1);
            AnalyzePlantedFoot(violations, "left", true, SquatPlayerGamePhase.WALKOUT_STEP_2);
            AnalyzeSwingFoot(violations, "left", true, SquatPlayerGamePhase.WALKOUT_STEP_1, first, expectedLeftLanding);
            AnalyzeSwingFoot(violations, "right", false, SquatPlayerGamePhase.WALKOUT_STEP_2, first, expectedRightLanding);
            AnalyzeGeometry(violations, first);
            AnalyzeBarAttachment(violations, first);
            AnalyzeTouchdown(violations, "left", true, expectedLeftLanding);
            AnalyzeTouchdown(violations, "right", false, expectedRightLanding);
            if (firstStep2Index <= firstStepIndex)
                violations.Add("the second authored step did not follow the first");
        }

        private void AnalyzePlantedFoot(
            List<string> violations,
            string side,
            bool left,
            SquatPlayerGamePhase stepPhase)
        {
            int referenceIndex = FindFirstIndex(stepPhase);
            if (referenceIndex < 0)
            {
                violations.Add($"no {stepPhase} frame was captured for the {side} planted foot");
                return;
            }

            Vector3 reference = left ? _frames[referenceIndex].LeftSole : _frames[referenceIndex].RightSole;
            float maxDisplacement = 0f;
            float maxSpeed = 0f;
            for (int index = referenceIndex + 1; index < _frames.Count; index++)
            {
                WalkoutFrame frame = _frames[index];
                if (frame.Phase != stepPhase)
                    break;
                Vector3 sole = left ? frame.LeftSole : frame.RightSole;
                Vector3 previousSole = left ? _frames[index - 1].LeftSole : _frames[index - 1].RightSole;
                maxDisplacement = Mathf.Max(maxDisplacement, HorizontalDistance(sole, reference));
                double dt = frame.Time - _frames[index - 1].Time;
                if (dt > 1e-6)
                    maxSpeed = Mathf.Max(maxSpeed, HorizontalDistance(sole, previousSole) / (float)dt);
            }

            if (maxDisplacement > PlantedDisplacementToleranceM)
                violations.Add(
                    $"{side} planted foot slid {maxDisplacement * 1000f:F1} mm in world space during {stepPhase} " +
                    $"(tolerance {PlantedDisplacementToleranceM * 1000f:F1} mm)");
            if (maxSpeed > PlantedSpeedToleranceMps)
                violations.Add(
                    $"{side} planted foot moved at {maxSpeed:F3} m/s during {stepPhase} " +
                    $"(tolerance {PlantedSpeedToleranceMps:F3} m/s)");
            TestContext.WriteLine(
                $"GAM68_WALKOUT {side} planted: displacement={maxDisplacement * 1000f:F2}mm speed={maxSpeed:F3}m/s");
        }

        private void AnalyzeSwingFoot(
            List<string> violations,
            string side,
            bool left,
            SquatPlayerGamePhase stepPhase,
            WalkoutFrame first,
            Vector3 expectedLanding)
        {
            Vector3 initialSole = left ? first.LeftSole : first.RightSole;
            float maxClearance = float.NegativeInfinity;
            float maxHorizontalTravel = 0f;
            float maxKneeForward = float.NegativeInfinity;
            for (int index = 0; index < _frames.Count; index++)
            {
                WalkoutFrame frame = _frames[index];
                if (frame.Phase != stepPhase)
                    continue;
                Vector3 sole = left ? frame.LeftSole : frame.RightSole;
                maxClearance = Mathf.Max(maxClearance, sole.y - PlatformTopM);
                maxHorizontalTravel = Mathf.Max(maxHorizontalTravel, HorizontalDistance(sole, initialSole));

                Vector3 hip = left ? frame.LeftHip : frame.RightHip;
                Vector3 knee = left ? frame.LeftKnee : frame.RightKnee;
                Vector3 ankle = left ? frame.LeftAnkle : frame.RightAnkle;
                Vector3 legDirection = (ankle - hip).normalized;
                Vector3 forward = Vector3.ProjectOnPlane(_athleteForward, legDirection);
                if (forward.sqrMagnitude > 1e-6f)
                {
                    forward.Normalize();
                    maxKneeForward = Mathf.Max(maxKneeForward, Vector3.Dot(knee - hip, forward));
                }
            }

            if (maxClearance < SwingClearanceM)
                violations.Add(
                    $"{side} swing foot cleared only {maxClearance * 1000f:F1} mm above the platform " +
                    $"(minimum {SwingClearanceM * 1000f:F1} mm); the foot dragged instead of stepping");
            if (maxHorizontalTravel < 0.30f)
                violations.Add($"{side} swing foot travelled only {maxHorizontalTravel:F3} m backward");
            if (maxKneeForward < 0.01f)
                violations.Add(
                    $"{side} swing knee did not bend toward the athlete's toes " +
                    $"(maximum anterior knee offset {maxKneeForward * 1000f:F1} mm)");

            int lastStepIndex = FindLastIndex(stepPhase);
            if (lastStepIndex >= 0)
            {
                WalkoutFrame lastStep = _frames[lastStepIndex];
                Vector3 finalSole = left ? lastStep.LeftSole : lastStep.RightSole;
                if (HorizontalDistance(finalSole, expectedLanding) > LandingPositionToleranceM)
                    violations.Add(
                        $"{side} swing foot landed {HorizontalDistance(finalSole, expectedLanding) * 1000f:F1} mm from its authored target");
                Quaternion finalRotation = left ? lastStep.LeftFootRotation : lastStep.RightFootRotation;
                Quaternion initialRotation = left ? first.LeftFootRotation : first.RightFootRotation;
                float touchdownRotationDelta = Quaternion.Angle(finalRotation, initialRotation);
                if (touchdownRotationDelta > 1f)
                    violations.Add($"{side} foot touched down {touchdownRotationDelta:F2} deg from the accepted sole orientation");
            }
            TestContext.WriteLine(
                $"GAM68_WALKOUT {side} swing: clearanceMax={maxClearance:F4}m travel={maxHorizontalTravel:F4}m " +
                $"kneeForwardMax={maxKneeForward:F4}m");
        }

        private void AnalyzeTouchdown(List<string> violations, string side, bool left, Vector3 target)
        {
            int touchdownIndex = -1;
            for (int index = 0; index < _frames.Count; index++)
            {
                Vector3 sole = left ? _frames[index].LeftSole : _frames[index].RightSole;
                if (HorizontalDistance(sole, target) <= LandingPositionToleranceM &&
                    Mathf.Abs(sole.y - PlatformTopM) <= LandingHeightToleranceM)
                {
                    touchdownIndex = index;
                    break;
                }
            }
            if (touchdownIndex < 0)
            {
                violations.Add($"{side} foot never reached its authored landing position and support plane");
                return;
            }

            WalkoutFrame touchdown = _frames[touchdownIndex];
            Vector3 touchdownSole = left ? touchdown.LeftSole : touchdown.RightSole;
            for (int index = touchdownIndex + 1; index < _frames.Count; index++)
            {
                if (!IsAuthoredPhase(_frames[index].Phase))
                    break;
                Vector3 sole = left ? _frames[index].LeftSole : _frames[index].RightSole;
                if (HorizontalDistance(sole, touchdownSole) > PlantedDisplacementToleranceM ||
                    Mathf.Abs(sole.y - touchdownSole.y) > LandingHeightToleranceM)
                {
                    violations.Add(
                        $"{side} foot slid {HorizontalDistance(sole, touchdownSole) * 1000f:F1} mm after touchdown");
                    break;
                }
            }
            TestContext.WriteLine(
                $"GAM68_WALKOUT {side} touchdown: t={touchdown.Time:F3}s sole={Format(touchdownSole)} phase={touchdown.Phase}");
        }

        private void AnalyzeGeometry(List<string> violations, WalkoutFrame first)
        {
            float maxAnchorSeparation = 0f;
            float maxSegmentError = 0f;
            float maxKneeDelta = 0f;
            float maxHipDelta = 0f;
            float maxAnkleDelta = 0f;
            WalkoutFrame maxAnchorFrame = default;
            foreach (WalkoutFrame frame in _frames)
            {
                if (!IsAuthoredPhase(frame.Phase))
                    continue;
                if (frame.MaxAnchorSeparation > maxAnchorSeparation)
                    maxAnchorFrame = frame;
                maxAnchorSeparation = Mathf.Max(maxAnchorSeparation, frame.MaxAnchorSeparation);
                maxSegmentError = Mathf.Max(
                    maxSegmentError,
                    Mathf.Abs(frame.LeftThighLength - first.LeftThighLength),
                    Mathf.Abs(frame.RightThighLength - first.RightThighLength),
                    Mathf.Abs(frame.LeftShankLength - first.LeftShankLength),
                    Mathf.Abs(frame.RightShankLength - first.RightShankLength));
                maxKneeDelta = Mathf.Max(
                    maxKneeDelta,
                    Quaternion.Angle(frame.LeftKneeRelative, first.LeftKneeRelative),
                    Quaternion.Angle(frame.RightKneeRelative, first.RightKneeRelative));
                maxHipDelta = Mathf.Max(
                    maxHipDelta,
                    Quaternion.Angle(frame.LeftHipRelative, first.LeftHipRelative),
                    Quaternion.Angle(frame.RightHipRelative, first.RightHipRelative));
                maxAnkleDelta = Mathf.Max(
                    maxAnkleDelta,
                    Quaternion.Angle(frame.LeftAnkleRelative, first.LeftAnkleRelative),
                    Quaternion.Angle(frame.RightAnkleRelative, first.RightAnkleRelative));
                if (!IsFinite(frame.Pelvis) || !IsFinite(frame.Bar) ||
                    !IsFinite(frame.LeftSole) || !IsFinite(frame.RightSole) ||
                    !IsFinite(frame.LeftFootRotation) || !IsFinite(frame.RightFootRotation))
                {
                    violations.Add("a walkout pose contained a non-finite transform");
                    break;
                }
            }

            if (maxAnchorSeparation > AnchorToleranceM)
                violations.Add(
                    $"joint anchors separated by up to {maxAnchorSeparation * 1000f:F3} mm during the walkout " +
                    $"(tolerance {AnchorToleranceM * 1000f:F3} mm)");
            if (maxSegmentError > SegmentLengthToleranceM)
                violations.Add(
                    $"leg segment lengths changed by up to {maxSegmentError * 1000f:F3} mm during the walkout");
            if (maxKneeDelta > 140f)
                violations.Add($"knee pose changed by {maxKneeDelta:F1} deg beyond the authored hinge range");
            if (maxHipDelta > 100f)
                violations.Add($"hip pose changed by {maxHipDelta:F1} deg beyond the authored ball-joint range");
            if (maxAnkleDelta > 50f)
                violations.Add($"ankle pose changed by {maxAnkleDelta:F1} deg beyond the authored hinge range");

            TestContext.WriteLine(
                $"GAM68_WALKOUT geometry: anchorMax={maxAnchorSeparation * 1000f:F4}mm " +
                $"at={maxAnchorFrame.Phase}@{maxAnchorFrame.Time:F3}s " +
                $"Lhip={maxAnchorFrame.LeftHipSeparation * 1000f:F4} Rhip={maxAnchorFrame.RightHipSeparation * 1000f:F4} " +
                $"Lknee={maxAnchorFrame.LeftKneeSeparation * 1000f:F4} Rknee={maxAnchorFrame.RightKneeSeparation * 1000f:F4} " +
                $"Lankle={maxAnchorFrame.LeftAnkleSeparation * 1000f:F4} Rankle={maxAnchorFrame.RightAnkleSeparation * 1000f:F4}mm " +
                $"segmentMax={maxSegmentError * 1000f:F4}mm kneeDelta={maxKneeDelta:F1}deg " +
                $"hipDelta={maxHipDelta:F1}deg ankleDelta={maxAnkleDelta:F1}deg");
        }

        private void AnalyzeBarAttachment(List<string> violations, WalkoutFrame first)
        {
            float maxPosition = 0f;
            float maxAngular = 0f;
            foreach (WalkoutFrame frame in _frames)
            {
                if (!IsAuthoredPhase(frame.Phase))
                    continue;
                maxPosition = Mathf.Max(
                    maxPosition,
                    Vector3.Distance(frame.BarInThoraxLocal, first.BarInThoraxLocal));
                maxAngular = Mathf.Max(
                    maxAngular,
                    Quaternion.Angle(frame.BarInThoraxLocalRotation, first.BarInThoraxLocalRotation));
            }
            if (maxPosition > BarAttachmentPositionToleranceM)
                violations.Add(
                    $"the bar moved {maxPosition * 1000f:F2} mm relative to the thorax during the walkout");
            if (maxAngular > BarAttachmentAngularToleranceDeg)
                violations.Add(
                    $"the bar rotated {maxAngular:F3} deg relative to the thorax during the walkout");
            TestContext.WriteLine(
                $"GAM68_WALKOUT bar: localPositionMax={maxPosition * 1000f:F3}mm localAngleMax={maxAngular:F4}deg");
        }

        private void AnalyzeHandoff(List<string> violations)
        {
            float poseError = _controller.HybridHandoffPoseErrorMeters;
            float angularError = _controller.HybridHandoffBarAngularErrorDegrees;
            float distance = _controller.HybridWalkoutDistanceMeters;
            if (!float.IsFinite(poseError) || poseError > HandoffPositionToleranceM)
                violations.Add($"handoff pose error {poseError * 1000f:F3} mm exceeds the accepted 1 mm contract");
            if (!float.IsFinite(angularError) || angularError > HandoffBarAngularToleranceDeg)
                violations.Add($"handoff bar angular error {angularError:F3} deg exceeds the accepted 0.1 deg contract");
            if (distance < 0.31f || distance > 0.41f)
                violations.Add($"reported walkout distance {distance:F4} m is outside the accepted range");

            foreach (PhysicalAthleteRig.SegmentRuntime segment in _controller.AthleteRig.Segments.Values)
            {
                if (segment.Body.isKinematic || !segment.Body.useGravity)
                {
                    violations.Add($"segment '{segment.Recipe.Id}' did not return to dynamic gravity authority at handoff");
                    break;
                }
                if (!float.IsFinite(segment.Body.linearVelocity.magnitude) ||
                    !float.IsFinite(segment.Body.angularVelocity.magnitude))
                {
                    violations.Add($"segment '{segment.Recipe.Id}' handed off with a non-finite velocity");
                    break;
                }
            }
            PhysicalBarbell barbell = _controller.Saddle.Barbell;
            if (barbell.Body.isKinematic || !barbell.Body.useGravity)
                violations.Add("the barbell did not return to dynamic gravity authority at handoff");

            TestContext.WriteLine(
                $"GAM68_WALKOUT handoff: poseError={poseError * 1000f:F4}mm barAngular={angularError:F5}deg " +
                $"distance={distance:F5}m started={_controller.AttemptOrchestrator.HasStarted}");
        }

        private WalkoutFrame CaptureFrame(int index, double time, SquatPlayerGamePhase phase)
        {
            PhysicalAthleteRig rig = _controller.AthleteRig;
            PhysicalAthleteRig.SegmentRuntime pelvis = rig.Segments["pelvis"];
            PhysicalAthleteRig.SegmentRuntime thorax = rig.Segments["thorax"];
            PhysicalAthleteRig.SegmentRuntime leftFoot = rig.Segments["left_foot"];
            PhysicalAthleteRig.SegmentRuntime rightFoot = rig.Segments["right_foot"];
            PhysicalAthleteRig.SegmentRuntime leftThigh = rig.Segments["left_thigh"];
            PhysicalAthleteRig.SegmentRuntime leftShank = rig.Segments["left_shank"];
            PhysicalAthleteRig.SegmentRuntime rightThigh = rig.Segments["right_thigh"];
            PhysicalAthleteRig.SegmentRuntime rightShank = rig.Segments["right_shank"];
            Rigidbody bar = _controller.Saddle.Barbell.Body;

            Vector3 leftHip = Anchor(rig, "left_thigh");
            Vector3 leftKnee = Anchor(rig, "left_shank");
            Vector3 leftAnkle = Anchor(rig, "left_foot");
            Vector3 rightHip = Anchor(rig, "right_thigh");
            Vector3 rightKnee = Anchor(rig, "right_shank");
            Vector3 rightAnkle = Anchor(rig, "right_foot");

            return new WalkoutFrame
            {
                Index = index,
                Time = time,
                Phase = phase,
                LeftSole = Sole(leftFoot),
                RightSole = Sole(rightFoot),
                LeftFootRotation = leftFoot.Body.rotation,
                RightFootRotation = rightFoot.Body.rotation,
                Pelvis = pelvis.Body.position,
                Bar = bar.position,
                BarInThoraxLocal = Quaternion.Inverse(thorax.Body.rotation) * (bar.position - thorax.Body.position),
                BarInThoraxLocalRotation = Quaternion.Inverse(thorax.Body.rotation) * bar.rotation,
                LeftHip = leftHip,
                RightHip = rightHip,
                LeftKnee = leftKnee,
                RightKnee = rightKnee,
                LeftAnkle = leftAnkle,
                RightAnkle = rightAnkle,
                LeftThighLength = Vector3.Distance(leftHip, leftKnee),
                RightThighLength = Vector3.Distance(rightHip, rightKnee),
                LeftShankLength = Vector3.Distance(leftKnee, leftAnkle),
                RightShankLength = Vector3.Distance(rightKnee, rightAnkle),
                LeftHipRelative = Quaternion.Inverse(pelvis.Body.rotation) * leftThigh.Body.rotation,
                RightHipRelative = Quaternion.Inverse(pelvis.Body.rotation) * rightThigh.Body.rotation,
                LeftKneeRelative = Quaternion.Inverse(leftThigh.Body.rotation) * leftShank.Body.rotation,
                RightKneeRelative = Quaternion.Inverse(rightThigh.Body.rotation) * rightShank.Body.rotation,
                LeftAnkleRelative = Quaternion.Inverse(leftShank.Body.rotation) * leftFoot.Body.rotation,
                RightAnkleRelative = Quaternion.Inverse(rightShank.Body.rotation) * rightFoot.Body.rotation,
                LeftHipSeparation = Separation(rig, "left_thigh"),
                RightHipSeparation = Separation(rig, "right_thigh"),
                LeftKneeSeparation = Separation(rig, "left_shank"),
                RightKneeSeparation = Separation(rig, "right_shank"),
                LeftAnkleSeparation = Separation(rig, "left_foot"),
                RightAnkleSeparation = Separation(rig, "right_foot"),
                MaxAnchorSeparation = Mathf.Max(
                    Separation(rig, "left_thigh"), Separation(rig, "right_thigh"),
                    Separation(rig, "left_shank"), Separation(rig, "right_shank"),
                    Separation(rig, "left_foot"), Separation(rig, "right_foot"))
            };
        }

        private static bool IsAuthoredPhase(SquatPlayerGamePhase phase) =>
            phase == SquatPlayerGamePhase.UNRACK ||
            phase == SquatPlayerGamePhase.WALKOUT_STEP_1 ||
            phase == SquatPlayerGamePhase.WALKOUT_STEP_2 ||
            phase == SquatPlayerGamePhase.SETTLE ||
            phase == SquatPlayerGamePhase.PHYSICS_HANDOFF;

        private int FindFirstIndex(SquatPlayerGamePhase phase)
        {
            for (int index = 0; index < _frames.Count; index++)
                if (_frames[index].Phase == phase)
                    return index;
            return -1;
        }

        private int FindLastIndex(SquatPlayerGamePhase phase)
        {
            for (int index = _frames.Count - 1; index >= 0; index--)
                if (_frames[index].Phase == phase)
                    return index;
            return -1;
        }

        private static float HorizontalDistance(Vector3 first, Vector3 second)
        {
            Vector2 delta = new Vector2(first.x - second.x, first.z - second.z);
            return delta.magnitude;
        }

        private static Vector3 MeasureAthleteForward(PhysicalAthleteRig rig)
        {
            Animator reference = rig.ReferenceAnimator;
            Transform leftFoot = reference.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform leftToes = reference.GetBoneTransform(HumanBodyBones.LeftToes);
            Transform rightFoot = reference.GetBoneTransform(HumanBodyBones.RightFoot);
            Transform rightToes = reference.GetBoneTransform(HumanBodyBones.RightToes);
            if (leftFoot == null || leftToes == null || rightFoot == null || rightToes == null)
                return Vector3.zero;
            Vector3 direction = Vector3.ProjectOnPlane(
                (leftToes.position - leftFoot.position) +
                (rightToes.position - rightFoot.position),
                Vector3.up);
            return direction.sqrMagnitude > 1e-8f ? direction.normalized : Vector3.zero;
        }

        private static Vector3 Sole(PhysicalAthleteRig.SegmentRuntime foot)
        {
            BoxCollider box = (BoxCollider)foot.Collider;
            Vector3 localSole = box.center - new Vector3(0f, box.size.y * 0.5f, 0f);
            return foot.Body.transform.TransformPoint(localSole);
        }

        private static Vector3 Anchor(PhysicalAthleteRig rig, string jointId)
        {
            PoweredJointController.PoweredJointRuntime joint = rig.PoweredController.GetJoint(jointId);
            return joint == null || joint.Joint == null
                ? Vector3.zero
                : joint.Joint.transform.TransformPoint(joint.Joint.anchor);
        }

        private static float Separation(PhysicalAthleteRig rig, string jointId)
        {
            PoweredJointController.PoweredJointRuntime joint = rig.PoweredController.GetJoint(jointId);
            if (joint == null || joint.Joint == null || joint.Joint.connectedBody == null)
                return float.PositiveInfinity;
            Vector3 childAnchor = joint.Joint.transform.TransformPoint(joint.Joint.anchor);
            Vector3 parentAnchor = joint.Joint.connectedBody.transform.TransformPoint(joint.Joint.connectedAnchor);
            return Vector3.Distance(childAnchor, parentAnchor);
        }

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        private static bool IsFinite(Quaternion value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z) && float.IsFinite(value.w);

        private string BuildSummary(List<string> violations)
        {
            WalkoutFrame first = _frames[0];
            WalkoutFrame last = _frames[_frames.Count - 1];
            Vector3 delta = last.Pelvis - first.Pelvis;
            return string.Format(
                CultureInfo.InvariantCulture,
                "GAM68_WALKOUT summary frames={0} duration={1:F3}s lastPhase={2} displacement=({3:F4},{4:F4},{5:F4}) " +
                "leftSole={6} rightSole={7} violations={8}",
                _frames.Count,
                last.Time,
                _controller.PlayerGamePhase,
                delta.x, delta.y, delta.z,
                Format(first.LeftSole),
                Format(first.RightSole),
                violations.Count);
        }

        private void WriteEvidence(List<string> violations)
        {
            string label = Environment.GetEnvironmentVariable("GAM68_WALKOUT_LABEL");
            if (string.IsNullOrWhiteSpace(label))
                label = "current";
            string directory = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                "Artifacts", "Evidence", "GAM-68-walkout", label);
            Directory.CreateDirectory(directory);

            var csv = new StringBuilder();
            csv.AppendLine("index,time,phase,leftSoleX,leftSoleY,leftSoleZ,rightSoleX,rightSoleY,rightSoleZ," +
                "pelvisX,pelvisY,pelvisZ,barX,barY,barZ,barLocalX,barLocalY,barLocalZ," +
                "maxAnchorSeparation,leftHipSep,rightHipSep,leftKneeSep,rightKneeSep,leftAnkleSep,rightAnkleSep," +
                "kneeDelta,hipDelta,ankleDelta,leftThigh,rightThigh,leftShank,rightShank");
            foreach (WalkoutFrame frame in _frames)
            {
                csv.AppendLine(string.Join(",", new[]
                {
                    frame.Index.ToString(CultureInfo.InvariantCulture),
                    frame.Time.ToString("F5", CultureInfo.InvariantCulture),
                    frame.Phase.ToString(),
                    Format(frame.LeftSole), Format(frame.RightSole), Format(frame.Pelvis), Format(frame.Bar),
                    Format(frame.BarInThoraxLocal),
                    frame.MaxAnchorSeparation.ToString("F8", CultureInfo.InvariantCulture),
                    frame.LeftHipSeparation.ToString("F8", CultureInfo.InvariantCulture),
                    frame.RightHipSeparation.ToString("F8", CultureInfo.InvariantCulture),
                    frame.LeftKneeSeparation.ToString("F8", CultureInfo.InvariantCulture),
                    frame.RightKneeSeparation.ToString("F8", CultureInfo.InvariantCulture),
                    frame.LeftAnkleSeparation.ToString("F8", CultureInfo.InvariantCulture),
                    frame.RightAnkleSeparation.ToString("F8", CultureInfo.InvariantCulture),
                    Quaternion.Angle(frame.LeftKneeRelative, _frames[0].LeftKneeRelative).ToString("F4", CultureInfo.InvariantCulture),
                    Quaternion.Angle(frame.LeftHipRelative, _frames[0].LeftHipRelative).ToString("F4", CultureInfo.InvariantCulture),
                    Quaternion.Angle(frame.LeftAnkleRelative, _frames[0].LeftAnkleRelative).ToString("F4", CultureInfo.InvariantCulture),
                    frame.LeftThighLength.ToString("F6", CultureInfo.InvariantCulture),
                    frame.RightThighLength.ToString("F6", CultureInfo.InvariantCulture),
                    frame.LeftShankLength.ToString("F6", CultureInfo.InvariantCulture),
                    frame.RightShankLength.ToString("F6", CultureInfo.InvariantCulture)
                }));
            }
            File.WriteAllText(Path.Combine(directory, "walkout-trace.csv"), csv.ToString());

            var summary = new StringBuilder();
            summary.AppendLine("{");
            summary.AppendLine("  \"schema\": \"GAM68_WALKOUT_LOCOMOTION_EVIDENCE_V1\",");
            summary.AppendLine($"  \"label\": \"{label}\",");
            summary.AppendLine($"  \"unityVersion\": \"{Application.unityVersion}\",");
            summary.AppendLine($"  \"frames\": {_frames.Count},");
            summary.AppendLine($"  \"handoffPoseErrorMeters\": {_controller.HybridHandoffPoseErrorMeters.ToString("R", CultureInfo.InvariantCulture)},");
            summary.AppendLine($"  \"handoffBarAngularErrorDegrees\": {_controller.HybridHandoffBarAngularErrorDegrees.ToString("R", CultureInfo.InvariantCulture)},");
            summary.AppendLine($"  \"walkoutDistanceMeters\": {_controller.HybridWalkoutDistanceMeters.ToString("R", CultureInfo.InvariantCulture)},");
            summary.AppendLine("  \"violations\": [");
            for (int index = 0; index < violations.Count; index++)
            {
                summary.Append("    \"");
                summary.Append(violations[index].Replace("\\", "\\\\").Replace("\"", "\\\""));
                summary.Append('"');
                summary.AppendLine(index == violations.Count - 1 ? string.Empty : ",");
            }
            summary.AppendLine("  ]");
            summary.AppendLine("}");
            File.WriteAllText(Path.Combine(directory, "walkout-summary.json"), summary.ToString());
            TestContext.WriteLine($"GAM68_WALKOUT evidence={directory}");
        }

        private static string Format(Vector3 value) =>
            string.Format(CultureInfo.InvariantCulture, "{0:F5},{1:F5},{2:F5}", value.x, value.y, value.z);

        private IEnumerator LoadFreshGame()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(0, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            while (!load.isDone)
                yield return null;
            for (int frame = 0; frame < 500; frame++)
            {
                _controller = UnityEngine.Object.FindFirstObjectByType<SquatPhysicalPrototypeController>();
                _bootstrap = UnityEngine.Object.FindFirstObjectByType<FoundationBootstrap>();
                if (_controller != null && _controller.IsInitialized && _bootstrap != null &&
                    _bootstrap.Runtime != null && _bootstrap.Runtime.IsInitialized)
                    yield break;
                yield return new WaitForSecondsRealtime(0.01f);
            }
            Assert.Fail("The player-facing squat scene did not initialize.");
        }

        private IEnumerator WaitForPlayerReadiness()
        {
            for (int frame = 0; frame < MaxWaitFrames && !_controller.CanBeginPlayerSetup; frame++)
                yield return new WaitForSecondsRealtime(0.01f);
            Assert.That(_controller.CanBeginPlayerSetup, Is.True,
                "The production start predicate never reached its stable 25 kg setup pose.");
        }

        private struct WalkoutFrame
        {
            public int Index;
            public double Time;
            public SquatPlayerGamePhase Phase;
            public Vector3 LeftSole;
            public Vector3 RightSole;
            public Quaternion LeftFootRotation;
            public Quaternion RightFootRotation;
            public Vector3 Pelvis;
            public Vector3 Bar;
            public Vector3 BarInThoraxLocal;
            public Quaternion BarInThoraxLocalRotation;
            public Vector3 LeftHip;
            public Vector3 RightHip;
            public Vector3 LeftKnee;
            public Vector3 RightKnee;
            public Vector3 LeftAnkle;
            public Vector3 RightAnkle;
            public float LeftThighLength;
            public float RightThighLength;
            public float LeftShankLength;
            public float RightShankLength;
            public Quaternion LeftHipRelative;
            public Quaternion RightHipRelative;
            public Quaternion LeftKneeRelative;
            public Quaternion RightKneeRelative;
            public Quaternion LeftAnkleRelative;
            public Quaternion RightAnkleRelative;
            public float LeftHipSeparation;
            public float RightHipSeparation;
            public float LeftKneeSeparation;
            public float RightKneeSeparation;
            public float LeftAnkleSeparation;
            public float RightAnkleSeparation;
            public float MaxAnchorSeparation;
        }
    }
}
