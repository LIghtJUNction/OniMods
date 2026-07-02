using System.Collections.Generic;
using Newtonsoft.Json;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class OrdersTools
    {
        public static McpTool DeconstructBuilding()
        {
            return new McpTool
            {
                Name = "buildings_deconstruct",
                Hidden = true,
                Group = "buildings",
                Mode = "execute",
                Risk = "dangerous",
                Description = "兼容入口：请优先使用 orders_control domain=designation action=deconstruct。将指定建筑标记为拆除，需要 confirm=true；dryRun=true 只预览。",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "危险操作确认，执行时必须为 true", Required = false },
                    ["dryRun"] = new McpToolParameter { Type = "boolean", Description = "只预览目标，不排拆除订单", Required = false }
                }),
                Handler = args =>
                {
                    var go = FindTarget(args);
                    if (go == null)
                        return CallToolResult.Error("Target not found");

                    var deconstructable = go.GetComponent<Deconstructable>();
                    if (deconstructable == null)
                        return CallToolResult.Error("Target is not deconstructable");

                    if (ToolUtil.GetBool(args, "dryRun", false))
                    {
                        return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                        {
                            ["dryRun"] = true,
                            ["wouldQueue"] = true,
                            ["target"] = DeconstructionTargetInfo(go),
                            ["next"] = "Re-run with confirm=true and dryRun=false to queue deconstruction."
                        }, McpJsonUtil.Settings));
                    }

                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required for deconstruction");

                    deconstructable.QueueDeconstruction(userTriggered: true);
                    return CallToolResult.Text($"Queued deconstruction for {go.GetProperName()}");
                }
            };
        }

        private static Dictionary<string, object> DeconstructionTargetInfo(UnityEngine.GameObject go)
        {
            var kpid = go.GetComponent<KPrefabID>();
            int cell = Grid.PosToCell(go);
            return new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? 0,
                ["name"] = go.GetProperName(),
                ["prefabId"] = kpid?.PrefabTag.Name ?? go.name,
                ["x"] = Grid.CellColumn(cell),
                ["y"] = Grid.CellRow(cell),
                ["worldId"] = go.GetMyWorldId()
            };
        }
    }
}
