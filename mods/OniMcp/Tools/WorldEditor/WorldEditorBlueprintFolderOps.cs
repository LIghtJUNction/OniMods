using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static void ShowBlueprintVisualFeedback(string message)
        {
            try
            {
                ToolCallSpeechOverlay.ShowNearPlayerMouse(message, 4f);
            }
            catch { }
        }

        private static CallToolResult BlueprintList(JObject args)
        {
            string category = Text(args, "category", "folder");
            string root = BlueprintRootFullPath();
            string targetDir = root;
            if (!string.IsNullOrWhiteSpace(category))
            {
                string cleanCat = category.Trim().Trim('/', '\\').Replace('\\', '/');
                if (cleanCat.Contains("..") || cleanCat.Contains(":"))
                    return CallToolResult.Error("Invalid category folder name.");
                targetDir = Path.GetFullPath(Path.Combine(root, cleanCat));
                if (!targetDir.StartsWith(root, StringComparison.Ordinal))
                    return CallToolResult.Error("Category escapes blueprint directory.");
            }

            if (!Directory.Exists(targetDir))
                return CallToolResult.Error("Blueprint category or directory not found: " + category);

            var folders = new JArray();
            var blueprints = new JArray();

            try
            {
                foreach (string dir in Directory.GetDirectories(targetDir).OrderBy(d => d))
                {
                    string dirName = Path.GetFileName(dir);
                    string rel = dir.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
                    int count = Directory.GetFiles(dir, "*.blueprint", SearchOption.AllDirectories).Length;
                    folders.Add(new JObject
                    {
                        ["name"] = dirName,
                        ["path"] = rel + "/",
                        ["blueprintCount"] = count
                    });
                }

                SearchOption opt = ToolUtil.GetBool(args, "recursive", true) ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                foreach (string file in Directory.GetFiles(targetDir, "*.blueprint", opt).OrderBy(f => f))
                {
                    if (!IsBlueprintDiskPath(file) || !ValidateBlueprintPathComponents(file, out _))
                        continue;

                    string rel = file.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
                    var fi = new FileInfo(file);
                    JObject bp = ReadBlueprintJson(file);
                    BlueprintRect rect = BlueprintBounds(bp);
                    int bCount = (bp["buildings"] as JArray)?.Count ?? 0;
                    string cat = Path.GetDirectoryName(rel).Replace('\\', '/');

                    blueprints.Add(new JObject
                    {
                        ["name"] = Path.GetFileNameWithoutExtension(file),
                        ["file"] = rel,
                        ["category"] = string.IsNullOrEmpty(cat) ? "root" : cat,
                        ["width"] = rect.Width,
                        ["height"] = rect.Height,
                        ["buildings"] = bCount,
                        ["sizeBytes"] = fi.Length,
                        ["modified"] = fi.LastWriteTimeUtc.ToString("o")
                    });
                }
            }
            catch (Exception ex)
            {
                return CallToolResult.Error("Failed to list blueprints: " + ex.Message);
            }

            ShowBlueprintVisualFeedback($"📐 蓝图管理 - 发现 {blueprints.Count} 个蓝图，{folders.Count} 个分类");
            return JsonResult(new JObject
            {
                ["ok"] = true,
                ["category"] = string.IsNullOrEmpty(category) ? "root" : category,
                ["categories"] = folders,
                ["blueprints"] = blueprints
            });
        }

        private static CallToolResult BlueprintImport(JObject args)
        {
            string name = Text(args, "name", "target", "path");
            string category = Text(args, "category", "folder");
            if (string.IsNullOrWhiteSpace(name))
                return CallToolResult.Error("blueprint import requires 'name'.");

            string combined = !string.IsNullOrWhiteSpace(category) && !name.Contains("/")
                ? category.Trim().Trim('/', '\\') + "/" + name.Trim()
                : name.Trim();

            if (!TryGetBlueprintPath(combined, false, out string path, out string pathError))
                return CallToolResult.Error(pathError);

            if (File.Exists(path) && !ToolUtil.GetBool(args, "overwrite", false))
                return CallToolResult.Error("Blueprint already exists; pass overwrite=true to replace.");

            string raw = Text(args, "content", "data", "text", "bundle");
            if (string.IsNullOrWhiteSpace(raw))
                return CallToolResult.Error("blueprint import requires 'content' (JSON, Base64, or Markdown).");

            string content = raw.Trim();
            if (!content.StartsWith("{", StringComparison.Ordinal) && !content.Contains("Y="))
            {
                try
                {
                    byte[] bytes = Convert.FromBase64String(content);
                    content = Encoding.UTF8.GetString(bytes);
                }
                catch { }
            }

            JObject root = BuildBlueprintCreateJson(path, content, out string error);
            if (root == null)
                return CallToolResult.Error(error);

            if (!WorldEditorExecutionAllowed(args))
                return WorldEditorPreview("blueprint_import", "/active/blueprints/" + combined, new JObject { ["target"] = path });

            if (!AtomicWriteBlueprint(path, root.ToString(Formatting.Indented), out error))
                return CallToolResult.Error(error);

            ShowBlueprintVisualFeedback($"📐 蓝图导入成功: {Path.GetFileNameWithoutExtension(path)}");
            return JsonResult(new JObject
            {
                ["ok"] = true,
                ["path"] = path,
                ["buildings"] = (root["buildings"] as JArray)?.Count ?? 0,
                ["message"] = "Blueprint imported successfully."
            });
        }

        private static CallToolResult BlueprintExport(JObject args)
        {
            string path = FindBlueprintPath(Text(args, "name", "path", "target"));
            if (path == null)
                return CallToolResult.Error("Blueprint not found. Pass name=...");

            string format = Text(args, "format", "profile").ToLowerInvariant();
            JObject bp = ReadBlueprintJson(path);
            BlueprintRect rect = BlueprintBounds(bp);
            string baseName = Path.GetFileNameWithoutExtension(path);

            ShowBlueprintVisualFeedback($"📐 导出蓝图: {baseName}");

            if (format == "base64")
            {
                string json = ReadBlueprintText(path);
                string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
                return JsonResult(new JObject
                {
                    ["ok"] = true,
                    ["name"] = baseName,
                    ["format"] = "base64",
                    ["data"] = b64
                });
            }

            if (format == "markdown" || format == "md")
                return CallToolResult.Text(ReadBlueprintMarkdown(path));

            return JsonResult(new JObject
            {
                ["ok"] = true,
                ["name"] = baseName,
                ["width"] = rect.Width,
                ["height"] = rect.Height,
                ["buildings"] = (bp["buildings"] as JArray)?.Count ?? 0,
                ["json"] = bp
            });
        }

        private static CallToolResult BlueprintShare(JObject args)
        {
            string name = Text(args, "name", "path", "target");
            string category = Text(args, "category", "folder");
            var paths = new List<string>();

            if (!string.IsNullOrWhiteSpace(name))
            {
                string p = FindBlueprintPath(name);
                if (p != null) paths.Add(p);
            }
            else if (!string.IsNullOrWhiteSpace(category))
            {
                string root = BlueprintRootFullPath();
                string dir = Path.GetFullPath(Path.Combine(root, category.Trim().Trim('/', '\\')));
                if (Directory.Exists(dir))
                {
                    foreach (string f in Directory.GetFiles(dir, "*.blueprint", SearchOption.AllDirectories))
                        if (IsBlueprintDiskPath(f)) paths.Add(f);
                }
            }

            if (paths.Count == 0)
                return CallToolResult.Error("No matching blueprints found to share. Pass name=... or category=...");

            var bundleItems = new JArray();
            foreach (string p in paths)
            {
                JObject bp = ReadBlueprintJson(p);
                bundleItems.Add(new JObject
                {
                    ["name"] = Path.GetFileNameWithoutExtension(p),
                    ["file"] = Path.GetFileName(p),
                    ["data"] = bp
                });
            }

            var bundle = new JObject
            {
                ["type"] = "oni_blueprint_bundle",
                ["version"] = 1,
                ["count"] = bundleItems.Count,
                ["items"] = bundleItems
            };

            string bundleJson = bundle.ToString(Formatting.None);
            string base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(bundleJson));

            ShowBlueprintVisualFeedback($"📐 蓝图分享代码已生成 ({bundleItems.Count} 个蓝图)");
            return JsonResult(new JObject
            {
                ["ok"] = true,
                ["count"] = bundleItems.Count,
                ["shareCode"] = base64,
                ["bundle"] = bundle
            });
        }

        private static CallToolResult BlueprintRename(JObject args)
        {
            string from = Text(args, "from", "source", "name");
            string to = Text(args, "to", "target", "destination");
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
                return CallToolResult.Error("blueprint rename requires 'from' and 'to'.");

            string sourcePath = FindBlueprintPath(from);
            if (sourcePath == null)
                return CallToolResult.Error("Source blueprint not found: " + from);

            if (!TryGetBlueprintPath(to, false, out string destPath, out string error))
                return CallToolResult.Error(error);

            if (File.Exists(destPath) && !ToolUtil.GetBool(args, "overwrite", false))
                return CallToolResult.Error("Destination blueprint already exists; pass overwrite=true to replace.");

            if (!WorldEditorExecutionAllowed(args))
                return WorldEditorPreview("blueprint_rename", from, new JObject { ["from"] = sourcePath, ["to"] = destPath });

            try
            {
                string parent = Path.GetDirectoryName(destPath);
                if (!Directory.Exists(parent))
                    Directory.CreateDirectory(parent);
                if (File.Exists(destPath))
                    File.Delete(destPath);
                File.Move(sourcePath, destPath);
                ShowBlueprintVisualFeedback($"📐 蓝图已重命名: {Path.GetFileName(sourcePath)} -> {Path.GetFileName(destPath)}");
                return JsonResult(new JObject
                {
                    ["ok"] = true,
                    ["from"] = sourcePath,
                    ["to"] = destPath
                });
            }
            catch (Exception ex)
            {
                return CallToolResult.Error("Failed to rename blueprint: " + ex.Message);
            }
        }

        private static CallToolResult BlueprintCategorize(JObject args)
        {
            string name = Text(args, "name", "target", "path");
            string category = Text(args, "category", "folder");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(category))
                return CallToolResult.Error("blueprint categorize requires 'name' and 'category'.");

            string sourcePath = FindBlueprintPath(name);
            if (sourcePath == null)
                return CallToolResult.Error("Blueprint not found: " + name);

            string fileName = Path.GetFileName(sourcePath);
            string targetRel = category.Trim().Trim('/', '\\') + "/" + fileName;
            if (!TryGetBlueprintPath(targetRel, false, out string destPath, out string error))
                return CallToolResult.Error(error);

            if (!WorldEditorExecutionAllowed(args))
                return WorldEditorPreview("blueprint_categorize", name, new JObject { ["category"] = category, ["target"] = destPath });

            try
            {
                string parent = Path.GetDirectoryName(destPath);
                if (!Directory.Exists(parent))
                    Directory.CreateDirectory(parent);
                if (File.Exists(destPath))
                    File.Delete(destPath);
                File.Move(sourcePath, destPath);
                ShowBlueprintVisualFeedback($"📐 蓝图已移动至分类 [{category}]: {fileName}");
                return JsonResult(new JObject
                {
                    ["ok"] = true,
                    ["category"] = category,
                    ["path"] = destPath
                });
            }
            catch (Exception ex)
            {
                return CallToolResult.Error("Failed to categorize blueprint: " + ex.Message);
            }
        }

        private static CallToolResult BlueprintPreview(JObject args)
        {
            string path = FindBlueprintPath(Text(args, "name", "path", "target"));
            if (path == null)
                return CallToolResult.Error("Blueprint not found. Pass name=...");

            JObject bp = ReadBlueprintJson(path);
            BlueprintRect rect = BlueprintBounds(bp);
            var buildings = bp["buildings"] as JArray ?? new JArray();

            var prefabCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var materialsSummary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (JObject item in buildings.OfType<JObject>())
            {
                string def = item.Value<string>("buildingdef") ?? "unknown";
                prefabCounts[def] = prefabCounts.ContainsKey(def) ? prefabCounts[def] + 1 : 1;

                if (item["selectedElements"] is JArray elements)
                {
                    foreach (var elem in elements)
                    {
                        string elStr = elem?.ToString();
                        if (!string.IsNullOrEmpty(elStr))
                            materialsSummary[elStr] = materialsSummary.ContainsKey(elStr) ? materialsSummary[elStr] + 1 : 1;
                    }
                }
            }

            string baseName = Path.GetFileNameWithoutExtension(path);
            ShowBlueprintVisualFeedback($"📐 正在预览蓝图: {baseName} ({rect.Width}x{rect.Height}, {buildings.Count}建筑)");

            return JsonResult(new JObject
            {
                ["ok"] = true,
                ["name"] = baseName,
                ["file"] = Path.GetFileName(path),
                ["bounds"] = new JObject { ["width"] = rect.Width, ["height"] = rect.Height, ["maxX"] = rect.MaxX, ["maxY"] = rect.MaxY },
                ["buildingCount"] = buildings.Count,
                ["buildings"] = JObject.FromObject(prefabCounts),
                ["materialsRequired"] = JObject.FromObject(materialsSummary),
                ["gridMap"] = ReadBlueprintMarkdown(path)
            });
        }

        private static CallToolResult BlueprintReplaceMaterials(JObject args)
        {
            string path = FindBlueprintPath(Text(args, "name", "path", "target"));
            if (path == null)
                return CallToolResult.Error("Blueprint not found. Pass name=...");

            var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (args["replacements"] is JObject repObj)
            {
                foreach (var prop in repObj.Properties())
                    replacements[prop.Name] = prop.Value?.ToString() ?? "";
            }
            string from = Text(args, "from", "sourceMaterial");
            string to = Text(args, "to", "targetMaterial");
            if (!string.IsNullOrEmpty(from) && !string.IsNullOrEmpty(to))
                replacements[from] = to;

            if (replacements.Count == 0)
                return CallToolResult.Error("replace_materials requires replacements dictionary or from/to parameters.");

            JObject bp = ReadBlueprintJson(path);
            int replacedCount = 0;
            var buildings = bp["buildings"] as JArray ?? new JArray();

            foreach (JObject item in buildings.OfType<JObject>())
            {
                if (item["selectedElements"] is JArray elements)
                {
                    for (int i = 0; i < elements.Count; i++)
                    {
                        string el = elements[i]?.ToString();
                        if (el != null && replacements.TryGetValue(el, out string nextEl))
                        {
                            elements[i] = nextEl;
                            replacedCount++;
                        }
                    }
                }
            }

            string saveAs = Text(args, "saveAs");
            string targetPath = path;
            if (!string.IsNullOrWhiteSpace(saveAs))
            {
                if (!TryGetBlueprintPath(saveAs, false, out targetPath, out string err))
                    return CallToolResult.Error(err);
            }

            if (!WorldEditorExecutionAllowed(args))
                return WorldEditorPreview("blueprint_replace_materials", Path.GetFileName(path), new JObject { ["replacedCount"] = replacedCount, ["target"] = targetPath });

            if (!AtomicWriteBlueprint(targetPath, bp.ToString(Formatting.Indented), out string writeErr))
                return CallToolResult.Error(writeErr);

            ShowBlueprintVisualFeedback($"📐 蓝图材料已替换: {Path.GetFileNameWithoutExtension(targetPath)} ({replacedCount} 处材料已变更)");
            return JsonResult(new JObject
            {
                ["ok"] = true,
                ["path"] = targetPath,
                ["replacedCount"] = replacedCount,
                ["replacements"] = JObject.FromObject(replacements)
            });
        }
    }
}
