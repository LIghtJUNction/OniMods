using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Server;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Tools
{
    public static class EditMarkTools
    {
        private static readonly object Lock = new object();
        private static readonly Dictionary<string, EditMarkRequest> PendingRequests = new Dictionary<string, EditMarkRequest>();
        private static int nextRequestId = 1;

        public static McpTool CreateEditMarkRequest()
        {
            return new McpTool
            {
                Name = "edit_mark_request_create",
                Group = "ui",
                Mode = "execute",
                Risk = "low",
                Aliases = new List<string> { "map_edit_mark_create", "agent_edit_area" },
                Description = "为指定矩形区域创建编辑标记请求：区域+提示词+文本地图上下文，交给 MCP 客户端 agent 先计划再执行；截图仅作可选视觉补充",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["prompt"] = new McpToolParameter { Type = "string", Description = "用户对框选区域的修改提示词", Required = true },
                    ["areaId"] = new McpToolParameter { Type = "string", Description = "可选区域句柄；提供后可省略 x1/y1/x2/y2", Required = false },
                    ["x1"] = new McpToolParameter { Type = "integer", Description = "区域起点/左下 X；使用 areaId 时可省略", Required = false },
                    ["y1"] = new McpToolParameter { Type = "integer", Description = "区域起点/左下 Y；使用 areaId 时可省略", Required = false },
                    ["x2"] = new McpToolParameter { Type = "integer", Description = "区域终点/右上 X；使用 areaId 时可省略", Required = false },
                    ["y2"] = new McpToolParameter { Type = "integer", Description = "区域终点/右上 Y；使用 areaId 时可省略", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID，默认当前激活世界", Required = false },
                    ["includeTextMap"] = new McpToolParameter { Type = "boolean", Description = "是否内联框选区域文本地图，默认 true；客户端应优先使用它而非截图", Required = false },
                    ["includeScreenshot"] = new McpToolParameter { Type = "boolean", Description = "是否附带当前屏幕截图路径，默认 false；仅作为视觉补充", Required = false }
                },
                Handler = args =>
                {
                    string prompt = NormalizePrompt(args["prompt"]?.ToString());
                    if (string.IsNullOrEmpty(prompt))
                        return CallToolResult.Error("prompt is required");

                    var rect = ToolUtil.GetRect(args);
                    int worldId = ToolUtil.GetInt(args, "worldId") ?? ClusterManager.Instance?.activeWorldId ?? 0;
                    bool includeTextMap = ToolUtil.GetBool(args, "includeTextMap", true);
                    bool includeScreenshot = ToolUtil.GetBool(args, "includeScreenshot", false);
                    var result = CreateRequest(rect, worldId, prompt, includeTextMap, includeScreenshot, "mcp_tool");
                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ListEditMarkRequests()
        {
            return new McpTool
            {
                Name = "edit_mark_request_list",
                Group = "ui",
                Mode = "read",
                Risk = "none",
                Description = "列出等待 MCP 客户端 agent 处理的编辑标记请求",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "返回数量，默认 20，最大 100", Required = false }
                },
                Handler = args =>
                {
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 20, 100));
                    List<Dictionary<string, object>> requests;
                    lock (Lock)
                    {
                        requests = PendingRequests.Values
                            .OrderByDescending(request => request.CreatedAt)
                            .Take(limit)
                            .Select(request => request.ToDictionary(includeClientRequest: false))
                            .ToList();
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["returned"] = requests.Count,
                        ["requests"] = requests
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ClearEditMarkRequest()
        {
            return new McpTool
            {
                Name = "edit_mark_request_clear",
                Group = "ui",
                Mode = "execute",
                Risk = "low",
                Description = "清除指定或全部编辑标记请求",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "string", Description = "请求 ID；留空且 all=true 时清除全部", Required = false },
                    ["all"] = new McpToolParameter { Type = "boolean", Description = "是否清除全部请求，默认 false", Required = false }
                },
                Handler = args =>
                {
                    bool all = ToolUtil.GetBool(args, "all", false);
                    string id = args["id"]?.ToString();
                    int removed = 0;

                    lock (Lock)
                    {
                        if (all)
                        {
                            removed = PendingRequests.Count;
                            PendingRequests.Clear();
                        }
                        else
                        {
                            if (string.IsNullOrWhiteSpace(id))
                                return CallToolResult.Error("id is required unless all=true");
                            if (PendingRequests.Remove(id.Trim()))
                                removed = 1;
                        }
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["removed"] = removed,
                        ["remaining"] = PendingRequests.Count
                    }, McpJsonUtil.Settings));
                }
            };
        }

        internal static Dictionary<string, object> CreateFromSelection(int x1, int y1, int x2, int y2, string prompt)
        {
            var rect = NormalizeRect(x1, y1, x2, y2);
            int worldId = ClusterManager.Instance?.activeWorldId ?? 0;
            return CreateRequest(rect, worldId, NormalizePrompt(prompt), includeTextMap: true, includeScreenshot: false, source: "in_game_tool");
        }

        internal static List<EditMarkSnapshot> GetPendingSnapshots(int limit = 20)
        {
            limit = Math.Max(1, Math.Min(limit, 100));
            lock (Lock)
            {
                return PendingRequests.Values
                    .OrderByDescending(request => request.CreatedAt)
                    .Take(limit)
                    .Select(request => request.ToSnapshot())
                    .ToList();
            }
        }

        private static Dictionary<string, object> CreateRequest(Dictionary<string, int> rect, int worldId, string prompt, bool includeTextMap, bool includeScreenshot, string source)
        {
            var area = AreaHandleRegistry.Define(rect, worldId, "edit_mark");
            string id;
            lock (Lock)
            {
                id = "em" + nextRequestId++.ToString("D4");
            }

            var request = new EditMarkRequest
            {
                Id = id,
                Prompt = prompt,
                AreaId = area.Id,
                WorldId = worldId,
                Rect = area.Rect(),
                TextMap = includeTextMap ? BuildTextMapContext(area.Id, area.Rect(), worldId) : null,
                ScreenshotPath = includeScreenshot ? TrySaveScreenshot(id) : null,
                Source = source,
                CreatedAt = System.DateTime.UtcNow
            };

            lock (Lock)
            {
                PendingRequests[id] = request;
            }

            TryNotifyRequest(request);
            return request.ToDictionary(includeClientRequest: true);
        }

        private static Dictionary<string, int> NormalizeRect(int x1, int y1, int x2, int y2)
        {
            var args = new JObject
            {
                ["x1"] = x1,
                ["y1"] = y1,
                ["x2"] = x2,
                ["y2"] = y2
            };
            return ToolUtil.GetRect(args);
        }

        private static string NormalizePrompt(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
                return "";
            prompt = prompt.Trim();
            return prompt.Length > 4000 ? prompt.Substring(0, 4000) : prompt;
        }

        private static string TrySaveScreenshot(string requestId)
        {
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "oni-mcp", "edit-marks");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, requestId + "_" + System.DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + ".png");
                ScreenCapture.CaptureScreenshot(path);
                return path;
            }
            catch (Exception ex)
            {
                OniMcpLog.Warning("[OniMcp] Failed to capture edit mark screenshot: " + ex.Message);
                return null;
            }
        }

        private static EditMarkTextMap BuildTextMapContext(string areaId, Dictionary<string, int> rect, int worldId)
        {
            try
            {
                var args = new JObject
                {
                    ["areaId"] = areaId,
                    ["worldId"] = worldId,
                    ["visibleOnly"] = true,
                    ["includeBuildings"] = true,
                    ["includeItems"] = true,
                    ["includeDupes"] = true,
                    ["includeElements"] = true,
                    ["includeSummary"] = true,
                    ["detail"] = "compact",
                    ["encoding"] = "plain",
                    ["profile"] = "minimal",
                    ["format"] = "text",
                    ["elementLimit"] = 40,
                    ["objectLimit"] = 120,
                    ["maxCells"] = 2500
                };

                var result = WorldAnalysisTools.GetWorldTextMap().Handler(args);
                string text = result == null || result.Content == null || result.Content.Count == 0
                    ? ""
                    : result.Content[0].Text ?? "";

                return new EditMarkTextMap
                {
                    Format = "world_text_map.text",
                    AreaId = areaId,
                    WorldId = worldId,
                    Rect = new[] { rect["x1"], rect["y1"], rect["x2"], rect["y2"] },
                    Text = result != null && result.IsError ? "" : text,
                    Error = result != null && result.IsError ? text : null
                };
            }
            catch (Exception ex)
            {
                OniMcpLog.Warning("[OniMcp] Failed to build edit mark text map: " + ex.Message);
                return new EditMarkTextMap
                {
                    Format = "world_text_map.text",
                    AreaId = areaId,
                    WorldId = worldId,
                    Rect = new[] { rect["x1"], rect["y1"], rect["x2"], rect["y2"] },
                    Error = ex.Message
                };
            }
        }

        private static void TryNotifyRequest(EditMarkRequest request)
        {
            try
            {
                TryPushClientRequest(request);
                if (NotificationManager.Instance == null)
                    return;

                var notification = new Notification(
                    "MCP 编辑标记已创建",
                    NotificationType.MessageImportant,
                    (notifications, data) => "请求 " + request.Id + " 正在等待客户端 agent 读取 edit_mark_request_list 并先计划再执行。",
                    request.Id,
                    true,
                    0f,
                    null,
                    null,
                    null,
                    true,
                    false,
                    true);
                NotificationManager.Instance.AddNotification(notification);
            }
            catch (Exception ex)
            {
                OniMcpLog.Warning("[OniMcp] Failed to show edit mark notification: " + ex.Message);
            }
        }

        private static void TryPushClientRequest(EditMarkRequest request)
        {
            try
            {
                var server = McpHttpServer.Instance;
                if (server == null)
                    return;

                JObject clientRequest = request.BuildClientRequest();
                int pushed = server.EnqueueClientRequest(clientRequest, requireSampling: true);
                if (pushed == 0)
                {
                    server.EnqueueClientNotification(
                        "info",
                        "OniMcp.EditMark",
                        new JObject
                        {
                            ["type"] = "edit_mark_request_created",
                            ["id"] = request.Id,
                            ["areaId"] = request.AreaId,
                            ["prompt"] = request.Prompt,
                            ["message"] = "MCP edit mark request created. Call edit_mark_request_list to read prompt, areaId, and textMap."
                        });
                }
            }
            catch (Exception ex)
            {
                OniMcpLog.Warning("[OniMcp] Failed to push edit mark client request: " + ex.Message);
            }
        }

        private sealed class EditMarkRequest
        {
            public string Id;
            public string Prompt;
            public string AreaId;
            public int WorldId;
            public Dictionary<string, int> Rect;
            public EditMarkTextMap TextMap;
            public string ScreenshotPath;
            public string Source;
            public System.DateTime CreatedAt;

            public EditMarkSnapshot ToSnapshot()
            {
                return new EditMarkSnapshot
                {
                    Id = Id,
                    Prompt = Prompt,
                    AreaId = AreaId,
                    WorldId = WorldId,
                    X1 = Rect["x1"],
                    Y1 = Rect["y1"],
                    X2 = Rect["x2"],
                    Y2 = Rect["y2"],
                    ScreenshotPath = ScreenshotPath,
                    HasTextMap = TextMap != null && !string.IsNullOrWhiteSpace(TextMap.Text),
                    Source = Source,
                    CreatedAt = CreatedAt
                };
            }

            public Dictionary<string, object> ToDictionary(bool includeClientRequest)
            {
                var data = new Dictionary<string, object>
                {
                    ["id"] = Id,
                    ["areaId"] = AreaId,
                    ["worldId"] = WorldId,
                    ["rect"] = Rect,
                    ["prompt"] = Prompt,
                    ["source"] = Source,
                    ["createdAtUtc"] = CreatedAt.ToString("o"),
                    ["contextPriority"] = new[] { "textMap", "world_text_map", "screenshotPath" },
                    ["textMap"] = TextMap,
                    ["screenshotPath"] = ScreenshotPath,
                    ["workflow"] = new Dictionary<string, object>
                    {
                        ["planFirst"] = true,
                        ["executeOnlyAfterPlan"] = true,
                        ["contextRule"] = "Use textMap first. Call world_text_map for more detail if needed. Use screenshotPath/game_screenshot only for visual confirmation.",
                        ["planRule"] = "Write a concise executable plan in the response, then use dryRun/validateOnly where available before execution. If a planned call is invalid, report the issue and revise before executing.",
                        ["recommendedTools"] = new[] { "world_text_map", "tools_search", "tools_call_many", "agent_pointer_jump", "agent_pointer_select_tool", "agent_pointer_left_click", "agent_pointer_hold_left", "orders_dig_area", "orders_sweep_area", "game_screenshot" }
                    }
                };

                if (includeClientRequest)
                    data["clientRequest"] = BuildClientRequest();

                return data;
            }

            public JObject BuildClientRequest()
            {
                string agentPrompt =
                    "你是 ONI MCP 客户端 agent。用户框选了一个游戏区域并给出修改提示词。\n" +
                    "必须先输出计划，列出将读取的上下文、预期改动和风险；计划完成后再调用 MCP 工具执行。不要跳过计划。\n" +
                    "优先使用下面的 textMap 文本地图理解区域；只有文本地图无法表达的视觉细节才使用 screenshotPath 或 game_screenshot。\n\n" +
                    "规划必须列出将调用的工具、关键参数、dryRun/验证步骤和风险；如果发现参数无效，先反馈并修正规划再执行。\n\n" +
                    "areaId: " + AreaId + "\n" +
                    "worldId: " + WorldId + "\n" +
                    "rect: " + JsonConvert.SerializeObject(Rect) + "\n" +
                    "textMap:\n" + (TextMap != null ? TextMap.Text : "") + "\n" +
                    "screenshotPath: " + (ScreenshotPath ?? "") + "\n" +
                    "用户提示词: " + Prompt;

                return new JObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = Guid.NewGuid().ToString("N"),
                    ["method"] = "sampling/createMessage",
                    ["params"] = new JObject
                    {
                        ["messages"] = new JArray
                        {
                            new JObject
                            {
                                ["role"] = "user",
                                ["content"] = new JObject
                                {
                                    ["type"] = "text",
                                    ["text"] = agentPrompt
                                }
                            }
                        },
                        ["maxTokens"] = 3000,
                        ["includeContext"] = "thisServer"
                    },
                    ["note"] = "Client-side MCP request object. Poll edit_mark_request_list or use this sampling request to wake an agent client."
                };
            }
        }

        internal sealed class EditMarkSnapshot
        {
            public string Id;
            public string Prompt;
            public string AreaId;
            public int WorldId;
            public int X1;
            public int Y1;
            public int X2;
            public int Y2;
            public string ScreenshotPath;
            public bool HasTextMap;
            public string Source;
            public System.DateTime CreatedAt;
        }

        public sealed class EditMarkTextMap
        {
            public string Format;
            public string AreaId;
            public int WorldId;
            public int[] Rect;
            public string Text;
            public string Error;
        }
    }
}
