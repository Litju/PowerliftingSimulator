using System.Collections;
using System.Linq;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Deadlift.Unity;
using UnityEngine;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.Deadlift
{
    public sealed class DeadliftShoulderTransferTests
    {
        private DeadliftPlantWorld _world;

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (_world != null)
                yield return _world.Teardown();
        }

        [UnityTest, Timeout(1800000), Explicit("Grounded B2 shoulder transfer identification")]
        public IEnumerator GROUNDED_SHOULDER_REFERENCE_TRANSFER()
        {
            var results = new float[3];
            var handLoads = new float[3];
            int trial = 0;
            foreach (float degrees in new[] { 0f, 2f, -2f })
            {
                yield return DeadliftPlantWorld.Load();
                _world = DeadliftPlantWorld.Current;
                var rig = _world.Rig;
                var runtime = new DeadliftAttemptRuntime(_world.Foundation, rig, _world.Bar);
                runtime.Initialize();
                _world.Step(300);

                var shoulders = rig.PoweredController.Joints.Where(j => j.Recipe.Family == "shoulder").ToArray();
                Assert.That(shoulders.Length, Is.EqualTo(2));
                var axes = new Vector3[2];
                var initial = new Quaternion[2];
                var baseline = new PoweredJointDiagnostic[2];
                for (int i = 0; i < shoulders.Length; i++)
                {
                    var shoulder = shoulders[i];
                    var joint = shoulder.Joint;
                    Quaternion worldFromJoint = joint.transform.rotation * shoulder.JointSpace;
                    Vector3 pivot = joint.transform.TransformPoint(joint.anchor);
                    string side = shoulder.Id.StartsWith("left") ? "left" : "right";
                    Vector3 arm = (rig.Segments[side + "_forearm"].Body.worldCenterOfMass - pivot).normalized;
                    Vector3 extension = Vector3.Cross(arm, Vector3.back).normalized;
                    Vector3 medial = Vector3.Cross(arm, side == "left" ? Vector3.right : Vector3.left).normalized;
                    axes[i] = Quaternion.Inverse(worldFromJoint) * extension;
                    baseline[i] = shoulder.PostPhysicsDiagnostic;
                    initial[i] = baseline[i].ActualRelative;
                    if (degrees != 0f)
                        runtime.Hold.IdentificationOffsets[shoulder.Id] = Quaternion.AngleAxis(degrees, axes[i]);
                    Debug.Log($"{shoulder.Id} W axes {worldFromJoint * Vector3.right:F3}/{worldFromJoint * Vector3.up:F3}/{worldFromJoint * Vector3.forward:F3} " +
                        $"extension J {axes[i]:F3}, medial J {Quaternion.Inverse(worldFromJoint) * medial:F3}, " +
                        $"limits {joint.lowAngularXLimit.limit}/{joint.highAngularXLimit.limit}/{joint.angularYLimit.limit}/{joint.angularZLimit.limit}, " +
                        $"mode {joint.rotationDriveMode}, caps {joint.angularXDrive.maximumForce}/{joint.angularYZDrive.maximumForce}/{joint.slerpDrive.maximumForce}");
                }

                var leftFoot = rig.Segments["left_foot"].Body.GetComponent<DeadliftFootPressureSensor>();
                var rightFoot = rig.Segments["right_foot"].Body.GetComponent<DeadliftFootPressureSensor>();
                var support = new DeadliftSupportMonitor(rig, runtime.Bar, rig.PlatformCollider);
                Vector3 leftCop0 = leftFoot.Cop, rightCop0 = rightFoot.Cop;
                float barY0 = runtime.Bar.position.y;
                Vector3 leftForce = Vector3.zero, rightForce = Vector3.zero, leftTorque = Vector3.zero, rightTorque = Vector3.zero;
                Vector3 leftCop = Vector3.zero, rightCop = Vector3.zero;
                float shin = 0f, shoulderToBar = 0f, minFoot = float.MaxValue, minPlate = float.MaxValue;
                float minMargin = float.MaxValue, maxRise = 0f;
                var maxTwist = new float[2];
                var maxSwing = new float[2];
                var maxTwistLimit = new float[2];
                var maxSwingDeg = new float[2];
                const int samples = 80;
                for (int step = 1; step <= 40 + samples; step++)
                {
                    _world.Step(1);
                    minFoot = Mathf.Min(minFoot, runtime.FootLoadMinN);
                    minPlate = Mathf.Min(minPlate, runtime.PlateLeftN, runtime.PlateRightN);
                    minMargin = Mathf.Min(minMargin, support.Update());
                    maxRise = Mathf.Max(maxRise, runtime.Bar.position.y - barY0);
                    for (int i = 0; i < shoulders.Length; i++)
                    {
                        var d = shoulders[i].PostPhysicsDiagnostic;
                        maxTwist[i] = Mathf.Max(maxTwist[i], d.TwistDriveDemandFraction);
                        maxSwing[i] = Mathf.Max(maxSwing[i], d.SwingDriveDemandFraction);
                        maxTwistLimit[i] = Mathf.Max(maxTwistLimit[i], d.LimitProximity);
                        maxSwingDeg[i] = Mathf.Max(maxSwingDeg[i], Vector3.Angle(Vector3.right, d.ActualRelative * Vector3.right));
                    }
                    if (step <= 40)
                        continue;

                    leftForce += runtime.LeftGrip.ForceOnBarWorld;
                    rightForce += runtime.RightGrip.ForceOnBarWorld;
                    leftTorque += runtime.LeftGrip.SolverTorqueRaw;
                    rightTorque += runtime.RightGrip.SolverTorqueRaw;
                    leftCop += leftFoot.Cop - leftCop0;
                    rightCop += rightFoot.Cop - rightCop0;
                    shin += DeadliftBarLegGeometry.Realized(rig, runtime.Bar, out _).Shin;
                    shoulderToBar += runtime.Bar.position.z - shoulders.Average(j => j.Joint.transform.TransformPoint(j.Joint.anchor).z);
                }

                results[trial] = (leftForce.z + rightForce.z) / samples;
                handLoads[trial++] = (leftForce.y + rightForce.y) / (2 * samples);
                Debug.Log(
                    $"shoulder offset {degrees:F1}° | hand-on-bar N L {leftForce / samples:F3} R {rightForce / samples:F3} | raw grip torque L {leftTorque / samples:F3} R {rightTorque / samples:F3} | " +
                    $"COP change mm L {leftCop * (1000f / samples):F2} R {rightCop * (1000f / samples):F2} | shin {shin * 1000f / samples:F2} mm | bar ahead of shoulders {shoulderToBar * 1000f / samples:F2} mm | " +
                    $"min foot {minFoot:F1} N plate {minPlate:F1} N capture {minMargin * 1000f:F1} mm | rise {maxRise * 1000f:F3} mm");
                for (int i = 0; i < shoulders.Length; i++)
                {
                    var d = shoulders[i].PostPhysicsDiagnostic;
                    float actual = SignedChange(initial[i], d.ActualRelative, axes[i]);
                    Debug.Log($"{shoulders[i].Id} signed delta deg requested/applied/actual " +
                        $"{SignedChange(baseline[i].RequestedTarget, d.RequestedTarget, axes[i]):F3}/" +
                        $"{SignedChange(baseline[i].AppliedTarget, d.AppliedTarget, axes[i]):F3}/" +
                        $"{actual:F3} " +
                        $"max X/YZ demand {maxTwist[i]:F3}/{maxSwing[i]:F3}, X limit fraction {maxTwistLimit[i]:F3}, swing {maxSwingDeg[i]:F2}°");
                    Assert.That(SignedChange(baseline[i].RequestedTarget, d.RequestedTarget, axes[i]), Is.EqualTo(degrees).Within(0.03f));
                    Assert.That(SignedChange(baseline[i].AppliedTarget, d.AppliedTarget, axes[i]), Is.EqualTo(degrees).Within(0.03f));
                    Assert.That(maxTwist[i], Is.LessThan(1f));
                    Assert.That(maxSwing[i], Is.LessThan(1f));
                    if (degrees != 0f)
                        Assert.That(actual * Mathf.Sign(degrees), Is.GreaterThan(0.2f));
                }
                Assert.That(runtime.Pull.Phase, Is.EqualTo(DeadliftPullPhase.Grounded));
                Assert.That(minFoot, Is.GreaterThan(support.LoadedFootN));
                Assert.That(minPlate, Is.GreaterThan(0f));
                Assert.That(minMargin, Is.GreaterThan(0f));
                yield return _world.Teardown();
                _world = null;
            }
            Assert.That(handLoads[1], Is.EqualTo(handLoads[0]).Within(10f));
            Assert.That(handLoads[2], Is.EqualTo(handLoads[0]).Within(10f));
            Assert.That(results[1], Is.LessThan(-10f));
            Assert.That(results[2], Is.GreaterThan(results[0] + 10f));
        }

        private static float SignedChange(Quaternion before, Quaternion after, Vector3 axis) =>
            PoweredJointController.SignedTwistRadians(Quaternion.Inverse(before) * after, axis) * Mathf.Rad2Deg;
    }
}
