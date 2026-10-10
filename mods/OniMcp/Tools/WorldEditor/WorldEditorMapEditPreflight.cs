using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static bool TryCompileMapEdit(JObject args, string search, string replacement, out List<MapEditCell> changes, out string error)
        {
            changes = null;
            if (!TryReadVirtualFileTextForMapEdit(args, args["sourcePath"]?.ToString(), search, out string current, out string readError))
            {
                error = "Cannot read current map before applying map edit: " + readError;
                return false;
            }
            if (!TryParseMapEditChangesFromPatchCoordinates(current, search, replacement, out changes, out error))
                changes = ParseMapEditChanges(current, search, replacement, out error);
            if (changes == null)
                return false;
            if (changes.Count == 0)
            {
                error = "Map edit changed no grid cells; the replacement already matches current state.";
                return false;
            }
            return ValidateCompiledMapChanges(args, changes, out error);
        }

        private static bool ValidateCompiledMapChanges(JObject args, List<MapEditCell> changes, out string error)
        {
            error = null;
            if (args["priority"] != null
                && (!int.TryParse(args["priority"].ToString(), out int requestedPriority)
                    || requestedPriority < 1 || requestedPriority > 9))
            {
                error = "priority must be an integer from 1 through 9.";
                return false;
            }
            foreach (var change in changes)
            {
                if (!MapTextReadPolicy.Inside(change.X, change.Y, Grid.WidthInCells, Grid.HeightInCells)
                    || !IsReadableMapCell(Grid.XYToCell(change.X, change.Y))
                    || change.FromToken == "?")
                {
                    error = "Map edit targets an unknown, hidden, or out-of-world cell; no order was issued.";
                    return false;
                }
                string kind = ChangeKind(change);
                if (IsOrderAction(kind) && (!ParseBuildToken(change.ToToken, out _, out _, out string material)
                    || material != null))
                {
                    error = "Invalid order token: " + change.ToToken + ". Priority must be 1 through 9; orders do not take materials.";
                    return false;
                }
            }
            foreach (var group in changes.GroupBy(ChangeKind))
            {
                if (group.Key == "wire")
                {
                    error = "Connection glyph edits are refused because auto_connect may touch cells outside the source snapshot. Use an explicit infrastructure plan or /active/ops/build.md auto_connect command.";
                    return false;
                }
                if (group.Key == "unsupported")
                {
                    error = UnsupportedMapEdit(group).Content?.FirstOrDefault()?.Text ?? "unsupported map edit";
                    return false;
                }
                if (group.Key != "build")
                    continue;

                foreach (var tokenGroup in group.GroupBy(cell => cell.ToToken))
                {
                    if (!ParseBuildToken(tokenGroup.Key, out char symbol, out int? priority, out _)
                        || !priority.HasValue)
                    {
                        error = "Build token `" + tokenGroup.Key + "` must include a valid :priority.";
                        return false;
                    }
                    if (!TryResolveBuildPrefabFromToken(tokenGroup.Key, symbol, out string prefabId))
                    {
                        error = "Cannot map building token `" + tokenGroup.Key + "` to a buildable prefab.";
                        return false;
                    }
                    if (!TryBuildAnchorsForPrefabFootprints(args, prefabId, tokenGroup, out JArray _, out error))
                        return false;
                }
            }
            return true;
        }

        private static CallToolResult PreflightMapEdit(JObject args, string search, string replacement)
        {
            if (!TryCompileMapEdit(args, search, replacement, out List<MapEditCell> changes, out string error))
                return CallToolResult.Error(error);
            return JsonResult(new JObject
            {
                ["ok"] = true,
                ["phase"] = "preflight",
                ["sourcePath"] = args["sourcePath"]?.ToString(),
                ["changedCells"] = changes.Count,
                ["kinds"] = new JArray(changes.GroupBy(ChangeKind).Select(group => new JObject
                {
                    ["kind"] = group.Key,
                    ["cells"] = group.Count()
                }))
            });
        }
    }
}
