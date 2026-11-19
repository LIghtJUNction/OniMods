using System;

namespace OniMcp
{
    // Pixel placement shared by the two screen axes; the world coordinate stays unchanged.
    internal static class CoordinateLabelPlacement
    {
        internal static bool TryPlace(float position, float edge, float viewportLength,
            float viewportDepth, float labelLength, float labelDepth, out float along, out float across)
        {
            along = across = 0f;
            if (float.IsNaN(position) || float.IsInfinity(position)
                || float.IsNaN(edge) || float.IsInfinity(edge)
                || labelLength <= 0f || labelDepth <= 0f
                || viewportLength < labelLength || viewportDepth < labelDepth
                || position < 0f || position > viewportLength)
                return false;

            along = Inside(position - labelLength * 0.5f, labelLength, viewportLength);
            across = Inside(edge, labelDepth, viewportDepth);
            return true;
        }

        private static float Inside(float start, float size, float extent)
        {
            float margin = Math.Min(4f, (extent - size) * 0.5f);
            return Math.Max(margin, Math.Min(start, extent - size - margin));
        }
    }
}
