using System;

namespace OniMcp.Tools
{
    internal static class ThresholdValuePolicy
    {
        public static float ClampProcessedToNativeRange(
            float processed,
            float nativeMin,
            float nativeMax)
        {
            return Math.Min(nativeMax, Math.Max(nativeMin, processed));
        }
    }
}
