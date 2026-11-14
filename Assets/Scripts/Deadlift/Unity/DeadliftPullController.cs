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
        Aborted
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
