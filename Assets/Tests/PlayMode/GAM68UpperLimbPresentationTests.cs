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
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.Tests
{
    /// <summary>
    /// GAM-68 owner-visible grip evidence. The same harness is run against the
    /// clean production baseline and the presentation-grip candidate, so the
    /// rear, side and gameplay images and the palm-to-live-shaft numbers are
    /// directly comparable. It does not pass judgement on physical validity;
    /// it proves the plant topology and physical authority did not change.
    /// </summary>
    public sealed class GAM68UpperLimbPresentationTests
    {
        private const int MaxWaitFrames = 2400;
        private const float BarRadiusM = 0.0145f;

        private InputTestFixture _input;
        private Keyboard _keyboard;
        private SquatPhysicalPrototypeController _controller;
        private FoundationBootstrap _bootstrap;
        private PowerliftingGameUiController _ui;
        private PowerliftingCameraDirector _cameraDirector;
        private PowerliftingGymEnvironment _gym;
        private readonly List<Gam68Sample> _samples = new List<Gam68Sample>();
        private readonly List<string> _capturedPhases = new List<string>();

        [SetUp]
        public void SetUp()
        {
            PowerliftingGameUiController.ClearPendingReloadScreen();
            _input = new InputTestFixture();
            _input.Setup();
            _keyboard = InputSystem.AddDevice<Keyboard>();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PowerliftingGameUiController.ClearPendingReloadScreen();
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
            _ui = null;
            _cameraDirector = null;

            if (_input != null)
            {
                _input.TearDown();
                _input = null;
                _keyboard = null;
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator CAPTURE_BILATERAL_GRIP_ACROSS_THE_PLAYED_SQUAT()
        {
            string label = Environment.GetEnvironmentVariable("GAM68_CAPTURE_LABEL");
            if (string.IsNullOrWhiteSpace(label))
                label = "current";
            string captureDir = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                "Artifacts", "Evidence", "GAM-68", label);
            Directory.CreateDirectory(captureDir);

            yield return LoadFreshSceneZero();
            yield return WaitForPlayerReadiness();

            Assert.That(_controller, Is.Not.Null);
            Assert.That(_controller.AthleteRig, Is.Not.Null);
            Assert.That(_controller.Adapter, Is.Not.Null);
            Assert.That(_controller.Adapter.ReferenceCalibration, Is.Not.Null);
            Assert.That(_controller.AthleteRig.Segments.Count, Is.EqualTo(16),
                "The accepted physical plant must keep exactly 16 bodies.");
            Assert.That(_controller.AthleteRig.Joints.Count, Is.EqualTo(15),
                "The accepted physical plant must keep exactly 15 joints.");

            _ui.EnterTrainingGym();
            _ui.SelectSquatStation();
            yield return WaitForPlayerReadiness();
            _ui.BeginPlayerSetupAction();
            _ui.BeginUnrackAndWalkoutAction();

            for (int frame = 0; frame < MaxWaitFrames && _controller.PlayerGamePhase != SquatPlayerGamePhase.JUDGED_SQUAT; frame++)
                yield return new WaitForSecondsRealtime(0.01f);
            Assert.That(_controller.PlayerGamePhase, Is.EqualTo(SquatPlayerGamePhase.JUDGED_SQUAT));

            for (int frame = 0; frame < MaxWaitFrames && !_controller.AttemptOrchestrator.HasSquatCommand; frame++)
                yield return new WaitForSecondsRealtime(0.01f);
            Assert.That(_controller.AttemptOrchestrator.HasSquatCommand, Is.True);

            yield return CapturePhase(captureDir, "01_standing");

            Press(_keyboard.sKey);
            bool capturedDescent = false;
            for (int frame = 0; frame < MaxWaitFrames && _controller.Adapter.State != SquatState.BOTTOM; frame++)
            {
                if (!capturedDescent && _controller.Adapter.Sq >= 0.5f)
                {
                    yield return CapturePhase(captureDir, "02_descent");
                    capturedDescent = true;
                }
                yield return new WaitForSecondsRealtime(0.01f);
            }
            Assert.That(_controller.Adapter.State, Is.EqualTo(SquatState.BOTTOM));
            yield return CapturePhase(captureDir, "03_bottom");

            Release(_keyboard.sKey);
            yield return new WaitForSecondsRealtime(0.02f);
            Press(_keyboard.wKey);
            bool capturedAscent = false;
            for (int frame = 0; frame < MaxWaitFrames && _controller.Adapter.State != SquatState.LOCKOUT; frame++)
            {
                if (!capturedAscent && _controller.Adapter.Sq <= 0.5f && _controller.Adapter.State != SquatState.BOTTOM)
                {
                    yield return CapturePhase(captureDir, "04_ascent");
                    capturedAscent = true;
                }
                yield return new WaitForSecondsRealtime(0.01f);
            }
            Assert.That(_controller.Adapter.State, Is.EqualTo(SquatState.LOCKOUT));
            Release(_keyboard.wKey);
            yield return CapturePhase(captureDir, "05_lockout");

            WriteMeasurements(captureDir);
            Assert.That(_capturedPhases, Is.EquivalentTo(new[] { "01_standing", "02_descent", "03_bottom", "04_ascent", "05_lockout" }));
            Assert.That(_samples.Count, Is.GreaterThanOrEqualTo(5), "Every phase must have bilateral geometry samples.");

            foreach (Gam68Sample sample in _samples)
            {
                Assert.That(sample.physicalBodyCount, Is.EqualTo(16), "Presentation changed the physical body count.");
                Assert.That(sample.physicalJointCount, Is.EqualTo(15), "Presentation changed the physical joint count.");
                Assert.That(sample.barDynamic, Is.True, "The bar must stay dynamic.");
                Assert.That(sample.leftHandJointCount, Is.EqualTo(1), "The left hand must keep only its physical wrist joint.");
                Assert.That(sample.rightHandJointCount, Is.EqualTo(1), "The right hand must keep only its physical wrist joint.");
                Assert.That(sample.leftHandFixedJointCount, Is.EqualTo(0));
                Assert.That(sample.rightHandFixedJointCount, Is.EqualTo(0));
                Assert.That(sample.leftPalmSurfaceValid, Is.True, "The rendered left palm must be measurable.");
                Assert.That(sample.rightPalmSurfaceValid, Is.True, "The rendered right palm must be measurable.");
            }

            TestContext.WriteLine($"GAM-68 capture label={label} directory={captureDir} samples={_samples.Count}");
        }

        private IEnumerator CapturePhase(string captureDir, string phase)
        {
            _capturedPhases.Add(phase);
            yield return null;
            Sample(phase);
            WriteMeasurements(captureDir);
            TestContext.WriteLine(
                $"GAM-68 phase={phase} leftGap={_samples[_samples.Count - 1].leftPalmGapM * 1000f:F1}mm " +
                $"rightGap={_samples[_samples.Count - 1].rightPalmGapM * 1000f:F1}mm " +
                $"leftNormal={_samples[_samples.Count - 1].leftPalmNormalErrorDeg:F1}deg " +
                $"rightNormal={_samples[_samples.Count - 1].rightPalmNormalErrorDeg:F1}deg");
            yield return RenderViews(captureDir, phase);
            yield return null;
        }

        private void Sample(string phase)
        {
            PhysicalAthleteRig rig = _controller.AthleteRig;
            PhysicalBarbell barbell = UnityEngine.Object.FindFirstObjectByType<PhysicalBarbell>();
            Assert.That(barbell, Is.Not.Null);
            Rigidbody bar = barbell.Body;
            Assert.That(bar, Is.Not.Null);

            Transform leftHand = rig.VisibleAnimator.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform rightHand = rig.VisibleAnimator.GetBoneTransform(HumanBodyBones.RightHand);
            Assert.That(leftHand, Is.Not.Null);
            Assert.That(rightHand, Is.Not.Null);

            Vector3 axis = (bar.rotation * Vector3.right).normalized;
            Vector3 center = bar.position;
            bool leftValid = SquatPalmSurface.TryMeasure(
                rig.VisibleAnimator, true, center, axis, out SquatPalmSurfaceFrame leftFrame, out string leftReason);
            bool rightValid = SquatPalmSurface.TryMeasure(
                rig.VisibleAnimator, false, center, axis, out SquatPalmSurfaceFrame rightFrame, out string rightReason);

            var sample = new Gam68Sample
            {
                phase = phase,
                tick = _controller.ObservationCollector != null ? _controller.ObservationCollector.Trace.Count : 0,
                sq = _controller.Adapter.Sq,
                state = _controller.Adapter.State.ToString(),
                loadKg = _controller.CurrentLoadKg,
                physicalBodyCount = rig.Segments.Count,
                physicalJointCount = rig.Joints.Count,
                barDynamic = !bar.isKinematic && bar.useGravity,
                leftHandJointCount = rig.Segments["left_hand"].Body.GetComponents<ConfigurableJoint>().Length,
                rightHandJointCount = rig.Segments["right_hand"].Body.GetComponents<ConfigurableJoint>().Length,
                leftHandFixedJointCount = rig.Segments["left_hand"].Body.GetComponents<FixedJoint>().Length,
                rightHandFixedJointCount = rig.Segments["right_hand"].Body.GetComponents<FixedJoint>().Length,
                barCenter = V(center),
                barAxis = V(axis),
                leftPalmSurfaceValid = leftValid,
                rightPalmSurfaceValid = rightValid,
                leftPalmSurfaceReason = leftReason,
                rightPalmSurfaceReason = rightReason,
                leftHandWorld = V(leftHand.position),
                rightHandWorld = V(rightHand.position),
                leftHandRotation = Q(leftHand.rotation),
                rightHandRotation = Q(rightHand.rotation)
            };

            if (leftValid)
                FillPalm(sample, true, leftHand, leftFrame, center, axis);
            if (rightValid)
                FillPalm(sample, false, rightHand, rightFrame, center, axis);

            sample.leftUpperArmPosition = V(JointAnchor(rig, "left_upper_arm"));
            sample.rightUpperArmPosition = V(JointAnchor(rig, "right_upper_arm"));
            sample.leftForearmPosition = V(JointAnchor(rig, "left_forearm"));
            sample.rightForearmPosition = V(JointAnchor(rig, "right_forearm"));
            sample.leftHandBodyPosition = V(JointAnchor(rig, "left_hand"));
            sample.rightHandBodyPosition = V(JointAnchor(rig, "right_hand"));
            sample.leftElbowFlexionDeg = ElbowAngle(rig, true);
            sample.rightElbowFlexionDeg = ElbowAngle(rig, false);
            sample.leftShoulderAbductionDeg = ShoulderAngle(rig, true);
            sample.rightShoulderAbductionDeg = ShoulderAngle(rig, false);
            sample.leftWristErrorDeg = WristError(rig, true);
            sample.rightWristErrorDeg = WristError(rig, false);
            sample.liveUpperLimbActive = _controller.Adapter.LiveUpperLimbActive;
            sample.liveLeftHandPositionResidualM = _controller.Adapter.LiveLeftHandPositionResidualM;
            sample.liveRightHandPositionResidualM = _controller.Adapter.LiveRightHandPositionResidualM;
            sample.liveLeftHandOrientationResidualDeg = _controller.Adapter.LiveLeftHandOrientationResidualDeg;
            sample.liveRightHandOrientationResidualDeg = _controller.Adapter.LiveRightHandOrientationResidualDeg;
            sample.liveLeftHandTargetWorld = V(_controller.Adapter.LiveLeftHandTargetWorld);
            sample.liveRightHandTargetWorld = V(_controller.Adapter.LiveRightHandTargetWorld);
            sample.liveLeftElbowTargetWorld = V(_controller.Adapter.LiveLeftElbowTargetWorld);
            sample.liveRightElbowTargetWorld = V(_controller.Adapter.LiveRightElbowTargetWorld);
            sample.leftShoulderTelemetry = JointTelemetry(rig, "left_upper_arm");
            sample.rightShoulderTelemetry = JointTelemetry(rig, "right_upper_arm");
            sample.leftElbowTelemetry = JointTelemetry(rig, "left_forearm");
            sample.rightElbowTelemetry = JointTelemetry(rig, "right_forearm");
            sample.leftWristTelemetry = JointTelemetry(rig, "left_hand");
            sample.rightWristTelemetry = JointTelemetry(rig, "right_hand");
            sample.virtualBarCenter = V(VirtualReferenceBarCenter());
            sample.liveBarCenter = V(center);
            sample.virtualToLiveBarErrorM = Vector3.Distance(ParseV(sample.virtualBarCenter), center);
            Vector3 thoraxPosition = rig.Segments["thorax"].Body.position;
            Quaternion thoraxRotation = rig.Segments["thorax"].Body.rotation;
            sample.barInThoraxLocal = V(Quaternion.Inverse(thoraxRotation) * (center - thoraxPosition));
            sample.saddleAnchorInThoraxLocal = V(SquatBarSaddle.ThoraxLocalAnchor);
            _samples.Add(sample);
        }

        private void FillPalm(
            Gam68Sample sample,
            bool isLeft,
            Transform hand,
            SquatPalmSurfaceFrame frame,
            Vector3 barCenter,
            Vector3 axis)
        {
            Vector3 palmCenter = hand.TransformPoint(frame.CenterLocal);
            Vector3 palmNormal = (hand.rotation * frame.NormalLocal).normalized;
            Vector3 closest = ClosestPointOnShaft(palmCenter, barCenter, axis);
            Vector3 radial = palmCenter - closest;
            float radialDistance = radial.magnitude;
            float gap = radialDistance - BarRadiusM;
            float normalError = radialDistance > 1e-5f
                ? Vector3.Angle(palmNormal, (closest - palmCenter).normalized)
                : 0f;
            if (isLeft)
            {
                sample.leftPalmCenter = V(palmCenter);
                sample.leftPalmNormal = V(palmNormal);
                sample.leftPalmGapM = gap;
                sample.leftPalmNormalErrorDeg = normalError;
                sample.leftPalmSpanM = frame.SpanMeters;
            }
            else
            {
                sample.rightPalmCenter = V(palmCenter);
                sample.rightPalmNormal = V(palmNormal);
                sample.rightPalmGapM = gap;
                sample.rightPalmNormalErrorDeg = normalError;
                sample.rightPalmSpanM = frame.SpanMeters;
            }
        }

        private static Vector3 ClosestPointOnShaft(Vector3 point, Vector3 shaftOrigin, Vector3 axis)
        {
            float along = Vector3.Dot(point - shaftOrigin, axis);
            return shaftOrigin + axis * along;
        }

        private static float ElbowAngle(PhysicalAthleteRig rig, bool isLeft)
        {
            string upper = isLeft ? "left_upper_arm" : "right_upper_arm";
            string fore = isLeft ? "left_forearm" : "right_forearm";
            string hand = isLeft ? "left_hand" : "right_hand";
            Vector3 a = rig.Segments[upper].Body.position;
            Vector3 b = rig.Segments[fore].Body.position;
            Vector3 c = rig.Segments[hand].Body.position;
            return Vector3.Angle(a - b, c - b);
        }

        private static float ShoulderAngle(PhysicalAthleteRig rig, bool isLeft)
        {
            string upper = isLeft ? "left_upper_arm" : "right_upper_arm";
            string fore = isLeft ? "left_forearm" : "right_forearm";
            string thorax = "thorax";
            Vector3 humerus = rig.Segments[fore].Body.position - rig.Segments[upper].Body.position;
            Vector3 up = rig.Segments[thorax].Body.rotation * Vector3.up;
            return Vector3.Angle(humerus, up);
        }

        private static float WristError(PhysicalAthleteRig rig, bool isLeft)
        {
            string hand = isLeft ? "left_hand" : "right_hand";
            Transform bone = rig.VisibleAnimator.GetBoneTransform(isLeft ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            if (bone == null)
                return float.NaN;
            PhysicalAthleteRig.SegmentRuntime segment = rig.Segments[hand];
            Quaternion expected = segment.Body.rotation * segment.BodyToReferenceBoneRotation;
            return Quaternion.Angle(bone.rotation, expected);
        }

        private Vector3 VirtualReferenceBarCenter()
        {
            SquatReferenceRigCalibration calibration = _controller.Adapter.ReferenceCalibration;
            if (calibration == null)
                return Vector3.zero;
            SquatReferencePose pose = _controller.Adapter.ReferenceAnatomicalPose(
                _controller.Adapter.Sq,
                _controller.Adapter.Direction == SquatPhaseDirection.None
                    ? SquatPhaseDirection.Descent
                    : _controller.Adapter.Direction);
            SquatReferenceKinematicSolution solution = SquatReferenceKinematics.Solve(
                calibration,
                pose,
                _controller.Adapter.LeftReferencePlantarAnchorWorld,
                _controller.Adapter.RightReferencePlantarAnchorWorld);
            if (!solution.IsValid)
                return Vector3.zero;
            Vector3 upperChest = SquatReferenceUpperLimb.UpperChestBonePosition(calibration, solution);
            Vector3 forward = solution.UpperChestFrameRotation * calibration.GameForward;
            Vector3 up = solution.UpperChestFrameRotation * calibration.GameUp;
            return upperChest -
                forward * SquatReferenceKinematics.ArmBackOffsetM +
                up * SquatReferenceKinematics.ArmBarHeightM;
        }

        private static Vector3 ParseV(string text)
        {
            string[] parts = text.Split(',');
            return new Vector3(
                float.Parse(parts[0], CultureInfo.InvariantCulture),
                float.Parse(parts[1], CultureInfo.InvariantCulture),
                float.Parse(parts[2], CultureInfo.InvariantCulture));
        }

        private static Vector3 JointAnchor(PhysicalAthleteRig rig, string jointId)
        {
            PoweredJointController.PoweredJointRuntime joint = rig.PoweredController.GetJoint(jointId);
            return joint == null || joint.Joint == null
                ? Vector3.zero
                : joint.Joint.transform.TransformPoint(joint.Joint.anchor);
        }

        private static string JointTelemetry(PhysicalAthleteRig rig, string jointId)
        {            PoweredJointController.PoweredJointRuntime joint = rig.PoweredController.GetJoint(jointId);
            if (joint == null)
                return "missing";
            PoweredJointDiagnostic d = joint.Diagnostic;
            return string.Format(
                CultureInfo.InvariantCulture,
                "req={0} app={1} act={2} err={3:F4} max={4:F2} dem={5:F3} lim={6:F3} twist={7:F3}/{8:F3} swing={9:F3}/{10:F3}",
                Q(d.RequestedTarget), Q(d.AppliedTarget), Q(d.ActualRelative),
                d.ErrorRad.magnitude, d.MaximumForceNm, d.ModeledDemand, d.LimitProximity,
                d.TwistDriveDemandNm, d.TwistDriveDemandFraction,
                d.SwingDriveDemandNm, d.SwingDriveDemandFraction);
        }

        private IEnumerator RenderViews(string captureDir, string phase)
        {
            if (_cameraDirector == null || _cameraDirector.TargetCamera == null)
                yield break;
            Camera camera = _cameraDirector.TargetCamera;
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                yield break;

            bool wasEnabled = _cameraDirector.enabled;
            Vector3 savedPosition = camera.transform.position;
            Quaternion savedRotation = camera.transform.rotation;
            float savedFov = camera.fieldOfView;
            _cameraDirector.enabled = false;

            Vector3 pelvis = _controller.AthleteRig.Segments["pelvis"].Body.position;
            Vector3 focus = new Vector3(pelvis.x, pelvis.y + 0.22f, pelvis.z);
            Transform squatStation = _gym != null ? _gym.SquatStationRoot : null;
            bool stationWasActive = squatStation != null && squatStation.gameObject.activeSelf;
            try
            {
                _cameraDirector.SetActiveLiftCameraAngle(PowerliftingCameraAngle.ThreeQuarterCompetition, true);
                _cameraDirector.enabled = true;
                yield return null;
                _cameraDirector.enabled = false;
                Render(captureDir, phase + "_gameplay", camera);

                if (squatStation != null)
                    squatStation.gameObject.SetActive(false);

                SetView(camera, focus + new Vector3(2.2f, 0.18f, 1.15f), focus, 36f);
                Render(captureDir, phase + "_side", camera);

                SetView(camera, focus + new Vector3(0f, 0.28f, -2.3f), focus + new Vector3(0f, 0.15f, 0f), 31f);
                Render(captureDir, phase + "_rear", camera);

                SetView(camera, focus + new Vector3(0.10f, 0.38f, -1.55f), focus + new Vector3(0f, 0.27f, 0f), 36f);
                Render(captureDir, phase + "_rear_close", camera);
            }
            finally
            {
                if (squatStation != null)
                    squatStation.gameObject.SetActive(stationWasActive);
                camera.transform.position = savedPosition;
                camera.transform.rotation = savedRotation;
                camera.fieldOfView = savedFov;
                _cameraDirector.enabled = wasEnabled;
                if (wasEnabled)
                    _cameraDirector.SnapToTargetImmediate();
            }
        }

        private static void SetView(Camera camera, Vector3 position, Vector3 lookAt, float fov)
        {
            camera.transform.position = position;
            camera.transform.rotation = Quaternion.LookRotation((lookAt - position).normalized, Vector3.up);
            camera.fieldOfView = fov;
        }

        private static void Render(string directory, string fileName, Camera camera)
        {
            const int width = 1600;
            const int height = 1000;
            RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = rt;
                camera.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(directory, fileName + ".png"), tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
        }

        private void WriteMeasurements(string directory)
        {
            var builder = new StringBuilder();
            builder.AppendLine("{");
            builder.AppendLine("  \"schema\": \"GAM68_BILATERAL_GRIP_EVIDENCE_V1\",");
            builder.AppendLine("  \"unityVersion\": \"" + Application.unityVersion + "\",");
            builder.AppendLine("  \"label\": \"" + Environment.GetEnvironmentVariable("GAM68_CAPTURE_LABEL") + "\",");
            builder.AppendLine("  \"samples\": [");
            for (int index = 0; index < _samples.Count; index++)
            {
                builder.Append("    ");
                builder.Append(JsonUtility.ToJson(_samples[index]));
                builder.AppendLine(index == _samples.Count - 1 ? string.Empty : ",");
            }
            builder.AppendLine("  ]");
            builder.AppendLine("}");
            File.WriteAllText(Path.Combine(directory, "measurements.json"), builder.ToString());
        }

        private IEnumerator LoadFreshSceneZero()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(0, LoadSceneMode.Single);
            while (load != null && !load.isDone)
                yield return null;
            yield return null;
            _bootstrap = UnityEngine.Object.FindFirstObjectByType<FoundationBootstrap>();
            _controller = UnityEngine.Object.FindFirstObjectByType<SquatPhysicalPrototypeController>();
            _ui = UnityEngine.Object.FindFirstObjectByType<PowerliftingGameUiController>();
            _cameraDirector = UnityEngine.Object.FindFirstObjectByType<PowerliftingCameraDirector>();
            _gym = UnityEngine.Object.FindFirstObjectByType<PowerliftingGymEnvironment>();
            Assert.That(_bootstrap, Is.Not.Null);
            Assert.That(_controller, Is.Not.Null);
            Assert.That(_ui, Is.Not.Null);
            Assert.That(_cameraDirector, Is.Not.Null);
        }

        private IEnumerator WaitForPlayerReadiness()
        {
            for (int frame = 0; frame < MaxWaitFrames && !_controller.CanBeginPlayerSetup; frame++)
                yield return new WaitForSecondsRealtime(0.01f);
            Assert.That(_controller.CanBeginPlayerSetup, Is.True, "Athlete did not reach a ready setup state.");
        }

        private void Press(UnityEngine.InputSystem.Controls.KeyControl key)
        {
            _input.currentTime = Time.realtimeSinceStartupAsDouble;
            _input.Press(key);
        }

        private void Release(UnityEngine.InputSystem.Controls.KeyControl key)
        {
            _input.currentTime = Time.realtimeSinceStartupAsDouble;
            _input.Release(key);
        }

        private static string V(Vector3 value) => string.Format(
            CultureInfo.InvariantCulture, "{0:F6},{1:F6},{2:F6}", value.x, value.y, value.z);

        private static string Q(Quaternion value) => string.Format(
            CultureInfo.InvariantCulture, "{0:F6},{1:F6},{2:F6},{3:F6}", value.x, value.y, value.z, value.w);

        [Serializable]
        private sealed class Gam68Sample
        {
            public string phase;
            public int tick;
            public float sq;
            public string state;
            public float loadKg;
            public int physicalBodyCount;
            public int physicalJointCount;
            public bool barDynamic;
            public int leftHandJointCount;
            public int rightHandJointCount;
            public int leftHandFixedJointCount;
            public int rightHandFixedJointCount;
            public string barCenter;
            public string barAxis;
            public bool leftPalmSurfaceValid;
            public bool rightPalmSurfaceValid;
            public string leftPalmSurfaceReason;
            public string rightPalmSurfaceReason;
            public string leftHandWorld;
            public string rightHandWorld;
            public string leftHandRotation;
            public string rightHandRotation;
            public string leftPalmCenter;
            public string rightPalmCenter;
            public string leftPalmNormal;
            public string rightPalmNormal;
            public float leftPalmGapM;
            public float rightPalmGapM;
            public float leftPalmNormalErrorDeg;
            public float rightPalmNormalErrorDeg;
            public float leftPalmSpanM;
            public float rightPalmSpanM;
            public string leftUpperArmPosition;
            public string rightUpperArmPosition;
            public string leftForearmPosition;
            public string rightForearmPosition;
            public string leftHandBodyPosition;
            public string rightHandBodyPosition;
            public float leftElbowFlexionDeg;
            public float rightElbowFlexionDeg;
            public float leftShoulderAbductionDeg;
            public float rightShoulderAbductionDeg;
            public float leftWristErrorDeg;
            public float rightWristErrorDeg;
            public bool liveUpperLimbActive;
            public float liveLeftHandPositionResidualM;
            public float liveRightHandPositionResidualM;
            public float liveLeftHandOrientationResidualDeg;
            public float liveRightHandOrientationResidualDeg;
            public string liveLeftHandTargetWorld;
            public string liveRightHandTargetWorld;
            public string liveLeftElbowTargetWorld;
            public string liveRightElbowTargetWorld;
            public string leftShoulderTelemetry;
            public string rightShoulderTelemetry;
            public string leftElbowTelemetry;
            public string rightElbowTelemetry;
            public string leftWristTelemetry;
            public string rightWristTelemetry;
            public string virtualBarCenter;
            public string liveBarCenter;
            public float virtualToLiveBarErrorM;
            public string barInThoraxLocal;
            public string saddleAnchorInThoraxLocal;
        }
    }
}
