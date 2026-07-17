using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static Dictionary<string, object> ErrorResult(string prefabId, int x, int y, string error, Dictionary<string, object> details = null)
        {
            string reasonCode = ClassifyBuildFailure(error, details);
            var result = new Dictionary<string, object>
            {
                ["planned"] = false,
                ["blueprintPlaced"] = false,
                ["actualAnchor"] = null,
                ["valid"] = false,
                ["prefabId"] = prefabId,
                ["x"] = x,
                ["y"] = y,
                ["anchor"] = AnchorDictionary(x, y, ExtractWorldId(details)),
                ["error"] = error,
                ["failureReason"] = reasonCode,
                ["reasonCode"] = reasonCode,
                ["coordinateContract"] = "x/y and anchor are the requested lower-left footprint cell; footprint/obstruction/support cells are absolute world cells when present."
            };
            if (details != null)
            {
                result["details"] = details;
                result["diagnostics"] = BuildFailureDiagnostics(reasonCode, error, details);
                CopyFailureField(details, result, "placement");
                CopyFailureField(details, result, "support");
                CopyFailureField(details, result, "materialSelection");
                CopyFailureField(details, result, "invalidCells");
                CopyFailureField(details, result, "obstructions");
                CopyFailureField(details, result, "missingSupportCells");
                CopyFailureField(details, result, "autoDig");
            }
            return result;
        }

        private static int ExtractWorldId(Dictionary<string, object> details)
        {
            var placement = GetObject(details, "placement");
            if (placement != null && placement.ContainsKey("worldId"))
                return Convert.ToInt32(placement["worldId"]);
            object value;
            if (details != null && details.TryGetValue("worldId", out value) && value != null)
                return Convert.ToInt32(value);
            return -1;
        }

        private static Dictionary<string, object> BuildFailureDiagnostics(string reasonCode, string error, Dictionary<string, object> details)
        {
            var diagnostics = new Dictionary<string, object>
            {
                ["reasonCode"] = reasonCode,
                ["message"] = error
            };
            CopyFailureField(details, diagnostics, "placement");
            CopyFailureField(details, diagnostics, "support");
            CopyFailureField(details, diagnostics, "materialSelection");
            CopyFailureField(details, diagnostics, "invalidCells");
            CopyFailureField(details, diagnostics, "obstructions");
            CopyFailureField(details, diagnostics, "missingSupportCells");
            CopyFailureField(details, diagnostics, "autoDig");
            CopyFailureField(details, diagnostics, "reasonHint");
            return diagnostics;
        }

        private static void CopyFailureField(Dictionary<string, object> source, Dictionary<string, object> target, string key)
        {
            object value;
            if (source != null && target != null && source.TryGetValue(key, out value))
                target[key] = value;
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static object ActualAnchorArray(Dictionary<string, object> actualPlacement)
        {
            if (actualPlacement == null)
                return null;
            int x = actualPlacement.ContainsKey("derivedAnchorX") ? Convert.ToInt32(actualPlacement["derivedAnchorX"]) : -1;
            int y = actualPlacement.ContainsKey("derivedAnchorY") ? Convert.ToInt32(actualPlacement["derivedAnchorY"]) : -1;
            return new[] { x, y };
        }

        private static Dictionary<string, object> AnchorDictionary(int x, int y, int worldId)
        {
            return new Dictionary<string, object>
            {
                ["x"] = x,
                ["y"] = y,
                ["worldId"] = worldId,
                ["coordRole"] = "lowerLeftCell",
                ["note"] = "Anchor is the lower-left footprint cell used by build_preview and build_area."
            };
        }

        private static string ClassifyBuildFailure(string error, Dictionary<string, object> details)
        {
            if (details != null && details.TryGetValue("reasonCode", out object explicitReasonCode)
                && !string.IsNullOrWhiteSpace(explicitReasonCode?.ToString()))
                return explicitReasonCode.ToString();
            string text = (error ?? "") + " " + (details != null ? JsonConvert.SerializeObject(details, Formatting.None) : "");
            if (text.IndexOf("Unsupported", StringComparison.OrdinalIgnoreCase) >= 0)
                return "unsupported";
            if (text.IndexOf("Obstructed", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("obstructions", StringComparison.OrdinalIgnoreCase) >= 0)
                return "obstructed";
            if (text.IndexOf("Invalid footprint", StringComparison.OrdinalIgnoreCase) >= 0)
                return "invalidFloor";
            if (text.IndexOf("material", StringComparison.OrdinalIgnoreCase) >= 0)
                return "unavailableMaterial";
            if (text.IndexOf("locked", StringComparison.OrdinalIgnoreCase) >= 0)
                return "locked";
            return "failed";
        }

        private static bool EqualsIgnoreCase(string value, string query)
        {
            return string.Equals(value, query, StringComparison.OrdinalIgnoreCase);
        }
    }
}
