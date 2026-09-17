using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using PowerliftingSimulator.Foundation;

namespace PowerliftingSimulator.Squat
{
    public enum SquatRuleCommandKind : byte
    {
        SquatCommandIssued,
        RackCommandIssued,
        RerackStarted
    }

    public readonly struct SquatRuleCommandEvent
    {
        public SquatRuleCommandEvent(
            SquatRuleCommandKind kind,
            ulong simulationTick,
            double simulationTimeSeconds)
        {
            if (kind < SquatRuleCommandKind.SquatCommandIssued || kind > SquatRuleCommandKind.RerackStarted)
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (!IsFinite(simulationTimeSeconds) || simulationTimeSeconds < 0d)
                throw new ArgumentOutOfRangeException(nameof(simulationTimeSeconds));

            Kind = kind;
            SimulationTick = simulationTick;
            SimulationTimeSeconds = simulationTimeSeconds;
        }

        public SquatRuleCommandEvent(SquatRuleCommandKind kind, ulong simulationTick)
            : this(kind, simulationTick, SimulationConstants.TimeForTick(simulationTick))
        {
        }

        public SquatRuleCommandEvent(
            ulong simulationTick,
            double simulationTimeSeconds,
            SquatRuleCommandKind kind)
            : this(kind, simulationTick, simulationTimeSeconds)
        {
        }

        public static SquatRuleCommandEvent AtTick(
            SquatRuleCommandKind kind,
            ulong simulationTick) => new SquatRuleCommandEvent(kind, simulationTick);

        public SquatRuleCommandKind Kind { get; }
        public ulong SimulationTick { get; }
        public double SimulationTimeSeconds { get; }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    /// <summary>
    /// Immutable authoritative command events supplied by lift orchestration.
    /// The rule processor never derives referee signals from state or intent.
    /// </summary>
    public sealed class SquatRuleCommandTimeline
    {
        private readonly ReadOnlyCollection<SquatRuleCommandEvent> _events;

        public SquatRuleCommandTimeline(IReadOnlyList<SquatRuleCommandEvent> events)
        {
            if (events == null)
                throw new ArgumentNullException(nameof(events));
            if (events.Count == 0)
                throw new ArgumentException("A squat command timeline cannot be empty.", nameof(events));

            SquatRuleCommandEvent[] copy = new SquatRuleCommandEvent[events.Count];
            for (int index = 0; index < copy.Length; index++)
                copy[index] = events[index];

            Array.Sort(copy, CompareEvents);
            for (int index = 0; index < copy.Length; index++)
            {
                for (int prior = 0; prior < index; prior++)
                {
                    if (copy[prior].Kind == copy[index].Kind)
                        throw new ArgumentException("A squat command kind may occur only once per attempt.", nameof(events));
                }

                if (index > 0 && copy[index].SimulationTimeSeconds < copy[index - 1].SimulationTimeSeconds)
                    throw new ArgumentException("Command timestamps must be monotonic by simulation tick.", nameof(events));
            }

            _events = Array.AsReadOnly(copy);
        }

        public SquatRuleCommandTimeline(
            ulong squatCommandTick,
            ulong rackCommandTick,
            ulong rerackStartedTick)
            : this(new[]
            {
                new SquatRuleCommandEvent(SquatRuleCommandKind.SquatCommandIssued, squatCommandTick),
                new SquatRuleCommandEvent(SquatRuleCommandKind.RackCommandIssued, rackCommandTick),
                new SquatRuleCommandEvent(SquatRuleCommandKind.RerackStarted, rerackStartedTick)
            })
        {
        }

        public int Count => _events.Count;
        public IReadOnlyList<SquatRuleCommandEvent> Events => _events;

        public bool Has(SquatRuleCommandKind kind) => TryGet(kind, out _);

        public bool TryGet(SquatRuleCommandKind kind, out SquatRuleCommandEvent command)
        {
            for (int index = 0; index < _events.Count; index++)
            {
                if (_events[index].Kind == kind)
                {
                    command = _events[index];
                    return true;
                }
            }

            command = default(SquatRuleCommandEvent);
            return false;
        }

        public SquatRuleCommandEvent Get(SquatRuleCommandKind kind)
        {
            if (TryGet(kind, out SquatRuleCommandEvent command))
                return command;
            throw new InvalidOperationException("The requested command is not present in the squat timeline.");
        }

        private static int CompareEvents(SquatRuleCommandEvent left, SquatRuleCommandEvent right)
        {
            int tickComparison = left.SimulationTick.CompareTo(right.SimulationTick);
            return tickComparison != 0 ? tickComparison : left.Kind.CompareTo(right.Kind);
        }
    }

    public enum SquatRuleImplementationClass : byte
    {
        SOURCE_DIRECT,
        RULE_DERIVED_GAME_PROXY,
        GAME_SIMPLIFICATION
    }

    public enum SquatRuleMappingDisposition : byte
    {
        SOURCE_DIRECT_IMPLEMENTED,
        RULE_DERIVED_GAME_PROXY,
        GAME_SIMPLIFICATION,
        NOT_OBSERVABLE_V1,
        OUT_OF_SCOPE_V1
    }

    public readonly struct SquatRuleMapping
    {
        public SquatRuleMapping(
            string officialSection,
            string pdfLocator,
            string printedPageLocator,
            string semanticRequirement,
            string observability,
            SquatRuleMappingDisposition disposition,
            string implementationPredicate,
            SquatRuleImplementationClass sourceClassification,
            string simplification)
        {
            RequireText(officialSection, nameof(officialSection));
            RequireText(pdfLocator, nameof(pdfLocator));
            RequireText(printedPageLocator, nameof(printedPageLocator));
            RequireText(semanticRequirement, nameof(semanticRequirement));
            RequireText(observability, nameof(observability));
            RequireText(implementationPredicate, nameof(implementationPredicate));
            RequireText(simplification, nameof(simplification));

            OfficialSection = officialSection;
            PdfLocator = pdfLocator;
            PrintedPageLocator = printedPageLocator;
            SemanticRequirement = semanticRequirement;
            Observability = observability;
            Disposition = disposition;
            ImplementationPredicate = implementationPredicate;
            SourceClassification = sourceClassification;
            Simplification = simplification;
        }

        public string OfficialSection { get; }
        public string PdfLocator { get; }
        public string PrintedPageLocator { get; }
        public string SemanticRequirement { get; }
        public string Observability { get; }
        public SquatRuleMappingDisposition Disposition { get; }
        public string ImplementationPredicate { get; }
        public SquatRuleImplementationClass SourceClassification { get; }
        public string Simplification { get; }

        private static void RequireText(string value, string name)
        {
            if (string.IsNullOrEmpty(value))
                throw new ArgumentException("Rule mapping metadata is required.", name);
        }
    }

    public readonly struct SquatRuleToleranceDescriptor
    {
        public SquatRuleToleranceDescriptor(
            string name,
            string unit,
            float value,
            string source,
            string rationale,
            string version)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(unit) ||
                string.IsNullOrEmpty(source) || string.IsNullOrEmpty(rationale) ||
                string.IsNullOrEmpty(version))
                throw new ArgumentException("Rule tolerances require complete provenance metadata.");
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                throw new ArgumentOutOfRangeException(nameof(value));

            Name = name;
            Unit = unit;
            Value = value;
            Source = source;
            Rationale = rationale;
            Version = version;
        }

        public string Name { get; }
        public string Unit { get; }
        public float Value { get; }
        public string Source { get; }
        public string Rationale { get; }
        public string Version { get; }
    }

    public sealed class SquatRuleToleranceSet
    {
        public const string DefaultVersion = "GAM12_P2_RULE_TOLERANCES_V1";
        public const float DefaultKneeLockoutToleranceRad = 0.08726646f;
        public const float DefaultHipErectToleranceRad = 0.17453292f;
        public const float DefaultTrunkErectToleranceRad = 0.17453292f;
        public const float DefaultMotionlessBarVelocityMps = 0.02f;
        public const float DefaultMotionlessBarAngularVelocityRadS = 0.20f;
        public const float DefaultDescentOnsetVelocityMps = 0.02f;
        public const int DefaultDescentOnsetPersistenceTicks = 2;
        public const float DefaultAscentEstablishmentVelocityMps = 0.02f;
        public const int DefaultAscentEstablishmentPersistenceTicks = 3;
        public const float DefaultAscentEstablishmentDisplacementM = 0.02f;
        public const float DefaultDownwardMovementVelocityMps = 0.02f;
        public const float DefaultDownwardMovementToleranceM = 0.01f;
        public const int DefaultDownwardMovementPersistenceTicks = 2;
        public const float DefaultDoubleDescentVelocityMps = 0.02f;
        public const float DefaultDoubleDescentToleranceM = 0.01f;
        public const int DefaultDoubleDescentPersistenceTicks = 2;
        public const float DefaultSupportSlipToleranceM = 0.02f;
        public const float DefaultSupportSlipSpeedMps = 0.05f;
        public const int DefaultSupportViolationPersistenceTicks = 2;
        public const int DefaultStartPositionPersistenceTicks = 3;
        public const int DefaultFinalPositionPersistenceTicks = 3;

        private readonly ReadOnlyCollection<SquatRuleToleranceDescriptor> _descriptors;

        public SquatRuleToleranceSet(
            string version = DefaultVersion,
            float depthMarginM = SquatDepthGeometry.DefaultDepthMarginM,
            float kneeLockoutToleranceRad = DefaultKneeLockoutToleranceRad,
            float hipErectToleranceRad = DefaultHipErectToleranceRad,
            float trunkErectToleranceRad = DefaultTrunkErectToleranceRad,
            float motionlessBarVelocityMps = DefaultMotionlessBarVelocityMps,
            float motionlessBarAngularVelocityRadS = DefaultMotionlessBarAngularVelocityRadS,
            float descentOnsetVelocityMps = DefaultDescentOnsetVelocityMps,
            int descentOnsetPersistenceTicks = DefaultDescentOnsetPersistenceTicks,
            float ascentEstablishmentVelocityMps = DefaultAscentEstablishmentVelocityMps,
            int ascentEstablishmentPersistenceTicks = DefaultAscentEstablishmentPersistenceTicks,
            float ascentEstablishmentDisplacementM = DefaultAscentEstablishmentDisplacementM,
            float downwardMovementVelocityMps = DefaultDownwardMovementVelocityMps,
            float downwardMovementToleranceM = DefaultDownwardMovementToleranceM,
            int downwardMovementPersistenceTicks = DefaultDownwardMovementPersistenceTicks,
            float doubleDescentVelocityMps = DefaultDoubleDescentVelocityMps,
            float doubleDescentToleranceM = DefaultDoubleDescentToleranceM,
            int doubleDescentPersistenceTicks = DefaultDoubleDescentPersistenceTicks,
            float supportSlipToleranceM = DefaultSupportSlipToleranceM,
            float supportSlipSpeedMps = DefaultSupportSlipSpeedMps,
            int supportViolationPersistenceTicks = DefaultSupportViolationPersistenceTicks,
            int startPositionPersistenceTicks = DefaultStartPositionPersistenceTicks,
            int finalPositionPersistenceTicks = DefaultFinalPositionPersistenceTicks)
        {
            if (string.IsNullOrEmpty(version))
                throw new ArgumentException("Rule tolerance version is required.", nameof(version));

            RequireNonNegative(depthMarginM, nameof(depthMarginM));
            RequireNonNegative(kneeLockoutToleranceRad, nameof(kneeLockoutToleranceRad));
            RequireNonNegative(hipErectToleranceRad, nameof(hipErectToleranceRad));
            RequireNonNegative(trunkErectToleranceRad, nameof(trunkErectToleranceRad));
            RequirePositive(motionlessBarVelocityMps, nameof(motionlessBarVelocityMps));
            RequirePositive(motionlessBarAngularVelocityRadS, nameof(motionlessBarAngularVelocityRadS));
            RequirePositive(descentOnsetVelocityMps, nameof(descentOnsetVelocityMps));
            RequirePositive(descentOnsetPersistenceTicks, nameof(descentOnsetPersistenceTicks));
            RequirePositive(ascentEstablishmentVelocityMps, nameof(ascentEstablishmentVelocityMps));
            RequirePositive(ascentEstablishmentPersistenceTicks, nameof(ascentEstablishmentPersistenceTicks));
            RequireNonNegative(ascentEstablishmentDisplacementM, nameof(ascentEstablishmentDisplacementM));
            RequirePositive(downwardMovementVelocityMps, nameof(downwardMovementVelocityMps));
            RequirePositive(downwardMovementToleranceM, nameof(downwardMovementToleranceM));
            RequirePositive(downwardMovementPersistenceTicks, nameof(downwardMovementPersistenceTicks));
            RequirePositive(doubleDescentVelocityMps, nameof(doubleDescentVelocityMps));
            RequirePositive(doubleDescentToleranceM, nameof(doubleDescentToleranceM));
            RequirePositive(doubleDescentPersistenceTicks, nameof(doubleDescentPersistenceTicks));
            RequireNonNegative(supportSlipToleranceM, nameof(supportSlipToleranceM));
            RequirePositive(supportSlipSpeedMps, nameof(supportSlipSpeedMps));
            RequirePositive(supportViolationPersistenceTicks, nameof(supportViolationPersistenceTicks));
            RequirePositive(startPositionPersistenceTicks, nameof(startPositionPersistenceTicks));
            RequirePositive(finalPositionPersistenceTicks, nameof(finalPositionPersistenceTicks));

            Version = version;
            DepthMarginM = depthMarginM;
            KneeLockoutToleranceRad = kneeLockoutToleranceRad;
            HipErectToleranceRad = hipErectToleranceRad;
            TrunkErectToleranceRad = trunkErectToleranceRad;
            MotionlessBarVelocityMps = motionlessBarVelocityMps;
            MotionlessBarAngularVelocityRadS = motionlessBarAngularVelocityRadS;
            DescentOnsetVelocityMps = descentOnsetVelocityMps;
            DescentOnsetPersistenceTicks = descentOnsetPersistenceTicks;
            AscentEstablishmentVelocityMps = ascentEstablishmentVelocityMps;
            AscentEstablishmentPersistenceTicks = ascentEstablishmentPersistenceTicks;
            AscentEstablishmentDisplacementM = ascentEstablishmentDisplacementM;
            DownwardMovementVelocityMps = downwardMovementVelocityMps;
            DownwardMovementToleranceM = downwardMovementToleranceM;
            DownwardMovementPersistenceTicks = downwardMovementPersistenceTicks;
            DoubleDescentVelocityMps = doubleDescentVelocityMps;
            DoubleDescentToleranceM = doubleDescentToleranceM;
            DoubleDescentPersistenceTicks = doubleDescentPersistenceTicks;
            SupportSlipToleranceM = supportSlipToleranceM;
            SupportSlipSpeedMps = supportSlipSpeedMps;
            SupportViolationPersistenceTicks = supportViolationPersistenceTicks;
            StartPositionPersistenceTicks = startPositionPersistenceTicks;
            FinalPositionPersistenceTicks = finalPositionPersistenceTicks;

            _descriptors = Array.AsReadOnly(CreateDescriptors());
        }

        public static SquatRuleToleranceSet Default { get; } = new SquatRuleToleranceSet();

        public string Version { get; }
        public string ToleranceVersion => Version;
        public float DepthMarginM { get; }
        public float KneeLockoutToleranceRad { get; }
        public float KneeUnlockToleranceRad => KneeLockoutToleranceRad;
        public float HipErectToleranceRad { get; }
        public float TrunkErectToleranceRad { get; }
        public float MotionlessBarVelocityMps { get; }
        public float MotionlessBarAngularVelocityRadS { get; }
        public float DescentOnsetVelocityMps { get; }
        public int DescentOnsetPersistenceTicks { get; }
        public float AscentEstablishmentVelocityMps { get; }
        public int AscentEstablishmentPersistenceTicks { get; }
        public float AscentEstablishmentDisplacementM { get; }
        public float DownwardMovementVelocityMps { get; }
        public float DownwardMovementToleranceM { get; }
        public int DownwardMovementPersistenceTicks { get; }
        public float DoubleDescentVelocityMps { get; }
        public float DoubleDescentToleranceM { get; }
        public int DoubleDescentPersistenceTicks { get; }
        public float SupportSlipToleranceM { get; }
        public float SupportSlipSpeedMps { get; }
        public int SupportViolationPersistenceTicks { get; }
        public int StartPositionPersistenceTicks { get; }
        public int FinalPositionPersistenceTicks { get; }
        public IReadOnlyList<SquatRuleToleranceDescriptor> Descriptors => _descriptors;

        private SquatRuleToleranceDescriptor[] CreateDescriptors()
        {
            const string source = "GAME_CALIBRATION";
            return new[]
            {
                Descriptor("depth_margin", "m", DepthMarginM, source, "Reuse PSMS-SQ-11 and SquatDepthGeometry bilateral depth margin."),
                Descriptor("knee_lockout_tolerance", "rad", KneeLockoutToleranceRad, source, "Bounded proxy for a straight knee in the calibrated joint scalar."),
                Descriptor("hip_erect_tolerance", "rad", HipErectToleranceRad, source, "Bounded game proxy for an erect hip posture."),
                Descriptor("trunk_erect_tolerance", "rad", TrunkErectToleranceRad, source, "Bounded game proxy for an erect trunk posture."),
                Descriptor("motionless_bar_velocity", "m/s", MotionlessBarVelocityMps, source, "Solver-scale motion threshold for apparent stillness."),
                Descriptor("motionless_bar_angular_velocity", "rad/s", MotionlessBarAngularVelocityRadS, source, "Solver-scale angular stillness threshold."),
                Descriptor("descent_onset_velocity", "m/s", DescentOnsetVelocityMps, source, "Direct whole-bar downward-motion threshold for the separate physical-descent-motion proxy; it does not establish official attempt commencement."),
                Descriptor("descent_onset_persistence", "ticks", DescentOnsetPersistenceTicks, source, "Versioned observation persistence for the separate physical-descent-motion proxy."),
                Descriptor("ascent_establishment_velocity", "m/s", AscentEstablishmentVelocityMps, source, "Direct upward bar-velocity threshold for ascent establishment."),
                Descriptor("ascent_establishment_persistence", "ticks", AscentEstablishmentPersistenceTicks, source, "Consecutive direct upward samples required before ascent is established."),
                Descriptor("ascent_establishment_displacement", "m", AscentEstablishmentDisplacementM, source, "Minimum upward displacement from the observed first bottom."),
                Descriptor("downward_movement_velocity", "m/s", DownwardMovementVelocityMps, source, "Direct downward bar-velocity threshold after ascent establishment."),
                Descriptor("downward_movement_tolerance", "m", DownwardMovementToleranceM, source, "Rejects solver-scale displacement noise before a rule event."),
                Descriptor("downward_movement_persistence", "ticks", DownwardMovementPersistenceTicks, source, "Consecutive downward samples required after ascent."),
                Descriptor("double_descent_velocity", "m/s", DoubleDescentVelocityMps, source, "Direct downward bar-velocity threshold for a second bottom movement."),
                Descriptor("double_descent_tolerance", "m", DoubleDescentToleranceM, source, "Minimum second-descent displacement beyond the first reversal."),
                Descriptor("double_descent_persistence", "ticks", DoubleDescentPersistenceTicks, source, "Consecutive downward samples required before a second descent is recorded."),
                Descriptor("support_slip_tolerance", "m", SupportSlipToleranceM, source, "Existing 20 mm command-relative support/slip game proxy boundary after the Squat baseline."),
                Descriptor("support_slip_speed", "m/s", SupportSlipSpeedMps, source, "Support-slip speed proxy above allowed foot rocking tolerance."),
                Descriptor("support_violation_persistence", "ticks", SupportViolationPersistenceTicks, source, "Persistence for observed loss/slip support events."),
                Descriptor("start_position_persistence", "ticks", StartPositionPersistenceTicks, source, "Pre-command samples required for a stable start predicate."),
                Descriptor("final_position_persistence", "ticks", FinalPositionPersistenceTicks, source, "Pre-Rack samples required for a stable final predicate.")
            };
        }

        private SquatRuleToleranceDescriptor Descriptor(
            string name,
            string unit,
            float value,
            string source,
            string rationale) => new SquatRuleToleranceDescriptor(
                name,
                unit,
                value,
                source,
                rationale,
                Version);

        private static void RequireNonNegative(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                throw new ArgumentOutOfRangeException(name);
        }

        private static void RequirePositive(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
                throw new ArgumentOutOfRangeException(name);
        }

        private static void RequirePositive(int value, string name)
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(name);
        }
    }

    public sealed class SquatRuleSetMetadata
    {
        public const string DefaultRuleSetId = "IPF_2026_V3_SQUAT_GAME_V1";
        public const string DefaultOrganization = "International Powerlifting Federation";
        public const string DefaultSourceTitle = "IPF Technical Rule Book";
        public const string DefaultSourceVersion = "3";
        public const string DefaultEffectiveDate = "2026-03-01";
        public const string DefaultRetrievedDate = "2026-09-14";
        public const string DefaultImplementationVersion = "GAM12_P2A1_RULE_PROCESSOR_V1";
        public const string DefaultSimplificationVersion = "GAM12_P2_GAME_SIMPLIFICATIONS_V1";
        public const string DefaultToleranceVersion = SquatRuleToleranceSet.DefaultVersion;
        public const string DefaultPrimaryViolationPrecedenceVersion =
            "GAM12_P2_PRIMARY_PRECEDENCE_FIRST_ONSET_THEN_EXPLICIT_RANK_V1";
        public const string DefaultTemporalDiscretizationPolicy =
            "GAME_TEMPORAL_DISCRETIZATION_POLICY";
        public const string OfficialSourceUrl = "https://www.powerlifting.sport/rules/codes/info/technical-rules";
        public const string OfficialPdfUrl = "https://www.powerlifting.sport/fileadmin/ipf/data/rules/technical-rules/english/2026_IPF_Technical_Rulebook__effective_01_March_2026__v3.pdf";

        private readonly ReadOnlyCollection<SquatRuleMapping> _ruleMappings;

        public SquatRuleSetMetadata()
            : this(
                DefaultRuleSetId,
                DefaultOrganization,
                DefaultSourceTitle,
                DefaultSourceVersion,
                DefaultEffectiveDate,
                DefaultRetrievedDate,
                DefaultImplementationVersion,
                DefaultSimplificationVersion,
                DefaultToleranceVersion,
                OfficialSourceUrl,
                OfficialPdfUrl,
                "NOT_DOWNLOADED",
                CreateDefaultMappings())
        {
        }

        public SquatRuleSetMetadata(
            string ruleSetId,
            string governingOrganization,
            string sourceTitle,
            string sourceVersion,
            string effectiveDate,
            string retrievedDate,
            string implementationVersion,
            string gameSimplificationVersion,
            string ruleToleranceVersion,
            string sourceUrl,
            string pdfUrl,
            string pdfSha256,
            IReadOnlyList<SquatRuleMapping> ruleMappings)
        {
            RequireText(ruleSetId, nameof(ruleSetId));
            RequireText(governingOrganization, nameof(governingOrganization));
            RequireText(sourceTitle, nameof(sourceTitle));
            RequireText(sourceVersion, nameof(sourceVersion));
            RequireText(effectiveDate, nameof(effectiveDate));
            RequireText(retrievedDate, nameof(retrievedDate));
            RequireText(implementationVersion, nameof(implementationVersion));
            RequireText(gameSimplificationVersion, nameof(gameSimplificationVersion));
            RequireText(ruleToleranceVersion, nameof(ruleToleranceVersion));
            RequireText(sourceUrl, nameof(sourceUrl));
            RequireText(pdfUrl, nameof(pdfUrl));
            RequireText(pdfSha256, nameof(pdfSha256));
            if (ruleMappings == null || ruleMappings.Count == 0)
                throw new ArgumentException("At least one official rule mapping is required.", nameof(ruleMappings));

            RuleSetId = ruleSetId;
            GoverningOrganization = governingOrganization;
            SourceTitle = sourceTitle;
            SourceVersion = sourceVersion;
            EffectiveDate = effectiveDate;
            RetrievedDate = retrievedDate;
            ImplementationVersion = implementationVersion;
            GameSimplificationVersion = gameSimplificationVersion;
            RuleToleranceVersion = ruleToleranceVersion;
            SourceUrl = sourceUrl;
            PdfUrl = pdfUrl;
            PdfSha256 = pdfSha256;

            SquatRuleMapping[] mappings = new SquatRuleMapping[ruleMappings.Count];
            for (int index = 0; index < mappings.Length; index++)
                mappings[index] = ruleMappings[index];
            _ruleMappings = Array.AsReadOnly(mappings);
        }

        public static SquatRuleSetMetadata Default { get; } = new SquatRuleSetMetadata();

        public string RuleSetId { get; }
        public string GoverningOrganization { get; }
        public string SourceTitle { get; }
        public string SourceVersion { get; }
        public string EffectiveDate { get; }
        public string RetrievedDate { get; }
        public string ImplementationVersion { get; }
        public string GameSimplificationVersion { get; }
        public string RuleToleranceVersion { get; }
        public string SourceUrl { get; }
        public string PdfUrl { get; }
        public string PdfSha256 { get; }
        public string PrimaryViolationPrecedenceVersion => DefaultPrimaryViolationPrecedenceVersion;
        public string TemporalDiscretizationPolicy => DefaultTemporalDiscretizationPolicy;
        public string RulebookVersion => SourceVersion;
        public string RulebookEffectiveDate => EffectiveDate;
        public int RuleMappingCount => _ruleMappings.Count;
        public IReadOnlyList<SquatRuleMapping> RuleMappings => _ruleMappings;

        public SquatRuleMapping RuleMappingAt(int index)
        {
            if (index < 0 || index >= _ruleMappings.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _ruleMappings[index];
        }

        private static SquatRuleMapping[] CreateDefaultMappings()
        {
            const string pdfPerformance = "Official PDF p.20 (web PDF index 19)";
            const string pdfDisqualification = "Official PDF p.21 (web PDF index 20)";
            const string printedPerformance = "Printed page 20";
            const string printedDisqualification = "Printed page 21";
            return new[]
            {
                new SquatRuleMapping(
                    "4.1(1)", pdfPerformance, printedPerformance,
                    "Bar is across the shoulders and hands, thumbs, and fingers remain in contact.",
                    "No trustworthy bar-placement or hand-contact channels exist in P1.",
                    SquatRuleMappingDisposition.NOT_OBSERVABLE_V1,
                    "No V1 predicate; preserve as explicit unavailable evidence.",
                    SquatRuleImplementationClass.SOURCE_DIRECT,
                    "Bar placement and hand contact are not fabricated from the saddle or intent."),
                new SquatRuleMapping(
                    "4.1(2)", pdfPerformance, printedPerformance,
                    "After unrack the lifter establishes the start, is motionless and erect with knees locked, then receives Squat.",
                    "Bilateral calibrated joints, bar motion, support, and explicit Squat command are observable.",
                    SquatRuleMappingDisposition.RULE_DERIVED_GAME_PROXY,
                    "Pre-command persistence window checks bilateral knee/hip/trunk posture, bar stillness, feet, and support.",
                    SquatRuleImplementationClass.RULE_DERIVED_GAME_PROXY,
                    "Exact referee-visible posture and backward walkout are represented by bounded physical proxies."),
                new SquatRuleMapping(
                    "4.1(2)", pdfPerformance, printedPerformance,
                    "The lifter moves backward after removing the bar to establish the start.",
                    "P1 has support/contact and slip summaries, not a complete referee-visible foot trajectory.",
                    SquatRuleMappingDisposition.GAME_SIMPLIFICATION,
                    "No separate backward-walk predicate; support is checked through the selected V1 proxy.",
                    SquatRuleImplementationClass.GAME_SIMPLIFICATION,
                    "Walkout direction is not claimed from contact counts or slip alone."),
                new SquatRuleMapping(
                    "4.1(2)", pdfPerformance, printedPerformance,
                    "If the lifter is not ready after the rulebook safety interval, the referee may request Replace.",
                    "P1 has no authoritative Replace command or referee-timer event.",
                    SquatRuleMappingDisposition.OUT_OF_SCOPE_V1,
                    "No V1 predicate; safety command lifecycle is outside the rule processor.",
                    SquatRuleImplementationClass.SOURCE_DIRECT,
                    "P2 does not invent a safety intervention or timer."),
                new SquatRuleMapping(
                    "4.1(2) / 4.1.1(1)", pdfPerformance + "; " + pdfDisqualification,
                    printedPerformance + "; " + printedDisqualification,
                    "The Squat signal must precede commencement and the Rack signal must precede reracking.",
                    "Explicit command timeline and physical onset/rerack events are supplied as value data.",
                    SquatRuleMappingDisposition.RULE_DERIVED_GAME_PROXY,
                    "Knee-unlock commencement before Squat records EARLY_DESCENT; rerack before Rack records EARLY_RACK.",
                    SquatRuleImplementationClass.RULE_DERIVED_GAME_PROXY,
                    "Command event provenance is explicit; state and input are not substituted. Same-tick commencement is not early under the GAME_TEMPORAL_DISCRETIZATION_POLICY."),
                new SquatRuleMapping(
                    "4.1(3) / 4.1.1(5)", pdfPerformance + "; " + pdfDisqualification,
                    printedPerformance + "; " + printedDisqualification,
                    "The hip-joint leg surface descends below the corresponding knee; only one descent is allowed.",
                    "Raw bilateral hip-crease and knee-top landmark Y values are captured by P1.",
                    SquatRuleMappingDisposition.RULE_DERIVED_GAME_PROXY,
                    "Both raw bilateral landmark differences must be at or below the versioned depth margin at a post-command sample.",
                    SquatRuleImplementationClass.RULE_DERIVED_GAME_PROXY,
                    "Calibrated landmarks are proxies for the official anatomical surfaces."),
                new SquatRuleMapping(
                    "4.1(3) / 4.1.1(2)", pdfPerformance + "; " + pdfDisqualification,
                    printedPerformance + "; " + printedDisqualification,
                    "The attempt commences at knee unlocking; double bouncing or a second descent is prohibited.",
                    "Bilateral calibrated knee scalars and raw bar position/velocity are available.",
                    SquatRuleMappingDisposition.RULE_DERIVED_GAME_PROXY,
                    "Attempt commencement is the first loss of the bilateral locked-knee proxy; physical bar descent remains a separate motion-analysis signal. A persistent direct bar reversal before ascent establishment records DOUBLE_DESCENT.",
                    SquatRuleImplementationClass.RULE_DERIVED_GAME_PROXY,
                    "Knee lockout uses the versioned calibrated scalar tolerance; named velocity, displacement, and persistence thresholds reject solver-scale motion noise."),
                new SquatRuleMapping(
                    "4.1(4) / 4.1.1(2) / 4.1.1(3)", pdfPerformance + "; " + pdfDisqualification,
                    printedPerformance + "; " + printedDisqualification,
                    "The lifter recovers erect with knees locked and may not make downward movement during ascent.",
                    "Raw whole-bar Y position/velocity and final bilateral physical posture are observable.",
                    SquatRuleMappingDisposition.RULE_DERIVED_GAME_PROXY,
                    "A persistent whole-bar downward displacement after direct ascent establishment records DOWNWARD_MOVEMENT; final posture records FAILED_LOCKOUT.",
                    SquatRuleImplementationClass.RULE_DERIVED_GAME_PROXY,
                    "Apparent stillness and erectness use named game tolerances."),
                new SquatRuleMapping(
                    "4.1(5)", pdfPerformance, printedPerformance,
                    "Rack is issued from the apparent final position; the lifter returns the bar to the racks.",
                    "Rack command and explicit RerackStarted event are observable; exact rack geometry is not.",
                    SquatRuleMappingDisposition.RULE_DERIVED_GAME_PROXY,
                    "The timeline compares RerackStarted with RackCommandIssued; no rack transform is read.",
                    SquatRuleImplementationClass.RULE_DERIVED_GAME_PROXY,
                    "Rerack completion and exact rack contact remain outside this processor."),
                new SquatRuleMapping(
                    "4.1(5) / 4.1.1(4)", pdfPerformance + "; " + pdfDisqualification,
                    printedPerformance + "; " + printedDisqualification,
                    "Foot stepping backward, forward, or laterally is disallowed; rocking between ball and heel is permitted. Movement after Rack is allowed.",
                    "P1 provides foot contact and slip summaries, not exact bilateral foot trajectories.",
                    SquatRuleMappingDisposition.GAME_SIMPLIFICATION,
                    "From SquatCommandIssued up to RackCommandIssued, persistent support loss or command-relative slip delta beyond the named proxy tolerance records SUPPORT_VIOLATION; post-Rack samples are ignored.",
                    SquatRuleImplementationClass.GAME_SIMPLIFICATION,
                    "The proxy does not call all observed slip stepping or distinguish every referee-visible foot motion. Cumulative P1 slip is baseline-subtracted at Squat."),
                new SquatRuleMapping(
                    "4.1(5)", pdfPerformance, printedPerformance,
                    "The lifter stays with the bar while reracking and may not exit through the front of the rack.",
                    "No reliable bar/athlete path or rack-exit event is present in P1.",
                    SquatRuleMappingDisposition.NOT_OBSERVABLE_V1,
                    "No V1 predicate; preserve as explicit unavailable evidence.",
                    SquatRuleImplementationClass.SOURCE_DIRECT,
                    "The command timeline does not invent a spatial rack-exit sensor."),
                new SquatRuleMapping(
                    "4.1(6)", pdfPerformance, printedPerformance,
                    "Competition platform spotter/loader count is bounded by the rulebook.",
                    "This platform-operations rule is not an athlete attempt observation.",
                    SquatRuleMappingDisposition.OUT_OF_SCOPE_V1,
                    "No attempt legality predicate in the squat rule processor.",
                    SquatRuleImplementationClass.SOURCE_DIRECT,
                    "Meet operations are outside P2 domain scope."),
                new SquatRuleMapping(
                    "4.1.1(6)", pdfDisqualification, printedDisqualification,
                    "Spotter/loader contact must not make the lift easier between Chief Referee signals.",
                    "P1 has no trusted spotter-contact channel.",
                    SquatRuleMappingDisposition.NOT_OBSERVABLE_V1,
                    "No V1 predicate; preserve as explicit unavailable evidence.",
                    SquatRuleImplementationClass.SOURCE_DIRECT,
                    "Saddle state is not treated as spotter contact."),
                new SquatRuleMapping(
                    "4.1.1(7)", pdfDisqualification, printedDisqualification,
                    "Elbow or upper-arm contact with the legs is disallowed when supportive.",
                    "P1 has no validated upper-arm/leg support-contact event for referee interpretation.",
                    SquatRuleMappingDisposition.NOT_OBSERVABLE_V1,
                    "No V1 predicate; preserve as explicit unavailable evidence.",
                    SquatRuleImplementationClass.SOURCE_DIRECT,
                    "Joint kinematics do not establish contact or assistance."),
                new SquatRuleMapping(
                    "4.1.1(8)", pdfDisqualification, printedDisqualification,
                    "Dropping or dumping the bar after completion is disallowed.",
                    "P1 bar pose/velocity does not include a validated completion/drop event or rack geometry.",
                    SquatRuleMappingDisposition.NOT_OBSERVABLE_V1,
                    "No V1 predicate; preserve as explicit unavailable evidence.",
                    SquatRuleImplementationClass.SOURCE_DIRECT,
                    "A raw bar trajectory is not relabeled as a referee-visible dump."),
                new SquatRuleMapping(
                    "4.1.1(9)", pdfDisqualification, printedDisqualification,
                    "Failure to comply with any Rules of Performance item is disqualifying.",
                    "Mapped predicates cover the observable subset; unobservable items remain explicit above.",
                    SquatRuleMappingDisposition.OUT_OF_SCOPE_V1,
                    "No catch-all sensor; outcome is the conjunction of implemented predicates and evidence validity.",
                    SquatRuleImplementationClass.SOURCE_DIRECT,
                    "The umbrella clause does not silently expand V1 observability."),
                new SquatRuleMapping(
                    "4.1(1) / 4.1(2) / 4.1(3) / 4.1(4) / 4.1(5)",
                    pdfPerformance,
                    printedPerformance,
                    "Rules of Performance require the complete squat sequence and lawful commands.",
                    "The value-only command timeline and raw P1 trace represent the observable sequence boundary.",
                    SquatRuleMappingDisposition.SOURCE_DIRECT_IMPLEMENTED,
                    "SquatRuleProcessor evaluates only explicit command events and copied physical observations.",
                    SquatRuleImplementationClass.SOURCE_DIRECT,
                    "The processor is canonical and deterministic; presentation lights are not implemented here.")
            };
        }

        private static void RequireText(string value, string name)
        {
            if (string.IsNullOrEmpty(value))
                throw new ArgumentException("Ruleset metadata is required.", name);
        }
    }

    public enum SquatRuleViolationKind : byte
    {
        NONE,
        EARLY_DESCENT,
        INSUFFICIENT_DEPTH,
        DOUBLE_DESCENT,
        DOWNWARD_MOVEMENT,
        FAILED_START_POSITION,
        FAILED_LOCKOUT,
        EARLY_RACK,
        SUPPORT_VIOLATION
    }

    [Flags]
    public enum SquatRuleEvidenceChannel : uint
    {
        NONE = 0,
        COMMAND_TIMELINE = 1u << 0,
        BAR_POSITION = 1u << 1,
        BAR_LINEAR_VELOCITY = 1u << 2,
        BAR_ANGULAR_VELOCITY = 1u << 3,
        DEPTH_LANDMARKS = 1u << 4,
        KNEE_JOINTS = 1u << 5,
        HIP_JOINTS = 1u << 6,
        TRUNK_JOINTS = 1u << 7,
        SUPPORT_CONTACT = 1u << 8,
        FOOT_CONTACT = 1u << 9,
        FOOT_SLIP = 1u << 10
    }

    public readonly struct SquatRuleViolationRecord
    {
        public SquatRuleViolationRecord(
            SquatRuleViolationKind kind,
            ulong onsetTick,
            string ruleSetId,
            string ruleImplementationVersion,
            SquatRuleImplementationClass sourceClassification,
            SquatRuleEvidenceChannel evidenceChannels,
            float measuredValueA,
            float measuredValueB,
            float measuredValueC,
            string toleranceVersion)
        {
            if (kind == SquatRuleViolationKind.NONE)
                throw new ArgumentOutOfRangeException(nameof(kind));
            RequireText(ruleSetId, nameof(ruleSetId));
            RequireText(ruleImplementationVersion, nameof(ruleImplementationVersion));
            RequireText(toleranceVersion, nameof(toleranceVersion));
            RequireMeasuredValue(measuredValueA, nameof(measuredValueA));
            RequireMeasuredValue(measuredValueB, nameof(measuredValueB));
            RequireMeasuredValue(measuredValueC, nameof(measuredValueC));

            Kind = kind;
            OnsetTick = onsetTick;
            RuleSetId = ruleSetId;
            RuleImplementationVersion = ruleImplementationVersion;
            SourceClassification = sourceClassification;
            EvidenceChannels = evidenceChannels;
            MeasuredValueA = measuredValueA;
            MeasuredValueB = measuredValueB;
            MeasuredValueC = measuredValueC;
            ToleranceVersion = toleranceVersion;
        }

        public SquatRuleViolationKind Kind { get; }
        public ulong OnsetTick { get; }
        public ulong FirstTick => OnsetTick;
        public string RuleSetId { get; }
        public string RuleImplementationVersion { get; }
        public SquatRuleImplementationClass SourceClassification { get; }
        public SquatRuleImplementationClass ImplementationSource => SourceClassification;
        public SquatRuleEvidenceChannel EvidenceChannels { get; }
        public float MeasuredValueA { get; }
        public float MeasuredValueB { get; }
        public float MeasuredValueC { get; }
        public string ToleranceVersion { get; }

        private static void RequireText(string value, string name)
        {
            if (string.IsNullOrEmpty(value))
                throw new ArgumentException("Violation provenance is required.", name);
        }

        private static void RequireMeasuredValue(float value, string name)
        {
            if (float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(name);
        }
    }

    public enum SquatJudgmentEvidenceStatus : byte
    {
        EVALUABLE,
        INCOMPLETE_ATTEMPT,
        INSUFFICIENT_EVIDENCE,
        INVALID_TRACE
    }

    public enum SquatJudgmentOutcome : byte
    {
        UNDETERMINED,
        GOOD_LIFT,
        NO_LIFT
    }

    public enum SquatPhysicalCompletionStatus : byte
    {
        UNKNOWN,
        INCOMPLETE,
        COMPLETED
    }

    public readonly struct SquatRuleEventTicks
    {
        public const ulong NotAvailable = ulong.MaxValue;

        public SquatRuleEventTicks(
            ulong squatCommandTick,
            ulong descentOnsetTick,
            ulong bottomTick,
            ulong ascentEstablishmentTick,
            ulong lockoutTick,
            ulong rackCommandTick,
            ulong rerackStartedTick)
        {
            SquatCommandTick = squatCommandTick;
            DescentOnsetTick = descentOnsetTick;
            BottomTick = bottomTick;
            AscentEstablishmentTick = ascentEstablishmentTick;
            LockoutTick = lockoutTick;
            RackCommandTick = rackCommandTick;
            RerackStartedTick = rerackStartedTick;
        }

        public ulong SquatCommandTick { get; }
        public ulong DescentOnsetTick { get; }
        public ulong BottomTick { get; }
        public ulong AscentEstablishmentTick { get; }
        public ulong LockoutTick { get; }
        public ulong RackCommandTick { get; }
        public ulong RerackStartedTick { get; }
    }

    public sealed class SquatAttemptJudgment
    {
        private readonly ReadOnlyCollection<SquatRuleViolationRecord> _violations;
        private readonly ReadOnlyCollection<SquatRuleViolationRecord> _secondaryViolations;

        internal SquatAttemptJudgment(
            SquatJudgmentEvidenceStatus evidenceStatus,
            SquatJudgmentOutcome outcome,
            SquatPhysicalCompletionStatus physicalCompletion,
            SquatRuleSetMetadata ruleSet,
            SquatRuleToleranceSet toleranceSet,
            string evaluatedTraceSchema,
            SquatRuleEventTicks eventTicks,
            IReadOnlyList<SquatRuleViolationRecord> violations)
        {
            if (ruleSet == null)
                throw new ArgumentNullException(nameof(ruleSet));
            if (toleranceSet == null)
                throw new ArgumentNullException(nameof(toleranceSet));
            if (violations == null)
                throw new ArgumentNullException(nameof(violations));

            SquatRuleViolationRecord[] ordered = new SquatRuleViolationRecord[violations.Count];
            for (int index = 0; index < ordered.Length; index++)
                ordered[index] = violations[index];
            Array.Sort(ordered, CompareViolations);

            EvidenceStatus = evidenceStatus;
            Outcome = outcome;
            PhysicalCompletion = physicalCompletion;
            RuleSet = ruleSet;
            RuleImplementationVersion = ruleSet.ImplementationVersion;
            ToleranceVersion = toleranceSet.Version;
            EvaluatedTraceSchema = evaluatedTraceSchema ?? string.Empty;
            EventTicks = eventTicks;
            _violations = Array.AsReadOnly(ordered);

            SquatRuleViolationRecord[] secondary = new SquatRuleViolationRecord[Math.Max(0, ordered.Length - 1)];
            if (secondary.Length > 0)
                Array.Copy(ordered, 1, secondary, 0, secondary.Length);
            _secondaryViolations = Array.AsReadOnly(secondary);
        }

        public SquatJudgmentEvidenceStatus EvidenceStatus { get; }
        public SquatJudgmentEvidenceStatus Status => EvidenceStatus;
        public SquatJudgmentOutcome Outcome { get; }
        public SquatJudgmentOutcome Result => Outcome;
        public SquatJudgmentOutcome Decision => Outcome;
        public SquatPhysicalCompletionStatus PhysicalCompletion { get; }
        public bool HasDecision => EvidenceStatus == SquatJudgmentEvidenceStatus.EVALUABLE &&
            Outcome != SquatJudgmentOutcome.UNDETERMINED;
        public bool IsGoodLift => Outcome == SquatJudgmentOutcome.GOOD_LIFT;
        public bool IsNoLift => Outcome == SquatJudgmentOutcome.NO_LIFT;
        public SquatRuleSetMetadata RuleSet { get; }
        public string RuleSetId => RuleSet.RuleSetId;
        public string RuleImplementationVersion { get; }
        public string ToleranceVersion { get; }
        public string PrimaryViolationPrecedenceVersion =>
            RuleSet.PrimaryViolationPrecedenceVersion;
        public string EvaluatedTraceSchema { get; }
        public SquatRuleEventTicks EventTicks { get; }
        public int ViolationCount => _violations.Count;
        public IReadOnlyList<SquatRuleViolationRecord> Violations => _violations;
        public IReadOnlyList<SquatRuleViolationRecord> SecondaryViolations => _secondaryViolations;
        public bool HasPrimaryViolation => _violations.Count > 0;
        public SquatRuleViolationRecord PrimaryViolation => _violations.Count > 0
            ? _violations[0]
            : default(SquatRuleViolationRecord);
        public SquatRuleViolationKind PrimaryViolationKind => _violations.Count > 0
            ? _violations[0].Kind
            : SquatRuleViolationKind.NONE;

        public SquatRuleViolationRecord ViolationAt(int index)
        {
            if (index < 0 || index >= _violations.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _violations[index];
        }

        private static int CompareViolations(
            SquatRuleViolationRecord left,
            SquatRuleViolationRecord right)
        {
            int tickComparison = left.OnsetTick.CompareTo(right.OnsetTick);
            if (tickComparison != 0)
                return tickComparison;

            int rankComparison = PrecedenceRank(left.Kind).CompareTo(PrecedenceRank(right.Kind));
            return rankComparison != 0 ? rankComparison : left.Kind.CompareTo(right.Kind);
        }

        private static int PrecedenceRank(SquatRuleViolationKind kind)
        {
            switch (kind)
            {
                case SquatRuleViolationKind.FAILED_START_POSITION: return 10;
                case SquatRuleViolationKind.EARLY_DESCENT: return 20;
                case SquatRuleViolationKind.DOUBLE_DESCENT: return 30;
                case SquatRuleViolationKind.DOWNWARD_MOVEMENT: return 40;
                case SquatRuleViolationKind.INSUFFICIENT_DEPTH: return 50;
                case SquatRuleViolationKind.FAILED_LOCKOUT: return 60;
                case SquatRuleViolationKind.EARLY_RACK: return 70;
                case SquatRuleViolationKind.SUPPORT_VIOLATION: return 80;
                default: return int.MaxValue;
            }
        }
    }

    /// <summary>
    /// Post-attempt, value-only squat competition-rule evaluation. It does not
    /// own physics, infer commands, or classify physical failure causes.
    /// </summary>
    public sealed class SquatRuleProcessor
    {
        private readonly SquatRuleSetMetadata _ruleSet;
        private readonly SquatRuleToleranceSet _tolerances;

        public SquatRuleProcessor(
            SquatRuleSetMetadata ruleSet = null,
            SquatRuleToleranceSet toleranceSet = null)
        {
            _ruleSet = ruleSet ?? SquatRuleSetMetadata.Default;
            _tolerances = toleranceSet ?? SquatRuleToleranceSet.Default;
            if (!string.Equals(_ruleSet.RuleToleranceVersion, _tolerances.Version, StringComparison.Ordinal))
                throw new ArgumentException("The ruleset and tolerance versions must agree.", nameof(toleranceSet));
        }

        public SquatRuleSetMetadata RuleSet => _ruleSet;
        public SquatRuleToleranceSet ToleranceSet => _tolerances;

        public SquatAttemptJudgment Evaluate(
            SquatTrace trace,
            SquatRuleCommandTimeline commandTimeline)
        {
            if (trace == null)
                throw new ArgumentNullException(nameof(trace));
            if (commandTimeline == null)
                throw new ArgumentNullException(nameof(commandTimeline));

            if (!trace.IsFrozen || trace.IsRecording || trace.Count == 0 ||
                !string.Equals(trace.Schema, SquatTrace.SchemaVersion, StringComparison.Ordinal))
            {
                return InvalidJudgment(trace);
            }

            if (!HasCanonicalFixedStep(trace))
                return InvalidJudgment(trace);

            if (!commandTimeline.TryGet(SquatRuleCommandKind.SquatCommandIssued, out SquatRuleCommandEvent squatCommand) ||
                !commandTimeline.TryGet(SquatRuleCommandKind.RackCommandIssued, out SquatRuleCommandEvent rackCommand) ||
                !commandTimeline.TryGet(SquatRuleCommandKind.RerackStarted, out SquatRuleCommandEvent rerackStarted))
            {
                return IncompleteJudgment(trace);
            }

            if (rackCommand.SimulationTick <= squatCommand.SimulationTick)
                return IncompleteJudgment(trace, squatCommand, rackCommand, rerackStarted);

            // A rerack at the Squat tick has no observable sub-tick ordering;
            // both it and any rerack before Squat are malformed lifecycle evidence.
            if (rerackStarted.SimulationTick <= squatCommand.SimulationTick)
                return IncompleteJudgment(trace, squatCommand, rackCommand, rerackStarted);

            if (!HasRuleWindowCoverage(
                    trace,
                    squatCommand,
                    rackCommand,
                    rerackStarted,
                    out int squatCommandIndex,
                    out int rackCommandIndex,
                    out int rerackStartedIndex))
                return IncompleteJudgment(trace, squatCommand, rackCommand, rerackStarted);

            if (!HasCommandEventTimeCoherence(trace[squatCommandIndex], squatCommand) ||
                !HasCommandEventTimeCoherence(trace[rackCommandIndex], rackCommand) ||
                !HasCommandEventTimeCoherence(trace[rerackStartedIndex], rerackStarted))
                return IncompleteJudgment(trace, squatCommand, rackCommand, rerackStarted);

            int preRackEnd = rackCommandIndex - 1;

            if (!HasMandatoryEvidence(trace, preRackEnd))
                return InsufficientEvidenceJudgment(trace, squatCommand, rackCommand, rerackStarted);

            int startEnd = squatCommandIndex - 1;
            if (startEnd < 0 || !HasConsecutiveWindow(trace, startEnd - _tolerances.StartPositionPersistenceTicks + 1, startEnd))
                return IncompleteJudgment(trace, squatCommand, rackCommand, rerackStarted);

            List<SquatRuleViolationRecord> violations = new List<SquatRuleViolationRecord>(8);
            int startBegin = startEnd - _tolerances.StartPositionPersistenceTicks + 1;
            int firstInvalidStart = FirstInvalidStartIndex(trace, startBegin, startEnd);
            if (firstInvalidStart >= 0)
            {
                SquatObservationSnapshot invalidStart = trace[firstInvalidStart];
                AddViolation(
                    violations,
                    SquatRuleViolationKind.FAILED_START_POSITION,
                    invalidStart.SimulationTick,
                    SquatRuleImplementationClass.RULE_DERIVED_GAME_PROXY,
                    SquatRuleEvidenceChannel.BAR_LINEAR_VELOCITY |
                    SquatRuleEvidenceChannel.BAR_ANGULAR_VELOCITY |
                    SquatRuleEvidenceChannel.KNEE_JOINTS |
                    SquatRuleEvidenceChannel.HIP_JOINTS |
                    SquatRuleEvidenceChannel.TRUNK_JOINTS |
                    SquatRuleEvidenceChannel.SUPPORT_CONTACT |
                    SquatRuleEvidenceChannel.FOOT_CONTACT,
                    MaxKneeAngle(invalidStart),
                    MaxHipAngle(invalidStart),
                    MaxTrunkAngle(invalidStart));
            }

            int attemptCommencement = FindAttemptCommencementFromKnees(trace, startBegin, preRackEnd);
            int physicalOnset = FindFirstPhysicalDescentMotionOnset(trace, 0, preRackEnd);
            int postCommandOnset = FindFirstPhysicalDescentMotionOnsetAtOrAfter(
                trace,
                squatCommand.SimulationTick,
                preRackEnd);
            if (attemptCommencement < 0 || physicalOnset < 0)
                return IncompleteJudgment(trace, squatCommand, rackCommand, rerackStarted, violations);

            if (trace[attemptCommencement].SimulationTick < squatCommand.SimulationTick)
            {
                SquatObservationSnapshot onset = trace[attemptCommencement];
                AddViolation(
                    violations,
                    SquatRuleViolationKind.EARLY_DESCENT,
                    onset.SimulationTick,
                    SquatRuleImplementationClass.RULE_DERIVED_GAME_PROXY,
                    SquatRuleEvidenceChannel.COMMAND_TIMELINE |
                    SquatRuleEvidenceChannel.KNEE_JOINTS,
                    onset.Joints.LeftKnee.ActualAngleRadians,
                    onset.Joints.RightKnee.ActualAngleRadians,
                    _tolerances.KneeUnlockToleranceRad);
            }

            if (postCommandOnset < 0)
                return IncompleteJudgment(trace, squatCommand, rackCommand, rerackStarted, violations);

            int legalDepthIndex = FindLegalDepthIndex(
                trace,
                postCommandOnset,
                preRackEnd);
            if (legalDepthIndex < 0)
            {
                int deepestIndex = FindDeepestDepthIndex(trace, postCommandOnset, preRackEnd);
                SquatObservationSnapshot deepest = trace[deepestIndex];
                AddViolation(
                    violations,
                    SquatRuleViolationKind.INSUFFICIENT_DEPTH,
                    deepest.SimulationTick,
                    SquatRuleImplementationClass.RULE_DERIVED_GAME_PROXY,
                    SquatRuleEvidenceChannel.DEPTH_LANDMARKS,
                    deepest.Depth.LeftDepthM,
                    deepest.Depth.RightDepthM,
                    _tolerances.DepthMarginM);
            }

            int bottomIndex = FindFirstBottomIndex(trace, postCommandOnset, preRackEnd);
            int ascentIndex = bottomIndex >= 0
                ? FindAscentEstablishmentIndex(trace, bottomIndex, preRackEnd)
                : -1;
            int doubleDescentIndex = bottomIndex >= 0
                ? FindDoubleDescentIndex(trace, bottomIndex, ascentIndex, preRackEnd)
                : -1;
            if (doubleDescentIndex >= 0)
            {
                SquatObservationSnapshot eventSample = trace[doubleDescentIndex];
                AddViolation(
                    violations,
                    SquatRuleViolationKind.DOUBLE_DESCENT,
                    eventSample.SimulationTick,
                    SquatRuleImplementationClass.RULE_DERIVED_GAME_PROXY,
                    SquatRuleEvidenceChannel.BAR_POSITION |
                    SquatRuleEvidenceChannel.BAR_LINEAR_VELOCITY,
                    eventSample.Bar.LinearVelocityWorldMetersPerSecond.Y,
                    DownwardDisplacementFromPriorSample(trace, doubleDescentIndex),
                    _tolerances.DoubleDescentToleranceM);
            }

            int downwardMovementIndex = ascentIndex >= 0
                ? FindDownwardMovementIndex(trace, ascentIndex, preRackEnd)
                : -1;
            if (downwardMovementIndex >= 0)
            {
                SquatObservationSnapshot eventSample = trace[downwardMovementIndex];
                AddViolation(
                    violations,
                    SquatRuleViolationKind.DOWNWARD_MOVEMENT,
                    eventSample.SimulationTick,
                    SquatRuleImplementationClass.RULE_DERIVED_GAME_PROXY,
                    SquatRuleEvidenceChannel.BAR_POSITION |
                    SquatRuleEvidenceChannel.BAR_LINEAR_VELOCITY,
                    eventSample.Bar.LinearVelocityWorldMetersPerSecond.Y,
                    DownwardDisplacementFromPriorSample(trace, downwardMovementIndex),
                    _tolerances.DownwardMovementToleranceM);
            }

            int finalEnd = preRackEnd;
            int finalBegin = finalEnd - _tolerances.FinalPositionPersistenceTicks + 1;
            if (!HasConsecutiveWindow(trace, finalBegin, finalEnd))
                return IncompleteJudgment(trace, squatCommand, rackCommand, rerackStarted, violations);

            int firstInvalidFinal = FirstInvalidFinalIndex(trace, finalBegin, finalEnd);
            bool finalValid = firstInvalidFinal < 0;
            if (!finalValid)
            {
                SquatObservationSnapshot invalidFinal = trace[firstInvalidFinal];
                AddViolation(
                    violations,
                    SquatRuleViolationKind.FAILED_LOCKOUT,
                    invalidFinal.SimulationTick,
                    SquatRuleImplementationClass.RULE_DERIVED_GAME_PROXY,
                    SquatRuleEvidenceChannel.BAR_LINEAR_VELOCITY |
                    SquatRuleEvidenceChannel.BAR_ANGULAR_VELOCITY |
                    SquatRuleEvidenceChannel.KNEE_JOINTS |
                    SquatRuleEvidenceChannel.HIP_JOINTS |
                    SquatRuleEvidenceChannel.TRUNK_JOINTS,
                    MaxKneeAngle(invalidFinal),
                    MaxHipAngle(invalidFinal),
                    MaxTrunkAngle(invalidFinal));
            }
            else if (ascentIndex < 0)
            {
                AddViolation(
                    violations,
                    SquatRuleViolationKind.FAILED_LOCKOUT,
                    trace[finalBegin].SimulationTick,
                    SquatRuleImplementationClass.RULE_DERIVED_GAME_PROXY,
                    SquatRuleEvidenceChannel.BAR_POSITION |
                    SquatRuleEvidenceChannel.BAR_LINEAR_VELOCITY |
                    SquatRuleEvidenceChannel.KNEE_JOINTS,
                    0f,
                    0f,
                    0f);
            }

            float leftSlipBaseline = trace[squatCommandIndex].LeftFoot.SlipAccumulatedMeters;
            float rightSlipBaseline = trace[squatCommandIndex].RightFoot.SlipAccumulatedMeters;
            bool supportSlipEvidenceValid;
            int supportViolationIndex = FindSupportViolationIndex(
                trace,
                squatCommandIndex,
                preRackEnd,
                leftSlipBaseline,
                rightSlipBaseline,
                out supportSlipEvidenceValid);
            if (!supportSlipEvidenceValid)
                return IncompleteJudgment(trace, squatCommand, rackCommand, rerackStarted, violations);

            if (supportViolationIndex >= 0)
            {
                SquatObservationSnapshot eventSample = trace[supportViolationIndex];
                float leftSlipDelta = eventSample.LeftFoot.SlipAccumulatedMeters - leftSlipBaseline;
                float rightSlipDelta = eventSample.RightFoot.SlipAccumulatedMeters - rightSlipBaseline;
                AddViolation(
                    violations,
                    SquatRuleViolationKind.SUPPORT_VIOLATION,
                    eventSample.SimulationTick,
                    SquatRuleImplementationClass.GAME_SIMPLIFICATION,
                    SquatRuleEvidenceChannel.SUPPORT_CONTACT |
                    SquatRuleEvidenceChannel.FOOT_CONTACT |
                    SquatRuleEvidenceChannel.FOOT_SLIP,
                    Math.Max(leftSlipDelta, rightSlipDelta),
                    Math.Max(eventSample.LeftFoot.SlipSpeedMetersPerSecond, eventSample.RightFoot.SlipSpeedMetersPerSecond),
                    _tolerances.SupportSlipToleranceM);
            }

            if (rerackStarted.SimulationTick < rackCommand.SimulationTick)
            {
                AddViolation(
                    violations,
                    SquatRuleViolationKind.EARLY_RACK,
                    rerackStarted.SimulationTick,
                    SquatRuleImplementationClass.RULE_DERIVED_GAME_PROXY,
                    SquatRuleEvidenceChannel.COMMAND_TIMELINE,
                    float.NaN,
                    float.NaN,
                    float.NaN);
            }

            SquatPhysicalCompletionStatus completion = ascentIndex >= 0 && finalValid
                ? SquatPhysicalCompletionStatus.COMPLETED
                : SquatPhysicalCompletionStatus.INCOMPLETE;
            SquatJudgmentOutcome outcome = violations.Count == 0 &&
                completion == SquatPhysicalCompletionStatus.COMPLETED
                ? SquatJudgmentOutcome.GOOD_LIFT
                : SquatJudgmentOutcome.NO_LIFT;
            SquatRuleEventTicks eventTicks = new SquatRuleEventTicks(
                squatCommand.SimulationTick,
                trace[attemptCommencement].SimulationTick,
                bottomIndex >= 0 ? trace[bottomIndex].SimulationTick : SquatRuleEventTicks.NotAvailable,
                ascentIndex >= 0 ? trace[ascentIndex].SimulationTick : SquatRuleEventTicks.NotAvailable,
                finalValid ? trace[finalBegin].SimulationTick : SquatRuleEventTicks.NotAvailable,
                rackCommand.SimulationTick,
                rerackStarted.SimulationTick);
            return new SquatAttemptJudgment(
                SquatJudgmentEvidenceStatus.EVALUABLE,
                outcome,
                completion,
                _ruleSet,
                _tolerances,
                trace.Schema,
                eventTicks,
                violations);
        }

        public SquatAttemptJudgment Evaluate(
            SquatTrace trace,
            SquatRuleCommandTimeline commandTimeline,
            SquatRuleSetMetadata ruleSet,
            SquatRuleToleranceSet toleranceSet) => new SquatRuleProcessor(ruleSet, toleranceSet)
            .Evaluate(trace, commandTimeline);

        public static SquatAttemptJudgment EvaluateAttempt(
            SquatTrace trace,
            SquatRuleCommandTimeline commandTimeline,
            SquatRuleSetMetadata ruleSet,
            SquatRuleToleranceSet toleranceSet) => new SquatRuleProcessor(ruleSet, toleranceSet)
            .Evaluate(trace, commandTimeline);

        private SquatAttemptJudgment InvalidJudgment(SquatTrace trace) => new SquatAttemptJudgment(
            SquatJudgmentEvidenceStatus.INVALID_TRACE,
            SquatJudgmentOutcome.UNDETERMINED,
            SquatPhysicalCompletionStatus.UNKNOWN,
            _ruleSet,
            _tolerances,
            trace == null ? string.Empty : trace.Schema,
            EmptyEventTicks(),
            Array.Empty<SquatRuleViolationRecord>());

        private SquatAttemptJudgment IncompleteJudgment(
            SquatTrace trace,
            SquatRuleCommandEvent squatCommand = default(SquatRuleCommandEvent),
            SquatRuleCommandEvent rackCommand = default(SquatRuleCommandEvent),
            SquatRuleCommandEvent rerackStarted = default(SquatRuleCommandEvent),
            IReadOnlyList<SquatRuleViolationRecord> violations = null) => new SquatAttemptJudgment(
            SquatJudgmentEvidenceStatus.INCOMPLETE_ATTEMPT,
            SquatJudgmentOutcome.UNDETERMINED,
            SquatPhysicalCompletionStatus.INCOMPLETE,
            _ruleSet,
            _tolerances,
            trace.Schema,
            new SquatRuleEventTicks(
                squatCommand.SimulationTick,
                SquatRuleEventTicks.NotAvailable,
                SquatRuleEventTicks.NotAvailable,
                SquatRuleEventTicks.NotAvailable,
                SquatRuleEventTicks.NotAvailable,
                rackCommand.SimulationTick,
                rerackStarted.SimulationTick),
            violations ?? Array.Empty<SquatRuleViolationRecord>());

        private SquatAttemptJudgment InsufficientEvidenceJudgment(
            SquatTrace trace,
            SquatRuleCommandEvent squatCommand,
            SquatRuleCommandEvent rackCommand,
            SquatRuleCommandEvent rerackStarted) => new SquatAttemptJudgment(
            SquatJudgmentEvidenceStatus.INSUFFICIENT_EVIDENCE,
            SquatJudgmentOutcome.UNDETERMINED,
            SquatPhysicalCompletionStatus.UNKNOWN,
            _ruleSet,
            _tolerances,
            trace.Schema,
            new SquatRuleEventTicks(
                squatCommand.SimulationTick,
                SquatRuleEventTicks.NotAvailable,
                SquatRuleEventTicks.NotAvailable,
                SquatRuleEventTicks.NotAvailable,
                SquatRuleEventTicks.NotAvailable,
                rackCommand.SimulationTick,
                rerackStarted.SimulationTick),
            Array.Empty<SquatRuleViolationRecord>());

        private static SquatRuleEventTicks EmptyEventTicks() => new SquatRuleEventTicks(
            SquatRuleEventTicks.NotAvailable,
            SquatRuleEventTicks.NotAvailable,
            SquatRuleEventTicks.NotAvailable,
            SquatRuleEventTicks.NotAvailable,
            SquatRuleEventTicks.NotAvailable,
            SquatRuleEventTicks.NotAvailable,
            SquatRuleEventTicks.NotAvailable);

        private bool HasCanonicalFixedStep(SquatTrace trace)
        {
            for (int index = 0; index < trace.Count; index++)
            {
                if (Math.Abs(trace[index].FixedStepSeconds - SimulationConstants.FixedDeltaTimeSeconds) >
                    FoundationTolerances.SimulationTimeMapping)
                    return false;
            }

            return true;
        }

        private bool HasRuleWindowCoverage(
            SquatTrace trace,
            SquatRuleCommandEvent squatCommand,
            SquatRuleCommandEvent rackCommand,
            SquatRuleCommandEvent rerackStarted,
            out int squatCommandIndex,
            out int rackCommandIndex,
            out int rerackStartedIndex)
        {
            squatCommandIndex = FindIndexAtTick(trace, squatCommand.SimulationTick);
            rackCommandIndex = FindIndexAtTick(trace, rackCommand.SimulationTick);
            rerackStartedIndex = FindIndexAtTick(trace, rerackStarted.SimulationTick);
            if (squatCommandIndex < 0 || rackCommandIndex < 0 || rerackStartedIndex < 0)
                return false;

            int startEnd = squatCommandIndex - 1;
            int startBegin = startEnd - _tolerances.StartPositionPersistenceTicks + 1;
            if (!HasConsecutiveWindow(trace, startBegin, startEnd))
                return false;

            if (!HasConsecutiveWindow(trace, squatCommandIndex, rackCommandIndex))
                return false;

            int lifecycleBegin = Math.Min(rackCommandIndex, rerackStartedIndex);
            int lifecycleEnd = Math.Max(rackCommandIndex, rerackStartedIndex);
            return HasConsecutiveWindow(trace, lifecycleBegin, lifecycleEnd);
        }

        private static bool HasCommandEventTimeCoherence(
            SquatObservationSnapshot traceSample,
            SquatRuleCommandEvent commandEvent)
        {
            return Math.Abs(commandEvent.SimulationTimeSeconds - traceSample.SimulationTimeSeconds) <=
                FoundationTolerances.SimulationTimeMapping;
        }

        private bool HasMandatoryEvidence(SquatTrace trace, int preRackEnd)
        {
            for (int index = 0; index <= preRackEnd; index++)
            {
                SquatObservationSnapshot sample = trace[index];
                if (!sample.Quality.HasFlag(SquatTelemetryQualityFlags.POST_PHYSICS) ||
                    !sample.Quality.HasFlag(SquatTelemetryQualityFlags.RAW) ||
                    !sample.Quality.HasFlag(SquatTelemetryQualityFlags.BAR_AVAILABLE) ||
                    !sample.Quality.HasFlag(SquatTelemetryQualityFlags.SUPPORT_PRODUCER_AVAILABLE) ||
                    !sample.Quality.HasFlag(SquatTelemetryQualityFlags.DEPTH_LANDMARKS_AVAILABLE) ||
                    !sample.Quality.HasFlag(SquatTelemetryQualityFlags.JOINTS_AVAILABLE) ||
                    !sample.Quality.HasFlag(SquatTelemetryQualityFlags.LEFT_FOOT_PRODUCER_AVAILABLE) ||
                    !sample.Quality.HasFlag(SquatTelemetryQualityFlags.RIGHT_FOOT_PRODUCER_AVAILABLE) ||
                    !sample.Bar.IsAvailable ||
                    sample.Depth.Availability != SquatTelemetryAvailability.AVAILABLE ||
                    sample.Support.SupportAvailability != SquatTelemetryAvailability.AVAILABLE ||
                    sample.LeftFoot.Availability != SquatTelemetryAvailability.AVAILABLE ||
                    sample.RightFoot.Availability != SquatTelemetryAvailability.AVAILABLE ||
                    !RequiredJointEvidenceAvailable(sample))
                    return false;
            }

            return true;
        }

        private static bool RequiredJointEvidenceAvailable(SquatObservationSnapshot sample)
        {
            return sample.Joints.LeftKnee.JointAvailability == SquatTelemetryAvailability.AVAILABLE &&
                sample.Joints.RightKnee.JointAvailability == SquatTelemetryAvailability.AVAILABLE &&
                sample.Joints.LeftHip.JointAvailability == SquatTelemetryAvailability.AVAILABLE &&
                sample.Joints.RightHip.JointAvailability == SquatTelemetryAvailability.AVAILABLE &&
                sample.Joints.Abdomen.JointAvailability == SquatTelemetryAvailability.AVAILABLE &&
                sample.Joints.Thorax.JointAvailability == SquatTelemetryAvailability.AVAILABLE;
        }

        private int FirstInvalidStartIndex(SquatTrace trace, int start, int end)
        {
            for (int index = start; index <= end; index++)
            {
                if (!IsStartPosition(trace[index]))
                    return index;
            }

            return -1;
        }

        private int FirstInvalidFinalIndex(SquatTrace trace, int start, int end)
        {
            for (int index = start; index <= end; index++)
            {
                if (!IsFinalLockoutPosture(trace[index]))
                    return index;
            }

            return -1;
        }

        private bool IsStartPosition(SquatObservationSnapshot sample)
        {
            return IsKneesLocked(sample) &&
                MaxHipAngle(sample) <= _tolerances.HipErectToleranceRad &&
                MaxTrunkAngle(sample) <= _tolerances.TrunkErectToleranceRad &&
                IsBarMotionless(sample) &&
                HasEstablishedSupport(sample);
        }

        private bool IsFinalLockoutPosture(SquatObservationSnapshot sample)
        {
            return IsKneesLocked(sample) &&
                MaxHipAngle(sample) <= _tolerances.HipErectToleranceRad &&
                MaxTrunkAngle(sample) <= _tolerances.TrunkErectToleranceRad &&
                IsBarMotionless(sample);
        }

        private bool IsKneesLocked(SquatObservationSnapshot sample)
        {
            return Math.Abs(sample.Joints.LeftKnee.ActualAngleRadians) <= _tolerances.KneeLockoutToleranceRad &&
                Math.Abs(sample.Joints.RightKnee.ActualAngleRadians) <= _tolerances.KneeLockoutToleranceRad;
        }

        private bool IsBarMotionless(SquatObservationSnapshot sample)
        {
            return sample.Bar.LinearVelocityWorldMetersPerSecond.Length <= _tolerances.MotionlessBarVelocityMps &&
                sample.Bar.AngularVelocityBarRadiansPerSecond.Length <= _tolerances.MotionlessBarAngularVelocityRadS;
        }

        private static bool HasEstablishedSupport(SquatObservationSnapshot sample)
        {
            return sample.Support.HasSupport && sample.LeftFoot.IsInContact && sample.RightFoot.IsInContact;
        }

        private int FindAttemptCommencementFromKnees(SquatTrace trace, int start, int end)
        {
            for (int index = start; index <= end; index++)
            {
                if (!IsKneesLocked(trace[index]))
                    return index;
            }

            return -1;
        }

        private int FindFirstPhysicalDescentMotionOnset(SquatTrace trace, int start, int end)
        {
            int persistence = _tolerances.DescentOnsetPersistenceTicks;
            for (int index = start; index + persistence - 1 <= end; index++)
            {
                if (!HasConsecutiveWindow(trace, index, index + persistence - 1))
                    continue;

                bool physical = true;
                for (int sampleIndex = index; sampleIndex < index + persistence; sampleIndex++)
                {
                    if (!IsPhysicalDescentMotionSample(trace, sampleIndex))
                    {
                        physical = false;
                        break;
                    }
                }

                if (physical)
                    return index;
            }

            return -1;
        }

        private int FindFirstPhysicalDescentMotionOnsetAtOrAfter(SquatTrace trace, ulong tick, int end)
        {
            int persistence = _tolerances.DescentOnsetPersistenceTicks;
            for (int index = 0; index + persistence - 1 <= end; index++)
            {
                if (trace[index].SimulationTick < tick ||
                    !HasConsecutiveWindow(trace, index, index + persistence - 1))
                    continue;

                bool physical = true;
                for (int sampleIndex = index; sampleIndex < index + persistence; sampleIndex++)
                {
                    if (!IsPhysicalDescentMotionSample(trace, sampleIndex))
                    {
                        physical = false;
                        break;
                    }
                }

                if (physical)
                    return index;
            }

            return -1;
        }

        private bool IsPhysicalDescentMotionSample(SquatTrace trace, int index)
        {
            SquatObservationSnapshot sample = trace[index];
            if (sample.Bar.LinearVelocityWorldMetersPerSecond.Y <= -_tolerances.DescentOnsetVelocityMps)
                return true;

            if (index == 0)
                return false;

            return trace[index - 1].Bar.PositionWorldMeters.Y - sample.Bar.PositionWorldMeters.Y >=
                _tolerances.DoubleDescentToleranceM;
        }

        private int FindLegalDepthIndex(SquatTrace trace, int start, int end)
        {
            for (int index = start; index <= end; index++)
            {
                SquatDepthLandmarks depth = trace[index].Depth;
                if (depth.LeftHipCreaseY - depth.LeftKneeTopY <= -_tolerances.DepthMarginM &&
                    depth.RightHipCreaseY - depth.RightKneeTopY <= -_tolerances.DepthMarginM)
                    return index;
            }

            return -1;
        }

        private static int FindDeepestDepthIndex(SquatTrace trace, int start, int end)
        {
            int deepest = start;
            float worst = float.PositiveInfinity;
            for (int index = start; index <= end; index++)
            {
                float sideWorst = Math.Max(trace[index].Depth.LeftDepthM, trace[index].Depth.RightDepthM);
                if (sideWorst < worst)
                {
                    deepest = index;
                    worst = sideWorst;
                }
            }

            return deepest;
        }

        private int FindFirstBottomIndex(SquatTrace trace, int start, int end)
        {
            bool sawDownward = false;
            for (int index = start + 1; index <= end; index++)
            {
                SquatObservationSnapshot previous = trace[index - 1];
                SquatObservationSnapshot current = trace[index];
                if (previous.Bar.LinearVelocityWorldMetersPerSecond.Y <= -_tolerances.DescentOnsetVelocityMps ||
                    previous.Bar.PositionWorldMeters.Y - current.Bar.PositionWorldMeters.Y >= _tolerances.DoubleDescentToleranceM)
                {
                    sawDownward = true;
                    continue;
                }

                bool upwardTransition = current.Bar.LinearVelocityWorldMetersPerSecond.Y >=
                    _tolerances.AscentEstablishmentVelocityMps ||
                    current.Bar.PositionWorldMeters.Y - previous.Bar.PositionWorldMeters.Y >=
                    _tolerances.DoubleDescentToleranceM;
                if (sawDownward && upwardTransition)
                    return index - 1;
            }

            return -1;
        }

        private int FindAscentEstablishmentIndex(SquatTrace trace, int bottomIndex, int end)
        {
            int persistence = _tolerances.AscentEstablishmentPersistenceTicks;
            for (int index = bottomIndex + 1; index + persistence - 1 <= end; index++)
            {
                int last = index + persistence - 1;
                if (!HasConsecutiveWindow(trace, index, last))
                    continue;

                bool upward = true;
                for (int sampleIndex = index; sampleIndex <= last; sampleIndex++)
                {
                    if (trace[sampleIndex].Bar.LinearVelocityWorldMetersPerSecond.Y <
                        _tolerances.AscentEstablishmentVelocityMps)
                    {
                        upward = false;
                        break;
                    }
                }

                if (upward && trace[last].Bar.PositionWorldMeters.Y - trace[bottomIndex].Bar.PositionWorldMeters.Y >=
                    _tolerances.AscentEstablishmentDisplacementM)
                    return last;
            }

            return -1;
        }

        private int FindDoubleDescentIndex(SquatTrace trace, int bottomIndex, int ascentIndex, int end)
        {
            int scanEnd = ascentIndex >= 0 ? ascentIndex - 1 : end;
            return FindPersistentDownwardIndex(
                trace,
                bottomIndex + 1,
                scanEnd,
                _tolerances.DoubleDescentVelocityMps,
                _tolerances.DoubleDescentToleranceM,
                _tolerances.DoubleDescentPersistenceTicks);
        }

        private int FindDownwardMovementIndex(SquatTrace trace, int ascentIndex, int end)
        {
            return FindPersistentDownwardIndex(
                trace,
                ascentIndex + 1,
                end,
                _tolerances.DownwardMovementVelocityMps,
                _tolerances.DownwardMovementToleranceM,
                _tolerances.DownwardMovementPersistenceTicks);
        }

        private int FindPersistentDownwardIndex(
            SquatTrace trace,
            int start,
            int end,
            float velocityThreshold,
            float displacementTolerance,
            int persistence)
        {
            if (start > end)
                return -1;

            int runStart = -1;
            float referencePosition = 0f;
            for (int index = start; index <= end; index++)
            {
                bool downward = trace[index].Bar.LinearVelocityWorldMetersPerSecond.Y <= -velocityThreshold ||
                    trace[index - 1].Bar.PositionWorldMeters.Y - trace[index].Bar.PositionWorldMeters.Y >
                    displacementTolerance;
                if (!downward)
                {
                    runStart = -1;
                    continue;
                }

                if (runStart < 0)
                {
                    runStart = index;
                    referencePosition = trace[index - 1].Bar.PositionWorldMeters.Y;
                }

                if (index - runStart + 1 >= persistence &&
                    HasConsecutiveWindow(trace, runStart, index) &&
                    referencePosition - trace[index].Bar.PositionWorldMeters.Y > displacementTolerance)
                    return runStart;
            }

            return -1;
        }

        private int FindSupportViolationIndex(
            SquatTrace trace,
            int start,
            int end,
            float leftSlipBaseline,
            float rightSlipBaseline,
            out bool slipEvidenceValid)
        {
            slipEvidenceValid = true;
            int runStart = -1;
            for (int index = start; index <= end; index++)
            {
                SquatObservationSnapshot sample = trace[index];
                float leftSlipDelta = sample.LeftFoot.SlipAccumulatedMeters - leftSlipBaseline;
                float rightSlipDelta = sample.RightFoot.SlipAccumulatedMeters - rightSlipBaseline;
                if (leftSlipDelta < 0f || rightSlipDelta < 0f)
                {
                    slipEvidenceValid = false;
                    return -1;
                }

                bool violation = !HasEstablishedSupport(sample) ||
                    leftSlipDelta > _tolerances.SupportSlipToleranceM ||
                    rightSlipDelta > _tolerances.SupportSlipToleranceM ||
                    sample.LeftFoot.SlipSpeedMetersPerSecond > _tolerances.SupportSlipSpeedMps ||
                    sample.RightFoot.SlipSpeedMetersPerSecond > _tolerances.SupportSlipSpeedMps;
                if (!violation)
                {
                    runStart = -1;
                    continue;
                }

                if (runStart < 0)
                    runStart = index;
                if (index - runStart + 1 >= _tolerances.SupportViolationPersistenceTicks &&
                    HasConsecutiveWindow(trace, runStart, index))
                    return runStart;
            }

            return -1;
        }

        private static float DownwardDisplacementFromPriorSample(SquatTrace trace, int index)
        {
            return index <= 0
                ? 0f
                : trace[index - 1].Bar.PositionWorldMeters.Y - trace[index].Bar.PositionWorldMeters.Y;
        }

        private float MaxKneeAngle(SquatObservationSnapshot sample) => Math.Max(
            Math.Abs(sample.Joints.LeftKnee.ActualAngleRadians),
            Math.Abs(sample.Joints.RightKnee.ActualAngleRadians));

        private float MaxHipAngle(SquatObservationSnapshot sample) => Math.Max(
            Math.Abs(sample.Joints.LeftHip.ActualAngleRadians),
            Math.Abs(sample.Joints.RightHip.ActualAngleRadians));

        private float MaxTrunkAngle(SquatObservationSnapshot sample) => Math.Max(
            Math.Abs(sample.Joints.Abdomen.ActualAngleRadians),
            Math.Abs(sample.Joints.Thorax.ActualAngleRadians));

        private bool HasConsecutiveWindow(SquatTrace trace, int start, int end)
        {
            if (start < 0 || end < start || end >= trace.Count)
                return false;
            for (int index = start + 1; index <= end; index++)
            {
                if (trace[index].SimulationTick != trace[index - 1].SimulationTick + 1ul)
                    return false;
            }

            return true;
        }

        private static int FindIndexAtTick(SquatTrace trace, ulong tick)
        {
            for (int index = 0; index < trace.Count; index++)
            {
                if (trace[index].SimulationTick == tick)
                    return index;
                if (trace[index].SimulationTick > tick)
                    break;
            }

            return -1;
        }

        private void AddViolation(
            List<SquatRuleViolationRecord> violations,
            SquatRuleViolationKind kind,
            ulong onsetTick,
            SquatRuleImplementationClass sourceClassification,
            SquatRuleEvidenceChannel evidenceChannels,
            float measuredValueA,
            float measuredValueB,
            float measuredValueC)
        {
            if (violations.Count >= 8)
                return;

            violations.Add(new SquatRuleViolationRecord(
                kind,
                onsetTick,
                _ruleSet.RuleSetId,
                _ruleSet.ImplementationVersion,
                sourceClassification,
                evidenceChannels,
                measuredValueA,
                measuredValueB,
                measuredValueC,
                _tolerances.Version));
        }
    }
}
