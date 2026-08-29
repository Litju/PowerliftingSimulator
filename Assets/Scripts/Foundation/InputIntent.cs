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

    public sealed class InputTimeDomain
    {
        private bool _hasRealTimestamp;
        private double _lastRealTimestampSeconds;
        private double _mappedSimulationTimeSeconds;

        public bool HasEpoch => _hasRealTimestamp;

        public double LastMappedTimestampSeconds => _mappedSimulationTimeSeconds;

        public double Map(double realTimestampSeconds)
        {
            ValidateTimestamp(realTimestampSeconds);

            if (!_hasRealTimestamp)
            {
                _hasRealTimestamp = true;
                _lastRealTimestampSeconds = realTimestampSeconds;
                return _mappedSimulationTimeSeconds;
            }

            if (realTimestampSeconds < _lastRealTimestampSeconds)
                throw new InvalidOperationException("Input timestamps must be monotonic.");

            double elapsedSeconds = realTimestampSeconds - _lastRealTimestampSeconds;
            _lastRealTimestampSeconds = realTimestampSeconds;
            _mappedSimulationTimeSeconds += Math.Min(
                elapsedSeconds,
                SimulationConstants.MaxAccumulatedTimeSeconds);
            return _mappedSimulationTimeSeconds;
        }

        public void Reset(double simulationTimeSeconds = 0d)
        {
            ValidateTimestamp(simulationTimeSeconds);
            _hasRealTimestamp = false;
            _lastRealTimestampSeconds = 0d;
            _mappedSimulationTimeSeconds = simulationTimeSeconds;
        }

        private static void ValidateTimestamp(double timestampSeconds)
        {
            if (double.IsNaN(timestampSeconds) || double.IsInfinity(timestampSeconds))
                throw new ArgumentOutOfRangeException(nameof(timestampSeconds));
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
            bool abortHeld)
        {
            Tick = tick;
            SimulationTimeSeconds = simulationTimeSeconds;
            Edges = edges;
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
            false);

        public ulong Tick { get; }
        public double SimulationTimeSeconds { get; }
        public IntentEdgeFlags Edges { get; }
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

        private readonly BufferedEdge[] _edges;
        private int _head;
        private int _count;
        private double _lastInputTimestampSeconds;
        private double _lastSampleEndSeconds;

        private float _brace01;
        private float _yield01;
        private float _drive01;
        private float _balanceX;
        private float _grip01;

        private bool _braceContinuousPending;
        private bool _yieldContinuousPending;
        private bool _driveContinuousPending;
        private bool _balanceContinuousPending;
        private bool _gripContinuousPending;
        private float _braceContinuous01;
        private float _yieldContinuous01;
        private float _driveContinuous01;
        private float _balanceContinuousX;
        private float _gripContinuous01;
        private double _braceContinuousTimestampSeconds;
        private double _yieldContinuousTimestampSeconds;
        private double _driveContinuousTimestampSeconds;
        private double _balanceContinuousTimestampSeconds;
        private double _gripContinuousTimestampSeconds;

        private bool _braceHeld;
        private bool _yieldHeld;
        private bool _driveHeld;
        private bool _balanceHeld;
        private bool _gripHeld;
        private bool _confirmHeld;
        private bool _abortHeld;

        public IntentBuffer(int capacity = 64)
        {
            if (capacity < 1)
                throw new ArgumentOutOfRangeException(nameof(capacity));

            _edges = new BufferedEdge[capacity];
            Reset();
        }

        public int PendingSampleCount => _count +
            (_braceContinuousPending ? 1 : 0) +
            (_yieldContinuousPending ? 1 : 0) +
            (_driveContinuousPending ? 1 : 0) +
            (_balanceContinuousPending ? 1 : 0) +
            (_gripContinuousPending ? 1 : 0);

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

            switch (action)
            {
                case IntentAction.Brace:
                    _braceContinuous01 = clamped;
                    _braceContinuousTimestampSeconds = timestampSeconds;
                    _braceContinuousPending = true;
                    break;
                case IntentAction.Yield:
                    _yieldContinuous01 = clamped;
                    _yieldContinuousTimestampSeconds = timestampSeconds;
                    _yieldContinuousPending = true;
                    break;
                case IntentAction.Drive:
                    _driveContinuous01 = clamped;
                    _driveContinuousTimestampSeconds = timestampSeconds;
                    _driveContinuousPending = true;
                    break;
                case IntentAction.Balance:
                    _balanceContinuousX = clamped;
                    _balanceContinuousTimestampSeconds = timestampSeconds;
                    _balanceContinuousPending = true;
                    break;
                case IntentAction.Grip:
                    _gripContinuous01 = clamped;
                    _gripContinuousTimestampSeconds = timestampSeconds;
                    _gripContinuousPending = true;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(action), action, null);
            }

            _lastInputTimestampSeconds = timestampSeconds;
        }

        public PlayerIntentFrame SampleForTick(ulong tick, double tickStartSeconds, double tickEndSeconds)
        {
            if (double.IsNaN(tickStartSeconds) || double.IsInfinity(tickStartSeconds) ||
                double.IsNaN(tickEndSeconds) || double.IsInfinity(tickEndSeconds) ||
                tickEndSeconds < tickStartSeconds)
                throw new ArgumentOutOfRangeException(nameof(tickEndSeconds));
            if (tickEndSeconds <= _lastSampleEndSeconds)
                throw new InvalidOperationException("Intent ticks must be sampled in strictly increasing time order.");

            IntentEdgeFlags edges = IntentEdgeFlags.None;
            double eligibleThroughSeconds = tickEndSeconds + FoundationTolerances.SimulationTimeMapping;
            while (_count > 0 && _edges[_head].TimestampSeconds <= eligibleThroughSeconds)
            {
                BufferedEdge edge = _edges[_head];
                _head = (_head + 1) % _edges.Length;
                _count--;
                edges |= ApplyEdge(edge.Action, edge.EdgeKind);
            }

            ApplyPendingContinuous(eligibleThroughSeconds);

            _lastSampleEndSeconds = tickEndSeconds;
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
                _abortHeld);
        }

        public void Reset()
        {
            _head = 0;
            _count = 0;
            _lastInputTimestampSeconds = double.NegativeInfinity;
            _lastSampleEndSeconds = double.NegativeInfinity;
            _brace01 = 0f;
            _yield01 = 0f;
            _drive01 = 0f;
            _balanceX = 0f;
            _grip01 = 0f;
            _braceContinuous01 = 0f;
            _yieldContinuous01 = 0f;
            _driveContinuous01 = 0f;
            _balanceContinuousX = 0f;
            _gripContinuous01 = 0f;
            _braceContinuousPending = false;
            _yieldContinuousPending = false;
            _driveContinuousPending = false;
            _balanceContinuousPending = false;
            _gripContinuousPending = false;
            _braceContinuousTimestampSeconds = 0d;
            _yieldContinuousTimestampSeconds = 0d;
            _driveContinuousTimestampSeconds = 0d;
            _balanceContinuousTimestampSeconds = 0d;
            _gripContinuousTimestampSeconds = 0d;
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
            if (_braceContinuousPending && _braceContinuousTimestampSeconds <= tickEndSeconds)
            {
                _brace01 = _braceContinuous01;
                _braceContinuousPending = false;
            }
            if (_yieldContinuousPending && _yieldContinuousTimestampSeconds <= tickEndSeconds)
            {
                _yield01 = _yieldContinuous01;
                _yieldContinuousPending = false;
            }
            if (_driveContinuousPending && _driveContinuousTimestampSeconds <= tickEndSeconds)
            {
                _drive01 = _driveContinuous01;
                _driveContinuousPending = false;
            }
            if (_balanceContinuousPending && _balanceContinuousTimestampSeconds <= tickEndSeconds)
            {
                _balanceX = _balanceContinuousX;
                _balanceContinuousPending = false;
            }
            if (_gripContinuousPending && _gripContinuousTimestampSeconds <= tickEndSeconds)
            {
                _grip01 = _gripContinuous01;
                _gripContinuousPending = false;
            }
        }

    }
}
