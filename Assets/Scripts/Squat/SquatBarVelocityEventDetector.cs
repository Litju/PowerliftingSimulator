using System;
using System.Collections.Generic;

namespace PowerliftingSimulator.Squat
{
    public enum SquatBarVelocityEventKind : byte
    {
        V0,
        Vmax1,
        Dmax1,
        Vmin,
        Vmax2
    }

    public enum SquatBarVelocityEventStatus : byte
    {
        ResolvableStickingRegion,
        NoStickingRegion,
        InsufficientSignal,
        InvalidInput
    }

    public readonly struct SquatBarVelocitySample
    {
        public SquatBarVelocitySample(double timeSeconds, float positionMeters, float velocityMetersPerSecond)
        {
            TimeSeconds = timeSeconds;
            PositionMeters = positionMeters;
            VelocityMetersPerSecond = velocityMetersPerSecond;
        }

        public double TimeSeconds { get; }
        public float PositionMeters { get; }
        public float VelocityMetersPerSecond { get; }
    }

    public readonly struct SquatBarVelocityDetectorOptions
    {
        public SquatBarVelocityDetectorOptions(
            float samplePeriodSeconds = 0.01f,
            float lowPassCutoffHz = 10f,
            float noiseSigmaMultiplier = 4f)
        {
            if (!IsFinite(samplePeriodSeconds) || samplePeriodSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(samplePeriodSeconds));
            if (!IsFinite(lowPassCutoffHz) || lowPassCutoffHz <= 0f || lowPassCutoffHz >= 0.5f / samplePeriodSeconds)
                throw new ArgumentOutOfRangeException(nameof(lowPassCutoffHz));
            if (!IsFinite(noiseSigmaMultiplier) || noiseSigmaMultiplier <= 0f)
                throw new ArgumentOutOfRangeException(nameof(noiseSigmaMultiplier));

            SamplePeriodSeconds = samplePeriodSeconds;
            LowPassCutoffHz = lowPassCutoffHz;
            NoiseSigmaMultiplier = noiseSigmaMultiplier;
        }

        public float SamplePeriodSeconds { get; }
        public float LowPassCutoffHz { get; }
        public float NoiseSigmaMultiplier { get; }

        public static readonly SquatBarVelocityDetectorOptions Default =
            new SquatBarVelocityDetectorOptions(0.01f, 10f, 4f);

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public readonly struct SquatBarVelocityNoiseFloor
    {
        public SquatBarVelocityNoiseFloor(
            int stationarySampleCount,
            float velocitySigmaMetersPerSecond,
            float accelerationSigmaMetersPerSecondSquared,
            float velocityThresholdMetersPerSecond,
            float accelerationThresholdMetersPerSecondSquared)
        {
            StationarySampleCount = stationarySampleCount;
            VelocitySigmaMetersPerSecond = velocitySigmaMetersPerSecond;
            AccelerationSigmaMetersPerSecondSquared = accelerationSigmaMetersPerSecondSquared;
            VelocityThresholdMetersPerSecond = velocityThresholdMetersPerSecond;
            AccelerationThresholdMetersPerSecondSquared = accelerationThresholdMetersPerSecondSquared;
        }

        public int StationarySampleCount { get; }
        public float VelocitySigmaMetersPerSecond { get; }
        public float AccelerationSigmaMetersPerSecondSquared { get; }
        public float VelocityThresholdMetersPerSecond { get; }
        public float AccelerationThresholdMetersPerSecondSquared { get; }
        public bool IsAvailable => StationarySampleCount >= 3;
    }

    public readonly struct SquatBarVelocityEvent
    {
        public SquatBarVelocityEvent(
            SquatBarVelocityEventKind kind,
            int sampleIndex,
            double timeSeconds,
            float positionMeters,
            float displacementFromV0Meters,
            float rawVelocityMetersPerSecond,
            float filteredVelocityMetersPerSecond,
            float filteredAccelerationMetersPerSecondSquared,
            float prominenceMetersPerSecond)
        {
            Kind = kind;
            SampleIndex = sampleIndex;
            TimeSeconds = timeSeconds;
            PositionMeters = positionMeters;
            DisplacementFromV0Meters = displacementFromV0Meters;
            RawVelocityMetersPerSecond = rawVelocityMetersPerSecond;
            FilteredVelocityMetersPerSecond = filteredVelocityMetersPerSecond;
            FilteredAccelerationMetersPerSecondSquared = filteredAccelerationMetersPerSecondSquared;
            ProminenceMetersPerSecond = prominenceMetersPerSecond;
        }

        public SquatBarVelocityEventKind Kind { get; }
        public int SampleIndex { get; }
        public double TimeSeconds { get; }
        public float PositionMeters { get; }
        public float DisplacementFromV0Meters { get; }
        public float RawVelocityMetersPerSecond { get; }
        public float FilteredVelocityMetersPerSecond { get; }
        public float FilteredAccelerationMetersPerSecondSquared { get; }
        public float ProminenceMetersPerSecond { get; }
    }

    public sealed class SquatBarVelocityEventDetection
    {
        private readonly SquatBarVelocityEvent[] _events;
        private readonly float[] _filteredVelocity;
        private readonly float[] _filteredAcceleration;

        internal SquatBarVelocityEventDetection(
            SquatBarVelocityEventStatus status,
            SquatBarVelocityNoiseFloor noiseFloor,
            SquatBarVelocityEvent[] events,
            float[] filteredVelocity,
            float[] filteredAcceleration)
        {
            Status = status;
            NoiseFloor = noiseFloor;
            _events = events ?? Array.Empty<SquatBarVelocityEvent>();
            _filteredVelocity = filteredVelocity ?? Array.Empty<float>();
            _filteredAcceleration = filteredAcceleration ?? Array.Empty<float>();
        }

        public SquatBarVelocityEventStatus Status { get; }
        public SquatBarVelocityNoiseFloor NoiseFloor { get; }
        public bool HasStickingRegion => Status == SquatBarVelocityEventStatus.ResolvableStickingRegion;
        public IReadOnlyList<SquatBarVelocityEvent> Events => _events;
        public IReadOnlyList<float> FilteredVelocityMetersPerSecond => _filteredVelocity;
        public IReadOnlyList<float> FilteredAccelerationMetersPerSecondSquared => _filteredAcceleration;

        public bool TryGetEvent(SquatBarVelocityEventKind kind, out SquatBarVelocityEvent value)
        {
            for (int index = 0; index < _events.Length; index++)
            {
                if (_events[index].Kind == kind)
                {
                    value = _events[index];
                    return true;
                }
            }

            value = default(SquatBarVelocityEvent);
            return false;
        }
    }

    /// <summary>
    /// Offline diagnostic event extraction for an actual vertical bar signal.
    /// The input samples remain the raw engine observations; filtering is used
    /// only to make event identity robust to solver-scale jitter.
    /// </summary>
    public static class SquatBarVelocityEventDetector
    {
        private const int ButterworthOrder = 2;
        private const float MadToSigma = 1.4826f;
        private const float NumericalVelocityFloor = 1e-6f;
        private const float NumericalAccelerationFloor = 1e-5f;

        public static SquatBarVelocityNoiseFloor EstimateNoiseFloor(
            IReadOnlyList<float> stationaryVelocities,
            float samplePeriodSeconds)
        {
            if (!IsFinite(samplePeriodSeconds) || samplePeriodSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(samplePeriodSeconds));
            if (stationaryVelocities == null)
                throw new ArgumentNullException(nameof(stationaryVelocities));

            int count = stationaryVelocities.Count;
            if (count < 3)
                return new SquatBarVelocityNoiseFloor(0, float.NaN, float.NaN, float.NaN, float.NaN);

            float[] values = new float[count];
            for (int index = 0; index < count; index++)
            {
                if (!IsFinite(stationaryVelocities[index]))
                    return new SquatBarVelocityNoiseFloor(0, float.NaN, float.NaN, float.NaN, float.NaN);
                values[index] = stationaryVelocities[index];
            }

            float velocitySigma = RobustSigma(values);
            float[] accelerationSamples = new float[count - 1];
            for (int index = 1; index < count; index++)
                accelerationSamples[index - 1] = (values[index] - values[index - 1]) / samplePeriodSeconds;
            float accelerationSigma = RobustSigma(accelerationSamples);

            float velocityThreshold = Math.Max(NumericalVelocityFloor, velocitySigma);
            float accelerationThreshold = Math.Max(NumericalAccelerationFloor, accelerationSigma);
            return new SquatBarVelocityNoiseFloor(
                count,
                velocitySigma,
                accelerationSigma,
                velocityThreshold,
                accelerationThreshold);
        }

        public static SquatBarVelocityEventDetection Detect(
            IReadOnlyList<SquatBarVelocitySample> samples,
            IReadOnlyList<float> stationaryVelocities,
            SquatBarVelocityDetectorOptions options)
        {
            if (samples == null)
                throw new ArgumentNullException(nameof(samples));
            if (stationaryVelocities == null)
                throw new ArgumentNullException(nameof(stationaryVelocities));

            SquatBarVelocityNoiseFloor noiseFloor = EstimateNoiseFloor(
                stationaryVelocities,
                options.SamplePeriodSeconds);
            if (!noiseFloor.IsAvailable || samples.Count < 8)
                return new SquatBarVelocityEventDetection(
                    SquatBarVelocityEventStatus.InsufficientSignal,
                    noiseFloor,
                    null,
                    null,
                    null);

            int sampleCount = samples.Count;
            float[] positions = new float[sampleCount];
            float[] rawVelocity = new float[sampleCount];
            for (int index = 0; index < sampleCount; index++)
            {
                SquatBarVelocitySample sample = samples[index];
                if (!IsFinite(sample.TimeSeconds) || !IsFinite(sample.PositionMeters) ||
                    !IsFinite(sample.VelocityMetersPerSecond) ||
                    (index > 0 && sample.TimeSeconds <= samples[index - 1].TimeSeconds))
                {
                    return new SquatBarVelocityEventDetection(
                        SquatBarVelocityEventStatus.InvalidInput,
                        noiseFloor,
                        null,
                        null,
                        null);
                }

                positions[index] = sample.PositionMeters;
                rawVelocity[index] = sample.VelocityMetersPerSecond;
            }

            float[] filteredVelocity = ZeroPhaseButterworth(rawVelocity, options);
            float[] filteredAcceleration = Differentiate(filteredVelocity, options.SamplePeriodSeconds);
            int v0Index = IndexOfMinimum(positions);
            if (v0Index < 1 || v0Index >= sampleCount - 3)
            {
                return new SquatBarVelocityEventDetection(
                    SquatBarVelocityEventStatus.NoStickingRegion,
                    noiseFloor,
                    null,
                    filteredVelocity,
                    filteredAcceleration);
            }

            List<Extremum> extrema = FindExtrema(filteredVelocity);
            float velocityThreshold = options.NoiseSigmaMultiplier * noiseFloor.VelocityThresholdMetersPerSecond;
            float accelerationThreshold = options.NoiseSigmaMultiplier * noiseFloor.AccelerationThresholdMetersPerSecondSquared;

            if (!TryFindSequence(
                    extrema,
                    v0Index,
                    filteredVelocity,
                    filteredAcceleration,
                    velocityThreshold,
                    accelerationThreshold,
                    out Extremum vmax1,
                    out Extremum dmax1,
                    out Extremum vmin,
                    out Extremum vmax2))
            {
                return new SquatBarVelocityEventDetection(
                    SquatBarVelocityEventStatus.NoStickingRegion,
                    noiseFloor,
                    null,
                    filteredVelocity,
                    filteredAcceleration);
            }

            float bottomPosition = positions[v0Index];
            SquatBarVelocityEvent[] events =
            {
                CreateEvent(SquatBarVelocityEventKind.V0, v0Index, samples, positions, rawVelocity,
                    filteredVelocity, filteredAcceleration, bottomPosition, 0f),
                CreateEvent(SquatBarVelocityEventKind.Vmax1, vmax1.Index, samples, positions, rawVelocity,
                    filteredVelocity, filteredAcceleration, bottomPosition, vmax1.Prominence),
                CreateEvent(SquatBarVelocityEventKind.Dmax1, dmax1.Index, samples, positions, rawVelocity,
                    filteredVelocity, filteredAcceleration, bottomPosition, dmax1.Prominence),
                CreateEvent(SquatBarVelocityEventKind.Vmin, vmin.Index, samples, positions, rawVelocity,
                    filteredVelocity, filteredAcceleration, bottomPosition, vmin.Prominence),
                CreateEvent(SquatBarVelocityEventKind.Vmax2, vmax2.Index, samples, positions, rawVelocity,
                    filteredVelocity, filteredAcceleration, bottomPosition, vmax2.Prominence)
            };

            return new SquatBarVelocityEventDetection(
                SquatBarVelocityEventStatus.ResolvableStickingRegion,
                noiseFloor,
                events,
                filteredVelocity,
                filteredAcceleration);
        }

        private static SquatBarVelocityEvent CreateEvent(
            SquatBarVelocityEventKind kind,
            int index,
            IReadOnlyList<SquatBarVelocitySample> samples,
            IReadOnlyList<float> positions,
            IReadOnlyList<float> rawVelocity,
            IReadOnlyList<float> filteredVelocity,
            IReadOnlyList<float> filteredAcceleration,
            float bottomPosition,
            float prominence)
        {
            return new SquatBarVelocityEvent(
                kind,
                index,
                samples[index].TimeSeconds,
                positions[index],
                positions[index] - bottomPosition,
                rawVelocity[index],
                filteredVelocity[index],
                filteredAcceleration[index],
                prominence);
        }

        private static bool TryFindSequence(
            IReadOnlyList<Extremum> extrema,
            int v0Index,
            IReadOnlyList<float> filteredVelocity,
            IReadOnlyList<float> filteredAcceleration,
            float velocityThreshold,
            float accelerationThreshold,
            out Extremum vmax1,
            out Extremum dmax1,
            out Extremum vmin,
            out Extremum vmax2)
        {
            for (int firstIndex = 0; firstIndex < extrema.Count; firstIndex++)
            {
                Extremum first = extrema[firstIndex];
                if (!first.IsMaximum || first.Index <= v0Index || first.Value <= velocityThreshold ||
                    first.Prominence < velocityThreshold)
                    continue;

                for (int minimumIndex = firstIndex + 1; minimumIndex < extrema.Count; minimumIndex++)
                {
                    Extremum minimum = extrema[minimumIndex];
                    if (minimum.IsMaximum || minimum.Index <= first.Index)
                        continue;
                    if (minimum.Value >= first.Value - velocityThreshold || minimum.Prominence < velocityThreshold)
                        continue;

                    for (int secondIndex = minimumIndex + 1; secondIndex < extrema.Count; secondIndex++)
                    {
                        Extremum second = extrema[secondIndex];
                        if (!second.IsMaximum || second.Index <= minimum.Index ||
                            second.Value <= minimum.Value + velocityThreshold)
                            continue;

                        int decelerationIndex = minimum.Index;
                        float strongestDeceleration = filteredAcceleration[decelerationIndex];
                        for (int index = first.Index + 1; index < minimum.Index; index++)
                        {
                            if (filteredAcceleration[index] < strongestDeceleration)
                            {
                                strongestDeceleration = filteredAcceleration[index];
                                decelerationIndex = index;
                            }
                        }

                        if (strongestDeceleration >= -accelerationThreshold)
                            continue;

                        vmax1 = first;
                        dmax1 = new Extremum(decelerationIndex, false, filteredVelocity[decelerationIndex], 0f);
                        vmin = minimum;
                        vmax2 = second;
                        return true;
                    }
                }
            }

            vmax1 = default(Extremum);
            dmax1 = default(Extremum);
            vmin = default(Extremum);
            vmax2 = default(Extremum);
            return false;
        }

        private static List<Extremum> FindExtrema(IReadOnlyList<float> values)
        {
            List<Extremum> extrema = new List<Extremum>();
            for (int index = 1; index < values.Count - 1; index++)
            {
                float previousDelta = values[index] - values[index - 1];
                float nextDelta = values[index + 1] - values[index];
                if (previousDelta > 0f && nextDelta <= 0f)
                    extrema.Add(new Extremum(index, true, values[index], 0f));
                else if (previousDelta < 0f && nextDelta >= 0f)
                    extrema.Add(new Extremum(index, false, values[index], 0f));
            }

            for (int index = 0; index < extrema.Count; index++)
            {
                Extremum current = extrema[index];
                float prominence;
                if (current.IsMaximum)
                {
                    float left = PreviousOppositeValue(extrema, index, false, values[0]);
                    float right = NextOppositeValue(extrema, index, false, values[values.Count - 1]);
                    prominence = current.Value - Math.Max(left, right);
                }
                else
                {
                    float left = PreviousOppositeValue(extrema, index, true, values[0]);
                    float right = NextOppositeValue(extrema, index, true, values[values.Count - 1]);
                    prominence = Math.Min(left, right) - current.Value;
                }

                extrema[index] = new Extremum(current.Index, current.IsMaximum, current.Value, Math.Max(0f, prominence));
            }

            return extrema;
        }

        private static float PreviousOppositeValue(
            IReadOnlyList<Extremum> extrema,
            int currentIndex,
            bool wantMaximum,
            float fallback)
        {
            for (int index = currentIndex - 1; index >= 0; index--)
            {
                if (extrema[index].IsMaximum == wantMaximum)
                    return extrema[index].Value;
            }

            return fallback;
        }

        private static float NextOppositeValue(
            IReadOnlyList<Extremum> extrema,
            int currentIndex,
            bool wantMaximum,
            float fallback)
        {
            for (int index = currentIndex + 1; index < extrema.Count; index++)
            {
                if (extrema[index].IsMaximum == wantMaximum)
                    return extrema[index].Value;
            }

            return fallback;
        }

        private static float[] ZeroPhaseButterworth(
            IReadOnlyList<float> values,
            SquatBarVelocityDetectorOptions options)
        {
            int count = values.Count;
            float[] result = new float[count];
            if (count < 7)
            {
                for (int index = 0; index < count; index++)
                    result[index] = values[index];
                return result;
            }

            double omega = 2d * Math.PI * options.LowPassCutoffHz * options.SamplePeriodSeconds;
            double cosine = Math.Cos(omega);
            double sine = Math.Sin(omega);
            double alpha = sine / (2d * Math.Sqrt(0.5d));
            double b0 = (1d - cosine) * 0.5d;
            double b1 = 1d - cosine;
            double b2 = b0;
            double a0 = 1d + alpha;
            double a1 = -2d * cosine;
            double a2 = 1d - alpha;
            b0 /= a0;
            b1 /= a0;
            b2 /= a0;
            a1 /= a0;
            a2 /= a0;

            int padding = Math.Min(3 * (ButterworthOrder + 1), count - 1);
            float[] padded = new float[count + 2 * padding];
            for (int index = 0; index < padding; index++)
            {
                padded[padding - 1 - index] = values[index + 1];
                padded[padding + count + index] = values[count - 2 - index];
            }
            for (int index = 0; index < count; index++)
                padded[padding + index] = values[index];

            float[] forward = FilterOneWay(padded, b0, b1, b2, a1, a2);
            Array.Reverse(forward);
            float[] backward = FilterOneWay(forward, b0, b1, b2, a1, a2);
            Array.Reverse(backward);
            Array.Copy(backward, padding, result, 0, count);
            return result;
        }

        private static float[] FilterOneWay(
            IReadOnlyList<float> values,
            double b0,
            double b1,
            double b2,
            double a1,
            double a2)
        {
            float[] result = new float[values.Count];
            double x1 = values[0];
            double x2 = values[0];
            double y1 = values[0];
            double y2 = values[0];
            for (int index = 0; index < values.Count; index++)
            {
                double x0 = values[index];
                double y0 = b0 * x0 + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                result[index] = (float)y0;
                x2 = x1;
                x1 = x0;
                y2 = y1;
                y1 = y0;
            }

            return result;
        }

        private static float[] Differentiate(IReadOnlyList<float> values, float samplePeriodSeconds)
        {
            float[] derivative = new float[values.Count];
            if (values.Count == 1)
            {
                derivative[0] = 0f;
                return derivative;
            }

            derivative[0] = (values[1] - values[0]) / samplePeriodSeconds;
            for (int index = 1; index < values.Count - 1; index++)
                derivative[index] = (values[index + 1] - values[index - 1]) / (2f * samplePeriodSeconds);
            derivative[values.Count - 1] =
                (values[values.Count - 1] - values[values.Count - 2]) / samplePeriodSeconds;
            return derivative;
        }

        private static float RobustSigma(IReadOnlyList<float> values)
        {
            float center = Median(values);
            float[] deviations = new float[values.Count];
            for (int index = 0; index < values.Count; index++)
                deviations[index] = Math.Abs(values[index] - center);
            return MadToSigma * Median(deviations);
        }

        private static float Median(IReadOnlyList<float> values)
        {
            float[] sorted = new float[values.Count];
            for (int index = 0; index < values.Count; index++)
                sorted[index] = values[index];
            Array.Sort(sorted);
            int middle = sorted.Length / 2;
            return sorted.Length % 2 == 0
                ? 0.5f * (sorted[middle - 1] + sorted[middle])
                : sorted[middle];
        }

        private static int IndexOfMinimum(IReadOnlyList<float> values)
        {
            int indexOfMinimum = 0;
            float minimum = values[0];
            for (int index = 1; index < values.Count; index++)
            {
                if (values[index] < minimum)
                {
                    minimum = values[index];
                    indexOfMinimum = index;
                }
            }

            return indexOfMinimum;
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private readonly struct Extremum
        {
            public Extremum(int index, bool isMaximum, float value, float prominence)
            {
                Index = index;
                IsMaximum = isMaximum;
                Value = value;
                Prominence = prominence;
            }

            public int Index { get; }
            public bool IsMaximum { get; }
            public float Value { get; }
            public float Prominence { get; }
        }
    }
}
