using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;

namespace PowerliftingSimulator.Bench.Unity
{
    /// <summary>One sample of a bar path and the arm joint references that realize it.</summary>
    public readonly struct BenchArmSample
    {
        public BenchArmSample(Vector3 barCenter, Quaternion[] arm, double residualM)
        {
            BarCenter = barCenter;
            Arm = arm;
            ResidualM = residualM;
        }

        public Vector3 BarCenter { get; }

        /// <summary>Logical targets in <see cref="BenchSetupSolver.ArmJointIds"/> order.</summary>
        public Quaternion[] Arm { get; }

        /// <summary>Worst grip-anchor position error of the IK solution, metres.</summary>
        public double ResidualM { get; }
    }

    /// <summary>A bar path sampled at equal parameter steps, s in [0, 1].</summary>
    public sealed class BenchArmPath
    {
        private readonly BenchArmSample[] _samples;

        public BenchArmPath(string name, BenchArmSample[] samples)
        {
            Name = name;
            _samples = samples ?? throw new ArgumentNullException(nameof(samples));
            if (samples.Length < 2)
                throw new ArgumentException("A path needs at least two samples.", nameof(samples));
        }

        public string Name { get; }
        public IReadOnlyList<BenchArmSample> Samples => _samples;

        public double MaxResidualM
        {
            get
            {
                double max = 0d;
                foreach (BenchArmSample sample in _samples)
                    max = Math.Max(max, sample.ResidualM);
                return max;
            }
        }

        public Quaternion Evaluate(int joint, float s)
        {
            s = Mathf.Clamp01(s);
            float f = s * (_samples.Length - 1);
            int i = Mathf.Min(_samples.Length - 2, Mathf.FloorToInt(f));
            return PoweredJointController.NormalizeCanonical(
                Quaternion.Slerp(_samples[i].Arm[joint], _samples[i + 1].Arm[joint], f - i));
        }

        public Vector3 BarCenter(float s)
        {
            s = Mathf.Clamp01(s);
            float f = s * (_samples.Length - 1);
            int i = Mathf.Min(_samples.Length - 2, Mathf.FloorToInt(f));
            return Vector3.Lerp(_samples[i].BarCenter, _samples[i + 1].BarCenter, f - i);
        }

        /// <summary>Body-frame logical angular velocity for ds/dt at s.</summary>
        public Vector3 Velocity(int joint, float s, float sRatePerSecond)
        {
            const float ds = 0.01f;
            float a = Mathf.Clamp01(s - ds);
            float b = Mathf.Clamp01(s + ds);
            if (b - a < 1e-6f || Mathf.Abs(sRatePerSecond) < 1e-6f)
                return Vector3.zero;
            Quaternion qa = Evaluate(joint, a);
            Quaternion qb = Evaluate(joint, b);
            Quaternion delta = PoweredJointController.NormalizeCanonical(Quaternion.Inverse(qa) * qb);
            delta.ToAngleAxis(out float degrees, out Vector3 axis);
            if (degrees > 180f)
                degrees -= 360f;
            if (!float.IsFinite(axis.x))
                return Vector3.zero;
            return axis * (degrees * Mathf.Deg2Rad / (b - a) * sRatePerSecond);
        }
    }

    /// <summary>
    /// Everything Gate A derives from the built athlete: the supine setup,
    /// the bench placed around it, the grip and the arm paths. All numbers are
    /// measured from the rig's own colliders, anchors and bones.
    /// </summary>
    public sealed class BenchPlantModel
    {
        public Quaternion SupineRotation;
        public Pose PelvisWorld;
        public float TorsoPitchDeg;
        public float NeckLogicalDeg;
        public Dictionary<string, Quaternion> SetupLogical = new Dictionary<string, Quaternion>(StringComparer.Ordinal);

        /// <summary>Leg drive references (soles aimed into the floor); placement uses SetupLogical.</summary>
        public Dictionary<string, Quaternion> LegReferenceLogical = new Dictionary<string, Quaternion>(StringComparer.Ordinal);
        public BenchEquipmentSpec Equipment;
        public float BarRadius;
        public Vector3 BarRack;
        public Vector3 BarLockout;
        public Vector3 BarTouch;
        public float GripHalfWidth;
        public float ForefingerSpacingM;
        public Vector3 LeftGripAnchorLocal;
        public Vector3 RightGripAnchorLocal;
        public Vector3 LeftIndexLocal;
        public Vector3 RightIndexLocal;
        public Vector3 LeftPalmNormalLocal;
        public Vector3 RightPalmNormalLocal;
        public float RackElbowFlexDeg;
        public float LockoutElbowFlexDeg;
        public float TouchLeftElbowFlexDeg;
        public float TouchRightElbowFlexDeg;
        public float TouchElbowBelowShoulderM;
        public float ChestTopY;
        public float ArmEnvelopeX;
        public float ShoulderMidZ;
        public float HeadGapM;
        public float LeftSoleGapM;
        public float RightSoleGapM;
        public BenchArmPath LiftPath;
        public BenchArmPath PressPath;
        public readonly List<string> Notes = new List<string>();

        public string Describe()
        {
            var b = new StringBuilder();
            CultureInfo c = CultureInfo.InvariantCulture;
            b.AppendLine(string.Format(c, "torsoPitch={0:F2}deg neck={1:F2}deg headGap={2:F4}m soleGap L={3:F4} R={4:F4}m",
                TorsoPitchDeg, NeckLogicalDeg, HeadGapM, LeftSoleGapM, RightSoleGapM));
            b.AppendLine(string.Format(c, "bar rack={0} lockout={1} touch={2} chestTop={3:F4} shoulderMidZ={4:F4}",
                BarRack.ToString("F4"), BarLockout.ToString("F4"), BarTouch.ToString("F4"), ChestTopY, ShoulderMidZ));
            b.AppendLine(string.Format(c, "rest top={0:F4} safety top={1:F4} safety x {2:F3}..{3:F3} (arm envelope {4:F3}) headEndZ={5:F4} uprightZ={6:F4}",
                Equipment.RestTopY, Equipment.SafetyTopY, Equipment.SafetyInnerX, BenchEquipmentSpec.SafetyOuterX, ArmEnvelopeX, Equipment.HeadEndZ, Equipment.UprightZ));
            b.AppendLine(string.Format(c, "grip half={0:F4} forefinger spacing={1:F4} elbow rack={2:F1} lock={3:F1} touch L/R={4:F1}/{5:F1}deg elbowBelowShoulder={6:F4}m",
                GripHalfWidth, ForefingerSpacingM, RackElbowFlexDeg, LockoutElbowFlexDeg, TouchLeftElbowFlexDeg, TouchRightElbowFlexDeg, TouchElbowBelowShoulderM));
            b.AppendLine(string.Format(c, "IK residual lift={0:F5}m press={1:F5}m", LiftPath?.MaxResidualM ?? double.NaN, PressPath?.MaxResidualM ?? double.NaN));
            foreach (string note in Notes)
                b.AppendLine(note);
            return b.ToString();
        }
    }

    public sealed class BenchSetupSolver
    {
        public static readonly string[] ArmJointIds =
        {
            "left_upper_arm", "left_forearm", "left_hand", "right_upper_arm", "right_forearm", "right_hand"
        };

        // Design choices of this game plant, each named and bounded.
        public const float TargetForefingerSpacingM = 0.76f;   // inside the 0.81 m limit
        public const float LateralFootPlacementM = 0.30f;      // sole centre from the midline
        public const float RackElbowFlexDeg = 20f;             // slightly bent arms under the racked bar
        public const float LockoutElbowFlexDeg = 2f;           // drive reference at lockout
        public const float PalmUpTiltDeg = 30f;                // palm faces the feet, tilted up: wrist extension
        public const float TouchBelowChestSurfaceM = 0.004f;   // reference slightly inside the chest so contact happens
        public const float TouchCaudalOffsetM = 0.06f;         // lower-chest touch, caudal of the thorax centre
        public const float LiftOffClearanceM = 0.060f;         // well above the 0.020 m front lip
        /// <summary>
        /// Initial contact closure of the supported surfaces (thorax, pelvis,
        /// head) into the pad. A unilateral contact carries load only once it
        /// is closed; at exactly zero gap the trunk drives let the arms hold
        /// the upper body off the pad on the racked bar (measured: head and
        /// shoulders 0 N, rests 690 N for a 245 N bar).
        /// </summary>
        public const float SupportPreloadM = 0.0015f;

        /// <summary>
        /// The leg drive references aim the soles this far into the floor
        /// (bodies are placed with the soles on it). The finite hip/knee/ankle
        /// drives then press the feet down with a measured force: leg drive.
        /// Measured without it: a foot unloaded to 0 N right after Start.
        /// </summary>
        public const float FootDriveIntoFloorM = 0.004f;

        /// <summary>Grip point moved from mid-palm toward the wrist: the bar low in the palm.</summary>
        public const float GripTowardWristM = 0.020f;
        public const float ShouldersFromUprightLineM = 0.20f;  // the lifter's chosen lying position
        public const float SafetyBelowTouchM = 0.035f;
        public const float LimitMarginDeg = 4f;
        public const int PathSamples = 41;

        private float _lastLeftRollDeg = PalmUpTiltDeg;
        private float _lastRightRollDeg = PalmUpTiltDeg;

        /// <summary>Solved bar roll (palm tilt) of the most recent arm solution, degrees.</summary>
        public float LastRollDeg { get; private set; }

        private readonly BenchKinematics _kin;
        private readonly PhysicalAthleteRig _rig;

        public BenchSetupSolver(PhysicalAthleteRig rig)
        {
            _rig = rig ?? throw new ArgumentNullException(nameof(rig));
            _kin = new BenchKinematics(rig);
        }

        public BenchKinematics Kinematics => _kin;

        public BenchPlantModel Solve(float barRadius)
        {
            var model = new BenchPlantModel { BarRadius = barRadius };
            Animator reference = _rig.ReferenceAnimator;
            Vector3 forward = reference.transform.root.forward;
            forward.y = 0f;
            forward.Normalize();
            Quaternion source = Quaternion.LookRotation(forward, Vector3.up);
            Quaternion target = Quaternion.LookRotation(Vector3.up, Vector3.back);
            model.SupineRotation = target * Quaternion.Inverse(source);
            model.Notes.Add($"standing forward {forward:F3}; supine maps anterior to +Y and superior to -Z");

            Quaternion pelvisStanding = _rig.Segments["pelvis"].Body.rotation;
            var logical = model.SetupLogical;

            // 1. Torso pitch so thorax and pelvis bear on the pad together.
            float pitch = Bisect(-25f, 25f, deg =>
            {
                Pose root = new Pose(Vector3.zero, Quaternion.AngleAxis(deg, Vector3.right) * model.SupineRotation * pelvisStanding);
                Dictionary<string, Pose> poses = _kin.Solve(root, logical);
                return LowestPoint("thorax", poses) - LowestPoint("pelvis", poses);
            });
            model.TorsoPitchDeg = pitch;
            Quaternion rootRotation = Quaternion.AngleAxis(pitch, Vector3.right) * model.SupineRotation * pelvisStanding;

            // 2. Place on the pad: the lower of thorax/pelvis at the pad top, midline at x = 0.
            Dictionary<string, Pose> neutral = _kin.Solve(new Pose(Vector3.zero, rootRotation), logical);
            float lowest = Mathf.Min(LowestPoint("thorax", neutral), LowestPoint("pelvis", neutral));
            // Lying position: the shoulder joints a chosen distance toward the
            // feet from the bar-rest line. The rests sit in front of the
            // posts, whose centre is 0.22 m from the head end of the pad
            // (2.6). The head must still rest on the pad.
            const float uprightZ = 0f; // bar-rest line
            float shoulderZ = 0.5f * (_kin.JointAnchorWorld(neutral, "left_upper_arm").z + _kin.JointAnchorWorld(neutral, "right_upper_arm").z);
            Vector3 offset = new Vector3(-neutral["pelvis"].position.x, BenchEquipmentSpec.PadTopY - SupportPreloadM - lowest,
                uprightZ + ShouldersFromUprightLineM - shoulderZ);
            Pose pelvisWorld = new Pose(neutral["pelvis"].position + offset, rootRotation);
            model.PelvisWorld = pelvisWorld;
            float headEndZ = uprightZ - BenchEquipmentSpec.RestSetbackFromPostM - (float)BenchRulebook.HeadBeyondUprightCenterM;

            // 3. Neck: rest the head on the pad.
            float neck = Bisect(-35f, 35f, deg =>
            {
                logical["head_neck"] = Quaternion.AngleAxis(deg, Vector3.right);
                Dictionary<string, Pose> p = _kin.Solve(pelvisWorld, logical);
                return LowestPoint("head_neck", p) - (BenchEquipmentSpec.PadTopY - SupportPreloadM);
            });
            logical["head_neck"] = Quaternion.AngleAxis(neck, Vector3.right);
            model.NeckLogicalDeg = neck;
            Dictionary<string, Pose> poses0 = _kin.Solve(pelvisWorld, logical);
            model.HeadGapM = LowestPoint("head_neck", poses0) - (BenchEquipmentSpec.PadTopY - SupportPreloadM);
            if (Mathf.Abs(model.HeadGapM) > 0.002f)
                model.Notes.Add($"REJECT: head cannot rest on the pad within neck +/-35 deg (gap {model.HeadGapM:F4} m)");
            CapsuleCollider headCapsule = (CapsuleCollider)_rig.Segments["head_neck"].Collider;
            float headTopZ = CapsuleCenter("head_neck", poses0).z - headCapsule.height * 0.5f;
            model.Notes.Add(string.Format(CultureInfo.InvariantCulture, "head top z={0:F3} (pad head end {1:F3})", headTopZ, headEndZ));
            if (CapsuleCenter("head_neck", poses0).z - headCapsule.radius < headEndZ)
                model.Notes.Add("REJECT: the head is not over the pad");

            // 4. Legs: soles flat on the floor.
            SolveLeg(model, poses0, "left", -1f, 0f, model.SetupLogical);
            SolveLeg(model, poses0, "right", 1f, 0f, model.SetupLogical);
            SolveLeg(model, poses0, "left", -1f, -FootDriveIntoFloorM, model.LegReferenceLogical);
            SolveLeg(model, poses0, "right", 1f, -FootDriveIntoFloorM, model.LegReferenceLogical);
            Dictionary<string, Pose> poses1 = _kin.Solve(pelvisWorld, logical);
            model.LeftSoleGapM = LowestPoint("left_foot", poses1);
            model.RightSoleGapM = LowestPoint("right_foot", poses1);
            if (LowestPoint("pelvis", poses1) < BenchEquipmentSpec.PadTopY - SupportPreloadM - 0.002f)
                model.Notes.Add("REJECT: pelvis penetrates the pad after the leg solve");

            // 5. Hand frames measured on the reference rig (same avatar as the visible skin).
            MeasureHandFrames(model);

            // 6. Chest, shoulders, grip width and keyframes.
            Pose thorax = poses1["thorax"];
            Vector3 leftShoulder = _kin.JointAnchorWorld(poses1, "left_upper_arm");
            Vector3 rightShoulder = _kin.JointAnchorWorld(poses1, "right_upper_arm");
            model.ShoulderMidZ = 0.5f * (leftShoulder.z + rightShoulder.z);
            float touchZ = thorax.position.z + TouchCaudalOffsetM;
            model.ChestTopY = AnteriorSurfaceY("thorax", poses1, touchZ);

            float half = 0.40f;
            for (int iteration = 0; iteration < 4; iteration++)
            {
                model.GripHalfWidth = half;
                Quaternion[] lock0 = SolveArmsAtFreeHeight(thorax, model, model.ShoulderMidZ, LockoutElbowFlexDeg, null, out float yLock, out _);
                Dictionary<string, Pose> armPoses = ArmPoses(thorax, lock0);
                float spacing = Mathf.Abs(armPoses["right_hand"].position.x + (armPoses["right_hand"].rotation * model.RightIndexLocal).x -
                    (armPoses["left_hand"].position.x + (armPoses["left_hand"].rotation * model.LeftIndexLocal).x));
                model.ForefingerSpacingM = spacing;
                half += 0.5f * (TargetForefingerSpacingM - spacing);
                _ = yLock;
            }

            Quaternion[] lockout = SolveArmsAtFreeHeight(thorax, model, model.ShoulderMidZ, LockoutElbowFlexDeg, null, out float lockY, out double lockRes);
            model.BarLockout = new Vector3(0f, lockY, model.ShoulderMidZ);
            _leftWristHold = lockout[2];
            _rightWristHold = lockout[5];
            model.LockoutElbowFlexDeg = LockoutElbowFlexDeg;
            Quaternion[] rack = SolveArmsAtFreeHeight(thorax, model, uprightZ, RackElbowFlexDeg, lockout, out float rackY, out double rackRes);
            model.BarRack = new Vector3(0f, rackY, uprightZ);
            model.RackElbowFlexDeg = RackElbowFlexDeg;
            model.BarTouch = new Vector3(0f, model.ChestTopY + barRadius - TouchBelowChestSurfaceM, touchZ);
            if (lockRes > 0.003 || rackRes > 0.003)
                model.Notes.Add($"REJECT: lockout/rack keyframes unreachable (residual {lockRes:F4}/{rackRes:F4} m)");

            // 7. Paths.
            // The lift-off moves a bar that sits in the hooks: the wrist must
            // stay put (a changing wrist reference cannot be realized while the
            // hand is locked to a supported bar; measured wrist demand 2.0).
            _wristHoldWeight = 20d;
            // Built from the lockout back to the rests so its lockout end is
            // the same configuration the press path starts from.
            model.LiftPath = BuildPath("liftoff", thorax, model, lockout, t =>
            {
                Vector3 up = model.BarRack + Vector3.up * LiftOffClearanceM;
                return t < 0.3f
                    ? Vector3.Lerp(model.BarRack, up, t / 0.3f)
                    : Vector3.Lerp(up, model.BarLockout, Smooth((t - 0.3f) / 0.7f));
            }, s => s >= 0.999f ? LockoutElbowFlexDeg : (float?)null, reverse: true);
            // Same lesson on the press path: a drifting wrist reference against
            // a loaded locked grip saturates the wrist drives through the
            // velocity feedforward (measured demand 1.9 at 4-5 deg error).
            _wristHoldWeight = 20d;
            model.PressPath = BuildPath("press", thorax, model, lockout, t =>
            {
                float e = Smooth(t);
                return new Vector3(0f, Mathf.Lerp(model.BarLockout.y, model.BarTouch.y, e),
                    Mathf.Lerp(model.BarLockout.z, model.BarTouch.z, Mathf.Sin(t * Mathf.PI * 0.5f)));
            }, s => s <= 0.001f ? LockoutElbowFlexDeg : (float?)null);

            // Equipment from the measured lifter: rests at the racked height,
            // safeties below the touch and outside the arm envelope.
            float restTop = model.BarRack.y - barRadius;
            float safetyTop = model.BarTouch.y - barRadius - SafetyBelowTouchM;
            model.ArmEnvelopeX = ArmEnvelopeX(thorax, model, safetyTop + 0.15f);
            float safetyInner = model.ArmEnvelopeX + 0.015f;
            model.Equipment = new BenchEquipmentSpec(headEndZ, restTop, safetyTop, uprightZ - 0.10f, touchZ + 0.25f, safetyInner);
            foreach (string violation in model.Equipment.Violations())
                model.Notes.Add("REJECT (IPF 2.6 / clearance): " + violation);

            // Arms in the setup: gripping the racked bar.
            Quaternion[] rackArms = model.LiftPath.Samples[0].Arm;
            for (int i = 0; i < ArmJointIds.Length; i++)
                logical[ArmJointIds[i]] = rackArms[i];

            Dictionary<string, Pose> touchPoses = ArmPoses(thorax, model.PressPath.Samples[PathSamples - 1].Arm);
            model.TouchLeftElbowFlexDeg = PoweredJointController.SignedTwistRadians(model.PressPath.Samples[PathSamples - 1].Arm[1], Vector3.right) * Mathf.Rad2Deg;
            model.TouchRightElbowFlexDeg = PoweredJointController.SignedTwistRadians(model.PressPath.Samples[PathSamples - 1].Arm[4], Vector3.right) * Mathf.Rad2Deg;
            float elbowL = touchPoses["left_forearm"].position.y;
            float shoulderY = Mathf.Max(leftShoulder.y, rightShoulder.y);
            model.TouchElbowBelowShoulderM = shoulderY - Mathf.Max(
                _kin.JointAnchorWorld(touchPoses, "left_forearm").y, _kin.JointAnchorWorld(touchPoses, "right_forearm").y);
            _ = elbowL;
            return model;
        }

        public static bool IsRejected(BenchPlantModel model)
        {
            foreach (string note in model.Notes)
            {
                if (note.StartsWith("REJECT", StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        // ---------------- legs ----------------

        private void SolveLeg(BenchPlantModel model, Dictionary<string, Pose> poses, string side, float sign, float soleY,
            Dictionary<string, Quaternion> output)
        {
            string thighId = side + "_thigh", shankId = side + "_shank", footId = side + "_foot";
            BenchKinematics.JointInfo hip = _kin.Joint(thighId);
            BenchKinematics.JointInfo knee = _kin.Joint(shankId);
            BenchKinematics.JointInfo ankle = _kin.Joint(footId);
            Pose pelvis = poses["pelvis"];
            BoxCollider footBox = (BoxCollider)_rig.Segments[footId].Collider;
            Vector3 soleLocal = footBox.center - new Vector3(0f, footBox.size.y * 0.5f, 0f);

            // Seed: knee flexed 90 deg.
            double[] p = new double[5];
            p[3] = 90d * Mathf.Deg2Rad;
            double[] seed = (double[])p.Clone();
            double[] solved = BenchKinematics.LevenbergMarquardt(p, x =>
            {
                Quaternion qh = BenchKinematics.FromParameters(hip, x, 0);
                Quaternion qk = BenchKinematics.FromParameters(knee, x, 3);
                Quaternion qa = BenchKinematics.FromParameters(ankle, x, 4);
                Pose thigh = BenchKinematics.ChildPose(pelvis, hip, qh);
                Pose shank = BenchKinematics.ChildPose(thigh, knee, qk);
                Pose foot = BenchKinematics.ChildPose(shank, ankle, qa);
                Vector3 sole = foot.position + foot.rotation * soleLocal;
                Vector3 kneeWorld = shank.position + shank.rotation * knee.Anchor;
                Vector3 ankleWorld = foot.position + foot.rotation * ankle.Anchor;
                Vector3 footUp = foot.rotation * Vector3.up;
                Vector3 footForward = foot.rotation * Vector3.forward;
                var r = new List<double>
                {
                    50d * (sole.y - soleY),
                    5d * (sole.x - sign * LateralFootPlacementM),
                    2d * (ankleWorld.z - kneeWorld.z),
                    10d * footUp.x,
                    10d * footUp.z,
                    2d * footForward.x
                };
                BenchKinematics.LimitResiduals(hip, qh, LimitMarginDeg, r, 20d);
                BenchKinematics.LimitResiduals(knee, qk, LimitMarginDeg, r, 20d);
                BenchKinematics.LimitResiduals(ankle, qa, LimitMarginDeg, r, 20d);
                for (int i = 0; i < x.Length; i++)
                    r.Add(0.02d * (x[i] - seed[i]));
                return r;
            }, 200, out double cost);

            output[thighId] = BenchKinematics.FromParameters(hip, solved, 0);
            output[shankId] = BenchKinematics.FromParameters(knee, solved, 3);
            output[footId] = BenchKinematics.FromParameters(ankle, solved, 4);
            if (output != model.SetupLogical)
                return;
            Pose t1 = BenchKinematics.ChildPose(pelvis, hip, model.SetupLogical[thighId]);
            Pose s1 = BenchKinematics.ChildPose(t1, knee, model.SetupLogical[shankId]);
            Pose f1 = BenchKinematics.ChildPose(s1, ankle, model.SetupLogical[footId]);
            Vector3 sole1 = f1.position + f1.rotation * soleLocal;
            Vector3 knee1 = s1.position + s1.rotation * knee.Anchor;
            Vector3 ankle1 = f1.position + f1.rotation * ankle.Anchor;
            model.Notes.Add(string.Format(CultureInfo.InvariantCulture,
                "{0} leg: hip rotvec=({1:F1},{2:F1},{3:F1}) knee={4:F1} ankle={5:F1} deg cost={6:E2}; sole={7} footTilt={8:F1}deg shankLean={9:F1}deg toeYaw={10:F1}deg",
                side, solved[0] * Mathf.Rad2Deg, solved[1] * Mathf.Rad2Deg, solved[2] * Mathf.Rad2Deg,
                solved[3] * Mathf.Rad2Deg, solved[4] * Mathf.Rad2Deg, cost, sole1.ToString("F3"),
                Vector3.Angle(f1.rotation * Vector3.up, Vector3.up), Vector3.Angle(ankle1 - knee1, Vector3.down),
                Vector3.SignedAngle(Vector3.forward, Vector3.ProjectOnPlane(f1.rotation * Vector3.forward, Vector3.up), Vector3.up)));
        }

        // ---------------- arms ----------------

        private void MeasureHandFrames(BenchPlantModel model)
        {
            Animator reference = _rig.ReferenceAnimator;
            foreach (bool left in new[] { true, false })
            {
                PhysicalAthleteRig.SegmentRuntime hand = _rig.Segments[left ? "left_hand" : "right_hand"];
                Transform bone = reference.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                Transform index = reference.GetBoneTransform(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
                Vector3 palmCenterBone = left ? SquatPalmSurface.LeftPalmCenterLocal : SquatPalmSurface.RightPalmCenterLocal;
                Vector3 palmNormalBone = left ? SquatPalmSurface.LeftPalmNormalLocal : SquatPalmSurface.RightPalmNormalLocal;
                Transform body = hand.Body.transform;
                Vector3 palmCenter = body.InverseTransformPoint(bone.TransformPoint(palmCenterBone));
                Vector3 palmNormal = body.InverseTransformDirection(bone.TransformDirection(palmNormalBone)).normalized;
                Vector3 wristLocal = _rig.PoweredController.GetJoint(left ? "left_hand" : "right_hand").Joint.anchor;
                Vector3 towardWrist = Vector3.ProjectOnPlane(wristLocal - palmCenter, palmNormal).normalized;
                Vector3 anchor = palmCenter + towardWrist * GripTowardWristM + palmNormal * model.BarRadius;
                Vector3 indexLocal = body.InverseTransformPoint(index.position);
                if (left)
                {
                    model.LeftGripAnchorLocal = anchor;
                    model.LeftPalmNormalLocal = palmNormal;
                    model.LeftIndexLocal = indexLocal;
                }
                else
                {
                    model.RightGripAnchorLocal = anchor;
                    model.RightPalmNormalLocal = palmNormal;
                    model.RightIndexLocal = indexLocal;
                }
            }
        }

        private Dictionary<string, Pose> ArmPoses(Pose thorax, Quaternion[] arm)
        {
            var poses = new Dictionary<string, Pose>(StringComparer.Ordinal) { ["thorax"] = thorax };
            for (int i = 0; i < ArmJointIds.Length; i++)
            {
                BenchKinematics.JointInfo joint = _kin.Joint(ArmJointIds[i]);
                poses[ArmJointIds[i]] = BenchKinematics.ChildPose(poses[joint.ParentId], joint, arm[i]);
            }
            return poses;
        }

        private Quaternion[] SolveArmsAtFreeHeight(Pose thorax, BenchPlantModel model, float barZ, float elbowDeg, Quaternion[] seed,
            out float barY, out double residual)
        {
            float y = seed == null ? 1.0f : model.BarLockout.y;
            Quaternion[] arms = new Quaternion[6];
            residual = 0d;
            float ySum = 0f;
            foreach (bool left in new[] { true, false })
            {
                int o = left ? 0 : 3;
                Quaternion[] armSeed = seed == null ? null : new[] { seed[o], seed[o + 1], seed[o + 2] };
                Quaternion[] side = SolveArm(thorax, model, left, new Vector3(0f, y, barZ), elbowDeg, freeHeight: true, armSeed,
                    out Vector3 reached, out double res);
                Array.Copy(side, 0, arms, o, 3);
                ySum += reached.y;
                residual = Math.Max(residual, res);
            }
            barY = ySum * 0.5f;
            return arms;
        }

        /// <summary>
        /// Arm IK in the measured joint frames: grip anchor on the shaft at
        /// x = +/- half width, MCP line along the shaft, palm facing the feet
        /// tilted up, forearm toward vertical, inside the joint limits.
        /// </summary>
        /// <summary>
        /// Wrist configuration held along the paths (from the lockout
        /// keyframe). Null while the keyframe itself is being solved.
        /// </summary>
        private double _wristHoldWeight = 1d;
        private Quaternion? _leftWristHold;
        private Quaternion? _rightWristHold;

        private Quaternion[] SolveArm(Pose thorax, BenchPlantModel model, bool left, Vector3 barCenter, float? elbowDeg, bool freeHeight,
            Quaternion[] seed, out Vector3 reachedBarCenter, out double residualM)
        {
            Quaternion? wristHold = left ? _leftWristHold : _rightWristHold;
            string prefix = left ? "left" : "right";
            BenchKinematics.JointInfo shoulder = _kin.Joint(prefix + "_upper_arm");
            BenchKinematics.JointInfo elbow = _kin.Joint(prefix + "_forearm");
            BenchKinematics.JointInfo wrist = _kin.Joint(prefix + "_hand");
            Vector3 anchor = left ? model.LeftGripAnchorLocal : model.RightGripAnchorLocal;
            Vector3 normal = left ? model.LeftPalmNormalLocal : model.RightPalmNormalLocal;
            Vector3 indexLocal = left ? model.LeftIndexLocal : model.RightIndexLocal;
            float sideSign = left ? -1f : 1f;
            // The bar is a free body: its roll about its own axis is not
            // imposed. The palm faces the feet tilted up by a solved roll,
            // regularized toward the previous sample for continuity.
            int rollIndex = freeHeight ? 8 : 7;
            int n = rollIndex + 1;
            double rollSeed = (left ? _lastLeftRollDeg : _lastRightRollDeg) * Mathf.Deg2Rad;
            double[] p = new double[n];
            p[rollIndex] = rollSeed;
            if (seed != null)
            {
                BenchKinematics.ToParameters(shoulder, seed[0], p, 0);
                BenchKinematics.ToParameters(elbow, seed[1], p, 3);
                BenchKinematics.ToParameters(wrist, seed[2], p, 4);
            }
            else
            {
                // Arm toward the ceiling: seed found by a coarse search below.
                p[3] = 10d * Mathf.Deg2Rad;
            }
            if (freeHeight)
                p[7] = barCenter.y;

            Func<double[], List<double>> residualFunction = x =>
            {
                Quaternion qs = BenchKinematics.FromParameters(shoulder, x, 0);
                Quaternion qe = BenchKinematics.FromParameters(elbow, x, 3);
                Quaternion qw = BenchKinematics.FromParameters(wrist, x, 4);
                Pose upper = BenchKinematics.ChildPose(thorax, shoulder, qs);
                Pose fore = BenchKinematics.ChildPose(upper, elbow, qe);
                Pose hand = BenchKinematics.ChildPose(fore, wrist, qw);
                Vector3 target = new Vector3(sideSign * model.GripHalfWidth, freeHeight ? (float)x[7] : barCenter.y, barCenter.z);
                Vector3 normalTarget = Quaternion.AngleAxis(-(float)(x[rollIndex] * Mathf.Rad2Deg), Vector3.right) * Vector3.forward;
                Vector3 grip = hand.position + hand.rotation * anchor;
                Vector3 n = hand.rotation * normal;
                Vector3 mcp = hand.rotation * (indexLocal - anchor);
                mcp -= Vector3.Project(mcp, n);
                Vector3 elbowWorld = fore.position + fore.rotation * elbow.Anchor;
                Vector3 wristWorld = hand.position + hand.rotation * wrist.Anchor;
                var r = new List<double>
                {
                    60d * (grip.x - target.x), 60d * (grip.y - target.y), 60d * (grip.z - target.z),
                    // The MCP line lies along the shaft, index finger medial
                    // (an overhand, thumb-around grip): left index toward +X,
                    // right index toward -X.
                    1.5d * mcp.normalized.y, 1.5d * mcp.normalized.z, 1.5d * (mcp.normalized.x + sideSign),
                    // Forearm toward vertical, and the bar stacked over the
                    // wrist joint: the minimal-moment posture for the wrist.
                    0.6d * (wristWorld.x - elbowWorld.x), 0.6d * (wristWorld.z - elbowWorld.z),
                    2.0d * (grip.x - wristWorld.x), 2.0d * (grip.z - wristWorld.z)
                };
                if (elbowDeg.HasValue)
                    r.Add(8d * (x[3] - elbowDeg.Value * Mathf.Deg2Rad));
                if (wristHold.HasValue)
                {
                    // Path mode: the hand keeps its lockout relation to the
                    // forearm; the bar rolls with the hands. No palm target.
                    Quaternion dw = Quaternion.Inverse(wristHold.Value) * qw;
                    dw.ToAngleAxis(out float dwDeg, out Vector3 dwAxis);
                    if (dwDeg > 180f) dwDeg -= 360f;
                    Vector3 dwVec = float.IsFinite(dwAxis.x) ? dwAxis * (dwDeg * Mathf.Deg2Rad) : Vector3.zero;
                    r.Add(_wristHoldWeight * dwVec.x); r.Add(_wristHoldWeight * dwVec.y); r.Add(_wristHoldWeight * dwVec.z);
                    r.Add(0d); r.Add(0d); r.Add(0d); r.Add(0d);
                }
                else
                {
                    // Keyframe mode: palm toward the feet at a solved tilt.
                    r.Add(1.5d * (n.x - normalTarget.x)); r.Add(1.5d * (n.y - normalTarget.y)); r.Add(1.5d * (n.z - normalTarget.z));
                    r.Add(0.3d * (x[rollIndex] - rollSeed));
                }
                BenchKinematics.LimitResiduals(shoulder, qs, LimitMarginDeg, r, 25d);
                BenchKinematics.LimitResiduals(elbow, qe, LimitMarginDeg, r, 25d);
                BenchKinematics.LimitResiduals(wrist, qw, LimitMarginDeg, r, 25d);
                return r;
            };

            if (seed == null)
            {
                // Coarse global seed over shoulder rotation vectors: the arm
                // has to swing from its bind direction to the ceiling, which
                // a local solver will not find from identity on its own.
                double best = double.PositiveInfinity;
                double[] bestP = (double[])p.Clone();
                for (int a = -4; a <= 4; a++)
                for (int b = -4; b <= 4; b++)
                for (int c = -4; c <= 4; c++)
                {
                    double[] trial = (double[])p.Clone();
                    trial[0] = a * 0.4; trial[1] = b * 0.4; trial[2] = c * 0.4;
                    double cost = 0d;
                    foreach (double value in residualFunction(trial))
                        cost += value * value;
                    if (cost < best)
                    {
                        best = cost;
                        bestP = trial;
                    }
                }
                p = bestP;
            }

            double[] solved = BenchKinematics.LevenbergMarquardt(p, residualFunction, 300, out _);
            Quaternion s1 = BenchKinematics.FromParameters(shoulder, solved, 0);
            Quaternion e1 = BenchKinematics.FromParameters(elbow, solved, 3);
            Quaternion w1 = BenchKinematics.FromParameters(wrist, solved, 4);
            Pose u = BenchKinematics.ChildPose(thorax, shoulder, s1);
            Pose f = BenchKinematics.ChildPose(u, elbow, e1);
            Pose h = BenchKinematics.ChildPose(f, wrist, w1);
            Vector3 g = h.position + h.rotation * anchor;
            float ty = freeHeight ? (float)solved[7] : barCenter.y;
            residualM = Vector3.Distance(g, new Vector3(sideSign * model.GripHalfWidth, ty, barCenter.z));
            float roll = (float)(solved[rollIndex] * Mathf.Rad2Deg);
            if (left) _lastLeftRollDeg = roll; else _lastRightRollDeg = roll;
            LastRollDeg = roll;
            reachedBarCenter = new Vector3(0f, ty, barCenter.z);
            return new[] { s1, e1, w1 };
        }

        private BenchArmPath BuildPath(string name, Pose thorax, BenchPlantModel model, Quaternion[] start,
            Func<float, Vector3> barAt, Func<float, float?> elbowAt, bool reverse = false)
        {
            var samples = new BenchArmSample[PathSamples];
            Quaternion[] previous = start;
            for (int k = 0; k < PathSamples; k++)
            {
                int i = reverse ? PathSamples - 1 - k : k;
                float t = i / (float)(PathSamples - 1);
                Vector3 bar = barAt(t);
                Quaternion[] arms = new Quaternion[6];
                double residual = 0d;
                foreach (bool left in new[] { true, false })
                {
                    int o = left ? 0 : 3;
                    Quaternion[] side = SolveArm(thorax, model, left, bar, elbowAt(t), false,
                        new[] { previous[o], previous[o + 1], previous[o + 2] }, out _, out double res);
                    Array.Copy(side, 0, arms, o, 3);
                    residual = Math.Max(residual, res);
                }
                samples[i] = new BenchArmSample(bar, arms, residual);
                if (k == 0 || k == PathSamples - 1 || residual > 0.003)
                    model.Notes.Add(string.Format(CultureInfo.InvariantCulture, "{0}[{1}] bar={2} roll L/R={3:F1}/{4:F1}deg residual={5:F4}m",
                        name, i, bar.ToString("F3"), _lastLeftRollDeg, _lastRightRollDeg, residual));
                previous = arms;
            }
            return new BenchArmPath(name, samples);
        }

        /// <summary>
        /// Widest lateral extent of the arm colliders, over both paths, below
        /// <paramref name="belowY"/> (the height band where safeties live).
        /// </summary>
        private float ArmEnvelopeX(Pose thorax, BenchPlantModel model, float belowY)
        {
            float widest = 0f;
            foreach (BenchArmPath path in new[] { model.LiftPath, model.PressPath })
            {
                foreach (BenchArmSample sample in path.Samples)
                {
                    Dictionary<string, Pose> poses = ArmPoses(thorax, sample.Arm);
                    foreach (string id in ArmJointIds)
                    {
                        Collider collider = _rig.Segments[id].Collider;
                        Pose pose = poses[id];
                        if (collider is CapsuleCollider capsule)
                        {
                            float half = Mathf.Max(0f, capsule.height * 0.5f - capsule.radius);
                            for (int k = 0; k <= 8; k++)
                            {
                                Vector3 p = pose.position + pose.rotation * (capsule.center + Vector3.up * Mathf.Lerp(-half, half, k / 8f));
                                if (p.y - capsule.radius < belowY)
                                    widest = Mathf.Max(widest, Mathf.Abs(p.x) + capsule.radius);
                            }
                        }
                        else if (collider is BoxCollider box)
                        {
                            Vector3 e = box.size * 0.5f;
                            for (int c = 0; c < 8; c++)
                            {
                                Vector3 corner = box.center + new Vector3((c & 1) == 0 ? -e.x : e.x, (c & 2) == 0 ? -e.y : e.y, (c & 4) == 0 ? -e.z : e.z);
                                Vector3 p = pose.position + pose.rotation * corner;
                                if (p.y < belowY)
                                    widest = Mathf.Max(widest, Mathf.Abs(p.x));
                            }
                        }
                    }
                }
            }
            return widest;
        }

        // ---------------- geometry ----------------

        private float LowestPoint(string segmentId, IReadOnlyDictionary<string, Pose> poses)
        {
            Collider collider = _rig.Segments[segmentId].Collider;
            Pose pose = poses[segmentId];
            if (collider is BoxCollider box)
            {
                float min = float.PositiveInfinity;
                Vector3 e = box.size * 0.5f;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = box.center + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                    min = Mathf.Min(min, (pose.position + pose.rotation * corner).y);
                }
                return min;
            }

            CapsuleCollider capsule = (CapsuleCollider)collider;
            float halfSegment = Mathf.Max(0f, capsule.height * 0.5f - capsule.radius);
            Vector3 a = pose.position + pose.rotation * (capsule.center + Vector3.up * halfSegment);
            Vector3 b = pose.position + pose.rotation * (capsule.center - Vector3.up * halfSegment);
            return Mathf.Min(a.y, b.y) - capsule.radius;
        }

        private Vector3 CapsuleCenter(string segmentId, IReadOnlyDictionary<string, Pose> poses)
        {
            Collider collider = _rig.Segments[segmentId].Collider;
            Pose pose = poses[segmentId];
            Vector3 center = collider is CapsuleCollider capsule ? capsule.center : ((BoxCollider)collider).center;
            return pose.position + pose.rotation * center;
        }

        /// <summary>Height of a box's upward-facing face along the vertical line x = 0, z = <paramref name="z"/>.</summary>
        private float AnteriorSurfaceY(string segmentId, IReadOnlyDictionary<string, Pose> poses, float z)
        {
            BoxCollider box = (BoxCollider)_rig.Segments[segmentId].Collider;
            Pose pose = poses[segmentId];
            Vector3 upLocal = Quaternion.Inverse(pose.rotation) * Vector3.up;
            int axis = 0;
            for (int i = 1; i < 3; i++)
            {
                if (Mathf.Abs(upLocal[i]) > Mathf.Abs(upLocal[axis]))
                    axis = i;
            }
            float faceSign = Mathf.Sign(upLocal[axis]);
            float face = box.center[axis] + faceSign * box.size[axis] * 0.5f;
            // local(t) = R^-1 ((0, t, z) - p); its axis component equals face.
            Vector3 p0 = Quaternion.Inverse(pose.rotation) * (new Vector3(0f, 0f, z) - pose.position);
            float slope = upLocal[axis];
            return (face - p0[axis]) / slope;
        }

        private static float Bisect(float a, float b, Func<float, float> f)
        {
            float fa = f(a);
            float fb = f(b);
            if (Mathf.Sign(fa) == Mathf.Sign(fb))
                return Mathf.Abs(fa) < Mathf.Abs(fb) ? a : b;
            for (int i = 0; i < 60; i++)
            {
                float m = 0.5f * (a + b);
                float fm = f(m);
                if (Mathf.Sign(fm) == Mathf.Sign(fa))
                {
                    a = m;
                    fa = fm;
                }
                else
                {
                    b = m;
                }
            }
            return 0.5f * (a + b);
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
