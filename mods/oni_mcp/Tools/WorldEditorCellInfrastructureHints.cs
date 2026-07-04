using System.Text;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static void AppendCellInfrastructureDetailHint(StringBuilder sb, int cell)
        {
            bool hasLine = HasLayer(cell, PowerLayers)
                || HasLayer(cell, LiquidLayers)
                || HasLayer(cell, GasLayers)
                || HasLayer(cell, LogicLayers)
                || HasLayer(cell, ConveyorLayers);

            string bridge = BridgeText(cell, default(HashedString));
            if (!hasLine && string.IsNullOrEmpty(bridge))
                return;

            sb.AppendLine("- 读法: glyph=本格上下左右连接, dirs=已连方向, open=邻格有线但本格未连, to=已连接邻格坐标");
            sb.AppendLine("- 端口: `⊗`=输入/耗电/入口, `⊙`=输出/发电/出口, `⌒`=桥或跨线锚点；端口坐标以 Endpoint 行为准");
            sb.AppendLine("- 渐进: 若 `open` 非 `.` 或 glyph=`*`, 先读局部 zoom 与相邻 `/active/map/cell_X_Y.md`, 再下连接/拆除/重建命令");
        }
    }
}
