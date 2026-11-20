using System;

namespace PowerliftingSimulator.Deadlift
{
    /// <summary>
    /// Closed-form mass properties of the 75 kg deadlift bar, recomputed here
    /// from the IPF dimensions rather than read from the production inertia
    /// model: a uniform-density 20 kg steel bar (29 mm shaft, 50 mm sleeves),
    /// two 2.5 kg collars and two 25 kg plates, symmetric about the centre.
    /// </summary>
    public static class DeadliftBarReference
    {
        public const double PlateRadius = 0.225;
        public const double PlateThickness = 0.055;
        public const double CollarHalfSize = 0.060;
        public const double TotalMassKg = 75.0;

        /// <summary>Principal moments about the centre of mass: x is the bar axis, y and z are transverse.</summary>
        public static void Inertia(out double ix, out double iy)
        {
            const double shaftR = 0.029 / 2, shaftL = 1.310, sleeveR = 0.050 / 2, sleeveL = 1.100 - 0.655;
            double shaftV = Math.PI * shaftR * shaftR * shaftL;
            double sleeveV = Math.PI * sleeveR * sleeveR * sleeveL;
            double rho = 20.0 / (shaftV + 2 * sleeveV);
            double mShaft = rho * shaftV, mSleeve = rho * sleeveV;
            double xSleeve = (0.655 + 1.100) / 2;
            double xCollar = 0.705 + PlateThickness + 0.020, xPlate = 0.705 + PlateThickness / 2;

            ix = Axial(mShaft, shaftR) + 2 * Axial(mSleeve, sleeveR) + 2 * Axial(2.5, CollarHalfSize) + 2 * Axial(25, PlateRadius);
            iy = Transverse(mShaft, shaftR, shaftL, 0) + 2 * Transverse(mSleeve, sleeveR, sleeveL, xSleeve) +
                2 * Transverse(2.5, CollarHalfSize, 0.040, xCollar) + 2 * Transverse(25, PlateRadius, PlateThickness, xPlate);
        }

        private static double Axial(double m, double r) => 0.5 * m * r * r;

        private static double Transverse(double m, double r, double length, double x) =>
            m / 12 * (3 * r * r + length * length) + m * x * x;

        /// <summary>Apothem of a regular N-gon inscribed in the 225 mm plate radius.</summary>
        public static double Apothem(int sides) => PlateRadius * Math.Cos(Math.PI / sides);
    }
}
