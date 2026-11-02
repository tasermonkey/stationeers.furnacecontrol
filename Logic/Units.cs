using System;
using System.Globalization;

namespace FurnaceControlMod.Logic
{
    public static class Units
    {
        public const double NoLimit = 99999;

        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        public static string Kelvin(double kelvin) =>
            kelvin < 0 ? "-" : Math.Round(kelvin).ToString("0", Invariant) + " K";

        public static string Pressure(double kPa)
        {
            if (kPa < 0) return "-";
            return kPa < 1000
                ? Math.Round(kPa).ToString("0", Invariant) + " kPa"
                : (kPa / 1000).ToString("0.##", Invariant) + " MPa";
        }

        public static string KelvinRange(double min, double max) =>
            max >= NoLimit ? $"{Kelvin(min)} and up" : $"{min.ToString("0", Invariant)}-{Kelvin(max)}";

        public static string PressureRange(double min, double max) =>
            max >= NoLimit ? $"{Pressure(min)} and up" : $"{Pressure(min)}-{Pressure(max)}";

        public static string Reagent(double quantity) =>
            Math.Abs(quantity - Math.Round(quantity)) < 0.05
                ? Math.Round(quantity).ToString("0", Invariant)
                : quantity.ToString("0.0", Invariant);
    }
}
