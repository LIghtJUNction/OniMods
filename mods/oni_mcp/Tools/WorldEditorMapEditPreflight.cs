using System;
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

        private static bool ValidateExplicitMapChangesAgainstSource(JObject args, string path, List<MapEditCell> changes, out string error)
        {
            if (!TryReadVirtualFileText(args, path, out string current, out string readError))
            {
                error = "Cannot read current map before applying explicit edit: " + readError;
                return false;
            }
            var rows = ParseMapRows(current, out int[] hundreds, out int[] tens, out int[] ones, out error);
            if (rows == null || !TryBuildAxisCoordinates(hundreds, tens, ones, out int[] coordinates, out error))
                return false;
            var offsets = coordinates.Select((x, index) => new { x, index }).ToDictionary(item => item.x, item => item.index);

            for (int i = changes.Count - 1; i >= 0; i--)
            {
                MapEditCell change = changes[i];
                if (!rows.TryGetValue(change.Y, out string[] row)
                    || !offsets.TryGetValue(change.X, out int offset)
                    || offset >= row.Length)
                {
                    error = "Explicit edit cell (" + change.X + "," + change.Y + ") is outside the source viewport/layer.";
                    return false;
                }
                string actual = row[offset];
                if (!string.IsNullOrWhiteSpace(change.FromToken) && !SearchTokenMatches(actual, change.FromToken))
                {
                    error = "Stale explicit edit at (" + change.X + "," + change.Y + "): expected `" + change.FromToken + "`, current `" + actual + "`.";
                    return false;
                }
                change.FromToken = actual;
                if (MapTokensEquivalent(actual, change.ToToken))
                    changes.RemoveAt(i);
            }
            if (changes.Count == 0)
            {
                error = "Explicit map edit contains no differences from current state.";
                return false;
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
