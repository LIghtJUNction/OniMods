using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static void AppendCellPortSnapshot(StringBuilder sb, int cell)
        {
            var buildingObject = Grid.Objects[cell, (int)ObjectLayer.Building];
            var building = buildingObject != null ? buildingObject.GetComponent<Building>() : null;
            if (building == null || building.Def == null)
                return;

            sb.AppendLine();
            sb.AppendLine("## Ports / Interfaces");
            sb.AppendLine("- 判定点: " + CellRef(cell));
            AppendPowerPorts(sb, building);
            AppendConduitPorts(sb, buildingObject, building);
            AppendLogicPorts(sb, buildingObject);
            AppendSolidPorts(sb, buildingObject, building);
        }

        private static void AppendPowerPorts(StringBuilder sb, Building building)
        {
            var def = building.Def;
            if (def.RequiresPowerInput)
                sb.AppendLine("- 电力输入: " + PortLine(building.GetPowerInputCell(), "wire", HasLayer(building.GetPowerInputCell(), PowerLayers)));
            if (def.RequiresPowerOutput)
                sb.AppendLine("- 电力输出: " + PortLine(building.GetPowerOutputCell(), "wire", HasLayer(building.GetPowerOutputCell(), PowerLayers)));

            var consumer = building.GetComponent<EnergyConsumer>();
            if (consumer != null)
                sb.AppendLine("- 耗电端: " + consumer.WattsNeededWhenActive.ToString("F0") + "W");
            var generator = building.GetComponent<Generator>();
            if (generator != null)
                sb.AppendLine("- 发电端: " + generator.WattageRating.ToString("F0") + "W");
            var battery = building.GetComponent<Battery>();
            if (battery != null)
                sb.AppendLine("- 蓄电端: " + battery.JoulesAvailable.ToString("F0") + "J/" + battery.Capacity.ToString("F0") + "J");
        }

        private static void AppendConduitPorts(StringBuilder sb, GameObject go, Building building)
        {
            foreach (var consumer in go.GetComponents<ConduitConsumer>())
            {
                ObjectLayer[] layers = consumer.ConduitType == ConduitType.Gas ? GasLayers : LiquidLayers;
                string label = consumer.ConduitType == ConduitType.Gas ? "气体输入" : "液体输入";
                int portCell = building.GetUtilityInputCell();
                sb.AppendLine("- " + label + ": " + PortLine(portCell, "pipe", HasLayer(portCell, layers))
                    + " connected=" + consumer.IsConnected);
            }

            foreach (var dispenser in go.GetComponents<ConduitDispenser>())
            {
                ObjectLayer[] layers = dispenser.ConduitType == ConduitType.Gas ? GasLayers : LiquidLayers;
                string label = dispenser.ConduitType == ConduitType.Gas ? "气体输出" : "液体输出";
                int portCell = building.GetUtilityOutputCell();
                sb.AppendLine("- " + label + ": " + PortLine(portCell, "pipe", HasLayer(portCell, layers))
                    + " connected=" + dispenser.IsConnected);
            }
        }

        private static void AppendLogicPorts(StringBuilder sb, GameObject go)
        {
            var ports = go.GetComponent<LogicPorts>();
            if (ports == null)
                return;
            if (LogicPortReadSemantics.TryBridgeRoute(go, out int from, out int to))
            {
                sb.AppendLine("- 信号输入: " + PortLine(from, "logic", HasLayer(from, LogicLayers))
                    + " connected=" + LogicPortReadSemantics.ConnectedAtCell(from));
                sb.AppendLine("- 信号输出: " + PortLine(to, "logic", HasLayer(to, LogicLayers))
                    + " connected=" + LogicPortReadSemantics.ConnectedAtCell(to));
                sb.AppendLine("- 桥接路由: from:" + CellRef(from) + " via:"
                    + CellRef(Grid.PosToCell(go)) + "⌒ to:" + CellRef(to));
                return;
            }

            AppendLogicPortGroup(sb, "信号输入", ports, ports.inputPortInfo, true);
            AppendLogicPortGroup(sb, "信号输出", ports, ports.outputPortInfo, false);
        }

        private static void AppendLogicPortGroup(StringBuilder sb, string label, LogicPorts ports, LogicPorts.Port[] infos, bool input)
        {
            if (infos == null || infos.Length == 0)
                return;

            for (int i = 0; i < infos.Length; i++)
            {
                var port = infos[i];
                int portCell = LogicPortReadSemantics.ActualCell(ports, port);
                sb.AppendLine("- " + label + ": " + port.id
                    + " " + PortLine(portCell, "logic", HasLayer(portCell, LogicLayers))
                    + " connected=" + LogicPortReadSemantics.ConnectedAtCell(portCell)
                    + " value=" + (input ? LogicPortReadSemantics.InputValue(ports, i) : LogicPortReadSemantics.OutputValue(ports, i))
                    + " required=" + port.requiresConnection);
            }
        }

        private static void AppendSolidPorts(StringBuilder sb, GameObject go, Building building)
        {
            foreach (var consumer in go.GetComponents<SolidConduitConsumer>())
            {
                int portCell = building.GetUtilityInputCell();
                sb.AppendLine("- 轨道输入: " + PortLine(portCell, "rail", HasLayer(portCell, ConveyorLayers))
                    + " connected=" + consumer.IsConnected);
            }

            foreach (var dispenser in go.GetComponents<SolidConduitDispenser>())
            {
                int portCell = building.GetUtilityOutputCell();
                sb.AppendLine("- 轨道输出: " + PortLine(portCell, "rail", HasLayer(portCell, ConveyorLayers))
                    + " connected=" + dispenser.IsConnected);
            }
        }

        private static string PortLine(int cell, string layer, bool connected)
        {
            return CellRef(cell) + " layer=" + layer + " hasLine=" + connected;
        }

        private static string CellRef(int cell)
        {
            if (!Grid.IsValidCell(cell))
                return "(invalid)";
            return "(" + Grid.CellColumn(cell) + "," + Grid.CellRow(cell) + "), Cell=" + cell;
        }

        private static void AppendCellPickupSummary(StringBuilder sb, int cell)
        {
            var groups = Components.Pickupables.Items
                .Where(item => item != null && item.gameObject != null && Grid.PosToCell(item.gameObject) == cell)
                .GroupBy(item => StripLinkTags(item.GetProperName()))
                .Select(group => new
                {
                    Name = group.Key,
                    Count = group.Count(),
                    Mass = group.Sum(item => item.PrimaryElement != null ? item.PrimaryElement.Mass : 0f)
                })
                .OrderByDescending(item => item.Mass)
                .ThenBy(item => item.Name)
                .ToList();

            if (groups.Count == 0)
                return;

            sb.AppendLine();
            sb.AppendLine("## Dropped Items Summary");
            sb.AppendLine("- 种类: " + groups.Count + " | 总数量: " + groups.Sum(item => item.Count)
                + " | 总质量: " + groups.Sum(item => item.Mass).ToString("F2") + "kg");
            foreach (var item in groups.Take(12))
                sb.AppendLine("- " + item.Name + ": x" + item.Count + ", " + item.Mass.ToString("F2") + "kg");
            if (groups.Count > 12)
                sb.AppendLine("- ... +" + (groups.Count - 12) + " more");
        }
    }
}
