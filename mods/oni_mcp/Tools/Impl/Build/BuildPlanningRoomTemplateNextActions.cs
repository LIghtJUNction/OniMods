using System;
using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
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
                ["tool"] = "tools_call_many",
                ["arguments"] = new JObject
                {
                    ["responseMode"] = "summary",
                    ["maxTextChars"] = 900,
                    ["calls"] = calls
                },
                ["why"] = "Verify Outhouse, WashBasin, and ResearchCenter core cells in one low-token call; each cell shows debris, temperature, ports, Decision Hints, quick ops."
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
    }
}
