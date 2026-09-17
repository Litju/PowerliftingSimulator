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
        public const string CalibrationVersion = "GAM13_SQUAT_EQUILIBRIUM_FEEDFORWARD_V1";

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
        /// The standing knot lives in the four StandingBiasDegrees properties
        /// rather than in these arrays, because 5H15 shipped it at zero and
        /// 5H16 qualified it separately. Index 0 of each array is unused.
        ///
        /// One entry still departs from the raw solve.
        ///
        /// The loaded bottom holds the s_q 0.80 value. Its own solve returned
        /// -19.15 deg against a 12 deg hard bound, on the one sample whose
        /// response matrix is ill-conditioned: the abdomen's response to a
        /// thorax bias reverses sign there and the determinant halves. An
        /// unqualified extrapolation is worse than holding the deepest
        /// qualified neighbour.
        /// </summary>
        private static readonly float[] SpinePhaseKnots = { 0.00f, 0.25f, 0.55f, 0.80f, 1.00f };
        private static readonly float[] LoadKnotsKg = { 0f, 25f, 60f, 140f, 170f, 300f };

        private static readonly float[] AbdomenBias0Kg = { 0.00f, -2.56f, -6.11f, -6.73f, -7.74f };
        private static readonly float[] ThoraxBias0Kg = { 0.00f, -1.04f, -3.03f, -3.38f, -3.59f };
        private static readonly float[] AbdomenBias25Kg = { 0.00f, -1.51f, -7.19f, -8.17f, -8.17f };
        private static readonly float[] ThoraxBias25Kg = { 0.00f, 0.36f, -3.06f, -3.66f, -3.66f };

        // GAM13_STAGE_A engineering-derived seeds, refined on the production
        // closed loop. These are explicit load knots, not a load-specific
        // control path: the runtime evaluates one smooth phase x load surface.
        private static readonly float[] AbdomenBias60Kg = { 7.40f, -0.20f, -8.50f, -9.60f, -9.40f };
        private static readonly float[] AbdomenBias140Kg = { 11.20f, 1.20f, -9.40f, -10.60f, -10.20f };
        private static readonly float[] AbdomenBias170Kg = { 11.80f, 1.80f, -9.80f, -11.00f, -10.60f };
        private static readonly float[] AbdomenBias300Kg = { 12.00f, 2.20f, -10.40f, -11.80f, -11.40f };
        private static readonly float[] ThoraxBias60Kg = { 6.60f, 1.20f, -3.60f, -4.80f, -4.60f };
        private static readonly float[] ThoraxBias140Kg = { 10.30f, 2.30f, -4.40f, -6.00f, -5.80f };
        private static readonly float[] ThoraxBias170Kg = { 11.00f, 2.80f, -4.80f, -6.50f, -6.20f };
        private static readonly float[] ThoraxBias300Kg = { 12.00f, 3.50f, -5.40f, -7.30f, -7.00f };

        private static readonly float[][] AbdomenBiasByLoad =
        {
            AbdomenBias0Kg, AbdomenBias25Kg, AbdomenBias60Kg,
            AbdomenBias140Kg, AbdomenBias170Kg, AbdomenBias300Kg
        };

        private static readonly float[][] ThoraxBiasByLoad =
        {
            ThoraxBias0Kg, ThoraxBias25Kg, ThoraxBias60Kg,
            ThoraxBias140Kg, ThoraxBias170Kg, ThoraxBias300Kg
        };

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
        /// anatomical flexion. The representation is one bounded, smooth
        /// phase x load feed-forward surface over the Stage-A knots. Values at
        /// the 0/25 kg knots preserve the GAM-11 qualification; heavy knots
        /// are GAM-13 engineering-derived game calibration refined by closed-
        /// loop physical holds.
        /// </summary>
        /// <summary>
        /// Standing knot, held separately from the rest of the table because
        /// it needed its own qualification. 5H15 solved these from the same
        /// closed-loop response as the deep knots but withheld them, since
        /// standing was already a qualified state.
        ///
        /// 5H16 qualified them: three independent ten-second holds per case,
        /// production balance on, nothing else varied. At 25 kg the abdomen
        /// reference miss goes from -6.92 deg to +0.25 and world trunk from
        /// -7.75 to -0.47, with the support interval, the centre of pressure,
        /// the contact count and the hip all unmoved and the drift still under
        /// 0.03 deg over the measurement window
        /// (Artifacts/Measurements/GAM-11/H16/standing-bias-authorization.csv).
        ///
        /// Setting these to zero reproduces the 5H15 shipped behaviour.
        /// </summary>
        public float StandingAbdomenBiasDegrees0Kg { get; set; }
        public float StandingThoraxBiasDegrees0Kg { get; set; }
        public float StandingAbdomenBiasDegrees25Kg { get; set; }
        public float StandingThoraxBiasDegrees25Kg { get; set; }

        public float SpineBiasDegrees(SquatJointFamily family, float phase, float loadKg)
        {
            if (!SpineCalibrationEnabled)
                return 0f;
            if (family != SquatJointFamily.Abdomen && family != SquatJointFamily.Thorax)
                return 0f;
            if (!float.IsFinite(loadKg))
                return 0f;

            bool abdomen = family == SquatJointFamily.Abdomen;
            return EvaluateSurface(
                abdomen ? AbdomenBiasByLoad : ThoraxBiasByLoad,
                abdomen,
                phase,
                loadKg);
        }

        /// <summary>
        /// Derivative of the same table with respect to phase, in degrees per
        /// unit phase. The table is piecewise linear, so this is the slope of
        /// the active segment and is exact rather than differenced. It is
        /// piecewise constant and steps at the knots, which is a property of
        /// the calibrated path and is inspected rather than smoothed away.
        /// </summary>
        public float SpineBiasRateDegreesPerPhase(SquatJointFamily family, float phase, float loadKg)
        {
            if (!SpineCalibrationEnabled)
                return 0f;
            if (family != SquatJointFamily.Abdomen && family != SquatJointFamily.Thorax)
                return 0f;
            if (!float.IsFinite(loadKg))
                return 0f;

            const float phaseStep = 0.0005f;
            float lower = SpineBiasDegrees(family, Mathf.Max(0f, phase - phaseStep), loadKg);
            float upper = SpineBiasDegrees(family, Mathf.Min(1f, phase + phaseStep), loadKg);
            float span = Mathf.Min(1f, phase + phaseStep) - Mathf.Max(0f, phase - phaseStep);
            return span <= 1e-6f ? 0f : (upper - lower) / span;
        }

        private float EvaluateSurface(
            float[][] valuesByLoad,
            bool abdomen,
            float phase,
            float loadKg)
        {
            float clampedLoad = Mathf.Clamp(loadKg, LoadKnotsKg[0], LoadKnotsKg[LoadKnotsKg.Length - 1]);
            int loadIndex = 0;
            while (loadIndex < LoadKnotsKg.Length - 2 && clampedLoad > LoadKnotsKg[loadIndex + 1])
                loadIndex++;

            float loadT = SmoothStep(Mathf.InverseLerp(
                LoadKnotsKg[loadIndex], LoadKnotsKg[loadIndex + 1], clampedLoad));
            float lower = EvaluatePhase(valuesByLoad[loadIndex], abdomen, phase);
            float upper = EvaluatePhase(valuesByLoad[loadIndex + 1], abdomen, phase);
            return Mathf.Clamp(
                Mathf.Lerp(lower, upper, loadT),
                -HardBoundRad * Mathf.Rad2Deg,
                HardBoundRad * Mathf.Rad2Deg);
        }

        private float EvaluatePhase(float[] values, bool abdomen, float phase)
        {
            float clamped = Mathf.Clamp01(phase);
            int phaseIndex = 0;
            while (phaseIndex < SpinePhaseKnots.Length - 2 && clamped > SpinePhaseKnots[phaseIndex + 1])
                phaseIndex++;

            float phaseT = SmoothStep(Mathf.InverseLerp(
                SpinePhaseKnots[phaseIndex], SpinePhaseKnots[phaseIndex + 1], clamped));
            float lower = ValueAt(values, abdomen, phaseIndex);
            float upper = values[phaseIndex + 1];
            return Mathf.Lerp(lower, upper, phaseT);
        }

        private float ValueAt(float[] values, bool abdomen, int index)
        {
            if (index != 0)
                return values[index];

            if (ReferenceEquals(values, AbdomenBias0Kg))
                return StandingAbdomenBiasDegrees0Kg;
            if (ReferenceEquals(values, ThoraxBias0Kg))
                return StandingThoraxBiasDegrees0Kg;
            if (ReferenceEquals(values, AbdomenBias25Kg))
                return StandingAbdomenBiasDegrees25Kg;
            if (ReferenceEquals(values, ThoraxBias25Kg))
                return StandingThoraxBiasDegrees25Kg;
            return values[0];
        }

        private static float SmoothStep(float value)
        {
            float clamped = Mathf.Clamp01(value);
            return clamped * clamped * (3f - 2f * clamped);
        }

        private static float Interpolate(float[] values, float standingValue, float phase)
        {
            float clamped = Mathf.Clamp01(phase);
            for (int index = 1; index < SpinePhaseKnots.Length; index++)
            {
                if (clamped > SpinePhaseKnots[index])
                    continue;
                float t = Mathf.InverseLerp(SpinePhaseKnots[index - 1], SpinePhaseKnots[index], clamped);
                return Mathf.Lerp(ValueAt(values, standingValue, index - 1), values[index], t);
            }
            return values[values.Length - 1];
        }

        private static float Slope(float[] values, float standingValue, float phase)
        {
            float clamped = Mathf.Clamp01(phase);
            for (int index = 1; index < SpinePhaseKnots.Length; index++)
            {
                if (clamped > SpinePhaseKnots[index])
                    continue;
                float span = SpinePhaseKnots[index] - SpinePhaseKnots[index - 1];
                return span <= 1e-6f
                    ? 0f
                    : (values[index] - ValueAt(values, standingValue, index - 1)) / span;
            }
            return 0f;
        }

        private static float ValueAt(float[] values, float standingValue, int index) =>
            index == 0 ? standingValue : values[index];

        public static int QualifiedLoadCount => LoadKnotsKg.Length;

        public static float QualifiedLoadKgAt(int index)
        {
            if (index < 0 || index >= LoadKnotsKg.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            return LoadKnotsKg[index];
        }

        public float MaxQualifiedSpineBiasDegrees()
        {
            float maximum = 0f;
            for (int loadIndex = 0; loadIndex < LoadKnotsKg.Length; loadIndex++)
            {
                for (int phaseIndex = 0; phaseIndex < SpinePhaseKnots.Length; phaseIndex++)
                {
                    float phase = SpinePhaseKnots[phaseIndex];
                    maximum = Mathf.Max(maximum,
                        Mathf.Abs(SpineBiasDegrees(SquatJointFamily.Abdomen, phase, LoadKnotsKg[loadIndex])));
                    maximum = Mathf.Max(maximum,
                        Mathf.Abs(SpineBiasDegrees(SquatJointFamily.Thorax, phase, LoadKnotsKg[loadIndex])));
                }
            }
            return maximum;
        }

        /// <summary>
        /// The qualified standing preload. The flat per-family bias is zero on
        /// every family; the spine additionally carries the standing knot of
        /// its phase-dependent table, qualified in 5H16.
        ///
        /// On the grounded plant the open-loop ankle imbalance is nulled by
        /// about -11.5 deg of plantarflexion bias: the COM-to-COP offset
        /// crosses zero between -10 and -12 deg and COM velocity at 0.6 s falls
        /// from 0.325 to 0.011 m/s
        /// (Artifacts/Measurements/GAM11-ankle-preload-cop-sensitivity.csv).
        /// That value is measured but deliberately not adopted, for two
        /// reasons. It sits on the 12 deg investigation boundary rather than
        /// near the 6 deg soft target, and it was identified with feedback
        /// disabled, so with the predictive balance controller running the two
        /// corrections stack and the athlete departs backwards instead. The
        /// spine values below avoid exactly that trap: they were identified
        /// with the loop closed.
        /// </summary>
        public static SquatEquilibriumPreload QualifiedStanding()
        {
            var preload = new SquatEquilibriumPreload();
            preload.SetAnatomicalFlexionBiasDegrees(SquatJointFamily.Ankle, 0f);
            preload.SetAnatomicalFlexionBiasDegrees(SquatJointFamily.Knee, 0f);
            preload.SetAnatomicalFlexionBiasDegrees(SquatJointFamily.Hip, 0f);
            preload.SetAnatomicalFlexionBiasDegrees(SquatJointFamily.Abdomen, 0f);
            preload.SetAnatomicalFlexionBiasDegrees(SquatJointFamily.Thorax, 0f);
            // Qualified in 5H16. The flat family bias above stays zero; these
            // are the standing knot of the phase-dependent spine table.
            preload.StandingAbdomenBiasDegrees0Kg = 0.83f;
            preload.StandingThoraxBiasDegrees0Kg = 0.92f;
            preload.StandingAbdomenBiasDegrees25Kg = 3.93f;
            preload.StandingThoraxBiasDegrees25Kg = 3.67f;
            return preload;
        }
    }
}
