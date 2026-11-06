using System;

namespace OniMcp.Tools
{
    internal static class CameraFramingPolicy
    {
        internal static string ResolveMode(string requested)
        {
            switch ((requested ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "detail":
                case "local":
                case "close":
                    return "detail";
                case "overview":
                case "global":
                case "wide":
                    return "overview";
                default:
                    return "fit";
            }
        }

        internal static float ForBounds(int width, int height, float aspect, float padding,
            string mode, float minimum, float maximum)
        {
            float size = Math.Max(height * 0.5f + padding,
                (width * 0.5f + padding) / Math.Max(0.5f, aspect));
            if (mode == "detail")
                size *= 0.72f;
            else if (mode == "overview")
                size *= 1.28f;
            return Math.Max(minimum, Math.Min(size, Math.Max(minimum, maximum)));
        }
    }
}
