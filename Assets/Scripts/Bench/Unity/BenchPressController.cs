using System;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Bench;
using PowerliftingSimulator.Foundation;
using UnityEngine;

namespace PowerliftingSimulator.Bench.Unity
{
    public enum BenchMotionPhase : byte
    {
        Racked,
        LiftingOff,
        Holding,
        Lowering,
        OnChest,
        Pressing,
        Reracking,
        Released,
        SpotterRescue
    }

    /// <summary>
    /// Player intent for one tick, already sampled for that tick. Tests use
    /// the same structure, so a scripted attempt goes through exactly the
    /// controller a player drives.
    /// </summary>
    public readonly struct BenchIntent
    {
        public BenchIntent(float lower01, float press01, bool confirm, bool abort)
        {
            Lower01 = Mathf.Clamp01(lower01);
            Press01 = Mathf.Clamp01(press01);
            Confirm = confirm;
            Abort = abort;
        }

        public float Lower01 { get; }
        public float Press01 { get; }
        public bool Confirm { get; }
        public bool Abort { get; }

        public static BenchIntent FromFrame(PlayerIntentFrame frame) =>
            new BenchIntent(frame.Yield01, frame.Drive01, frame.ConfirmHeld, frame.AbortHeld);
    }

    /// <summary>
    /// Plant: athlete + bar + bench + floor. Actuators: the 15 powered joints
    /// at their finite family capacities. This controller is their only
    /// writer. It commands joint-space references - the arm references come
    /// from measured IK paths parameterized by s, the rest hold the solved
    /// setup posture - and never writes a body, the bar or a force.
    ///
    /// Feedback it uses is physical and one tick old: bar-chest contact from
    /// the contact solver to stop the descent at a real touch, and bar-rest
    /// force to finish the rerack. It does not read the referee's commands;
    /// the player decides when to move, and the referee judges it.
    /// </summary>
    public sealed class BenchPressController : IPhysicalAthleteCommandSource
    {
        public const float LiftOffSeconds = 1.6f;
        public const float LowerSeconds = 1.3f;     // full path at full intent
        public const float PressSeconds = 1.0f;
        public const float RerackSeconds = 1.8f;
        public const float RescueSeconds = 2.5f;
        public const float ChestSettlePath = 0.03f; // path beyond first contact the reference may go

        /// <summary>
        /// Path acceleration limit, 1/s^2. A stepped rate made the velocity
        /// feedforward jolt the lifter at descent onset (measured: shoulders
        /// 487 -> 0 -> 765 N and a foot unloaded for 3 ticks).
        /// </summary>
        public const float PathAccelerationPerS2 = 3.0f;
        private float _commandedRate;

        private readonly PhysicalAthleteRig _rig;
        private readonly BenchPlantModel _model;
        private readonly float _dt;
        private bool _previousConfirm;
        private bool _liftOffRequested;
        private bool _rerackRequested;
        private float _contactS = float.NaN;

        // Foot loading loop. Pelvis-on-pad plus feet-on-floor through stiff
        // position-driven legs is statically indeterminate: a fixed reference
        // offset gave feet 50/50 N with buttocks 486 N at 0 mm, 185/204 N with
        // buttocks 0 N at 4 mm, and 281/73 N at 1.5 mm. Instead the leg
        // reference depth is the actuator of an integral loop on each foot's
        // measured floor normal force (observation), aiming at a fixed share.
        // Measured during the chest pause with 0.3 s / +-6 mm: the lifter rolls
        // on the 0.30 m pad and the feet trade load (L 50->129 N, R 134->3.7 N).
        public const float FootTargetForceN = 110f;
        // Split by mode. Common (both feet against the buttocks) is slow: a
        // 0.15 s common loop limit-cycled at ~0.6 s (feet 45<->170 N, buttocks
        // 155<->516 N, bar never still at lockout). Differential (left vs
        // right, the lateral roll on the pad) is the fast one.
        private const float FootCommonGainMPerNs = 5.5e-5f;   // ~0.5 s with the measured ~36 N/mm
        private const float FootDifferentialGainMPerNs = 1.8e-4f; // ~0.15 s
        private float _commonDepthM;
        private float _differentialDepthM;
        private const float FootDepthMinM = -0.004f;
        private const float FootDepthMaxM = 0.012f;
        private float _leftLegDepthM;
        private float _rightLegDepthM;

        public float LeftLegDepthM => _leftLegDepthM;
        public float RightLegDepthM => _rightLegDepthM;

        public BenchPressController(PhysicalAthleteRig rig, BenchPlantModel model)
        {
            _rig = rig ?? throw new ArgumentNullException(nameof(rig));
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
        }

        public BenchMotionPhase Phase { get; private set; } = BenchMotionPhase.Racked;
        public float LiftS { get; private set; }
        public float PressS { get; private set; }
        public float SRate { get; private set; }

        /// <summary>Observation of the last completed tick, set by the post-physics owner.</summary>
        public BenchObservation LastObservation { get; set; }
        public bool HasObservation { get; set; }

        /// <summary>Overrides the input-system intent; used by deterministic tests and the spotter flow.</summary>
        public Func<ulong, BenchIntent> ScriptedIntent { get; set; }

        public BenchIntent LastIntent { get; private set; }

        /// <summary>Bar weight, used only to judge that the rests carry the bar after a rerack.</summary>
        public float BarWeightN { get; set; }

        /// <summary>
        /// Scale on the arm joints' finite capacity (shoulders, elbows,
        /// wrists). 1 is the canonical athlete. It reaches the drives only as
        /// maximumForce, never as a gain; 0 is the "no actuator" control of
        /// the Gate B A/B test.
        /// </summary>
        public float ArmStrengthScale { get; set; } = 1f;

        public void RequestLiftOff() => _liftOffRequested = true;

        public void RequestRerack() => _rerackRequested = true;

        public void BeginSpotterRescue()
        {
            if (Phase == BenchMotionPhase.Released || Phase == BenchMotionPhase.Reracking || Phase == BenchMotionPhase.Racked)
                return;
            Phase = BenchMotionPhase.SpotterRescue;
        }

        public void MarkReleased() => Phase = BenchMotionPhase.Released;

        public bool RestsCarryBar =>
            HasObservation && LastObservation.BarRackForceN >= 0.8 * BarWeightN;

        public void PrepareCommands(
            PhysicalObservation previousObservation,
            SimulationTime time,
            PlayerIntentFrame intent,
            IPhysicalAthleteJointCommandSink sink)
        {
            BenchIntent i = ScriptedIntent != null ? ScriptedIntent(time.Tick) : BenchIntent.FromFrame(intent);
            LastIntent = i;
            if (time.Tick > 0)
            {
                Advance(i);
                UpdateFootLoop();
            }

            // Every powered joint receives exactly one command per tick.
            float rate = SRate;
            bool onLift = Phase == BenchMotionPhase.Racked || Phase == BenchMotionPhase.LiftingOff ||
                Phase == BenchMotionPhase.Reracking || Phase == BenchMotionPhase.Released;
            BenchArmPath path = onLift ? _model.LiftPath : _model.PressPath;
            float s = onLift ? LiftS : PressS;

            foreach (PoweredJointController.PoweredJointRuntime joint in _rig.PoweredController.Joints)
            {
                if (!joint.Profile.HasValue)
                    continue;
                int arm = Array.IndexOf(BenchSetupSolver.ArmJointIds, joint.Id);
                Quaternion target;
                Vector3 velocity = Vector3.zero;
                if (arm >= 0)
                {
                    target = path.Evaluate(arm, s);
                    velocity = path.Velocity(arm, s, rate);
                    if (joint.Recipe.Kind == PhysicalJointKind.Hinge)
                        velocity = new Vector3(velocity.x, 0f, 0f);
                }
                else if (_model.LegReferenceLogical.TryGetValue(joint.Id, out Quaternion deep) &&
                    _model.SetupLogical.TryGetValue(joint.Id, out Quaternion flat))
                {
                    float depth = joint.Id.StartsWith("left", StringComparison.Ordinal) ? _leftLegDepthM : _rightLegDepthM;
                    target = Quaternion.SlerpUnclamped(flat, deep, depth / BenchSetupSolver.FootDriveIntoFloorM);
                }
                else
                {
                    target = _model.SetupLogical.TryGetValue(joint.Id, out Quaternion q) ? q : Quaternion.identity;
                }
                float strength = arm >= 0 ? Mathf.Max(0f, ArmStrengthScale) : 1f;
                sink.ApplyCommand(joint.Id, new JointCommand(target, velocity, 1f, strength), time.Tick);
            }
        }

        private void Advance(BenchIntent i)
        {
            bool confirmEdge = i.Confirm && !_previousConfirm;
            _previousConfirm = i.Confirm;
            bool chest = HasObservation && LastObservation.BarOnChest;
            SRate = 0f;
            BenchMotionPhase before = Phase;

            switch (Phase)
            {
                case BenchMotionPhase.Racked:
                    if (confirmEdge || _liftOffRequested)
                    {
                        _liftOffRequested = false;
                        Phase = BenchMotionPhase.LiftingOff;
                    }
                    break;

                case BenchMotionPhase.LiftingOff:
                    LiftS = Step(LiftS, 1f / LiftOffSeconds, 1f);
                    if (LiftS >= 1f)
                    {
                        PressS = 0f;
                        Phase = BenchMotionPhase.Holding;
                    }
                    break;

                case BenchMotionPhase.Holding:
                    if (confirmEdge || _rerackRequested)
                    {
                        _rerackRequested = false;
                        Phase = BenchMotionPhase.Reracking;
                        LiftS = 1f;
                    }
                    else if (i.Lower01 > 0.05f)
                    {
                        Phase = BenchMotionPhase.Lowering;
                    }
                    break;

                case BenchMotionPhase.Lowering:
                    if (i.Press01 > 0.05f)
                    {
                        Phase = BenchMotionPhase.Pressing;
                        break;
                    }
                    PressS = Step(PressS, i.Lower01 / LowerSeconds, 1f);
                    if (chest)
                    {
                        _contactS = PressS;
                        Phase = BenchMotionPhase.OnChest;
                    }
                    break;

                case BenchMotionPhase.OnChest:
                    if (i.Press01 > 0.05f)
                    {
                        Phase = BenchMotionPhase.Pressing;
                        break;
                    }
                    PressS = Step(PressS, i.Lower01 / LowerSeconds, Mathf.Min(1f, _contactS + ChestSettlePath));
                    break;

                case BenchMotionPhase.Pressing:
                    if (i.Lower01 > 0.05f && i.Press01 <= 0.05f)
                    {
                        Phase = BenchMotionPhase.Lowering;
                        break;
                    }
                    PressS = Step(PressS, -i.Press01 / PressSeconds, 0f);
                    if (PressS <= 0f)
                        Phase = BenchMotionPhase.Holding;
                    break;

                case BenchMotionPhase.Reracking:
                    LiftS = Step(LiftS, -1f / RerackSeconds, 0f);
                    break;

                case BenchMotionPhase.SpotterRescue:
                    PressS = Step(PressS, -1f / RescueSeconds, 0f);
                    if (PressS <= 0f)
                    {
                        LiftS = 1f;
                        Phase = BenchMotionPhase.Reracking;
                    }
                    break;
            }

            // Phases that hold still decay the commanded rate too.
            if (SRate == 0f)
            {
                float maxDelta = PathAccelerationPerS2 * _dt;
                _commandedRate = Mathf.MoveTowards(_commandedRate, 0f, maxDelta);
            }
            // Switching between the lift-off and press paths restarts from rest.
            bool wasLift = IsLiftPhase(before), isLift = IsLiftPhase(Phase);
            if (wasLift != isLift)
                _commandedRate = 0f;
        }

        private static bool IsLiftPhase(BenchMotionPhase phase) =>
            phase == BenchMotionPhase.Racked || phase == BenchMotionPhase.LiftingOff ||
            phase == BenchMotionPhase.Reracking || phase == BenchMotionPhase.Released;

        private void UpdateFootLoop()
        {
            if (!HasObservation)
                return;
            float leftError = FootTargetForceN - (float)LastObservation.LeftFootFloorForceN;
            float rightError = FootTargetForceN - (float)LastObservation.RightFootFloorForceN;
            _commonDepthM = Mathf.Clamp(_commonDepthM + FootCommonGainMPerNs * 0.5f * (leftError + rightError) * _dt,
                FootDepthMinM, FootDepthMaxM);
            _differentialDepthM = Mathf.Clamp(_differentialDepthM + FootDifferentialGainMPerNs * 0.5f * (leftError - rightError) * _dt,
                -0.5f * (FootDepthMaxM - FootDepthMinM), 0.5f * (FootDepthMaxM - FootDepthMinM));
            _leftLegDepthM = Mathf.Clamp(_commonDepthM + _differentialDepthM, FootDepthMinM, FootDepthMaxM);
            _rightLegDepthM = Mathf.Clamp(_commonDepthM - _differentialDepthM, FootDepthMinM, FootDepthMaxM);
        }

        private float Step(float s, float ratePerSecond, float bound)
        {
            float maxDelta = PathAccelerationPerS2 * _dt;
            _commandedRate = Mathf.Clamp(ratePerSecond, _commandedRate - maxDelta, _commandedRate + maxDelta);
            ratePerSecond = _commandedRate;
            float next = s + ratePerSecond * _dt;
            next = ratePerSecond >= 0f ? Mathf.Min(next, Mathf.Max(bound, s)) : Mathf.Max(next, Mathf.Min(bound, s));
            next = Mathf.Clamp01(next);
            SRate = (next - s) / _dt;
            return next;
        }
    }
}
