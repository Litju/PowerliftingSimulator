using System.Collections;
using System.Globalization;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using H = PowerliftingSimulator.Bench.BenchTestHarness;

namespace PowerliftingSimulator.Bench
{
    /// <summary>
    /// GAM-69 bounded control for <c>Joint.currentForce</c>. Unity 6000.3
    /// documents only that the value is "relative to Joint.connectedBody if
    /// it's set, otherwise world space"; it states neither the body the force
    /// acts on nor a sign. The bench grip reads it with the bar at about
    /// identity rotation, where bar-local and world frames cannot be told
    /// apart. This control hangs a known 25 kg body from a kinematic holder
    /// with both bodies at arbitrary, different rotations, in an isolated
    /// local physics scene, and decides frame and sign by static equilibrium.
    /// </summary>
    public sealed class BenchJointForceFrameControlTests
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;
        private const float MassKg = 25f;
        private const float Dt = 0.01f;

        private struct Reading
        {
            public Vector3 Raw;
            public Vector3 RawTorque;
            public Quaternion Owner;
            public Quaternion Connected;
            public float Drift;
        }

        /// <summary>Owner = kinematic holder ("hand"), connected = hanging dynamic body ("bar").</summary>
        private static Reading Measure(Quaternion holderRotation, Quaternion hangingRotation, bool jointOnHolder)
        {
            Scene scene = SceneManager.CreateScene("GAM69_JointForceControl_" + Random.Range(0, int.MaxValue),
                new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            PhysicsScene physics = scene.GetPhysicsScene();
            try
            {
                var holder = new GameObject("holder");
                var hanging = new GameObject("hanging");
                SceneManager.MoveGameObjectToScene(holder, scene);
                SceneManager.MoveGameObjectToScene(hanging, scene);
                holder.transform.SetPositionAndRotation(new Vector3(0f, 2f, 0f), holderRotation);
                // COM 0.25 m below and 0.30 m beside the joint point: a moment as well as a force.
                hanging.transform.SetPositionAndRotation(new Vector3(0.30f, 1.75f, 0f), hangingRotation);
                Rigidbody a = holder.AddComponent<Rigidbody>();
                a.isKinematic = true;
                Rigidbody b = hanging.AddComponent<Rigidbody>();
                b.mass = MassKg;
                b.useGravity = true;
                b.linearDamping = 0f;
                b.angularDamping = 0f;
                b.centerOfMass = Vector3.zero;
                b.inertiaTensor = new Vector3(0.5f, 0.6f, 0.7f);
                b.inertiaTensorRotation = Quaternion.identity;

                Rigidbody owner = jointOnHolder ? a : b;
                Rigidbody other = jointOnHolder ? b : a;
                Vector3 pivot = new Vector3(0f, 2f, 0f);
                ConfigurableJoint joint = owner.gameObject.AddComponent<ConfigurableJoint>();
                joint.connectedBody = other;
                joint.autoConfigureConnectedAnchor = false;
                joint.anchor = owner.transform.InverseTransformPoint(pivot);
                joint.connectedAnchor = other.transform.InverseTransformPoint(pivot);
                joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Locked;
                joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Locked;
                joint.projectionMode = JointProjectionMode.None;
                joint.enableCollision = false;

                Vector3 start = b.position;
                for (int i = 0; i < 300; i++)
                    physics.Simulate(Dt);
                return new Reading
                {
                    Raw = joint.currentForce,
                    RawTorque = joint.currentTorque,
                    Owner = owner.rotation,
                    Connected = other.rotation,
                    Drift = Vector3.Distance(start, b.position)
                };
            }
            finally
            {
                SceneManager.UnloadSceneAsync(scene);
            }
        }

        [UnityTest]
        public IEnumerator GATE_B_CONTROL_JOINT_CURRENTFORCE_FRAME_AND_SIGN_BY_EQUILIBRIUM()
        {
            Quaternion qa = Quaternion.Euler(30f, 50f, 70f);
            Quaternion qb = Quaternion.Euler(-40f, 20f, 110f);
            float w = MassKg * Mathf.Abs(Physics.gravity.y);
            var cases = new (string Name, Quaternion Holder, Quaternion Hanging, bool OnHolder)[]
            {
                ("joint on holder, both rotated", qa, qb, true),
                ("joint on holder, hanging at identity", qa, Quaternion.identity, true),
                ("joint on hanging body, both rotated", qa, qb, false),
            };
            var report = new StringBuilder();
            report.AppendLine(string.Format(C, "Joint.currentForce control: {0:F2} kg hanging (weight {1:F2} N), dt {2} s, isolated physics scene", MassKg, w, Dt));
            foreach (var c in cases)
            {
                Reading r = Measure(c.Holder, c.Hanging, c.OnHolder);
                yield return null;
                // The force on the hanging body from the joint must be +W up;
                // on the kinematic holder, -W.
                Vector3 onOwner = c.OnHolder ? new Vector3(0f, -w, 0f) : new Vector3(0f, w, 0f);
                Vector3 asWorld = r.Raw;
                Vector3 asConnected = r.Connected * r.Raw;
                Vector3 asOwner = r.Owner * r.Raw;
                report.AppendLine(string.Format(C,
                    "  {0}: raw {1} |{2:F2}|, drift {3:F5} m\n    as world {4}  err-vs-onOwner {5:F2} N  err-vs-onConnected {6:F2} N\n" +
                    "    connected-local->world {7}  err-vs-onOwner {8:F2} N  err-vs-onConnected {9:F2} N\n" +
                    "    owner-local->world {10}  err-vs-onOwner {11:F2} N  err-vs-onConnected {12:F2} N\n    raw torque {13}",
                    c.Name, r.Raw.ToString("F2"), r.Raw.magnitude, r.Drift,
                    asWorld.ToString("F2"), (asWorld - onOwner).magnitude, (asWorld + onOwner).magnitude,
                    asConnected.ToString("F2"), (asConnected - onOwner).magnitude, (asConnected + onOwner).magnitude,
                    asOwner.ToString("F2"), (asOwner - onOwner).magnitude, (asOwner + onOwner).magnitude,
                    r.RawTorque.ToString("F2")));

                Assert.That(r.Drift, Is.LessThan(0.002f), "control body moved: not a static equilibrium\n" + report);
                Assert.That(r.Raw.magnitude, Is.EqualTo(w).Within(0.02f * w), "magnitude is not the carried weight\n" + report);
                // Measured contract: world frame, force on the joint's own body.
                Vector3 owner = Bench.Unity.BenchGrip.RawForceToOwnerWorld(r.Raw);
                Assert.That((owner - onOwner).magnitude, Is.LessThan(0.01f * w),
                    "currentForce is not the world-frame force on the joint's own body\n" + report);
                // Torque: moment on the joint's own body about the joint point,
                // world frame. The hanging COM sits (0.30, -0.25, 0) from the pivot.
                Vector3 r0 = new Vector3(0.30f, -0.25f, 0f);
                Vector3 onHanging = -Vector3.Cross(r0, new Vector3(0f, -w, 0f));
                Vector3 expectTorque = c.OnHolder ? -onHanging : onHanging;
                Assert.That((r.RawTorque - expectTorque).magnitude, Is.LessThan(0.01f * w),
                    "currentTorque is not the world-frame moment on the joint's own body\n" + report);
            }
            H.Write("gateB-control-joint-force-frame.txt", report.ToString());
            Debug.Log(report.ToString());
        }
    }
}
