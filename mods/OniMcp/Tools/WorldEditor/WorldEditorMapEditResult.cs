using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static JToken MapEditResultValue(CallToolResult result)
        {
            string text = result?.Content?.FirstOrDefault()?.Text ?? string.Empty;
            try { return JToken.Parse(text); }
            catch (Newtonsoft.Json.JsonReaderException) { return new JValue(text); }
        }

        private static JToken MapEditBuildResultValue(CallToolResult result)
        {
            var value = MapEditResultValue(result);
            var source = value as JObject;
            if (source == null)
                return value;

            var failures = new JArray();
            AddBuildFailureAnchors(failures, source["preflightErrors"] as JArray);
            var constructionDiagnostics = new JArray();
            AddConstructionAnchorDiagnostics(constructionDiagnostics, source["preflightErrors"] as JArray);
            var executionResults = source["results"] as JArray;
            if (executionResults != null)
            {
                foreach (var item in executionResults.OfType<JObject>())
                {
                    AddConstructionAnchorDiagnostic(constructionDiagnostics, item);
                    if (item["valid"]?.Value<bool?>() == false && item["autoDig"]?["available"]?.Value<bool?>() != true)
                        AddBuildFailureAnchor(failures, item);
                }
            }

            var summary = new JObject
            {
                ["committed"] = source["committed"]?.DeepClone() ?? new JValue(false),
                ["blueprintsPlaced"] = source["planned"]?.DeepClone() ?? new JValue(0),
                ["blueprintsAlreadyPresent"] = source["alreadyPresent"]?.DeepClone() ?? new JValue(0),
                ["validAnchors"] = source["validAnchors"]?.DeepClone() ?? new JValue(0),
                ["autoDiggableAnchors"] = source["autoDiggableAnchors"]?.DeepClone() ?? new JValue(0),
                ["autoDigMarked"] = source["autoDigQueued"]?.DeepClone() ?? new JValue(0),
                ["autoDigAlreadyMarked"] = source["autoDigAlreadyMarked"]?.DeepClone() ?? new JValue(0),
                ["pendingBuildAfterDig"] = source["pendingBuildAfterDig"]?.DeepClone() ?? new JValue(0),
                ["failed"] = source["failed"]?.DeepClone() ?? new JValue(0),
                ["failedAnchors"] = failures,
                ["constructionDiagnostics"] = constructionDiagnostics,
                ["batchConstructionDiagnostics"] = source["batchConstructionDiagnostics"]?.DeepClone() ?? new JObject(),
                ["remainingAnchors"] = source["remainingAnchors"]?.DeepClone() ?? new JArray(),
                ["throttled"] = source["throttled"]?.DeepClone() ?? new JValue(false),
                ["deferred"] = source["deferred"]?.DeepClone() ?? new JValue(0),
                ["allRequestedSatisfied"] = source["allRequestedSatisfied"]?.DeepClone() ?? new JValue(false),
                ["requiresVerification"] = source["committed"]?.Value<bool?>() == true,
                ["next"] = source["next"]?.DeepClone() ?? new JValue("Re-read the edited area, run briefly, pause, and verify the blueprints and dig orders.")
            };

            var remainingAction = source["remainingAction"];
            if (remainingAction != null && remainingAction.Type != JTokenType.Null)
                summary["remainingAction"] = remainingAction.DeepClone();
            return summary;
        }

        private static void AddConstructionAnchorDiagnostics(JArray target, JArray results)
        {
            if (target == null || results == null)
                return;
            foreach (var item in results.OfType<JObject>())
                AddConstructionAnchorDiagnostic(target, item);
        }

        private static void AddConstructionAnchorDiagnostic(JArray target, JObject item)
        {
            var diagnostics = item?["constructionDiagnostics"];
            if (target == null || diagnostics == null || diagnostics.Type == JTokenType.Null || target.Count >= 50)
                return;
            var entry = new JObject
            {
                ["x"] = item["x"]?.DeepClone(),
                ["y"] = item["y"]?.DeepClone(),
                ["prefabId"] = item["prefabId"]?.DeepClone(),
                ["planned"] = item["planned"]?.DeepClone(),
                ["valid"] = item["valid"]?.DeepClone(),
                ["reasonCode"] = item["reasonCode"]?.DeepClone(),
                ["diagnostics"] = diagnostics.DeepClone()
            };
            target.Add(entry);
        }

        private static void AddBuildFailureAnchors(JArray target, JArray errors)
        {
            if (errors == null)
                return;
            foreach (var error in errors.OfType<JObject>())
                AddBuildFailureAnchor(target, error);
        }

        private static void AddBuildFailureAnchor(JArray target, JObject error)
        {
            var anchor = new JObject();
            foreach (string key in new[] { "x", "y", "reasonCode", "failureReason", "error" })
            {
                var value = error[key];
                if (value != null)
                    anchor[key] = value.DeepClone();
            }
            if (anchor["reasonCode"] == null && anchor["failureReason"] != null)
                anchor["reasonCode"] = anchor["failureReason"].DeepClone();
            anchor.Remove("failureReason");
            if (anchor.HasValues && !target.Any(existing => JToken.DeepEquals(existing, anchor)))
                target.Add(anchor);
        }
    }
}
