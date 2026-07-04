using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    public static partial class CurrentStateReadTools
    {
        private static JArray BuildInfrastructureWorkflow()
        {
            return new JArray
            {
                WorkflowStep("power_lines", "/active/infrastructure/power.md",
                    "Use glyph/dirs/links/to and Endpoint Anchors to verify wires, bridges, producers, consumers, batteries."),
                WorkflowStep("liquid_pipes", "/active/infrastructure/liquid_conduits.md",
                    "Use dirs/links/to plus input/output endpoints; bridgePorts are jumps, not direct pipe links."),
                WorkflowStep("gas_pipes", "/active/infrastructure/gas_conduits.md",
                    "Use dirs/links/to plus input/output endpoints; verify nearby ports before issuing pipe fixes."),
                WorkflowStep("logic_wires", "/active/infrastructure/logic.md",
                    "Use logicIn/logicOut anchors and bridgePorts; do not infer automation direction from glyph alone."),
                WorkflowStep("solid_rails", "/active/infrastructure/solid_conveyor.md",
                    "Use rail links plus loader input and receptacle output anchors before conveyor edits."),
                WorkflowStep("single_cell_crosscheck", "/active/map/cell_X_Y.md",
                    "Cell snapshot joins pivot/footprint, ports, line links, bridge text, power role, items, and Decision Hints."),
                new JObject
                {
                    ["step"] = "line_repair_rules",
                    ["why"] = "Use this before editing broken wires/pipes/rails/signals.",
                    ["rules"] = new JArray
                    {
                        "`*` or `dirs=.` means this cell has infrastructure but no detected edge; inspect `open` and adjacent cell detail before connecting.",
                        "`open=R:(x,y)` means adjacent infrastructure exists but this cell is not connected that way; repair by rebuilding/patching only that missing edge.",
                        "`bridgePorts=from:(x,y) via:⌒ to:(x,y)` is a bridge jump, not normal neighbor continuity; do not replace bridge cells with straight line glyphs.",
                        "`⊗` marks input/consumer/entrance; `⊙` marks output/generator/exit. Match ports to nearby line cells before issuing build fixes.",
                        "For small fixes prefer local zoom or `/active/map/cell_X_Y.md`; avoid broad viewport reads after every failed connection."
                    },
                    ["quickReads"] = new JArray
                    {
                        new JObject
                        {
                            ["tool"] = "world_editor",
                            ["arguments"] = new JObject
                            {
                                ["command"] = "read",
                                ["path"] = "/active/map/cell_X_Y.md"
                            }
                        },
                        new JObject
                        {
                            ["tool"] = "read_control",
                            ["arguments"] = new JObject
                            {
                                ["domain"] = "infrastructure",
                                ["action"] = "nearby_ports",
                                ["x"] = "target-x",
                                ["y"] = "target-y",
                                ["radius"] = 4,
                                ["kind"] = "all"
                            }
                        }
                    }
                },
                new JObject
                {
                    ["step"] = "nearby_ports",
                    ["tool"] = "read_control",
                    ["arguments"] = new JObject
                    {
                        ["domain"] = "infrastructure",
                        ["action"] = "nearby_ports",
                        ["x"] = "target-x",
                        ["y"] = "target-y",
                        ["radius"] = 8,
                        ["kind"] = "all"
                    },
                    ["why"] = "Low-token local port search when cell detail says a building has missing input/output."
                }
            };
        }

        private static JObject WorkflowStep(string step, string path, string why)
        {
            return new JObject
            {
                ["step"] = step,
                ["tool"] = "world_editor",
                ["arguments"] = new JObject { ["command"] = "read", ["path"] = path },
                ["why"] = why
            };
        }
    }
}
