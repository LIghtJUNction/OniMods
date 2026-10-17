namespace OniMcp.Tools
{
    internal static class MapTextReadPolicy
    {
        internal static bool Inside(int x, int y, int width, int height)
        {
            return x >= 0 && y >= 0 && x < width && y < height;
        }

        internal static bool CanRead(bool valid, bool visible, int cellWorld, int activeWorld)
        {
            return valid && visible && activeWorld >= 0 && cellWorld == activeWorld;
        }

        // Bits are U=8, D=4, L=2, R=1. Unknown is not a disconnected segment.
        internal static char ConnectionGlyph(bool known, int mask)
        {
            if (!known || mask < 0 || mask > 15)
                return '?';
            return "*→←─↓┌┐┬↑└┘┴│├┤┼"[mask];
        }
    }
}
