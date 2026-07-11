using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static bool TryParseCellSnapshotPath(string relative, out int x, out int y)
        {
            x = 0;
            y = 0;
            const string prefix = "map/cell_";
            const string suffix = ".md";
            if (string.IsNullOrEmpty(relative)
                || !relative.StartsWith(prefix, StringComparison.Ordinal)
                || !relative.EndsWith(suffix, StringComparison.Ordinal))
                return false;

            string body = relative.Substring(prefix.Length, relative.Length - prefix.Length - suffix.Length);
            string[] parts = body.Split('_');
            return parts.Length == 2 && int.TryParse(parts[0], out x) && int.TryParse(parts[1], out y);
        }

        private static string ReadCellSnapshotMarkdown(JObject args, int x, int y)
        {
            int cell = Grid.XYToCell(x, y);
            var sb = new StringBuilder();
            sb.AppendLine("# Cell (" + x + "," + y + ") Cross-View Snapshot");
            sb.AppendLine();

            if (!Grid.IsValidCell(cell))
            {
                sb.AppendLine("- 状态: invalid cell");
                return sb.ToString();
            }

            AppendCellBaseSnapshot(sb, cell);
            AppendCellObjectSnapshot(sb, x, y, cell);
            AppendCellInfrastructureSnapshot(sb, cell);
            AppendCellPortSnapshot(sb, cell);
            AppendCellItemSnapshot(sb, cell, args);
            AppendCellPickupDetailSnapshot(sb, cell, args);
            AppendCellPickupSummary(sb, cell);
            AppendCellDecisionHints(sb, x, y, cell);
            AppendCellQuickOps(sb, x, y, cell);
            AppendCellNextReads(sb, x, y, cell);
            sb.AppendLine();
            sb.AppendLine("## Links");
            sb.AppendLine("- `/active/map/viewport.md`");
            sb.AppendLine("- `/active/infrastructure/power.md`");
            sb.AppendLine("- `/active/infrastructure/liquid_conduits.md`");
            sb.AppendLine("- `/active/infrastructure/gas_conduits.md`");
            sb.AppendLine("- `/active/infrastructure/logic.md`");
            sb.AppendLine("- `/active/infrastructure/solid_conveyor.md`");
            return sb.ToString();
        }

        private static void AppendCellQuickOps(StringBuilder sb, int x, int y, int cell)
        {
            sb.AppendLine();
            sb.AppendLine("## Quick Ops");
            sb.AppendLine("- execute: paste one command into `/active/ops/orders.md` under `## Edit Commands`; keep `dryRun=true` until the preview is clean.");
            sb.AppendLine("- 挖: `挖 @(" + x + "," + y + "):7 dryRun=true`");
            sb.AppendLine("- 擦: `擦 @(" + x + "," + y + "):6 dryRun=true`");
            sb.AppendLine("- 扫: `扫 @(" + x + "," + y + "):6 dryRun=true`");
            sb.AppendLine("- 消: `消 @(" + x + "," + y + ") dryRun=true`");
            GameObject building = Grid.Objects[cell, (int)ObjectLayer.Building];
            if (building != null)
                sb.AppendLine("- 拆: `拆 建筑@(" + x + "," + y + "):7 dryRun=true`");
            if (BuildCritterCellMap().ContainsKey(cell))
            {
                sb.AppendLine("- 捕: `捕 小动物@(" + x + "," + y + "):7 dryRun=true`");
                sb.AppendLine("- 杀: `杀 小动物@(" + x + "," + y + "):7 dryRun=true`");
            }
        }

        private static void AppendCellBaseSnapshot(StringBuilder sb, int cell)
        {
            Element element = Grid.Element[cell];
            string elementName = element != null ? StripLinkTags(element.name) : "Vacuum";
            string elementId = element != null ? element.id.ToString() : "Vacuum";
            sb.AppendLine("## Base");
            sb.AppendLine("- Cell: " + cell);
            sb.AppendLine("- 元素: " + elementName + " (" + elementId + ")");
            sb.AppendLine("- 质量: " + Grid.Mass[cell].ToString("F2") + " kg");
            float tempC = Grid.Temperature[cell] - 273.15f;
            sb.AppendLine("- 温度: " + tempC.ToString("F1") + "°C");
            sb.AppendLine("- 温度适宜: " + TemperatureSuitabilityText(tempC));
            sb.AppendLine("- 温度适宜详情: band=" + TemperatureSuitabilityBand(tempC)
                + " dupeComfortC=10~40 cautionC=-20~10/40~72 dangerC=<-20/>=72");
            sb.AppendLine("- 环境判定: " + CellEnvironmentText(element, Grid.Mass[cell], tempC));
        }

        private static string CellEnvironmentText(Element element, float massKg, float tempC)
        {
            string phase = CellPhase(element, massKg);
            string breathable = IsBreathableElement(element, massKg) ? "yes" : "no";
            string pressure = CellPressureBand(element, massKg);
            string liquidRisk = element != null && element.IsLiquid && massKg > 1f ? "yes" : "no";
            return "phase=" + phase
                + " breathable=" + breathable
                + " pressure=" + pressure
                + " liquidRisk=" + liquidRisk
                + " tempBand=" + TemperatureSuitabilityBand(tempC);
        }

        private static string CellPhase(Element element, float massKg)
        {
            if (element == null || massKg <= 0.001f)
                return "vacuum";
            if (element.IsSolid)
                return "solid";
            if (element.IsLiquid)
                return "liquid";
            if (element.IsGas)
                return "gas";
            return "unknown";
        }

        private static bool IsBreathableElement(Element element, float massKg)
        {
            return element != null
                && massKg > 0.01f
                && (element.id == SimHashes.Oxygen || element.id == SimHashes.ContaminatedOxygen);
        }

        private static string CellPressureBand(Element element, float massKg)
        {
            if (element == null || massKg <= 0.001f)
                return "vacuum";
            if (element.IsLiquid)
                return massKg > 1f ? "liquid_blocking" : "liquid_trace";
            if (!element.IsGas)
                return "solid";
            if (massKg < 0.05f)
                return "near_vacuum";
            if (massKg < 0.2f)
                return "thin";
            if (massKg > 3f)
                return "overpressure";
            return "normal";
        }

        private static string TemperatureSuitabilityText(float tempC)
        {
            if (tempC >= 72f) return "危险-烫伤风险; comfort=10~40°C, caution=40~72°C";
            if (tempC <= -20f) return "危险-低温风险; comfort=10~40°C, caution=-20~10°C";
            if (tempC > 40f) return "偏热; comfort=10~40°C";
            if (tempC < 10f) return "偏冷; comfort=10~40°C";
            return "适宜; comfort=10~40°C";
        }

        private static string TemperatureSuitabilityBand(float tempC)
        {
            if (tempC >= 72f) return "danger_hot";
            if (tempC <= -20f) return "danger_cold";
            if (tempC > 40f) return "hot";
            if (tempC < 10f) return "cold";
            return "dupe_comfort";
        }

        private static void AppendCellObjectSnapshot(StringBuilder sb, int x, int y, int cell)
        {
            sb.AppendLine();
            sb.AppendLine("## Objects");
            GameObject building = Grid.Objects[cell, (int)ObjectLayer.Building];
            GameObject digPlacer = Grid.Objects[cell, (int)ObjectLayer.DigPlacer];
            GameObject minion = Grid.Objects[cell, (int)ObjectLayer.Minion];
            BuildCritterCellMap().TryGetValue(cell, out var critter);
            sb.AppendLine("- 建筑: " + FormatCellGameObject(building));
            sb.AppendLine("- 挖掘标记: " + FormatDigOrder(digPlacer));
            sb.AppendLine("- 复制人/仿生人: " + FormatCellDupe(minion));
            sb.AppendLine("- 小动物: " + FormatCellGameObject(critter));

            if (building != null || minion != null || critter != null)
                sb.AppendLine("- 地图标记: " + FormatMapCellToken(
                    OverlayModes.None.ID,
                    ResolveMapSymbol(OverlayModes.None.ID, cell, "", "", CellPrefabId(building), CellProperName(building), building, minion, critter, 0f),
                    x, y, cell, building, minion, critter, CellPrefabId(building), CellProperName(building), null, out _));
        }

        private static string FormatDigOrder(GameObject digPlacer)
        {
            if (digPlacer == null)
                return "无";
            var prioritizable = digPlacer.GetComponent<Prioritizable>();
            if (prioritizable == null)
                return "有; priority=unknown";
            var setting = prioritizable.GetMasterPriority();
            return "有; priority=" + setting.priority_value
                + "; class=" + setting.priority_class;
        }

        private static void AppendCellInfrastructureSnapshot(StringBuilder sb, int cell)
        {
            sb.AppendLine();
            sb.AppendLine("## Infrastructure");
            sb.AppendLine("- 电力: " + CellConnectionText(OverlayModes.Power.ID, cell, "wire", PowerLayers));
            sb.AppendLine("- 液管: " + CellConnectionText(OverlayModes.LiquidConduits.ID, cell, "liquid", LiquidLayers));
            sb.AppendLine("- 气管: " + CellConnectionText(OverlayModes.GasConduits.ID, cell, "gas", GasLayers));
            sb.AppendLine("- 信号: " + CellConnectionText(OverlayModes.Logic.ID, cell, "logic", LogicLayers));
            sb.AppendLine("- 运输: " + CellConnectionText(OverlayModes.SolidConveyor.ID, cell, "rail", ConveyorLayers));

            GameObject building = Grid.Objects[cell, (int)ObjectLayer.Building];
            string powerInfo = GetPowerInfo(building);
            if (!string.IsNullOrEmpty(powerInfo))
                sb.AppendLine("- 电力设备: " + powerInfo);
            AppendCellEndpointSnapshot(sb, "电力", OverlayModes.Power.ID, cell);
            AppendCellEndpointSnapshot(sb, "液管", OverlayModes.LiquidConduits.ID, cell);
            AppendCellEndpointSnapshot(sb, "气管", OverlayModes.GasConduits.ID, cell);
            AppendCellEndpointSnapshot(sb, "信号", OverlayModes.Logic.ID, cell);
            AppendCellEndpointSnapshot(sb, "运输", OverlayModes.SolidConveyor.ID, cell);
            AppendCellInfrastructureDetailHint(sb, cell);
        }

        private static void AppendCellEndpointSnapshot(StringBuilder sb, string label, HashedString mode, int cell)
        {
            string endpoint = EndpointDetailLine(mode, cell);
            if (!string.IsNullOrEmpty(endpoint))
                sb.AppendLine("- 端口(" + label + "): " + TrimListBullet(endpoint));
        }

        private static string TrimListBullet(string text)
        {
            return text != null && text.StartsWith("- ", StringComparison.Ordinal)
                ? text.Substring(2)
                : text;
        }

        private static string CellConnectionText(HashedString mode, int cell, string layerName, ObjectLayer[] layers)
        {
            char glyph = mode == OverlayModes.Power.ID
                ? ResolvePowerConnectionSymbol(cell)
                : ResolveUtilityConnectionSymbol(cell, layers);
            var dirs = ConnectionDirections(cell, layers, mode == OverlayModes.Power.ID);
            string text = "glyph=" + glyph
                + " dirs=" + (dirs.Count == 0 ? "." : string.Join("", dirs.Select(d => d.Dir).ToArray()))
                + " links=" + ConnectionLinkText(dirs)
                 + " open=" + OpenAdjacentConnectionText(cell, layers, dirs)
            + " to=" + (dirs.Count == 0 ? "." : string.Join(",", dirs.Select(d => CellCoord(d.Cell)).ToArray()));
            if (mode == OverlayModes.Power.ID)
                text += " " + PowerCircuitText(cell);
            string bridge = BridgeText(cell, mode);
            if (!string.IsNullOrEmpty(bridge))
                text += " " + bridge;
            return "layer=" + layerName + " " + text;
        }

        private static void AppendCellItemSnapshot(StringBuilder sb, int cell, JObject args)
        {
            int limit = CellPickupItemLimit(args);
            var allItems = Components.Pickupables.Items
                .Where(item => PickupableTouchesCell(item, cell))
                .ToList();

            if (allItems.Count == 0)
                return;

            var items = allItems.Take(limit)
                .Select(item => "- " + StripLinkTags(item.GetProperName()) + " | "
                    + (item.PrimaryElement != null ? item.PrimaryElement.Mass.ToString("F2") + " kg" : "mass=?"))
                .ToList();

            sb.AppendLine();
            sb.AppendLine("## Items");
            sb.AppendLine("- total=" + allItems.Count
                + ", shown=" + items.Count
                + ", truncated=" + (items.Count < allItems.Count).ToString().ToLowerInvariant()
                + ", itemLimit=" + limit);
            foreach (string item in items)
                sb.AppendLine(item);
            if (items.Count < allItems.Count)
                sb.AppendLine("- ... +" + (allItems.Count - items.Count) + " more; reread with `itemLimit=" + allItems.Count + "` or `includeAllItems=true`.");
        }

        private static char InfrastructureGlyph(int cell, ObjectLayer[] layers)
        {
            return ResolveUtilityConnectionSymbol(cell, layers);
        }

        private static string FormatCellDupe(GameObject go)
        {
            if (go == null)
                return "无";

            return GetDupeMapKind(go) + "@" + MapTokenPart(StripLinkTags(go.GetProperName()))
                + " | " + ObjectLocationText(go);
        }

        private static string FormatCellGameObject(GameObject go)
        {
            if (go == null)
                return "无";

            string name = CellProperName(go);
            string id = CellPrefabId(go);
            string label = string.IsNullOrEmpty(id) ? name : name + " (ID=" + id + ")";
            return label + " | " + ObjectLocationText(go) + BuildingFootprintText(go);
        }

        private static string ObjectLocationText(GameObject go)
        {
            int objectCell = Grid.PosToCell(go);
            if (!Grid.IsValidCell(objectCell))
                return "判定点=invalid";

            var pos = go.transform.position;
            return "判定点=(" + Grid.CellColumn(objectCell) + "," + Grid.CellRow(objectCell) + ")"
                + ", Cell=" + objectCell
                + ", Pos=(" + pos.x.ToString("F2") + "," + pos.y.ToString("F2") + ")";
        }

        private static string BuildingFootprintText(GameObject go)
        {
            var building = go.GetComponent<Building>();
            BuildingDef def = building != null ? building.Def : null;
            if (def == null)
                return string.Empty;

            int anchorCell = building.GetBottomLeftCell();
            if (!Grid.IsValidCell(anchorCell))
                anchorCell = Grid.PosToCell(go);
            string anchor = Grid.IsValidCell(anchorCell)
                ? "(" + Grid.CellColumn(anchorCell) + "," + Grid.CellRow(anchorCell) + ")"
                : "invalid";
            return ", footprint=" + Math.Max(1, def.WidthInCells) + "x" + Math.Max(1, def.HeightInCells)
                + ", bottomLeft=" + anchor
                + ", rule=" + def.BuildLocationRule;
        }

        private static string CellProperName(GameObject go)
        {
            return go == null ? string.Empty : StripLinkTags(go.GetProperName());
        }

        private static string CellPrefabId(GameObject go)
        {
            var prefab = go != null ? go.GetComponent<KPrefabID>() : null;
            return prefab != null && prefab.PrefabTag.IsValid ? prefab.PrefabTag.Name : string.Empty;
        }
    }
}
