using System;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static JArray BuildRoomTemplateExecutionPlan(string kind, RoomTemplateAnchor anchor, int priority)
        {
            var steps = new JArray();
            if (kind == "starter")
            {
                int roomWidth = Math.Max(7, (anchor.Width - 1) / 2);
                steps.Add(RoomStep("dig_toilet_interior", "orders_control", "dig",
                    Rect(anchor.X + 1, anchor.Y + 1, anchor.X + roomWidth - 2, anchor.Y + anchor.Height - 2),
                    priority, "Clear natural solids inside toilet room before furniture errands."));
                steps.Add(RoomStep("dig_lab_interior", "orders_control", "dig",
                    Rect(anchor.X + roomWidth + 2, anchor.Y + 1, anchor.X + anchor.Width - 2, anchor.Y + anchor.Height - 2),
                    priority, "Clear natural solids inside lab before research station."));
                steps.Add(RoomStep("build_shells_and_divider", "building_control", "build_area",
                    Rect(anchor.X, anchor.Y, anchor.X + anchor.Width - 1, anchor.Y + anchor.Height - 1),
                    priority, "Build room floors, ceilings, walls, middle divider, doors."));
                steps.Add(RoomStep("place_core_buildings", "building_control", "build_area",
                    Rect(anchor.X + 2, anchor.Y + 1, anchor.X + anchor.Width - 3, anchor.Y + 1),
                    priority, "Place Outhouse, WashBasin, and ResearchCenter in same template call."));
            }
            else
            {
                steps.Add(RoomStep("dig_interior", "orders_control", "dig",
                    Rect(anchor.X + 1, anchor.Y + 1, anchor.X + anchor.Width - 2, anchor.Y + anchor.Height - 2),
                    priority, "Clear natural solids inside room before furniture errands."));
                steps.Add(RoomStep("build_shell_and_core", "building_control", "build_area",
                    Rect(anchor.X, anchor.Y, anchor.X + anchor.Width - 1, anchor.Y + anchor.Height - 1),
                    priority, "Build shell, door, core building."));
                steps.Add(RoomStep("optional_sweep_debris", "orders_control", "sweep",
                    Rect(anchor.X + 1, anchor.Y + 1, anchor.X + anchor.Width - 2, anchor.Y + anchor.Height - 2),
                    priority, "Sweep newly dug debris if pathing allows."));
            }

            return steps;
        }

        private static JArray BuildRoomTemplateVerificationPlan(string kind, RoomTemplateAnchor anchor, int priority)
        {
            string purpose = kind == "starter"
                ? "Confirm toilet, wash station, research station, interior digs, temperature, oxygen, power anchors in one compact view."
                : "Confirm room shell, door, core building, interior dig, temperature, oxygen.";
            var plan = new JArray
            {
                new JObject
                {
                    ["step"] = "inspect_compact_result",
                    ["why"] = "Use results[].summary/errors first; avoid broad map reads unless blocker appears."
                },
                ZoomRead("verify_local_zoom", anchor, purpose)
            };

            if (kind == "starter")
            {
                int roomWidth = Math.Max(7, (anchor.Width - 1) / 2);
                int washBasinX = anchor.X + Math.Max(4, roomWidth - 3);
                plan.Add(new JObject
                {
                    ["step"] = "expected_anchor_cells",
                    ["why"] = "Use these exact cells to verify the one-call starter template without counting columns.",
                    ["cells"] = new JArray
                    {
                        ExpectedCell("Outhouse", anchor.X + 2, anchor.Y + 1),
                        ExpectedCell("WashBasin", washBasinX, anchor.Y + 1),
                        ExpectedCell("ResearchCenter", anchor.X + roomWidth + 3, anchor.Y + 1)
                    }
                });
                AddCellRead(plan, "verify_outhouse_cell", anchor.X + 2, anchor.Y + 1,
                    "Verify outhouse blueprint/building, debris, temperature, element, Decision Hints.");
                AddCellRead(plan, "verify_wash_basin_cell", washBasinX, anchor.Y + 1,
                    "Verify wash basin blueprint/building, ports if any, debris, Decision Hints.");
                AddCellRead(plan, "verify_research_station_cell", anchor.X + roomWidth + 3, anchor.Y + 1,
                    "Verify lab research station blueprint/building without counting columns.");
            }
            else
            {
                AddCellRead(plan, "verify_core_cell", anchor.X + 2, anchor.Y + 1,
                    "Check building, pickup stacks, element, temperature, ports, Decision Hints, and obstruction first core cell.");
            }

            plan.Add(new JObject
            {
                ["step"] = "verify_reachability",
                ["tool"] = "world_editor",
                ["arguments"] = new JObject
                {
                    ["command"] = "read",
                    ["path"] = "/active/dupes/reachability.md",
                    ["radius"] = 12,
                    ["sampleLimit"] = 12
                },
                ["call"] = "world_editor command=read path=/active/dupes/reachability.md radius=12 sampleLimit=12",
                ["why"] = "Check reachable area before adding rescue ladders or extra digs."
            });
            plan.Add(new JObject
            {
                ["step"] = "debris_followup",
                ["tool"] = "orders_control",
                ["arguments"] = new JObject
                {
                    ["domain"] = "area",
                    ["action"] = "sweep",
                    ["x1"] = anchor.X,
                    ["y1"] = anchor.Y,
                    ["x2"] = anchor.X + anchor.Width - 1,
                    ["y2"] = anchor.Y + anchor.Height - 1,
                    ["priority"] = priority,
                    ["dryRun"] = true
                },
                ["call"] = "orders_control domain=area action=sweep x1=" + anchor.X
                    + " y1=" + anchor.Y
                    + " x2=" + (anchor.X + anchor.Width - 1)
                    + " y2=" + (anchor.Y + anchor.Height - 1)
                    + " priority=" + priority + " dryRun=true",
                ["why"] = "Preview sweeping newly dug debris before issuing cleanup."
            });
            plan.Add(new JObject
            {
                ["step"] = "if_failed",
                ["tool"] = "world_editor",
                ["arguments"] = new JObject
                {
                    ["command"] = "read",
                    ["path"] = "/active/diagnostics/logs.md",
                    ["logLimit"] = 220
                },
                ["call"] = "world_editor command=read path=/active/diagnostics/logs.md logLimit=220",
                ["why"] = "Only read logs when generated work fails, crashes, or returns unsafe diagnostics."
            });
            return plan;
        }

        private static JObject ZoomRead(string step, RoomTemplateAnchor anchor, string why)
        {
            var args = new JObject
            {
                ["command"] = "zoom",
                ["views"] = "default,power,oxygen,temperature",
                ["compact"] = true,
                ["syncView"] = true,
                ["focusCamera"] = true,
                ["x1"] = anchor.X,
                ["y1"] = anchor.Y,
                ["x2"] = anchor.X + anchor.Width - 1,
                ["y2"] = anchor.Y + anchor.Height - 1
            };
            return new JObject
            {
                ["step"] = step,
                ["tool"] = "world_editor",
                ["arguments"] = args,
                ["call"] = "world_editor command=zoom views=default,power,oxygen,temperature compact=true x1="
                    + anchor.X + " y1=" + anchor.Y
                    + " x2=" + (anchor.X + anchor.Width - 1)
                    + " y2=" + (anchor.Y + anchor.Height - 1),
                ["why"] = why
            };
        }

        private static JObject ExpectedCell(string prefabId, int x, int y)
        {
            return new JObject
            {
                ["prefabId"] = prefabId,
                ["x"] = x,
                ["y"] = y,
                ["cellPath"] = "/active/map/cell_" + x + "_" + y + ".md"
            };
        }

        private static void AddCellRead(JArray plan, string step, int x, int y, string why)
        {
            string path = "/active/map/cell_" + x + "_" + y + ".md";
            plan.Add(new JObject
            {
                ["step"] = step,
                ["tool"] = "world_editor",
                ["arguments"] = new JObject { ["command"] = "read", ["path"] = path },
                ["call"] = "world_editor command=read path=" + path,
                ["why"] = why
            });
        }

        private static JObject RoomStep(string name, string tool, string action, JObject rect, int priority, string why)
        {
            return new JObject
            {
                ["name"] = name,
                ["tool"] = tool,
                ["action"] = action,
                ["priority"] = priority,
                ["rect"] = rect,
                ["why"] = why
            };
        }

        private static JObject Rect(int x1, int y1, int x2, int y2)
        {
            return new JObject { ["x1"] = x1, ["y1"] = y1, ["x2"] = x2, ["y2"] = y2 };
        }

        private static JArray BuildRoomTemplateNextActions(string kind, RoomTemplateAnchor anchor, int priority)
        {
            var actions = new JArray();
            if (kind == "starter")
                actions.Add(StarterCoreCellBatchRead(anchor));
            else
                actions.Add(CoreCellRead(anchor.X + 2, anchor.Y + 1, "Verify core building cell, debris, material status, temperature, ports, Decision Hints, quick ops."));

            actions.Add(SweepDryRun(anchor, priority));
            actions.Add(ZoomTemplateRead(kind, anchor));
            return actions;
        }

        private static JObject StarterCoreCellBatchRead(RoomTemplateAnchor anchor)
        {
            int roomWidth = Math.Max(7, (anchor.Width - 1) / 2);
            int washBasinX = anchor.X + Math.Max(4, roomWidth - 3);
            var calls = new JArray
            {
                BatchReadCell("verify_outhouse_cell", anchor.X + 2, anchor.Y + 1),
                BatchReadCell("verify_wash_basin_cell", washBasinX, anchor.Y + 1),
                BatchReadCell("verify_research_station_cell", anchor.X + roomWidth + 3, anchor.Y + 1)
            };

            return new JObject
            {
                ["tool"] = "server_control",
                ["arguments"] = new JObject
                {
                    ["domain"] = "batch",
                    ["action"] = "call_many",
                    ["responseMode"] = "summary",
                    ["maxTextChars"] = 900,
                    ["calls"] = calls
                },
                ["why"] = "Use the public batch endpoint to verify Outhouse, WashBasin, and ResearchCenter core cells in one low-token call; each cell shows debris, temperature, ports, Decision Hints, quick ops."
            };
        }

        private static JObject BatchReadCell(string id, int x, int y)
        {
            return new JObject
            {
                ["id"] = id,
                ["name"] = "world_editor",
                ["arguments"] = new JObject
                {
                    ["command"] = "read",
                    ["path"] = CellPath(x, y)
                }
            };
        }

        private static JObject CoreCellRead(int x, int y, string why)
        {
            return new JObject
            {
                ["tool"] = "world_editor",
                ["arguments"] = new JObject
                {
                    ["command"] = "read",
                    ["path"] = CellPath(x, y)
                },
                ["why"] = why
            };
        }

        private static JObject SweepDryRun(RoomTemplateAnchor anchor, int priority)
        {
            return new JObject
            {
                ["tool"] = "orders_control",
                ["arguments"] = new JObject
                {
                    ["domain"] = "area",
                    ["action"] = "sweep",
                    ["x1"] = anchor.X,
                    ["y1"] = anchor.Y,
                    ["x2"] = anchor.X + anchor.Width - 1,
                    ["y2"] = anchor.Y + anchor.Height - 1,
                    ["priority"] = priority,
                    ["dryRun"] = true
                },
                ["why"] = "Preview newly dug debris cleanup without issuing a blind sweep order."
            };
        }

        private static JObject ZoomTemplateRead(string kind, RoomTemplateAnchor anchor)
        {
            return new JObject
            {
                ["tool"] = "world_editor",
                ["arguments"] = new JObject
                {
                    ["command"] = "zoom",
                    ["x1"] = anchor.X,
                    ["y1"] = anchor.Y,
                    ["x2"] = anchor.X + anchor.Width - 1,
                    ["y2"] = anchor.Y + anchor.Height - 1,
                    ["views"] = "default,power,oxygen,temperature",
                    ["compact"] = true,
                    ["syncView"] = true,
                    ["focusCamera"] = true
                },
                ["why"] = kind == "starter"
                    ? "Confirm toilet lab shells, doors, oxygen, heat, power anchors, and camera framing in one synced view."
                    : "Confirm shell, door, core building alignment, and camera framing."
            };
        }

        private static string CellPath(int x, int y)
        {
            return "/active/map/cell_" + x + "_" + y + ".md";
        }

        private static RoomTemplateAnchor TryAutoRoomTemplateAnchor(JObject args, string kind, int worldId, out string error)
        {
            error = null;
            bool requestedAutoLayout = ToolUtil.GetBool(args, "autoLayout", false) || ToolUtil.GetBool(args, "auto", false);
            if (!requestedAutoLayout && kind != "starter")
                return null;

            var layoutArgs = new JObject
            {
                ["action"] = "layout_candidates",
                ["purpose"] = kind == "starter" ? "starter" : kind,
                ["width"] = ToolUtil.GetInt(args, "width") ?? DefaultRoomTemplateWidth(kind),
                ["height"] = ToolUtil.GetInt(args, "height") ?? 4,
                ["limit"] = 1,
                ["maxCells"] = ToolUtil.GetInt(args, "maxCells") ?? 1600,
                ["worldId"] = worldId
            };

            CallToolResult result = WorldAnalysisTools.GetLayoutCandidates().Handler(layoutArgs);
            string text = result.Content?.FirstOrDefault()?.Text ?? string.Empty;
            if (result.IsError)
            {
                error = "autoLayout layout_candidates failed: " + TrimRoomTemplateText(text, 400);
                return null;
            }

            var rect = (JObject.Parse(text)["planning"]?["candidates"]?.FirstOrDefault()?["rect"]) as JArray;
            if (rect == null || rect.Count < 4)
            {
                error = "autoLayout found no room candidates.";
                return null;
            }

            var rectDict = new Dictionary<string, int>
            {
                ["x1"] = rect[0].Value<int>(),
                ["y1"] = rect[1].Value<int>(),
                ["x2"] = rect[2].Value<int>(),
                ["y2"] = rect[3].Value<int>()
            };
            return BuildRoomTemplateAnchor(args, kind, rectDict["x1"], rectDict["y1"], worldId, rectDict);
        }

        private static JObject DiagnoseRoomTemplateResult(string text, bool isError)
        {
            var diagnostic = new JObject { ["status"] = isError ? "error" : "ok" };
            if (string.IsNullOrWhiteSpace(text))
                return diagnostic;

            string haystack = text;
            try
            {
                JObject obj = JObject.Parse(text);
                foreach (string key in new[] { "error", "reason", "message", "summary", "failedReason", "status" })
                {
                    string value = obj[key]?.ToString();
                    if (!string.IsNullOrWhiteSpace(value))
                        haystack += "\n" + value;
                }
            }
            catch
            {
            }

            string lower = haystack.ToLowerInvariant();
            string category = null;
            if (ContainsAny(lower, "obstruct", "blocked", "occupied", "阻挡", "堵塞", "占用", "被占", "挡住"))
                category = "obstructed";
            else if (ContainsAny(lower, "material", "resource", "材料", "资源", "原料", "缺少"))
                category = "missing_material";
            else if (ContainsAny(lower, "support", "foundation", "支撑", "地基", "依托"))
                category = "missing_support";
            else if (ContainsAny(lower, "reach", "access", "不可达", "无法到达", "够不到", "路径"))
                category = "unreachable";
            else if (ContainsAny(lower, "confirm", "确认", "confirm=true"))
                category = "missing_confirm";

            if (!string.IsNullOrEmpty(category))
            {
                diagnostic["category"] = category;
                diagnostic["nextRead"] = category == "unreachable"
                    ? "/active/dupes/reachability.md"
                    : "/active/map/cell_X_Y.md";
                diagnostic["hint"] = "Use verificationPlan or nextActions before broad map reads.";
            }

            return diagnostic;
        }

        private static bool ContainsAny(string text, params string[] needles)
        {
            foreach (string needle in needles)
            {
                if (!string.IsNullOrEmpty(needle) && text.Contains(needle))
                    return true;
            }

            return false;
        }
    }
}
