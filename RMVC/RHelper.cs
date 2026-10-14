using System;

namespace RMVC
{
    internal static class RHelper
    {
        public static double ClampPercent(double percent)
        {
            if (double.IsNaN(percent) || double.IsInfinity(percent))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(percent),
                    percent,
                    "Progress percentages and caps must be finite.");
            }

            return Math.Max(0, Math.Min(percent, 100));
        }
    }
}
