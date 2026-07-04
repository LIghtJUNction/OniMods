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
