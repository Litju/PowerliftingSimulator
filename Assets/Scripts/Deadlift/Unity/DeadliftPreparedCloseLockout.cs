using System;
using System.Linq;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Bench.Unity;
using PowerliftingSimulator.Equipment;
using UnityEngine;

namespace PowerliftingSimulator.Deadlift.Unity
{
    /// <summary>Owns the measured post-knee reference until the bar returns through the knee.</summary>
    internal sealed class DeadliftPreparedCloseLockout
    {
        // The return A/B matched the stock shaft height and broad sole patches here, before the reverse prefix diverged.
        private const float StockReturnProgress = 0.369f;
        private readonly DeadliftAttemptRuntime _runtime;
        private readonly PhysicalAthleteRig _rig;
        private readonly DeadliftPreparedAscentPlan _prefix;
        private readonly float _startProgress;
        private readonly Func<string, Vector3> _previousCop;
        private readonly Func<float> _previousDemand;
        private readonly Vector3[] _soleCenters;
        private readonly Vector3[] _soleAxes;
        public DeadliftPreparedAscentPlan Plan { get; }
        public bool IsReady { get; private set; }
        private bool _active;

        public DeadliftPreparedCloseLockout(DeadliftAttemptRuntime runtime, PhysicalAthleteRig rig,
            DeadliftPreparedAscentPlan prefix, bool incremental)
        {
            _runtime = runtime;
            _rig = rig;
            _prefix = prefix;
            _startProgress = runtime.Pull.LiftProgress01;
            _previousCop = runtime.Regulator.FootCopTarget;
            _previousDemand = runtime.Pull.ReferenceDemand;
            var feet = new[] { (BoxCollider)rig.Segments["left_foot"].Collider, (BoxCollider)rig.Segments["right_foot"].Collider };
            _soleCenters = feet.Select(box => box.transform.TransformPoint(box.center - 0.5f * box.size.y * Vector3.up)).ToArray();
            _soleAxes = feet.Select(box => box.GetComponent<DeadliftFootPressureSensor>().Axis).ToArray();
            var path = new DeadliftLiftPath(rig, runtime.Measurements, runtime.Model, runtime.Bar.mass) { BarProximity = true };
            var endpoint = path.SolveEndpoint();
            Quaternion barRotation = Quaternion.AngleAxis(180f / PhysicalBarbell.DefaultPlateColliderSides, Vector3.right);
            Plan = DeadliftPreparedAscentPlan.ContinueToLockout(runtime, rig, endpoint, barRotation, incremental);
            if (!incremental)
                AdvanceSolve();
        }

        public bool AdvanceSolve()
        {
            if (!Plan.SolveNextKey())
                return false;

            var check = Plan.MeasureInterpolation();
            if (!(check.SweptGap > 0f && check.PositionError < 0.001f && check.RotationError < 0.3f))
                throw new InvalidOperationException("The measured post-knee plant did not produce a closed, shaft-clear continuation.");

            _runtime.Hold.AscentReferenceOverride = (id, progress) =>
            {
                if (progress > _startProgress)
                    return Plan.Target(id, progress);
                float rejoin = ReturnBlend(progress);
                return Quaternion.Slerp(_prefix.Target(id, progress), _runtime.Path.Target(id, progress), rejoin);
            };
            _runtime.Hold.SuspendedPlanReference = SuspendedReference;
            _runtime.Regulator.ContactConsistentControl = true;
            _runtime.Regulator.ComPositionAnchor = true;
            _runtime.Regulator.PlannedCom = PlannedCom;
            _runtime.Regulator.FootCopTarget = PlannedCop;
            var left = _rig.PoweredController.GetJoint("left_foot");
            var right = _rig.PoweredController.GetJoint("right_foot");
            _runtime.Pull.LowerHandoffProgress = _startProgress;
            _runtime.Pull.LowerHandoffReady = () =>
                _runtime.Bar.linearVelocity.magnitude <= DeadliftRulebook.MotionlessSpeedMps &&
                _runtime.Monitor.Update() >= _runtime.Pull.MinSupportMarginM &&
                _runtime.Monitor.ActiveMarginM >= 0f && _runtime.Monitor.LoadedFeet == 2;
            _runtime.Pull.ReferenceDemand = () => Mathf.Max(_previousDemand(), Mathf.Max(AnkleDemand(left), AnkleDemand(right)));
            IsReady = _active = true;
            return true;
        }

        public void Update()
        {
            if (!_active || _runtime.Pull.Phase != DeadliftPullPhase.Lowering || _runtime.Pull.LiftProgress01 > StockReturnProgress)
                return;
            _runtime.Hold.SuspendedPlanReference = null;
            _runtime.Regulator.ContactConsistentControl = false;
            _runtime.Regulator.ComPositionAnchor = false;
            _runtime.Regulator.PlannedCom = null;
            _runtime.Regulator.FootCopTarget = _previousCop;
            _runtime.Pull.ReferenceDemand = _previousDemand;
            _active = false;
        }

        private static float AnkleDemand(PoweredJointController.PoweredJointRuntime ankle) =>
            Mathf.Max(ankle.PostPhysicsDiagnostic.TwistDriveDemandFraction, ankle.PostPhysicsDiagnostic.SecondaryDriveDemandFraction);

        private float ReturnBlend(float progress) => _runtime.Pull.Phase == DeadliftPullPhase.Lowering
            ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(_startProgress, StockReturnProgress, progress)) : 0f;

        private float Blend(float progress)
        {
            float transition = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Abs(progress - _startProgress) / 0.035f));
            float barWeight = -Physics.gravity.y * _runtime.Bar.mass;
            float plateLoad = _runtime.PlateLeftN + _runtime.PlateRightN;
            return transition * Mathf.Clamp01(1f - plateLoad / barWeight) * (1f - ReturnBlend(progress));
        }

        private DeadliftPreparedAscentPlan PlanAt(float progress) => progress < _startProgress ? _prefix : Plan;

        private (DeadliftContactStatics Reference, float Blend) SuspendedReference(float progress)
        {
            var sample = PlanAt(progress).At(progress);
            Vector3 Anchor(string id, BenchGrip grip)
            {
                Pose pose = sample.Poses[id];
                return pose.position + pose.rotation * grip.HandAnchorLocal;
            }
            var reference = DeadliftContactStatics.SolveSuspended(_rig, _runtime.Bar, sample.Bar, sample.Poses,
                Anchor("left_hand", _runtime.LeftGrip), Anchor("right_hand", _runtime.RightGrip));
            if (!reference.Feasible)
                throw new InvalidOperationException("Suspended deadlift reference has no admitted wrench at this key.\n" + reference.Describe());
            return (reference, Blend(progress));
        }

        private (float Z, float Vz) PlannedCom()
        {
            float s0 = _runtime.Pull.LiftProgress01;
            float s1 = Mathf.Min(1f, s0 + 0.001f);
            float z = PlanAt(s0).SystemComZ(s0);
            float velocity = s1 > s0 ? (PlanAt(s0).SystemComZ(s1) - z) / (s1 - s0) * _runtime.Pull.LiftRate01PerSecond : 0f;
            float rejoin = ReturnBlend(s0);
            float position = Mathf.Lerp(Plan.SystemComZ(_startProgress), z, Blend(s0));
            return (Mathf.Lerp(position, _runtime.Regulator.LastComZ, rejoin), velocity * (1f - rejoin));
        }

        private Vector3 PlannedCop(string id)
        {
            int i = id == "left_foot" ? 0 : 1;
            float progress = _runtime.Pull.LiftProgress01;
            Vector3 center = _soleCenters[i], axis = _soleAxes[i];
            Vector3 target = center + axis * ((PlanAt(progress).SystemComZ(progress) - center.z) / axis.z);
            return Vector3.Lerp(_previousCop(id), target, Blend(progress));
        }
    }
}
