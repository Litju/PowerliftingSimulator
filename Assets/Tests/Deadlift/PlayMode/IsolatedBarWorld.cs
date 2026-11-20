using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Equipment;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Foundation.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PowerliftingSimulator.Deadlift
{
    public struct BarSample
    {
        public ulong Tick;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Velocity;
        public Vector3 AngularVelocity;
        public Vector3 LeftImpulse, RightImpulse, OtherImpulse;
        public int LeftPoints, RightPoints, OtherPoints;
        public float LowestPlateVertexY;
    }

    /// <summary>
    /// GAM-70 Gate A1 test world: the production foundation runtime, the
    /// production <see cref="PhysicalBarbell"/> and a platform built from the
    /// athlete definition's own platform constants, with no athlete. Time
    /// advances only through the foundation's authoritative StepOne (two
    /// 5 ms PhysX substeps per 10 ms tick), exactly as in the games.
    ///
    /// The platform here is a parity copy of PhysicalAthleteRig's platform
    /// (same size, centre and PhysicsMaterial); the athlete-scene test
    /// asserts the parity against the real rig.
    /// </summary>
    public sealed class IsolatedBarWorld
    {
        public const double TickSeconds = 0.01;

        public FoundationBootstrap Bootstrap { get; private set; }
        public PhysicalBarbell Bar { get; private set; }
        public BoxCollider Platform { get; private set; }
        public DeadliftBarContactSensor Sensor { get; private set; }
        public List<BarSample> Samples { get; } = new List<BarSample>();
        public Rigidbody Body => Bar.Body;
        public MeshCollider LeftPlate { get; private set; }
        public MeshCollider RightPlate { get; private set; }

        public static string EvidenceDirectory
        {
            get
            {
                string env = Environment.GetEnvironmentVariable("GAM70_EVIDENCE_DIR");
                string dir = string.IsNullOrEmpty(env)
                    ? Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs", "GAM70")
                    : env;
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static void WriteEvidence(string name, string text)
        {
            File.WriteAllText(Path.Combine(EvidenceDirectory, name), text);
            Debug.Log("GAM-70 evidence " + name + ":\n" + text);
        }

        public static IsolatedBarWorld Create(float loadKg, Vector3 spawnAxisPosition, bool observationAwake = true, bool withPlatform = true, int plateSides = PhysicalBarbell.DefaultPlateColliderSides, float initialRollDeg = 0f)
        {
            var world = new IsolatedBarWorld();
            var root = new GameObject("DeadliftA1_Foundation");
            world.Bootstrap = root.AddComponent<FoundationBootstrap>();
            // Time is advanced by explicit StepOne calls only.
            world.Bootstrap.enabled = false;

            if (withPlatform)
                world.Platform = CreatePlatform(world.Bootstrap.Runtime.AuthoritativeScene);

            var barHost = new GameObject("DeadliftA1_Barbell");
            world.Bar = barHost.AddComponent<PhysicalBarbell>();
            world.Bar.SetInitialSpawnPosition(spawnAxisPosition);
            world.Bar.Build();
            world.Bar.ConfigureLoad(loadKg);
            if (plateSides != PhysicalBarbell.DefaultPlateColliderSides)
                world.Bar.ConfigurePlateColliderSides(plateSides);
            world.Bar.enabled = false; // no keyboard hooks, no IMGUI

            if (initialRollDeg != 0f)
            {
                // Authored initial orientation (roll about the bar axis), written once before tick 0.
                world.Body.rotation = Quaternion.AngleAxis(initialRollDeg, Vector3.right);
                world.Body.transform.rotation = world.Body.rotation;
            }

            if (observationAwake)
                world.Body.sleepThreshold = 0f;

            Transform left = world.Body.transform.Find("LeftPlateAggregateCollider");
            Transform right = world.Body.transform.Find("RightPlateAggregateCollider");
            world.LeftPlate = left != null ? left.GetComponent<MeshCollider>() : null;
            world.RightPlate = right != null ? right.GetComponent<MeshCollider>() : null;

            world.Sensor = world.Body.gameObject.AddComponent<DeadliftBarContactSensor>();
            world.Sensor.Ground = world.Platform;
            world.Sensor.LeftPlate = world.LeftPlate;
            world.Sensor.RightPlate = world.RightPlate;

            world.Bootstrap.Runtime.RegisterPostPhysicsStep(world.Record);
            Physics.SyncTransforms();
            return world;
        }

        private static BoxCollider CreatePlatform(Scene authoritativeScene)
        {
            var go = new GameObject("DeadliftA1_Platform");
            SceneManager.MoveGameObjectToScene(go, authoritativeScene);
            go.transform.SetPositionAndRotation(PhysicalAthleteDefinition.PlatformCenterMeters, Quaternion.identity);
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.size = PhysicalAthleteDefinition.PlatformSizeMeters;
            box.material = new PhysicsMaterial("GAM6_PlatformContact")
            {
                dynamicFriction = 0.75f,
                staticFriction = 0.85f,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Average,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
            return box;
        }

        public void Step(int ticks)
        {
            for (int i = 0; i < ticks; i++)
                Bootstrap.Runtime.StepOne();
        }

        private void Record(SimulationTime time, PhysicalObservation observation, PlayerIntentFrame intent)
        {
            Sensor.CompleteTick();
            Rigidbody b = Body;
            Samples.Add(new BarSample
            {
                Tick = time.Tick,
                Position = b.position,
                Rotation = b.rotation,
                Velocity = b.linearVelocity,
                AngularVelocity = b.angularVelocity,
                LeftImpulse = Sensor.LeftImpulse,
                RightImpulse = Sensor.RightImpulse,
                OtherImpulse = Sensor.OtherImpulse,
                LeftPoints = Sensor.LeftPoints,
                RightPoints = Sensor.RightPoints,
                OtherPoints = Sensor.OtherPoints,
                LowestPlateVertexY = LowestPlateVertexY()
            });
        }

        /// <summary>Lowest world-space vertex of the plate collider meshes (NaN with no plates).</summary>
        public float LowestPlateVertexY()
        {
            float lowest = float.PositiveInfinity;
            foreach (MeshCollider plate in new[] { LeftPlate, RightPlate })
            {
                if (plate == null || !plate.gameObject.activeInHierarchy || plate.sharedMesh == null)
                    continue;
                Matrix4x4 m = plate.transform.localToWorldMatrix;
                foreach (Vector3 v in plate.sharedMesh.vertices)
                    lowest = Mathf.Min(lowest, m.MultiplyPoint3x4(v).y);
            }
            return float.IsInfinity(lowest) ? float.NaN : lowest;
        }

        /// <summary>Axis height that leaves <paramref name="clearanceM"/> between the lowest plate vertex and the platform, for a roll angle and facet count.</summary>
        public static float AxisYForClearance(float clearanceM, float rollDeg, int sides = PhysicalBarbell.DefaultPlateColliderSides)
        {
            float step = 360f / sides;
            float delta = Mathf.Abs(Mathf.Repeat(rollDeg + step * 0.5f, step) - step * 0.5f);
            return 0.225f * Mathf.Cos(delta * Mathf.Deg2Rad) + clearanceM;
        }

        /// <summary>Roll that puts a rim facet flat on the platform (half a facet from a vertex).</summary>
        public static float FacetFlatRollDeg(int sides = PhysicalBarbell.DefaultPlateColliderSides) => 180f / sides;

        public static float PlatformTopY => PhysicalAthleteDefinition.PlatformSupportPlaneY;

        /// <summary>Angle between the bar's long axis and world X, degrees.</summary>
        public static float AxisTiltDeg(Quaternion rotation) => Vector3.Angle(rotation * Vector3.right, Vector3.right);

        /// <summary>Bar roll about its own long axis, degrees in (-180, 180]; 0 when bar-local Y is world Y.</summary>
        public static float RollAboutAxisDeg(Quaternion rotation)
        {
            Vector3 up = rotation * Vector3.up;
            return Mathf.Atan2(up.z, up.y) * Mathf.Rad2Deg;
        }

        public IEnumerator Teardown()
        {
            if (Bootstrap != null && Bootstrap.Runtime != null && Bootstrap.Runtime.IsInitialized)
            {
                AsyncOperation unload = Bootstrap.Runtime.Shutdown();
                while (unload != null && !unload.isDone)
                    yield return null;
            }
            if (Bar != null)
                UnityEngine.Object.DestroyImmediate(Bar.gameObject);
            if (Bootstrap != null)
                UnityEngine.Object.DestroyImmediate(Bootstrap.gameObject);
            yield return null;
        }

        public static string F(double value, string format = "F4") => value.ToString(format, CultureInfo.InvariantCulture);
    }
}
