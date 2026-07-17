using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Net;
using System.Text.RegularExpressions;
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
            if (args["editCells"] != null || args["editLines"] != null)
                return CallToolResult.Error("Coordinate map edits are forbidden. Read /active/map/viewport.md and submit content as a SEARCH/REPLACE patch.");

            string block = Text(args, "content");
            List<KeyValuePair<string, string>> edits;
            if (!TryParseSearchReplaceBlocks(block, out edits))
                return CallToolResult.Error("edit requires at least one <<<<<<< SEARCH / ======= / >>>>>>> REPLACE block");
            if (edits.Count > 1 && !ToolUtil.GetBool(args, "allowPartial", false))
                return CallToolResult.Error("Multiple write blocks require allowPartial=true because game mutations cannot be rolled back transactionally.");

            var preflight = new JArray();
            for (int i = 0; i < edits.Count; i++)
            {
                var result = PreflightSingleEditBlock(args, path, relative, edits[i].Key, edits[i].Value);
                if (WorldEditorResultFailed(result))
                    return CallToolResult.Error(JsonResultText(new JObject
                    {
                        ["ok"] = false,
                        ["phase"] = "preflight",
                        ["block"] = i,
                        ["error"] = result.Content?.FirstOrDefault()?.Text ?? "preflight failed",
                        ["applied"] = 0
                    }));
                preflight.Add(new JObject { ["block"] = i, ["result"] = result.Content?.FirstOrDefault()?.Text ?? string.Empty });
            }

            if (!WorldEditorExecutionAllowed(args))
                return WorldEditorPreview("search_replace", path, preflight);

            var results = new JArray();
            int applied = 0;
            bool partial = false;
            for (int i = 0; i < edits.Count; i++)
            {
                var result = ApplySingleEditBlock(args, path, relative, edits[i].Key, edits[i].Value);
                results.Add(new JObject
                {
                    ["block"] = i,
                    ["ok"] = !WorldEditorResultFailed(result),
                    ["result"] = result?.Content?.FirstOrDefault()?.Text ?? "edit failed"
                });
                if (WorldEditorResultFailed(result))
                    return WorldEditorExecutionFailure("search_replace", path, applied + ResultAppliedCount(result), results);
                partial = partial || ResultReportsPartial(result);
                applied++;
            }

            return JsonResult(new JObject { ["ok"] = true, ["partial"] = partial, ["applied"] = applied, ["failed"] = 0, ["results"] = results });
        }

        private static CallToolResult PreflightSingleEditBlock(JObject args, string path, string relative, string search, string replace)
        {
            if (!ValidateVirtualFileSearch(args, path, relative, search, out string searchError))
                return CallToolResult.Error(searchError);
            var routed = CopyPayload(args);
            routed["sourcePath"] = path;
            if (IsEditableMapMarkdown(relative))
                return PreflightMapEdit(routed, search, replace);
            if (IsEditableBuildCommandFile(relative))
                return PreflightBuildEdit(routed, relative, replace);
            if (IsEditableManagementMarkdown(relative))
                return PreflightManagementMarkdownEdit(relative, replace);
            if (IsBlueprintMarkdown(relative))
                return PreflightBlueprintMarkdownEdit(relative, search, replace);
            if (IsEditableOperationMarkdown(relative))
                return PreflightOperationMarkdownEdit(args, relative, replace);
            if (IsDupeDetailMarkdown(relative))
                return PreflightDupeDetailEdit(relative, replace);
            if (IsBuildingDetailMarkdown(relative))
                return PreflightBuildingDetailEdit(relative, search, replace);
            return CallToolResult.Error("file is read-only for edits: " + path);
        }

        private static CallToolResult ApplySingleEditBlock(JObject args, string path, string relative, string search, string replace)
        {
            string searchError;
            if (!ValidateVirtualFileSearch(args, path, relative, search, out searchError))
                return CallToolResult.Error(searchError);
            var routed = CopyPayload(args);
            routed.Remove("content");
            routed["sourcePath"] = path;
            routed["searchText"] = search;
            routed["replacementText"] = replace;

            if (IsEditableMapMarkdown(relative))
                return ApplyMapEdit(routed, search, replace);
            if (IsEditableBuildCommandFile(relative))
                return ApplyBuildEdit(routed, relative, replace);
            if (IsEditableManagementMarkdown(relative))
                return ApplyManagementMarkdownEdit(routed, relative, replace);
            if (IsBlueprintMarkdown(relative))
                return ApplyBlueprintMarkdownEdit(routed, relative, search, replace);
            if (IsEditableOperationMarkdown(relative))
                return ApplyOperationMarkdownEdit(routed, relative, replace);
            if (IsDupeDetailMarkdown(relative))
                return ApplyDupeDetailEdit(routed, relative, replace);
            if (IsBuildingDetailMarkdown(relative))
                return ApplyBuildingDetailEdit(routed, relative, search, replace);

            return CallToolResult.Error("file is read-only for edits: " + path);
        }

        private static CallToolResult ApplyBuildEdit(JObject args, string relative, string replacement)
        {
            args["domain"] = "planning";
            args["plan"] = replacement.Trim();
            bool connectionFile = relative.StartsWith("infrastructure/", StringComparison.Ordinal);
            if (connectionFile && !TryApplyInfrastructurePlan(args, relative, replacement, out string error))
                return CallToolResult.Error(error);
            if (connectionFile || LooksLikeConnection(replacement))
                args["action"] = "auto_connect";
            else
                args["action"] = ToolUtil.GetBool(args, "confirm", false) ? "build_area" : "parse_plan";
            return BuildingControlTools.ControlBuildingFromVirtualFile(args);
        }

        private static CallToolResult PreflightBuildEdit(JObject args, string relative, string replacement)
        {
            var preview = (JObject)args.DeepClone();
            preview["domain"] = "planning";
            preview["plan"] = replacement.Trim();
            preview["dryRun"] = true;
            preview["confirm"] = false;
            if (relative.StartsWith("infrastructure/", StringComparison.Ordinal)
                && !TryApplyInfrastructurePlan(preview, relative, replacement, out string error))
                return CallToolResult.Error(error);
            preview["action"] = relative == "buildings/plans.oni" ? "build_area" : "auto_connect";
            return PromoteWorldEditorFailure(BuildingControlTools.ControlBuildingFromVirtualFile(preview));
        }

        private static bool IsEditableBuildCommandFile(string relative)
        {
            return relative == "buildings/plans.oni"
                || relative == "infrastructure/power.oni"
                || relative == "infrastructure/liquid_conduits.oni"
                || relative == "infrastructure/gas_conduits.oni"
                || relative == "infrastructure/logic.oni"
                || relative == "infrastructure/solid_conveyor.oni";
        }

        private static bool TryApplyInfrastructurePlan(JObject args, string relative, string replacement, out string error)
        {
            error = null;
            string plan = (replacement ?? string.Empty).Trim();
            const string fullPattern = @"^connect\s+\(\s*-?\d+\s*,\s*-?\d+\s*\)(?:\s*(?:->|→)\s*\(\s*-?\d+\s*,\s*-?\d+\s*\)){1,}$";
            if (!Regex.IsMatch(plan, fullPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                error = "Infrastructure plans must use exactly: connect (x1,y1) -> (x2,y2) [-> (x3,y3) ...]";
                return false;
            }

            var points = new JArray();
            foreach (Match match in Regex.Matches(plan, @"\(\s*(-?\d+)\s*,\s*(-?\d+)\s*\)"))
            {
                if (!int.TryParse(match.Groups[1].Value, out int x)
                    || !int.TryParse(match.Groups[2].Value, out int y))
                {
                    error = "Infrastructure plan coordinates must be 32-bit integers";
                    return false;
                }
                points.Add(new JArray(x, y));
            }
            if (points.Count < 2)
            {
                error = "Infrastructure plans require at least two explicit points";
                return false;
            }

            args["prefabId"] = PrefabForConnectionMap(relative);
            args["points"] = points;
            args["material"] = args["material"] ?? "auto";
            args["nativePathPlacement"] = true;
            return true;
        }

    }
}
