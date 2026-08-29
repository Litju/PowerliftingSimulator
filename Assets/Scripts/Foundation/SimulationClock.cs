using System;

namespace PowerliftingSimulator.Foundation
{
    public readonly struct SimulationTime
    {
        public SimulationTime(ulong tick, double simulationTimeSeconds)
        {
            Tick = tick;
            SimulationTimeSeconds = simulationTimeSeconds;
            FixedDeltaTimeSeconds = SimulationConstants.FixedDeltaTimeSeconds;
        }

        public ulong Tick { get; }
        public double SimulationTimeSeconds { get; }
        public double FixedDeltaTimeSeconds { get; }
    }

    public sealed class SimulationClock
    {
        private ulong _tick;

        public SimulationTime Current => new SimulationTime(_tick, SimulationConstants.TimeForTick(_tick));

        public SimulationTime Advance()
        {
            if (_tick == ulong.MaxValue)
                throw new InvalidOperationException("Simulation tick overflow.");

            _tick++;
            return Current;
        }

        public void Reset() => _tick = 0;
    }
}
