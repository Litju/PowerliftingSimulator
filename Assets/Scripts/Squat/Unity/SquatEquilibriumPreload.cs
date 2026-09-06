using System;
using UnityEngine;

namespace PowerliftingSimulator.Squat.Unity
{
    public enum SquatJointFamily : byte
    {
        Ankle,
        Knee,
        Hip,
        Abdomen,
        Thorax
    }

    /// <summary>
    /// Persistent target-space preload that lets the finite GAM-7 joint
    /// springs develop an antigravity moment while the actual physical pose
    /// stays near the canonical GAM-10 standing pose.
    ///
    /// A ConfigurableJoint drive only produces torque from target error, so a
    /// body commanded exactly at its reference pose sags until the error it
    /// develops is worth the gravitational moment. Preloading the target by
    /// that same error means the drive is already carrying the load when the
    /// actual pose is still at the reference.
    ///
    /// This is offline game calibration, not runtime control, and it is not a
    /// movement trajectory. It never touches the GAM-10 reference; it is a
    /// separate additive term in
    /// FINAL = NOMINAL_GAM10 + GRAVITY_EQUILIBRIUM_BIAS + DYNAMIC_BALANCE.
    /// </summary>
    public sealed class SquatEquilibriumPreload
    {
        public const string ClaimClass = "OFFLINE_GAME_CALIBRATION";

        /// <summary>
        /// Beyond this the preload has stopped being a preload. The old
        /// 30-degree ankle prop is exactly what this bound exists to prevent:
        /// if equilibrium needs more than this, the substrate is wrong and the
        /// answer is not a bigger bias.
        /// </summary>
        public const float HardBoundRad = 0.20944f; // 12 deg

        /// <summary>Soft target for a plausible standing preload.</summary>
        public const float SoftBoundRad = 0.10472f; // 6 deg

        private readonly float[] _anatomicalFlexionBiasRad = new float[5];

        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Anatomical flexion bias for a family, in radians, positive in the
        /// canonical sense: ankle dorsiflexion, knee flexion, hip flexion,
        /// trunk flexion. The qualified per-joint sign mapping converts this
        /// into logical joint space, so no raw quaternion offsets are authored
        /// here.
        /// </summary>
        public float AnatomicalFlexionBiasRad(SquatJointFamily family) =>
            Enabled ? _anatomicalFlexionBiasRad[(int)family] : 0f;

        public float AnatomicalFlexionBiasDegrees(SquatJointFamily family) =>
            AnatomicalFlexionBiasRad(family) * Mathf.Rad2Deg;

        public void SetAnatomicalFlexionBiasRad(SquatJointFamily family, float biasRad)
        {
            if (!float.IsFinite(biasRad))
                throw new ArgumentOutOfRangeException(nameof(biasRad));
            if (Mathf.Abs(biasRad) > HardBoundRad + 1e-4f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(biasRad),
                    $"A standing preload of {biasRad * Mathf.Rad2Deg:F1} deg on {family} exceeds the {HardBoundRad * Mathf.Rad2Deg:F0} deg investigation boundary. Classify PRELOAD_REQUIREMENT_IMPLAUSIBLY_LARGE and reopen substrate calibration instead of widening this.");
            }
            _anatomicalFlexionBiasRad[(int)family] = biasRad;
        }

        public void SetAnatomicalFlexionBiasDegrees(SquatJointFamily family, float biasDegrees) =>
            SetAnatomicalFlexionBiasRad(family, biasDegrees * Mathf.Deg2Rad);

        public void Clear()
        {
            for (int index = 0; index < _anatomicalFlexionBiasRad.Length; index++)
                _anatomicalFlexionBiasRad[index] = 0f;
        }

        public float MaxAbsoluteBiasRad()
        {
            float worst = 0f;
            for (int index = 0; index < _anatomicalFlexionBiasRad.Length; index++)
                worst = Mathf.Max(worst, Mathf.Abs(_anatomicalFlexionBiasRad[index]));
            return worst;
        }

        public bool IsWithinHardBound() => MaxAbsoluteBiasRad() <= HardBoundRad + 1e-4f;

        public void CopyFrom(SquatEquilibriumPreload other)
        {
            if (other == null)
                throw new ArgumentNullException(nameof(other));
            for (int index = 0; index < _anatomicalFlexionBiasRad.Length; index++)
                _anatomicalFlexionBiasRad[index] = other._anatomicalFlexionBiasRad[index];
            Enabled = other.Enabled;
        }

        /// <summary>
        /// The qualified standing preload. Every value here comes from the
        /// deterministic identification sweep recorded in
        /// Artifacts/Measurements/GAM11-preload-identification.csv, not from
        /// hand tuning.
        /// </summary>
        public static SquatEquilibriumPreload QualifiedStanding()
        {
            var preload = new SquatEquilibriumPreload();
            preload.SetAnatomicalFlexionBiasDegrees(SquatJointFamily.Ankle, 0f);
            preload.SetAnatomicalFlexionBiasDegrees(SquatJointFamily.Knee, 0f);
            preload.SetAnatomicalFlexionBiasDegrees(SquatJointFamily.Hip, 0f);
            preload.SetAnatomicalFlexionBiasDegrees(SquatJointFamily.Abdomen, 0f);
            preload.SetAnatomicalFlexionBiasDegrees(SquatJointFamily.Thorax, 0f);
            return preload;
        }
    }
}
