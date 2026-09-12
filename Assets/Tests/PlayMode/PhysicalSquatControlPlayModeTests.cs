using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Equipment;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Foundation.Unity;
using PowerliftingSimulator.Squat;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.Tests
{
    public sealed class PhysicalSquatControlPlayModeTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const string EvidenceDirectory = "Artifacts/Evidence/GAM-11";
        // Deliberately generous safety envelope for H17-scale runaway detection;
        // this is not a GAM-13 heavy-load performance limit.
        private const float OutOfDomainPositionBoundMeters = 20f;
        private const string MeasurementDirectory = "Artifacts/Measurements";

        private FoundationBootstrap _bootstrap;
        private PhysicalAthleteRig _rig;
        private PhysicalBarbell _bar;
        private SquatPhysicalPrototypeController _controller;
        private Collider _platform;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return LoadQualificationScene();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
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
            _bar = null;
            _controller = null;
            _platform = null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator G1_UNLOADED_PHYSICAL_SQUAT_QUALIFICATION()
        {
            PrepareManualRuntime(0f);
            FoundationRuntime runtime = _bootstrap.Runtime;
            SquatPhysicalAdapter adapter = _controller.Adapter;
            PhysicalFootContactDetector leftFootDetector = _controller.LeftFootContact;
            PhysicalFootContactDetector rightFootDetector = _controller.RightFootContact;

            for (int i = 0; i < 30; i++)
                AdvanceTicks(runtime, 1);

            float standingPelvisY = _rig.Segments["pelvis"].Body.position.y;
            Assert.That(standingPelvisY, Is.GreaterThan(0.7f), "Standing pelvis is too low.");

            Debug.Log($"THIGH: axis={_rig.PoweredController.GetJoint("left_thigh").Joint.axis} secAxis={_rig.PoweredController.GetJoint("left_thigh").Joint.secondaryAxis} lowX={_rig.PoweredController.GetJoint("left_thigh").Joint.lowAngularXLimit.limit} highX={_rig.PoweredController.GetJoint("left_thigh").Joint.highAngularXLimit.limit}\n" +
                      $"SHANK: axis={_rig.PoweredController.GetJoint("left_shank").Joint.axis} secAxis={_rig.PoweredController.GetJoint("left_shank").Joint.secondaryAxis} lowX={_rig.PoweredController.GetJoint("left_shank").Joint.lowAngularXLimit.limit} highX={_rig.PoweredController.GetJoint("left_shank").Joint.highAngularXLimit.limit}\n" +
                      $"FOOT:  axis={_rig.PoweredController.GetJoint("left_foot").Joint.axis} secAxis={_rig.PoweredController.GetJoint("left_foot").Joint.secondaryAxis} lowX={_rig.PoweredController.GetJoint("left_foot").Joint.lowAngularXLimit.limit} highX={_rig.PoweredController.GetJoint("left_foot").Joint.highAngularXLimit.limit}");

            adapter.StartSquat();

            float minPelvisY = standingPelvisY;
            int totalTicks = Mathf.CeilToInt(8.0f / (float)SimulationConstants.FixedDeltaTimeSeconds);
            bool reachedLegalDepth = false;

            for (int tick = 0; tick < totalTicks; tick++)
            {
                AdvanceTicks(runtime, 1);
                leftFootDetector.PhysicsTickUpdate((float)SimulationConstants.FixedDeltaTimeSeconds);
                rightFootDetector.PhysicsTickUpdate((float)SimulationConstants.FixedDeltaTimeSeconds);

                float currentPelvisY = _rig.Segments["pelvis"].Body.position.y;
                float lfY = _rig.Segments["left_foot"].Body.position.y;
                float rfY = _rig.Segments["right_foot"].Body.position.y;
                if (tick % 25 == 0 || tick == totalTicks - 1 || tick == 1 || tick == 10)
                {
                    var ankleDiag = _rig.PoweredController.GetJoint("left_foot").Diagnostic;
                    var kneeDiag = _rig.PoweredController.GetJoint("left_shank").Diagnostic;
                    var hipDiag = _rig.PoweredController.GetJoint("left_thigh").Diagnostic;
                    var abdDiag = _rig.PoweredController.GetJoint("abdomen").Diagnostic;
                    Vector3 pPos = _rig.Segments["pelvis"].Body.position;
                    Vector3 tPos = _rig.Segments["thorax"].Body.position;
                    Vector3 lfPos = _rig.Segments["left_foot"].Body.position;
                    Vector3 lsPos = _rig.Segments["left_shank"].Body.position;
                    Vector3 ltPos = _rig.Segments["left_thigh"].Body.position;
                    Debug.Log($"[G1 Tick {tick:D3}] sq={adapter.Sq:F2} pelvis=({pPos.x:F3},{pPos.y:F3},{pPos.z:F3}) thorax=({tPos.x:F3},{tPos.y:F3},{tPos.z:F3}) foot=({lfPos.x:F3},{lfPos.y:F3},{lfPos.z:F3}) shank=({lsPos.x:F3},{lsPos.y:F3},{lsPos.z:F3}) apErr={adapter.ApComError:F3}\n" +
                              $"  Ankle: req={ankleDiag.RequestedTarget.eulerAngles} act={ankleDiag.ActualRelative.eulerAngles} errRad={ankleDiag.ErrorRad.x:F3}\n" +
                              $"  Knee:  req={kneeDiag.RequestedTarget.eulerAngles} act={kneeDiag.ActualRelative.eulerAngles} errRad={kneeDiag.ErrorRad.x:F3}\n" +
                              $"  Hip:   req={hipDiag.RequestedTarget.eulerAngles} act={hipDiag.ActualRelative.eulerAngles} errRad={hipDiag.ErrorRad.x:F3}\n" +
                              $"  Abd:   req={abdDiag.RequestedTarget.eulerAngles} act={abdDiag.ActualRelative.eulerAngles} errRad={abdDiag.ErrorRad.x:F3}");
                }
                if (currentPelvisY < minPelvisY)
                    minPelvisY = currentPelvisY;

                if (adapter.State == SquatState.BOTTOM || adapter.Sq >= 0.5f)
                {
                    if (standingPelvisY - currentPelvisY >= 0.25f)
                        reachedLegalDepth = true;
                }

                Assert.That(_rig.Segments["pelvis"].Body.isKinematic, Is.False);
                Assert.That(lfY, Is.LessThan(0.45f), $"Left foot lifted at tick {tick}: {lfY:F3}");
                Assert.That(rfY, Is.LessThan(0.45f), $"Right foot lifted at tick {tick}: {rfY:F3}");
            }

            Assert.That(reachedLegalDepth, Is.True, $"Squat did not reach legal depth: standingPelvisY={standingPelvisY:F3}, minPelvisY={minPelvisY:F3}");

            float finalPelvisY = _rig.Segments["pelvis"].Body.position.y;
            Assert.That(standingPelvisY - finalPelvisY, Is.LessThan(0.12f),
                $"Did not recover to lockout height: standing={standingPelvisY:F3}, final={finalPelvisY:F3}");

            Assert.That(leftFootDetector.SlipAccumulatedM, Is.LessThan(0.08f), "Left foot slipped excessively.");
            Assert.That(rightFootDetector.SlipAccumulatedM, Is.LessThan(0.08f), "Right foot slipped excessively.");

            CaptureEvidence("GAM-11-unloaded-squat-lockout.png");
            yield return null;
        }

        [UnityTest]
        public IEnumerator G2_BARBELL_25KG_PHYSICAL_SQUAT_QUALIFICATION()
        {
            PrepareManualRuntime(25f);
            FoundationRuntime runtime = _bootstrap.Runtime;
            SquatBarSaddle saddle = _controller.Saddle;
            SquatPhysicalAdapter adapter = _controller.Adapter;
            Assert.That(_bar.LoadedMassKg, Is.EqualTo(25f).Within(0.0001f));

            for (int i = 0; i < 30; i++)
                AdvanceTicks(runtime, 1);

            float standingBarY = _bar.Body.position.y;
            Assert.That(saddle.IsAttached, Is.True);

            adapter.StartSquat();
            int totalTicks = Mathf.CeilToInt(8.0f / (float)SimulationConstants.FixedDeltaTimeSeconds);
            float minBarY = standingBarY;

            for (int tick = 0; tick < totalTicks; tick++)
            {
                AdvanceTicks(runtime, 1);

                float currentBarY = _bar.Body.position.y;
                float currentPelvisY = _rig.Segments["pelvis"].Body.position.y;
                if (tick % 25 == 0 || tick == totalTicks - 1)
                {
                    Debug.Log($"[G2 Tick {tick:D3}] sq={adapter.Sq:F2} state={adapter.State} barY={currentBarY:F3} pelvisY={currentPelvisY:F3} apErr={adapter.ApComError:F3} sep={saddle.SaddleSeparationMeters:F3}");
                }
                if (currentBarY < minBarY)
                    minBarY = currentBarY;

                Assert.That(saddle.IsAttached, Is.True, "Bar saddle detached during 25kg squat!");
                Assert.That(_bar.Body.isKinematic, Is.False, "Barbell must remain dynamic.");
            }

            Assert.That(standingBarY - minBarY, Is.GreaterThan(0.20f), "Bar did not descend sufficiently during squat.");
            float finalBarY = _bar.Body.position.y;
            Assert.That(standingBarY - finalBarY, Is.LessThan(0.12f), "Bar did not return to standing lockout height.");

            CaptureEvidence("GAM-11-25kg-squat-lockout.png");
            yield return null;
        }

        [UnityTest]
        public IEnumerator GAM11_105KG_OUT_OF_DOMAIN_STRESS_REGRESSION()
        {
            PrepareManualRuntime(105f);
            FoundationRuntime runtime = _bootstrap.Runtime;
            SquatBarSaddle saddle = _controller.Saddle;
            SquatPhysicalAdapter adapter = _controller.Adapter;
            Assert.That(_bar.LoadedMassKg, Is.EqualTo(105f).Within(0.0001f));
            Assert.That(_bar.Body.isKinematic, Is.False, "The stress case requires the production dynamic barbell.");

            for (int i = 0; i < 30; i++)
                AdvanceTicks(runtime, 1);

            adapter.StartSquat();
            int totalTicks = Mathf.CeilToInt(8.0f / (float)SimulationConstants.FixedDeltaTimeSeconds);

            for (int tick = 0; tick < totalTicks; tick++)
            {
                AdvanceTicks(runtime, 1);

                AssertFiniteStressState(_rig, _bar);
                AssertFiniteControllerOutputs(_rig.PoweredController);

                if (tick % 25 == 0 || tick == totalTicks - 1)
                {
                    float pelvisY = _rig.Segments["pelvis"].Body.position.y;
                    Debug.Log($"[GAM11 105KG stress tick {tick:D3}] sq={adapter.Sq:F2} state={adapter.State} barY={_bar.Body.position.y:F3} pelvisY={pelvisY:F3} apErr={adapter.ApComError:F3} sep={saddle.SaddleSeparationMeters:F3} attached={saddle.IsAttached}");
                }
            }

            Debug.Log($"[GAM11 105KG stress result] finite bounded state within {OutOfDomainPositionBoundMeters:F0}m envelope; terminalState={adapter.State}; saddleAttached={saddle.IsAttached}; 105kg performance is not asserted by GAM-11.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator G4_MUTATION_GATES_REJECT_FORBIDDEN_AUTHORITY()
        {
            PrepareManualRuntime(105f);
            FoundationRuntime runtime = _bootstrap.Runtime;

            // M1: Kinematic pelvis must be rejected by runtime validation
            _rig.Segments["pelvis"].Body.isKinematic = true;
            Exception kinematicPelvisError = InvokeRuntimeValidation(_rig);
            _rig.Segments["pelvis"].Body.isKinematic = false;
            Assert.That(kinematicPelvisError, Is.Not.Null, "Kinematic pelvis was not rejected by runtime validation.");

            // M2: Zero drives collapse test under 105kg
            SquatBarSaddle saddle = _controller.Saddle;

            _rig.StartZeroActivation();
            float initialBarY = _bar.Body.position.y;
            for (int i = 0; i < 60; i++)
                AdvanceTicks(runtime, 1);

            float collapsedBarDrop = initialBarY - _bar.Body.position.y;
            Assert.That(collapsedBarDrop, Is.GreaterThan(0.30f), "Athlete did not collapse when drives were zeroed.");
            saddle.BreakSaddle();

            // M3: Saddle break test (excessive force)
            _controller.SetLoad(25f);
            SquatBarSaddle weakSaddle = _controller.Saddle;
            weakSaddle.Joint.breakForce = 10f;
            weakSaddle.Joint.breakTorque = 10f;
            _bar.Body.AddForce(Vector3.down * 5000f, ForceMode.Impulse);
            AdvanceTicks(runtime, 10);
            Assert.That(weakSaddle.IsBroken, Is.True, "Weak saddle joint failed to break under high impulse.");

            yield return null;
        }

        private static void AdvanceTicks(FoundationRuntime runtime, int count)
        {
            for (int index = 0; index < count; index++)
                Assert.That(runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds), Is.EqualTo(1));
        }

        private static void AssertFiniteStressState(PhysicalAthleteRig rig, PhysicalBarbell bar)
        {
            AssertFiniteBody("barbell", bar.Body);
            foreach (var segment in rig.Segments)
                AssertFiniteBody(segment.Key, segment.Value.Body);
        }

        private static void AssertFiniteBody(string label, Rigidbody body)
        {
            Assert.That(body, Is.Not.Null, $"{label} body is missing during the stress run.");
            Assert.That(PoweredJointController.IsFinite(body.position), Is.True, $"{label} position became non-finite.");
            Assert.That(PoweredJointController.IsFinite(body.rotation), Is.True, $"{label} rotation became non-finite.");
            Assert.That(PoweredJointController.IsFinite(body.linearVelocity), Is.True, $"{label} linear velocity became non-finite.");
            Assert.That(PoweredJointController.IsFinite(body.angularVelocity), Is.True, $"{label} angular velocity became non-finite.");
            Assert.That(body.position.sqrMagnitude, Is.LessThan(OutOfDomainPositionBoundMeters * OutOfDomainPositionBoundMeters),
                $"{label} exceeded the GAM-11 out-of-domain safety envelope.");
        }

        private static void AssertFiniteControllerOutputs(PoweredJointController controller)
        {
            foreach (PoweredJointController.PoweredJointRuntime joint in controller.Joints)
            {
                string label = $"joint '{joint.Id}'";
                Assert.That(PoweredJointController.IsFinite(joint.RequestedCommand.TargetRelativeRotation), Is.True,
                    $"{label} requested target became non-finite.");
                Assert.That(PoweredJointController.IsFinite(joint.RequestedCommand.TargetRelativeAngularVelocityRadS), Is.True,
                    $"{label} requested rate became non-finite.");
                Assert.That(PoweredJointController.IsFinite(joint.Joint.targetRotation), Is.True,
                    $"{label} applied target became non-finite.");
                Assert.That(PoweredJointController.IsFinite(joint.Joint.targetAngularVelocity), Is.True,
                    $"{label} applied rate became non-finite.");
                Assert.That(PoweredJointController.IsValidPoweredDrive(joint.Joint.angularXDrive), Is.True,
                    $"{label} X drive authority became invalid.");
                Assert.That(PoweredJointController.IsValidPoweredDrive(joint.Joint.angularYZDrive), Is.True,
                    $"{label} YZ drive authority became invalid.");
                Assert.That(PoweredJointController.IsValidPoweredDrive(joint.Joint.slerpDrive), Is.True,
                    $"{label} Slerp drive authority became invalid.");
                if (joint.Profile.HasValue)
                    Assert.That(float.IsFinite(joint.Diagnostic.ModeledDemand), Is.True,
                        $"{label} modeled demand became non-finite.");
            }
        }

        private IEnumerator LoadQualificationScene()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(QualificationScene, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null, "The qualification scene is missing from the project.");
            while (!load.isDone)
                yield return null;
            yield return null;

            _bootstrap = UnityEngine.Object.FindFirstObjectByType<FoundationBootstrap>();
            _rig = UnityEngine.Object.FindFirstObjectByType<PhysicalAthleteRig>();
            _bar = UnityEngine.Object.FindFirstObjectByType<PhysicalBarbell>();
            _controller = UnityEngine.Object.FindFirstObjectByType<SquatPhysicalPrototypeController>();
            _platform = GameObject.Find("PhysicalPlatform_GAM6")?.GetComponent<Collider>();

            Assert.That(_bootstrap, Is.Not.Null);
            Assert.That(_bootstrap.Runtime, Is.Not.Null);
            Assert.That(_bootstrap.Runtime.IsInitialized, Is.True);
            Assert.That(_rig, Is.Not.Null);
            Assert.That(_bar, Is.Not.Null);
            Assert.That(_controller, Is.Not.Null);
            for (int frame = 0; frame < 5 && !_controller.IsInitialized; frame++)
                yield return null;
            Assert.That(_controller.IsInitialized, Is.True, _controller.StartupFailure);
            Assert.That(_controller.RuntimeWired, Is.True, "Production GAM-11 runtime is not fully wired.");
            Assert.That(_platform, Is.Not.Null);
        }

        private void PrepareManualRuntime(float loadKg)
        {
            Assert.That(_controller, Is.Not.Null);
            _controller.SetLoad(loadKg);
            Assert.That(_controller.Adapter, Is.Not.Null);
            if (loadKg > 0f)
                Assert.That(_controller.Saddle, Is.Not.Null);

            // The test advances the same production runtime explicitly so the
            // assertions observe deterministic 100 Hz ticks without a second
            // gameplay loop or test-only adapter/saddle construction.
            _controller.enabled = false;
            _bootstrap.enabled = false;
            _rig.enabled = false;
            _bar.enabled = false;
        }

        private static Exception InvokeRuntimeValidation(PhysicalAthleteRig rig)
        {
            MethodInfo validator = typeof(PhysicalAthleteRig).GetMethod("ValidateRuntime", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(validator, Is.Not.Null);
            try
            {
                validator.Invoke(rig, null);
                return null;
            }
            catch (TargetInvocationException exception)
            {
                return exception.InnerException ?? exception;
            }
        }

        private static void CaptureEvidence(string filename)
        {
            // Batch qualification runs use Unity's null graphics device. The
            // owner-facing screenshots are captured from the real editor
            // scene; avoid turning a successful physics gate into a render
            // backend error in headless CI.
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                return;

            Camera camera = Camera.main;
            if (camera == null)
                return;

            string directory = Path.GetFullPath(EvidenceDirectory);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, filename);

            RenderTexture texture = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32);
            Texture2D image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            try
            {
                camera.targetTexture = texture;
                camera.Render();
                RenderTexture.active = texture;
                image.ReadPixels(new Rect(0f, 0f, 1280f, 720f), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(texture);
                UnityEngine.Object.DestroyImmediate(image);
            }
        }
    }
}
