using System.Collections;
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
    public sealed class GAM48ObservationAuthorityTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private FoundationBootstrap _bootstrap;
        private PhysicalAthleteRig _rig;
        private SquatPhysicalPrototypeController _controller;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(QualificationScene, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null, "The GAM-48 qualification scene is missing.");
            while (!load.isDone)
                yield return null;
            yield return null;

            _bootstrap = Object.FindFirstObjectByType<FoundationBootstrap>();
            _rig = Object.FindFirstObjectByType<PhysicalAthleteRig>();
            _controller = Object.FindFirstObjectByType<SquatPhysicalPrototypeController>();
            Assert.That(_bootstrap, Is.Not.Null);
            Assert.That(_rig, Is.Not.Null);
            Assert.That(_controller, Is.Not.Null);
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
                Object.DestroyImmediate(_bootstrap.gameObject);
            _bootstrap = null;
            _rig = null;
            _controller = null;
            yield return null;
            LogAssert.ignoreFailingMessages = false;
        }

        [UnityTest]
        public IEnumerator GAM48_GATE1_LOAD_RESET_PRESERVES_PLANTED_CONTACT()
        {
            _controller.SetLoad(0f);
            AdvanceTicks(40);
            AssertSupportObservation("before load reset");

            _controller.SetLoad(25f);

            AssertPersistentContact("after load reset");
            AdvanceTicks(5);
            Assert.That(_controller.Adapter.Balance.HasSupport, Is.True,
                "Aggregate support disappeared after a load reset while both feet remained planted.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator GAM48_GATE1_STATIC_CONTACT_SURVIVES_BUFFER_CLEAR()
        {
            _controller.SetLoad(0f);
            AdvanceTicks(40);
            AssertSupportObservation("before static hold");

            _rig.Segments["left_foot"].Body.Sleep();
            _rig.Segments["right_foot"].Body.Sleep();
            _controller.LeftFootContact.ResetContact();
            _controller.RightFootContact.ResetContact();

            AssertPersistentContact("after static buffer reset");
            Assert.That(_controller.LeftFootContact.CompletedContactCount, Is.EqualTo(0));
            Assert.That(_controller.RightFootContact.CompletedContactCount, Is.EqualTo(0));

            AdvanceTicks(2);
            Assert.That(_controller.Adapter.Balance.HasSupport, Is.True,
                "Support authority depended on a newly populated per-step manifold buffer.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator GAM48_GATE1_CONTACT_LOSS_AND_REENTRY_UPDATE_PERSISTENT_AUTHORITY()
        {
            _controller.SetLoad(0f);
            AdvanceTicks(40);
            Assert.That(_controller.LeftFootContact.IsInContact, Is.True);

            Rigidbody leftBody = _rig.Segments["left_foot"].Body;
            leftBody.detectCollisions = false;
            Physics.SyncTransforms();
            AdvanceTicks(3);
            Assert.That(_controller.LeftFootContact.IsInContact, Is.False,
                "Disabling the planted foot collision detection did not remove its active platform pair.");

            leftBody.detectCollisions = true;
            Physics.SyncTransforms();
            AdvanceTicks(5);
            Assert.That(_controller.LeftFootContact.IsInContact, Is.True,
                "Re-enabling the planted foot collision detection did not restore its active platform pair.");
            Assert.That(_controller.Adapter.Balance.HasSupport, Is.True,
                "Bilateral support did not recover after contact re-entry.");
            yield return null;
        }

        private void AssertSupportObservation(string boundary)
        {
            Assert.That(_controller.Adapter.Balance.HasSupport, Is.True,
                "Aggregate support was not observed " + boundary + ".");
            Assert.That(_controller.Adapter.Balance.SupportContactCount, Is.GreaterThan(0), boundary);
        }

        private void AssertPersistentContact(string boundary)
        {
            Assert.That(_controller.LeftFootContact, Is.Not.Null, boundary);
            Assert.That(_controller.RightFootContact, Is.Not.Null, boundary);
            Assert.That(_controller.LeftFootContact.IsInContact, Is.True,
                "Left foot lost persistent contact " + boundary + ".");
            Assert.That(_controller.RightFootContact.IsInContact, Is.True,
                "Right foot lost persistent contact " + boundary + ".");
        }

        private void AdvanceTicks(int count)
        {
            for (int tick = 0; tick < count; tick++)
            {
                Assert.That(
                    _bootstrap.Runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds),
                    Is.EqualTo(1));
                _controller.LeftFootContact?.PhysicsTickUpdate((float)SimulationConstants.FixedDeltaTimeSeconds);
                _controller.RightFootContact?.PhysicsTickUpdate((float)SimulationConstants.FixedDeltaTimeSeconds);
            }
        }
    }
}
