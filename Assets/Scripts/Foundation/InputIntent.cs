using System;

namespace PowerliftingSimulator.Foundation
{
    public enum IntentAction : byte
    {
        Brace,
        Yield,
        Drive,
        Balance,
        Grip,
        Confirm,
        Abort
    }

    public enum IntentEdgeKind : byte
    {
        Pressed,
        Released
    }

    public readonly struct IntentEdgeEvent
    {
        public IntentEdgeEvent(IntentAction action, IntentEdgeKind edgeKind, double timestampSeconds)
        {
            Action = action;
            EdgeKind = edgeKind;
            TimestampSeconds = timestampSeconds;
        }

        public IntentAction Action { get; }
        public IntentEdgeKind EdgeKind { get; }
        public double TimestampSeconds { get; }
    }

    public readonly struct IntentInputSample
    {
        private IntentInputSample(
            IntentAction action,
            IntentEdgeKind edgeKind,
            float value,
            double timestampSeconds,
            bool isEdge)
        {
            Action = action;
            EdgeKind = edgeKind;
            Value = value;
            TimestampSeconds = timestampSeconds;
            IsEdge = isEdge;
        }

        public static IntentInputSample Edge(
            IntentAction action,
            IntentEdgeKind edgeKind,
            double timestampSeconds) =>
            new IntentInputSample(action, edgeKind, 0f, timestampSeconds, true);

        public static IntentInputSample Continuous(
            IntentAction action,
            float value,
            double timestampSeconds) =>
            new IntentInputSample(action, IntentEdgeKind.Pressed, value, timestampSeconds, false);

        public IntentAction Action { get; }
        public IntentEdgeKind EdgeKind { get; }
        public float Value { get; }
        public double TimestampSeconds { get; }
        public bool IsEdge { get; }
    }

    public sealed class InputTimeDomain
    {
        // For render interval k, [R0, R1] is the real-time window and
        // [S0, S1] is its accepted simulation window. The standalone mapper
        // derives S1 as S0 + min(R1 - R0, MaxAccumulatedTimeSeconds); the
        // runtime overload supplies the same horizon after its accumulator
        // has accounted for an already-pending fractional interval.
        // An event at E in that interval maps to
        //   S0 + ((E - R0) / (R1 - R0)) * (S1 - S0).
        // The interval is compressed as a whole, so discarded wall time is
        // never charged once per event or reintroduced by a later sample.
        private bool _hasRealTimestamp;
        private double _lastRealTimestampSeconds;
        private double _mappedSimulationTimeSeconds;
        private bool _hasOpenRenderInterval;
        private double _renderIntervalRealStartSeconds;
        private double _renderIntervalRealEndSeconds;
        private double _renderIntervalSimulationStartSeconds;
        private double _renderIntervalSimulationEndSeconds;
        private bool _hasMappedEventTimestamp;
        private double _lastMappedEventTimestampSeconds;

        public bool HasEpoch => _hasRealTimestamp;

        public double LastMappedTimestampSeconds => _mappedSimulationTimeSeconds;

        public Checkpoint CaptureCheckpoint() => new Checkpoint(
            _hasRealTimestamp,
            _lastRealTimestampSeconds,
            _mappedSimulationTimeSeconds,
            _hasOpenRenderInterval,
            _renderIntervalRealStartSeconds,
            _renderIntervalRealEndSeconds,
            _renderIntervalSimulationStartSeconds,
            _renderIntervalSimulationEndSeconds,
            _hasMappedEventTimestamp,
            _lastMappedEventTimestampSeconds);

        public void RestoreCheckpoint(Checkpoint checkpoint)
        {
            _hasRealTimestamp = checkpoint.HasRealTimestamp;
            _lastRealTimestampSeconds = checkpoint.LastRealTimestampSeconds;
            _mappedSimulationTimeSeconds = checkpoint.MappedSimulationTimeSeconds;
            _hasOpenRenderInterval = checkpoint.HasOpenRenderInterval;
            _renderIntervalRealStartSeconds = checkpoint.RenderIntervalRealStartSeconds;
            _renderIntervalRealEndSeconds = checkpoint.RenderIntervalRealEndSeconds;
            _renderIntervalSimulationStartSeconds = checkpoint.RenderIntervalSimulationStartSeconds;
            _renderIntervalSimulationEndSeconds = checkpoint.RenderIntervalSimulationEndSeconds;
            _hasMappedEventTimestamp = checkpoint.HasMappedEventTimestamp;
            _lastMappedEventTimestampSeconds = checkpoint.LastMappedEventTimestampSeconds;
        }

        public double AdvanceRenderInterval(double realTimestampSeconds)
        {
            ValidateTimestamp(realTimestampSeconds);

            if (!_hasRealTimestamp)
            {
                return OpenRenderInterval(
                    realTimestampSeconds,
                    _mappedSimulationTimeSeconds,
                    _mappedSimulationTimeSeconds);
            }

            if (realTimestampSeconds < _lastRealTimestampSeconds)
                throw new InvalidOperationException("Render timestamps must be monotonic.");

            double elapsedSeconds = realTimestampSeconds - _lastRealTimestampSeconds;
            double acceptedElapsedSeconds = Math.Min(
                elapsedSeconds,
                SimulationConstants.MaxAccumulatedTimeSeconds);
            return OpenRenderInterval(
                realTimestampSeconds,
                _mappedSimulationTimeSeconds,
                _mappedSimulationTimeSeconds + acceptedElapsedSeconds);
        }

        public double AdvanceRenderInterval(
            double realTimestampSeconds,
            double authoritativeSimulationStartSeconds,
            double authoritativeSimulationEndSeconds)
        {
            ValidateTimestamp(realTimestampSeconds);
            ValidateTimestamp(authoritativeSimulationStartSeconds);
            ValidateTimestamp(authoritativeSimulationEndSeconds);
            if (authoritativeSimulationEndSeconds < authoritativeSimulationStartSeconds)
                throw new ArgumentOutOfRangeException(nameof(authoritativeSimulationEndSeconds));
            if (authoritativeSimulationEndSeconds - authoritativeSimulationStartSeconds >
                SimulationConstants.MaxAccumulatedTimeSeconds)
                throw new ArgumentOutOfRangeException(nameof(authoritativeSimulationEndSeconds),
                    "The accepted input horizon cannot exceed the four-tick catch-up bound.");
            if (_hasOpenRenderInterval &&
                IsWithinMappingTolerance(realTimestampSeconds, _renderIntervalRealEndSeconds) &&
                IsWithinMappingTolerance(authoritativeSimulationStartSeconds, _renderIntervalSimulationStartSeconds) &&
                IsWithinMappingTolerance(authoritativeSimulationEndSeconds, _renderIntervalSimulationEndSeconds))
            {
                // Re-open the same interval after a transactional delivery
                // failure. This retries input without accepting wall time twice.
                _hasMappedEventTimestamp = false;
                return _renderIntervalSimulationEndSeconds;
            }
            if (_hasRealTimestamp &&
                Math.Abs(authoritativeSimulationStartSeconds - _mappedSimulationTimeSeconds) >
                    FoundationTolerances.SimulationTimeMapping)
                throw new InvalidOperationException(
                    "The authoritative input window must begin at the previous accepted simulation horizon.");
            if (_hasRealTimestamp && authoritativeSimulationStartSeconds < _mappedSimulationTimeSeconds)
                authoritativeSimulationStartSeconds = _mappedSimulationTimeSeconds;
            if (authoritativeSimulationEndSeconds < authoritativeSimulationStartSeconds)
                throw new ArgumentOutOfRangeException(nameof(authoritativeSimulationEndSeconds));

            return OpenRenderInterval(
                realTimestampSeconds,
                authoritativeSimulationStartSeconds,
                authoritativeSimulationEndSeconds);
        }

        public double Map(double realTimestampSeconds)
        {
            ValidateTimestamp(realTimestampSeconds);

            if (!_hasRealTimestamp || !_hasOpenRenderInterval)
                throw new InvalidOperationException("AdvanceRenderInterval must precede input event mapping.");
            if (realTimestampSeconds < _renderIntervalRealStartSeconds - FoundationTolerances.SimulationTimeMapping ||
                realTimestampSeconds > _renderIntervalRealEndSeconds + FoundationTolerances.SimulationTimeMapping)
                throw new InvalidOperationException("Input event timestamp must belong to the current render interval.");

            double clampedRealTimestampSeconds = realTimestampSeconds;
            if (clampedRealTimestampSeconds < _renderIntervalRealStartSeconds)
                clampedRealTimestampSeconds = _renderIntervalRealStartSeconds;
            else if (clampedRealTimestampSeconds > _renderIntervalRealEndSeconds)
                clampedRealTimestampSeconds = _renderIntervalRealEndSeconds;
            if (_hasMappedEventTimestamp && clampedRealTimestampSeconds < _lastMappedEventTimestampSeconds)
            {
                if (_lastMappedEventTimestampSeconds - clampedRealTimestampSeconds >
                    FoundationTolerances.SimulationTimeMapping)
                    throw new InvalidOperationException("Input event timestamps must be monotonic within a render interval.");

                clampedRealTimestampSeconds = _lastMappedEventTimestampSeconds;
            }
            _lastMappedEventTimestampSeconds = clampedRealTimestampSeconds;
            _hasMappedEventTimestamp = true;

            double realDurationSeconds = _renderIntervalRealEndSeconds - _renderIntervalRealStartSeconds;
            if (realDurationSeconds <= FoundationTolerances.SimulationTimeMapping)
                return _renderIntervalSimulationStartSeconds;

            double fraction = (clampedRealTimestampSeconds - _renderIntervalRealStartSeconds) / realDurationSeconds;
            return _renderIntervalSimulationStartSeconds +
                fraction * (_renderIntervalSimulationEndSeconds - _renderIntervalSimulationStartSeconds);
        }

        public void Reset(double simulationTimeSeconds = 0d)
        {
            ValidateTimestamp(simulationTimeSeconds);
            _hasRealTimestamp = false;
            _lastRealTimestampSeconds = 0d;
            _mappedSimulationTimeSeconds = simulationTimeSeconds;
            _hasOpenRenderInterval = false;
            _renderIntervalRealStartSeconds = 0d;
            _renderIntervalRealEndSeconds = 0d;
            _renderIntervalSimulationStartSeconds = simulationTimeSeconds;
            _renderIntervalSimulationEndSeconds = simulationTimeSeconds;
            _hasMappedEventTimestamp = false;
            _lastMappedEventTimestampSeconds = 0d;
        }

        private static void ValidateTimestamp(double timestampSeconds)
        {
            if (double.IsNaN(timestampSeconds) || double.IsInfinity(timestampSeconds))
                throw new ArgumentOutOfRangeException(nameof(timestampSeconds));
        }

        private static bool IsWithinMappingTolerance(double left, double right) =>
            Math.Abs(left - right) <= FoundationTolerances.SimulationTimeMapping;

        private double OpenRenderInterval(
            double realTimestampSeconds,
            double simulationStartSeconds,
            double simulationEndSeconds)
        {
            double realStartSeconds = _hasRealTimestamp
                ? _lastRealTimestampSeconds
                : realTimestampSeconds;
            if (realTimestampSeconds < realStartSeconds)
                throw new InvalidOperationException("Render timestamps must be monotonic.");

            _renderIntervalRealStartSeconds = realStartSeconds;
            _renderIntervalRealEndSeconds = realTimestampSeconds;
            _renderIntervalSimulationStartSeconds = simulationStartSeconds;
            _renderIntervalSimulationEndSeconds = simulationEndSeconds;
            _mappedSimulationTimeSeconds = simulationEndSeconds;
            _lastRealTimestampSeconds = realTimestampSeconds;
            _hasRealTimestamp = true;
            _hasMappedEventTimestamp = false;
            _hasOpenRenderInterval = true;
            return simulationEndSeconds;
        }

        public readonly struct Checkpoint
        {
            public Checkpoint(
                bool hasRealTimestamp,
                double lastRealTimestampSeconds,
                double mappedSimulationTimeSeconds,
                bool hasOpenRenderInterval,
                double renderIntervalRealStartSeconds,
                double renderIntervalRealEndSeconds,
                double renderIntervalSimulationStartSeconds,
                double renderIntervalSimulationEndSeconds,
                bool hasMappedEventTimestamp,
                double lastMappedEventTimestampSeconds)
            {
                HasRealTimestamp = hasRealTimestamp;
                LastRealTimestampSeconds = lastRealTimestampSeconds;
                MappedSimulationTimeSeconds = mappedSimulationTimeSeconds;
                HasOpenRenderInterval = hasOpenRenderInterval;
                RenderIntervalRealStartSeconds = renderIntervalRealStartSeconds;
                RenderIntervalRealEndSeconds = renderIntervalRealEndSeconds;
                RenderIntervalSimulationStartSeconds = renderIntervalSimulationStartSeconds;
                RenderIntervalSimulationEndSeconds = renderIntervalSimulationEndSeconds;
                HasMappedEventTimestamp = hasMappedEventTimestamp;
                LastMappedEventTimestampSeconds = lastMappedEventTimestampSeconds;
            }

            public bool HasRealTimestamp { get; }
            public double LastRealTimestampSeconds { get; }
            public double MappedSimulationTimeSeconds { get; }
            public bool HasOpenRenderInterval { get; }
            public double RenderIntervalRealStartSeconds { get; }
            public double RenderIntervalRealEndSeconds { get; }
            public double RenderIntervalSimulationStartSeconds { get; }
            public double RenderIntervalSimulationEndSeconds { get; }
            public bool HasMappedEventTimestamp { get; }
            public double LastMappedEventTimestampSeconds { get; }
        }
    }

    [Flags]
    public enum IntentEdgeFlags : ushort
    {
        None = 0,
        BracePressed = 1 << 0,
        BraceReleased = 1 << 1,
        YieldPressed = 1 << 2,
        YieldReleased = 1 << 3,
        DrivePressed = 1 << 4,
        DriveReleased = 1 << 5,
        BalancePressed = 1 << 6,
        BalanceReleased = 1 << 7,
        GripPressed = 1 << 8,
        GripReleased = 1 << 9,
        ConfirmPressed = 1 << 10,
        ConfirmReleased = 1 << 11,
        AbortPressed = 1 << 12,
        AbortReleased = 1 << 13
    }

    public readonly struct PlayerIntentFrame
    {
        public PlayerIntentFrame(
            ulong tick,
            double simulationTimeSeconds,
            IntentEdgeFlags edges,
            float brace01,
            float yield01,
            float drive01,
            float balanceX,
            float grip01,
            bool braceHeld,
            bool yieldHeld,
            bool driveHeld,
            bool balanceHeld,
            bool gripHeld,
            bool confirmHeld,
            bool abortHeld,
            int edgeEventCount = 0,
            IntentEdgeEvent[] edgeEvents = null)
        {
            if (edgeEventCount < 0 ||
                (edgeEventCount > 0 && (edgeEvents == null || edgeEvents.Length < edgeEventCount)))
                throw new ArgumentOutOfRangeException(nameof(edgeEventCount));
            Tick = tick;
            SimulationTimeSeconds = simulationTimeSeconds;
            Edges = edges;
            EdgeEventCount = edgeEventCount;
            _edgeEvents = edgeEvents ?? Array.Empty<IntentEdgeEvent>();
            Brace01 = brace01;
            Yield01 = yield01;
            Drive01 = drive01;
            BalanceX = balanceX;
            Grip01 = grip01;
            BraceHeld = braceHeld;
            YieldHeld = yieldHeld;
            DriveHeld = driveHeld;
            BalanceHeld = balanceHeld;
            GripHeld = gripHeld;
            ConfirmHeld = confirmHeld;
            AbortHeld = abortHeld;
        }

        private readonly IntentEdgeEvent[] _edgeEvents;

        public static PlayerIntentFrame Empty => new PlayerIntentFrame(
            0,
            0d,
            IntentEdgeFlags.None,
            0f,
            0f,
            0f,
            0f,
            0f,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            0);

        public ulong Tick { get; }
        public double SimulationTimeSeconds { get; }
        public IntentEdgeFlags Edges { get; }
        public int EdgeEventCount { get; }
        public float Brace01 { get; }
        public float Yield01 { get; }
        public float Drive01 { get; }
        public float BalanceX { get; }
        public float Grip01 { get; }
        public bool BraceHeld { get; }
        public bool YieldHeld { get; }
        public bool DriveHeld { get; }
        public bool BalanceHeld { get; }
        public bool GripHeld { get; }
        public bool ConfirmHeld { get; }
        public bool AbortHeld { get; }

        public bool WasPressed(IntentAction action) => Edges.HasFlag(GetEdgeFlag(action, IntentEdgeKind.Pressed));

        public bool WasReleased(IntentAction action) => Edges.HasFlag(GetEdgeFlag(action, IntentEdgeKind.Released));

        public IntentEdgeEvent EdgeEventAt(int index)
        {
            if (index < 0 || index >= EdgeEventCount)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _edgeEvents[index];
        }

        public bool IsHeld(IntentAction action)
        {
            return action switch
            {
                IntentAction.Brace => BraceHeld,
                IntentAction.Yield => YieldHeld,
                IntentAction.Drive => DriveHeld,
                IntentAction.Balance => BalanceHeld,
                IntentAction.Grip => GripHeld,
                IntentAction.Confirm => ConfirmHeld,
                IntentAction.Abort => AbortHeld,
                _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
            };
        }

        public float Value(IntentAction action)
        {
            return action switch
            {
                IntentAction.Brace => Brace01,
                IntentAction.Yield => Yield01,
                IntentAction.Drive => Drive01,
                IntentAction.Balance => BalanceX,
                IntentAction.Grip => Grip01,
                IntentAction.Confirm => ConfirmHeld ? 1f : 0f,
                IntentAction.Abort => AbortHeld ? 1f : 0f,
                _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
            };
        }

        internal static IntentEdgeFlags GetEdgeFlag(IntentAction action, IntentEdgeKind edgeKind)
        {
            return (action, edgeKind) switch
            {
                (IntentAction.Brace, IntentEdgeKind.Pressed) => IntentEdgeFlags.BracePressed,
                (IntentAction.Brace, IntentEdgeKind.Released) => IntentEdgeFlags.BraceReleased,
                (IntentAction.Yield, IntentEdgeKind.Pressed) => IntentEdgeFlags.YieldPressed,
                (IntentAction.Yield, IntentEdgeKind.Released) => IntentEdgeFlags.YieldReleased,
                (IntentAction.Drive, IntentEdgeKind.Pressed) => IntentEdgeFlags.DrivePressed,
                (IntentAction.Drive, IntentEdgeKind.Released) => IntentEdgeFlags.DriveReleased,
                (IntentAction.Balance, IntentEdgeKind.Pressed) => IntentEdgeFlags.BalancePressed,
                (IntentAction.Balance, IntentEdgeKind.Released) => IntentEdgeFlags.BalanceReleased,
                (IntentAction.Grip, IntentEdgeKind.Pressed) => IntentEdgeFlags.GripPressed,
                (IntentAction.Grip, IntentEdgeKind.Released) => IntentEdgeFlags.GripReleased,
                (IntentAction.Confirm, IntentEdgeKind.Pressed) => IntentEdgeFlags.ConfirmPressed,
                (IntentAction.Confirm, IntentEdgeKind.Released) => IntentEdgeFlags.ConfirmReleased,
                (IntentAction.Abort, IntentEdgeKind.Pressed) => IntentEdgeFlags.AbortPressed,
                (IntentAction.Abort, IntentEdgeKind.Released) => IntentEdgeFlags.AbortReleased,
                _ => throw new ArgumentOutOfRangeException(nameof(edgeKind), edgeKind, null)
            };
        }
    }

    public sealed class IntentBuffer
    {
        public const int DefaultEdgeCapacity = 64;
        public const int ContinuousHistoryCapacityPerChannel = SimulationConstants.MaxCatchUpTicksPerRenderFrame + 1;
        public const int ContinuousChannelCount = 5;
        public const int MaxContinuousPendingSampleCount = ContinuousHistoryCapacityPerChannel * ContinuousChannelCount;
        public const int MaxBatchInputCount = DefaultEdgeCapacity + MaxContinuousPendingSampleCount + ContinuousChannelCount;

        private readonly struct BufferedEdge
        {
            public BufferedEdge(IntentAction action, IntentEdgeKind edgeKind, double timestampSeconds)
            {
                Action = action;
                EdgeKind = edgeKind;
                TimestampSeconds = timestampSeconds;
            }

            public IntentAction Action { get; }
            public IntentEdgeKind EdgeKind { get; }
            public double TimestampSeconds { get; }
        }

        private readonly struct ContinuousSample
        {
            public ContinuousSample(float value, double timestampSeconds, ulong eligibilityTick)
            {
                Value = value;
                TimestampSeconds = timestampSeconds;
                EligibilityTick = eligibilityTick;
            }

            public float Value { get; }
            public double TimestampSeconds { get; }
            public ulong EligibilityTick { get; }
        }

        private readonly BufferedEdge[] _edges;
        private int _head;
        private int _count;
        private double _lastInputTimestampSeconds;
        private double _lastSampleEndSeconds;
        private bool _hasSampledTick;
        private ulong _lastSampledTick;

        private readonly ContinuousSample[] _braceHistory = new ContinuousSample[ContinuousHistoryCapacityPerChannel];
        private readonly ContinuousSample[] _yieldHistory = new ContinuousSample[ContinuousHistoryCapacityPerChannel];
        private readonly ContinuousSample[] _driveHistory = new ContinuousSample[ContinuousHistoryCapacityPerChannel];
        private readonly ContinuousSample[] _balanceHistory = new ContinuousSample[ContinuousHistoryCapacityPerChannel];
        private readonly ContinuousSample[] _gripHistory = new ContinuousSample[ContinuousHistoryCapacityPerChannel];
        private int _braceHistoryHead;
        private int _braceHistoryCount;
        private int _yieldHistoryHead;
        private int _yieldHistoryCount;
        private int _driveHistoryHead;
        private int _driveHistoryCount;
        private int _balanceHistoryHead;
        private int _balanceHistoryCount;
        private int _gripHistoryHead;
        private int _gripHistoryCount;

        private float _brace01;
        private float _yield01;
        private float _drive01;
        private float _balanceX;
        private float _grip01;

        private bool _braceHeld;
        private bool _yieldHeld;
        private bool _driveHeld;
        private bool _balanceHeld;
        private bool _gripHeld;
        private bool _confirmHeld;
        private bool _abortHeld;

        public IntentBuffer(int capacity = DefaultEdgeCapacity)
        {
            if (capacity < 1)
                throw new ArgumentOutOfRangeException(nameof(capacity));

            _edges = new BufferedEdge[capacity];
            Reset();
        }

        public int PendingSampleCount => _count +
            _braceHistoryCount +
            _yieldHistoryCount +
            _driveHistoryCount +
            _balanceHistoryCount +
            _gripHistoryCount;

        public void PushEdge(IntentAction action, IntentEdgeKind edgeKind, double timestampSeconds)
        {
            ValidateTimestamp(timestampSeconds);
            EnqueueEdge(new BufferedEdge(action, edgeKind, timestampSeconds));
        }

        public void SetContinuous(IntentAction action, float value, double timestampSeconds)
        {
            ValidateTimestamp(timestampSeconds);
            if (action == IntentAction.Confirm || action == IntentAction.Abort)
                throw new ArgumentException("Confirm and Abort are edge/held actions, not continuous actions.", nameof(action));
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value));

            float clamped = action == IntentAction.Balance
                ? Math.Max(-1f, Math.Min(1f, value))
                : Math.Max(0f, Math.Min(1f, value));

            if (timestampSeconds < _lastInputTimestampSeconds)
                throw new InvalidOperationException("Input samples must be enqueued in timestamp order.");
            if (timestampSeconds <= _lastSampleEndSeconds + FoundationTolerances.SimulationTimeMapping)
                throw new InvalidOperationException("Continuous input cannot arrive after its tick has been sampled.");

            switch (action)
            {
                case IntentAction.Brace:
                    StoreContinuousSample(
                        _braceHistory,
                        ref _braceHistoryHead,
                        ref _braceHistoryCount,
                        clamped,
                        timestampSeconds);
                    break;
                case IntentAction.Yield:
                    StoreContinuousSample(
                        _yieldHistory,
                        ref _yieldHistoryHead,
                        ref _yieldHistoryCount,
                        clamped,
                        timestampSeconds);
                    break;
                case IntentAction.Drive:
                    StoreContinuousSample(
                        _driveHistory,
                        ref _driveHistoryHead,
                        ref _driveHistoryCount,
                        clamped,
                        timestampSeconds);
                    break;
                case IntentAction.Balance:
                    StoreContinuousSample(
                        _balanceHistory,
                        ref _balanceHistoryHead,
                        ref _balanceHistoryCount,
                        clamped,
                        timestampSeconds);
                    break;
                case IntentAction.Grip:
                    StoreContinuousSample(
                        _gripHistory,
                        ref _gripHistoryHead,
                        ref _gripHistoryCount,
                        clamped,
                        timestampSeconds);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(action), action, null);
            }

            _lastInputTimestampSeconds = timestampSeconds;
        }

        public void ApplyBatch(IntentInputSample[] samples, int count)
        {
            if (samples == null)
                throw new ArgumentNullException(nameof(samples));
            if (count < 0 || count > samples.Length)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (count == 0)
                return;

            int batchEdgeCount = 0;
            double previousTimestampSeconds = _lastInputTimestampSeconds;
            int braceCount = _braceHistoryCount;
            int yieldCount = _yieldHistoryCount;
            int driveCount = _driveHistoryCount;
            int balanceCount = _balanceHistoryCount;
            int gripCount = _gripHistoryCount;
            ulong braceLastEligibilityTick = LastEligibilityTick(_braceHistory, _braceHistoryHead, braceCount);
            ulong yieldLastEligibilityTick = LastEligibilityTick(_yieldHistory, _yieldHistoryHead, yieldCount);
            ulong driveLastEligibilityTick = LastEligibilityTick(_driveHistory, _driveHistoryHead, driveCount);
            ulong balanceLastEligibilityTick = LastEligibilityTick(_balanceHistory, _balanceHistoryHead, balanceCount);
            ulong gripLastEligibilityTick = LastEligibilityTick(_gripHistory, _gripHistoryHead, gripCount);
            bool braceHasEligibility = braceCount > 0;
            bool yieldHasEligibility = yieldCount > 0;
            bool driveHasEligibility = driveCount > 0;
            bool balanceHasEligibility = balanceCount > 0;
            bool gripHasEligibility = gripCount > 0;

            for (int index = 0; index < count; index++)
            {
                IntentInputSample sample = samples[index];
                ValidateTimestamp(sample.TimestampSeconds);
                if (sample.TimestampSeconds < previousTimestampSeconds)
                    throw new InvalidOperationException("Input samples must be enqueued in timestamp order.");
                if (sample.TimestampSeconds <= _lastSampleEndSeconds + FoundationTolerances.SimulationTimeMapping)
                    throw new InvalidOperationException("Input cannot arrive after its tick has been sampled.");

                if (sample.IsEdge)
                {
                    batchEdgeCount++;
                    if (_count + batchEdgeCount > _edges.Length)
                        throw new InvalidOperationException("Input buffer capacity exceeded.");
                }
                else
                {
                    if (sample.Action == IntentAction.Confirm || sample.Action == IntentAction.Abort)
                        throw new ArgumentException("Confirm and Abort are edge/held actions, not continuous actions.", nameof(samples));
                    if (float.IsNaN(sample.Value) || float.IsInfinity(sample.Value))
                        throw new ArgumentOutOfRangeException(nameof(samples));

                    ulong eligibilityTick = FirstEligibleTick(sample.TimestampSeconds);
                    switch (sample.Action)
                    {
                        case IntentAction.Brace:
                            ValidateBatchContinuousSample(
                                eligibilityTick,
                                _braceHistory.Length,
                                ref braceCount,
                                ref braceLastEligibilityTick,
                                ref braceHasEligibility);
                            break;
                        case IntentAction.Yield:
                            ValidateBatchContinuousSample(
                                eligibilityTick,
                                _yieldHistory.Length,
                                ref yieldCount,
                                ref yieldLastEligibilityTick,
                                ref yieldHasEligibility);
                            break;
                        case IntentAction.Drive:
                            ValidateBatchContinuousSample(
                                eligibilityTick,
                                _driveHistory.Length,
                                ref driveCount,
                                ref driveLastEligibilityTick,
                                ref driveHasEligibility);
                            break;
                        case IntentAction.Balance:
                            ValidateBatchContinuousSample(
                                eligibilityTick,
                                _balanceHistory.Length,
                                ref balanceCount,
                                ref balanceLastEligibilityTick,
                                ref balanceHasEligibility);
                            break;
                        case IntentAction.Grip:
                            ValidateBatchContinuousSample(
                                eligibilityTick,
                                _gripHistory.Length,
                                ref gripCount,
                                ref gripLastEligibilityTick,
                                ref gripHasEligibility);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(samples));
                    }
                }

                previousTimestampSeconds = sample.TimestampSeconds;
            }

            for (int index = 0; index < count; index++)
            {
                IntentInputSample sample = samples[index];
                if (sample.IsEdge)
                    EnqueueEdge(new BufferedEdge(sample.Action, sample.EdgeKind, sample.TimestampSeconds));
                else
                    SetContinuous(sample.Action, sample.Value, sample.TimestampSeconds);
            }
        }

        public PlayerIntentFrame SampleForTick(ulong tick, double tickStartSeconds, double tickEndSeconds)
        {
            if (double.IsNaN(tickStartSeconds) || double.IsInfinity(tickStartSeconds) ||
                double.IsNaN(tickEndSeconds) || double.IsInfinity(tickEndSeconds) ||
                tickEndSeconds < tickStartSeconds)
                throw new ArgumentOutOfRangeException(nameof(tickEndSeconds));
            if (!_hasSampledTick)
            {
                if (tick != 1ul || Math.Abs(tickStartSeconds) > FoundationTolerances.SimulationTimeMapping)
                    throw new InvalidOperationException("Intent ticks must start at tick one and simulation time zero.");
            }
            else
            {
                if (_lastSampledTick == ulong.MaxValue || tick != _lastSampledTick + 1ul ||
                    Math.Abs(tickStartSeconds - _lastSampleEndSeconds) > FoundationTolerances.SimulationTimeMapping)
                    throw new InvalidOperationException("Intent ticks must be sampled as contiguous fixed intervals.");
            }

            IntentEdgeFlags edges = IntentEdgeFlags.None;
            int edgeEventCount = 0;
            IntentEdgeEvent[] edgeEvents = _count == 0 ? Array.Empty<IntentEdgeEvent>() : new IntentEdgeEvent[_count];
            double eligibleThroughSeconds = tickEndSeconds + FoundationTolerances.SimulationTimeMapping;
            if (_count > 0 && _edges[_head].TimestampSeconds < tickStartSeconds - FoundationTolerances.SimulationTimeMapping)
                throw new InvalidOperationException("An edge older than the current fixed interval cannot be reassigned.");
            while (_count > 0 && _edges[_head].TimestampSeconds <= eligibleThroughSeconds)
            {
                BufferedEdge edge = _edges[_head];
                _head = (_head + 1) % _edges.Length;
                _count--;
                edges |= ApplyEdge(edge.Action, edge.EdgeKind);
                edgeEvents[edgeEventCount] = new IntentEdgeEvent(edge.Action, edge.EdgeKind, edge.TimestampSeconds);
                edgeEventCount++;
            }

            ApplyPendingContinuous(tickEndSeconds);

            _lastSampleEndSeconds = tickEndSeconds;
            _hasSampledTick = true;
            _lastSampledTick = tick;
            return new PlayerIntentFrame(
                tick,
                tickEndSeconds,
                edges,
                _brace01,
                _yield01,
                _drive01,
                _balanceX,
                _grip01,
                _braceHeld,
                _yieldHeld,
                _driveHeld,
                _balanceHeld,
                _gripHeld,
                _confirmHeld,
                _abortHeld,
                edgeEventCount,
                edgeEvents);
        }

        public void Reset()
        {
            _head = 0;
            _count = 0;
            _lastInputTimestampSeconds = double.NegativeInfinity;
            _lastSampleEndSeconds = double.NegativeInfinity;
            _hasSampledTick = false;
            _lastSampledTick = 0ul;
            _brace01 = 0f;
            _yield01 = 0f;
            _drive01 = 0f;
            _balanceX = 0f;
            _grip01 = 0f;
            _braceHistoryHead = 0;
            _braceHistoryCount = 0;
            _yieldHistoryHead = 0;
            _yieldHistoryCount = 0;
            _driveHistoryHead = 0;
            _driveHistoryCount = 0;
            _balanceHistoryHead = 0;
            _balanceHistoryCount = 0;
            _gripHistoryHead = 0;
            _gripHistoryCount = 0;
            _braceHeld = false;
            _yieldHeld = false;
            _driveHeld = false;
            _balanceHeld = false;
            _gripHeld = false;
            _confirmHeld = false;
            _abortHeld = false;
        }

        private void EnqueueEdge(BufferedEdge edge)
        {
            if (edge.TimestampSeconds < _lastInputTimestampSeconds)
                throw new InvalidOperationException("Input samples must be enqueued in timestamp order.");
            if (edge.TimestampSeconds <= _lastSampleEndSeconds + FoundationTolerances.SimulationTimeMapping)
                throw new InvalidOperationException("Edge input cannot arrive after its tick has been sampled.");
            if (_count == _edges.Length)
                throw new InvalidOperationException("Input buffer capacity exceeded.");

            int tail = (_head + _count) % _edges.Length;
            _edges[tail] = edge;
            _count++;
            _lastInputTimestampSeconds = edge.TimestampSeconds;
        }

        private void ValidateTimestamp(double timestampSeconds)
        {
            if (double.IsNaN(timestampSeconds) || double.IsInfinity(timestampSeconds))
                throw new ArgumentOutOfRangeException(nameof(timestampSeconds));
        }

        private IntentEdgeFlags ApplyEdge(IntentAction action, IntentEdgeKind edgeKind)
        {
            bool held = edgeKind == IntentEdgeKind.Pressed;
            switch (action)
            {
                case IntentAction.Brace:
                    _braceHeld = held;
                    break;
                case IntentAction.Yield:
                    _yieldHeld = held;
                    break;
                case IntentAction.Drive:
                    _driveHeld = held;
                    break;
                case IntentAction.Balance:
                    _balanceHeld = held;
                    break;
                case IntentAction.Grip:
                    _gripHeld = held;
                    break;
                case IntentAction.Confirm:
                    _confirmHeld = held;
                    break;
                case IntentAction.Abort:
                    _abortHeld = held;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(action), action, null);
            }

            return PlayerIntentFrame.GetEdgeFlag(action, edgeKind);
        }

        private void ApplyPendingContinuous(double tickEndSeconds)
        {
            ApplyContinuousHistory(_braceHistory, ref _braceHistoryHead, ref _braceHistoryCount, tickEndSeconds, ref _brace01);
            ApplyContinuousHistory(_yieldHistory, ref _yieldHistoryHead, ref _yieldHistoryCount, tickEndSeconds, ref _yield01);
            ApplyContinuousHistory(_driveHistory, ref _driveHistoryHead, ref _driveHistoryCount, tickEndSeconds, ref _drive01);
            ApplyContinuousHistory(_balanceHistory, ref _balanceHistoryHead, ref _balanceHistoryCount, tickEndSeconds, ref _balanceX);
            ApplyContinuousHistory(_gripHistory, ref _gripHistoryHead, ref _gripHistoryCount, tickEndSeconds, ref _grip01);
        }

        private static void StoreContinuousSample(
            ContinuousSample[] history,
            ref int head,
            ref int count,
            float value,
            double timestampSeconds)
        {
            ulong eligibilityTick = FirstEligibleTick(timestampSeconds);
            if (count > 0)
            {
                int lastIndex = (head + count - 1) % history.Length;
                // Samples in one fixed-tick bucket have the same
                // LatestContinuousAt(tick_end) result. Replacing only the
                // newest sample in that bucket preserves every value that a
                // later eligible tick can still observe.
                if (history[lastIndex].EligibilityTick == eligibilityTick)
                {
                    history[lastIndex] = new ContinuousSample(value, timestampSeconds, eligibilityTick);
                    return;
                }
            }

            if (count == history.Length)
                throw new InvalidOperationException("Continuous input history capacity exceeded before a tick consumed it.");

            int tail = (head + count) % history.Length;
            history[tail] = new ContinuousSample(value, timestampSeconds, eligibilityTick);
            count++;
        }

        private static void ValidateBatchContinuousSample(
            ulong eligibilityTick,
            int historyLength,
            ref int count,
            ref ulong lastEligibilityTick,
            ref bool hasEligibility)
        {
            if (hasEligibility && eligibilityTick < lastEligibilityTick)
                throw new InvalidOperationException("Continuous input samples must be enqueued in timestamp order.");
            if (!hasEligibility || eligibilityTick != lastEligibilityTick)
            {
                if (count == historyLength)
                    throw new InvalidOperationException("Continuous input history capacity exceeded before a tick consumed it.");
                count++;
                hasEligibility = true;
            }

            lastEligibilityTick = eligibilityTick;
        }

        private static ulong LastEligibilityTick(ContinuousSample[] history, int head, int count)
        {
            if (count == 0)
                return 0ul;
            return history[(head + count - 1) % history.Length].EligibilityTick;
        }

        private static void ApplyContinuousHistory(
            ContinuousSample[] history,
            ref int head,
            ref int count,
            double tickEndSeconds,
            ref float currentValue)
        {
            double eligibleThroughSeconds = tickEndSeconds + FoundationTolerances.SimulationTimeMapping;
            while (count > 0 && history[head].TimestampSeconds <= eligibleThroughSeconds)
            {
                currentValue = history[head].Value;
                head = (head + 1) % history.Length;
                count--;
            }
        }

        private static ulong FirstEligibleTick(double timestampSeconds)
        {
            double adjustedTick = (timestampSeconds - FoundationTolerances.SimulationTimeMapping) /
                SimulationConstants.FixedDeltaTimeSeconds;
            if (adjustedTick <= 1d)
                return 1ul;
            if (adjustedTick >= ulong.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(timestampSeconds));

            return (ulong)Math.Ceiling(adjustedTick);
        }

    }
}
