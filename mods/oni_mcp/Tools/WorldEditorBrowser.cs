using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Net;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        public static object BuildBrowserListing(string rawPath, string mcpEndpoint, string protocolVersion)
        {
            string path = NormalizePath(rawPath, "/");
            if (!IsDirectory(path))
            {
                string fileContent = "";
                string errorMessage = null;
                try
                {
                    var readResult = Read(new JObject { ["path"] = path });
                    if (readResult.IsError)
                    {
                        errorMessage = readResult.Content?.FirstOrDefault()?.Text ?? "Unknown error";
                    }
                    else
                    {
                        var firstContent = readResult.Content?.FirstOrDefault();
                        if (firstContent != null)
                        {
                            fileContent = firstContent.Text;
                        }
                    }
                }
                catch (Exception ex)
                {
                    errorMessage = ex.Message;
                }

                string parent = "/";
                string trimmed = path.TrimEnd('/');
                int lastSlash = trimmed.LastIndexOf('/');
                if (lastSlash > 0)
                {
                    parent = trimmed.Substring(0, lastSlash + 1);
                }

                return new
                {
                    server = "OniMcp",
                    protocolVersion,
                    mcpEndpoint,
                    path,
                    isFile = true,
                    content = fileContent,
                    error = errorMessage,
                    parent
                };
            }

            var entries = ListEntries(path);
            if (path != "/")
            {
                string parent = "/";
                string trimmed = path.TrimEnd('/');
                int lastSlash = trimmed.LastIndexOf('/');
                if (lastSlash > 0)
                {
                    parent = trimmed.Substring(0, lastSlash + 1);
                }
                var list = new List<object>();
                list.Add(new { name = "..", type = "dir", path = parent, description = "Go to parent directory." });
                if (entries != null)
                {
                    list.AddRange(entries);
                }
                entries = list;
            }
            return new
            {
                server = "OniMcp",
                protocolVersion,
                mcpEndpoint,
                path,
                model = "save-folders-and-code-files",
                editContract = "<<<<<<< SEARCH\\n...old observed text...\\n=======\\n...new desired text...\\n>>>>>>> REPLACE",
                note = "/mcp/ is the MCP JSON-RPC endpoint. This browser view lists virtual save/world files for feedback.",
                entries
            };
        }

        private static CallToolResult Cd(JObject args)
        {
            string path = Text(args, "path");
            if (string.IsNullOrWhiteSpace(path) || path == "~")
            {
                _cwd = "/";
                return JsonResult(new JObject { ["cwd"] = _cwd, ["state"] = "main_menu" });
            }

            string resolved = NormalizePath(path, _cwd);
            if (!IsDirectory(resolved))
                return CallToolResult.Error("not a virtual directory: " + resolved);
            _cwd = resolved;
            return JsonResult(new JObject { ["cwd"] = _cwd });
        }

        private static CallToolResult Ls(JObject args)
        {
            string path = NormalizePath(Text(args, "path"), _cwd);
            if (!IsDirectory(path))
                return CallToolResult.Error("not a virtual directory: " + path);
            return JsonResult(new JObject { ["cwd"] = _cwd, ["path"] = path, ["entries"] = JArray.FromObject(ListEntries(path)) });
        }

        private static string GetColonyName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "Unknown";
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(dir))
                    return Path.GetFileNameWithoutExtension(path);
                string parent = Path.GetFileName(dir);
                if (string.Equals(parent, "auto_save", StringComparison.OrdinalIgnoreCase))
                {
                    string grandDir = Path.GetDirectoryName(dir);
                    if (!string.IsNullOrEmpty(grandDir))
                        return Path.GetFileName(grandDir);
                }
                return parent;
            }
            catch
            {
                return Path.GetFileNameWithoutExtension(path);
            }
        }

        private static string GetActiveSaveName()
        {
            try
            {
                string active = SaveLoader.GetActiveSaveFilePath();
                if (!string.IsNullOrEmpty(active))
                    return Path.GetFileNameWithoutExtension(active);
            }
            catch {}
            return null;
        }

        private static string GetVirtualSaveName(string saveFileName, string colonyName)
        {
            if (string.IsNullOrEmpty(saveFileName)) return "base";
            if (string.Equals(saveFileName, colonyName, StringComparison.OrdinalIgnoreCase))
            {
                return "base";
            }
            if (saveFileName.StartsWith(colonyName, StringComparison.OrdinalIgnoreCase))
            {
                string rest = saveFileName.Substring(colonyName.Length).Trim();
                rest = rest.TrimStart('-', '_').Trim();
                if (string.IsNullOrEmpty(rest))
                    return "base";
                return rest;
            }
            return saveFileName;
        }

        private static bool ResolveSaveFilePath(string path, out string saveFilePath)
        {
            saveFilePath = null;
            if (!path.StartsWith("/saves/", StringComparison.Ordinal))
                return false;

            string rest = path.Substring("/saves/".Length);

            // 1. Overall latest
            if (rest == "latest" || rest == "latest/" || rest.StartsWith("latest/", StringComparison.Ordinal))
            {
                try
                {
                    var files = SaveLoader.GetAllFiles(sort: true, type: SaveLoader.SaveType.both);
                    if (files != null && files.Count > 0)
                    {
                        saveFilePath = files[0].path;
                        return true;
                    }
                }
                catch {}
                return false;
            }

            int firstSlash = rest.IndexOf('/');
            if (firstSlash < 0)
            {
                return false;
            }

            string colonyName = rest.Substring(0, firstSlash);
            string subRest = rest.Substring(firstSlash + 1);

            // Get all files for this colony
            List<SaveLoader.SaveFileEntry> colonyFiles = null;
            try
            {
                var files = SaveLoader.GetAllFiles(sort: true, type: SaveLoader.SaveType.both);
                if (files != null)
                {
                    colonyFiles = files.Where(f => string.Equals(GetColonyName(f.path), colonyName, StringComparison.OrdinalIgnoreCase)).ToList();
                }
            }
            catch {}

            if (colonyFiles == null || colonyFiles.Count == 0)
                return false;

            // 2. Colony latest
            if (subRest == "latest" || subRest == "latest/" || subRest.StartsWith("latest/", StringComparison.Ordinal))
            {
                saveFilePath = colonyFiles[0].path;
                return true;
            }

            // 3. Specific save file (matched using GetVirtualSaveName)
            int secondSlash = subRest.IndexOf('/');
            string virtualSaveName = secondSlash > 0 ? subRest.Substring(0, secondSlash) : subRest;
            if (virtualSaveName.EndsWith("/", StringComparison.Ordinal))
                virtualSaveName = virtualSaveName.TrimEnd('/');

            foreach (var file in colonyFiles)
            {
                string sName = Path.GetFileNameWithoutExtension(file.path);
                string mappedName = GetVirtualSaveName(sName, colonyName);
                if (string.Equals(mappedName, virtualSaveName, StringComparison.OrdinalIgnoreCase))
                {
                    saveFilePath = file.path;
                    return true;
                }
            }

            return false;
        }

        private static bool IsActiveSavePath(string path)
        {
            string saveFilePath;
            if (ResolveSaveFilePath(path, out saveFilePath))
            {
                try
                {
                    string active = SaveLoader.GetActiveSaveFilePath();
                    if (!string.IsNullOrEmpty(active) && !string.IsNullOrEmpty(saveFilePath))
                        return string.Equals(active, saveFilePath, StringComparison.OrdinalIgnoreCase);
                }
                catch {}
            }
            return false;
        }

        private static string GetSaveNameFromPath(string path)
        {
            string saveFilePath;
            if (ResolveSaveFilePath(path, out saveFilePath))
            {
                return Path.GetFileNameWithoutExtension(saveFilePath);
            }
            return null;
        }

    }
}
