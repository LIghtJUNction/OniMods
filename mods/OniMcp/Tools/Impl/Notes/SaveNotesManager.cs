using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Tools
{
    /// <summary>
    /// 按游戏存档隔离的文件夹式游戏笔记管理器
    /// 包含每个存档专属的 AGENTS.md 背景信息、子目录管理与视觉反馈。
    /// </summary>
    public static class SaveNotesManager
    {
        private const string AgentsTemplate = @"# AGENTS.md - Colony Core Background & Guidelines

## Colony Profile
- Colony Name: {0}
- Strategy: Sustainable Survival, SPOM Oxygen, Food Security, ST/AT Industrial Cooling
- Management: Autonomous Agent Paired Play

## Core Objectives
1. Food Security: Maintain >= 30,000 kcal buffer before admitting new duplicants.
2. Oxygen Security: Build closed SPOM; keep living quarters at 1000g~1800g oxygen pressure.
3. Thermal Management: Insulate base core; plan ST/AT cooling loop for industrial heat sources.
4. Power Grid: Transition from manual/coal to hydrogen, natural gas, and petroleum.

## Inviolable Taboos & Rules
- NEVER enable auto-disinfect globally.
- NEVER dig into unknown liquid/gas reservoirs blindly without inspection and containment barriers.
- Observe -> Plan -> Act -> Short Resume -> Pause -> Verify loop at all times.
- For Printing Pod, prefer care packages; do not add duplicants unless explicitly requested.

## Milestone Log
- Cycle 1: Bootstrap initialized. AGENTS.md recorded as core reference.
";

        private static string GetString(JObject args, params string[] keys)
        {
            if (args == null) return null;
            foreach (var k in keys)
            {
                var token = args[k];
                if (token != null && token.Type != JTokenType.Null)
                {
                    string s = token.ToString();
                    if (!string.IsNullOrWhiteSpace(s))
                        return s.Trim();
                }
            }
            return null;
        }

        private static CallToolResult JsonResult(JObject obj)
        {
            return CallToolResult.Text(obj.ToString(Formatting.Indented));
        }

        public static string GetActiveColonyName()
        {
            try
            {
                if (SaveLoader.Instance != null && !string.IsNullOrEmpty(SaveLoader.Instance.GameInfo.baseName))
                    return SaveLoader.Instance.GameInfo.baseName;
            }
            catch { }
            return "default_colony";
        }

        public static string GetNotesRootDirectory(bool autoInitAgentsMd = true)
        {
            string colonyFolder = null;
            try
            {
                colonyFolder = SaveLoader.GetActiveSaveColonyFolder();
            }
            catch { }

            if (string.IsNullOrWhiteSpace(colonyFolder))
            {
                string baseName = GetActiveColonyName();
                colonyFolder = Path.Combine(Application.persistentDataPath, "colony_notes", baseName);
            }

            string notesRoot = Path.GetFullPath(Path.Combine(colonyFolder, "notes"));
            if (!Directory.Exists(notesRoot))
            {
                Directory.CreateDirectory(notesRoot);
            }

            if (autoInitAgentsMd)
            {
                string agentsFile = Path.Combine(notesRoot, "AGENTS.md");
                if (!File.Exists(agentsFile))
                {
                    try
                    {
                        string content = string.Format(AgentsTemplate, GetActiveColonyName());
                        File.WriteAllText(agentsFile, content, Encoding.UTF8);
                    }
                    catch (Exception ex)
                    {
                        OniMcpLog.Warning("[SaveNotesManager] Failed to init AGENTS.md: " + ex.Message);
                    }
                }
            }

            return notesRoot;
        }

        public static bool TryResolveNotePath(string input, bool mustExist, out string fullPath, out string relativePath, out string error)
        {
            fullPath = null;
            relativePath = null;
            error = null;

            string root = GetNotesRootDirectory(true);
            string clean = (input ?? string.Empty).Trim().Replace('\\', '/');
            if (clean.StartsWith("/active/notes/", StringComparison.OrdinalIgnoreCase))
                clean = clean.Substring("/active/notes/".Length);
            else if (clean.StartsWith("notes/", StringComparison.OrdinalIgnoreCase))
                clean = clean.Substring("notes/".Length);

            if (string.IsNullOrWhiteSpace(clean))
            {
                fullPath = root;
                relativePath = string.Empty;
                return true;
            }

            if (Path.IsPathRooted(clean) || clean.Contains("..") || clean.Contains(":"))
            {
                error = "Note path must be relative to notes/ and cannot escape the notes directory.";
                return false;
            }

            string candidate = Path.GetFullPath(Path.Combine(root, clean));
            string canonicalRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(canonicalRoot, StringComparison.Ordinal) && candidate != root)
            {
                error = "Note path escapes the active colony notes directory.";
                return false;
            }

            if (mustExist && !File.Exists(candidate) && !Directory.Exists(candidate))
            {
                error = "Note or directory not found: " + clean;
                return false;
            }

            fullPath = candidate;
            relativePath = clean;
            return true;
        }

        public static CallToolResult ListNotes(JObject args)
        {
            string sub = GetString(args, "folder", "category", "path");
            if (!TryResolveNotePath(sub, false, out string targetDir, out string relDir, out string error))
                return CallToolResult.Error(error);

            if (!Directory.Exists(targetDir))
                return CallToolResult.Error("Target notes directory does not exist: " + relDir);

            string root = GetNotesRootDirectory(true);
            var entries = new JArray();
            try
            {
                foreach (string dir in Directory.GetDirectories(targetDir).OrderBy(d => d))
                {
                    string dirName = Path.GetFileName(dir);
                    string rel = dir.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
                    int count = Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length;
                    entries.Add(new JObject
                    {
                        ["name"] = dirName + "/",
                        ["type"] = "dir",
                        ["path"] = rel + "/",
                        ["fileCount"] = count
                    });
                }

                foreach (string file in Directory.GetFiles(targetDir).OrderBy(f => f))
                {
                    var fi = new FileInfo(file);
                    string rel = file.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
                    int lineCount = 0;
                    try { lineCount = File.ReadAllLines(file).Length; } catch { }
                    entries.Add(new JObject
                    {
                        ["name"] = fi.Name,
                        ["type"] = "file",
                        ["path"] = rel,
                        ["size"] = fi.Length,
                        ["lines"] = lineCount,
                        ["modified"] = fi.LastWriteTimeUtc.ToString("o")
                    });
                }
            }
            catch (Exception ex)
            {
                return CallToolResult.Error("Failed to list notes: " + ex.Message);
            }

            return JsonResult(new JObject
            {
                ["ok"] = true,
                ["colony"] = GetActiveColonyName(),
                ["directory"] = string.IsNullOrEmpty(relDir) ? "notes/" : "notes/" + relDir + "/",
                ["count"] = entries.Count,
                ["entries"] = entries
            });
        }

        public static CallToolResult ReadNote(JObject args)
        {
            string target = GetString(args, "path", "name", "file") ?? "AGENTS.md";
            if (!TryResolveNotePath(target, true, out string fullPath, out string relPath, out string error))
                return CallToolResult.Error(error);

            if (Directory.Exists(fullPath))
                return ListNotes(args);

            try
            {
                string text = File.ReadAllText(fullPath, Encoding.UTF8);
                ShowVisualFeedback($"📝 正在查阅笔记: {Path.GetFileName(relPath)}");
                return CallToolResult.Text(text);
            }
            catch (Exception ex)
            {
                return CallToolResult.Error("Failed to read note: " + ex.Message);
            }
        }

        public static string ReadNoteContent(string inputPath)
        {
            string target = string.IsNullOrWhiteSpace(inputPath) ? "AGENTS.md" : inputPath;
            if (!TryResolveNotePath(target, true, out string fullPath, out _, out _))
                return null;
            if (!File.Exists(fullPath))
                return null;
            try
            {
                return File.ReadAllText(fullPath, Encoding.UTF8);
            }
            catch
            {
                return null;
            }
        }

        public static CallToolResult WriteNote(JObject args, bool isAppend = false)
        {
            string target = GetString(args, "path", "name", "file");
            if (string.IsNullOrWhiteSpace(target))
                return CallToolResult.Error("Note write requires target path (e.g. AGENTS.md, plans/spom.md).");

            if (!TryResolveNotePath(target, false, out string fullPath, out string relPath, out string error))
                return CallToolResult.Error(error);

            string content = GetString(args, "content", "text") ?? string.Empty;
            try
            {
                string parent = Path.GetDirectoryName(fullPath);
                if (!Directory.Exists(parent))
                    Directory.CreateDirectory(parent);

                if (isAppend)
                {
                    File.AppendAllText(fullPath, (File.Exists(fullPath) ? Environment.NewLine : string.Empty) + content, Encoding.UTF8);
                    ShowVisualFeedback($"📝 笔记已追加: {Path.GetFileName(relPath)}");
                }
                else
                {
                    File.WriteAllText(fullPath, content, Encoding.UTF8);
                    ShowVisualFeedback($"📝 笔记已更新: {Path.GetFileName(relPath)}");
                }

                return JsonResult(new JObject
                {
                    ["ok"] = true,
                    ["colony"] = GetActiveColonyName(),
                    ["path"] = "notes/" + relPath,
                    ["action"] = isAppend ? "append" : "write",
                    ["bytesWritten"] = Encoding.UTF8.GetByteCount(content)
                });
            }
            catch (Exception ex)
            {
                return CallToolResult.Error("Failed to write note: " + ex.Message);
            }
        }

        public static CallToolResult DeleteNote(JObject args)
        {
            string target = GetString(args, "path", "name", "file");
            if (string.IsNullOrWhiteSpace(target))
                return CallToolResult.Error("Note delete requires path.");

            if (!TryResolveNotePath(target, true, out string fullPath, out string relPath, out string error))
                return CallToolResult.Error(error);

            if (relPath.Equals("AGENTS.md", StringComparison.OrdinalIgnoreCase))
                return CallToolResult.Error("AGENTS.md is the core colony background document and cannot be deleted.");

            try
            {
                if (Directory.Exists(fullPath))
                {
                    if (Directory.GetFileSystemEntries(fullPath).Length > 0 && !ToolUtil.GetBool(args, "recursive", false))
                        return CallToolResult.Error("Directory is not empty; pass recursive=true to delete.");
                    Directory.Delete(fullPath, true);
                    ShowVisualFeedback($"📝 目录已删除: {relPath}/");
                }
                else if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                    ShowVisualFeedback($"📝 笔记已删除: {Path.GetFileName(relPath)}");
                }

                return JsonResult(new JObject
                {
                    ["ok"] = true,
                    ["deleted"] = "notes/" + relPath
                });
            }
            catch (Exception ex)
            {
                return CallToolResult.Error("Failed to delete note: " + ex.Message);
            }
        }

        public static CallToolResult RenameNote(JObject args)
        {
            string from = GetString(args, "from", "source", "name");
            string to = GetString(args, "to", "target", "destination");
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
                return CallToolResult.Error("Note rename requires 'from' and 'to'.");

            if (!TryResolveNotePath(from, true, out string fullFrom, out string relFrom, out string err1))
                return CallToolResult.Error(err1);
            if (!TryResolveNotePath(to, false, out string fullTo, out string relTo, out string err2))
                return CallToolResult.Error(err2);

            try
            {
                string targetDir = Path.GetDirectoryName(fullTo);
                if (!Directory.Exists(targetDir))
                    Directory.CreateDirectory(targetDir);

                if (File.Exists(fullFrom))
                    File.Move(fullFrom, fullTo);
                else if (Directory.Exists(fullFrom))
                    Directory.Move(fullFrom, fullTo);

                ShowVisualFeedback($"📝 笔记已重命名: {Path.GetFileName(relFrom)} -> {Path.GetFileName(relTo)}");
                return JsonResult(new JObject
                {
                    ["ok"] = true,
                    ["from"] = "notes/" + relFrom,
                    ["to"] = "notes/" + relTo
                });
            }
            catch (Exception ex)
            {
                return CallToolResult.Error("Failed to rename note: " + ex.Message);
            }
        }

        public static CallToolResult GetStatus()
        {
            string root = GetNotesRootDirectory(true);
            string colony = GetActiveColonyName();
            int fileCount = 0;
            int dirCount = 0;
            try
            {
                fileCount = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length;
                dirCount = Directory.GetDirectories(root, "*", SearchOption.AllDirectories).Length;
            }
            catch { }

            string agentsPath = Path.Combine(root, "AGENTS.md");
            bool hasAgents = File.Exists(agentsPath);
            ShowVisualFeedback($"📝 殖民地笔记 [{colony}] - 共 {fileCount} 篇笔记");

            return JsonResult(new JObject
            {
                ["ok"] = true,
                ["colony"] = colony,
                ["notesDirectory"] = root,
                ["agentsMdInitialized"] = hasAgents,
                ["fileCount"] = fileCount,
                ["folderCount"] = dirCount
            });
        }

        private static void ShowVisualFeedback(string message)
        {
            try
            {
                ToolCallSpeechOverlay.ShowNearPlayerMouse(message, 3.5f);
            }
            catch { }
        }
    }
}
