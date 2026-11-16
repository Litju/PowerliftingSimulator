using System;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Equipment;
using PowerliftingSimulator.Foundation;
using UnityEngine;

namespace PowerliftingSimulator.Deadlift.Unity
{
    public enum DeadliftPullPhase : byte
    {
        Grounded,
        Pulling,
        Separated,
        Aborted,
        /// <summary>GAM-70 Gate 6: the bar is airborne and the player's Drive advances the lift path.</summary>
        Lifting,
        /// <summary>Path end reached; the referee judges the finished position.</summary>
        Lockout,
        /// <summary>After the referee's Down signal: controlled lowering along the same path.</summary>
        Lowering,
        /// <summary>The bar rests on the platform again.</summary>
        Returned
    }

    /// <summary>
    /// GAM-70 Gate 3: the player's PULL intent as a bounded reference shaper.
    /// It does not command a bar position, a bar velocity or a force. It adds a
    /// bounded hip/knee/trunk extension reference on top of the qualified
    /// contact-consistent setup target; the physical hands, grip constraints and
    /// solver transmit that extension to the real bar.
    ///
    /// Sign mapping is the one already qualified in the A3 pretension helper:
    /// positive hip X is extension, negative knee X is extension. The trunk
    /// channel is disabled until its sign is measured; it is not guessed.
    ///
    /// Phase transition is measured, never timed: the phase becomes Separated
    /// only after both plate normal forces are below
    /// <see cref="SeparationPlateN"/> on consecutive ticks and the bar has
    /// positive vertical velocity. Releasing PULL before that returns the
    /// offsets to zero (Abort).
    /// </summary>
    public sealed class DeadliftPullController
    {
        public const float MaxHipExtensionDeg = 8f;
        public const float MaxKneeExtensionDeg = 8f;
        public const float MaxTrunkExtensionDeg = 0f;   // sign not yet measured; never guessed
        public const float SeparationPlateN = 2f;
        public const int SeparationConfirmTicks = 2;

        private readonly PhysicalAthleteRig _rig;
        private readonly PhysicalBarbell _bar;
        private readonly Func<float> _leftPlateN;
        private readonly Func<float> _rightPlateN;
        private int _separationTicks;
        private float _separatedApplied;
        private float _barYAtSeparation;

        public DeadliftPullController(PhysicalAthleteRig rig, PhysicalBarbell bar, Func<float> leftPlateN = null, Func<float> rightPlateN = null)
        {
            if (rig == null) throw new ArgumentNullException(nameof(rig));
            _rig = rig;
            _bar = bar ?? throw new ArgumentNullException(nameof(bar));
            _leftPlateN = leftPlateN;
            _rightPlateN = rightPlateN;
        }

        /// <summary>Test seam: replace the sampled player drive with a deterministic value.</summary>
        public float? IntentOverride01 { get; set; }

        /// <summary>Full-intent ramp rate (1/s). A full pull takes one second, not one tick.</summary>
        public float RampPerSecond { get; set; } = 1f;
        public bool RequireBraceAndGrip { get; set; } = true;

        /// <summary>Bounded extension per unit of applied intent (deg). Defaults are the declared game limits.</summary>
        public float HipExtensionLimitDeg { get; set; } = MaxHipExtensionDeg;
        public float KneeExtensionLimitDeg { get; set; } = MaxKneeExtensionDeg;
        public float TrunkExtensionLimitDeg { get; set; } = MaxTrunkExtensionDeg;

        /// <summary>
        /// Horizontal COM governor: the pull target is released when the measured
        /// whole-body COM moves more than this from its starting point, and is
        /// re-admitted once it returns inside the release band. This is a
        /// controller objective for the changed support mode, not a bar command.
        /// </summary>
        public float MaxComShiftM { get; set; } = 0.015f;
        public float GovernorReleaseM { get; set; } = 0.008f;
        public float ReleasePerSecond { get; set; } = 2f;
        public bool ComGovernorEnabled { get; set; } = true;
        public float ComShiftM { get; private set; }
        public bool GovernorActive { get; private set; }
        private Vector3 _com0;
        private bool _comEngaged;
        private bool _governorOpen = true;

        public DeadliftPullPhase Phase { get; private set; } = DeadliftPullPhase.Grounded;
        public float Intent01 { get; private set; }
        public float Applied01 { get; private set; }
        public float HipExtensionDeg => HipExtensionLimitDeg * Applied01;
        public float KneeExtensionDeg => KneeExtensionLimitDeg * Applied01;
        public float TrunkExtensionDeg => TrunkExtensionLimitDeg * Applied01;
        public float PlateLeftN { get; private set; } = float.NaN;
        public float PlateRightN { get; private set; } = float.NaN;
        public float BarVerticalVelocity { get; private set; }
        public float BarVerticalDisplacement { get; private set; }
        public ulong SeparationTick { get; private set; }
        public int PullTicks { get; private set; }
        public float PlateReferenceN { get; private set; }
        public float LoadFraction { get; private set; }
        /// <summary>Support-mode load schedule: 0 = grounded ply, 1 = full separated-bar boundary.</summary>
        public float LoadScheduleFraction { get; private set; }
        public float ScheduleRampPerSecond { get; set; } = 10f;

        /// <summary>
        /// GAM-70 Gate 6: moving phase. When enabled, the Separated cap is
        /// replaced by lift-path progress s in [0, 1], advanced at
        /// LiftRatePerSecond x Drive and held (never reversed) while the
        /// support observation reports a capture-point margin below
        /// MinSupportMarginM or the applied reference demands more than
        /// MaxReferenceDemand of a joint ceiling. Off by default: the Gate 3/4
        /// controls keep the qualified break behaviour.
        /// </summary>
        public bool LiftEnabled { get; set; }
        public float LiftRatePerSecond { get; set; } = 0.30f;
        public float LowerRatePerSecond { get; set; } = 0.30f;
        public Func<float> SupportMarginM { get; set; }
        public float MinSupportMarginM { get; set; } = 0.04f;
        /// <summary>Bound on the change of the progress rate (1/s^2): 0.6 reaches the full 0.3/s rate in 0.5 s.</summary>
        public float LiftAccelPerSecond2 { get; set; } = 0.6f;
        public float LiftRate01PerSecond { get; private set; }
        public Func<float> ReferenceDemand { get; set; }
        public float MaxReferenceDemand { get; set; } = 0.95f;
        public float LiftProgress01 { get; private set; }
        public Rigidbody BarBody => _bar.Body;
        public bool LiftHeld { get; private set; }
        public string LiftHoldReason { get; private set; } = string.Empty;
        public int LiftHeldTicks { get; private set; }
        public bool DownReceived { get; private set; }
        public ulong DownTick { get; private set; }
        /// <summary>Plate load (both plates) at which the lowered bar counts as resting on the platform.</summary>
        public float ReturnedPlateN { get; set; } = 600f;

        /// <summary>The chief referee's "Down" signal (IPF 4.3(2)); only accepted at lockout.</summary>
        public void CommandDown(ulong tick)
        {
            if (Phase != DeadliftPullPhase.Lockout || DownReceived)
                return;
            DownReceived = true;
            DownTick = tick;
            Phase = DeadliftPullPhase.Lowering;
        }

        private void UpdateMovingPhase(float dt)
        {
            if (Phase == DeadliftPullPhase.Separated)
            {
                Phase = DeadliftPullPhase.Lifting;
                _breakApplied = Applied01;
            }
            if (Phase == DeadliftPullPhase.Lifting)
            {
                float margin = SupportMarginM != null ? SupportMarginM() : float.PositiveInfinity;
                float demand = ReferenceDemand != null ? ReferenceDemand() : 0f;
                LiftHeld = false;
                LiftHoldReason = string.Empty;
                if (!(margin >= MinSupportMarginM)) { LiftHeld = true; LiftHoldReason = "support margin"; }
                else if (demand > MaxReferenceDemand) { LiftHeld = true; LiftHoldReason = "joint demand"; }
                if (LiftHeld)
                    LiftHeldTicks++;
                // Progress rate with bounded acceleration: Drive, a governor hold and
                // the arrival at lockout all change the joint-reference velocity at
                // most LiftAccelPerSecond2 (an instant stop of a moving reference
                // was measured to unload a foot within 0.4 s, Gate 6 run 6). The
                // arrival is a stopping-distance law, so s reaches 1 at zero rate.
                float wanted = LiftHeld ? 0f : LiftRatePerSecond * Intent01;
                wanted = Mathf.Min(wanted, Mathf.Sqrt(2f * LiftAccelPerSecond2 * Mathf.Max(0f, 1f - LiftProgress01)) + 0.01f);
                LiftRate01PerSecond = Mathf.MoveTowards(LiftRate01PerSecond, wanted, LiftAccelPerSecond2 * dt);
                LiftProgress01 = Mathf.Min(1f, LiftProgress01 + LiftRate01PerSecond * dt);
                if (LiftProgress01 >= 1f)
                {
                    LiftRate01PerSecond = 0f;
                    Phase = DeadliftPullPhase.Lockout;
                }
            }
            else if (Phase == DeadliftPullPhase.Lowering)
            {
                float wanted = -Mathf.Min(LowerRatePerSecond, Mathf.Sqrt(2f * LiftAccelPerSecond2 * Mathf.Max(0f, LiftProgress01)) + 0.02f);
                LiftRate01PerSecond = Mathf.MoveTowards(LiftRate01PerSecond, wanted, LiftAccelPerSecond2 * dt);
                LiftProgress01 = Mathf.Max(0f, LiftProgress01 + LiftRate01PerSecond * dt);
                // At the floor posture, unwind the bar-break extension too so the
                // plates take the bar back (the break extension is what lifted it).
                if (LiftProgress01 <= 0f)
                    _breakApplied = Mathf.MoveTowards(_breakApplied, 0f, ReleasePerSecond * 0.25f * dt);
                float plates = float.IsNaN(PlateLeftN) || float.IsNaN(PlateRightN) ? 0f : PlateLeftN + PlateRightN;
                if (LiftProgress01 <= 0f && plates >= ReturnedPlateN)
                    Phase = DeadliftPullPhase.Returned;
            }
            // The break extension fades out along the path: the path itself carries the posture.
            Applied01 = _breakApplied * (1f - LiftProgress01);
            BarVerticalDisplacement = _bar.Body.position.y - _barYAtSeparation;
        }

        private float _breakApplied;

        public void Update(PlayerIntentFrame intent)
        {
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
            Intent01 = Mathf.Clamp01(IntentOverride01 ?? intent.Drive01);
            bool gate = !RequireBraceAndGrip || (intent.Grip01 >= 0.5f && intent.Brace01 >= 0.5f);
            if (RequireBraceAndGrip && IntentOverride01.HasValue)
                gate = true;   // deterministic test seam still exercises the pull mapping

            PlateLeftN = _leftPlateN != null ? _leftPlateN() : float.NaN;
            PlateRightN = _rightPlateN != null ? _rightPlateN() : float.NaN;
            BarVerticalVelocity = _bar.Body.linearVelocity.y;
            if (!float.IsNaN(PlateLeftN) && !float.IsNaN(PlateRightN))
            {
                float sum = PlateLeftN + PlateRightN;
                LoadFraction = PlateReferenceN > 1f ? Mathf.Clamp01(1f - sum / PlateReferenceN) : 0f;
            }

            Vector3 com = Vector3.zero;
            float mass = 0f;
            foreach (PhysicalAthleteRig.SegmentRuntime segment in _rig.Segments.Values)
            {
                com += segment.Body.worldCenterOfMass * segment.Body.mass;
                mass += segment.Body.mass;
            }
            com /= Mathf.Max(mass, 0.0001f);
            if (!_comEngaged) { _com0 = com; _comEngaged = true; }
            ComShiftM = Vector2.Distance(new Vector2(com.x, com.z), new Vector2(_com0.x, _com0.z));
            if (ComGovernorEnabled)
            {
                if (ComShiftM > MaxComShiftM) _governorOpen = false;
                else if (ComShiftM < GovernorReleaseM) _governorOpen = true;
            }
            GovernorActive = ComGovernorEnabled && !_governorOpen;
            float governedTarget = Intent01 * (_governorOpen ? 1f : 0f);

            if (Phase >= DeadliftPullPhase.Lifting)
            {
                UpdateMovingPhase(dt);
                LoadScheduleFraction = 1f;
                return;
            }

            if (intent.AbortHeld && Phase != DeadliftPullPhase.Separated)
            {
                Phase = DeadliftPullPhase.Aborted;
                Intent01 = 0f;
            }

            if (Phase == DeadliftPullPhase.Grounded)
            {
                if (gate && Intent01 > 0.05f)
                {
                    Phase = DeadliftPullPhase.Pulling;
                    PullTicks = 0;
                    PlateReferenceN = float.IsNaN(PlateLeftN) || float.IsNaN(PlateRightN) ? 0f : PlateLeftN + PlateRightN;
                }
                else
                {
                    Applied01 = Mathf.MoveTowards(Applied01, 0f, RampPerSecond * dt);
                    return;
                }
            }

            if (Phase == DeadliftPullPhase.Pulling)
            {
                if (!gate || Intent01 <= 0.05f)
                {
                    Phase = DeadliftPullPhase.Aborted;
                    Intent01 = 0f;
                }
                else
                {
                    float rate = governedTarget < Applied01 ? ReleasePerSecond : RampPerSecond;
                    Applied01 = Mathf.MoveTowards(Applied01, governedTarget, rate * dt);
                    PullTicks++;
                }
            }

            if (Phase == DeadliftPullPhase.Aborted)
            {
                Applied01 = Mathf.MoveTowards(Applied01, 0f, RampPerSecond * dt);
                return;
            }

            if (Phase == DeadliftPullPhase.Pulling && SeparationObserved())
            {
                Phase = DeadliftPullPhase.Separated;
                SeparationTick = intent.Tick;
                _separatedApplied = Applied01;
            }
            else if (Phase == DeadliftPullPhase.Separated && LiftEnabled)
            {
                if (_barYAtSeparation == 0f)
                    _barYAtSeparation = _bar.Body.position.y;
                UpdateMovingPhase(dt);
                LoadScheduleFraction = 1f;
                return;
            }
            else if (Phase == DeadliftPullPhase.Separated)
            {
                // The reference that produced separation is the ceiling. The
                // COM governor may reduce it, but the controller never raises
                // the extension after separation and never commands bar position.
                float ceiling = Mathf.Min(_separatedApplied, governedTarget);
                float rate = ceiling < Applied01 ? ReleasePerSecond : RampPerSecond;
                Applied01 = Mathf.MoveTowards(Applied01, ceiling, rate * dt);
                BarVerticalDisplacement = _bar.Body.position.y - _barYAtSeparation;
            }
            if (SeparationTick != 0 && _barYAtSeparation == 0f)
                _barYAtSeparation = _bar.Body.position.y;
            LoadScheduleFraction = Phase == DeadliftPullPhase.Separated
                ? Mathf.MoveTowards(LoadScheduleFraction, 1f, ScheduleRampPerSecond * dt)
                : 0f;
        }

        private bool SeparationObserved()
        {
            if (float.IsNaN(PlateLeftN) || float.IsNaN(PlateRightN))
                return false;
            bool unloaded = PlateLeftN < SeparationPlateN && PlateRightN < SeparationPlateN;
            bool rising = BarVerticalVelocity > 0.005f;
            if (unloaded && rising)
                _separationTicks++;
            else
                _separationTicks = 0;
            return _separationTicks >= SeparationConfirmTicks;
        }
    }
}
