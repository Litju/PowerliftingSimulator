using System;
using PowerliftingSimulator.Athlete;

namespace PowerliftingSimulator.Deadlift.Unity
{
    /// <summary>
    /// GAM-70 owner-approved deadlift-local ankle inversion/eversion DOF.
    ///
    /// Evidence that required it (docs/evidence/GAM-70/a3-grounded-pretension.txt):
    /// with the canonical hinge ankle and knee, foot roll about the sole's long
    /// axis is unactuated below the hip; the frozen baseline drifts 37 mm across
    /// the sole in 10 ticks; four reference-shaping regulators were measured and
    /// rejected. The canonical rig, squat and bench are unchanged: this is enabled
    /// only by the deadlift plant, after build and before tick 0.
    ///
    /// Declared game approximation (not measured physiology):
    ///   range +/-15 deg (symmetric Unity limit; inside typical eversion ROM,
    ///   the smaller side of the subtalar range);
    ///   ceiling 60 N m (order of published inversion/eversion strength);
    ///   spring and damper = ankle-flexion family x 60/450, so the roll channel
    ///   saturates at the same error angle (11.2 deg) as ankle flexion.
    /// </summary>
    public static class DeadliftAnkleRoll
    {
        public const float LimitDegrees = 15f;
        public const float CeilingNm = 60f;

        public static JointFamilyProfile Profile
        {
            get
            {
                JointFamilyProfile ankle = default;
                foreach (JointFamilyProfile p in PoweredJointController.FamilyProfiles)
                    if (p.Id == "ankle") ankle = p;
                if (ankle.Id == null)
                    throw new InvalidOperationException("ankle family profile missing");
                float k = CeilingNm / ankle.BaseCapacityNm;
                return new JointFamilyProfile("ankle_roll", ankle.Spring * k, ankle.Damper * k, CeilingNm, ankle.MaxTargetRateRadS);
            }
        }

        public static void Enable(PhysicalAthleteRig rig)
        {
            if (rig == null)
                throw new ArgumentNullException(nameof(rig));
            rig.EnableAnkleRoll(LimitDegrees);
            JointFamilyProfile p = Profile;
            rig.PoweredController.EnableSecondaryChannel("left_foot", p);
            rig.PoweredController.EnableSecondaryChannel("right_foot", p);
        }
    }
}
