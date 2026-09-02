using System;

namespace PowerliftingSimulator.Squat
{
    public enum SquatLandmarkId : byte
    {
        LeftHipCreaseProxy,
        RightHipCreaseProxy,
        LeftKneeTopProxy,
        RightKneeTopProxy
    }

    public readonly struct SquatPoint3
    {
        public SquatPoint3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float X { get; }
        public float Y { get; }
        public float Z { get; }
    }

    public readonly struct SquatDepthObservation
    {
        public SquatDepthObservation(float leftDepthM, float rightDepthM, float marginM)
        {
            LeftDepthM = leftDepthM;
            RightDepthM = rightDepthM;
            MarginM = marginM;
            WorstSideDepthM = Math.Max(leftDepthM, rightDepthM);
            BilateralLegalReference = WorstSideDepthM <= -marginM;
        }

        public float LeftDepthM { get; }
        public float RightDepthM { get; }
        public float WorstSideDepthM { get; }
        public float MarginM { get; }
        public bool BilateralLegalReference { get; }
    }

    public static class SquatDepthGeometry
    {
        public const float DefaultDepthMarginM = 0.005f;
        public const string RuleSource = "IPF Technical Rule Book, effective 01 March 2026, version 3";
        public const string SourceClass = "RULE_DERIVED_GAME_PROXY";

        public static SquatDepthObservation Evaluate(
            SquatPoint3 leftHipCrease,
            SquatPoint3 rightHipCrease,
            SquatPoint3 leftKneeTop,
            SquatPoint3 rightKneeTop,
            float depthMarginM = DefaultDepthMarginM)
        {
            return Evaluate(
                leftHipCrease.Y,
                rightHipCrease.Y,
                leftKneeTop.Y,
                rightKneeTop.Y,
                depthMarginM);
        }

        public static SquatDepthObservation Evaluate(
            float leftHipCreaseY,
            float rightHipCreaseY,
            float leftKneeTopY,
            float rightKneeTopY,
            float depthMarginM = DefaultDepthMarginM)
        {
            ValidateFinite(leftHipCreaseY, nameof(leftHipCreaseY));
            ValidateFinite(rightHipCreaseY, nameof(rightHipCreaseY));
            ValidateFinite(leftKneeTopY, nameof(leftKneeTopY));
            ValidateFinite(rightKneeTopY, nameof(rightKneeTopY));
            ValidateFinite(depthMarginM, nameof(depthMarginM));
            if (depthMarginM < 0f)
                throw new ArgumentOutOfRangeException(nameof(depthMarginM));

            return new SquatDepthObservation(
                leftHipCreaseY - leftKneeTopY,
                rightHipCreaseY - rightKneeTopY,
                depthMarginM);
        }

        private static void ValidateFinite(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(name);
        }
    }
}
