namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static bool IsConnectionEdit(MapEditCell cell)
        {
            return cell != null
                && TryGetConnectionGlyph(cell.ToToken, out char glyph)
                && IsConnectionGlyph(glyph);
        }

        private static bool TryGetConnectionGlyph(string token, out char glyph)
        {
            glyph = '\0';
            token = (token ?? string.Empty).Trim();
            if (token.Length == 0)
                return false;
            glyph = token[0];
            return IsConnectionGlyph(glyph);
        }

        private static string PrefabForConnectionMap(string sourcePath)
        {
            sourcePath = (sourcePath ?? string.Empty).ToLowerInvariant();
            if (sourcePath.Contains("liquid_conduits"))
                return "LiquidConduit";
            if (sourcePath.Contains("gas_conduits"))
                return "GasConduit";
            if (sourcePath.Contains("logic"))
                return "LogicWire";
            if (sourcePath.Contains("solid_conveyor"))
                return "SolidConduit";
            return "Wire";
        }

    }
}
