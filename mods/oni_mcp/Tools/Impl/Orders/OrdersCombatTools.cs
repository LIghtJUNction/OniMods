using System;
using System.Collections.Generic;
using System.Linq;
using Klei.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class OrdersTools
    {
                public static McpTool Attack()
                {
                    return new McpTool
                    {
                        Name = "orders_attack",
                        Hidden = true,
                        Group = "orders",
                        Mode = "execute",
                        Risk = "dangerous",
                        Description = "兼容入口：请优先使用 orders_control domain=designation action=attack。仅用于攻击小动物/敌对目标，不能用于挖掘。区域攻击除 confirm=true 外还必须 action=mark 且 attackAreaConfirm=\"attack area\"",
                        Parameters = RectParams(new Dictionary<string, McpToolParameter>
                        {
                            ["id"] = new McpToolParameter { Type = "integer", Description = "目标对象 InstanceID；提供后优先于坐标/区域", Required = false },
                            ["x"] = new McpToolParameter { Type = "integer", Description = "目标格子 X；不提供矩形时按单格查找", Required = false },
                            ["y"] = new McpToolParameter { Type = "integer", Description = "目标格子 Y；不提供矩形时按单格查找", Required = false },
                            ["action"] = new McpToolParameter { Type = "string", Description = "mark 标记攻击，cancel 取消攻击；默认 mark", Required = false, EnumValues = new List<string> { "mark", "cancel" } },
                            ["priority"] = new McpToolParameter { Type = "integer", Description = "攻击差事优先级 1-9，默认 5", Required = false },
                            ["topPriority"] = new McpToolParameter { Type = "boolean", Description = "是否设为红色最高优先级，默认 false", Required = false },
                            ["force"] = new McpToolParameter { Type = "boolean", Description = "允许标记友方/协助阵营目标，默认 false", Required = false },
                            ["attackAreaConfirm"] = new McpToolParameter { Type = "string", Description = "区域攻击二次确认；矩形区域 mark 攻击必须精确填写 attack area，防止把挖掘误调成攻击", Required = false },
                            ["confirm"] = new McpToolParameter { Type = "boolean", Description = "危险操作确认；标记攻击时必须为 true", Required = false }
                        }),
                        Handler = args =>
                        {
                            string action = (args["action"]?.ToString() ?? "mark").Trim().ToLowerInvariant();
                            bool mark = action != "cancel";
                            if (mark && !ToolUtil.GetBool(args, "confirm", false))
                                return CallToolResult.Error("confirm=true is required for attack orders");
                            bool areaAttack = mark && args["id"] == null && HasRectInput(args);
                            if (areaAttack && args["attackAreaConfirm"]?.ToString() != "attack area")
                                return CallToolResult.Error("Refusing area attack without attackAreaConfirm=\"attack area\". For terrain excavation use orders_control domain=area action=dig, not orders_attack.");
                            if (FactionManager.Instance == null)
                                return CallToolResult.Error("FactionManager is not initialized");

                            var targets = FindAttackTargets(args);
                            if (targets.Count == 0)
                                return CallToolResult.Error("No attack target found");

                            bool force = ToolUtil.GetBool(args, "force", false);
                            int changed = 0;
                            int skipped = 0;
                            var results = new List<Dictionary<string, object>>();
                            foreach (var target in targets)
                            {
                                var go = target?.gameObject;
                                if (go == null)
                                    continue;

                                if (mark && !force && FactionManager.Instance.GetDisposition(FactionManager.FactionID.Duplicant, target.Alignment) == FactionManager.Disposition.Assist)
                                {
                                    skipped++;
                                    results.Add(TargetResult(go, target, "skipped_assist_faction"));
                                    continue;
                                }

                                target.SetPlayerTargeted(mark);
                                if (mark)
                                    ApplyPriority(go, args);

                                changed++;
                                results.Add(TargetResult(go, target, mark ? "marked" : "cancelled"));
                            }

                            return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                            {
                                ["action"] = mark ? "mark" : "cancel",
                                ["changed"] = changed,
                                ["skipped"] = skipped,
                                ["targets"] = results
                            }, McpJsonUtil.Settings));
                        }
                    };
                }
    }
}
