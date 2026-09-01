using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace PowerliftingSimulator.Equipment
{
    public readonly struct BarbellInventoryEntry
    {
        public BarbellInventoryEntry(float massKilograms, int maximumPairsPerSide)
        {
            MassKilograms = massKilograms;
            MaximumPairsPerSide = maximumPairsPerSide;
        }

        public float MassKilograms { get; }
        public int MaximumPairsPerSide { get; }
    }

    public readonly struct BarbellPlateGeometry
    {
        public BarbellPlateGeometry(float massKilograms, float diameterMeters, float thicknessMeters, Color color, string colorSource)
        {
            MassKilograms = massKilograms;
            DiameterMeters = diameterMeters;
            ThicknessMeters = thicknessMeters;
            Color = color;
            ColorSource = colorSource;
        }

        public float MassKilograms { get; }
        public float DiameterMeters { get; }
        public float ThicknessMeters { get; }
        public Color Color { get; }
        public string ColorSource { get; }
    }

    public sealed class BarbellLoadPlan
    {
        private readonly ReadOnlyCollection<float> _platesPerSide;

        internal BarbellLoadPlan(float requestedTotalMassKg, IList<float> platesPerSide)
        {
            RequestedTotalMassKg = requestedTotalMassKg;
            _platesPerSide = new ReadOnlyCollection<float>(new List<float>(platesPerSide));

            float plateMassPerSide = 0f;
            for (int index = 0; index < _platesPerSide.Count; index++)
                plateMassPerSide += _platesPerSide[index];

            PlateMassPerSideKg = plateMassPerSide;
            TotalMassKg = BarbellPrototypeConfiguration.BaseBarbellMassKg + plateMassPerSide * 2f;
        }

        public float RequestedTotalMassKg { get; }
        public float TotalMassKg { get; }
        public float PlateMassPerSideKg { get; }
        public int PlateCountPerSide => _platesPerSide.Count;
        public IReadOnlyList<float> PlatesPerSideKg => _platesPerSide;
    }

    public readonly struct BarbellMassComponent
    {
        public BarbellMassComponent(
            string id,
            float massKilograms,
            Vector3 positionBarMeters,
            Vector3 dimensionsBarMeters,
            Vector3 principalInertiaKgM2)
        {
            Id = id ?? string.Empty;
            MassKilograms = massKilograms;
            PositionBarMeters = positionBarMeters;
            DimensionsBarMeters = dimensionsBarMeters;
            PrincipalInertiaKgM2 = principalInertiaKgM2;
        }

        public string Id { get; }
        public float MassKilograms { get; }
        public Vector3 PositionBarMeters { get; }
        public Vector3 DimensionsBarMeters { get; }
        public Vector3 PrincipalInertiaKgM2 { get; }
    }

    public sealed class BarbellInertiaModel
    {
        internal BarbellInertiaModel(
            float effectiveDensityKgM3,
            IList<BarbellMassComponent> components,
            Vector3 centerOfMassBarMeters,
            Vector3 inertiaTensorKgM2)
        {
            EffectiveDensityKgM3 = effectiveDensityKgM3;
            Components = new ReadOnlyCollection<BarbellMassComponent>(new List<BarbellMassComponent>(components));
            CenterOfMassBarMeters = centerOfMassBarMeters;
            InertiaTensorKgM2 = inertiaTensorKgM2;
        }

        public float EffectiveDensityKgM3 { get; }
        public IReadOnlyList<BarbellMassComponent> Components { get; }
        public Vector3 CenterOfMassBarMeters { get; }
        public Vector3 InertiaTensorKgM2 { get; }
    }

    public static class BarbellLoadingSolver
    {
        private const int GramsPerKilogram = 1000;
        private const int PlateUnitGrams = 1250;
        private const int BaseBarbellMassGrams = 25000;

        public static BarbellLoadPlan Solve(float requestedTotalMassKg)
        {
            int requestedMassGrams = ToExactMassGrams(requestedTotalMassKg);
            if (requestedMassGrams < BaseBarbellMassGrams)
                throw new ArgumentOutOfRangeException(nameof(requestedTotalMassKg), "A loaded barbell cannot be lighter than the 25 kg base barbell.");

            int totalPlateMassGrams = requestedMassGrams - BaseBarbellMassGrams;
            if (totalPlateMassGrams % (PlateUnitGrams * 2) != 0)
                throw new ArgumentException("The requested load cannot be split into symmetric 1.25 kg plate units.", nameof(requestedTotalMassKg));

            int sideUnits = totalPlateMassGrams / (PlateUnitGrams * 2);
            var platesPerSide = new List<float>();
            IReadOnlyList<BarbellInventoryEntry> inventory = BarbellPrototypeConfiguration.Inventory;

            for (int index = 0; index < inventory.Count; index++)
            {
                BarbellInventoryEntry entry = inventory[index];
                int denominationUnits = (int)Math.Round(entry.MassKilograms * GramsPerKilogram / (double)PlateUnitGrams);
                int plateCount = Math.Min(sideUnits / denominationUnits, entry.MaximumPairsPerSide);
                for (int plateIndex = 0; plateIndex < plateCount; plateIndex++)
                    platesPerSide.Add(entry.MassKilograms);
                sideUnits -= plateCount * denominationUnits;
            }

            if (sideUnits != 0)
                throw new InvalidOperationException("The finite prototype plate inventory cannot solve the requested symmetric load.");

            BarbellLoadPlan plan = new BarbellLoadPlan(requestedTotalMassKg, platesPerSide);
            if (Math.Abs(plan.TotalMassKg - requestedTotalMassKg) > 0.0001f)
                throw new InvalidOperationException("The loading solver produced a non-exact total mass.");
            return plan;
        }

        private static int ToExactMassGrams(float massKilograms)
        {
            if (float.IsNaN(massKilograms) || float.IsInfinity(massKilograms) || massKilograms < 0f)
                throw new ArgumentOutOfRangeException(nameof(massKilograms), "Mass must be finite and non-negative.");

            double grams = massKilograms * GramsPerKilogram;
            if (grams > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(massKilograms), "Mass is outside the prototype solver range.");

            int roundedGrams = (int)Math.Round(grams, MidpointRounding.AwayFromZero);
            if (Math.Abs(grams - roundedGrams) > 0.0001d)
                throw new ArgumentException("Mass must be representable exactly in grams.", nameof(massKilograms));
            return roundedGrams;
        }
    }

    public static class BarbellPrototypeConfiguration
    {
        public const string BodyId = "barbell";
        public const string RulebookTitle = "IPF Technical Rule Book";
        public const string RulebookEffectiveDate = "01 March 2026";
        public const string RulebookVersion = "3";

        public const float OverallLengthMeters = 2.200f;
        public const float CollarFaceSpacingMeters = 1.310f;
        public const float ShaftDiameterMeters = 0.029f;
        public const float SleeveDiameterMeters = 0.050f;
        public const float RingXBarMeters = 0.405f;
        public const float CollarFaceXBarMeters = 0.655f;
        public const float SleeveEndXBarMeters = 1.100f;
        public const float BareBarMassKg = 20f;
        public const float CollarMassEachKg = 2.5f;
        public const float BaseBarbellMassKg = 25f;
        public const float CollarThicknessMeters = 0.040f;
        public const float CollarDiameterMeters = 0.120f;
        public const float PlateStartXBarMeters = 0.705f;
        public const float ContactRestitution = 0.02f;
        public const float ContactDynamicFriction = 0.55f;
        public const float ContactStaticFriction = 0.65f;

        private static readonly ReadOnlyCollection<BarbellInventoryEntry> InventoryValues =
            new ReadOnlyCollection<BarbellInventoryEntry>(new[]
            {
                new BarbellInventoryEntry(25f, 4),
                new BarbellInventoryEntry(20f, 4),
                new BarbellInventoryEntry(15f, 4),
                new BarbellInventoryEntry(10f, 4),
                new BarbellInventoryEntry(5f, 8),
                new BarbellInventoryEntry(2.5f, 8),
                new BarbellInventoryEntry(1.25f, 8)
            });

        public static IReadOnlyList<BarbellInventoryEntry> Inventory => InventoryValues;

        public static BarbellPlateGeometry GetPlateGeometry(float massKg)
        {
            if (Mathf.Abs(massKg - 25f) < 0.0001f)
                return new BarbellPlateGeometry(25f, 0.450f, 0.055f, Color.red, "SOURCE_DIRECT_IPF_2026_V3_COLOR");
            if (Mathf.Abs(massKg - 20f) < 0.0001f)
                return new BarbellPlateGeometry(20f, 0.420f, 0.050f, Color.blue, "SOURCE_DIRECT_IPF_2026_V3_COLOR");
            if (Mathf.Abs(massKg - 15f) < 0.0001f)
                return new BarbellPlateGeometry(15f, 0.370f, 0.030f, Color.yellow, "SOURCE_DIRECT_IPF_2026_V3_COLOR");
            if (Mathf.Abs(massKg - 10f) < 0.0001f)
                return new BarbellPlateGeometry(10f, 0.315f, 0.026f, new Color(0.12f, 0.62f, 0.20f), "PRESENTATION_CALIBRATION");
            if (Mathf.Abs(massKg - 5f) < 0.0001f)
                return new BarbellPlateGeometry(5f, 0.245f, 0.022f, Color.white, "PRESENTATION_CALIBRATION");
            if (Mathf.Abs(massKg - 2.5f) < 0.0001f)
                return new BarbellPlateGeometry(2.5f, 0.210f, 0.018f, new Color(0.16f, 0.17f, 0.19f), "PRESENTATION_CALIBRATION");
            if (Mathf.Abs(massKg - 1.25f) < 0.0001f)
                return new BarbellPlateGeometry(1.25f, 0.180f, 0.012f, new Color(0.65f, 0.70f, 0.74f), "PRESENTATION_CALIBRATION");

            throw new ArgumentOutOfRangeException(nameof(massKg), "The mass is not in the standard prototype plate inventory.");
        }

        public static BarbellInertiaModel ComputeInertia(BarbellLoadPlan plan)
        {
            if (plan == null)
                throw new ArgumentNullException(nameof(plan));

            float shaftLength = CollarFaceSpacingMeters;
            float sleeveLength = SleeveEndXBarMeters - CollarFaceXBarMeters;
            float shaftVolume = CylinderVolume(ShaftDiameterMeters * 0.5f, shaftLength);
            float sleeveVolume = CylinderVolume(SleeveDiameterMeters * 0.5f, sleeveLength);
            float effectiveDensity = BareBarMassKg / (shaftVolume + sleeveVolume * 2f);
            var components = new List<BarbellMassComponent>();

            AddCylinder(components, "shaft", effectiveDensity * shaftVolume, 0f, ShaftDiameterMeters, shaftLength);
            AddCylinder(components, "sleeve_left", effectiveDensity * sleeveVolume, -((CollarFaceXBarMeters + SleeveEndXBarMeters) * 0.5f), SleeveDiameterMeters, sleeveLength);
            AddCylinder(components, "sleeve_right", effectiveDensity * sleeveVolume, (CollarFaceXBarMeters + SleeveEndXBarMeters) * 0.5f, SleeveDiameterMeters, sleeveLength);
            AddCylinder(components, "collar_left", CollarMassEachKg, -(CollarFaceXBarMeters + CollarThicknessMeters * 0.5f), CollarDiameterMeters, CollarThicknessMeters);
            AddCylinder(components, "collar_right", CollarMassEachKg, CollarFaceXBarMeters + CollarThicknessMeters * 0.5f, CollarDiameterMeters, CollarThicknessMeters);

            AddPlateComponents(components, plan, -1f, "left");
            AddPlateComponents(components, plan, 1f, "right");

            float totalMass = 0f;
            Vector3 weightedPosition = Vector3.zero;
            for (int index = 0; index < components.Count; index++)
            {
                BarbellMassComponent component = components[index];
                totalMass += component.MassKilograms;
                weightedPosition += component.PositionBarMeters * component.MassKilograms;
            }

            Vector3 centerOfMass = weightedPosition / totalMass;
            Vector3 inertia = Vector3.zero;
            for (int index = 0; index < components.Count; index++)
            {
                BarbellMassComponent component = components[index];
                Vector3 displacement = component.PositionBarMeters - centerOfMass;
                inertia.x += component.PrincipalInertiaKgM2.x + component.MassKilograms * (displacement.y * displacement.y + displacement.z * displacement.z);
                inertia.y += component.PrincipalInertiaKgM2.y + component.MassKilograms * (displacement.x * displacement.x + displacement.z * displacement.z);
                inertia.z += component.PrincipalInertiaKgM2.z + component.MassKilograms * (displacement.x * displacement.x + displacement.y * displacement.y);
            }

            if (Mathf.Abs(totalMass - plan.TotalMassKg) > 0.001f)
                throw new InvalidOperationException("The component mass model does not equal the loading plan.");
            if (!IsFinitePositive(inertia.x) || !IsFinitePositive(inertia.y) || !IsFinitePositive(inertia.z))
                throw new InvalidOperationException("The compound inertia model must be finite and positive.");

            return new BarbellInertiaModel(effectiveDensity, components, centerOfMass, inertia);
        }

        private static void AddPlateComponents(List<BarbellMassComponent> components, BarbellLoadPlan plan, float side, string sideName)
        {
            float accumulatedThickness = 0f;
            for (int index = 0; index < plan.PlatesPerSideKg.Count; index++)
            {
                float massKg = plan.PlatesPerSideKg[index];
                BarbellPlateGeometry geometry = GetPlateGeometry(massKg);
                float centerX = side * (PlateStartXBarMeters + accumulatedThickness + geometry.ThicknessMeters * 0.5f);
                AddCylinder(components, $"plate_{massKg:0.##}_{sideName}_{index}", massKg, centerX, geometry.DiameterMeters, geometry.ThicknessMeters);
                accumulatedThickness += geometry.ThicknessMeters;
            }
        }

        private static void AddCylinder(List<BarbellMassComponent> components, string id, float massKg, float centerX, float diameter, float length)
        {
            float radius = diameter * 0.5f;
            Vector3 principalInertia = new Vector3(
                0.5f * massKg * radius * radius,
                massKg / 12f * (3f * radius * radius + length * length),
                massKg / 12f * (3f * radius * radius + length * length));
            components.Add(new BarbellMassComponent(
                id,
                massKg,
                new Vector3(centerX, 0f, 0f),
                new Vector3(length, diameter, diameter),
                principalInertia));
        }

        private static float CylinderVolume(float radius, float length) => Mathf.PI * radius * radius * length;

        private static bool IsFinitePositive(float value) => value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
