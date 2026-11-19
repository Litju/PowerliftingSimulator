using System;
using System.Collections.Generic;
using System.Linq;
using PowerliftingSimulator.Athlete;
using UnityEngine;

namespace PowerliftingSimulator.Deadlift.Unity
{
    /// <summary>Opt-in reference handoff on the measured prepared plant.</summary>
    internal sealed class DeadliftPreparedRejoin
    {
        public const float EndProgress = 0.80f;
        private const float ShoulderTrimDeg = 0.50f;
        private const float SettlingSpeedMps = 0.045f;
        private const float TrimRampTicks = 35f;

        private readonly DeadliftAttemptRuntime _runtime;
        private readonly PhysicalAthleteRig _rig;
        private readonly Shoulder[] _shoulders;
        private float _maxProgress;
        private ulong _trimStart;
        private readonly double _solveStart;
        private readonly bool _closeLockout, _incremental;
        private DeadliftPreparedCloseLockout _close;

        private sealed class Shoulder
        {
            public PoweredJointController.PoweredJointRuntime Joint;
            public Quaternion InitialOffset;
            public Vector3 TrimAxis;
        }

        public DeadliftPreparedAscentPlan Plan { get; }
        public bool IsReady { get; private set; }
        public DeadliftPreparedAscentPlan PostKneePlan => _close?.IsReady == true ? _close.Plan : null;
        public DeadliftPreparedAscentPlan PendingPlan => !IsReady ? Plan : _close?.IsReady == false ? _close.Plan : null;

        public DeadliftPreparedRejoin(DeadliftAttemptRuntime runtime, PhysicalAthleteRig rig, bool incremental)
        {
            _runtime = runtime;
            _rig = rig;
            _closeLockout = runtime.PreparedCloseLockout;
            _incremental = incremental;
            _shoulders = rig.PoweredController.Joints.Where(j => j.Recipe.Family == "shoulder")
                .Select(j => new Shoulder { Joint = j, InitialOffset = runtime.Hold.PreparationOffsetFor(j.Id) }).ToArray();
            _solveStart = Time.realtimeSinceStartupAsDouble;
            Plan = new DeadliftPreparedAscentPlan(runtime, rig, throughKnee: true, complete: !_closeLockout,
                centered: false, endProgress: EndProgress, incremental: incremental);
            if (!incremental)
                AdvanceSolve();
        }

        public bool AdvanceSolve()
        {
            if (IsReady)
                return _close.AdvanceSolve();
            if (!Plan.SolveNextKey())
                return false;

            var plan = Plan.MeasureInterpolation();
            if (!(plan.MinGap > 0.020f && plan.SweptGap > 0f && plan.PositionError < 0.001f && plan.RotationError < 0.3f))
                throw new InvalidOperationException("The measured prepared configuration did not produce a closed, clear ascent reference.");
            _runtime.Hold.AscentReferenceOverride = _closeLockout ? Plan.Target
                : (id, progress) => Plan.RejoinTarget(id, progress, EndProgress);
            IsReady = true;
            Debug.Log($"GAM-70 prepared reference at tick {_runtime.Tick}. Solve {Time.realtimeSinceStartupAsDouble - _solveStart:F2} s, swept clearance {plan.SweptGap * 1000f:F2} mm.");
            return true;
        }

        public void Update()
        {
            float progress = _runtime.Pull.LiftProgress01;
            _maxProgress = Mathf.Max(_maxProgress, progress);
            float release = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_maxProgress / 0.07f));
            if (!_closeLockout && _trimStart == 0 && _runtime.Pull.Phase == DeadliftPullPhase.Lockout &&
                _runtime.Bar.linearVelocity.sqrMagnitude < SettlingSpeedMps * SettlingSpeedMps)
            {
                _trimStart = _runtime.Tick;
                foreach (Shoulder shoulder in _shoulders)
                    shoulder.TrimAxis = ShoulderExtensionAxis(_rig, shoulder.Joint);
            }
            float trim = 0f;
            if (_trimStart != 0)
            {
                trim = ShoulderTrimDeg * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.82f, 0.98f, progress));
                trim *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((_runtime.Tick - _trimStart) / TrimRampTicks));
            }
            foreach (Shoulder shoulder in _shoulders)
            {
                Quaternion offset = Quaternion.Slerp(shoulder.InitialOffset, Quaternion.identity, release);
                if (trim != 0f)
                    offset *= Quaternion.AngleAxis(trim, shoulder.TrimAxis);
                _runtime.Hold.PreparationOffsets[shoulder.Joint.Id] = offset;
            }
            if (_closeLockout && _close == null && _runtime.Bar.position.y - _runtime.Model.BarCenterWorld.y >= _runtime.Path.KneePassageRiseM)
                _close = new DeadliftPreparedCloseLockout(_runtime, _rig, Plan, _incremental);
            _close?.Update();
        }

        internal static Vector3 ShoulderExtensionAxis(PhysicalAthleteRig rig, PoweredJointController.PoweredJointRuntime shoulder)
        {
            ConfigurableJoint joint = shoulder.Joint;
            string side = shoulder.Id.StartsWith("left", StringComparison.Ordinal) ? "left" : "right";
            Vector3 arm = rig.Segments[side + "_forearm"].Body.worldCenterOfMass - joint.transform.TransformPoint(joint.anchor);
            Vector3 extension = Vector3.Cross(arm, Vector3.back).normalized;
            return Quaternion.Inverse(joint.transform.rotation * shoulder.JointSpace) * extension;
        }
    }
}
