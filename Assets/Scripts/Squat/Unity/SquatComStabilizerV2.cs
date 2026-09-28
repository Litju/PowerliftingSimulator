using System;
using UnityEngine;

namespace PowerliftingSimulator.Squat.Unity
{
    public readonly struct SquatComStabilizerV2Calibration
    {
        public const int ActiveScalarCount = 14;
        public static readonly SquatComStabilizerV2Calibration Default = new SquatComStabilizerV2Calibration(
            "GAM13_SQUAT_COM_STABILIZER_V2_1",
            0.80f / 0.20446f, (0.80f / 0.20446f) * 0.1f, 0.65f, 0.05f,
            1.00f, 0.20f, 0.08f, 1.00f, 0.15f,
            0.26180f, 0.05f, 4.0f, 0.40f, 0.02f);

        public SquatComStabilizerV2Calibration(
            string version,
            float kpAp,
            float kdAp,
            float kpMl,
            float kdMl,
            float ankleApWeight,
            float hipApWeight,
            float trunkApCounterWeight,
            float ankleMlWeight,
            float hipMlWeight,
            float maxApCorrectionRad,
            float maxMlCorrectionRad,
            float maxApCorrectionRateRadS,
            float maxMlCorrectionRateRadS,
            float maxPlayerMlBiasRad)
        {
            Version = string.IsNullOrWhiteSpace(version) ? throw new ArgumentException("A calibration version is required.", nameof(version)) : version;
            KpAp = Nonnegative(kpAp, nameof(kpAp));
            KdAp = Nonnegative(kdAp, nameof(kdAp));
            KpMl = Nonnegative(kpMl, nameof(kpMl));
            KdMl = Nonnegative(kdMl, nameof(kdMl));
            AnkleApWeight = Nonnegative(ankleApWeight, nameof(ankleApWeight));
            HipApWeight = Nonnegative(hipApWeight, nameof(hipApWeight));
            TrunkApCounterWeight = Nonnegative(trunkApCounterWeight, nameof(trunkApCounterWeight));
            AnkleMlWeight = Nonnegative(ankleMlWeight, nameof(ankleMlWeight));
            HipMlWeight = Nonnegative(hipMlWeight, nameof(hipMlWeight));
            MaxApCorrectionRad = Positive(maxApCorrectionRad, nameof(maxApCorrectionRad));
            MaxMlCorrectionRad = Positive(maxMlCorrectionRad, nameof(maxMlCorrectionRad));
            MaxApCorrectionRateRadS = Positive(maxApCorrectionRateRadS, nameof(maxApCorrectionRateRadS));
            MaxMlCorrectionRateRadS = Positive(maxMlCorrectionRateRadS, nameof(maxMlCorrectionRateRadS));
            MaxPlayerMlBiasRad = Nonnegative(maxPlayerMlBiasRad, nameof(maxPlayerMlBiasRad));
        }

        public string Version { get; }
        public float KpAp { get; }
        public float KdAp { get; }
        public float KpMl { get; }
        public float KdMl { get; }
        public float AnkleApWeight { get; }
        public float HipApWeight { get; }
        public float TrunkApCounterWeight { get; }
        public float AnkleMlWeight { get; }
        public float HipMlWeight { get; }
        public float MaxApCorrectionRad { get; }
        public float MaxMlCorrectionRad { get; }
        public float MaxApCorrectionRateRadS { get; }
        public float MaxMlCorrectionRateRadS { get; }
        public float MaxPlayerMlBiasRad { get; }

        private static float Nonnegative(float value, string name) =>
            float.IsFinite(value) && value >= 0f ? value : throw new ArgumentOutOfRangeException(name);

        private static float Positive(float value, string name) =>
            float.IsFinite(value) && value > 0f ? value : throw new ArgumentOutOfRangeException(name);
    }

    public readonly struct SquatBalanceCorrectionV2
    {
        internal SquatBalanceCorrectionV2(
            float errorApM,
            float errorMlM,
            float commandApRad,
            float commandMlRad,
            float appliedApRad,
            float appliedMlRad,
            bool isApBoundSaturated,
            bool isMlBoundSaturated,
            SquatComStabilizerV2Calibration calibration)
        {
            ErrorApM = errorApM;
            ErrorMlM = errorMlM;
            CommandApRad = commandApRad;
            CommandMlRad = commandMlRad;
            AppliedApRad = appliedApRad;
            AppliedMlRad = appliedMlRad;
            IsApBoundSaturated = isApBoundSaturated;
            IsMlBoundSaturated = isMlBoundSaturated;
            AnkleApRad = appliedApRad * calibration.AnkleApWeight;
            HipApRad = appliedApRad * calibration.HipApWeight;
            TrunkApRad = -appliedApRad * calibration.TrunkApCounterWeight;
            AnkleMlRad = appliedMlRad * calibration.AnkleMlWeight;
            HipMlRad = appliedMlRad * calibration.HipMlWeight;
        }

        public float ErrorApM { get; }
        public float ErrorMlM { get; }
        public float CommandApRad { get; }
        public float CommandMlRad { get; }
        public float AppliedApRad { get; }
        public float AppliedMlRad { get; }
        public float AnkleApRad { get; }
        public float HipApRad { get; }
        public float TrunkApRad { get; }
        public float AnkleMlRad { get; }
        public float HipMlRad { get; }
        public bool IsApBoundSaturated { get; }
        public bool IsMlBoundSaturated { get; }
        public bool IsBoundSaturated => IsApBoundSaturated || IsMlBoundSaturated;
    }

    public sealed class SquatComStabilizerV2
    {
        private readonly SquatComStabilizerV2Calibration _calibration;
        private float _apOutputRad;
        private float _mlOutputRad;

        public SquatComStabilizerV2() : this(SquatComStabilizerV2Calibration.Default) { }

        public SquatComStabilizerV2(SquatComStabilizerV2Calibration calibration)
        {
            if (calibration.Version == null)
                throw new ArgumentException("A valid V2 calibration is required.", nameof(calibration));
            _calibration = calibration;
        }

        public SquatComStabilizerV2Calibration Calibration => _calibration;

        public SquatBalanceCorrectionV2 Solve(
            Vector3 athleteBarCom,
            Vector3 comVelocity,
            Vector3 supportCenter,
            float referenceComOffsetApM,
            float referenceComOffsetMlM,
            float playerMlBias01,
            float deltaTimeSeconds)
        {
            if (!IsFinite(athleteBarCom))
                throw new ArgumentOutOfRangeException(nameof(athleteBarCom));
            if (!IsFinite(comVelocity))
                throw new ArgumentOutOfRangeException(nameof(comVelocity));
            if (!IsFinite(supportCenter))
                throw new ArgumentOutOfRangeException(nameof(supportCenter));
            if (!float.IsFinite(referenceComOffsetApM))
                throw new ArgumentOutOfRangeException(nameof(referenceComOffsetApM));
            if (!float.IsFinite(referenceComOffsetMlM))
                throw new ArgumentOutOfRangeException(nameof(referenceComOffsetMlM));
            if (!float.IsFinite(playerMlBias01))
                throw new ArgumentOutOfRangeException(nameof(playerMlBias01));
            if (!float.IsFinite(deltaTimeSeconds) || deltaTimeSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(deltaTimeSeconds));

            float errorAp = (athleteBarCom.z - supportCenter.z) - referenceComOffsetApM;
            float errorMl = (athleteBarCom.x - supportCenter.x) - referenceComOffsetMlM;
            // Clean unloaded V2-3C measured positive ankle target -> forward
            // COP (G_ap = +0.985 m/rad), so forward COM error needs positive AP output.
            float commandAp = _calibration.KpAp * errorAp + _calibration.KdAp * comVelocity.z;
            float commandMl = -(_calibration.KpMl * errorMl + _calibration.KdMl * comVelocity.x);
            float biasedMl = commandMl + Mathf.Clamp(playerMlBias01, -1f, 1f) * _calibration.MaxPlayerMlBiasRad;
            float targetAp = Mathf.Clamp(commandAp, -_calibration.MaxApCorrectionRad, _calibration.MaxApCorrectionRad);
            float targetMl = Mathf.Clamp(biasedMl, -_calibration.MaxMlCorrectionRad, _calibration.MaxMlCorrectionRad);
            _apOutputRad = Mathf.MoveTowards(
                _apOutputRad, targetAp, _calibration.MaxApCorrectionRateRadS * deltaTimeSeconds);
            _mlOutputRad = Mathf.MoveTowards(
                _mlOutputRad, targetMl, _calibration.MaxMlCorrectionRateRadS * deltaTimeSeconds);

            return new SquatBalanceCorrectionV2(
                errorAp,
                errorMl,
                commandAp,
                commandMl,
                _apOutputRad,
                _mlOutputRad,
                Mathf.Abs(commandAp) > _calibration.MaxApCorrectionRad,
                Mathf.Abs(biasedMl) > _calibration.MaxMlCorrectionRad,
                _calibration);
        }

        public void Reset()
        {
            _apOutputRad = 0f;
            _mlOutputRad = 0f;
        }

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    }
}
