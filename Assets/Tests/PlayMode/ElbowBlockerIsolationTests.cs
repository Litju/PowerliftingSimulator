using System.Collections;
using System.Collections.Generic;
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
    /// Finds what stops a legal elbow target.
    ///
    /// H9D left the elbow with a 118.8 deg target inside a 145 deg limit,
    /// settling at 4.9 deg with its drive at the 380 Nm ceiling while the
    /// solver resolved about 176 Nm on a joint whose only honest load is the
    /// 5.4 Nm of gravity on a 2.2 kg forearm. Something is absorbing the rest.
    /// These tests measure what, rather than reasoning about it.
    /// </summary>
    public sealed class ElbowBlockerIsolationTests
    {
        private const string PhysicalScene = "SquatPhysicalPrototype";
        private const string MeasurementDirectory = "Artifacts/Measurements/GAM-11";
        private const int SettleTicks = 600;

        private static readonly string[] ArmSegments =
        {
            "left_upper_arm", "right_upper_arm",
            "left_forearm", "right_forearm",
            "left_hand", "right_hand"
        };

        private FoundationBootstrap _bootstrap;
        private PhysicalAthleteRig _rig;
        private SquatPhysicalPrototypeController _controller;

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

        private IEnumerator LoadScene()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(PhysicalScene, LoadSceneMode.Single);
            while (!load.isDone)
                yield return null;
            yield return null;

            _bootstrap = Object.FindFirstObjectByType<FoundationBootstrap>();
            _rig = Object.FindFirstObjectByType<PhysicalAthleteRig>();
            _controller = Object.FindFirstObjectByType<SquatPhysicalPrototypeController>();
            for (int frame = 0; frame < 8 && !_controller.IsInitialized; frame++)
                yield return null;
            Assert.That(_controller.IsInitialized, Is.True, _controller.StartupFailure);
            _controller.SetLoad(0f);
            _bootstrap.enabled = false;
        }

        private void Step()
        {
            _bootstrap.Runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
            if (_controller.LeftFootContact != null)
                _controller.LeftFootContact.PhysicsTickUpdate(dt);
            if (_controller.RightFootContact != null)
                _controller.RightFootContact.PhysicsTickUpdate(dt);
        }

        private float ElbowDegrees(string jointId)
        {
            PoweredJointController.PoweredJointRuntime joint = _rig.PoweredController.GetJoint(jointId);
            return Quaternion.Angle(Quaternion.identity, joint.Diagnostic.ActualRelative);
        }

        // ---- section 6: is the target actually on the hinge manifold? -----

        [UnityTest]
        public IEnumerator ELBOW_TARGET_IS_PURE_HINGE_SPACE()
        {
            yield return LoadScene();
            for (int tick = 0; tick < 20; tick++)
                Step();

            var report = new StringBuilder();
            report.AppendLine("joint,requested_x,requested_y,requested_z,applied_x,applied_y,applied_z," +
                              "target_velocity_x,target_velocity_y,target_velocity_z");

            foreach (string jointId in new[] { "left_forearm", "right_forearm" })
            {
                PoweredJointController.PoweredJointRuntime joint = _rig.PoweredController.GetJoint(jointId);
                Vector3 requested = AxisAngleDegrees(joint.Diagnostic.RequestedTarget);
                Vector3 applied = AxisAngleDegrees(joint.Diagnostic.AppliedTarget);
                Vector3 velocity = joint.Diagnostic.TargetAngularVelocityRadS;

                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0},{1:F4},{2:F4},{3:F4},{4:F4},{5:F4},{6:F4},{7:F5},{8:F5},{9:F5}",
                    jointId, requested.x, requested.y, requested.z,
                    applied.x, applied.y, applied.z, velocity.x, velocity.y, velocity.z));
                Debug.Log($"[HINGE TARGET] {jointId} applied=({applied.x:F3}, {applied.y:F3}, {applied.z:F3}) deg");

                Assert.That(Mathf.Abs(applied.y), Is.LessThan(0.5f),
                    $"{jointId} carries {applied.y:F3} deg of target off its hinge axis.");
                Assert.That(Mathf.Abs(applied.z), Is.LessThan(0.5f),
                    $"{jointId} carries {applied.z:F3} deg of target off its hinge axis.");
                Assert.That(Mathf.Abs(applied.x), Is.GreaterThan(100f),
                    $"{jointId} is not being asked for the projected flexion at all.");
            }

            WriteMeasurement("GAM11-elbow-hinge-target-space.csv", report.ToString());
            yield return null;
        }

        private static Vector3 AxisAngleDegrees(Quaternion rotation)
        {
            if (rotation.w < 0f)
                rotation = new Quaternion(-rotation.x, -rotation.y, -rotation.z, -rotation.w);
            var axis = new Vector3(rotation.x, rotation.y, rotation.z);
            float magnitude = axis.magnitude;
            if (magnitude <= 1e-7f)
                return Vector3.zero;
            float angleDeg = 2f * Mathf.Atan2(magnitude, Mathf.Clamp(rotation.w, -1f, 1f)) * Mathf.Rad2Deg;
            return axis * (angleDeg / magnitude);
        }

        // ---- sections 7 and 8: baseline plus a contact census -------------

        [UnityTest]
        public IEnumerator ELBOW_CONTACT_PAIR_TELEMETRY()
        {
            yield return LoadScene();
            ContactCensus census = ContactCensus.Attach(_rig, ArmSegments);

            var trace = new StringBuilder();
            trace.AppendLine("tick,joint,actual_deg,error_deg,demand,limit_proximity," +
                             "solver_torque_x,solver_torque_y,solver_torque_z");
            float maxLeft = 0f;
            int stallTick = -1;

            for (int tick = 0; tick <= SettleTicks; tick++)
            {
                float left = ElbowDegrees("left_forearm");
                if (left > maxLeft + 0.05f)
                {
                    maxLeft = left;
                    stallTick = tick;
                }

                if (tick % 25 == 0)
                {
                    foreach (string jointId in new[] { "left_forearm", "right_forearm" })
                    {
                        PoweredJointController.PoweredJointRuntime joint =
                            _rig.PoweredController.GetJoint(jointId);
                        trace.AppendLine(string.Format(CultureInfo.InvariantCulture,
                            "{0},{1},{2:F3},{3:F3},{4:F3},{5:F4},{6:F2},{7:F2},{8:F2}",
                            tick, jointId, ElbowDegrees(jointId),
                            joint.Diagnostic.ErrorRad.magnitude * Mathf.Rad2Deg,
                            joint.Diagnostic.ModeledDemand, joint.Diagnostic.LimitProximity,
                            joint.Diagnostic.SolverTorqueJointSpaceNm.x,
                            joint.Diagnostic.SolverTorqueJointSpaceNm.y,
                            joint.Diagnostic.SolverTorqueJointSpaceNm.z));
                    }
                }

                if (tick < SettleTicks)
                    Step();
            }

            WriteMeasurement("GAM11-elbow-blocker-trace.csv", trace.ToString());
            WriteMeasurement("GAM11-elbow-contact-census.csv", census.ToCsv());
            Debug.Log($"[P0] left elbow final {ElbowDegrees("left_forearm"):F3} deg, " +
                      $"right {ElbowDegrees("right_forearm"):F3} deg, " +
                      $"max left {maxLeft:F3} deg reached at tick {stallTick}");
            foreach (string line in census.Ranked(8))
                Debug.Log($"[CONTACT] {line}");

            WriteMeasurement("GAM11-elbow-penetration-audit.csv", PenetrationAudit());
            yield return null;
        }

        /// <summary>
        /// Geometric overlap for every arm collider against every other
        /// athlete collider, measured rather than inferred from proximity.
        /// </summary>
        private string PenetrationAudit()
        {
            var report = new StringBuilder();
            report.AppendLine("body_a,body_b,penetration_depth_m,separation_x,separation_y,separation_z");
            foreach (string armId in ArmSegments)
            {
                Collider armCollider = _rig.Segments[armId].Collider;
                foreach (KeyValuePair<string, PhysicalAthleteRig.SegmentRuntime> other in _rig.Segments)
                {
                    if (other.Key == armId || other.Value.Collider == null)
                        continue;
                    if (!Physics.ComputePenetration(
                            armCollider, armCollider.transform.position, armCollider.transform.rotation,
                            other.Value.Collider, other.Value.Collider.transform.position,
                            other.Value.Collider.transform.rotation,
                            out Vector3 direction, out float distance))
                        continue;
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0},{1},{2:F5},{3:F3},{4:F3},{5:F3}",
                        armId, other.Key, distance, direction.x, direction.y, direction.z));
                    Debug.Log($"[PENETRATION] {armId} into {other.Key}: {distance * 1000f:F2} mm");
                }
            }
            return report.ToString();
        }

        // ---- section 14: does removing self-collision free the elbow? -----

        [UnityTest]
        public IEnumerator ELBOW_SELF_COLLISION_ABLATION()
        {
            yield return LoadScene();

            // Diagnostic only. Platform contact stays live; this suppresses
            // collisions between athlete segments to see whether any of them
            // is what holds the elbow.
            var colliders = new List<Collider>();
            foreach (KeyValuePair<string, PhysicalAthleteRig.SegmentRuntime> entry in _rig.Segments)
                if (entry.Value.Collider != null)
                    colliders.Add(entry.Value.Collider);
            for (int a = 0; a < colliders.Count; a++)
                for (int b = a + 1; b < colliders.Count; b++)
                    Physics.IgnoreCollision(colliders[a], colliders[b], true);

            for (int tick = 0; tick < SettleTicks; tick++)
                Step();

            float left = ElbowDegrees("left_forearm");
            float right = ElbowDegrees("right_forearm");
            float pelvisY = _rig.Segments["pelvis"].Body.position.y;
            Debug.Log($"[SELF-COLLISION OFF] left elbow {left:F3} deg, right {right:F3} deg, " +
                      $"pelvisY {pelvisY:F4}");
            yield return null;
        }

        // ---- section 17: capacity versus constraint ----------------------

        [UnityTest]
        public IEnumerator ELBOW_DIAGNOSTIC_HIGH_CAPACITY()
        {
            yield return LoadScene();
            for (int tick = 0; tick < 30; tick++)
                Step();

            // Diagnostic ceiling only. Production capacity is untouched; this
            // just separates a hard constraint from a finite drive.
            foreach (string jointId in new[] { "left_forearm", "right_forearm" })
            {
                ConfigurableJoint joint = _rig.PoweredController.GetJoint(jointId).Joint;
                JointDrive drive = joint.angularXDrive;
                drive.maximumForce = 1500f;
                joint.angularXDrive = drive;
            }

            for (int tick = 0; tick < SettleTicks; tick++)
            {
                Step();
                foreach (string jointId in new[] { "left_forearm", "right_forearm" })
                {
                    ConfigurableJoint joint = _rig.PoweredController.GetJoint(jointId).Joint;
                    JointDrive drive = joint.angularXDrive;
                    drive.maximumForce = 1500f;
                    joint.angularXDrive = drive;
                }
            }

            Debug.Log($"[HIGH CAPACITY 1500Nm] left elbow {ElbowDegrees("left_forearm"):F3} deg, " +
                      $"right {ElbowDegrees("right_forearm"):F3} deg");
            yield return null;
        }

        /// <summary>
        /// Neither contact nor drive capacity moves the elbow, and it stops at
        /// exactly the magnitude of its own low limit. Unity takes an inverted
        /// targetRotation, which PoweredJointController supplies, but the
        /// angular limits are written un-inverted. If those two disagree the
        /// drive pushes toward the 5 deg stop instead of the 145 deg one, and
        /// no torque gets past a limit.
        ///
        /// Diagnostic only. Production limits are not changed here.
        /// </summary>
        [UnityTest]
        public IEnumerator ELBOW_ANGULAR_LIMIT_SIGN_ABLATION()
        {
            yield return LoadScene();
            for (int tick = 0; tick < 30; tick++)
                Step();
            Debug.Log($"[LIMIT ABLATION] production limits: left {ElbowDegrees("left_forearm"):F3} deg");

            foreach ((string name, float low, float high) in new[]
            {
                ("widened", -160f, 160f),
                ("mirrored", -145f, 5f)
            })
            {
                yield return LoadScene();
                foreach (string jointId in new[] { "left_forearm", "right_forearm" })
                {
                    ConfigurableJoint joint = _rig.PoweredController.GetJoint(jointId).Joint;
                    joint.lowAngularXLimit = new SoftJointLimit { limit = low, bounciness = 0f, contactDistance = 2f };
                    joint.highAngularXLimit = new SoftJointLimit { limit = high, bounciness = 0f, contactDistance = 2f };
                }

                for (int tick = 0; tick < SettleTicks; tick++)
                    Step();

                Debug.Log($"[LIMIT ABLATION] {name} [{low}, {high}]: " +
                          $"left {ElbowDegrees("left_forearm"):F3} deg, " +
                          $"right {ElbowDegrees("right_forearm"):F3} deg, " +
                          $"pelvisY {_rig.Segments["pelvis"].Body.position.y:F4}");
            }
            yield return null;
        }

        /// <summary>
        /// The controlled version of the whole-body result: one hinge, no other
        /// body, no contact and no gravity, with the production elbow gains and
        /// ceiling, an asymmetric range, and a target deep inside the wide side
        /// of that range.
        ///
        /// If Unity measured its angular X limits in the same sense as the
        /// targetRotation the controller writes, the joint would reach the
        /// target. Stopping at the magnitude of the opposite limit is what an
        /// inverted limit convention looks like.
        /// </summary>
        [UnityTest]
        public IEnumerator ISOLATED_ELBOW_ANGULAR_LIMIT_CONVENTION()
        {
            SimulationMode previousMode = Physics.simulationMode;
            float previousDelta = Time.fixedDeltaTime;
            Physics.simulationMode = SimulationMode.Script;
            Time.fixedDeltaTime = 0.01f;

            var report = new StringBuilder();
            report.AppendLine("case,low_limit,high_limit,target_deg,settled_deg");
            var settled = new Dictionary<string, float>();

            foreach ((string name, float low, float high) in new[]
            {
                ("authored_order", -5f, 145f),
                ("inverted_order", -145f, 5f),
                ("symmetric_wide", -160f, 160f)
            })
            {
                float result = RunIsolatedHinge(low, high, 118.795f);
                settled[name] = result;
                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0},{1:F1},{2:F1},118.795,{3:F3}", name, low, high, result));
                Debug.Log("[ISOLATED HINGE] " + name + " [" + low + ", " + high + "] settled " +
                          result.ToString("F3") + " deg");
                yield return null;
            }

            WriteMeasurement("GAM11-elbow-limit-convention.csv", report.ToString());
            Physics.simulationMode = previousMode;
            Time.fixedDeltaTime = previousDelta;

            Assert.That(settled["authored_order"], Is.LessThan(20f),
                "The authored limit order did not block the hinge, so the whole-body stall has " +
                "another cause and a convention repair would not be justified.");
            Assert.That(settled["inverted_order"], Is.GreaterThan(100f),
                "Inverting the limit order did not free the hinge, so the limit sign convention " +
                "is not the blocker.");
            yield return null;
        }

        /// <summary>
        /// A single production-parity elbow hinge with no gravity, driven to a
        /// target and left to settle. Returns the angle it reaches.
        /// </summary>
        private static float RunIsolatedHinge(float lowDegrees, float highDegrees, float targetDegrees)
        {
            PhysicalSegmentRecipe segment = default;
            foreach (PhysicalSegmentRecipe candidate in PhysicalAthleteDefinition.Segments)
                if (candidate.Id == "left_forearm")
                    segment = candidate;
            JointFamilyProfile profile = PoweredJointController.FindFamilyProfile("elbow").Value;

            var parentObject = new GameObject("IsolatedElbowParent");
            var childObject = new GameObject("IsolatedElbowChild");
            try
            {
                Rigidbody parent = parentObject.AddComponent<Rigidbody>();
                parent.isKinematic = true;
                parent.useGravity = false;

                Rigidbody child = childObject.AddComponent<Rigidbody>();
                child.mass = segment.MassFraction * PhysicalAthleteDefinition.PrototypeBodyMassKg;
                child.useGravity = false;
                child.automaticInertiaTensor = false;
                child.inertiaTensor = PhysicalAthleteDefinition.BoxInertia(child.mass, segment.DimensionsMeters);
                child.inertiaTensorRotation = Quaternion.identity;
                child.sleepThreshold = 0f;
                child.angularDamping = 0f;
                child.linearDamping = 0f;
                PhysicalAthleteSolverProfile.Apply(child);

                var joint = childObject.AddComponent<ConfigurableJoint>();
                joint.connectedBody = parent;
                joint.autoConfigureConnectedAnchor = false;
                joint.anchor = Vector3.zero;
                joint.connectedAnchor = Vector3.zero;
                joint.axis = Vector3.right;
                joint.secondaryAxis = Vector3.up;
                joint.xMotion = ConfigurableJointMotion.Locked;
                joint.yMotion = ConfigurableJointMotion.Locked;
                joint.zMotion = ConfigurableJointMotion.Locked;
                joint.angularXMotion = ConfigurableJointMotion.Limited;
                joint.angularYMotion = ConfigurableJointMotion.Locked;
                joint.angularZMotion = ConfigurableJointMotion.Locked;
                joint.lowAngularXLimit = new SoftJointLimit { limit = lowDegrees, contactDistance = 2f };
                joint.highAngularXLimit = new SoftJointLimit { limit = highDegrees, contactDistance = 2f };
                joint.projectionMode = JointProjectionMode.None;
                joint.enableCollision = false;
                joint.enablePreprocessing = true;
                joint.configuredInWorldSpace = false;
                joint.rotationDriveMode = RotationDriveMode.XYAndZ;
                joint.angularXDrive = new JointDrive
                {
                    positionSpring = profile.Spring,
                    positionDamper = profile.Damper,
                    maximumForce = profile.BaseCapacityNm * 3.8f,
                    useAcceleration = false
                };
                joint.angularYZDrive = default;
                joint.slerpDrive = default;

                Quaternion target = Quaternion.AngleAxis(targetDegrees, Vector3.right);
                joint.targetRotation = PoweredJointController.ToUnityTargetRotation(target);
                joint.targetAngularVelocity = Vector3.zero;

                for (int tick = 0; tick < 400; tick++)
                    Physics.Simulate(0.01f);

                Quaternion relative = Quaternion.Inverse(parent.rotation) * child.rotation;
                return Quaternion.Angle(Quaternion.identity, relative);
            }
            finally
            {
                Object.DestroyImmediate(childObject);
                Object.DestroyImmediate(parentObject);
            }
        }

        private static void WriteMeasurement(string filename, string content)
        {
            Directory.CreateDirectory(Path.GetFullPath(MeasurementDirectory));
            File.WriteAllText(Path.Combine(Path.GetFullPath(MeasurementDirectory), filename), content);
        }

        /// <summary>
        /// Records the solver contacts the arm colliders actually make, with
        /// the impulse each pair carried.
        /// </summary>
        private sealed class ContactCensus
        {
            private readonly Dictionary<string, Record> _records = new Dictionary<string, Record>();

            private sealed class Record
            {
                public string A;
                public string B;
                public int FirstTick = -1;
                public int LastTick;
                public int TickCount;
                public float MaxImpulse;
                public float TotalImpulse;
                public Vector3 Point;
                public Vector3 Normal;
            }

            public static ContactCensus Attach(PhysicalAthleteRig rig, IEnumerable<string> segmentIds)
            {
                var census = new ContactCensus();
                var names = new Dictionary<Rigidbody, string>();
                foreach (KeyValuePair<string, PhysicalAthleteRig.SegmentRuntime> entry in rig.Segments)
                    if (entry.Value.Body != null)
                        names[entry.Value.Body] = entry.Key;

                foreach (string id in segmentIds)
                {
                    ContactProbe probe = rig.Segments[id].Body.gameObject.AddComponent<ContactProbe>();
                    probe.Initialise(census, id, names);
                }
                return census;
            }

            public void Report(string a, string b, float impulse, Vector3 point, Vector3 normal)
            {
                string key = string.CompareOrdinal(a, b) <= 0 ? $"{a}|{b}" : $"{b}|{a}";
                if (!_records.TryGetValue(key, out Record record))
                {
                    record = new Record { A = a, B = b, FirstTick = ContactProbe.Tick };
                    _records[key] = record;
                }
                record.LastTick = ContactProbe.Tick;
                record.TickCount++;
                record.TotalImpulse += impulse;
                if (impulse >= record.MaxImpulse)
                {
                    record.MaxImpulse = impulse;
                    record.Point = point;
                    record.Normal = normal;
                }
            }

            public IEnumerable<string> Ranked(int count)
            {
                var ordered = new List<Record>(_records.Values);
                ordered.Sort((x, y) => y.TotalImpulse.CompareTo(x.TotalImpulse));
                for (int index = 0; index < ordered.Count && index < count; index++)
                {
                    Record record = ordered[index];
                    yield return $"{record.A} <-> {record.B}: ticks={record.TickCount} " +
                                 $"maxImpulse={record.MaxImpulse:F2} totalImpulse={record.TotalImpulse:F1} " +
                                 $"first={record.FirstTick} last={record.LastTick} normal={record.Normal}";
                }
            }

            public string ToCsv()
            {
                var report = new StringBuilder();
                report.AppendLine("body_a,body_b,first_tick,last_tick,tick_count," +
                                  "max_normal_impulse,total_normal_impulse,point_x,point_y,point_z," +
                                  "normal_x,normal_y,normal_z");
                var ordered = new List<Record>(_records.Values);
                ordered.Sort((x, y) => y.TotalImpulse.CompareTo(x.TotalImpulse));
                foreach (Record record in ordered)
                {
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0},{1},{2},{3},{4},{5:F3},{6:F3},{7:F4},{8:F4},{9:F4},{10:F3},{11:F3},{12:F3}",
                        record.A, record.B, record.FirstTick, record.LastTick, record.TickCount,
                        record.MaxImpulse, record.TotalImpulse,
                        record.Point.x, record.Point.y, record.Point.z,
                        record.Normal.x, record.Normal.y, record.Normal.z));
                }
                return report.ToString();
            }
        }

        private sealed class ContactProbe : MonoBehaviour
        {
            public static int Tick;

            private ContactCensus _census;
            private string _id;
            private Dictionary<Rigidbody, string> _names;

            public void Initialise(ContactCensus census, string id, Dictionary<Rigidbody, string> names)
            {
                _census = census;
                _id = id;
                _names = names;
            }

            private void OnCollisionStay(Collision collision) => Record(collision);

            private void OnCollisionEnter(Collision collision) => Record(collision);

            private void Record(Collision collision)
            {
                if (_census == null || collision.contactCount == 0)
                    return;
                string other = collision.rigidbody != null && _names.TryGetValue(collision.rigidbody, out string name)
                    ? name
                    : collision.collider != null ? collision.collider.name : "world";
                ContactPoint contact = collision.GetContact(0);
                _census.Report(_id, other, collision.impulse.magnitude, contact.point, contact.normal);
            }
        }
    }
}
