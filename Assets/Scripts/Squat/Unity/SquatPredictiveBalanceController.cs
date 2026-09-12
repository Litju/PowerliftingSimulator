using System;
using UnityEngine;

namespace PowerliftingSimulator.Squat.Unity
{
    /// <summary>
    /// Contact-aware zero-step balance. It regulates the system centre of
    /// mass by asking for a centre of pressure inside the measured support
    /// polygon, and it reaches that centre of pressure only through bounded
    /// joint-target offsets. It writes no force, no torque, no velocity and
    /// no transform; the ground reactions come from the contacts themselves.
    ///
    /// Small disturbances are handled at the ankle. As the ankle offset
    /// approaches its bound, or the capture point approaches the edge of
    /// support, hip and trunk offsets blend in.
    /// </summary>
    public sealed class SquatPredictiveBalanceController
    {
        public const string ClaimClass = "GAME_CONTROL_REDUCED_ORDER_MODEL";

        // Independent bounds. Each is smaller than the canonical excursion of
        // the joint it trims, so balance can shade the accepted movement but
        // never author it.
        // 15 deg. The bound exists so balance cannot author the movement, and
        // the canonical ankle travels 27 deg across the squat, so this still
        // sits well inside the reference excursion. It is not the tipping
        // guard: that is the clamp holding the desired COP inside the measured
        // support polygon, which is what actually stops the foot rotating.
        // At the measured 0.204 m/rad it is worth 53 mm of centre-of-pressure
        // travel, against the 52 mm the grounded standing state needs.
        public const float MaxAnkleSagittalOffsetRad = 0.26180f;   // 15 deg
        public const float MaxHipSagittalOffsetRad = 0.12217f;     // 7 deg
        public const float MaxTrunkSagittalOffsetRad = 0.10472f;   // 6 deg
        public const float MaxAnkleFrontalOffsetRad = 0.05236f;    // 3 deg
        public const float MaxHipFrontalOffsetRad = 0.03491f;      // 2 deg

        // Interior margin on the support polygon. The centre of pressure at
        // the polygon edge is the foot on the point of tipping, so this is the
        // tipping guard, not a tuning knob.
        public const float SupportInteriorMarginM = 0.030f;

        public const float DefaultComKp = 20f;   // 1/s^2
        public const float DefaultComKd = 12f;   // 1/s
        public const float DefaultMlComKp = 12f;
        public const float DefaultMlComKd = 5f;

        private const float MaxOffsetRateRadPerSecond = 4f;
        private const float HipBlendOnsetFraction = 0.85f;
        private const float HipStrategyGain = 0.35f;

        /// <summary>
        /// GAME_PHYSICS_CALIBRATION. Metres of measured centre-of-pressure
        /// travel per radian of commanded ankle target offset, identified on
        /// the grounded unloaded plant with balance and preload off, over
        /// +/-3 deg with a fresh scene reload per sample
        /// (Artifacts/Measurements/GAM11-g-target-cop-calibration.csv,
        /// R^2 = 0.99997, left 0.209 and right 0.200 m/rad).
        ///
        /// This replaces inverting the plant through the authored joint
        /// spring. A ConfigurableJoint drive is solved together with the
        /// body inertias, the other joints and the contacts, so
        /// positionSpring times target error is not what the target offset
        /// is worth at the ground. Reasoning through the spring implied
        /// about 0.66 m/rad and so under-commanded the ankle by roughly
        /// 3.2x, which is what the athlete fell from in C0.
        ///
        /// It is a property of this rig on this platform in this engine, not
        /// a biomechanical constant, and it does not change the GAM-7 spring,
        /// damper or force ceiling, which stay exactly as authored.
        /// </summary>
        public const float MeasuredTargetToCopMPerRad = 0.20446f;

        /// <summary>
        /// Proportional gain on centre-of-pressure error. There is no
        /// integrator, so the loop settles where the residual error is worth
        /// the offset that holds it: a required shift s settles at
        /// s/(1+K) of error and K/(1+K) * s/G of ankle offset.
        ///
        /// At the measured 52 mm standing requirement, K = 4 asks for about
        /// 11.7 deg of the 15 deg bound and leaves the rest for the moving
        /// part of the job.
        /// </summary>
        public const float DefaultCopTrackingGain = 4f;

        /// <summary>Keeps the plant inversion finite if a gain is misconfigured.</summary>
        private const float MinimumTargetToCopMPerRad = 0.01f;

        public float ComKp { get; set; } = DefaultComKp;
        public float ComKd { get; set; } = DefaultComKd;
        public float MlComKp { get; set; } = DefaultMlComKp;
        public float MlComKd { get; set; } = DefaultMlComKd;
        public float CopTrackingGain { get; set; } = DefaultCopTrackingGain;

        /// <summary>
        /// The identified target-to-COP gain the ankle mapping inverts.
        /// Settable so an experiment can measure the plant it is actually
        /// running on; it is never derived from the joint drive parameters.
        /// </summary>
        public float TargetToCopMPerRad { get; set; } = MeasuredTargetToCopMPerRad;

        /// <summary>Ankle target bound, so an experiment can test it.</summary>
        public float AnkleSagittalBoundRad { get; set; } = MaxAnkleSagittalOffsetRad;

        /// <summary>
        /// The proximal channel. Turning it off isolates the ankle, which is
        /// how the question "is the ankle actually out of authority" gets a
        /// measured answer instead of an assumed one.
        /// </summary>
        public bool HipTrunkStrategyEnabled { get; set; } = true;

        /// <summary>
        /// The posture guard. Off reproduces the rejected behaviour, where
        /// the loop buys a centred centre of mass with as much canonical
        /// posture as the trunk will give it, so the gate has something to
        /// keep failing against.
        /// </summary>
        public bool PostureGuardEnabled { get; set; } = true;

        /// <summary>
        /// Posture error, in radians, at which the guard begins withdrawing
        /// the ankle command that is causing it. Below this the ankle has its
        /// full authority; the athlete is allowed to sway.
        /// </summary>
        public const float PostureGuardOnsetRad = 0.06981f;   // 4 deg

        /// <summary>
        /// Posture error at which the harmful direction is withdrawn
        /// entirely. It sits under the 10 deg standing threshold so the guard
        /// acts before the gate would fail, rather than confirming the failure
        /// after it happens.
        /// </summary>
        public const float PostureGuardFullRad = 0.13963f;    // 8 deg

        /// <summary>
        /// Joint-limit proximity at which the guard stops trusting the drive.
        /// A joint resting on its limit is held by the limit, so any command
        /// pushing further into it is buying nothing.
        /// </summary>
        public const float LimitGuardOnset = 0.55f;
        public const float LimitGuardFull = 0.85f;

        /// <summary>
        /// Posture error growth, rad/s, at which the trend term counts fully.
        /// The guard keys on the trend rather than on a direction because the
        /// direction is not stable: probing the plant at 0.6 s says a more
        /// positive ankle command improves trunk posture by 0.58 deg/deg, at
        /// 1.5 s the same probe says it costs 1.74 deg/deg, and the closed
        /// loop agrees with neither, holding the ankle at its bound while the
        /// trunk folds onto its limit over seconds
        /// (Artifacts/Measurements/GAM11-ankle-to-posture-cost.csv). A guard
        /// built on a fixed harmful direction would be calibrated to whichever
        /// window it happened to be measured in. Growth and limit proximity
        /// are observable in the loop and do not depend on that sign.
        /// </summary>
        public const float PostureGuardRateFullRadPerS = 0.17453f;   // 10 deg/s

        /// <summary>
        /// How the limit half of the posture guard decides a joint is at risk.
        /// </summary>
        public enum LimitGuardSemantics
        {
            /// <summary>
            /// Absolute occupancy of the authored range, the original rule.
            /// Retained so the rejected behaviour stays reproducible.
            /// </summary>
            AbsoluteOccupancy,

            /// <summary>Limit term off. Diagnostic isolation only.</summary>
            Disabled,

            /// <summary>
            /// Occupancy the reference did not ask for, measured against the
            /// margin the reference left. See ComputeLimitScale.
            /// </summary>
            UnexpectedMarginConsumption
        }

        /// <summary>
        /// Which ankle demand the proximal strategy is allowed to see. The
        /// hip strategy exists for an ankle that has run out of authority, so
        /// reading it from the post-guard command means a guard that removes
        /// the ankle also removes the evidence that it needed removing.
        /// </summary>
        public enum ProximalTrigger
        {
            GuardedAnkleCommand,
            RawAnkleDemand
        }

        /// <summary>
        /// UnexpectedMarginConsumption is production. AbsoluteOccupancy was,
        /// and withdrew the entire ankle channel at squat depth because the
        /// accepted GAM-10 reference commands 111 deg of hip flexion against a
        /// 120 deg range: a correctly executed deep squat read 0.93 and closed
        /// a guard whose 0.55 and 0.85 thresholds were set standing. Measured
        /// at s_q 0.55, 0.80 and 1.00 under both loads, the hip's composed
        /// target equals its nominal target exactly, so the quantity that rule
        /// read carried no information at all about the correction it removed
        /// (GAM11-5h13-d2-limit-margins.csv, GAM11-5h13-e1-guard-candidates-static.txt).
        /// </summary>
        public LimitGuardSemantics LimitSemantics { get; set; } =
            LimitGuardSemantics.UnexpectedMarginConsumption;

        /// <summary>
        /// RawAnkleDemand is production. Reading the blend from the guarded
        /// command meant the guard removed the ankle and then hid the removal
        /// from the strategy that exists to rescue it: at 25 kg s_q 0.80 the
        /// balance law asked for 5.28 bounds of ankle correction and the hip
        /// blend stayed at zero because the guarded command looked like 0.679
        /// of authority used (GAM11-5h13-d1-guard-cascade.txt).
        /// </summary>
        public ProximalTrigger ProximalTriggerSource { get; set; } = ProximalTrigger.RawAnkleDemand;

        // Posture state, supplied by the adapter from the previous
        // post-physics observation.
        public float PostureErrorRad { get; private set; }
        public float PostureErrorRateRadPerS { get; private set; }
        public float PostureLimitProximity { get; private set; }

        /// <summary>
        /// The largest fraction, over the guarded joints, of the limit margin
        /// the reference left unused that something other than the reference
        /// has consumed. Zero when every joint sits exactly where the accepted
        /// reference put it, however deep that is; one when a joint has eaten
        /// all the room the reference left it.
        /// </summary>
        public float UnexpectedMarginConsumedFraction { get; private set; }

        public float PostureGuardScale { get; private set; } = 1f;
        public float RawAnkleSagittalOffsetRad { get; private set; }

        // Actuation outputs. These are target offsets in the sagittal and
        // frontal planes, in radians, already bounded and rate limited.
        public float AnkleSagittalOffsetRad { get; private set; }
        public float HipSagittalOffsetRad { get; private set; }
        public float TrunkSagittalOffsetRad { get; private set; }
        public float AnkleFrontalOffsetRad { get; private set; }
        public float HipFrontalOffsetRad { get; private set; }

        // Diagnostics.
        public bool IsActive { get; private set; }
        public float ComRefAp { get; private set; }
        public float ComApError { get; private set; }
        public float DesiredComAccelerationAp { get; private set; }
        public float CopDesiredAp { get; private set; }
        public float CopDesiredApUnclamped { get; private set; }
        public float CopMeasuredAp { get; private set; }
        public float CopErrorAp { get; private set; }
        public bool HasCopMeasurement { get; private set; }
        public float CaptureAp { get; private set; }
        public float CaptureMarginFront { get; private set; }
        public float CaptureMarginRear { get; private set; }
        public float HipStrategyBlend { get; private set; }
        public bool IsAnkleOffsetSaturated { get; private set; }
        public float RequestedAnkleTorqueNm { get; private set; }

        /// <summary>
        /// How much of the ankle target bound the current command is using.
        /// This is the honest measure of remaining ankle authority, and the
        /// gate any second strategy has to be justified against.
        /// </summary>
        public float AnkleAuthorityFraction { get; private set; }

        /// <summary>
        /// The same fraction taken on the demand before any safety filtering.
        /// Kept separate so a suppressed ankle can never read as an idle one:
        /// AnkleAuthorityFraction says what the ankle was allowed to use,
        /// this says what the balance law actually asked for.
        /// </summary>
        public float RawAnkleAuthorityFraction { get; private set; }

        public void Reset()
        {
            AnkleSagittalOffsetRad = 0f;
            HipSagittalOffsetRad = 0f;
            TrunkSagittalOffsetRad = 0f;
            AnkleFrontalOffsetRad = 0f;
            HipFrontalOffsetRad = 0f;
            HipStrategyBlend = 0f;
            PostureGuardScale = 1f;
            RawAnkleSagittalOffsetRad = 0f;
            PostureErrorRad = 0f;
            PostureErrorRateRadPerS = 0f;
            PostureLimitProximity = 0f;
            IsAnkleOffsetSaturated = false;
            AnkleAuthorityFraction = 0f;
            RawAnkleAuthorityFraction = 0f;
            UnexpectedMarginConsumedFraction = 0f;
            IsActive = false;
            HasCopMeasurement = false;
        }

        /// <summary>
        /// One control step against the previous post-physics observation.
        /// </summary>
        /// <param name="ankleAnchorAp">
        /// Anteroposterior position of the ankle joint anchors, the point the
        /// ankle torque acts about when it shifts the centre of pressure.
        /// </param>
        public void Solve(
            SquatBalanceObserver balance,
            float comRefAp,
            float comRefMl,
            float ankleAnchorAp,
            float dt)
        {
            if (balance == null)
                throw new ArgumentNullException(nameof(balance));
            if (!float.IsFinite(dt) || dt <= 0f)
                throw new ArgumentOutOfRangeException(nameof(dt));

            if (!balance.HasSupport || balance.SystemMassKg <= 0f)
            {
                // Airborne or contactless: there is nothing to push against,
                // so decay to the reference rather than command into nothing.
                IsActive = false;
                DecayToward(dt);
                return;
            }

            IsActive = true;
            ComRefAp = comRefAp;
            CaptureAp = balance.CaptureAp;
            CaptureMarginFront = balance.CaptureMarginFront;
            CaptureMarginRear = balance.CaptureMarginRear;
            HasCopMeasurement = balance.HasCopEstimate;
            CopMeasuredAp = balance.HasCopEstimate ? balance.CopEstimate.z : balance.SupportApCenter;

            // Outer reduced-order law: choose the COM acceleration we want,
            // then the COP that would produce it.
            float error = balance.SystemCom.z - comRefAp;
            float velocity = balance.SystemComVelocity.z;
            float desiredAcceleration = -ComKp * error - ComKd * velocity;
            ComApError = error;
            DesiredComAccelerationAp = desiredAcceleration;

            float heightOverGravity = balance.ComHeightM / SquatBalanceObserver.GravityMagnitudeMps2;
            float copDesired = balance.SystemCom.z - heightOverGravity * desiredAcceleration;
            CopDesiredApUnclamped = copDesired;

            float interiorMargin = Mathf.Min(
                SupportInteriorMarginM,
                Mathf.Max(0f, 0.4f * balance.SupportApLength));
            float copMin = balance.SupportApMin + interiorMargin;
            float copMax = balance.SupportApMax - interiorMargin;
            if (copMin > copMax)
            {
                float mid = 0.5f * (copMin + copMax);
                copMin = copMax = mid;
            }
            CopDesiredAp = Mathf.Clamp(copDesired, copMin, copMax);
            CopErrorAp = CopMeasuredAp - CopDesiredAp;

            // Ankle strategy. The offset is inverted through the measured
            // target-to-COP gain, on the COP error the engine actually
            // reported. Positive logical X at the ankle is plantarflexion,
            // which drives the centre of pressure forward; R6 calibrates that
            // sign and the identification confirms it.
            //
            // With no COP measurement there is nothing to track, so the
            // reduced-order model stands in: place the COP where the outer
            // law asked relative to the ankle, through the same measured
            // gain rather than through the joint spring.
            float gain = Mathf.Max(MinimumTargetToCopMPerRad, TargetToCopMPerRad);
            float ankleTargetOffset = HasCopMeasurement
                ? CopTrackingGain * (CopDesiredAp - CopMeasuredAp) / gain
                : (CopDesiredAp - ankleAnchorAp) / gain;

            // Kept as a diagnostic so the contact-moment cross-check has the
            // reduced-order moment to compare against. It no longer feeds the
            // actuator mapping.
            RequestedAnkleTorqueNm = balance.SystemMassKg *
                SquatBalanceObserver.GravityMagnitudeMps2 * (CopDesiredAp - ankleAnchorAp);

            // Posture guard. Balance may not buy a centred centre of mass
            // with unbounded canonical posture: withdraw ankle authority as
            // posture error grows, and as a joint closes on its anatomical
            // limit, where the limit rather than the drive would be holding
            // the pose.
            RawAnkleSagittalOffsetRad = ankleTargetOffset;
            RawAnkleAuthorityFraction = Mathf.Abs(ankleTargetOffset) / AnkleSagittalBoundRad;
            PostureGuardScale = ComputePostureGuardScale();
            ankleTargetOffset *= PostureGuardScale;

            float saturationFraction = Mathf.Abs(ankleTargetOffset) / AnkleSagittalBoundRad;
            IsAnkleOffsetSaturated = saturationFraction >= 1f;
            AnkleAuthorityFraction = saturationFraction;

            // Hip strategy blends in when the ankle runs out of authority or
            // the capture point closes on the edge of support.
            // Horak and Nashner: the hip strategy is what you reach for when
            // the ankle has run out, not something you add on top of a working
            // ankle. The gate is therefore the ankle offset bound alone. The
            // capture margin stays a diagnostic; driving the blend from it
            // turned the hip on hardest exactly when the stance was most
            // fragile, and the hip offset then accelerated the collapse.
            //
            // Which fraction the gate reads is a separate question from how
            // hard the gate bites. Reading the guarded command makes a
            // withdrawn ankle indistinguishable from an unused one.
            float triggerFraction = ProximalTriggerSource == ProximalTrigger.RawAnkleDemand
                ? RawAnkleAuthorityFraction
                : saturationFraction;
            HipStrategyBlend = HipTrunkStrategyEnabled
                ? Mathf.Clamp01(Mathf.InverseLerp(HipBlendOnsetFraction, 1f, triggerFraction))
                : 0f;

            // Hip strategy. This controller can only hold a bounded target
            // offset, so the useful hip action is the steady one: for a COM
            // that is too far forward, extend the hip and the trunk to carry
            // the upper body mass back over the feet. Hip flexion is negative
            // in logical joint space and trunk flexion is positive, both
            // calibrated by R6, so extension is positive hip and negative
            // trunk.
            float hipTargetOffset = HipStrategyGain * HipStrategyBlend *
                (ComKp * error + ComKd * velocity) * heightOverGravity;
            float trunkTargetOffset = -hipTargetOffset * 0.5f;

            float maxStep = MaxOffsetRateRadPerSecond * dt;
            AnkleSagittalOffsetRad = Step(AnkleSagittalOffsetRad, ankleTargetOffset, AnkleSagittalBoundRad, maxStep);
            HipSagittalOffsetRad = Step(HipSagittalOffsetRad, hipTargetOffset, MaxHipSagittalOffsetRad, maxStep);
            TrunkSagittalOffsetRad = Step(TrunkSagittalOffsetRad, trunkTargetOffset, MaxTrunkSagittalOffsetRad, maxStep);

            SolveFrontal(balance, comRefMl, maxStep);
        }

        /// <summary>
        /// Continuous, bounded and allocation-free. Posture error alone does
        /// not withdraw authority, because the settle transient is large and
        /// recovers on its own; it takes error that is both past the onset and
        /// still growing. Limit proximity withdraws on its own, since a joint
        /// approaching its limit has no more room to lend whatever the trend.
        /// </summary>
        private float ComputePostureGuardScale()
        {
            if (!PostureGuardEnabled)
                return 1f;

            float excess = Mathf.InverseLerp(PostureGuardOnsetRad, PostureGuardFullRad, PostureErrorRad);
            float growth = Mathf.Clamp01(
                Mathf.Max(0f, PostureErrorRateRadPerS) / PostureGuardRateFullRadPerS);
            float postureScale = 1f - excess * growth;

            return Mathf.Clamp01(Mathf.Min(postureScale, ComputeLimitScale()));
        }

        /// <summary>
        /// The limit half of the guard.
        ///
        /// AbsoluteOccupancy asks how much of its authored range a joint is
        /// using. That question has a wrong answer for this movement: the
        /// accepted GAM-10 reference commands 111 deg of hip flexion against
        /// a 120 deg range, so a correctly executed deep squat reads 0.93 and
        /// trips a guard whose thresholds were set standing. Measured at
        /// s_q 0.55, 0.80 and 1.00 under both loads, the hip's composed target
        /// equals its nominal target exactly, so the quantity this rule keys
        /// on carries no information about the balance correction it is
        /// withdrawing (GAM11-5h13-d2-limit-margins.csv).
        ///
        /// UnexpectedMarginConsumption asks the question the guard was for:
        /// how much of the room the reference left has been taken by
        /// something the reference did not ask for. A pose that tracks its
        /// reference scores zero however deep the reference goes, and a joint
        /// driven onto a stop the reference did not command scores one. The
        /// denominator is the reference's own remaining margin, so there is
        /// no onset or saturation constant to calibrate and nothing to retune
        /// per phase: the scale comes from the reference at that phase.
        /// </summary>
        private float ComputeLimitScale()
        {
            switch (LimitSemantics)
            {
                case LimitGuardSemantics.Disabled:
                    return 1f;
                case LimitGuardSemantics.UnexpectedMarginConsumption:
                    return 1f - Mathf.Clamp01(UnexpectedMarginConsumedFraction);
                default:
                    return 1f - Mathf.InverseLerp(LimitGuardOnset, LimitGuardFull, PostureLimitProximity);
            }
        }

        /// <summary>
        /// Posture state from the previous post-physics observation. The
        /// controller never reads the physical rig itself; the adapter owns
        /// that and hands the summary in.
        /// </summary>
        public void ObservePosture(
            float postureErrorRad,
            float postureErrorRateRadPerS,
            float limitProximity,
            float unexpectedMarginConsumedFraction)
        {
            PostureErrorRad = float.IsFinite(postureErrorRad) ? Mathf.Max(0f, postureErrorRad) : 0f;
            PostureErrorRateRadPerS = float.IsFinite(postureErrorRateRadPerS) ? postureErrorRateRadPerS : 0f;
            PostureLimitProximity = float.IsFinite(limitProximity) ? Mathf.Clamp01(limitProximity) : 0f;
            UnexpectedMarginConsumedFraction = float.IsFinite(unexpectedMarginConsumedFraction)
                ? Mathf.Clamp01(unexpectedMarginConsumedFraction)
                : 0f;
        }

        private void SolveFrontal(SquatBalanceObserver balance, float comRefMl, float maxStep)
        {
            float error = balance.SystemCom.x - comRefMl;
            float velocity = balance.SystemComVelocity.x;
            float desiredAcceleration = -MlComKp * error - MlComKd * velocity;
            float heightOverGravity = balance.ComHeightM / SquatBalanceObserver.GravityMagnitudeMps2;
            float copDesired = balance.SystemCom.x - heightOverGravity * desiredAcceleration;

            float interiorMargin = Mathf.Min(
                SupportInteriorMarginM,
                Mathf.Max(0f, 0.4f * balance.SupportMlWidth));
            copDesired = Mathf.Clamp(
                copDesired,
                balance.SupportMlMin + interiorMargin,
                balance.SupportMlMax - interiorMargin);

            float lateralShift = copDesired - balance.SupportMlCenter;
            float ankleFrontal = lateralShift * 0.5f;
            AnkleFrontalOffsetRad = Step(AnkleFrontalOffsetRad, ankleFrontal, MaxAnkleFrontalOffsetRad, maxStep);
            HipFrontalOffsetRad = Step(HipFrontalOffsetRad, ankleFrontal * 0.5f, MaxHipFrontalOffsetRad, maxStep);
        }

        private void DecayToward(float dt)
        {
            float maxStep = MaxOffsetRateRadPerSecond * dt;
            AnkleSagittalOffsetRad = Step(AnkleSagittalOffsetRad, 0f, AnkleSagittalBoundRad, maxStep);
            HipSagittalOffsetRad = Step(HipSagittalOffsetRad, 0f, MaxHipSagittalOffsetRad, maxStep);
            TrunkSagittalOffsetRad = Step(TrunkSagittalOffsetRad, 0f, MaxTrunkSagittalOffsetRad, maxStep);
            AnkleFrontalOffsetRad = Step(AnkleFrontalOffsetRad, 0f, MaxAnkleFrontalOffsetRad, maxStep);
            HipFrontalOffsetRad = Step(HipFrontalOffsetRad, 0f, MaxHipFrontalOffsetRad, maxStep);
        }

        /// <summary>
        /// Bound first, then rate limit. There is no integrator in V1, so a
        /// standing offset is always the direct consequence of the current
        /// measured state.
        /// </summary>
        private static float Step(float current, float desired, float bound, float maxStep)
        {
            float bounded = Mathf.Clamp(desired, -bound, bound);
            return Mathf.Clamp(bounded, current - maxStep, current + maxStep);
        }
    }
}
