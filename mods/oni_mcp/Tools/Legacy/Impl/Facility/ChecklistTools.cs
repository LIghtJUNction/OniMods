using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static class ChecklistTools
    {
        public static McpTool ListChecklists()
        {
            return new McpTool
            {
                Name = "side_checklists_list",
                Group = "controls",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "checkbox_list_group_controls_list", "quest_checklists_list", "side_screen_checklists_list" },
                Tags = new List<string> { "controls", "side-screen", "checklist", "quest", "conditions", "read-only" },
                Description = "兼容入口：请优先使用 building_control domain=side_surface kind=checklist action=list。列出实现 ICheckboxListGroupControl 的只读侧屏清单",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按对象、prefabId、标题、描述、条目或 tooltip 筛选", Required = false },
                    ["checkedOnly"] = new McpToolParameter { Type = "boolean", Description = "是否只返回已完成/勾选条目，默认 false", Required = false },
                    ["enabledOnly"] = new McpToolParameter { Type = "boolean", Description = "是否只返回当前启用的侧屏清单，默认 true", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回对象数量，默认 100，最大 500", Required = false }
                }),
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    bool hasRect = HasRectInput(args);
                    var rect = hasRect ? ToolUtil.GetRect(args) : null;
                    int worldId = hasRect || ToolUtil.GetInt(args, "worldId").HasValue ? ToolUtil.ResolveWorldId(args) : -1;
                    string query = args["query"]?.ToString();
                    bool checkedOnly = ToolUtil.GetBool(args, "checkedOnly", false);
                    bool enabledOnly = ToolUtil.GetBool(args, "enabledOnly", true);
                    int limit = ToolUtil.ClampLimit(args, 100, 500);

                    var targets = AllCandidateObjects()
                        .Where(go => MatchesTarget(go, rect, worldId))
                        .Select(go => ChecklistTargetInfo(go, checkedOnly, enabledOnly))
                        .Where(info => ((List<Dictionary<string, object>>)info["checklists"]).Count > 0)
                        .Where(info => MatchesQuery(info, query))
                        .OrderBy(info => info["name"].ToString())
                        .Take(limit)
                        .ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["returned"] = targets.Count,
                        ["worldId"] = worldId >= 0 ? (object)worldId : null,
                        ["checkedOnly"] = checkedOnly,
                        ["enabledOnly"] = enabledOnly,
                        ["targets"] = targets
                    }, McpJsonUtil.Settings));
                }
            };
        }

        private static Dictionary<string, object> ChecklistTargetInfo(GameObject go, bool checkedOnly, bool enabledOnly)
        {
            var result = TargetInfo(go);
            result["checklists"] = GetControls(go, enabledOnly)
                .Select((control, index) => ChecklistInfo(control, index, checkedOnly))
                .Where(info => ((List<Dictionary<string, object>>)info["groups"]).Count > 0)
                .ToList();
            return result;
        }

        private static Dictionary<string, object> ChecklistInfo(ICheckboxListGroupControl control, int index, bool checkedOnly)
        {
            var groups = new List<Dictionary<string, object>>();
            var data = control.GetData() ?? new ICheckboxListGroupControl.ListGroup[0];
            for (int groupIndex = 0; groupIndex < data.Length; groupIndex++)
            {
                var group = data[groupIndex];
                var items = new List<Dictionary<string, object>>();
                var checkboxItems = group.checkboxItems ?? new ICheckboxListGroupControl.CheckboxItem[0];
                for (int itemIndex = 0; itemIndex < checkboxItems.Length; itemIndex++)
                {
                    var item = checkboxItems[itemIndex];
                    if (checkedOnly && !item.isOn)
                        continue;
                    items.Add(new Dictionary<string, object>
                    {
                        ["index"] = itemIndex,
                        ["text"] = item.text,
                        ["tooltip"] = ResolveTooltip(item, control),
                        ["checked"] = item.isOn
                    });
                }

                if (items.Count == 0 && checkedOnly)
                    continue;

                groups.Add(new Dictionary<string, object>
                {
                    ["index"] = groupIndex,
                    ["title"] = ResolveTitle(group),
                    ["itemCount"] = items.Count,
                    ["checkedCount"] = items.Count(item => (bool)item["checked"]),
                    ["items"] = items
                });
            }

            return new Dictionary<string, object>
            {
                ["index"] = index,
                ["title"] = SafeString(() => control.Title),
                ["description"] = SafeString(() => control.Description),
                ["enabled"] = SafeBool(control.SidescreenEnabled),
                ["sortOrder"] = SafeInt(control.CheckboxSideScreenSortOrder),
                ["controlType"] = control.GetType().FullName,
                ["groups"] = groups
            };
        }

        private static List<ICheckboxListGroupControl> GetControls(GameObject go, bool enabledOnly)
        {
            if (go == null)
                return new List<ICheckboxListGroupControl>();
            var controls = go.GetAllSMI<ICheckboxListGroupControl>();
            controls.AddRange(go.GetComponents<ICheckboxListGroupControl>());
            return controls
                .Where(control => control != null)
                .Where(control => !enabledOnly || SafeBool(control.SidescreenEnabled))
                .ToList();
        }

        private static IEnumerable<GameObject> AllCandidateObjects()
        {
            var seen = new HashSet<int>();
            foreach (var kpid in UnityEngine.Object.FindObjectsByType<KPrefabID>(FindObjectsSortMode.None))
            {
                if (kpid == null || kpid.gameObject == null)
                    continue;
                int id = kpid.gameObject.GetInstanceID();
                if (seen.Add(id))
                    yield return kpid.gameObject;
            }
        }

        private static bool MatchesTarget(GameObject go, Dictionary<string, int> rect, int worldId)
        {
            if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                return false;
            if (GetControls(go, enabledOnly: false).Count == 0)
                return false;
            int cell = Grid.PosToCell(go);
            return rect == null || CellInRect(cell, rect, worldId);
        }

        private static bool MatchesQuery(Dictionary<string, object> info, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;
            return JsonConvert.SerializeObject(info).IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Dictionary<string, object> TargetInfo(GameObject go)
        {
            int cell = Grid.PosToCell(go);
            var building = go.GetComponent<Building>();
            var kpid = go.GetComponent<KPrefabID>();
            return new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? go.GetInstanceID(),
                ["prefabId"] = building?.Def?.PrefabID ?? kpid?.PrefabTag.Name ?? go.name,
                ["name"] = ToolUtil.CleanName(go.GetProperName()),
                ["x"] = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                ["y"] = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1,
                ["worldId"] = Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell) ? Grid.WorldIdx[cell] : -1
            };
        }

        private static string ResolveTitle(ICheckboxListGroupControl.ListGroup group)
        {
            return group.resolveTitleCallback == null
                ? group.title
                : SafeString(() => group.resolveTitleCallback(group.title));
        }

        private static string ResolveTooltip(ICheckboxListGroupControl.CheckboxItem item, ICheckboxListGroupControl control)
        {
            return item.resolveTooltipCallback == null
                ? item.tooltip
                : SafeString(() => item.resolveTooltipCallback(item.tooltip, control));
        }

        private static string SafeString(Func<string> getter)
        {
            try
            {
                return getter() ?? "";
            }
            catch (Exception ex)
            {
                return "<error: " + ex.GetType().Name + ">";
            }
        }

        private static bool SafeBool(Func<bool> getter)
        {
            try
            {
                return getter();
            }
            catch
            {
                return false;
            }
        }

        private static int SafeInt(Func<int> getter)
        {
            try
            {
                return getter();
            }
            catch
            {
                return 0;
            }
        }

        private static Dictionary<string, McpToolParameter> RectParams(Dictionary<string, McpToolParameter> extra)
        {
            var parameters = new Dictionary<string, McpToolParameter>
            {
                ["areaId"] = new McpToolParameter { Type = "string", Description = "可选区域句柄；提供后可省略 x1/y1/x2/y2", Required = false },
                ["x1"] = new McpToolParameter { Type = "integer", Description = "区域左下/起点 X；使用 areaId 时可省略", Required = false },
                ["y1"] = new McpToolParameter { Type = "integer", Description = "区域左下/起点 Y；使用 areaId 时可省略", Required = false },
                ["x2"] = new McpToolParameter { Type = "integer", Description = "区域右上/终点 X；使用 areaId 时可省略", Required = false },
                ["y2"] = new McpToolParameter { Type = "integer", Description = "区域右上/终点 Y；使用 areaId 时可省略", Required = false },
                ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认 areaId 绑定世界或当前激活世界", Required = false }
            };
            foreach (var item in extra)
                parameters[item.Key] = item.Value;
            return parameters;
        }

        private static bool HasRectInput(JObject args)
        {
            return !string.IsNullOrWhiteSpace(args["areaId"]?.ToString())
                   || ToolUtil.GetInt(args, "x1").HasValue
                   || ToolUtil.GetInt(args, "y1").HasValue
                   || ToolUtil.GetInt(args, "x2").HasValue
                   || ToolUtil.GetInt(args, "y2").HasValue;
        }

        private static bool CellInRect(int cell, Dictionary<string, int> rect, int worldId)
        {
            return Grid.IsValidCell(cell)
                   && ToolUtil.CellMatchesWorld(cell, worldId)
                   && Grid.CellColumn(cell) >= rect["x1"]
                   && Grid.CellColumn(cell) <= rect["x2"]
                   && Grid.CellRow(cell) >= rect["y1"]
                   && Grid.CellRow(cell) <= rect["y2"];
        }
    }
}
