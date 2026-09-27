using NUnit.Framework;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;

namespace PowerliftingSimulator.Tests
{
    public sealed class SquatComStabilizerV2Tests
    {
        [Test]
        public void DEFAULT_CALIBRATION_IS_VERSIONED_AND_WITHIN_THE_PARAMETER_BUDGET()
        {
            SquatComStabilizerV2Calibration calibration = SquatComStabilizerV2Calibration.Default;
            Assert.That(calibration.Version, Is.EqualTo("GAM13_SQUAT_COM_STABILIZER_V2_1"));
            Assert.That(SquatComStabilizerV2Calibration.ActiveScalarCount, Is.EqualTo(14));
            Assert.That(calibration.AnkleApWeight, Is.GreaterThan(calibration.HipApWeight));
            Assert.That(calibration.TrunkApCounterWeight, Is.GreaterThan(0f));
            Assert.That(calibration.AnkleMlWeight, Is.GreaterThan(calibration.HipMlWeight));
            Assert.That(calibration.KpAp, Is.EqualTo(0.80f / 0.20446f).Within(1e-6f));
            Assert.That(calibration.KdAp, Is.EqualTo(calibration.KpAp * 0.1f).Within(1e-6f));
            Assert.That(calibration.MaxApCorrectionRad, Is.EqualTo(0.26180f).Within(1e-6f));
            Assert.That(calibration.MaxApCorrectionRateRadS, Is.EqualTo(4f).Within(1e-6f));
        }

        [Test]
        public void PD_ERRORS_DISTRIBUTE_THROUGH_FIXED_TARGET_SPACE_WEIGHTS()
        {
            var stabilizer = new SquatComStabilizerV2();
            SquatBalanceCorrectionV2 correction = stabilizer.Solve(
                new Vector3(0.02f, 1f, 0.05f),
                new Vector3(0f, 0f, 0.10f),
                Vector3.zero,
                0.01f,
                0.005f,
                0f,
                1f);

            Assert.That(correction.ErrorApM, Is.EqualTo(0.04f).Within(1e-6f));
            Assert.That(correction.ErrorMlM, Is.EqualTo(0.015f).Within(1e-6f));
            Assert.That(correction.CommandApRad, Is.EqualTo(-(
                SquatComStabilizerV2Calibration.Default.KpAp * 0.04f +
                SquatComStabilizerV2Calibration.Default.KdAp * 0.10f)).Within(1e-6f));
            Assert.That(correction.CommandMlRad, Is.EqualTo(-0.00975f).Within(1e-6f));
            Assert.That(correction.AnkleApRad, Is.EqualTo(correction.AppliedApRad).Within(1e-6f));
            Assert.That(correction.HipApRad, Is.EqualTo(correction.AppliedApRad * 0.20f).Within(1e-6f));
            Assert.That(correction.TrunkApRad, Is.EqualTo(-correction.AppliedApRad * 0.08f).Within(1e-6f));
            Assert.That(correction.AnkleMlRad, Is.EqualTo(correction.AppliedMlRad).Within(1e-6f));
            Assert.That(correction.HipMlRad, Is.EqualTo(correction.AppliedMlRad * 0.15f).Within(1e-6f));
        }

        [Test]
        public void OUTPUTS_ARE_BOUNDED_AND_RATE_LIMITED()
        {
            var stabilizer = new SquatComStabilizerV2();
            SquatBalanceCorrectionV2 correction = default;
            for (int tick = 0; tick < 20; tick++)
            {
                correction = stabilizer.Solve(
                    new Vector3(2f, 1f, 2f),
                    Vector3.zero,
                    Vector3.zero,
                    0f,
                    0f,
                    0f,
                    0.01f);
            }

            Assert.That(correction.AppliedApRad, Is.EqualTo(-0.26180f).Within(1e-6f));
            Assert.That(correction.AppliedMlRad, Is.EqualTo(-0.05f).Within(1e-6f));
            Assert.That(correction.IsBoundSaturated, Is.True);
            Assert.That(correction.IsApBoundSaturated, Is.True);

            SquatBalanceCorrectionV2 reversed = stabilizer.Solve(
                new Vector3(-2f, 1f, -2f),
                Vector3.zero,
                Vector3.zero,
                0f,
                0f,
                0f,
                0.01f);
            Assert.That(Mathf.Abs(reversed.AppliedApRad - correction.AppliedApRad), Is.LessThanOrEqualTo(0.04f + 1e-6f));
            Assert.That(Mathf.Abs(reversed.AppliedMlRad - correction.AppliedMlRad), Is.LessThanOrEqualTo(0.004f + 1e-6f));
        }

        [Test]
        public void PLAYER_BALANCE_BIAS_IS_OPTIONAL_AND_FRONTAL_ONLY()
        {
            var stabilizer = new SquatComStabilizerV2();
            SquatBalanceCorrectionV2 correction = stabilizer.Solve(
                Vector3.zero,
                Vector3.zero,
                Vector3.zero,
                0f,
                0f,
                1f,
                0.05f);

            Assert.That(correction.AppliedApRad, Is.EqualTo(0f));
            Assert.That(correction.CommandMlRad, Is.EqualTo(0f));
            Assert.That(correction.AppliedMlRad, Is.EqualTo(0.02f).Within(1e-6f));
        }
    }
}
