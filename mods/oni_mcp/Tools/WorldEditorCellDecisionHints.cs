using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static void AppendCellDecisionHints(StringBuilder sb, int x, int y, int cell)
        {
            sb.AppendLine();
            sb.AppendLine("## Decision Hints");

            bool wrote = false;
            wrote |= AppendThermalDecisionHint(sb, x, y, cell);
            wrote |= AppendOrderDecisionHints(sb, x, y, cell);
            wrote |= AppendPortDecisionHint(sb, x, y, cell);
            wrote |= AppendNetworkDecisionHint(sb, x, y, cell);

            if (!wrote)
                sb.AppendLine("- no immediate order/network/thermal hint; use Next Reads for local context.");
        }

        private static bool AppendThermalDecisionHint(StringBuilder sb, int x, int y, int cell)
        {
            float tempC = Grid.Temperature[cell] - 273.15f;
            if (tempC >= 10f && tempC <= 37f)
                return false;

            string level = tempC > 37f ? "hot" : "cold";
            sb.AppendLine("- thermal: " + level + " " + tempC.ToString("F1")
                + "C; read `" + LocalZoomCall(x, y, "temperature") + "` before assigning long work here.");
            return true;
        }

        private static bool AppendOrderDecisionHints(StringBuilder sb, int x, int y, int cell)
        {
            bool wrote = false;
            string at = "@(" + x + "," + y + ")";
            Element element = Grid.Element[cell];

            if (element != null && element.IsSolid)
            {
                sb.AppendLine("- order: solid tile; preview `挖 " + at + ":7 dryRun=true`.");
                wrote = true;
            }

            if (element != null && element.IsLiquid)
            {
                sb.AppendLine("- order: liquid here; preview `擦 " + at + ":6 dryRun=true`.");
                wrote = true;
            }

            var pickups = Components.Pickupables.Items
                .Where(item => item != null && item.gameObject != null && Grid.PosToCell(item.gameObject) == cell)
                .ToList();
            if (pickups.Count > 0)
            {
                float mass = pickups.Sum(item => item.PrimaryElement != null ? item.PrimaryElement.Mass : 0f);
                sb.AppendLine("- order: " + pickups.Count + " pickup(s), " + mass.ToString("F2")
                    + " kg; preview `扫 " + at + ":6 dryRun=true`.");
                wrote = true;
            }

            return wrote;
        }

        private static bool AppendPortDecisionHint(StringBuilder sb, int x, int y, int cell)
        {
            GameObject go = Grid.Objects[cell, (int)ObjectLayer.Building];
            Building building = go != null ? go.GetComponent<Building>() : null;
            if (go == null || building == null || building.Def == null)
                return false;

            var missing = new List<string>();
            var connected = new List<string>();
            AppendBuildingPortHints(go, building, missing, connected);
            if (missing.Count == 0 && connected.Count == 0)
                return false;

            if (missing.Count > 0)
                sb.AppendLine("- ports: missing line(s) " + string.Join(", ", missing.Take(6).ToArray())
                    + "; inspect `" + LocalZoomCall(x, y, "power,liquid,gas,logic,conveyor") + "`.");
            if (connected.Count > 0)
                sb.AppendLine("- ports: connected " + string.Join(", ", connected.Take(6).ToArray()) + ".");
            return true;
        }

        private static void AppendBuildingPortHints(GameObject go, Building building, List<string> missing, List<string> connected)
        {
            if (building.Def.RequiresPowerInput)
                AddPortHint("powerIn", building.GetPowerInputCell(), PowerLayers, missing, connected);
            if (building.Def.RequiresPowerOutput)
                AddPortHint("powerOut", building.GetPowerOutputCell(), PowerLayers, missing, connected);

            foreach (var consumer in go.GetComponents<ConduitConsumer>())
                AddPortHint(consumer.ConduitType == ConduitType.Gas ? "gasIn" : "liquidIn",
                    building.GetUtilityInputCell(), consumer.ConduitType == ConduitType.Gas ? GasLayers : LiquidLayers, missing, connected);
            foreach (var dispenser in go.GetComponents<ConduitDispenser>())
                AddPortHint(dispenser.ConduitType == ConduitType.Gas ? "gasOut" : "liquidOut",
                    building.GetUtilityOutputCell(), dispenser.ConduitType == ConduitType.Gas ? GasLayers : LiquidLayers, missing, connected);

            LogicPorts logic = go.GetComponent<LogicPorts>();
            if (logic != null)
            {
                if (LogicPortReadSemantics.TryBridgeRoute(go, out int from, out int to))
                {
                    AddPortHint("logicIn", from, LogicLayers, missing, connected, LogicPortReadSemantics.ConnectedAtCell(from));
                    AddPortHint("logicOut", to, LogicLayers, missing, connected, LogicPortReadSemantics.ConnectedAtCell(to));
                }
                else
                {
                    AppendLogicPortHints(logic, logic.inputPortInfo, "logicIn", missing, connected);
                    AppendLogicPortHints(logic, logic.outputPortInfo, "logicOut", missing, connected);
                }
            }

            foreach (var consumer in go.GetComponents<SolidConduitConsumer>())
                AddPortHint("railIn", building.GetUtilityInputCell(), ConveyorLayers, missing, connected, consumer.IsConnected);
            foreach (var dispenser in go.GetComponents<SolidConduitDispenser>())
                AddPortHint("railOut", building.GetUtilityOutputCell(), ConveyorLayers, missing, connected, dispenser.IsConnected);
        }

        private static void AppendLogicPortHints(LogicPorts ports, LogicPorts.Port[] infos, string label, List<string> missing, List<string> connected)
        {
            if (infos == null)
                return;
            foreach (var port in infos)
            {
                int cell = LogicPortReadSemantics.ActualCell(ports, port);
                AddPortHint(label, cell, LogicLayers, missing, connected, LogicPortReadSemantics.ConnectedAtCell(cell));
            }
        }

        private static void AddPortHint(string label, int cell, ObjectLayer[] layers, List<string> missing, List<string> connected, bool? connectedOverride = null)
        {
            if (!Grid.IsValidCell(cell))
                return;
            bool hasLine = connectedOverride ?? HasLayer(cell, layers);
            string text = label + DecisionCellRef(cell);
            if (hasLine)
                connected.Add(text);
            else
                missing.Add(text);
        }

        private static bool AppendNetworkDecisionHint(StringBuilder sb, int x, int y, int cell)
        {
            bool hasNetwork = HasLayer(cell, PowerLayers) || HasLayer(cell, LiquidLayers)
                || HasLayer(cell, GasLayers) || HasLayer(cell, LogicLayers) || HasLayer(cell, ConveyorLayers);
            if (!hasNetwork)
                return false;

            sb.AppendLine("- network: cell has infrastructure; inspect same-cell dirs/links before editing: `"
                + LocalZoomCall(x, y, "power,liquid,gas,logic,conveyor") + "`.");
            return true;
        }

        private static string DecisionCellRef(int cell)
        {
            return "@(" + Grid.CellColumn(cell) + "," + Grid.CellRow(cell) + ")";
        }
    }
}
