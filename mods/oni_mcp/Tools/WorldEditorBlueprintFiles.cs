using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private const string BlueprintPrefix = "blueprints/";

        private static IEnumerable<object> BlueprintEntries(string virtualDir)
        {
            foreach (string path in BlueprintFiles())
            {
                string file = Path.GetFileName(path);
                string name = Path.GetFileNameWithoutExtension(path);
                yield return new { name = file, type = "file", path = virtualDir + file, description = "Blueprints Expanded raw JSON blueprint." };
                yield return new { name = name + ".md", type = "file", path = virtualDir + name + ".md", description = "Editable text-map view of blueprint." };
            }
        }

        private static bool IsBlueprintVirtualFile(string relative)
        {
            return relative.StartsWith(BlueprintPrefix, StringComparison.Ordinal)
                && (relative.EndsWith(".blueprint", StringComparison.OrdinalIgnoreCase)
                    || relative.EndsWith(".md", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsBlueprintMarkdown(string relative)
        {
            return relative.StartsWith(BlueprintPrefix, StringComparison.Ordinal)
                && relative.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
        }

        private static CallToolResult ReadBlueprintVirtualFile(string relative)
        {
            if (relative == "blueprints/index.md")
                return CallToolResult.Text(ReadBlueprintIndexMarkdown());
            string path = ResolveBlueprintPath(relative);
            if (path == null)
                return CallToolResult.Error("Blueprint not found: /active/" + relative);
            if (relative.EndsWith(".blueprint", StringComparison.OrdinalIgnoreCase))
                return CallToolResult.Text(File.ReadAllText(path));
            return CallToolResult.Text(ReadBlueprintMarkdown(path));
        }

        private static CallToolResult ApplyBlueprintMarkdownEdit(string relative, string search, string replace)
        {
            string path = ResolveBlueprintPath(relative);
            if (path == null)
                return CallToolResult.Error("Blueprint not found: /active/" + relative);
            string current = ReadBlueprintMarkdown(path);
            string next = string.IsNullOrWhiteSpace(search)
                ? replace
                : current.Replace(NormalizeSearchText(search), NormalizeSearchText(replace));
            if (next == current && !string.IsNullOrWhiteSpace(search))
                return CallToolResult.Error("Blueprint SEARCH did not match current markdown exactly; re-read /active/" + relative);

            string error;
            JObject updated = MarkdownToBlueprint(ReadBlueprintJson(path), next, out error);
            if (updated == null)
                return CallToolResult.Error(error);
            File.WriteAllText(path, updated.ToString(Formatting.Indented));
            return JsonResult(new JObject
            {
                ["ok"] = true,
                ["path"] = path,
                ["buildings"] = (updated["buildings"] as JArray)?.Count ?? 0,
                ["message"] = "blueprint markdown converted back to Blueprints Expanded JSON"
            });
        }

        private static CallToolResult BlueprintCommand(JObject args)
        {
            string action = Text(args, "blueprintAction", "action", "op").ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(action))
                action = "list";
            switch (action)
            {
                case "list":
                case "ls":
                    return CallToolResult.Text(ReadBlueprintIndexMarkdown());
                case "read":
                case "open":
                    return ReadBlueprintByName(args);
                case "create":
                case "new":
                    return CreateBlueprint(args);
                case "delete":
                case "remove":
                case "rm":
                    return DeleteBlueprint(args);
                case "use":
                case "place":
                    return UseBlueprint(args);
                default:
                    return CallToolResult.Error("blueprint action must be list, read, create, delete, or use");
            }
        }

        private static CallToolResult ReadBlueprintByName(JObject args)
        {
            string path = FindBlueprintPath(Text(args, "name", "path", "target"));
            if (path == null)
                return CallToolResult.Error("Blueprint not found. Pass name=...");
            bool json = FirstZoomText(args, "format", "profile").Equals("json", StringComparison.OrdinalIgnoreCase);
            return CallToolResult.Text(json ? File.ReadAllText(path) : ReadBlueprintMarkdown(path));
        }

        private static CallToolResult CreateBlueprint(JObject args)
        {
            string name = Text(args, "name", "target");
            if (string.IsNullOrWhiteSpace(name))
                return CallToolResult.Error("blueprint create requires name");
            string path = Path.Combine(BlueprintDirectory(), SafeBlueprintFileName(name));
            if (File.Exists(path) && !ToolUtil.GetBool(args, "overwrite", false))
                return CallToolResult.Error("Blueprint already exists; pass overwrite=true or choose another name.");
            if (!ToolUtil.GetBool(args, "confirm", false))
                return JsonResult(new JObject { ["dryRun"] = true, ["wouldCreate"] = path });

            Directory.CreateDirectory(BlueprintDirectory());
            string content = Text(args, "content", "text", "map");
            JObject root = BuildBlueprintCreateJson(path, content, out string error);
            if (root == null)
                return CallToolResult.Error(error);
            File.WriteAllText(path, root.ToString(Formatting.Indented));
            return JsonResult(new JObject { ["ok"] = true, ["created"] = path });
        }

        private static JObject BuildBlueprintCreateJson(string path, string content, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(content))
                return EmptyBlueprint(Path.GetFileNameWithoutExtension(path));
            if (content.TrimStart().StartsWith("{", StringComparison.Ordinal))
                return JObject.Parse(content);
            return MarkdownToBlueprint(EmptyBlueprint(Path.GetFileNameWithoutExtension(path)), content, out error);
        }

        private static CallToolResult DeleteBlueprint(JObject args)
        {
            string path = FindBlueprintPath(Text(args, "name", "path", "target"));
            if (path == null)
                return CallToolResult.Error("Blueprint not found. Pass name=...");
            if (!ToolUtil.GetBool(args, "confirm", false))
                return JsonResult(new JObject { ["dryRun"] = true, ["wouldDelete"] = path });
            File.Delete(path);
            return JsonResult(new JObject { ["ok"] = true, ["deleted"] = path });
        }

        private static CallToolResult UseBlueprint(JObject args)
        {
            string path = FindBlueprintPath(Text(args, "name", "path", "target"));
            if (path == null)
                return CallToolResult.Error("Blueprint not found. Pass name=...");
            int? x = ToolUtil.GetInt(args, "x") ?? ToolUtil.GetInt(args, "centerX");
            int? y = ToolUtil.GetInt(args, "y") ?? ToolUtil.GetInt(args, "centerY");
            if (!x.HasValue || !y.HasValue)
                return CallToolResult.Error("blueprint use requires x and y top-left origin");

            JObject root = ReadBlueprintJson(path);
            BlueprintRect rect = BlueprintBounds(root);
            int count = (root["buildings"] as JArray)?.Count ?? 0;
            if (ToolUtil.GetBool(args, "dryRun", true) || !ToolUtil.GetBool(args, "confirm", false))
            {
                return JsonResult(new JObject
                {
                    ["dryRun"] = true,
                    ["name"] = Path.GetFileNameWithoutExtension(path),
                    ["origin"] = new JObject { ["x"] = x.Value, ["y"] = y.Value },
                    ["bounds"] = new JObject { ["width"] = rect.Width, ["height"] = rect.Height },
                    ["buildings"] = count,
                    ["next"] = "repeat with dryRun=false confirm=true to place through Blueprints Expanded"
                });
            }

            if (!InvokeBlueprintsExpandedUse(path, x.Value, y.Value, out string error))
                return CallToolResult.Error(error);
            return JsonResult(new JObject
            {
                ["ok"] = true,
                ["placed"] = Path.GetFileName(path),
                ["origin"] = new JObject { ["x"] = x.Value, ["y"] = y.Value },
                ["buildings"] = count
            });
        }
    }
}
