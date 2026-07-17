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
        private static readonly ObjectLayer[] PowerLayers = { ObjectLayer.Wire, ObjectLayer.WireTile, ObjectLayer.ReplacementWire };
        private static readonly ObjectLayer[] LiquidLayers = { ObjectLayer.LiquidConduit, ObjectLayer.LiquidConduitTile, ObjectLayer.ReplacementLiquidConduit };
        private static readonly ObjectLayer[] GasLayers = { ObjectLayer.GasConduit, ObjectLayer.GasConduitTile, ObjectLayer.ReplacementGasConduit };
        private static readonly ObjectLayer[] LogicLayers = { ObjectLayer.LogicWire, ObjectLayer.LogicWireTile, ObjectLayer.ReplacementLogicWire };
        private static readonly ObjectLayer[] ConveyorLayers = { ObjectLayer.SolidConduit, ObjectLayer.SolidConduitTile, ObjectLayer.ReplacementSolidConduit };

        private static bool IsInfrastructureMapMarkdown(string relative)
        {
            return relative == "infrastructure/power.md"
                || relative == "infrastructure/liquid_conduits.md"
                || relative == "infrastructure/gas_conduits.md"
                || relative == "infrastructure/logic.md"
                || relative == "infrastructure/solid_conveyor.md";
        }

        private static string ReadInfrastructureMapMarkdown(JObject args, string path, string relative)
        {
            if (Camera.main == null)
                return "# " + path + "\n\nCamera not initialized.";

            HashedString mode = ModeForInfrastructurePath(relative);
            string syncNote = string.Empty;
            if (TryReadMapFocusBounds(args, out int focusXMin, out int focusYMin, out int focusXMax, out int focusYMax, out string focusError))
            {
                if (!string.IsNullOrWhiteSpace(focusError))
                    return "# " + path + "\n\n" + focusError;

                string viewName = GetOverlayViewName(mode);
                syncNote = SyncZoomCameraAndView(args, focusXMin, focusYMin, focusXMax, focusYMax, new List<ZoomView>
                {
                    new ZoomView { Name = viewName, Mode = mode }
                });
            }
            else if (ToolUtil.GetBool(args, "syncView", true))
            {
                ApplyZoomOverlayMode(mode, ToolUtil.GetBool(args, "allowSound", false));
                syncNote = "覆盖层=" + GetOverlayViewName(mode);
            }
            else
            {
                syncNote = "未同步(syncView=false)";
            }

            var cam = Camera.main;
            var pos = cam.transform.position;
            float size = cam.orthographicSize;
            float aspect = cam.aspect;
            int xMin = Mathf.Clamp(Mathf.RoundToInt(pos.x - size * aspect), 0, Grid.WidthInCells - 1);
            int xMax = Mathf.Clamp(Mathf.RoundToInt(pos.x + size * aspect), 0, Grid.WidthInCells - 1);
            int yMin = Mathf.Clamp(Mathf.RoundToInt(pos.y - size), 0, Grid.HeightInCells - 1);
            int yMax = Mathf.Clamp(Mathf.RoundToInt(pos.y + size), 0, Grid.HeightInCells - 1);
            string map = GetMapMd("[视图: " + GetOverlayViewName(mode) + "] " + path, xMin, xMax, yMin, yMax, mode, ShouldCompactMap(args));
            return map + "\n## View Sync\n- 直播视角: " + syncNote + "\n";
        }

        private static HashedString ModeForInfrastructurePath(string relative)
        {
            if (relative == "infrastructure/power.md") return OverlayModes.Power.ID;
            if (relative == "infrastructure/liquid_conduits.md") return OverlayModes.LiquidConduits.ID;
            if (relative == "infrastructure/gas_conduits.md") return OverlayModes.GasConduits.ID;
            if (relative == "infrastructure/logic.md") return OverlayModes.Logic.ID;
            if (relative == "infrastructure/solid_conveyor.md") return OverlayModes.SolidConveyor.ID;
            return OverlayModes.None.ID;
        }

        private static bool HasLayer(int cell, params ObjectLayer[] layers)
        {
            if (!Grid.IsValidCell(cell))
                return false;
            foreach (var layer in layers)
            {
                if (Grid.Objects[cell, (int)layer] != null)
                    return true;
            }
            return false;
        }

        private static char GetConnectionGlyph(Func<int, bool> isNode, int cell)
        {
            return GetConnectionGlyph(
                isNode(Grid.CellAbove(cell)),
                isNode(Grid.CellBelow(cell)),
                isNode(Grid.CellLeft(cell)),
                isNode(Grid.CellRight(cell)));
        }

        private static char GetConnectionGlyph(bool up, bool down, bool left, bool right)
        {
            int val = (up ? 8 : 0) | (down ? 4 : 0) | (left ? 2 : 0) | (right ? 1 : 0);
            switch (val)
            {
case 3: return '─';
                case 5: return '┌';
                case 6: return '┐';
                case 7: return '┬';
                case 9: return '└';
                case 10: return '┘';
                case 11: return '┴';
case 12: return '│';
                case 13: return '├';
                case 14: return '┤';
case 15: return '┼';
                default: return '*';
            }
        }

        private static bool IsConnectionGlyph(char symbol)
        {
return symbol == '─' || symbol == '│' || symbol == '┼'
|| symbol == '一' || symbol == '|' || symbol == '十' || symbol == '*'
|| symbol == '┌' || symbol == '┐' || symbol == '└' || symbol == '┘'
|| symbol == '┬' || symbol == '┴' || symbol == '├' || symbol == '┤'
|| symbol == '←' || symbol == '→' || symbol == '↑' || symbol == '↓'
|| symbol == '●';
        }

        private static string ConnectionLegend(char symbol)
        {
if (symbol == '─' || symbol == '一') return "左右连接";
if (symbol == '│' || symbol == '|') return "上下连接";
if (symbol == '┼' || symbol == '十') return "上下左右交叉";
if (symbol == '*') return "断点/端点/孤立段";
if (symbol == '●') return "实心焊点/强制交叉接点";
if (symbol == '←' || symbol == '→' || symbol == '↑' || symbol == '↓') return "单侧端点方向";
            if (symbol == '┌' || symbol == '┐' || symbol == '└' || symbol == '┘') return "拐角连接";
            return "丁字连接";
        }

        private static string GetMapMd(string title, int xMin, int xMax, int yMin, int yMax)
        {
            HashedString mode = OverlayScreen.Instance != null ? OverlayScreen.Instance.mode : OverlayModes.None.ID;
            return GetMapMd(title, xMin, xMax, yMin, yMax, mode);
        }

        private static string GetMapMd(string title, int xMin, int xMax, int yMin, int yMax, HashedString activeMode, bool compact = true)
        {
            var sb = new StringBuilder();
            sb.AppendFormat("# {0}\n", title);
            AppendMapMetadata(sb, xMin, xMax, yMin, yMax, activeMode);
            AppendVisualSpatialSummary(sb, xMin, xMax, yMin, yMax, activeMode);

            var gridLines = new List<string>();
            var details = new List<string>();
            var legend = new Dictionary<char, string>();
            var critterCells = BuildCritterCellMap();
            bool defaultView = activeMode == OverlayModes.None.ID;

            for (int y = yMax; y >= yMin; y--)
            {
                var line = new StringBuilder();
                string previousRunKey = null;
                for (int x = xMin; x <= xMax; x++)
                {
                    int cell = Grid.XYToCell(x, y);
                    string elemId = "Vacuum";
                    string elemName = "Vacuum";
                    string buildingId = string.Empty;
                    string buildingName = string.Empty;
                    GameObject building = null;
                    GameObject minion = null;
                    GameObject critter = null;

                    if (Grid.IsValidCell(cell))
                    {
                        var elem = Grid.Element[cell];
                        if (elem != null)
                        {
                            elemId = elem.id.ToString();
                            elemName = elem.name;
                        }
                        building = CellBuildingObject(cell);
                        if (building != null)
                        {
                            var complete = building.GetComponent<BuildingComplete>();
                            buildingId = complete != null ? complete.name : building.name;
                            buildingName = building.GetProperName();
                        }
                        minion = Grid.Objects[cell, (int)ObjectLayer.Minion];
                        critterCells.TryGetValue(cell, out critter);
                    }

                    float tempC = Grid.IsValidCell(cell) ? Grid.Temperature[cell] - 273.15f : 0f;
                    char symbol = ResolveMapSymbol(activeMode, cell, elemId, elemName, buildingId, buildingName, building, minion, critter, tempC);
                    if (!legend.ContainsKey(symbol))
                        legend[symbol] = SymbolLegend(activeMode, symbol);

                    string runKey;
                string token = FormatMapCellToken(activeMode, symbol, x, y, cell, building, minion, critter, buildingId, buildingName, previousRunKey, out runKey);
                    previousRunKey = runKey;
                    line.Append(token).Append(' ');

                    AppendCellDetails(details, activeMode, x, y, cell, elemName, tempC, building, buildingId, buildingName, minion, critter);
                }
                gridLines.Add("Y=" + y.ToString("D3") + ": " + line.ToString().Trim());
            }

            AppendGrid(sb, xMin, xMax, gridLines, compact);
            AppendLegend(sb, legend, activeMode);
            AppendConnectionDetails(sb, activeMode, xMin, xMax, yMin, yMax);
            if (details.Count > 0)
            {
                sb.AppendLine("## Cell Details (Buildings / Overlaps / Entities)");
                foreach (string detail in details.Distinct())
                    sb.AppendLine(detail);
            }
            AppendBuildingParameterReferences(sb, xMin, xMax, yMin, yMax);
            AppendMapFileIndex(sb);
            return sb.ToString();
        }

        private static void AppendMapMetadata(StringBuilder sb, int xMin, int xMax, int yMin, int yMax, HashedString mode)
        {
            string time = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz");
            try
            {
                int cycle = GameUtil.GetCurrentCycle();
                float percent = GameClock.Instance != null ? GameClock.Instance.GetCurrentCycleAsPercentage() : 0f;
                time += " | 游戏: Cycle " + cycle + ", " + (percent * 100f).ToString("F1") + "%";
            }
            catch { }

            string state = "unknown";
            try
            {
                var speed = SpeedControlScreen.Instance;
                state = speed != null ? (speed.IsPaused ? "暂停" : ((speed.GetSpeed() + 1) + "x")) : "timeScale=" + Time.timeScale.ToString("F2");
            }
            catch { }

            sb.AppendLine();
            sb.AppendLine("- 时间: " + time);
            sb.AppendLine("- 当前游戏状态: " + state);
            sb.AppendLine("- 视图: " + GetOverlayViewName(mode));
            sb.AppendLine("- 范围: X=" + xMin + "~" + xMax + ", Y=" + yMin + "~" + yMax + " (" + (xMax - xMin + 1) + "x" + (yMax - yMin + 1) + ")");
            sb.AppendLine();
        }

        private static Dictionary<int, GameObject> BuildCritterCellMap()
        {
            var result = new Dictionary<int, GameObject>();
            if (Components.Capturables == null || Components.Capturables.Items == null)
                return result;
            foreach (var cap in Components.Capturables.Items)
            {
                if (cap == null || cap.gameObject == null)
                    continue;
                int cell = Grid.PosToCell(cap.gameObject);
                if (Grid.IsValidCell(cell))
                    result[cell] = cap.gameObject;
            }
            return result;
        }

        private static char ResolveMapSymbol(HashedString mode, int cell, string elemId, string elemName, string buildingId, string buildingName, GameObject building, GameObject minion, GameObject critter, float tempC)
        {
            if (mode == OverlayModes.Power.ID) return ResolvePowerConnectionSymbol(cell);
            if (mode == OverlayModes.LiquidConduits.ID) return ResolveUtilityConnectionSymbol(cell, LiquidLayers);
            if (mode == OverlayModes.GasConduits.ID) return ResolveUtilityConnectionSymbol(cell, GasLayers);
            if (mode == OverlayModes.Logic.ID) return ResolveUtilityConnectionSymbol(cell, LogicLayers);
            if (mode == OverlayModes.SolidConveyor.ID) return ResolveUtilityConnectionSymbol(cell, ConveyorLayers);
            if (mode == OverlayModes.Temperature.ID) return TemperatureSymbol(elemId, tempC);
            if (mode == OverlayModes.Oxygen.ID) return OxygenSymbol(cell);
            if (mode == OverlayModes.Light.ID) return LightSymbol(cell);
            if (mode == OverlayModes.Decor.ID) return DecorSymbol(cell);
            if (mode == OverlayModes.Disease.ID) return DiseaseSymbol(cell);
            if (mode == OverlayModes.Radiation.ID) return RadiationSymbol(cell);
            if (mode == OverlayModes.TileMode.ID) return MaterialSymbol(elemId, elemName);
            if (mode == OverlayModes.Crop.ID || mode == OverlayModes.Harvest.ID) return CropSymbol(building);

            if (mode == OverlayModes.Rooms.ID) return RoomSymbol(cell);
            if (minion != null) return '人';
            if (critter != null) return '物';
            return !string.IsNullOrEmpty(buildingId) ? GetUniqueChar(buildingId, buildingName) : MaterialSymbol(elemId, elemName);
        }

        private static void AppendCellDetails(List<string> details, HashedString mode, int x, int y, int cell, string elemName, float tempC, GameObject building, string buildingId, string buildingName, GameObject minion, GameObject critter)
        {
            bool defaultView = mode == OverlayModes.None.ID;
            string bg = "元素=" + StripLinkTags(elemName) + ", 温度=" + tempC.ToString("F1") + "°C";
            if (defaultView && minion != null)
                details.Add("- " + GetDupeMapKind(minion) + "@" + MapTokenPart(StripLinkTags(minion.GetProperName())) + " (" + x + "," + y + "): " + bg + GetDupeMovementInfo(cell));
            if (defaultView && critter != null)
                details.Add("- 物@" + MapTokenPart(StripLinkTags(critter.GetProperName())) + " (" + x + "," + y + "): " + bg);
            if (mode == OverlayModes.Power.ID && building != null)
            {
                string info = GetPowerInfo(building);
                if (!string.IsNullOrEmpty(info))
                    details.Add("- " + MapTokenPart(StripLinkTags(string.IsNullOrEmpty(buildingName) ? buildingId : buildingName)) + "@(" + x + "," + y + "): " + info);
            }
        }

        private static void AppendGrid(StringBuilder sb, int xMin, int xMax, List<string> gridLines, bool compact)
        {
            sb.AppendLine("## Grid Map");
            sb.AppendLine("```text");
            sb.Append("百位X: ");
            AppendGuidedAxis(sb, xMin, xMax, x => (x / 100) % 10);
            sb.AppendLine();
            sb.Append("十位X: ");
            AppendGuidedAxis(sb, xMin, xMax, x => (x / 10) % 10);
            sb.AppendLine();
            sb.Append("个位X: ");
            AppendGuidedAxis(sb, xMin, xMax, x => x % 10);
            sb.AppendLine();
            foreach (string line in compact ? CompressBlankGridLines(gridLines) : gridLines)
                sb.AppendLine(InsertGridGuides(line, compact));
            sb.AppendLine("```");
            sb.AppendLine();
        }

        private static void AppendLegend(StringBuilder sb, Dictionary<char, string> legend, HashedString activeMode)
        {
            sb.AppendLine("## Legend");
            foreach (var item in legend.OrderBy(kv => kv.Key))
                sb.AppendLine(IsConnectionGlyph(item.Key) ? "- `" + item.Key + "` : 连接 (Connection) | " + item.Value : FormatLegendLine(item.Key, item.Value));
            AppendInfrastructureLegend(sb, activeMode);
            AppendInfrastructureReadHints(sb, activeMode);
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

        private static string FormatMapCellToken(HashedString mode, char symbol, int x, int y, int cell, GameObject building, GameObject minion, GameObject critter, string buildingId, string buildingName, string previousRunKey, out string runKey)
        {
            runKey = null;
            if (TryFormatPowerAnchorToken(mode, x, y, building, minion, buildingId, buildingName, previousRunKey, out string anchorToken, out runKey))
                return MergeOverlaySymbol(symbol, anchorToken);
            if (mode != OverlayModes.None.ID)
            {
                if (TryFormatOverlayAnchorToken(mode, symbol, cell, x, y, building, minion, critter, buildingId, buildingName, previousRunKey, out string overlayToken, out runKey))
                    return overlayToken;
                return symbol.ToString();
            }
            if (minion != null)
            {
                string name = MapTokenPart(StripLinkTags(minion.GetProperName()));
                runKey = "dupe:" + name;
                return previousRunKey == runKey ? "人" : "人@" + name;
            }
            if (critter != null)
            {
                string name = MapTokenPart(StripLinkTags(critter.GetProperName()));
                runKey = "critter:" + name;
                return previousRunKey == runKey ? "物" : "物@" + name;
            }
            if (building != null && !string.IsNullOrEmpty(buildingId))
            {
                var logicGate = building.GetComponent<LogicGate>();
                if (logicGate != null && !IsBuildingAnchorCell(building, cell))
                    return symbol.ToString();
                string token = MapTokenPart(!string.IsNullOrEmpty(buildingName) ? StripLinkTags(buildingName) : buildingId);
                string suffix = string.Empty;
                if (IsBlueprintToken(building))
                {
                    suffix += ":" + GetMapPriority(building);
                    string material = GetMapMaterialSymbol(building);
                    if (!string.IsNullOrEmpty(material)) suffix += "#" + material;
                }
                string identity = logicGate == null ? string.Empty
                    : ":" + (building.GetComponent<KPrefabID>()?.InstanceID ?? building.GetInstanceID());
                runKey = "building:" + buildingId + identity + suffix;
                return previousRunKey == runKey ? symbol.ToString() : token + suffix + "@(" + x + "," + y + ")";
            }
            return symbol.ToString();
        }

        private static string MapTokenPart(string text)
        {
            text = StripLinkTags(text);
            if (string.IsNullOrWhiteSpace(text)) return "?";
            var sb = new StringBuilder();
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c)) continue;
                sb.Append(c == '@' || c == ':' || c == '#' ? '_' : c);
            }
            return sb.Length == 0 ? "?" : sb.ToString();
        }

        private static string GetDupeMapKind(GameObject dupe)
        {
            try
            {
                var id = dupe != null ? dupe.GetComponent<KPrefabID>() : null;
                if (id != null && (id.HasTag(GameTags.Minions.Models.Bionic) || id.HasTag(GameTags.Robot))) return "仿";
            }
            catch { }
            return "人";
        }

        private static string GetDupeMovementInfo(int cell)
        {
            try
            {
                if (!Grid.IsValidCell(cell)) return string.Empty;
                var elem = Grid.Element[cell];
                if (elem == null || !elem.IsLiquid) return string.Empty;
                return ", 移动=游泳/液体(" + StripLinkTags(elem.name) + " " + Grid.Mass[cell].ToString("F1") + "kg)";
            }
            catch { return string.Empty; }
        }

        private static void AppendMapFileIndex(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("## File Index");
            sb.AppendLine("- `map/viewport.md` : current camera viewport map; move the camera to change it");
            sb.AppendLine("- `map/zoom_X1_Y1_X2_Y2.md` : local multi-view map, e.g. `/active/map/zoom_80_140_105_155.md`");
            sb.AppendLine("- `map/cell_X_Y.md` : single-cell cross-view snapshot, for example `/active/map/cell_91_149.md`");
            sb.AppendLine("- `map/index.md` : legacy alias for `map/viewport.md`");
            sb.AppendLine("- `map/layers/` : full world map layers");
            sb.AppendLine("- `infrastructure/power.md` : power connection map");
            sb.AppendLine("- `infrastructure/liquid_conduits.md` : liquid pipe connection map");
            sb.AppendLine("- `infrastructure/gas_conduits.md` : gas pipe connection map");
            sb.AppendLine("- `infrastructure/logic.md` : signal wire connection map");
            sb.AppendLine("- `infrastructure/solid_conveyor.md` : conveyor rail connection map");
            sb.AppendLine("- `symbols/glyphs.md` : generated global one-character mapping");
            sb.AppendLine("- `buildings/index.md` : completed building parameter files; map output links only compact references");
            sb.AppendLine("- `grep` command : search within virtual file without reading all content");
        }

        private static bool IsBlueprintToken(GameObject go)
        {
            if (go == null) return false;
            if (go.GetComponent<BuildingComplete>() != null) return false;
            return go.GetComponent("Constructable") != null
                || go.GetComponent("BuildingUnderConstruction") != null
                || (go.GetComponent<KPrefabID>() != null && go.name.IndexOf("UnderConstruction", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static int GetMapPriority(GameObject go)
        {
            var prioritizable = go != null ? go.GetComponent<Prioritizable>() : null;
            if (prioritizable == null) return 5;
            try { return Math.Max(1, Math.Min(prioritizable.GetMasterPriority().priority_value, 9)); }
            catch { }
            return 5;
        }

        private static string GetMapMaterialSymbol(GameObject go)
        {
            var primary = go != null ? go.GetComponent<PrimaryElement>() : null;
            return primary == null ? string.Empty : GetUniqueChar(primary.ElementID.ToString(), string.Empty).ToString();
        }

        private static string StripLinkTags(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var sb = new StringBuilder();
            bool inTag = false;
            foreach (char c in text)
            {
                if (c == '<') { inTag = true; continue; }
                if (c == '>') { inTag = false; continue; }
                if (!inTag) sb.Append(c);
            }
            return sb.ToString().Trim();
        }
    }
}
