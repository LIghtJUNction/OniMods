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
        private static CallToolResult Edit(JObject args)
        {
            string path = NormalizePath(Text(args, "path"), _cwd);
            if (!path.StartsWith("/active/", StringComparison.Ordinal))
            {
                return CallToolResult.Error("Cannot apply edits to historical or unloaded saves. Edits can only be performed under the '/active/' directory representing the currently active game.");
            }
            string relative = SaveRelativePath(path);
            if (args["editCells"] is JArray || args["editLines"] is JArray)
                return ApplyExplicitMapEditCells(args, path, relative, args["editCells"] as JArray, args["editLines"] as JArray);

            string block = Text(args, "content");
            List<KeyValuePair<string, string>> edits;
            if (!TryParseSearchReplaceBlocks(block, out edits))
                return CallToolResult.Error("edit requires at least one <<<<<<< SEARCH / ======= / >>>>>>> REPLACE block");

            CallToolResult last = null;
            for (int i = 0; i < edits.Count; i++)
            {
                last = ApplySingleEditBlock(args, path, relative, edits[i].Key, edits[i].Value);
                if (last == null || last.IsError)
                    return last ?? CallToolResult.Error("edit failed");
            }

            return last ?? CallToolResult.Error("edit produced no result");
        }

        private static CallToolResult ApplySingleEditBlock(JObject args, string path, string relative, string search, string replace)
        {
            string searchError;
            List<MapEditCell> patchChanges;
            string patchError;
            bool pinnedMapPatch = IsEditableMapMarkdown(relative)
                && TryParseMapEditChangesFromPatchCoordinates(search, replace, out patchChanges, out patchError);
            if (!pinnedMapPatch && !ValidateVirtualFileSearch(path, relative, search, out searchError))
                return CallToolResult.Error(searchError);
            var routed = CopyPayload(args);
            routed.Remove("content");
            routed["sourcePath"] = path;
            routed["searchText"] = search;
            routed["replacementText"] = replace;

            if (IsEditableMapMarkdown(relative))
                return ApplyMapEdit(routed, search, replace);
            if (relative == "buildings/plans.oni" || relative.StartsWith("infrastructure/", StringComparison.Ordinal))
                return ApplyBuildEdit(routed, relative, replace);
if (IsManagementMarkdown(relative))
return ApplyManagementMarkdownEdit(routed, relative, replace);
if (IsBlueprintMarkdown(relative))
return ApplyBlueprintMarkdownEdit(relative, search, replace);
if (IsOperationMarkdown(relative))
return ApplyOperationMarkdownEdit(relative, replace);
if (relative == "orders/orders.oni" || relative == "map/terrain.oni")
                return ApplyOrderEdit(routed, replace);
            if (IsDupeDetailMarkdown(relative))
                return ApplyDupeDetailEdit(routed, relative, replace);
            if (relative == "dupes/index.oni")
                return ApplyDupeEdit(routed, replace);

            return CallToolResult.Error("file is read-only for edits: " + path);
        }

        private static CallToolResult ApplyBuildEdit(JObject args, string relative, string replacement)
        {
            args["domain"] = "planning";
            args["plan"] = replacement.Trim();
            bool connectionFile = relative.StartsWith("infrastructure/", StringComparison.Ordinal);
            if (connectionFile || LooksLikeConnection(replacement))
                args["action"] = "auto_connect";
            else
                args["action"] = ToolUtil.GetBool(args, "confirm", false) ? "build_area" : "parse_plan";
            return BuildingControlTools.ControlBuilding().Handler(args);
        }

        private static CallToolResult ApplyOrderEdit(JObject args, string replacement)
        {
            string lower = replacement.ToLowerInvariant();
            args["target"] = replacement.Trim();
            if (string.IsNullOrWhiteSpace(args["action"]?.ToString()))
            {
                if (lower.Contains("dig") || replacement.Contains("挖"))
                    args["action"] = "dig";
                else if (lower.Contains("sweep") || replacement.Contains("扫"))
                    args["action"] = "sweep";
                else if (lower.Contains("mop") || replacement.Contains("拖"))
                    args["action"] = "mop";
                else if (lower.Contains("deconstruct") || replacement.Contains("拆"))
                    args["action"] = "deconstruct";
                else if (lower.Contains("cancel") || replacement.Contains("取消"))
                    args["action"] = "cancel";
            }
            return OrdersControlEntryTools.ControlOrders().Handler(args);
        }

        private static CallToolResult ApplyDupeEdit(JObject args, string replacement)
        {
            if (string.IsNullOrWhiteSpace(args["action"]?.ToString()))
                args["action"] = "command";
            args["target"] = replacement.Trim();
            return DupesControlEntryTools.ControlDupes().Handler(args);
        }

    }
}
