using System;
using System.Collections.Generic;
using PowerliftingSimulator.Foundation;

namespace PowerliftingSimulator.Squat
{
    public readonly struct SquatReferencePose
    {
        public SquatReferencePose(
            float ankleDorsiflexionRad,
            float kneeFlexionRad,
            float hipFlexionRad,
            float trunkFlexionRad,
            float shoulderFlexionRad,
            float elbowFlexionRad,
            float wristExtensionRad)
        {
            AnkleDorsiflexionRad = ankleDorsiflexionRad;
            KneeFlexionRad = kneeFlexionRad;
            HipFlexionRad = hipFlexionRad;
            TrunkFlexionRad = trunkFlexionRad;
            ShoulderFlexionRad = shoulderFlexionRad;
            ElbowFlexionRad = elbowFlexionRad;
            WristExtensionRad = wristExtensionRad;
        }

        public float AnkleDorsiflexionRad { get; }
        public float KneeFlexionRad { get; }
        public float HipFlexionRad { get; }
        public float TrunkFlexionRad { get; }
        public float ShoulderFlexionRad { get; }
        public float ElbowFlexionRad { get; }
        public float WristExtensionRad { get; }

        public SquatReferencePose WithTrunkFlexion(float trunkFlexionRad) => new SquatReferencePose(
            AnkleDorsiflexionRad,
            KneeFlexionRad,
            HipFlexionRad,
            trunkFlexionRad,
            ShoulderFlexionRad,
            ElbowFlexionRad,
            WristExtensionRad);

        public SquatReferencePose ApplyBrace(float brace01)
        {
            float brace = Math.Max(0f, Math.Min(1f, brace01));
            float adjustedTrunk = Math.Max(0f, TrunkFlexionRad - UnitContract.DegreesToRadians(3f) * brace);
            return WithTrunkFlexion(adjustedTrunk);
        }
    }

    public readonly struct SquatReferenceWaypointRecord
    {
        public SquatReferenceWaypointRecord(
            SquatReferenceWaypoint waypoint,
            float phase,
            SquatReferencePose pose)
        {
            Waypoint = waypoint;
            Phase = phase;
            Pose = pose;
        }

        public SquatReferenceWaypoint Waypoint { get; }
        public float Phase { get; }
        public SquatReferencePose Pose { get; }
    }

    public readonly struct SquatReferenceSample
    {
        public SquatReferenceSample(
            SquatState state,
            SquatPhaseDirection direction,
            float phase,
            SquatReferencePose pose,
            SquatReferencePose derivativePerPhase,
            float balanceIntentX)
        {
            State = state;
            Direction = direction;
            Phase = phase;
            Pose = pose;
            DerivativePerPhase = derivativePerPhase;
            BalanceIntentX = balanceIntentX;
            BalanceCorrectionApplied = false;
        }

        public SquatState State { get; }
        public SquatPhaseDirection Direction { get; }
        public float Phase { get; }
        public SquatReferencePose Pose { get; }
        public SquatReferencePose DerivativePerPhase { get; }
        public float BalanceIntentX { get; }
        public bool BalanceCorrectionApplied { get; }
    }

    public sealed class CubicHermiteCurve
    {
        private readonly float[] _phases;
        private readonly float[] _values;
        private readonly float[] _tangentsPerPhase;

        public CubicHermiteCurve(float[] phases, float[] values, float[] tangentsPerPhase)
        {
            if (phases == null || values == null || tangentsPerPhase == null ||
                phases.Length < 2 || phases.Length != values.Length || phases.Length != tangentsPerPhase.Length)
                throw new ArgumentException("Hermite curves require matching arrays with at least two keys.");

            _phases = (float[])phases.Clone();
            _values = (float[])values.Clone();
            _tangentsPerPhase = (float[])tangentsPerPhase.Clone();
            for (int index = 0; index < _phases.Length; index++)
            {
                if (!Finite(_phases[index]) || !Finite(_values[index]) || !Finite(_tangentsPerPhase[index]))
                    throw new ArgumentException("Hermite curve keys must be finite.");
                if (index > 0 && _phases[index] <= _phases[index - 1])
                    throw new ArgumentException("Hermite curve phases must be strictly increasing.");
            }
        }

        public int KeyCount => _phases.Length;

        public float PhaseAt(int index) => _phases[index];
        public float ValueAt(int index) => _values[index];
        public float TangentAt(int index) => _tangentsPerPhase[index];

        public float Evaluate(float phase)
        {
            FindSegment(phase, out int index, out float localPhase, out float duration);
            float leftValue = _values[index];
            float rightValue = _values[index + 1];
            float leftTangent = _tangentsPerPhase[index] * duration;
            float rightTangent = _tangentsPerPhase[index + 1] * duration;
            return Hermite(leftValue, leftTangent, rightValue, rightTangent, localPhase);
        }

        public float Derivative(float phase)
        {
            FindSegment(phase, out int index, out float localPhase, out float duration);
            float leftValue = _values[index];
            float rightValue = _values[index + 1];
            float leftTangent = _tangentsPerPhase[index] * duration;
            float rightTangent = _tangentsPerPhase[index + 1] * duration;
            return HermiteDerivative(leftValue, leftTangent, rightValue, rightTangent, localPhase) / duration;
        }

        private void FindSegment(float phase, out int index, out float localPhase, out float duration)
        {
            float clamped = Math.Max(_phases[0], Math.Min(_phases[_phases.Length - 1], phase));
            if (clamped >= _phases[_phases.Length - 1])
                index = _phases.Length - 2;
            else if (clamped <= _phases[0])
                index = 0;
            else
            {
                index = Array.BinarySearch(_phases, clamped);
                if (index < 0)
                    index = ~index - 1;
            }

            duration = _phases[index + 1] - _phases[index];
            localPhase = (clamped - _phases[index]) / duration;
        }

        private static float Hermite(float p0, float m0, float p1, float m1, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return (2f * t3 - 3f * t2 + 1f) * p0 +
                (t3 - 2f * t2 + t) * m0 +
                (-2f * t3 + 3f * t2) * p1 +
                (t3 - t2) * m1;
        }

        private static float HermiteDerivative(float p0, float m0, float p1, float m1, float t) =>
            (6f * t * t - 6f * t) * p0 +
            (3f * t * t - 4f * t + 1f) * m0 +
            (-6f * t * t + 6f * t) * p1 +
            (3f * t * t - 2f * t) * m1;

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public sealed class SquatReferenceProfile
    {
        private const string ProfileIdValue = "CANONICAL_POWERLIFTING_SQUAT_V1";
        private const string ClaimClassValue = "BIOMECHANICALLY_INFORMED_GAME_CALIBRATION";
        private static readonly float[] Phases = { 0f, 0.25f, 0.55f, 0.64f, 0.82f, 1f };
        private static readonly SquatReferenceProfile Canonical = CreateCanonical();

        private readonly CubicHermiteCurve _descentAnkle;
        private readonly CubicHermiteCurve _descentKnee;
        private readonly CubicHermiteCurve _descentHip;
        private readonly CubicHermiteCurve _descentTrunk;
        private readonly CubicHermiteCurve _descentShoulder;
        private readonly CubicHermiteCurve _descentElbow;
        private readonly CubicHermiteCurve _descentWrist;
        private readonly CubicHermiteCurve _ascentAnkle;
        private readonly CubicHermiteCurve _ascentKnee;
        private readonly CubicHermiteCurve _ascentHip;
        private readonly CubicHermiteCurve _ascentTrunk;
        private readonly CubicHermiteCurve _ascentShoulder;
        private readonly CubicHermiteCurve _ascentElbow;
        private readonly CubicHermiteCurve _ascentWrist;
        private readonly SquatReferenceWaypointRecord[] _waypoints;

        private SquatReferenceProfile(
            SquatReferenceWaypointRecord[] waypoints,
            CubicHermiteCurve[] descent,
            CubicHermiteCurve[] ascent)
        {
            _waypoints = (SquatReferenceWaypointRecord[])waypoints.Clone();
            _descentAnkle = descent[0];
            _descentKnee = descent[1];
            _descentHip = descent[2];
            _descentTrunk = descent[3];
            _descentShoulder = descent[4];
            _descentElbow = descent[5];
            _descentWrist = descent[6];
            _ascentAnkle = ascent[0];
            _ascentKnee = ascent[1];
            _ascentHip = ascent[2];
            _ascentTrunk = ascent[3];
            _ascentShoulder = ascent[4];
            _ascentElbow = ascent[5];
            _ascentWrist = ascent[6];
        }

        public static SquatReferenceProfile CanonicalPowerliftingSquatV1 => Canonical;
        public string ProfileId => ProfileIdValue;
        public string ClaimClass => ClaimClassValue;
        public IReadOnlyList<SquatReferenceWaypointRecord> Waypoints => _waypoints;
        public float PhaseStart => 0f;
        public float PhaseEnd => 1f;
        public float ReversalPoseDiscontinuity => PoseDistance(Evaluate(1f, SquatPhaseDirection.Descent), Evaluate(1f, SquatPhaseDirection.Ascent));
        public float LockoutPoseDiscontinuity => PoseDistance(Evaluate(0f, SquatPhaseDirection.Descent), Evaluate(0f, SquatPhaseDirection.Ascent));

        public SquatReferencePose Evaluate(float phase, SquatPhaseDirection direction)
        {
            SelectCurves(direction, out CubicHermiteCurve ankle, out CubicHermiteCurve knee, out CubicHermiteCurve hip,
                out CubicHermiteCurve trunk, out CubicHermiteCurve shoulder, out CubicHermiteCurve elbow,
                out CubicHermiteCurve wrist);
            return new SquatReferencePose(
                ankle.Evaluate(phase), knee.Evaluate(phase), hip.Evaluate(phase), trunk.Evaluate(phase),
                shoulder.Evaluate(phase), elbow.Evaluate(phase), wrist.Evaluate(phase));
        }

        public SquatReferencePose Derivative(float phase, SquatPhaseDirection direction)
        {
            SelectCurves(direction, out CubicHermiteCurve ankle, out CubicHermiteCurve knee, out CubicHermiteCurve hip,
                out CubicHermiteCurve trunk, out CubicHermiteCurve shoulder, out CubicHermiteCurve elbow,
                out CubicHermiteCurve wrist);
            return new SquatReferencePose(
                ankle.Derivative(phase), knee.Derivative(phase), hip.Derivative(phase), trunk.Derivative(phase),
                shoulder.Derivative(phase), elbow.Derivative(phase), wrist.Derivative(phase));
        }

        private void SelectCurves(
            SquatPhaseDirection direction,
            out CubicHermiteCurve ankle,
            out CubicHermiteCurve knee,
            out CubicHermiteCurve hip,
            out CubicHermiteCurve trunk,
            out CubicHermiteCurve shoulder,
            out CubicHermiteCurve elbow,
            out CubicHermiteCurve wrist)
        {
            if (direction == SquatPhaseDirection.Ascent)
            {
                ankle = _ascentAnkle;
                knee = _ascentKnee;
                hip = _ascentHip;
                trunk = _ascentTrunk;
                shoulder = _ascentShoulder;
                elbow = _ascentElbow;
                wrist = _ascentWrist;
                return;
            }

            ankle = _descentAnkle;
            knee = _descentKnee;
            hip = _descentHip;
            trunk = _descentTrunk;
            shoulder = _descentShoulder;
            elbow = _descentElbow;
            wrist = _descentWrist;
        }

        private static SquatReferenceProfile CreateCanonical()
        {
            SquatReferencePose standing = Pose(0f, 0f, 0f, 0f, 0f, 0f, 0f);
            SquatReferencePose quarter = Pose(10f, 30f, 25f, 8f, 48f, 25f, 8f);
            SquatReferencePose nearParallel = Pose(16f, 68f, 54f, 17f, 58f, 38f, 10f);
            SquatReferencePose sticking = Pose(15f, 61f, 48f, 15f, 55f, 34f, 9f);
            SquatReferencePose earlyAscent = Pose(18f, 82f, 63f, 20f, 60f, 40f, 10f);
            SquatReferencePose legalBottom = Pose(21f, 108f, 84f, 24f, 64f, 46f, 11f);
            SquatReferenceWaypointRecord[] waypoints =
            {
                new(SquatReferenceWaypoint.STANDING, 0f, standing),
                new(SquatReferenceWaypoint.QUARTER_DESCENT, 0.25f, quarter),
                new(SquatReferenceWaypoint.NEAR_PARALLEL, 0.55f, nearParallel),
                new(SquatReferenceWaypoint.LEGAL_BOTTOM, 1f, legalBottom),
                new(SquatReferenceWaypoint.EARLY_ASCENT, 0.82f, earlyAscent),
                new(SquatReferenceWaypoint.STICKING, 0.64f, sticking),
                new(SquatReferenceWaypoint.LOCKOUT, 0f, standing)
            };

            SquatReferencePose[] sorted = { standing, quarter, nearParallel, sticking, earlyAscent, legalBottom };
            CubicHermiteCurve[] descent = BuildCurves(sorted, 0.88f);
            CubicHermiteCurve[] ascent = BuildCurves(sorted, 1.04f);
            return new SquatReferenceProfile(waypoints, descent, ascent);
        }

        private static CubicHermiteCurve[] BuildCurves(SquatReferencePose[] poses, float tangentScale)
        {
            return new[]
            {
                BuildCurve(poses, pose => pose.AnkleDorsiflexionRad, tangentScale),
                BuildCurve(poses, pose => pose.KneeFlexionRad, tangentScale),
                BuildCurve(poses, pose => pose.HipFlexionRad, tangentScale),
                BuildCurve(poses, pose => pose.TrunkFlexionRad, tangentScale),
                BuildCurve(poses, pose => pose.ShoulderFlexionRad, tangentScale),
                BuildCurve(poses, pose => pose.ElbowFlexionRad, tangentScale),
                BuildCurve(poses, pose => pose.WristExtensionRad, tangentScale)
            };
        }

        private static CubicHermiteCurve BuildCurve(
            SquatReferencePose[] poses,
            Func<SquatReferencePose, float> selector,
            float tangentScale)
        {
            float[] values = new float[poses.Length];
            float[] tangents = new float[poses.Length];
            for (int index = 0; index < poses.Length; index++)
                values[index] = selector(poses[index]);

            tangents[0] = (values[1] - values[0]) / (Phases[1] - Phases[0]) * tangentScale;
            tangents[tangents.Length - 1] =
                (values[values.Length - 1] - values[values.Length - 2]) /
                (Phases[Phases.Length - 1] - Phases[Phases.Length - 2]) * tangentScale;
            for (int index = 1; index < tangents.Length - 1; index++)
                tangents[index] =
                    (values[index + 1] - values[index - 1]) /
                    (Phases[index + 1] - Phases[index - 1]) * tangentScale;
            return new CubicHermiteCurve(Phases, values, tangents);
        }

        private static SquatReferencePose Pose(
            float ankleDegrees,
            float kneeDegrees,
            float hipDegrees,
            float trunkDegrees,
            float shoulderDegrees,
            float elbowDegrees,
            float wristDegrees) => new SquatReferencePose(
                UnitContract.DegreesToRadians(ankleDegrees),
                UnitContract.DegreesToRadians(kneeDegrees),
                UnitContract.DegreesToRadians(hipDegrees),
                UnitContract.DegreesToRadians(trunkDegrees),
                UnitContract.DegreesToRadians(shoulderDegrees),
                UnitContract.DegreesToRadians(elbowDegrees),
                UnitContract.DegreesToRadians(wristDegrees));

        private static float PoseDistance(SquatReferencePose left, SquatReferencePose right) => Math.Max(
            Math.Max(Math.Abs(left.AnkleDorsiflexionRad - right.AnkleDorsiflexionRad),
                Math.Abs(left.KneeFlexionRad - right.KneeFlexionRad)),
            Math.Max(Math.Abs(left.HipFlexionRad - right.HipFlexionRad),
                Math.Max(Math.Abs(left.TrunkFlexionRad - right.TrunkFlexionRad),
                    Math.Max(Math.Abs(left.ShoulderFlexionRad - right.ShoulderFlexionRad),
                        Math.Max(Math.Abs(left.ElbowFlexionRad - right.ElbowFlexionRad),
                            Math.Abs(left.WristExtensionRad - right.WristExtensionRad))))));
    }

    public static class SquatReferenceMotion
    {
        public const float DescentRatePerSecond = 0.48f;
        public const float AscentRatePerSecond = 0.68f;
        public const float MaxPhaseRatePerSecond = 0.75f;
        public const float MaxPhaseAccelerationPerSecondSquared = 2.2f;
        public const string PhaseConvention = "s_q in [0,1]; 0=standing/lockout, 1=canonical legal-bottom; descent increases, ascent decreases";

        public static SquatReferenceSample Sample(
            SquatState state,
            float phase,
            SquatPhaseDirection direction,
            PlayerIntentFrame intent)
        {
            SquatReferenceProfile profile = SquatReferenceProfile.CanonicalPowerliftingSquatV1;
            SquatReferencePose pose = profile.Evaluate(phase, direction);
            if (intent.Brace01 > 0f)
                pose = pose.ApplyBrace(intent.Brace01);
            return new SquatReferenceSample(
                state,
                direction,
                Math.Max(0f, Math.Min(1f, phase)),
                pose,
                profile.Derivative(phase, direction),
                intent.BalanceX);
        }
    }
}
