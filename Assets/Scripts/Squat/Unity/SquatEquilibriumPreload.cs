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
        /// The qualified standing preload, currently zero on every family.
        ///
        /// On the grounded plant the open-loop imbalance is nulled by about
        /// -11.5 deg of ankle plantarflexion bias: the COM-to-COP offset
        /// crosses zero between -10 and -12 deg and COM velocity at 0.6 s falls
        /// from 0.325 to 0.011 m/s
        /// (Artifacts/Measurements/GAM11-ankle-preload-cop-sensitivity.csv).
        /// That value is measured but deliberately not adopted, for two
        /// reasons. It sits on the 12 deg investigation boundary rather than
        /// near the 6 deg soft target, and it was identified with feedback
        /// disabled, so with the predictive balance controller running the two
        /// corrections stack and the athlete departs backwards instead.
        ///
        /// A preload is a feed-forward trim for a steady gravitational moment.
        /// It cannot stabilise an inverted pendulum on its own, so the value
        /// that belongs here has to be identified with the loop closed.
        /// </summary>
        /// <summary>
        /// GAME_PHYSICS_SPINE_EQUILIBRIUM_CALIBRATION.
        ///
        /// Anatomical flexion bias, in degrees, that lets the finite spine
        /// drives hold the accepted GAM-10 abdomen and thorax angles while
        /// carrying the gravitational moment of everything above them. It is
        /// not a spinal torque model, a muscle model or an inverse-dynamics
        /// result: it is the target offset this rig needs on this plant.
        ///
        /// Identified in phase 5H15 by holding each phase to rest and solving
        /// J u = -e0 on the measured 2x2 closed-loop response, fresh reset per
        /// perturbation. Provenance:
        /// Artifacts/Measurements/GAM-11/H15/spine-coupled-response.csv.
        ///
        /// Two entries deliberately depart from the raw solve.
        ///
        /// Standing is held at zero at both loads. The measurement asks for
        /// +0.83 deg unloaded and +3.93 deg at 25 kg, because with the bar the
        /// spine leans back rather than forward, but standing is already a
        /// qualified state and moving its setpoint is a plant change that
        /// needs its own authorization rather than a side effect of repairing
        /// the deep squat.
        ///
        /// The loaded bottom holds the s_q 0.80 value. Its own solve returned
        /// -19.15 deg against a 12 deg hard bound, on the one sample whose
        /// response matrix is ill-conditioned: the abdomen's response to a
        /// thorax bias reverses sign there and the determinant halves. An
        /// unqualified extrapolation is worse than holding the deepest
        /// qualified neighbour.
        /// </summary>
        private static readonly float[] SpinePhaseKnots = { 0.00f, 0.25f, 0.55f, 0.80f, 1.00f };

        private static readonly float[] AbdomenBias0Kg = { 0.00f, -2.56f, -6.11f, -6.73f, -7.74f };
        private static readonly float[] ThoraxBias0Kg = { 0.00f, -1.04f, -3.03f, -3.38f, -3.59f };
        private static readonly float[] AbdomenBias25Kg = { 0.00f, -1.51f, -7.19f, -8.17f, -8.17f };
        private static readonly float[] ThoraxBias25Kg = { 0.00f, 0.36f, -3.06f, -3.66f, -3.66f };

        /// <summary>The load the second column was identified at.</summary>
        public const float CalibratedLoadKg = 25f;

        /// <summary>
        /// Off restores the pre-5H15 behaviour, where the spine carried no
        /// equilibrium bias at all. Identification runs with it off so the
        /// plant it measures is the uncompensated one.
        /// </summary>
        public bool SpineCalibrationEnabled { get; set; } = true;

        /// <summary>
        /// Qualified spine bias for a phase and bar load, in degrees of
        /// anatomical flexion. Deterministic piecewise-linear interpolation
        /// over the measured knots, with load interpolated between the two
        /// identified columns and clamped beyond the calibrated load: 105 kg
        /// is out of scope and must not be reached by extrapolation.
        ///
        /// Allocation-free and branch-light; the whole evaluation is two table
        /// walks and a lerp.
        /// </summary>
        public float SpineBiasDegrees(SquatJointFamily family, float phase, float loadKg)
        {
            if (!SpineCalibrationEnabled)
                return 0f;
            if (family != SquatJointFamily.Abdomen && family != SquatJointFamily.Thorax)
                return 0f;

            bool abdomen = family == SquatJointFamily.Abdomen;
            float unloaded = Interpolate(abdomen ? AbdomenBias0Kg : ThoraxBias0Kg, phase);
            float loaded = Interpolate(abdomen ? AbdomenBias25Kg : ThoraxBias25Kg, phase);
            float blend = Mathf.Clamp01(loadKg / CalibratedLoadKg);
            return Mathf.Lerp(unloaded, loaded, blend);
        }

        private static float Interpolate(float[] values, float phase)
        {
            float clamped = Mathf.Clamp01(phase);
            for (int index = 1; index < SpinePhaseKnots.Length; index++)
            {
                if (clamped > SpinePhaseKnots[index])
                    continue;
                float t = Mathf.InverseLerp(SpinePhaseKnots[index - 1], SpinePhaseKnots[index], clamped);
                return Mathf.Lerp(values[index - 1], values[index], t);
            }
            return values[values.Length - 1];
        }

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
