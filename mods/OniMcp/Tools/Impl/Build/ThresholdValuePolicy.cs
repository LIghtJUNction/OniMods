using System;

namespace OniMcp.Tools
{
    internal static class ThresholdValuePolicy
    {
        public static float ClampProcessed(
            float processed,
            float inputMin,
            float inputMax,
            float nativeMin,
            float nativeMax)
        {
            if (inputMax > inputMin)
                return Math.Min(inputMax, Math.Max(inputMin, processed));

            return Math.Min(nativeMax, Math.Max(nativeMin, processed));
        }
    }
}
