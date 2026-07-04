using System.Text;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static void AppendInfrastructureReadHints(StringBuilder sb, HashedString activeMode)
        {
            if (!IsInfrastructureOverlayMode(activeMode))
                return;

            sb.AppendLine();
            sb.AppendLine("## Infrastructure Reading Hints");
            sb.AppendLine("- The first connection glyph in a token describes that cell's real network edges: up/down/left/right.");
            sb.AppendLine("- `*` means this cell has infrastructure but no detected neighbor edge; treat it as isolated or broken until verified.");
            sb.AppendLine("- `⌒`, `⊗`, `⊙`, and `⊗⊙` are prefixes on the same anchor token, not separate cells.");
            sb.AppendLine("- Bridge tokens keep both facts: `⌒xxx` marks a bridge/crossing anchor, while the leading line glyph still marks this cell's visible network edge.");
            sb.AppendLine("- Repair: use `open=DIR:(x,y)` as the missing-edge candidate; a lone `*` without open neighbors needs port/cell detail first.");
            sb.AppendLine("- Bridge: `bridgePorts=from:(x,y) via:⌒ to:(x,y)` is a jump through the bridge, not direct neighbor continuity.");
            sb.AppendLine("- For exact ports, bridge endpoints, pickupables, temperature, and all overlapping objects, read `/active/map/cell_X_Y.md` for the suspicious cell.");
        }
    }
}
