using System;
using NUnit.Framework;
using PowerliftingSimulator.Equipment;
using PowerliftingSimulator.Foundation;
using UnityEngine;

namespace PowerliftingSimulator.Tests
{
    public sealed class BarbellAndTraceContractTests
    {
        [Test]
        public void BAR_LOADING_IS_EXACT_AND_SYMMETRIC()
        {
            float[] loads = { 25f, 105f, 205f };
            for (int loadIndex = 0; loadIndex < loads.Length; loadIndex++)
            {
                BarbellLoadPlan plan = BarbellLoadingSolver.Solve(loads[loadIndex]);
                Assert.That(plan.TotalMassKg, Is.EqualTo(loads[loadIndex]).Within(0.0001f));
                Assert.That(plan.PlateMassPerSideKg * 2f + BarbellPrototypeConfiguration.BaseBarbellMassKg, Is.EqualTo(loads[loadIndex]).Within(0.0001f));

                float previous = float.PositiveInfinity;
                for (int index = 0; index < plan.PlatesPerSideKg.Count; index++)
                {
                    Assert.That(plan.PlatesPerSideKg[index], Is.LessThanOrEqualTo(previous));
                    previous = plan.PlatesPerSideKg[index];
                }
            }

            Assert.Throws<ArgumentException>(() => BarbellLoadingSolver.Solve(106f));
            Assert.Throws<ArgumentOutOfRangeException>(() => BarbellLoadingSolver.Solve(24f));
            Assert.Throws<ArgumentOutOfRangeException>(() => BarbellLoadingSolver.Solve(float.NaN));
        }

        [Test]
        public void BAR_MASS_AND_INERTIA_CHANGE_WITH_LOAD()
        {
            BarbellInertiaModel light = BarbellPrototypeConfiguration.ComputeInertia(BarbellLoadingSolver.Solve(25f));
            BarbellInertiaModel heavy = BarbellPrototypeConfiguration.ComputeInertia(BarbellLoadingSolver.Solve(205f));

            Assert.That(heavy.Components.Count, Is.GreaterThan(light.Components.Count));
            Assert.That(heavy.CenterOfMassBarMeters.magnitude, Is.LessThan(0.00001f));
            Assert.That(light.CenterOfMassBarMeters.magnitude, Is.LessThan(0.00001f));
            Assert.That(heavy.InertiaTensorKgM2.x, Is.GreaterThan(light.InertiaTensorKgM2.x));
            Assert.That(heavy.InertiaTensorKgM2.y, Is.GreaterThan(light.InertiaTensorKgM2.y));
            Assert.That(heavy.InertiaTensorKgM2.z, Is.GreaterThan(light.InertiaTensorKgM2.z));
            Assert.That(IsFinitePositive(heavy.InertiaTensorKgM2), Is.True);
        }

        [Test]
        public void PHYSICAL_OBSERVATION_AND_TRACE_ARE_IMMUTABLE_BOUNDED_AND_MONOTONIC()
        {
            var source = new[] { Body("barbell", 1f) };
            PhysicalObservation observation = new PhysicalObservation(
                new SimulationTime(1ul, 0.01d),
                source[0],
                true,
                source);
            source[0] = Body("barbell", 99f);
            Assert.That(observation.BodyCount, Is.EqualTo(1));
            Assert.That(observation.TryGetBody("barbell", out PhysicalBodyObservation captured), Is.True);
            Assert.That(captured.PositionMeters.X, Is.EqualTo(1f));

            var trace = new AttemptTrace(3);
            trace.BeginRecording();
            trace.Append(observation, Intent(1ul));
            PhysicalObservation second = new PhysicalObservation(
                new SimulationTime(2ul, 0.02d),
                Body("barbell", 2f),
                true);
            trace.Append(second, Intent(2ul));
            Assert.That(trace.Count, Is.EqualTo(2));
            Assert.That(trace.GetSample(0).Observation.TryGetBody("barbell", out PhysicalBodyObservation oldBody), Is.True);
            Assert.That(oldBody.PositionMeters.X, Is.EqualTo(1f));
            Assert.Throws<InvalidOperationException>(() => trace.Append(second, Intent(2ul)));
            Assert.Throws<InvalidOperationException>(() => trace.Append(
                new PhysicalObservation(new SimulationTime(0ul, 0d), Body("barbell", 0f), true),
                Intent(0ul)));
            trace.Append(
                new PhysicalObservation(new SimulationTime(3ul, 0.03d), Body("barbell", 3f), true),
                Intent(3ul));
            Assert.Throws<InvalidOperationException>(() => trace.Append(
                new PhysicalObservation(new SimulationTime(4ul, 0.04d), Body("barbell", 4f), true),
                Intent(4ul)));
            trace.EndRecording();
            Assert.Throws<InvalidOperationException>(() => trace.Append(second, Intent(2ul)));
        }

        private static PhysicalBodyObservation Body(string bodyId, float x)
        {
            return new PhysicalBodyObservation(
                bodyId,
                1f,
                new Vector3Value(x, 0f, 0f),
                QuaternionValue.Identity,
                Vector3Value.Zero,
                Vector3Value.Zero);
        }

        private static PlayerIntentFrame Intent(ulong tick)
        {
            return new PlayerIntentFrame(
                tick,
                tick * SimulationConstants.FixedDeltaTimeSeconds,
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
        }

        private static bool IsFinitePositive(Vector3 value) =>
            value.x > 0f && value.y > 0f && value.z > 0f &&
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}
