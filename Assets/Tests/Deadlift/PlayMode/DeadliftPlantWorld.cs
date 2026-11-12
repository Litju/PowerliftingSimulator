using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Bench.Unity;
using PowerliftingSimulator.Deadlift.Unity;
using PowerliftingSimulator.Equipment;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Foundation.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PowerliftingSimulator.Deadlift
{
    public struct PlantSample
    {
        public ulong Tick;
        public double LeftFootN, RightFootN, BarFloorN, BarOtherN;
        public Vector3 Com;
        public Vector3 BarPosition;
        public Vector3 BarVelocity;
        public Vector3 BarAngularVelocity;
        public float BarTiltDeg;
        public float BarSpeed;
        public float MaxBodyDriftM;
        public float MaxJointErrorDeg;
        public float MaxDemand;
        public string MaxDemandJoint;
        public float LeftGripSeparationM, RightGripSeparationM;
        public Vector3 LeftGripForceRaw, RightGripForceRaw;
    }

    /// <summary>
    /// GAM-70 Gate A2/A3 test world: the production scene with the canonical
    /// 16-body athlete, the production barbell and the platform the rig
    /// builds, advanced only by the foundation's StepOne. The athlete is
    /// placed once, before tick zero, at the solved setup pose; from tick one
    /// every body moves by solver forces only.
    /// </summary>
    public sealed class DeadliftPlantWorld
    {
        public const string SceneName = "PhysicalAthletePhysics";
        public static DeadliftPlantWorld Current { get; private set; }

        public FoundationBootstrap Foundation { get; private set; }
        public PhysicalAthleteRig Rig { get; private set; }
        public PhysicalBarbell Bar { get; private set; }
        public DeadliftSetupSolver Solver { get; private set; }
        public DeadliftPlantModel Model { get; private set; }
        public DeadliftSetupHold Hold { get; private set; }
        public BenchGrip LeftGrip { get; private set; }
        public BenchGrip RightGrip { get; private set; }
        public readonly Dictionary<string, BenchContactSensor> Sensors = new Dictionary<string, BenchContactSensor>(StringComparer.Ordinal);
        public readonly List<PlantSample> Samples = new List<PlantSample>();
        public readonly List<string> TickZeroSelfPenetrations = new List<string>();
        public float TickZeroMaxPenetrationM;
        public float TickZeroMaxGripSeparationM;
        private Dictionary<string, Pose> _placed;

        public static IEnumerator Load()
        {
            void OnLoaded(Scene scene, LoadSceneMode mode)
            {
                // Awake has run, Start and the first Update have not: stop the render-clock before it can tick.
                foreach (FoundationBootstrap b in UnityEngine.Object.FindObjectsByType<FoundationBootstrap>(FindObjectsSortMode.None))
                    b.enabled = false;
            }

            SceneManager.sceneLoaded += OnLoaded;
            AsyncOperation load = SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            while (!load.isDone)
                yield return null;
            SceneManager.sceneLoaded -= OnLoaded;

            var world = new DeadliftPlantWorld();
            for (int i = 0; i < 300; i++)
            {
                world.Foundation = UnityEngine.Object.FindFirstObjectByType<FoundationBootstrap>();
                world.Rig = UnityEngine.Object.FindFirstObjectByType<PhysicalAthleteRig>();
                world.Bar = UnityEngine.Object.FindFirstObjectByType<PhysicalBarbell>();
                if (world.Rig != null && world.Rig.PoweredController != null && world.Bar != null && world.Bar.IsBuilt)
                    break;
                yield return null;
            }
            if (world.Rig == null || world.Rig.PoweredController == null || !world.Bar.IsBuilt)
                throw new InvalidOperationException("The athlete scene did not build.");
            world.Foundation.enabled = false;
            if (world.Foundation.Runtime.CurrentTime.Tick != 0ul)
                throw new InvalidOperationException("The render clock ticked before the setup was placed: " + world.Foundation.Runtime.CurrentTime.Tick);
            world.Bar.enabled = false;
            world.Rig.SetGameplayPerformanceProfile(true);
            Current = world;
        }

        public IEnumerator Teardown()
        {
            if (Foundation != null && Foundation.Runtime != null && Foundation.Runtime.IsInitialized)
            {
                AsyncOperation unload = Foundation.Runtime.Shutdown();
                while (unload != null && !unload.isDone)
                    yield return null;
            }
            if (Foundation != null)
                UnityEngine.Object.DestroyImmediate(Foundation.gameObject);
            Current = null;
            yield return null;
        }

        public void UseModel(DeadliftPlantModel model) => Model = model;

        public DeadliftPlantModel SolveSetup(DeadliftSetupSpec spec)
        {
            Solver = new DeadliftSetupSolver(Rig);
            Model = Solver.Solve(spec);
            return Model;
        }

        /// <summary>
        /// Authored initial condition, written once before tick zero: the bar
        /// resting facet-flat on the platform at the planned height and the
        /// athlete at the solved setup pose, all velocities zero.
        /// </summary>
        public void PlaceAtSetup(float loadKg, bool attachGrips, bool ignoreBarVsGraspingLimbs = true)
            => PlaceAtSetup(loadKg, attachGrips, attachGrips, ignoreBarVsGraspingLimbs);

        /// <summary>As above, with each hand's grip chosen separately (GAM-70 A3 single-grip control).</summary>
        public void PlaceAtSetup(float loadKg, bool attachLeft, bool attachRight, bool ignoreBarVsGraspingLimbs)
        {
            DeadliftSetupSpec spec = Model.Spec;
            Foundation.Runtime.HoldPhysicsUntilInitialStateValidated();

            Bar.ConfigureLoad(loadKg);
            Foundation.Runtime.HoldPhysicsUntilInitialStateValidated();
            Rigidbody bar = Bar.Body;
            bar.isKinematic = false;
            bar.position = Model.BarCenterWorld;
            bar.rotation = Quaternion.AngleAxis(180f / PhysicalBarbell.DefaultPlateColliderSides, Vector3.right);
            bar.transform.SetPositionAndRotation(bar.position, bar.rotation);
            bar.linearVelocity = Vector3.zero;
            bar.angularVelocity = Vector3.zero;
            bar.sleepThreshold = 0f;

            _placed = Model.Poses;
            foreach (KeyValuePair<string, PhysicalAthleteRig.SegmentRuntime> entry in Rig.Segments)
            {
                Rigidbody body = entry.Value.Body;
                Pose pose = _placed[entry.Key];
                body.isKinematic = false;
                body.transform.SetPositionAndRotation(pose.position, pose.rotation);
                body.position = pose.position;
                body.rotation = pose.rotation;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.sleepThreshold = 0f;
            }
            Physics.SyncTransforms();

            RegisterSurfacesAndSensors();
            if (ignoreBarVsGraspingLimbs)
                IgnoreGraspedBarCollisions();

            if (attachLeft)
            {
                LeftGrip = new BenchGrip("left");
                LeftGrip.Attach(Rig.Segments["left_hand"].Body, Bar.Body, Model.LeftGripAnchorLocal);
                TickZeroMaxGripSeparationM = Mathf.Max(TickZeroMaxGripSeparationM, LeftGrip.AnchorSeparationM);
            }
            if (attachRight)
            {
                RightGrip = new BenchGrip("right");
                RightGrip.Attach(Rig.Segments["right_hand"].Body, Bar.Body, Model.RightGripAnchorLocal);
                TickZeroMaxGripSeparationM = Mathf.Max(TickZeroMaxGripSeparationM, RightGrip.AnchorSeparationM);
            }

            Hold = new DeadliftSetupHold(Model.SetupLogical);
            Rig.SetCommandSource(Hold);
            Rig.PrimeCommandSource();
            ValidateTickZero();
            Foundation.Runtime.RegisterPostPhysicsStep(OnPostPhysicsStep);
            Foundation.Runtime.MarkInitialPhysicalStateValidated();
        }

        private void RegisterSurfacesAndSensors()
        {
            BenchSurfaceRegistry.Clear();
            BenchSurfaceRegistry.Register(Rig.PlatformCollider, BenchSurface.Floor);
            foreach (KeyValuePair<string, PhysicalAthleteRig.SegmentRuntime> entry in Rig.Segments)
                BenchSurfaceRegistry.Register(entry.Value.Collider, SurfaceFor(entry.Key));
            foreach (Collider c in Bar.Body.GetComponentsInChildren<Collider>(true))
                BenchSurfaceRegistry.Register(c, BenchSurface.Bar);
            foreach (string id in Rig.Segments.Keys)
            {
                Rigidbody body = Rig.Segments[id].Body;
                BenchContactSensor sensor = body.GetComponent<BenchContactSensor>() ?? body.gameObject.AddComponent<BenchContactSensor>();
                sensor.OwnSurface = SurfaceFor(id);
                Sensors[id] = sensor;
            }
            BenchContactSensor barSensor = Bar.Body.GetComponent<BenchContactSensor>() ?? Bar.Body.gameObject.AddComponent<BenchContactSensor>();
            barSensor.OwnSurface = BenchSurface.Bar;
            Sensors["bar"] = barSensor;
        }

        public static BenchSurface SurfaceFor(string id)
        {
            switch (id)
            {
                case "head_neck": return BenchSurface.AthleteHead;
                case "thorax": return BenchSurface.AthleteThorax;
                case "abdomen": return BenchSurface.AthleteAbdomen;
                case "pelvis": return BenchSurface.AthletePelvis;
                case "left_foot":
                case "right_foot": return BenchSurface.AthleteFoot;
                default: return id.Contains("arm") || id.Contains("hand") ? BenchSurface.AthleteArm : BenchSurface.AthleteLeg;
            }
        }

        /// <summary>
        /// Declared game approximation, as in the bench: the bar does not
        /// collide with the limbs that grasp it; the grip joint carries that
        /// load path. Shins, thighs, trunk, head and feet still collide with it.
        /// </summary>
        public static readonly string[] GraspingLimbs = { "left_hand", "right_hand", "left_forearm", "right_forearm", "left_upper_arm", "right_upper_arm" };

        private void IgnoreGraspedBarCollisions()
        {
            Collider[] bar = Bar.Body.GetComponentsInChildren<Collider>(true);
            foreach (string id in GraspingLimbs)
                foreach (Collider c in bar)
                    Physics.IgnoreCollision(Rig.Segments[id].Collider, c, true);
        }

        private void ValidateTickZero()
        {
            float worst = 0f;
            foreach (KeyValuePair<string, PhysicalAthleteRig.SegmentRuntime> entry in Rig.Segments)
                worst = Mathf.Max(worst, Penetration(entry.Value.Collider, Rig.PlatformCollider));
            foreach (Collider barCollider in Bar.Body.GetComponentsInChildren<Collider>(true))
            {
                if (!barCollider.gameObject.activeInHierarchy)
                    continue;
                worst = Mathf.Max(worst, Penetration(barCollider, Rig.PlatformCollider) );
                foreach (PhysicalAthleteRig.SegmentRuntime segment in Rig.Segments.Values)
                {
                    if (Physics.GetIgnoreCollision(barCollider, segment.Collider))
                        continue;
                    float d = Penetration(barCollider, segment.Collider);
                    if (d > 0f)
                        TickZeroSelfPenetrations.Add($"bar:{barCollider.name}-{segment.Recipe.Id} {d * 1000f:F1} mm");
                }
            }
            var segments = new List<PhysicalAthleteRig.SegmentRuntime>(Rig.Segments.Values);
            for (int i = 0; i < segments.Count; i++)
            {
                for (int j = i + 1; j < segments.Count; j++)
                {
                    Collider a = segments[i].Collider, b = segments[j].Collider;
                    if (Physics.GetIgnoreCollision(a, b))
                        continue;
                    float d = Penetration(a, b);
                    if (d > 0f)
                        TickZeroSelfPenetrations.Add($"{segments[i].Recipe.Id}-{segments[j].Recipe.Id} {d * 1000f:F1} mm");
                }
            }
            TickZeroMaxPenetrationM = worst;

            foreach (PhysicalAthleteRig.JointRuntime joint in Rig.Joints)
            {
                Vector3 a = joint.Joint.transform.TransformPoint(joint.Joint.anchor);
                Vector3 b = joint.Joint.connectedBody.transform.TransformPoint(joint.Joint.connectedAnchor);
                if (Vector3.Distance(a, b) > 0.0005f)
                    throw new InvalidOperationException($"Joint {joint.Recipe.ChildId} anchors separate after setup placement.");
            }
        }

        private static float Penetration(Collider a, Collider b)
        {
            if (a == null || b == null)
                return 0f;
            return Physics.ComputePenetration(a, a.transform.position, a.transform.rotation, b, b.transform.position, b.transform.rotation,
                out _, out float distance) ? distance : 0f;
        }

        private void OnPostPhysicsStep(SimulationTime time, PhysicalObservation observation, PlayerIntentFrame intent)
        {
            double dt = SimulationConstants.FixedDeltaTimeSeconds;
            foreach (BenchContactSensor sensor in Sensors.Values)
                sensor.CompleteTick(dt);
            Rig.PoweredController.CapturePostPhysicsDiagnostics();

            var s = new PlantSample
            {
                Tick = time.Tick,
                LeftFootN = Sensors["left_foot"].NormalForce(BenchSurface.Floor),
                RightFootN = Sensors["right_foot"].NormalForce(BenchSurface.Floor),
                BarFloorN = Sensors["bar"].NormalForce(BenchSurface.Floor),
                Com = WholeBodyCom(),
                BarPosition = Bar.Body.position,
                BarVelocity = Bar.Body.linearVelocity,
                BarAngularVelocity = Bar.Body.angularVelocity,
                BarTiltDeg = Vector3.Angle(Bar.Body.rotation * Vector3.right, Vector3.right),
                BarSpeed = Bar.Body.linearVelocity.magnitude
            };
            float drift = 0f;
            foreach (KeyValuePair<string, PhysicalAthleteRig.SegmentRuntime> entry in Rig.Segments)
                drift = Mathf.Max(drift, Vector3.Distance(entry.Value.Body.position, _placed[entry.Key].position));
            s.MaxBodyDriftM = drift;

            float worstJoint = 0f, worstDemand = 0f;
            string worstDemandJoint = string.Empty;
            foreach (PoweredJointController.PoweredJointRuntime joint in Rig.PoweredController.Joints)
            {
                if (!joint.HasPostPhysicsDiagnostic)
                    continue;
                PoweredJointDiagnostic d = joint.PostPhysicsDiagnostic;
                worstJoint = Mathf.Max(worstJoint, d.ErrorRad.magnitude * Mathf.Rad2Deg);
                if (d.ModeledDemand > worstDemand)
                {
                    worstDemand = d.ModeledDemand;
                    worstDemandJoint = joint.Id;
                }
            }
            s.MaxJointErrorDeg = worstJoint;
            s.MaxDemand = worstDemand;
            s.MaxDemandJoint = worstDemandJoint;
            if (LeftGrip != null && LeftGrip.IsAttached)
            {
                s.LeftGripSeparationM = LeftGrip.AnchorSeparationM;
                s.LeftGripForceRaw = LeftGrip.SolverForceRaw;
            }
            if (RightGrip != null && RightGrip.IsAttached)
            {
                s.RightGripSeparationM = RightGrip.AnchorSeparationM;
                s.RightGripForceRaw = RightGrip.SolverForceRaw;
            }
            Samples.Add(s);
            AfterSample?.Invoke(time, observation, intent);
        }

        /// <summary>
        /// Extra read-only observers run inside the single authoritative
        /// post-physics callback, after this world's own sample (the runtime
        /// allows one callback owner).
        /// </summary>
        public event Action<SimulationTime, PhysicalObservation, PlayerIntentFrame> AfterSample;

        public Vector3 WholeBodyCom()
        {
            Vector3 sum = Vector3.zero;
            float mass = 0f;
            foreach (PhysicalAthleteRig.SegmentRuntime segment in Rig.Segments.Values)
            {
                sum += segment.Body.worldCenterOfMass * segment.Body.mass;
                mass += segment.Body.mass;
            }
            return sum / mass;
        }

        public void Step(int ticks)
        {
            for (int i = 0; i < ticks; i++)
                Foundation.Runtime.StepOne();
        }
    }
}
