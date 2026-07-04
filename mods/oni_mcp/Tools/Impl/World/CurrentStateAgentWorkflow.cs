using Newtonsoft.Json.Linq;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class CurrentStateReadTools
    {
        private static JArray FirstCallWorkflow()
        {
            return new JArray
            {
                new JObject { ["step"] = 1, ["call"] = "read_control domain=state action=current", ["why"] = "Low-token colony, camera, editable files, recommended second call. Add includeReachability=true only before dig/build/rescue reach checks." },
                new JObject
                {
                    ["step"] = 2,
                    ["call"] = "building_control domain=planning action=room_template kind=starter autoLayout=true priority=7 execute=true confirm=true",
                    ["structuredCall"] = StarterRoomTemplateCall()["exactSecondCall"]?.DeepClone(),
                    ["recommendedSecondCallRef"] = "response.recommendedSecondCall.exactSecondCall",
                    ["why"] = "One-call toilet, wash basin, research station, shell, doors, interior digs."
                },
                new JObject { ["step"] = 3, ["call"] = "Use response.verificationPlan first; otherwise read /active/map/cell_X_Y.md only failed cells.", ["why"] = "Verify locally without broad map scans." }
            };
        }

        private static JObject TokenBudget()
        {
            return new JObject
            {
                ["default"] = "snapshot + file index + next calls only",
                ["avoidByDefault"] = new JArray { "broad viewport maps", "Player.log tail", "all infrastructure ports", "full atmosphere scan" },
                ["expandWith"] = new JArray { "includeState=true", "includeInfrastructure=true infrastructureKind=power", "includeReachability=true reachabilityRadius=12", "includeLogs=true logLimit=160" },
                ["editLoop"] = "current -> one write -> compact result -> only failed cell/detail reads"
            };
        }

        private static JObject StarterRoomTemplateCall()
        {
            return new JObject
            {
                ["tool"] = "building_control",
                ["arguments"] = StarterRoomTemplateArguments(execute: true, confirm: true),
                ["exactSecondCall"] = StarterRoomTemplateToolCall(execute: true, confirm: true),
                ["dryRunVariant"] = StarterRoomTemplateToolCall(execute: false, confirm: false, dryRun: true),
                ["why"] = "One-call starter build: auto-selects room candidate, digs interiors, builds toilet, wash basin, research station.",
                ["expectedResult"] = "Returns executionPlan, priorityAction, rooms, generated calls, compact results, verificationPlan, nextActions.",
                ["postRunReads"] = StarterPostRunReads(),
                ["successCriteria"] = StarterSuccessCriteria(),
                ["onFailure"] = StarterFailureReads()
            };
        }

        private static JObject StarterRoomTemplateToolCall(bool execute, bool confirm, bool dryRun = false)
        {
            return new JObject
            {
                ["tool"] = "building_control",
                ["arguments"] = StarterRoomTemplateArguments(execute, confirm, dryRun)
            };
        }

        private static JObject StarterRoomTemplateArguments(bool execute, bool confirm, bool dryRun = false)
        {
            var args = new JObject
            {
                ["domain"] = "planning",
                ["action"] = "room_template",
                ["kind"] = "starter",
                ["autoLayout"] = true,
                ["priority"] = 7,
                ["execute"] = execute
            };

            if (confirm)
                args["confirm"] = true;
            if (dryRun)
                args["dryRun"] = true;

            return args;
        }

        private static JObject StarterPreflight()
        {
            return new JObject
            {
                ["goal"] = "Second call should create reachable starter toilet plus lab in one planning action.",
                ["defaultPriority"] = 7,
                ["mustInclude"] = new JArray
                {
                    "dig interior blocking natural tiles",
                    "build room shell, divider, doors",
                    "place Outhouse WashBasin",
                    "place ResearchCenter",
                    "return compact executionPlan, priorityAction, nextActions"
                },
                ["afterCallVerify"] = new JArray
                {
                    "read /active/diagnostics/logs.md crashes or tester failures",
                    "read /active/map/cell_X_Y.md any blocked building cell",
                    "read /active/dupes/reachability.md before rescue or access fixes"
                },
                ["avoid"] = "Do not scan broad maps unless starter response reports missing context."
            };
        }

        private static JArray StarterPostRunReads()
        {
            return new JArray
            {
                "Use response.verificationPlan[].arguments first; already contains exact world_editor/order calls.",
                "Read only returned starter cell paths for Outhouse, WashBasin, and ResearchCenter.",
                "Read local zoom only if compact result cell detail reports obstruction, heat, missing material, or unreachable errands.",
                "Preview sweep verificationPlan.debris_followup before issuing cleanup."
            };
        }

        private static JArray StarterSuccessCriteria()
        {
            return new JArray
            {
                "rooms contains toilet lab rectangles",
                "executionPlan includes dig_toilet_interior dig_lab_interior",
                "executionPlan includes build_shells_and_divider place_core_buildings",
                "verificationPlan includes verify_outhouse_cell, verify_wash_basin_cell, verify_research_station_cell",
                "results stay compact; broad maps are not required unless blocker appears"
            };
        }

        private static JArray StarterFailureReads()
        {
            return new JArray
            {
                new JObject
                {
                    ["when"] = "generated call reports blocked/obstructed/missing support",
                    ["tool"] = "world_editor",
                    ["arguments"] = new JObject { ["command"] = "read", ["path"] = "/active/map/cell_X_Y.md" },
                    ["why"] = "Cell detail objects, pickups, ports, temperature suitability, Decision Hints."
                },
                new JObject
                {
                    ["when"] = "work is unreachable dupes idle",
                    ["tool"] = "world_editor",
                    ["arguments"] = new JObject { ["command"] = "read", ["path"] = "/active/dupes/reachability.md", ["radius"] = 12, ["sampleLimit"] = 12 },
                    ["why"] = "Reachability should be checked before adding ladders rescue digs."
                },
                new JObject
                {
                    ["when"] = "exception/crash/unsafe diagnostic appears",
                    ["tool"] = "world_editor",
                    ["arguments"] = new JObject { ["command"] = "read", ["path"] = "/active/diagnostics/logs.md", ["logLimit"] = 220 },
                    ["why"] = "Read logs only on failure; normal loop should stay low token."
                }
            };
        }

        private static JArray StarterDecisionTree()
        {
            return new JArray
            {
                StarterDecision("ok=true generated work succeeded", "Follow response.nextActions; verify one core cell only if suspicious."),
                StarterDecision("missing material", "Use response.nextActions material hint; do not retry same material blindly."),
                StarterDecision("blocked/obstructed cell", "Read `/active/map/cell_X_Y.md` for the reported cell; it includes Quick Ops Next Reads."),
                StarterDecision("unreachable work", "Read `/active/dupes/reachability.md radius=12 sampleLimit=12` before adding ladders or digs."),
                StarterDecision("crash/error/exception", "Read `/active/diagnostics/logs.md logLimit=220`; avoid broad map reads first.")
            };
        }

        private static JObject StarterDecision(string condition, string next)
        {
            return new JObject { ["condition"] = condition, ["next"] = next };
        }

        private static JArray NextCalls()
        {
            return new JArray
            {
                StarterRoomTemplateCall(),
                new JObject
                {
                    ["tool"] = "read_control",
                    ["arguments"] = new JObject
                    {
                        ["domain"] = "world",
                        ["action"] = "layout_candidates",
                        ["purpose"] = "starter",
                        ["width"] = 15,
                        ["height"] = 4,
                        ["limit"] = 5,
                        ["maxCells"] = 1600
                    },
                    ["why"] = "Fallback only: inspect low-risk rectangles if one-call starter reports missing context or unsafe layout."
                },
                new JObject
                {
                    ["tool"] = "read_control",
                    ["arguments"] = new JObject
                    {
                        ["domain"] = "world",
                        ["action"] = "reachable_area",
                        ["radius"] = 12,
                        ["sampleLimit"] = 12
                    },
                    ["why"] = "Compact duplicant movement ranges before planning dig/build/rescue work."
                },
                LocalZoomNextCall(),
                new JObject
                {
                    ["tool"] = "read_control",
                    ["arguments"] = new JObject
                    {
                        ["domain"] = "world",
                        ["action"] = "text_map",
                        ["profile"] = "scan",
                        ["encoding"] = "rle",
                        ["maxCells"] = 1200
                    },
                    ["why"] = "Compact default viewport map if visual context is needed."
                }
            };
        }

        private static JObject LocalZoomNextCall()
        {
            var args = new JObject
            {
                ["command"] = "zoom",
                ["views"] = "default,power,temperature",
                ["compact"] = true,
                ["syncView"] = true,
                ["focusCamera"] = true
            };

            var camera = Camera.main;
            if (camera != null)
            {
                int cx = Mathf.Clamp(Mathf.RoundToInt(camera.transform.position.x), 0, Grid.WidthInCells - 1);
                int cy = Mathf.Clamp(Mathf.RoundToInt(camera.transform.position.y), 0, Grid.HeightInCells - 1);
                args["x1"] = Mathf.Clamp(cx - 10, 0, Grid.WidthInCells - 1);
                args["y1"] = Mathf.Clamp(cy - 7, 0, Grid.HeightInCells - 1);
                args["x2"] = Mathf.Clamp(cx + 10, 0, Grid.WidthInCells - 1);
                args["y2"] = Mathf.Clamp(cy + 7, 0, Grid.HeightInCells - 1);
            }
            else
            {
                args["path"] = "/active/map/zoom_X1_Y1_X2_Y2.md";
            }

            return new JObject
            {
                ["tool"] = "world_editor",
                ["arguments"] = args,
                ["why"] = "Preferred local visual context: multi-view zoom syncs live camera/view, then use cell_X_Y only for exact details."
            };
        }
    }
}
