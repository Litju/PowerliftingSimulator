using System;

namespace PowerliftingSimulator.Foundation
{
    public enum ReferenceFrame : byte
    {
        World,
        Body,
        Joint,
        ReferenceRig,
        VisibleMesh,
        Bar
    }

    public readonly struct Vector3Value : IEquatable<Vector3Value>
    {
        public Vector3Value(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float X { get; }
        public float Y { get; }
        public float Z { get; }

        public float Length => (float)Math.Sqrt((double)X * X + (double)Y * Y + (double)Z * Z);

        public static Vector3Value Zero => new Vector3Value(0f, 0f, 0f);

        public static Vector3Value operator +(Vector3Value left, Vector3Value right) =>
            new Vector3Value(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

        public static Vector3Value operator -(Vector3Value left, Vector3Value right) =>
            new Vector3Value(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

        public static Vector3Value operator *(Vector3Value value, float scale) =>
            new Vector3Value(value.X * scale, value.Y * scale, value.Z * scale);

        public static float Dot(Vector3Value left, Vector3Value right) =>
            left.X * right.X + left.Y * right.Y + left.Z * right.Z;

        public static Vector3Value Cross(Vector3Value left, Vector3Value right) =>
            new Vector3Value(
                left.Y * right.Z - left.Z * right.Y,
                left.Z * right.X - left.X * right.Z,
                left.X * right.Y - left.Y * right.X);

        public Vector3Value Normalized(float zeroLengthThreshold = FoundationTolerances.VectorNormalizationThreshold)
        {
            float length = Length;
            if (length <= zeroLengthThreshold)
                throw new InvalidOperationException("Cannot normalize a zero-length vector.");

            return this * (1f / length);
        }

        public bool Equals(Vector3Value other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

        public override bool Equals(object obj) => obj is Vector3Value other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(X, Y, Z);

        public override string ToString() => $"({X}, {Y}, {Z})";
    }

    public readonly struct QuaternionValue : IEquatable<QuaternionValue>
    {
        public QuaternionValue(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public float W { get; }

        public static QuaternionValue Identity => new QuaternionValue(0f, 0f, 0f, 1f);

        public QuaternionValue Normalized(float zeroLengthThreshold = FoundationTolerances.QuaternionNormalizationThreshold)
        {
            ValidateFiniteComponents();
            double length = Math.Sqrt((double)X * X + (double)Y * Y + (double)Z * Z + (double)W * W);
            if (length <= zeroLengthThreshold)
                throw new InvalidOperationException("Cannot normalize a zero-length quaternion.");

            float inverseLength = (float)(1d / length);
            return new QuaternionValue(X * inverseLength, Y * inverseLength, Z * inverseLength, W * inverseLength);
        }

        public QuaternionValue Canonicalized(
            float zeroLengthThreshold = FoundationTolerances.QuaternionNormalizationThreshold,
            float signTieThreshold = FoundationTolerances.QuaternionCanonicalizationTie)
        {
            if (float.IsNaN(signTieThreshold) || float.IsInfinity(signTieThreshold) ||
                signTieThreshold < 0f || signTieThreshold >= 0.5f)
                throw new ArgumentOutOfRangeException(nameof(signTieThreshold));

            QuaternionValue normalized = Normalized(zeroLengthThreshold);
            if (ShouldNegate(normalized, signTieThreshold))
                return new QuaternionValue(-normalized.X, -normalized.Y, -normalized.Z, -normalized.W);

            return normalized;
        }

        public QuaternionValue Inverse(float zeroLengthThreshold = FoundationTolerances.QuaternionNormalizationThreshold)
        {
            ValidateFiniteComponents();
            double normSquared = (double)X * X + (double)Y * Y + (double)Z * Z + (double)W * W;
            if (normSquared <= (double)zeroLengthThreshold * zeroLengthThreshold)
                throw new InvalidOperationException("Cannot invert a zero-length quaternion.");

            float inverseNormSquared = (float)(1d / normSquared);
            return new QuaternionValue(-X * inverseNormSquared, -Y * inverseNormSquared, -Z * inverseNormSquared, W * inverseNormSquared);
        }

        public static QuaternionValue FromAxisAngleRadians(Vector3Value axis, float angleRadians)
        {
            Vector3Value unitAxis = axis.Normalized();
            float halfAngle = angleRadians * 0.5f;
            float sine = (float)Math.Sin(halfAngle);
            float cosine = (float)Math.Cos(halfAngle);
            return new QuaternionValue(unitAxis.X * sine, unitAxis.Y * sine, unitAxis.Z * sine, cosine).Normalized();
        }

        public Vector3Value TransformDirection(Vector3Value direction)
        {
            QuaternionValue normalized = Normalized();
            Vector3Value quaternionVector = new Vector3Value(normalized.X, normalized.Y, normalized.Z);
            Vector3Value twiceCross = Vector3Value.Cross(quaternionVector, direction) * 2f;
            return direction + twiceCross * normalized.W + Vector3Value.Cross(quaternionVector, twiceCross);
        }

        public float ShortestArcRadiansTo(QuaternionValue other)
        {
            QuaternionValue left = Canonicalized();
            QuaternionValue right = other.Canonicalized();
            if (left.Equals(right))
                return 0f;

            QuaternionValue relative = (left.Inverse() * right).Normalized();
            double vectorLength = Math.Sqrt((double)relative.X * relative.X +
                (double)relative.Y * relative.Y +
                (double)relative.Z * relative.Z);
            return 2f * (float)Math.Atan2(vectorLength, Math.Abs(relative.W));
        }

        public static QuaternionValue operator *(QuaternionValue left, QuaternionValue right) =>
            new QuaternionValue(
                left.W * right.X + left.X * right.W + left.Y * right.Z - left.Z * right.Y,
                left.W * right.Y - left.X * right.Z + left.Y * right.W + left.Z * right.X,
                left.W * right.Z + left.X * right.Y - left.Y * right.X + left.Z * right.W,
                left.W * right.W - left.X * right.X - left.Y * right.Y - left.Z * right.Z);

        public bool Equals(QuaternionValue other) =>
            X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z) && W.Equals(other.W);

        public override bool Equals(object obj) => obj is QuaternionValue other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(X, Y, Z, W);

        private static bool ShouldNegate(QuaternionValue normalized, float signTieThreshold)
        {
            if (normalized.W < -signTieThreshold)
                return true;
            if (normalized.W > signTieThreshold)
                return false;
            if (normalized.X < -signTieThreshold)
                return true;
            if (normalized.X > signTieThreshold)
                return false;
            if (normalized.Y < -signTieThreshold)
                return true;
            if (normalized.Y > signTieThreshold)
                return false;
            return normalized.Z < -signTieThreshold;
        }

        private void ValidateFiniteComponents()
        {
            if (float.IsNaN(X) || float.IsInfinity(X) ||
                float.IsNaN(Y) || float.IsInfinity(Y) ||
                float.IsNaN(Z) || float.IsInfinity(Z) ||
                float.IsNaN(W) || float.IsInfinity(W))
                throw new ArgumentOutOfRangeException(nameof(QuaternionValue), "Quaternion components must be finite.");
        }
    }

    public static class CoordinateContract
    {
        public const string WorldFrameId = "W";
        public const string BodyFramePattern = "B_i";
        public const string JointFramePattern = "J_i";
        public const string ReferenceRigFramePattern = "R_i";
        public const string VisibleMeshFrameId = "M";
        public const string BarFrameId = "BAR";

        public static Vector3Value UpAxis => new Vector3Value(0f, 1f, 0f);
        public static Vector3Value ForwardAxis => new Vector3Value(0f, 0f, 1f);
        public static Vector3Value RightAxis => new Vector3Value(1f, 0f, 0f);

        public static Vector3Value LocalToWorldDirection(QuaternionValue worldFromLocalRotation, Vector3Value localDirection) =>
            worldFromLocalRotation.TransformDirection(localDirection);

        public static Vector3Value WorldToLocalDirection(QuaternionValue worldFromLocalRotation, Vector3Value worldDirection) =>
            worldFromLocalRotation.Inverse().TransformDirection(worldDirection);

        public static Vector3Value LocalToWorldPoint(
            Vector3Value worldOriginMeters,
            QuaternionValue worldFromLocalRotation,
            Vector3Value localPointMeters) =>
            worldOriginMeters + LocalToWorldDirection(worldFromLocalRotation, localPointMeters);

        public static Vector3Value WorldToLocalPoint(
            Vector3Value worldOriginMeters,
            QuaternionValue worldFromLocalRotation,
            Vector3Value worldPointMeters) =>
            WorldToLocalDirection(worldFromLocalRotation, worldPointMeters - worldOriginMeters);
    }

    public static class UnitContract
    {
        public const string InternalSystemId = "SI";
        public const float RadiansPerDegree = (float)(Math.PI / 180d);
        public const float DegreesPerRadian = (float)(180d / Math.PI);

        public static float DegreesToRadians(float angleDegrees) => angleDegrees * RadiansPerDegree;

        public static float RadiansToDegrees(float angleRadians) => angleRadians * DegreesPerRadian;
    }

    public static class SimulationConstants
    {
        public const int FixedStepHz = 100;
        public const double FixedDeltaTimeSeconds = 0.01d;
        public const int MaxCatchUpTicksPerRenderFrame = 4;
        public const double MaxAccumulatedTimeSeconds = FixedDeltaTimeSeconds * MaxCatchUpTicksPerRenderFrame;

        public static double TimeForTick(ulong tick) => tick * FixedDeltaTimeSeconds;
    }

    public static class FoundationTolerances
    {
        public const float VectorNormalizationThreshold = 1e-6f;
        public const float QuaternionNormalizationThreshold = 1e-6f;
        public const float BasisOrthogonality = 1e-6f;
        public const float UnitConversionRoundTrip = 1e-5f;
        public const float QuaternionCanonicalizationTie = 1e-6f;
        public const double SimulationTimeMapping = 1e-12d;
        public const double RenderAccumulatorComparison = 1e-12d;
        public const float PhysicsFixturePositionMeters = 1e-5f;
    }
}
