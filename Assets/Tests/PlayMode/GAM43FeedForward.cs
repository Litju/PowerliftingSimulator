using System;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;

namespace PowerliftingSimulator.Tests
{
    /// <summary>Frozen GAM-43 F0/F1/F2 candidate laws.</summary>
    internal static class GAM43FeedForward
    {
        private static readonly float[] PhaseKnots = { 0f, 0.25f, 0.55f, 0.80f, 1f };
        private static readonly float[] LoadKnots = { 0f, 25f, 60f, 140f, 170f, 300f };

        private static readonly float[][] Abdomen =
        {
            new[] { 0f, -2.56f, -6.11f, -6.73f, -7.74f },
            new[] { 0f, -1.51f, -7.19f, -8.17f, -8.17f },
            new[] { 7.40f, -0.20f, -8.50f, -9.60f, -9.40f },
            new[] { 11.20f, 1.20f, -9.40f, -10.60f, -10.20f },
            new[] { 11.80f, 1.80f, -9.80f, -11.00f, -10.60f },
            new[] { 12.00f, 2.20f, -10.40f, -11.80f, -11.40f }
        };

        private static readonly float[][] Thorax =
        {
            new[] { 0f, -1.04f, -3.03f, -3.38f, -3.59f },
            new[] { 0f, 0.36f, -3.06f, -3.66f, -3.66f },
            new[] { 6.60f, 1.20f, -3.60f, -4.80f, -4.60f },
            new[] { 10.30f, 2.30f, -4.40f, -6.00f, -5.80f },
            new[] { 11.00f, 2.80f, -4.80f, -6.50f, -6.20f },
            new[] { 12.00f, 3.50f, -5.40f, -7.30f, -7.00f }
        };

        private static readonly float[] F1AbdomenStanding =
            { 0.83f, 3.4557111793f, 4.8284150722f, 4.6966275932f, 5.8672913100f, 7.7701211322f };
        private static readonly float[] F1ThoraxStanding =
            { 0.92f, 3.3476651683f, 2.3169222261f, 5.8474472453f, 6.1630915138f, 7.1453508792f };
        private static readonly float[] F2AnkleStanding =
            { 0f, -1.8063042634f, -2.1074897118f, 0.4350906376f, 1.4862751944f, 3.4342191243f };
        private static readonly float[] F2KneeStanding =
            { 0f, 1.0260491747f, 1.6714558729f, -0.2708576668f, -1.3010310077f, -3.3363286815f };
        private static readonly float[] F2HipStanding =
            { 0f, -0.5826498604f, 1.4604533976f, 2.6608095306f, 3.3683454750f, 4.7420170119f };

        public static SquatEquilibriumPreload.ExperimentalBiasEvaluator ForVariant(string variant)
        {
            switch (variant.ToUpperInvariant())
            {
                case "F0": return null;
                case "F1": return EvaluateF1;
                case "F2": return EvaluateF2;
                default: throw new ArgumentException("GAM43_FEED_FORWARD must be F0, F1, or F2.", nameof(variant));
            }
        }

        private static float EvaluateF1(SquatJointFamily family, float phase, float loadKg)
        {
            if (family != SquatJointFamily.Abdomen && family != SquatJointFamily.Thorax)
                return 0f;
            if (phase >= 0.25f)
                return CurrentSpine(family, phase, loadKg);

            float standing = InterpolateLoad(
                family == SquatJointFamily.Abdomen ? F1AbdomenStanding : F1ThoraxStanding,
                loadKg);
            float atQuarter = CurrentSpine(family, 0.25f, loadKg);
            return Mathf.Lerp(standing, atQuarter, SmoothStep(Mathf.Clamp01(phase / 0.25f)));
        }

        private static float EvaluateF2(SquatJointFamily family, float phase, float loadKg)
        {
            if (family == SquatJointFamily.Ankle)
                return FadeStanding(F2AnkleStanding, phase, loadKg);
            if (family == SquatJointFamily.Knee)
                return FadeStanding(F2KneeStanding, phase, loadKg);
            if (family == SquatJointFamily.Hip)
                return FadeStanding(F2HipStanding, phase, loadKg);
            return EvaluateF1(family, phase, loadKg);
        }

        private static float FadeStanding(float[] values, float phase, float loadKg) =>
            InterpolateLoad(values, loadKg) * (1f - SmoothStep(Mathf.Clamp01(phase / 0.25f)));

        private static float CurrentSpine(SquatJointFamily family, float phase, float loadKg)
        {
            float[][] values = family == SquatJointFamily.Abdomen ? Abdomen : Thorax;
            int loadIndex = SegmentIndex(loadKg, LoadKnots);
            float loadT = SmoothStep(Mathf.InverseLerp(
                LoadKnots[loadIndex], LoadKnots[loadIndex + 1], Mathf.Clamp(loadKg, LoadKnots[0], LoadKnots[LoadKnots.Length - 1])));
            float lower = EvaluatePhase(values[loadIndex], loadIndex, family, phase);
            float upper = EvaluatePhase(values[loadIndex + 1], loadIndex + 1, family, phase);
            return Mathf.Lerp(lower, upper, loadT);
        }

        private static float EvaluatePhase(float[] values, int loadIndex, SquatJointFamily family, float phase)
        {
            float clampedPhase = Mathf.Clamp01(phase);
            int phaseIndex = 0;
            while (phaseIndex < PhaseKnots.Length - 2 && clampedPhase > PhaseKnots[phaseIndex + 1])
                phaseIndex++;

            float t = SmoothStep(Mathf.InverseLerp(PhaseKnots[phaseIndex], PhaseKnots[phaseIndex + 1], clampedPhase));
            float lower = phaseIndex == 0 ? CurrentStandingValue(loadIndex, family) : values[phaseIndex];
            return Mathf.Lerp(lower, values[phaseIndex + 1], t);
        }

        private static float CurrentStandingValue(int loadIndex, SquatJointFamily family)
        {
            if (family == SquatJointFamily.Abdomen)
                return loadIndex == 0 ? 0.83f : loadIndex == 1 ? 3.93f : Abdomen[loadIndex][0];
            return loadIndex == 0 ? 0.92f : loadIndex == 1 ? 3.67f : Thorax[loadIndex][0];
        }

        private static float InterpolateLoad(float[] values, float loadKg)
        {
            int loadIndex = SegmentIndex(loadKg, LoadKnots);
            float clamped = Mathf.Clamp(loadKg, LoadKnots[0], LoadKnots[LoadKnots.Length - 1]);
            float t = SmoothStep(Mathf.InverseLerp(LoadKnots[loadIndex], LoadKnots[loadIndex + 1], clamped));
            return Mathf.Lerp(values[loadIndex], values[loadIndex + 1], t);
        }

        private static int SegmentIndex(float value, float[] knots)
        {
            float clamped = Mathf.Clamp(value, knots[0], knots[knots.Length - 1]);
            int index = 0;
            while (index < knots.Length - 2 && clamped > knots[index + 1])
                index++;
            return index;
        }

        private static float SmoothStep(float value)
        {
            float t = Mathf.Clamp01(value);
            return t * t * (3f - 2f * t);
        }
    }
}
