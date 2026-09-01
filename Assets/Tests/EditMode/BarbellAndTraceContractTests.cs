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
        public void BAR_LOADED_LAYOUT_PRESERVES_ORDER_SYMMETRY_AND_SLEEVE_CLEARANCE()
        {
            BarbellLoadPlan light = BarbellLoadingSolver.Solve(25f);
            AssertSideLayout(light, -0.705f, -0.705f, -0.725f, -0.745f, 0.355f, Array.Empty<float>());
            AssertSideLayout(light, 0.705f, 0.705f, 0.725f, 0.745f, 0.355f, Array.Empty<float>());

            BarbellLoadPlan medium = BarbellLoadingSolver.Solve(105f);
            AssertSideLayout(medium, -0.705f, -0.790f, -0.810f, -0.830f, 0.270f, new[] { -0.7325f, -0.775f });
            AssertSideLayout(medium, 0.705f, 0.790f, 0.810f, 0.830f, 0.270f, new[] { 0.7325f, 0.775f });

            BarbellLoadPlan heavy = BarbellLoadingSolver.Solve(205f);
            AssertSideLayout(heavy, -0.705f, -0.900f, -0.920f, -0.940f, 0.160f, new[] { -0.7325f, -0.7875f, -0.8425f, -0.885f });
            AssertSideLayout(heavy, 0.705f, 0.900f, 0.920f, 0.940f, 0.160f, new[] { 0.7325f, 0.7875f, 0.8425f, 0.885f });

            Assert.That(Mathf.Abs(light.Layout.Left.RemovableCollarCenterXBarMeters), Is.LessThan(Mathf.Abs(medium.Layout.Left.RemovableCollarCenterXBarMeters)));
            Assert.That(Mathf.Abs(medium.Layout.Left.RemovableCollarCenterXBarMeters), Is.LessThan(Mathf.Abs(heavy.Layout.Left.RemovableCollarCenterXBarMeters)));
            Assert.That(light.Layout.Left.RemovableCollarCenterXBarMeters, Is.EqualTo(-light.Layout.Right.RemovableCollarCenterXBarMeters).Within(0.0001f));
            Assert.That(medium.Layout.Left.RemovableCollarCenterXBarMeters, Is.EqualTo(-medium.Layout.Right.RemovableCollarCenterXBarMeters).Within(0.0001f));
            Assert.That(heavy.Layout.Left.RemovableCollarCenterXBarMeters, Is.EqualTo(-heavy.Layout.Right.RemovableCollarCenterXBarMeters).Within(0.0001f));
        }

        [Test]
        public void BAR_LOADING_REJECTS_FINITE_INVENTORY_SLEEVE_OVERFLOW()
        {
            float maximumFiniteInventoryLoadKg = BarbellPrototypeConfiguration.BaseBarbellMassKg;
            for (int index = 0; index < BarbellPrototypeConfiguration.Inventory.Count; index++)
            {
                BarbellInventoryEntry entry = BarbellPrototypeConfiguration.Inventory[index];
                maximumFiniteInventoryLoadKg += entry.MassKilograms * entry.MaximumPairsPerSide * 2f;
            }

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => BarbellLoadingSolver.Solve(maximumFiniteInventoryLoadKg));
            Assert.That(exception.Message, Does.Contain("sleeve"));
        }

        [Test]
        public void BAR_CORRECTED_COMPOUND_INERTIA_USES_LOAD_DEPENDENT_COLLAR_POSITIONS()
        {
            BarbellLoadPlan mediumPlan = BarbellLoadingSolver.Solve(105f);
            BarbellLoadPlan heavyPlan = BarbellLoadingSolver.Solve(205f);
            BarbellInertiaModel medium = BarbellPrototypeConfiguration.ComputeInertia(mediumPlan);
            BarbellInertiaModel heavy = BarbellPrototypeConfiguration.ComputeInertia(heavyPlan);

            BarbellMassComponent mediumLeftCollar = FindComponent(medium, "collar_left");
            BarbellMassComponent mediumRightCollar = FindComponent(medium, "collar_right");
            BarbellMassComponent heavyLeftCollar = FindComponent(heavy, "collar_left");
            BarbellMassComponent heavyRightCollar = FindComponent(heavy, "collar_right");
            Assert.That(mediumLeftCollar.MassKilograms, Is.EqualTo(BarbellPrototypeConfiguration.CollarMassEachKg).Within(0.0001f));
            Assert.That(mediumRightCollar.MassKilograms, Is.EqualTo(BarbellPrototypeConfiguration.CollarMassEachKg).Within(0.0001f));
            Assert.That(heavyLeftCollar.MassKilograms, Is.EqualTo(BarbellPrototypeConfiguration.CollarMassEachKg).Within(0.0001f));
            Assert.That(heavyRightCollar.MassKilograms, Is.EqualTo(BarbellPrototypeConfiguration.CollarMassEachKg).Within(0.0001f));
            Assert.That(mediumLeftCollar.PositionBarMeters.x, Is.EqualTo(mediumPlan.Layout.Left.RemovableCollarCenterXBarMeters).Within(0.0001f));
            Assert.That(mediumRightCollar.PositionBarMeters.x, Is.EqualTo(mediumPlan.Layout.Right.RemovableCollarCenterXBarMeters).Within(0.0001f));
            Assert.That(heavyLeftCollar.PositionBarMeters.x, Is.EqualTo(heavyPlan.Layout.Left.RemovableCollarCenterXBarMeters).Within(0.0001f));
            Assert.That(heavyRightCollar.PositionBarMeters.x, Is.EqualTo(heavyPlan.Layout.Right.RemovableCollarCenterXBarMeters).Within(0.0001f));
            Assert.That(Mathf.Abs(heavyLeftCollar.PositionBarMeters.x), Is.GreaterThan(Mathf.Abs(mediumLeftCollar.PositionBarMeters.x)));
            Assert.That(IsFinitePositive(medium.InertiaTensorKgM2), Is.True);
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

        private static void AssertSideLayout(
            BarbellLoadPlan plan,
            float expectedPlateStart,
            float expectedPlateStackOuterFace,
            float expectedCollarCenter,
            float expectedCollarOuterFace,
            float expectedClearance,
            float[] expectedPlateCenters)
        {
            BarbellSideLayout side = expectedPlateStart < 0f ? plan.Layout.Left : plan.Layout.Right;
            Assert.That(side.PlateStartXBarMeters, Is.EqualTo(expectedPlateStart).Within(0.0001f));
            Assert.That(side.PlateStackOuterFaceXBarMeters, Is.EqualTo(expectedPlateStackOuterFace).Within(0.0001f));
            Assert.That(side.RemovableCollarCenterXBarMeters, Is.EqualTo(expectedCollarCenter).Within(0.0001f));
            Assert.That(side.RemovableCollarOuterFaceXBarMeters, Is.EqualTo(expectedCollarOuterFace).Within(0.0001f));
            Assert.That(side.RemainingSleeveClearanceMeters, Is.EqualTo(expectedClearance).Within(0.0001f));
            Assert.That(Mathf.Abs(side.RemovableCollarOuterFaceXBarMeters), Is.LessThanOrEqualTo(BarbellPrototypeConfiguration.SleeveEndXBarMeters));
            Assert.That(side.PlatePlacements.Count, Is.EqualTo(expectedPlateCenters.Length));
            for (int index = 0; index < expectedPlateCenters.Length; index++)
            {
                Assert.That(side.PlatePlacements[index].CenterXBarMeters, Is.EqualTo(expectedPlateCenters[index]).Within(0.0001f));
                Assert.That(side.PlatePlacements[index].MassKilograms, Is.EqualTo(plan.PlatesPerSideKg[index]).Within(0.0001f));
            }
        }

        private static BarbellMassComponent FindComponent(BarbellInertiaModel model, string id)
        {
            for (int index = 0; index < model.Components.Count; index++)
            {
                if (model.Components[index].Id == id)
                    return model.Components[index];
            }

            Assert.Fail("Missing inertia component: " + id);
            return default;
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
