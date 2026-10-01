using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PowerliftingSimulator.PhysicsBenchmarks
{
    /// <summary>
    /// A private PhysX scene for engine-level benchmarks. Nothing in it is
    /// stepped by Unity: every tick is an explicit PhysicsScene.Simulate at
    /// the benchmark's own timestep, the same ownership model the production
    /// AuthoritativePhysicsScene uses, so a benchmark measures PhysX and not
    /// the player loop.
    /// </summary>
    public sealed class IsolatedPhysicsWorld : IDisposable
    {
        private static int s_sequence;
        private readonly List<GameObject> _objects = new List<GameObject>();
        private Scene _scene;
        private PhysicsScene _physics;

        public IsolatedPhysicsWorld(string name, float dt, int positionIterations, int velocityIterations)
        {
            if (!(dt > 0f))
                throw new ArgumentOutOfRangeException(nameof(dt));
            Dt = dt;
            PositionIterations = positionIterations;
            VelocityIterations = velocityIterations;
            _scene = SceneManager.CreateScene(
                $"PhysicsBenchmark_{name}_{s_sequence++}",
                new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            _physics = _scene.GetPhysicsScene();
            if (!_physics.IsValid())
                throw new InvalidOperationException("Could not create an isolated physics scene.");
        }

        public float Dt { get; }
        public int PositionIterations { get; }
        public int VelocityIterations { get; }
        public int Tick { get; private set; }
        public double Time => Tick * (double)Dt;

        public string Describe() => string.Format(CultureInfo.InvariantCulture,
            "dt={0:R};pos_iter={1};vel_iter={2}", Dt, PositionIterations, VelocityIterations);

        public GameObject CreateObject(string name, Vector3 position, Quaternion rotation)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, _scene);
            go.transform.SetPositionAndRotation(position, rotation);
            _objects.Add(go);
            return go;
        }

        /// <summary>A static (non-Rigidbody) box collider.</summary>
        public BoxCollider CreateStaticBox(string name, Vector3 size, Vector3 position, Quaternion rotation, PhysicsMaterial material = null)
        {
            GameObject go = CreateObject(name, position, rotation);
            var box = go.AddComponent<BoxCollider>();
            box.size = size;
            box.sharedMaterial = material;
            return box;
        }

        /// <summary>
        /// A dynamic or kinematic body with explicit mass properties. The
        /// inertia is set, never auto-computed, so the analytic expectation
        /// and the engine see the same number.
        /// </summary>
        public Rigidbody CreateBody(
            string name,
            Vector3 position,
            Quaternion rotation,
            float massKg,
            Vector3 inertiaKgM2,
            bool kinematic = false,
            bool gravity = true,
            Vector3? colliderBoxSize = null,
            PhysicsMaterial material = null)
        {
            GameObject go = CreateObject(name, position, rotation);
            if (colliderBoxSize.HasValue)
            {
                var box = go.AddComponent<BoxCollider>();
                box.size = colliderBoxSize.Value;
                box.sharedMaterial = material;
            }
            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = kinematic;
            body.useGravity = gravity;
            body.automaticCenterOfMass = false;
            body.automaticInertiaTensor = false;
            body.mass = massKg;
            body.centerOfMass = Vector3.zero;
            body.inertiaTensor = inertiaKgM2;
            body.inertiaTensorRotation = Quaternion.identity;
            body.linearDamping = 0f;
            body.angularDamping = 0f;
            body.maxAngularVelocity = 1000f;
            body.maxDepenetrationVelocity = 1000f;
            body.interpolation = RigidbodyInterpolation.None;
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            body.sleepThreshold = 0f;
            body.solverIterations = PositionIterations;
            body.solverVelocityIterations = VelocityIterations;
            return body;
        }

        public static PhysicsMaterial Material(string name, float staticFriction, float dynamicFriction,
            PhysicsMaterialCombine frictionCombine = PhysicsMaterialCombine.Maximum) =>
            new PhysicsMaterial(name)
            {
                staticFriction = staticFriction,
                dynamicFriction = dynamicFriction,
                bounciness = 0f,
                frictionCombine = frictionCombine,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };

        /// <summary>
        /// A ConfigurableJoint hinge about the child's local +X with every
        /// linear axis and both swing axes locked; the anchor is given in the
        /// child's local frame and the connected anchor is derived from the
        /// current pose so the joint starts with zero separation.
        /// </summary>
        public static ConfigurableJoint Hinge(Rigidbody child, Rigidbody parent, Vector3 childLocalAnchor, bool freeTwist = true)
        {
            var joint = child.gameObject.AddComponent<ConfigurableJoint>();
            joint.connectedBody = parent;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = childLocalAnchor;
            Vector3 worldAnchor = child.transform.TransformPoint(childLocalAnchor);
            joint.connectedAnchor = parent != null ? parent.transform.InverseTransformPoint(worldAnchor) : worldAnchor;
            joint.axis = Vector3.right;
            joint.secondaryAxis = Vector3.up;
            joint.xMotion = ConfigurableJointMotion.Locked;
            joint.yMotion = ConfigurableJointMotion.Locked;
            joint.zMotion = ConfigurableJointMotion.Locked;
            joint.angularXMotion = freeTwist ? ConfigurableJointMotion.Free : ConfigurableJointMotion.Locked;
            joint.angularYMotion = ConfigurableJointMotion.Locked;
            joint.angularZMotion = ConfigurableJointMotion.Locked;
            joint.rotationDriveMode = RotationDriveMode.XYAndZ;
            joint.projectionMode = JointProjectionMode.None;
            joint.enableCollision = false;
            joint.enablePreprocessing = true;
            return joint;
        }

        public static JointDrive Drive(float spring, float damper, float maximumForce) => new JointDrive
        {
            positionSpring = spring,
            positionDamper = damper,
            maximumForce = maximumForce,
            useAcceleration = false
        };

        public void Step()
        {
            _physics.Simulate(Dt);
            Tick++;
        }

        public void Step(int ticks)
        {
            for (int i = 0; i < ticks; i++)
                Step();
        }

        /// <summary>Signed rotation of the child about its own local +X relative to its start, radians.</summary>
        public static float TwistAboutLocalX(Quaternion start, Quaternion current, Quaternion parentStart, Quaternion parentCurrent)
        {
            Quaternion relStart = Quaternion.Inverse(parentStart) * start;
            Quaternion relNow = Quaternion.Inverse(parentCurrent) * current;
            Quaternion delta = Quaternion.Inverse(relStart) * relNow;
            delta.Normalize();
            if (delta.w < 0f)
                delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w);
            return 2f * Mathf.Atan2(delta.x, delta.w);
        }

        public void Dispose()
        {
            foreach (GameObject go in _objects)
            {
                if (go != null)
                    UnityEngine.Object.DestroyImmediate(go);
            }
            _objects.Clear();
            if (_scene.IsValid() && _scene.isLoaded)
                SceneManager.UnloadSceneAsync(_scene);
        }
    }
}
