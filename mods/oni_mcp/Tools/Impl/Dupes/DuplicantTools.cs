using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Klei.AI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class DuplicantTools
{
        private static readonly MethodInfo ConsumerStopChoreMethod = typeof(ChoreConsumer).GetMethod("StopChore", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly MethodInfo ChoreCancelWithStringMethod = typeof(Chore).GetMethod("Cancel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(string) }, null);

        private static readonly MethodInfo ChoreCancelNoArgsMethod = typeof(Chore).GetMethod("Cancel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);

        private static readonly MethodInfo BrainCancelChoreMethod = typeof(MinionBrain).GetMethod("CancelChore", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(string) }, null);

        private static readonly MethodInfo BrainCancelFetchesMethod = typeof(MinionBrain).GetMethod("CancelFetches", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(string) }, null);

        private static readonly MethodInfo MoveMonitorCancelMethod = typeof(MoveToLocationMonitor.Instance).GetMethod("CancelChore", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);

        public static McpTool ControlDupes()
        {
            return new McpTool
            {
                Name = "dupes_control",
                Group = "dupes",
                Mode = "execute",
                Risk = "medium",
                Aliases = new List<string> { "duplicants_control", "dupe_control" },
                Tags = new List<string> { "dupes", "duplicants", "priority", "skills", "hats", "assignable", "commands", "side-screen" },
                Description = "Unified duplicant compatibility entrypoint. domain=info/priority/hat/command/side_screen/skill/assignable; use action plus name/dupeName/query/target/search/id to locate and execute targets. Coordinate input is not accepted here; use coordinate_control for exact coordinate operations. action is forwarded to the matching legacy control.",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["domain"] = new McpToolParameter { Type = "string", Description = "复制人子系统：info、priority、hat、command、side_screen、skill、assignable", Required = true, EnumValues = new List<string> { "info", "priority", "hat", "command", "side_screen", "skill", "assignable" } },
                    ["action"] = new McpToolParameter { Type = "string", Description = "子系统动作。info=detail/attributes/needs/status_check；priority=list/set/batch/settings_get/settings_set/reset；hat=list/set；command=move_to/move_batch_to/force_action/rename/auto_rename；side_screen=direct_commands/equipment/todos/bionic_upgrades；skill=list/learn；assignable=list/set/set_slot", Required = true },
                    ["id"] = new McpToolParameter { Type = "integer", Description = "复制人、目标对象或可分配对象 InstanceID", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "复制人或目标对象名称", Required = false },
                    ["dupeId"] = new McpToolParameter { Type = "integer", Description = "复制人 InstanceID；部分写操作使用", Required = false },
                    ["dupeName"] = new McpToolParameter { Type = "string", Description = "复制人名称；部分写操作使用", Required = false },
                    ["targetId"] = new McpToolParameter { Type = "integer", Description = "目标对象 InstanceID", Required = false },
                    ["targetName"] = new McpToolParameter { Type = "string", Description = "目标对象名称", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "目标格 X", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "目标格 Y", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID；读操作过滤或坐标查找使用", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "读取动作的关键词过滤", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回或处理数量", Required = false },
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "domain=priority 写操作的优先级", Required = false },
                    ["skillId"] = new McpToolParameter { Type = "string", Description = "domain=skill action=learn 时的技能 ID", Required = false },
                    ["hatId"] = new McpToolParameter { Type = "string", Description = "domain=hat action=set 时的帽子 ID", Required = false },
                    ["newName"] = new McpToolParameter { Type = "string", Description = "domain=command action=rename 时的新名称", Required = false },
                    ["style"] = new McpToolParameter { Type = "string", Description = "domain=command action=auto_rename 时的命名风格", Required = false },
                    ["apply"] = new McpToolParameter { Type = "boolean", Description = "domain=command action=auto_rename 是否实际应用", Required = false },
                    ["commandAction"] = new McpToolParameter { Type = "string", Description = "domain=command action=force_action 时的底层动作", Required = false },
                    ["slotId"] = new McpToolParameter { Type = "string", Description = "domain=assignable action=set_slot 时的槽位 ID", Required = false },
                    ["itemId"] = new McpToolParameter { Type = "integer", Description = "domain=assignable action=set_slot 时的物品 InstanceID", Required = false },
                    ["includeDetails"] = new McpToolParameter { Type = "boolean", Description = "部分 info/side_screen 读取动作是否包含更多细节", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "底层写入、移动、强制动作或批量动作按原 control 要求确认", Required = false }
                },
                Handler = args =>
                {
                    string domain = (args["domain"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    switch (domain)
                    {
                        case "info":
                        case "read":
                            return ControlDupeInfo().Handler(args);
                        case "priority":
                        case "priorities":
                            return ControlPersonalPriority().Handler(args);
                        case "hat":
                        case "hats":
                            return ControlHat().Handler(args);
                        case "command":
                        case "commands":
                            return ControlDupeCommands().Handler(args);
                        case "side_screen":
                        case "side":
                        case "sidescreen":
                            return ControlDupeSideScreens().Handler(args);
                        case "skill":
                        case "skills":
                            return ControlSkill().Handler(args);
                        case "assignable":
                        case "assignables":
                            return ControlAssignable().Handler(args);
                        default:
                            return CallToolResult.Error("domain must be info, priority, hat, command, side_screen, skill, or assignable");
                    }
                }
            };
        }
}
}
