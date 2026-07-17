using System;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        internal static bool EnforceNormalMaterialRules { get; private set; }
        private static readonly string[] WorldEditorSandboxFlags =
        {
            "allowSandbox", "instantBuild", "allowForce", "allowTerrainMutation",
            "allowEntitySpawn", "allowDestroy"
        };

        private static CallToolResult HandleWorldEditorScoped(JObject rawArgs)
        {
            return RunWithWorldEditorInstantBuildScope(rawArgs, () => HandleWorldEditorCommand(rawArgs ?? new JObject()));
        }

        private static CallToolResult RunWithWorldEditorInstantBuildScope(JObject rawArgs, Func<CallToolResult> action)
        {
            JObject args = rawArgs ?? new JObject();
            bool previous = DebugHandler.InstantBuildMode;
            bool previousMaterialRules = EnforceNormalMaterialRules;
            try
            {
                DebugHandler.InstantBuildMode = false;
                EnforceNormalMaterialRules = true;
                bool instantBuild = ToolUtil.GetBool(args, "instantBuild", false);
                if (instantBuild && (!ToolUtil.GetBool(args, "allowSandbox", false)
                    || !ToolUtil.GetBool(args, "confirm", false)))
                    return CallToolResult.Error("instantBuild=true requires allowSandbox=true and confirm=true");
                DebugHandler.InstantBuildMode = instantBuild;
                EnforceNormalMaterialRules = !instantBuild;
                return action();
            }
            finally
            {
                DebugHandler.InstantBuildMode = previous;
                EnforceNormalMaterialRules = previousMaterialRules;
            }
        }

        private static JObject InheritWorldEditorSandboxPolicy(JObject parent, JObject child)
        {
            JObject result = child ?? new JObject();
            foreach (string key in WorldEditorSandboxFlags)
            {
                bool parentAllowed = ToolUtil.GetBool(parent, key, false);
                bool childAllowed = result[key] == null ? parentAllowed : ToolUtil.GetBool(result, key, false);
                result[key] = parentAllowed && childAllowed;
            }
            int parentMaxCells = SandboxCellLimit(parent);
            int childMaxCells = result["sandboxMaxCells"] == null
                ? parentMaxCells
                : SandboxCellLimit(result);
            result["sandboxMaxCells"] = Math.Min(parentMaxCells, childMaxCells);
            return result;
        }

        private static int SandboxCellLimit(JObject args)
        {
            return Math.Max(1, Math.Min(1000, ToolUtil.GetInt(args, "sandboxMaxCells") ?? 100));
        }

        private static CallToolResult ForwardSandbox(JObject args)
        {
            var forwarded = CopyPayload(args);
            forwarded["domain"] = "sandbox";
            foreach (string key in WorldEditorSandboxFlags)
                forwarded[key] = ToolUtil.GetBool(args, key, false);
            forwarded["confirm"] = ToolUtil.GetBool(args, "confirm", false);
            forwarded["sandboxMaxCells"] = SandboxCellLimit(args);
            if (!ValidateWorldEditorSandboxPolicy(forwarded, out string error))
                return CallToolResult.Error(error);
            return GameControlTools.ControlGame().Handler(forwarded);
        }

        private static bool ValidateWorldEditorSandboxPolicy(JObject args, out string error)
        {
            error = null;
            string kind = (args?["kind"]?.ToString() ?? "read").Trim().ToLowerInvariant();
            string action = (args?["action"]?.ToString() ?? "list_actions").Trim().ToLowerInvariant();
            if (action == "list" || action == "status")
                action = "list_actions";
            args["kind"] = kind;
            args["action"] = action;
            int policyMax = SandboxCellLimit(args);
            int requestedMax = Math.Max(1, ToolUtil.GetInt(args, "maxCells") ?? policyMax);
            args["maxCells"] = Math.Min(policyMax, requestedMax);

            bool read = kind == "read" && (action == "list_actions"
                || action == "sample_cell" || action == "list_story_traits");
            if (ToolUtil.GetBool(args, "force", false) && !ToolUtil.GetBool(args, "allowForce", false))
            {
                error = "Sandbox force=true requires allowForce=true";
                return false;
            }
            if (read)
                return true;
            if (!ToolUtil.GetBool(args, "allowSandbox", false)
                || !ToolUtil.GetBool(args, "confirm", false))
            {
                error = "Sandbox writes require allowSandbox=true and confirm=true";
                return false;
            }
            if ((kind == "area" || kind == "map_designate")
                && !ToolUtil.GetBool(args, "allowTerrainMutation", false))
            {
                error = "Sandbox area/map_designate writes require allowTerrainMutation=true";
                return false;
            }
            if (kind == "entity" && !ToolUtil.GetBool(args, "allowEntitySpawn", false))
            {
                error = "Sandbox entity writes require allowEntitySpawn=true";
                return false;
            }
            if (action == "destroy" && !ToolUtil.GetBool(args, "allowDestroy", false))
            {
                error = "Sandbox destroy requires allowDestroy=true";
                return false;
            }
            return true;
        }
    }
}
