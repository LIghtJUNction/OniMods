using System.Text;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static void AppendCellNextReads(StringBuilder sb, int x, int y, int cell)
        {
            sb.AppendLine();
            sb.AppendLine("## Next Reads");
            sb.AppendLine("- same cell: `world_editor command=read path=/active/map/cell_" + x + "_" + y + ".md`");
            sb.AppendLine("- local zoom: `" + LocalZoomCall(x, y, "default,power,oxygen,temperature") + "`");

            AppendCellReachabilityRead(sb, x, y, cell);
            AppendCellPortReads(sb, x, y, cell);
            AppendCellInfrastructureReads(sb, x, y, cell);
            AppendCellThermalReads(sb, x, y, cell);
            AppendCellOperationHints(sb, x, y, cell);
        }

        private static void AppendCellReachabilityRead(StringBuilder sb, int x, int y, int cell)
        {
            if (!CellLikelyNeedsReachability(cell))
                return;

            sb.AppendLine("- reachability: `world_editor command=read path=/active/dupes/reachability.md x="
                + x + " y=" + y + " radius=12 sampleLimit=12` before rescue/dig/build/sweep work.");
        }

        private static bool CellLikelyNeedsReachability(int cell)
        {
            if (Grid.Objects[cell, (int)ObjectLayer.Building] != null)
                return true;
            if (BuildCritterCellMap().ContainsKey(cell))
                return true;
            if (HasPickupAtCell(cell))
                return true;

            Element element = Grid.Element[cell];
            return element != null && (element.IsSolid || element.IsLiquid);
        }

        private static void AppendCellPortReads(StringBuilder sb, int x, int y, int cell)
        {
            GameObject building = Grid.Objects[cell, (int)ObjectLayer.Building];
            if (building == null)
                return;

            sb.AppendLine("- nearby ports: `read_control domain=infrastructure action=nearby_ports x="
                + x + " y=" + y + " radius=8 kind=all`");
            sb.AppendLine("- port workflow: read nearby_ports -> inspect this cell Ports / Interfaces -> confirm matching line in infrastructure view.");
        }

        private static void AppendCellInfrastructureReads(StringBuilder sb, int x, int y, int cell)
        {
            bool anyLine = false;
            if (HasLayer(cell, PowerLayers))
            {
                anyLine = true;
                sb.AppendLine("- power line: `" + LocalZoomCall(x, y, "power")
                    + "` then `/active/infrastructure/power.md` for dirs/links/to/circuit/bridgePorts.");
            }
            if (HasLayer(cell, LiquidLayers))
            {
                anyLine = true;
                sb.AppendLine("- liquid pipe: `" + LocalZoomCall(x, y, "liquid")
                    + "` then `/active/infrastructure/liquid_conduits.md` for dirs/links/to/bridgePorts.");
            }
            if (HasLayer(cell, GasLayers))
            {
                anyLine = true;
                sb.AppendLine("- gas pipe: `" + LocalZoomCall(x, y, "gas")
                    + "` then `/active/infrastructure/gas_conduits.md` for dirs/links/to/bridgePorts.");
            }
            if (HasLayer(cell, LogicLayers))
            {
                anyLine = true;
                sb.AppendLine("- logic wire: `" + LocalZoomCall(x, y, "logic")
                    + "` then `/active/infrastructure/logic.md` for logicIn/logicOut and bridgePorts.");
            }
            if (HasLayer(cell, ConveyorLayers))
            {
                anyLine = true;
                sb.AppendLine("- conveyor rail: `" + LocalZoomCall(x, y, "conveyor")
                    + "` then `/active/infrastructure/solid_conveyor.md` for loader/receptacle anchors.");
            }
            if (anyLine)
                sb.AppendLine("- line workflow: trust `links/to` over visual alignment; `bridgePorts` means bridge jump, not direct same-layer link.");
        }

        private static void AppendCellThermalReads(StringBuilder sb, int x, int y, int cell)
        {
            float tempC = Grid.Temperature[cell] - 273.15f;
            if (tempC >= 10f && tempC <= 37f)
                return;

            sb.AppendLine("- temperature: `" + LocalZoomCall(x, y, "temperature")
                + "` comfort=10~37C, current=" + tempC.ToString("F1") + "C");
            sb.AppendLine("- temperature detail: `read_control domain=world action=cell_info x="
                + x + " y=" + y + " includeTemperature=true marginC=15 limit=20`");
        }

        private static void AppendCellOperationHints(StringBuilder sb, int x, int y, int cell)
        {
            string at = "@(" + x + "," + y + ")";
            GameObject building = Grid.Objects[cell, (int)ObjectLayer.Building];
            if (building != null)
            {
                sb.AppendLine("- deconstruct preview: `拆 建筑" + at + ":7 dryRun=true`");
                sb.AppendLine("- cancel preview: `消 建筑" + at + ":6 dryRun=true`");
            }

            if (BuildCritterCellMap().ContainsKey(cell))
            {
                sb.AppendLine("- capture preview: `捕 小动物" + at + ":7 dryRun=true`");
                sb.AppendLine("- attack preview: `杀 小动物" + at + ":7 dryRun=true`");
            }

            Element element = Grid.Element[cell];
            if (element != null && element.IsSolid)
                sb.AppendLine("- dig preview: `挖 " + at + ":7 dryRun=true`");
            if (element != null && element.IsLiquid)
                sb.AppendLine("- mop preview: `擦 " + at + ":6 dryRun=true`");
            if (HasPickupAtCell(cell))
                sb.AppendLine("- sweep preview: `扫 " + at + ":6 dryRun=true`");
        }

        private static bool HasPickupAtCell(int cell)
        {
            foreach (var pickupable in Components.Pickupables.Items)
            {
                if (pickupable != null
                    && pickupable.gameObject != null
                    && Grid.PosToCell(pickupable.gameObject) == cell)
                    return true;
            }
            return false;
        }

        private static string LocalZoomCall(int x, int y, string views)
        {
            int x1 = Mathf.Max(0, x - 2);
            int y1 = Mathf.Max(0, y - 2);
            int x2 = Mathf.Min(Grid.WidthInCells - 1, x + 2);
            int y2 = Mathf.Min(Grid.HeightInCells - 1, y + 2);
            return "world_editor command=zoom x1=" + x1 + " y1=" + y1
                + " x2=" + x2 + " y2=" + y2 + " views=" + views + " compact=true";
        }
    }
}
