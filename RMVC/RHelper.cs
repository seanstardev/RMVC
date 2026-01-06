using System;

namespace RMVC {
    internal static class RHelper 
    {
        internal static double ClampPercent(double d) 
        {
            return Math.Max(0, Math.Min(d, 100));
        }
    }
}
