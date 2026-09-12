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
    /// The ground-registration invariant GAM-11 discovered. GAM-6 and GAM-9
    /// qualified topology, finite authority, fall behaviour, contact and reset;
    /// none of them required the athlete to be standing on anything. GAM-11
    /// does, so the registration between the canonical sole, the foot collider
    /// and the platform is asserted here.
    /// </summary>
    public sealed class PhysicalGroundRegistrationTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const string MeasurementDirectory = "Artifacts/Measurements";
        private const float RegistrationToleranceM = 0.001f;

        private FoundationBootstrap _bootstrap;
        private PhysicalAthleteRig _rig;
        private SquatPhysicalPrototypeController _controller;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(QualificationScene, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
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

        [Test]
        public void GR1_CANONICAL_PLANTAR_TO_COLLIDER_REGISTRATION()
        {
            _controller.SetLoad(0f);
            SquatPhysicalAdapter adapter = _controller.Adapter;
            float leftPlantarY = adapter.LeftReferencePlantarAnchorWorld.y;
            float rightPlantarY = adapter.RightReferencePlantarAnchorWorld.y;
            float leftSole = _rig.Segments["left_foot"].Collider.bounds.min.y;
            float rightSole = _rig.Segments["right_foot"].Collider.bounds.min.y;

            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[GR1] leftPlantar={0:F5} leftSole={1:F5} err={2:F5} | rightPlantar={3:F5} rightSole={4:F5} err={5:F5}",
                leftPlantarY, leftSole, leftSole - leftPlantarY,
                rightPlantarY, rightSole, rightSole - rightPlantarY));

            Assert.That(leftSole, Is.EqualTo(leftPlantarY).Within(RegistrationToleranceM),
                "The left foot collider sole is not seated on the canonical plantar anchor.");
            Assert.That(rightSole, Is.EqualTo(rightPlantarY).Within(RegistrationToleranceM),
                "The right foot collider sole is not seated on the canonical plantar anchor.");
        }

        [Test]
        public void GR2_CANONICAL_RIG_TO_PLATFORM_REGISTRATION()
        {
            _controller.SetLoad(0f);
            SquatPhysicalAdapter adapter = _controller.Adapter;
            float platform = PhysicalAthleteDefinition.PlatformSupportPlaneY;

            Assert.That(_rig.CanonicalPlantarPlaneY.HasValue, Is.True,
                "The rig was built without ground registration.");
            Assert.That(adapter.LeftReferencePlantarAnchorWorld.y, Is.EqualTo(platform).Within(RegistrationToleranceM),
                "The canonical left plantar plane is not registered to the platform support plane.");
            Assert.That(adapter.RightReferencePlantarAnchorWorld.y, Is.EqualTo(platform).Within(RegistrationToleranceM),
                "The canonical right plantar plane is not registered to the platform support plane.");
        }

        [Test]
        public void GR3_NO_AUTHORED_SPAWN_GAP_OR_PENETRATION()
        {
            _controller.SetLoad(0f);
            float platform = PhysicalAthleteDefinition.PlatformSupportPlaneY;
            float leftGap = _rig.Segments["left_foot"].Collider.bounds.min.y - platform;
            float rightGap = _rig.Segments["right_foot"].Collider.bounds.min.y - platform;

            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[GR3] leftGap={0:F5} m rightGap={1:F5} m registrationOffset={2:F5} m",
                leftGap, rightGap, _rig.GroundRegistrationOffsetMeters));

            // Both directions matter. A gap is a free fall; a penetration is a
            // hidden spring that would fake the support we are trying to earn.
            Assert.That(Mathf.Abs(leftGap), Is.LessThanOrEqualTo(RegistrationToleranceM),
                $"Left sole to platform is {leftGap * 1000f:F2} mm.");
            Assert.That(Mathf.Abs(rightGap), Is.LessThanOrEqualTo(RegistrationToleranceM),
                $"Right sole to platform is {rightGap * 1000f:F2} mm.");
        }

        [Test]
        public void GR4_FOOT_MASS_PROPERTIES_ARE_AUTHORED_NOT_DERIVED()
        {
            // Moving a collider changes automatically derived mass properties.
            // These are authored explicitly, which is why the registration is
            // safe; assert that rather than assume it.
            foreach (string footId in new[] { "left_foot", "right_foot" })
            {
                Rigidbody body = _rig.Segments[footId].Body;
                Vector3 dimensions = _rig.Segments[footId].DimensionsMeters;
                Vector3 expectedInertia = PhysicalAthleteDefinition.BoxInertia(body.mass, dimensions);

                Assert.That(body.centerOfMass.magnitude, Is.LessThan(1e-4f),
                    $"{footId} centre of mass drifted from the authored body origin.");
                Assert.That(body.inertiaTensor.x, Is.EqualTo(expectedInertia.x).Within(1e-4f), footId);
                Assert.That(body.inertiaTensor.y, Is.EqualTo(expectedInertia.y).Within(1e-4f), footId);
                Assert.That(body.inertiaTensor.z, Is.EqualTo(expectedInertia.z).Within(1e-4f), footId);
                Assert.That(Quaternion.Angle(body.inertiaTensorRotation, Quaternion.identity), Is.LessThan(0.01f), footId);
                Assert.That(body.mass, Is.EqualTo(PhysicalAthleteDefinition.PrototypeBodyMassKg * 0.0145f).Within(1e-4f), footId);
            }
        }

        [UnityTest]
        public IEnumerator GR5_NO_BALLISTIC_STARTUP()
        {
            _controller.SetLoad(0f);
            SquatPhysicalAdapter adapter = _controller.Adapter;
            adapter.BalanceCorrectionsEnabled = false;
            adapter.Preload.Enabled = false;

            float platform = PhysicalAthleteDefinition.PlatformSupportPlaneY;
            float initialPelvisY = _rig.Segments["pelvis"].Body.position.y;
            float preSimLeftGap = _rig.Segments["left_foot"].Collider.bounds.min.y - platform;
            float preSimRightGap = _rig.Segments["right_foot"].Collider.bounds.min.y - platform;

            var trace = new StringBuilder();
            trace.AppendLine("tick,pelvis_y,left_sole_y,right_sole_y,left_foot_vel_y,contacts");

            int firstContactTick = -1;
            float peakDownwardSpeed = 0f;

            for (int tick = 0; tick <= 20; tick++)
            {
                trace.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0},{1:F5},{2:F5},{3:F5},{4:F5},{5}",
                    tick, _rig.Segments["pelvis"].Body.position.y,
                    _rig.Segments["left_foot"].Collider.bounds.min.y,
                    _rig.Segments["right_foot"].Collider.bounds.min.y,
                    _rig.Segments["left_foot"].Body.linearVelocity.y,
                    adapter.Balance.SupportContactCount));

                if (firstContactTick < 0 && adapter.Balance.SupportContactCount > 0)
                    firstContactTick = tick;
                if (firstContactTick < 0)
                {
                    peakDownwardSpeed = Mathf.Max(peakDownwardSpeed,
                        -_rig.Segments["left_foot"].Body.linearVelocity.y);
                }

                _bootstrap.Runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                if (_controller.LeftFootContact != null)
                    _controller.LeftFootContact.PhysicsTickUpdate((float)SimulationConstants.FixedDeltaTimeSeconds);
                if (_controller.RightFootContact != null)
                    _controller.RightFootContact.PhysicsTickUpdate((float)SimulationConstants.FixedDeltaTimeSeconds);
            }

            float pelvisSagMm = (initialPelvisY - _rig.Segments["pelvis"].Body.position.y) * 1000f;
            string summary = string.Format(CultureInfo.InvariantCulture,
                "preSimGap left={0:F2} mm right={1:F2} mm firstContactTick={2} peakPreContactDownwardSpeed={3:F4} m/s pelvisSag20Ticks={4:F2} mm",
                preSimLeftGap * 1000f, preSimRightGap * 1000f, firstContactTick, peakDownwardSpeed, pelvisSagMm);
            Directory.CreateDirectory(Path.GetFullPath(MeasurementDirectory));
            File.WriteAllText(
                Path.Combine(Path.GetFullPath(MeasurementDirectory), "GAM11-first-contact-qualification.csv"),
                trace.ToString());
            Debug.Log("[GR5 NO BALLISTIC STARTUP] " + summary + Environment.NewLine + trace);

            // The condition is that no authored air gap is crossed before
            // support exists, not that a contact callback fires before the
            // first Simulate.
            Assert.That(firstContactTick, Is.LessThanOrEqualTo(2),
                "The athlete took more than two ticks to find the ground. " + summary);
            Assert.That(peakDownwardSpeed, Is.LessThan(0.10f),
                "The athlete built up ballistic speed before contact. " + summary);
            yield return null;
        }
    }
}
