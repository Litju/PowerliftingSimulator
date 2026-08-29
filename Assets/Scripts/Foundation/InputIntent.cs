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
        private enum SampleKind : byte
        {
            Edge,
            Continuous
        }

        private readonly struct BufferedSample
        {
            public BufferedSample(IntentAction action, SampleKind kind, IntentEdgeKind edgeKind, float value, double timestampSeconds)
            {
                Action = action;
                Kind = kind;
                EdgeKind = edgeKind;
                Value = value;
                TimestampSeconds = timestampSeconds;
            }

            public IntentAction Action { get; }
            public SampleKind Kind { get; }
            public IntentEdgeKind EdgeKind { get; }
            public float Value { get; }
            public double TimestampSeconds { get; }
        }

        private readonly BufferedSample[] _samples;
        private int _head;
        private int _count;
        private double _lastEnqueuedTimestampSeconds;
        private double _lastSampleEndSeconds;
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

        public IntentBuffer(int capacity = 64)
        {
            if (capacity < 1)
                throw new ArgumentOutOfRangeException(nameof(capacity));

            _samples = new BufferedSample[capacity];
            Reset();
        }

        public int PendingSampleCount => _count;

        public void PushEdge(IntentAction action, IntentEdgeKind edgeKind, double timestampSeconds)
        {
            ValidateTimestamp(timestampSeconds);
            Enqueue(new BufferedSample(action, SampleKind.Edge, edgeKind, 0f, timestampSeconds));
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
            Enqueue(new BufferedSample(action, SampleKind.Continuous, IntentEdgeKind.Pressed, clamped, timestampSeconds));
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
            while (_count > 0 && _samples[_head].TimestampSeconds <= tickEndSeconds)
            {
                BufferedSample sample = _samples[_head];
                _head = (_head + 1) % _samples.Length;
                _count--;

                if (sample.Kind == SampleKind.Edge)
                    edges |= ApplyEdge(sample.Action, sample.EdgeKind);
                else
                    ApplyContinuous(sample.Action, sample.Value);
            }

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
            _lastEnqueuedTimestampSeconds = double.NegativeInfinity;
            _lastSampleEndSeconds = double.NegativeInfinity;
            _brace01 = 0f;
            _yield01 = 0f;
            _drive01 = 0f;
            _balanceX = 0f;
            _grip01 = 0f;
            _braceHeld = false;
            _yieldHeld = false;
            _driveHeld = false;
            _balanceHeld = false;
            _gripHeld = false;
            _confirmHeld = false;
            _abortHeld = false;
        }

        private void Enqueue(BufferedSample sample)
        {
            if (sample.TimestampSeconds < _lastEnqueuedTimestampSeconds)
                throw new InvalidOperationException("Input samples must be enqueued in timestamp order.");
            if (_count == _samples.Length)
                throw new InvalidOperationException("Input buffer capacity exceeded.");

            int tail = (_head + _count) % _samples.Length;
            _samples[tail] = sample;
            _count++;
            _lastEnqueuedTimestampSeconds = sample.TimestampSeconds;
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

        private void ApplyContinuous(IntentAction action, float value)
        {
            switch (action)
            {
                case IntentAction.Brace:
                    _brace01 = value;
                    break;
                case IntentAction.Yield:
                    _yield01 = value;
                    break;
                case IntentAction.Drive:
                    _drive01 = value;
                    break;
                case IntentAction.Balance:
                    _balanceX = value;
                    break;
                case IntentAction.Grip:
                    _grip01 = value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(action), action, null);
            }
        }

    }
}
