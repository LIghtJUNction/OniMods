using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static JArray BuildRoomTemplateNextActions(string kind, RoomTemplateAnchor anchor, int priority)
        {
            return new JArray
            {
                new JObject
                {
                ["tool"] = "world_editor",
                    ["arguments"] = new JObject
                    {
                    ["command"] = "read",
                    ["path"] = "/active/map/cell_" + (anchor.X + 2) + "_" + (anchor.Y + 1) + ".md"
                    },
                    ["why"] = kind == "starter"
                        ? "Verify every returned rooms[].coreCells[].cellPath for Outhouse, WashBasin, ResearchCenter; each cell shows debris, material status, temperature, ports, Decision Hints, and quick ops."
                        : "Verify core building cell, debris, material status, temperature, ports, Decision Hints, and quick ops."
                },
                new JObject
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
                    ["why"] = "Check whether newly dug debris can be swept without issuing a blind order."
                },
                new JObject
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
                    ["compact"] = true
                    },
                    ["why"] = kind == "starter" ? "Confirm toilet lab shells, doors, oxygen, heat, and power anchors in one synced view." : "Confirm shell, door, and core building alignment."
                }
            };
        }

    }
}
