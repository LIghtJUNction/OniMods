using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Tools
{
    /// <summary>
    /// 殖民地笔记工具：对接 SaveNotesManager 提供按存档隔离的笔记管理
    /// </summary>
    public static class ColonyNotesTools
    {
        public static McpTool ControlNotes()
        {
            return new McpTool
            {
                Name = "colony_notes_control",
                Group = "colony",
                Mode = "execute",
                Risk = "medium",
                Aliases = new List<string> { "save_notes", "colony_notes" },
                Tags = new List<string> { "colony", "notes", "agents", "journal", "plans", "save-isolated" },
                Description = "按当前游戏存档隔离的文件夹式游戏笔记工具。支持管理 AGENTS.md 核心背景与计划里程碑。action: list, read, write, append, delete, rename, status。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "笔记操作：list、read、write、append、delete、rename、status", Required = true, EnumValues = new List<string> { "list", "ls", "read", "write", "append", "delete", "rm", "rename", "mv", "status" } },
                    ["path"] = new McpToolParameter { Type = "string", Description = "笔记相对路径，如 AGENTS.md、plans/spom.md、milestones/cycle-50.md", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "path 的别名", Required = false },
                    ["content"] = new McpToolParameter { Type = "string", Description = "write 或 append 时的笔记正文内容", Required = false },
                    ["from"] = new McpToolParameter { Type = "string", Description = "rename 时的原路径", Required = false },
                    ["to"] = new McpToolParameter { Type = "string", Description = "rename 时的目标路径", Required = false },
                    ["folder"] = new McpToolParameter { Type = "string", Description = "list 时的子目录，默认为根目录", Required = false },
                    ["recursive"] = new McpToolParameter { Type = "boolean", Description = "delete 目录时是否递归删除非空目录，默认 false", Required = false }
                },
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "list").Trim().ToLowerInvariant();
                    switch (action)
                    {
                        case "list":
                        case "ls":
                            return SaveNotesManager.ListNotes(args);
                        case "read":
                        case "open":
                        case "cat":
                            return SaveNotesManager.ReadNote(args);
                        case "write":
                        case "create":
                        case "save":
                            return SaveNotesManager.WriteNote(args, isAppend: false);
                        case "append":
                        case "log":
                            return SaveNotesManager.WriteNote(args, isAppend: true);
                        case "delete":
                        case "remove":
                        case "rm":
                            return SaveNotesManager.DeleteNote(args);
                        case "rename":
                        case "move":
                        case "mv":
                            return SaveNotesManager.RenameNote(args);
                        case "status":
                        case "info":
                            return SaveNotesManager.GetStatus();
                        default:
                            return CallToolResult.Error("Notes action must be list, read, write, append, delete, rename, or status.");
                    }
                }
            };
        }
    }
}
