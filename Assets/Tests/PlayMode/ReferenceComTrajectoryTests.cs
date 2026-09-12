using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Equipment;
using PowerliftingSimulator.Squat;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.Tests
{
    /// <summary>
    /// Phase 5H12, section 6. The balance controller calibrates the system
    /// centre of mass against the support polygon once, in setup, and then uses
    /// that standing relationship as its reference for the whole squat. This
    /// derives what the accepted GAM-10 reference actually implies the centre of
    /// mass should do, using the same segment masses and the same centre
    /// placement rule the physical rig builds its bodies from, and reports how
    /// far the standing assumption is from it.
    ///
    /// Offline and diagnostic. It poses the reference preview, reads bone
    /// transforms, and computes. It touches no physical body and no control
    /// value, and it does not author a phase COM curve for anyone to consume.
    /// </summary>
    public sealed class ReferenceComTrajectoryTests
    {
        private const string ReferenceScene = "SquatReferencePreview";
        private const string MeasurementDirectory = "Artifacts/Measurements/GAM-11";
        private const float ProductionPhaseRate = 0.30f;

        private SquatReferencePreview _preview;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(ReferenceScene, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            while (!load.isDone)
                yield return null;
            yield return null;
            _preview = UnityEngine.Object.FindFirstObjectByType<SquatReferencePreview>();
            Assert.That(_preview, Is.Not.Null);
            _preview.SetShowLandmarks(false);
            _preview.SetShowReferenceBarGhost(false);
            yield return null;
        }

        [UnityTest]
        public IEnumerator R1_REFERENCE_SYSTEM_COM_TRAJECTORY()
        {
            Animator animator = _preview.ReferenceAnimator;
            Assert.That(animator, Is.Not.Null);

            var csv = new StringBuilder();
            csv.AppendLine("phase,com_ap_0kg,com_height_0kg,com_ap_25kg,com_height_25kg," +
                           "reference_world_trunk_pitch_deg,bar_ap,thorax_ap,pelvis_ap");

            var samples = new List<float[]>();
            const int steps = 100;
            for (int step = 0; step <= steps; step++)
            {
                float phase = step / (float)steps;
                _preview.SetReviewPose(phase, SquatPhaseDirection.Descent,
                    phase >= 0.999f ? SquatState.BOTTOM : SquatState.DESCENT);
                yield return null;

                ComputeReferenceCom(animator, 0f, out Vector3 com0, out _, out _, out _);
                ComputeReferenceCom(animator, 25f, out Vector3 com25, out Vector3 barWorld,
                    out Vector3 thoraxCentre, out Vector3 pelvisCentre);

                Vector3 trunkAxis = thoraxCentre - pelvisCentre;
                float trunkPitch = trunkAxis.sqrMagnitude < 1e-8f
                    ? 0f
                    : Mathf.Atan2(trunkAxis.z, trunkAxis.y) * Mathf.Rad2Deg;

                samples.Add(new[] { phase, com0.z, com0.y, com25.z, com25.y, trunkPitch });
                csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:F3},{1:F5},{2:F5},{3:F5},{4:F5},{5:F3},{6:F5},{7:F5},{8:F5}",
                    phase, com0.z, com0.y, com25.z, com25.y, trunkPitch,
                    barWorld.z, thoraxCentre.z, pelvisCentre.z));
            }

            var report = new StringBuilder();
            report.AppendLine("GAM-11 PHASE 5H12 R1 REFERENCE SYSTEM COM TRAJECTORY");
            report.AppendLine("Derived from the accepted GAM-10 reference pose using the same segment");
            report.AppendLine("mass fractions and the same centre placement rule PhysicalAthleteRig uses");
            report.AppendLine("to build its bodies. No physical body is read.");
            report.AppendLine();

            float standing0 = samples[0][1];
            float standing25 = samples[0][3];
            float maxDelta0 = 0f, maxDelta25 = 0f, maxPhase0 = 0f, maxPhase25 = 0f;
            float bottom0 = samples[samples.Count - 1][1];
            float bottom25 = samples[samples.Count - 1][3];

            foreach (float[] s in samples)
            {
                float d0 = Mathf.Abs(s[1] - standing0);
                float d25 = Mathf.Abs(s[3] - standing25);
                if (d0 > maxDelta0) { maxDelta0 = d0; maxPhase0 = s[0]; }
                if (d25 > maxDelta25) { maxDelta25 = d25; maxPhase25 = s[0]; }
            }

            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "REFERENCE_COM_AP_STANDING       0 kg = {0:F5} m    25 kg = {1:F5} m", standing0, standing25));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "REFERENCE_COM_AP_BOTTOM         0 kg = {0:F5} m    25 kg = {1:F5} m", bottom0, bottom25));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "BOTTOM_COM_AP_DELTA_FROM_STANDING 0 kg = {0:F5} m  25 kg = {1:F5} m",
                bottom0 - standing0, bottom25 - standing25));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "MAX_PHASE_COM_AP_DELTA          0 kg = {0:F5} m at s_q {1:F2}    25 kg = {2:F5} m at s_q {3:F2}",
                maxDelta0, maxPhase0, maxDelta25, maxPhase25));
            report.AppendLine();

            // Numerical derivatives along the production phase schedule.
            report.AppendLine("Along the production phase rate of " +
                              ProductionPhaseRate.ToString("F2", CultureInfo.InvariantCulture) + " per second:");
            float dPhase = 1f / steps;
            float dt = dPhase / ProductionPhaseRate;
            float maxVel0 = 0f, maxAcc0 = 0f, maxVelV = 0f, maxAccV = 0f;
            for (int i = 1; i < samples.Count - 1; i++)
            {
                float vAp = (samples[i + 1][1] - samples[i - 1][1]) / (2f * dt);
                float aAp = (samples[i + 1][1] - 2f * samples[i][1] + samples[i - 1][1]) / (dt * dt);
                float vV = (samples[i + 1][2] - samples[i - 1][2]) / (2f * dt);
                float aV = (samples[i + 1][2] - 2f * samples[i][2] + samples[i - 1][2]) / (dt * dt);
                maxVel0 = Mathf.Max(maxVel0, Mathf.Abs(vAp));
                maxAcc0 = Mathf.Max(maxAcc0, Mathf.Abs(aAp));
                maxVelV = Mathf.Max(maxVelV, Mathf.Abs(vV));
                maxAccV = Mathf.Max(maxAccV, Mathf.Abs(aV));
            }
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  REFERENCE_COM_AP_VELOCITY   max {0:F4} m/s     ACCELERATION max {1:F4} m/s^2", maxVel0, maxAcc0));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  REFERENCE_COM_VERT_VELOCITY max {0:F4} m/s     ACCELERATION max {1:F4} m/s^2", maxVelV, maxAccV));
            report.AppendLine();
            report.AppendLine("CURRENT_STANDING_COM_REFERENCE_ERROR_AT_BOTTOM = " +
                              (bottom0 - standing0).ToString("F5", CultureInfo.InvariantCulture) +
                              " m unloaded, " +
                              (bottom25 - standing25).ToString("F5", CultureInfo.InvariantCulture) + " m at 25 kg.");
            report.AppendLine("That is the amount by which holding the standing relationship misplaces the");
            report.AppendLine("centre-of-mass setpoint at the bottom of the accepted reference squat.");
            report.AppendLine();
            report.AppendLine("phase   com_ap_0kg   com_ap_25kg   trunk_pitch_deg");
            foreach (float[] s in samples)
            {
                if (Mathf.Abs(s[0] * 20f - Mathf.Round(s[0] * 20f)) > 1e-4f)
                    continue;
                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,5:F2}   {1,10:F5}   {2,11:F5}   {3,15:F2}", s[0], s[1], s[3], s[5]));
            }

            WriteMeasurement("GAM11-5h12-r1-reference-com-trajectory.csv", csv.ToString());
            WriteMeasurement("GAM11-5h12-r1-reference-com-trajectory.txt", report.ToString());
            Debug.Log(report.ToString());
            yield return null;
        }

        /// <summary>
        /// Reproduces PhysicalAthleteRig.CreateSegment's centre placement: the
        /// body centre is the recipe's centre-of-mass fraction between the
        /// proximal and distal reference bones, plus the recipe's fixed offset,
        /// with mass PrototypeBodyMassKg times the recipe mass fraction. Bodies
        /// whose proximal and distal bones are the same sit on that bone.
        /// </summary>
        private static void ComputeReferenceCom(
            Animator animator,
            float barLoadKg,
            out Vector3 com,
            out Vector3 barWorld,
            out Vector3 thoraxCentre,
            out Vector3 pelvisCentre)
        {
            Vector3 weighted = Vector3.zero;
            float totalMass = 0f;
            thoraxCentre = Vector3.zero;
            pelvisCentre = Vector3.zero;
            Quaternion thoraxRotation = Quaternion.identity;

            foreach (PhysicalSegmentRecipe recipe in PhysicalAthleteDefinition.Segments)
            {
                Transform proximal = animator.GetBoneTransform(recipe.ProximalBone);
                Transform distal = animator.GetBoneTransform(recipe.DistalBone);
                if (proximal == null || distal == null)
                    continue;

                Vector3 centre = recipe.ProximalBone == recipe.DistalBone
                    ? proximal.position
                    : Vector3.Lerp(proximal.position, distal.position, recipe.ComFraction);
                centre += recipe.FixedCenterOffsetMeters;

                float mass = PhysicalAthleteDefinition.PrototypeBodyMassKg * recipe.MassFraction;
                weighted += centre * mass;
                totalMass += mass;

                if (recipe.Id == "thorax")
                {
                    thoraxCentre = centre;
                    Vector3 axis = distal.position - proximal.position;
                    thoraxRotation = axis.sqrMagnitude > 1e-8f
                        ? Quaternion.FromToRotation(Vector3.up, axis.normalized)
                        : Quaternion.identity;
                }
                else if (recipe.Id == "pelvis")
                {
                    pelvisCentre = centre;
                }
            }

            // The bar rides the calibrated trap shelf anchor on the thorax.
            barWorld = thoraxCentre + thoraxRotation * SquatBarSaddle.ThoraxLocalAnchor;
            if (barLoadKg > 0f)
            {
                weighted += barWorld * barLoadKg;
                totalMass += barLoadKg;
            }
            com = weighted / Mathf.Max(totalMass, 1e-6f);
        }

        private static void WriteMeasurement(string filename, string content)
        {
            string directory = Path.GetFullPath(MeasurementDirectory);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, filename), content);
        }
    }
}
