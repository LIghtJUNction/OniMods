using System.Collections.Generic;
using Newtonsoft.Json;
using OniMcp.Config;
using OniMcp.Core;
using OniMcp.Server;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class ServerTools
    {
        public static McpTool GetMcpStatus()
        {
            return new McpTool
            {
                Name = "server_status",
                Group = "server",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "get_mcp_status" },
                Description = "兼容旧名；建议使用 server_diagnostics_control action=status",
                Parameters = new Dictionary<string, McpToolParameter>(),
                Handler = args =>
                {
                    var server = McpHttpServer.Instance;
                    var status = new Dictionary<string, object>
                    {
                        ["loaded"] = server != null,
                        ["endpoint"] = server?.EndpointUrl,
                        ["port"] = server?.Port ?? 0,
                        ["configPath"] = OniMcpOptions.ConfigPath,
                        ["toolCount"] = OniToolRegistry.GetVisibleTools().Count,
                        ["listedToolCount"] = OniToolRegistry.GetDefaultToolInfoCount(),
                        ["toolsListMode"] = "core",
                        ["discovery"] = "tools/list and catalog return the public aggregate tools needed for normal play; hidden legacy aliases are internal compatibility only."
                    };

                    return CallToolResult.Text(JsonConvert.SerializeObject(status, McpJsonUtil.Settings));
                }
            };
        }
    }
}
