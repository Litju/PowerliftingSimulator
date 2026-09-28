using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Equipment;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Foundation.Unity;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.Tests
{
    public sealed class GAM13V23BSubstrateAuditTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";

        [UnityTest]
        public IEnumerator GAM13_V23B_TICK_ZERO_RECEIPT_AND_RESET_REBUILD()
        {
            float loadKg = ReadLoadKg();
            AsyncOperation loadScene = SceneManager.LoadSceneAsync(QualificationScene, LoadSceneMode.Single);
            Assert.That(loadScene, Is.Not.Null);
            while (!loadScene.isDone)
                yield return null;
            yield return null;

            FoundationBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<FoundationBootstrap>();
            PhysicalAthleteRig rig = UnityEngine.Object.FindFirstObjectByType<PhysicalAthleteRig>();
            SquatPhysicalPrototypeController controller =
                UnityEngine.Object.FindFirstObjectByType<SquatPhysicalPrototypeController>();
            Assert.That(bootstrap, Is.Not.Null);
            Assert.That(rig, Is.Not.Null);
            Assert.That(controller, Is.Not.Null);
            for (int frame = 0; frame < 8 && !controller.IsInitialized; frame++)
                yield return null;
            Assert.That(controller.IsInitialized, Is.True, controller.StartupFailure);

            controller.enabled = false;
            bootstrap.enabled = false;
            controller.SetLoad(loadKg);
            FoundationRuntime runtime = bootstrap.Runtime;
            Assert.That(controller.TickZeroSubstrateValidated, Is.True);
            Assert.That(controller.TickZeroValidationTick, Is.EqualTo(0ul));
            Assert.That(runtime.CurrentTime.Tick, Is.EqualTo(0ul));

            WriteReceipt(controller, runtime, loadKg);

            Rigidbody barBody = UnityEngine.Object.FindFirstObjectByType<PhysicalBarbell>().Body;
            BodyBaseline[] baseline = CaptureBaseline(rig, barBody);
            runtime.StepOne();
            runtime.Reset();
            Assert.That(runtime.CurrentTime.Tick, Is.EqualTo(0ul));
            AssertBaseline(rig, barBody, baseline);
        }

        private static float ReadLoadKg()
        {
            string value = Environment.GetEnvironmentVariable("GAM13_V23B_LOAD_KG");
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float loadKg) &&
                loadKg >= 0f
                ? loadKg
                : 0f;
        }

        private static void WriteReceipt(
            SquatPhysicalPrototypeController controller,
            FoundationRuntime runtime,
            float loadKg)
        {
            PhysicalAthleteRig rig = controller.AthleteRig;
            Receipt receipt = new Receipt
            {
                authority = "GAM-13 V2-3B",
                unityVersion = Application.unityVersion,
                physicsScene = runtime.AuthoritativeScene.name,
                physicsTick = (long)runtime.CurrentTime.Tick,
                fixedDeltaSeconds = (float)SimulationConstants.FixedDeltaTimeSeconds,
                tickZeroValidationPassed = controller.TickZeroSubstrateValidated,
                tickZeroValidationTick = (long)controller.TickZeroValidationTick,
                loadKg = loadKg,
                athleteBodyCount = rig.Segments.Count,
                athleteTotalMassKg = rig.TotalMassKg,
                maxInitialNonAdjacentPenetrationM = rig.MaxInitialNonAdjacentPenetrationMeters,
                suppressedInitialPenetrationPairs = controller.SuppressedInitialPenetrationPairCount,
                maximumSuppressedPenetrationM = controller.MaximumSuppressedInitialPenetrationMeters,
                suppressedPenetrationPairDetails = new List<string>(controller.SuppressedInitialPenetrationPairs).ToArray(),
                suppressedPenetrationPolicy = "Direct connected pairs with collision disabled by their joint or Physics.IgnoreCollision pairs; unsuppressed overlapping pairs fail initialization.",
                activeInitialPenetrationPairs = 0,
                canonicalStandingTargetsPrimed = rig.PoweredController.Mode == PoweredAthleteMode.Controlled,
                bilateralPlantarGeometricContact = PlantarGapWithinTolerance(rig, "left_foot") &&
                    PlantarGapWithinTolerance(rig, "right_foot"),
                contactDetectorTiming = "Collision-callback contact is published after PhysicsScene.Simulate; tick 0 records geometric sole registration.",
                segments = SegmentRecords(rig),
                joints = JointRecords(rig),
                platform = CreatePlatformRecord(rig),
                barbell = CreateBarRecord(controller),
                saddle = CreateSaddleRecord(controller.Saddle)
            };

            string path = Environment.GetEnvironmentVariable("GAM13_V23B_TICK0_RECEIPT_PATH");
            if (string.IsNullOrWhiteSpace(path))
            {
                string timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                path = Path.Combine("Artifacts", "Measurements", "GAM-13", "v2-3b-substrate",
                    $"tick-0-load-{loadKg:000}-{timestamp}.json");
            }

            string fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
                throw new InvalidOperationException($"Refusing to overwrite existing evidence '{fullPath}'.");
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(fullPath, JsonUtility.ToJson(receipt, true), new System.Text.UTF8Encoding(false));
            Debug.Log($"GAM13_V23B_TICK0_RECEIPT load={loadKg:F1}kg path={fullPath}");
        }

        private static SegmentRecord[] SegmentRecords(PhysicalAthleteRig rig)
        {
            var segments = new List<PhysicalAthleteRig.SegmentRuntime>(rig.Segments.Values);
            segments.Sort((a, b) => string.CompareOrdinal(a.Recipe.Id, b.Recipe.Id));
            var result = new SegmentRecord[segments.Count];
            for (int i = 0; i < segments.Count; i++)
            {
                PhysicalAthleteRig.SegmentRuntime segment = segments[i];
                Vector3 colliderCenter;
                string colliderType;
                if (segment.Collider is BoxCollider box)
                {
                    colliderCenter = box.center;
                    colliderType = "Box";
                }
                else
                {
                    CapsuleCollider capsule = (CapsuleCollider)segment.Collider;
                    colliderCenter = capsule.center;
                    colliderType = "CapsuleY";
                }
                result[i] = new SegmentRecord
                {
                    id = segment.Recipe.Id,
                    parentId = segment.Recipe.ParentId ?? string.Empty,
                    massKg = segment.Body.mass,
                    colliderType = colliderType,
                    dimensionsM = segment.DimensionsMeters,
                    colliderCenterM = colliderCenter,
                    centerOfMassLocalM = segment.Body.centerOfMass,
                    centerOfMassWorldM = segment.Body.worldCenterOfMass,
                    inertiaTensorKgM2 = segment.Body.inertiaTensor,
                    inertiaTensorRotation = segment.Body.inertiaTensorRotation,
                    automaticCenterOfMass = segment.Body.automaticCenterOfMass,
                    automaticInertiaTensor = segment.Body.automaticInertiaTensor,
                    positionWorldM = segment.Body.position,
                    rotationWorld = segment.Body.rotation,
                    linearVelocityMps = segment.Body.linearVelocity,
                    angularVelocityRadS = segment.Body.angularVelocity
                };
            }
            return result;
        }

        private static JointRecord[] JointRecords(PhysicalAthleteRig rig)
        {
            var result = new JointRecord[rig.Joints.Count];
            for (int i = 0; i < result.Length; i++)
            {
                PhysicalAthleteRig.JointRuntime runtime = rig.Joints[i];
                ConfigurableJoint joint = runtime.Joint;
                PoweredJointController.PoweredJointRuntime powered = rig.PoweredController.GetJoint(runtime.Recipe.ChildId);
                PoweredJointDiagnostic diagnostic = powered.Diagnostic;
                result[i] = new JointRecord
                {
                    childId = runtime.Recipe.ChildId,
                    parentId = rig.Segments[runtime.Recipe.ChildId].Recipe.ParentId,
                    kind = runtime.Recipe.Kind.ToString(),
                    anchorWorldErrorM = Vector3.Distance(
                        joint.transform.TransformPoint(joint.anchor),
                        joint.connectedBody.transform.TransformPoint(joint.connectedAnchor)),
                    axisChild = joint.axis,
                    secondaryAxisChild = joint.secondaryAxis,
                    angularXLowDeg = joint.lowAngularXLimit.limit,
                    angularXHighDeg = joint.highAngularXLimit.limit,
                    angularYLimitDeg = joint.angularYLimit.limit,
                    angularZLimitDeg = joint.angularZLimit.limit,
                    angularXDrive = DriveRecord.From(joint.angularXDrive),
                    angularYZDrive = DriveRecord.From(joint.angularYZDrive),
                    rotationDriveMode = joint.rotationDriveMode.ToString(),
                    configuredInWorldSpace = joint.configuredInWorldSpace,
                    projectionMode = joint.projectionMode.ToString(),
                    targetRotation = joint.targetRotation,
                    targetAngularVelocityRadS = joint.targetAngularVelocity,
                    twistDemandNm = diagnostic.TwistDriveDemandNm,
                    swingYZDemandNm = diagnostic.SwingDriveDemandNm,
                    twistDemandFraction = diagnostic.TwistDriveDemandFraction,
                    swingYZDemandFraction = diagnostic.SwingDriveDemandFraction,
                    maximumActiveDriveDemandFraction = diagnostic.ModeledDemand,
                    solverConstraintTorqueNm = diagnostic.SolverTorqueJointSpaceNm
                };
            }
            return result;
        }

        private static PlatformRecord CreatePlatformRecord(PhysicalAthleteRig rig)
        {
            BoxCollider platform = rig.PlatformCollider;
            PhysicsMaterial material = platform.sharedMaterial;
            return new PlatformRecord
            {
                centerWorldM = platform.transform.position,
                sizeM = platform.size,
                topY = platform.bounds.max.y,
                staticFriction = material.staticFriction,
                dynamicFriction = material.dynamicFriction,
                frictionCombine = material.frictionCombine.ToString(),
                bounce = material.bounciness,
                leftFootGapM = rig.Segments["left_foot"].Collider.bounds.min.y - platform.bounds.max.y,
                rightFootGapM = rig.Segments["right_foot"].Collider.bounds.min.y - platform.bounds.max.y
            };
        }

        private static BarRecord CreateBarRecord(SquatPhysicalPrototypeController controller)
        {
            PhysicalBarbell barbell = controller.Saddle != null
                ? controller.Saddle.Barbell
                : UnityEngine.Object.FindFirstObjectByType<PhysicalBarbell>();
            Rigidbody body = barbell.Body;
            return new BarRecord
            {
                active = body.gameObject.activeInHierarchy,
                massKg = body.mass,
                requestedTotalMassKg = barbell.LoadPlan.RequestedTotalMassKg,
                centerOfMassLocalM = body.centerOfMass,
                inertiaTensorKgM2 = body.inertiaTensor,
                modeledCenterOfMassLocalM = barbell.InertiaModel.CenterOfMassBarMeters,
                modeledInertiaTensorKgM2 = barbell.InertiaModel.InertiaTensorKgM2,
                inertiaTensorRotation = body.inertiaTensorRotation,
                colliderCount = body.GetComponentsInChildren<Collider>(true).Length,
                rigidbodyCount = body.GetComponentsInChildren<Rigidbody>(true).Length,
                isKinematic = body.isKinematic,
                useGravity = body.useGravity,
                automaticCenterOfMass = body.automaticCenterOfMass,
                automaticInertiaTensor = body.automaticInertiaTensor,
                positionWorldM = body.position,
                linearVelocityMps = body.linearVelocity,
                angularVelocityRadS = body.angularVelocity
            };
        }

        private static SaddleRecord CreateSaddleRecord(SquatBarSaddle saddle)
        {
            if (saddle == null)
                return new SaddleRecord { present = false };
            return new SaddleRecord
            {
                present = true,
                initialAnchorErrorM = saddle.InitialAnchorErrorMeters,
                separationM = saddle.SaddleSeparationMeters,
                linearLimitOccupancy = saddle.CurrentLinearLimitOccupancy,
                relativeRotationDeg = saddle.RelativeRotationDegrees,
                isBroken = saddle.IsBroken,
                breakForceN = saddle.Joint.breakForce,
                breakTorqueNm = saddle.Joint.breakTorque,
                currentForceEngine = saddle.CurrentForceEngine,
                currentTorqueEngine = saddle.CurrentTorqueEngine
            };
        }

        private static bool PlantarGapWithinTolerance(PhysicalAthleteRig rig, string footId) =>
            Mathf.Abs(rig.Segments[footId].Collider.bounds.min.y - rig.PlatformCollider.bounds.max.y) <=
            PhysicalAthleteDefinition.AnchorToleranceMeters;

        private static BodyBaseline[] CaptureBaseline(PhysicalAthleteRig rig, Rigidbody barBody)
        {
            var result = new List<BodyBaseline>();
            foreach (PhysicalAthleteRig.SegmentRuntime segment in OrderedSegments(rig))
                result.Add(BodyBaseline.From(segment.Body));
            if (barBody != null)
                result.Add(BodyBaseline.From(barBody));
            return result.ToArray();
        }

        private static void AssertBaseline(PhysicalAthleteRig rig, Rigidbody barBody, BodyBaseline[] expected)
        {
            int index = 0;
            foreach (PhysicalAthleteRig.SegmentRuntime segment in OrderedSegments(rig))
                expected[index++].AssertMatches(segment.Body);
            if (barBody != null)
                expected[index].AssertMatches(barBody);
        }

        private static List<PhysicalAthleteRig.SegmentRuntime> OrderedSegments(PhysicalAthleteRig rig)
        {
            var segments = new List<PhysicalAthleteRig.SegmentRuntime>(rig.Segments.Values);
            segments.Sort((a, b) => string.CompareOrdinal(a.Recipe.Id, b.Recipe.Id));
            return segments;
        }

        private sealed class BodyBaseline
        {
            private readonly Rigidbody _body;
            private readonly Vector3 _position;
            private readonly Quaternion _rotation;
            private readonly Vector3 _linearVelocity;
            private readonly Vector3 _angularVelocity;

            private BodyBaseline(Rigidbody body)
            {
                _body = body;
                _position = body.position;
                _rotation = body.rotation;
                _linearVelocity = body.linearVelocity;
                _angularVelocity = body.angularVelocity;
            }

            public static BodyBaseline From(Rigidbody body) => new BodyBaseline(body);

            public void AssertMatches(Rigidbody body)
            {
                Assert.That(body, Is.SameAs(_body));
                Assert.That(Vector3.Distance(body.position, _position), Is.LessThanOrEqualTo(0.000001f), body.name);
                Assert.That(Quaternion.Angle(body.rotation, _rotation), Is.LessThanOrEqualTo(0.0001f), body.name);
                Assert.That(body.linearVelocity.magnitude, Is.LessThanOrEqualTo(0.000001f), body.name);
                Assert.That(body.angularVelocity.magnitude, Is.LessThanOrEqualTo(0.000001f), body.name);
            }
        }

        [Serializable]
        private sealed class Receipt
        {
            public string authority;
            public string unityVersion;
            public string physicsScene;
            public long physicsTick;
            public float fixedDeltaSeconds;
            public bool tickZeroValidationPassed;
            public long tickZeroValidationTick;
            public float loadKg;
            public int athleteBodyCount;
            public float athleteTotalMassKg;
            public float maxInitialNonAdjacentPenetrationM;
            public int suppressedInitialPenetrationPairs;
            public float maximumSuppressedPenetrationM;
            public string[] suppressedPenetrationPairDetails;
            public string suppressedPenetrationPolicy;
            public int activeInitialPenetrationPairs;
            public bool canonicalStandingTargetsPrimed;
            public bool bilateralPlantarGeometricContact;
            public string contactDetectorTiming;
            public SegmentRecord[] segments;
            public JointRecord[] joints;
            public PlatformRecord platform;
            public BarRecord barbell;
            public SaddleRecord saddle;
        }

        [Serializable]
        private sealed class SegmentRecord
        {
            public string id;
            public string parentId;
            public float massKg;
            public string colliderType;
            public Vector3 dimensionsM;
            public Vector3 colliderCenterM;
            public Vector3 centerOfMassLocalM;
            public Vector3 centerOfMassWorldM;
            public Vector3 inertiaTensorKgM2;
            public Quaternion inertiaTensorRotation;
            public bool automaticCenterOfMass;
            public bool automaticInertiaTensor;
            public Vector3 positionWorldM;
            public Quaternion rotationWorld;
            public Vector3 linearVelocityMps;
            public Vector3 angularVelocityRadS;
        }

        [Serializable]
        private sealed class JointRecord
        {
            public string childId;
            public string parentId;
            public string kind;
            public float anchorWorldErrorM;
            public Vector3 axisChild;
            public Vector3 secondaryAxisChild;
            public float angularXLowDeg;
            public float angularXHighDeg;
            public float angularYLimitDeg;
            public float angularZLimitDeg;
            public DriveRecord angularXDrive;
            public DriveRecord angularYZDrive;
            public string rotationDriveMode;
            public bool configuredInWorldSpace;
            public string projectionMode;
            public Quaternion targetRotation;
            public Vector3 targetAngularVelocityRadS;
            public float twistDemandNm;
            public float swingYZDemandNm;
            public float twistDemandFraction;
            public float swingYZDemandFraction;
            public float maximumActiveDriveDemandFraction;
            public Vector3 solverConstraintTorqueNm;
        }

        [Serializable]
        private sealed class DriveRecord
        {
            public float spring;
            public float damper;
            public float maximumForce;
            public bool useAcceleration;

            public static DriveRecord From(JointDrive drive) => new DriveRecord
            {
                spring = drive.positionSpring,
                damper = drive.positionDamper,
                maximumForce = drive.maximumForce,
                useAcceleration = drive.useAcceleration
            };
        }

        [Serializable]
        private sealed class PlatformRecord
        {
            public Vector3 centerWorldM;
            public Vector3 sizeM;
            public float topY;
            public float staticFriction;
            public float dynamicFriction;
            public string frictionCombine;
            public float bounce;
            public float leftFootGapM;
            public float rightFootGapM;
        }

        [Serializable]
        private sealed class BarRecord
        {
            public bool active;
            public float massKg;
            public float requestedTotalMassKg;
            public Vector3 centerOfMassLocalM;
            public Vector3 inertiaTensorKgM2;
            public Vector3 modeledCenterOfMassLocalM;
            public Vector3 modeledInertiaTensorKgM2;
            public Quaternion inertiaTensorRotation;
            public int colliderCount;
            public int rigidbodyCount;
            public bool isKinematic;
            public bool useGravity;
            public bool automaticCenterOfMass;
            public bool automaticInertiaTensor;
            public Vector3 positionWorldM;
            public Vector3 linearVelocityMps;
            public Vector3 angularVelocityRadS;
        }

        [Serializable]
        private sealed class SaddleRecord
        {
            public bool present;
            public float initialAnchorErrorM;
            public float separationM;
            public float linearLimitOccupancy;
            public float relativeRotationDeg;
            public bool isBroken;
            public float breakForceN;
            public float breakTorqueNm;
            public Vector3 currentForceEngine;
            public Vector3 currentTorqueEngine;
        }
    }
}
