using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static void AppendLegend(StringBuilder sb, Dictionary<char, string> legend, HashedString activeMode, bool compact)
        {
            if (compact)
            {
                sb.AppendLine("Glyph meanings: `world_editor command=symbols` or `/active/symbols/glyphs.md` (use view/query to filter).");
                return;
            }

            sb.AppendLine("## Legend");
            foreach (var item in legend.OrderBy(kv => kv.Key))
                sb.AppendLine(IsConnectionGlyph(item.Key) ? "- `" + item.Key + "` : 连接 (Connection) | " + item.Value : FormatLegendLine(item.Key, item.Value));
            AppendInfrastructureLegend(sb, activeMode);
            // Detailed virtual-file help is available on demand, not repeated per map.
            sb.AppendLine();
        }

        private static void AppendInfrastructureLegend(StringBuilder sb, HashedString activeMode)
        {
            if (!IsInfrastructureOverlayMode(activeMode))
                return;

            sb.AppendLine("- `⌒xxx` : 桥/跨线建筑锚点，桥本体与下方线路可同时显示");
            sb.AppendLine("- `⊗xxx` : 输入端口锚点；电力=耗电端，管道/轨道=入口，信号=输入");
            sb.AppendLine("- `⊙xxx` : 输出端口锚点；电力=发电端，管道/轨道=出口，信号=输出");
            sb.AppendLine("- `⊗⊙xxx` : 同一判定格同时是输入和输出端口");
            sb.AppendLine("- `*xxx` : 该格存在未连接/孤立线路，同时保留建筑、生物或复制人锚点");
        }
    }
}
