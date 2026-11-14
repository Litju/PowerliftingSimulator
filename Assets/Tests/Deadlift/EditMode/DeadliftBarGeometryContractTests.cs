using System;
using NUnit.Framework;
using PowerliftingSimulator.Equipment;
using UnityEngine;

namespace PowerliftingSimulator.Deadlift
{
    /// <summary>
    /// GAM-70 Gate A1, pure contracts (no physics engine). The deadlift bar is
    /// the lightest configuration whose plates are full IPF diameter: a 25 kg
    /// plate per side. These tests recompute mass, centre of mass, inertia and
    /// plate placement from first principles instead of calling the production
    /// inertia model, then compare. Rule source: IPF Technical Rulebook 2026
    /// v3 equipment section (largest plate 450 mm, 25 kg plates thinner than
    /// 60 mm, bar 20 kg + collars 2 x 2.5 kg).
    /// </summary>
    public sealed class DeadliftBarGeometryContractTests
    {
        public const float DeadliftLoadKg = 75f;
        private const float PlateRadius = 0.225f;

        [Test]
        public void SEVENTY_FIVE_KG_IS_THE_LIGHTEST_LOAD_WITH_FULL_DIAMETER_PLATES()
        {
            for (float kg = 25f; kg < DeadliftLoadKg; kg += 2.5f)
            {
                BarbellLoadPlan plan = BarbellLoadingSolver.Solve(kg);
                float largest = 0f;
                foreach (float plate in plan.PlatesPerSideKg)
                    largest = Mathf.Max(largest, BarbellPrototypeConfiguration.GetPlateGeometry(plate).DiameterMeters);
                Assert.That(largest, Is.LessThan(0.450f - 1e-6f), $"{kg} kg already has 450 mm plates");
            }

            BarbellLoadPlan deadlift = BarbellLoadingSolver.Solve(DeadliftLoadKg);
            Assert.That(deadlift.PlatesPerSideKg, Is.EqualTo(new[] { 25f }));
            Assert.That(deadlift.TotalMassKg, Is.EqualTo(75f).Within(1e-4f));
            Assert.That(BarbellPrototypeConfiguration.GetPlateGeometry(25f).DiameterMeters, Is.EqualTo(0.450f));
            Assert.That(BarbellPrototypeConfiguration.GetPlateGeometry(25f).ThicknessMeters, Is.LessThanOrEqualTo(0.060f));
        }

        [Test]
        public void PLATE_PLACEMENT_IS_SYMMETRIC_ON_THE_SLEEVES_AND_AXIS_HEIGHT_IS_PLATE_RADIUS()
        {
            BarbellLoadPlan plan = BarbellLoadingSolver.Solve(DeadliftLoadKg);
            BarbellPlatePlacement left = plan.Layout.Left.PlatePlacements[0];
            BarbellPlatePlacement right = plan.Layout.Right.PlatePlacements[0];
            Assert.That(right.CenterXBarMeters, Is.EqualTo(0.705f + 0.0275f).Within(1e-6f));
            Assert.That(left.CenterXBarMeters, Is.EqualTo(-right.CenterXBarMeters).Within(1e-6f));
            Assert.That(right.InnerFaceXBarMeters, Is.EqualTo(0.705f).Within(1e-6f));
            Assert.That(right.OuterFaceXBarMeters, Is.EqualTo(0.760f).Within(1e-6f));
            Assert.That(plan.Layout.Right.RemovableCollarOuterFaceXBarMeters, Is.EqualTo(0.800f).Within(1e-6f));
            Assert.That(plan.Layout.Right.RemainingSleeveClearanceMeters, Is.GreaterThan(0.29f));
            // A bar resting on 450 mm plates has its axis one plate radius above the platform.
            Assert.That(BarbellPrototypeConfiguration.GetPlateGeometry(25f).DiameterMeters * 0.5f, Is.EqualTo(PlateRadius));
        }

        [Test]
        public void MASS_COM_AND_INERTIA_MATCH_AN_INDEPENDENT_CLOSED_FORM_RECOMPUTATION()
        {
            BarbellLoadPlan plan = BarbellLoadingSolver.Solve(DeadliftLoadKg);
            BarbellInertiaModel model = BarbellPrototypeConfiguration.ComputeInertia(plan);

            // Independent recomputation: uniform-density steel shaft + sleeves
            // (20 kg bare bar), two 2.5 kg collar cylinders, two 25 kg plates.
            const double shaftR = 0.029 / 2, shaftL = 1.310, sleeveR = 0.050 / 2, sleeveL = 1.100 - 0.655;
            double shaftV = Math.PI * shaftR * shaftR * shaftL;
            double sleeveV = Math.PI * sleeveR * sleeveR * sleeveL;
            double rho = 20.0 / (shaftV + 2 * sleeveV);
            double mShaft = rho * shaftV, mSleeve = rho * sleeveV;
            double xSleeve = (0.655 + 1.100) / 2;
            double xCollar = 0.705 + 0.055 + 0.020, xPlate = 0.705 + 0.0275;

            double Ix(double m, double r) => 0.5 * m * r * r;
            double Iperp(double m, double r, double len, double x) => m / 12 * (3 * r * r + len * len) + m * x * x;

            double ix = Ix(mShaft, shaftR) + 2 * Ix(mSleeve, sleeveR) + 2 * Ix(2.5, 0.060) + 2 * Ix(25, PlateRadius);
            double iy = Iperp(mShaft, shaftR, shaftL, 0) + 2 * Iperp(mSleeve, sleeveR, sleeveL, xSleeve) +
                2 * Iperp(2.5, 0.060, 0.040, xCollar) + 2 * Iperp(25, PlateRadius, 0.055, xPlate);

            Assert.That(mShaft + 2 * mSleeve + 2 * 2.5 + 2 * 25, Is.EqualTo(75.0).Within(1e-9));
            Assert.That(model.CenterOfMassBarMeters.magnitude, Is.LessThan(1e-6f), "symmetric load: COM on the bar centre");
            Assert.That(model.InertiaTensorKgM2.x, Is.EqualTo((float)ix).Within(1e-4f * (float)ix));
            Assert.That(model.InertiaTensorKgM2.y, Is.EqualTo((float)iy).Within(1e-4f * (float)iy));
            Assert.That(model.InertiaTensorKgM2.z, Is.EqualTo((float)iy).Within(1e-4f * (float)iy));
            Assert.That(model.InertiaTensorKgM2.x, Is.LessThan(model.InertiaTensorKgM2.y), "a bar spins easier about its own axis");
        }

        [Test]
        public void THE_25_KG_NO_PLATE_CONTROL_HAS_NO_PLATES_AND_ITS_LOWEST_ELEMENT_IS_THE_COLLAR()
        {
            BarbellLoadPlan control = BarbellLoadingSolver.Solve(25f);
            Assert.That(control.PlateCountPerSide, Is.Zero);
            Assert.That(control.TotalMassKg, Is.EqualTo(25f));
            // Largest radial extent of anything that is not a plate: the removable collar (120 mm square box collider).
            Assert.That(BarbellPrototypeConfiguration.CollarDiameterMeters * 0.5f, Is.EqualTo(0.060f));
            Assert.That(BarbellPrototypeConfiguration.SleeveDiameterMeters * 0.5f, Is.LessThan(0.060f));
        }
    }
}
