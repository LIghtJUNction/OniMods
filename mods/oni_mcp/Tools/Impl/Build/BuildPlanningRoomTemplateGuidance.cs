using System;
using Newtonsoft.Json.Linq;

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
    }
}
