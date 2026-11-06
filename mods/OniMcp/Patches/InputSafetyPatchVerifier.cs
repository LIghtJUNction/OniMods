using HarmonyLib;
using OniMcp.Support;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace OniMcp
{
    internal static class InputSafetyPatchVerifier
    {
        private const string HarmonyId = "LIghtJUNction.OniMcp";
        private static readonly HashSet<string> ReportedPhases = new HashSet<string>();

        public static void EnsureInstalled(Harmony harmony, string phase)
        {
            harmony = harmony ?? new Harmony(HarmonyId);

            // Keep KButtonEvent constructors unpatched: MonoMod can emit invalid IL for
            // them on ONI's Mono runtime. ToolMenu expands the bitmap when needed.
            var toolMenuOnKeyDown = AccessTools.Method(typeof(ToolMenu), "OnKeyDown", new[] { typeof(KButtonEvent) });
            var isAction = AccessTools.Method(typeof(KButtonEvent), nameof(KButtonEvent.IsAction), new[] { typeof(Action) });

            EnsurePatch(harmony, toolMenuOnKeyDown, typeof(ToolMenu_OnKeyDown_Patch));
            EnsurePatch(harmony, isAction, typeof(KButtonEvent_IsAction_Patch));

            string toolMenuOwners = DescribeOwners(toolMenuOnKeyDown);
            string isActionOwners = DescribeOwners(isAction);
            bool healthy = IsInstalledOrMissing(toolMenuOnKeyDown)
                && IsInstalledOrMissing(isAction);

            if (!healthy || ReportedPhases.Add(phase))
            {
                string message = "[OniMcp] Input safety patch check at " + phase
                    + ": healthy=" + healthy
                    + "; ToolMenu.OnKeyDown=" + toolMenuOwners
                    + "; KButtonEvent.IsAction=" + isActionOwners;
                if (healthy)
                    OniMcpLog.Debug(message);
                else
                    OniMcpLog.Warning(message);
            }
        }

        private static void EnsurePatch(Harmony harmony, MethodBase original, Type patchType)
        {
            if (original == null)
                return;
            if (OwnersContainOniMcp(original))
                return;

            try
            {
                var prefix = AccessTools.Method(patchType, "Prefix");
                var postfix = AccessTools.Method(patchType, "Postfix");
                var finalizer = AccessTools.Method(patchType, "Finalizer");
                harmony.Patch(
                    original,
                    prefix == null ? null : new HarmonyMethod(prefix),
                    postfix == null ? null : new HarmonyMethod(postfix),
                    null,
                    finalizer == null ? null : new HarmonyMethod(finalizer));
            }
            catch (Exception ex)
            {
                OniMcpLog.Warning("[OniMcp] input safety patch skipped for "
                    + original.DeclaringType?.Name + "." + original.Name
                    + ": " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static bool IsInstalledOrMissing(MethodBase original)
        {
            return original == null || OwnersContainOniMcp(original);
        }

        private static bool OwnersContainOniMcp(MethodBase original)
        {
            return original != null && Harmony.GetPatchInfo(original)?.Owners.Contains(HarmonyId) == true;
        }

        private static string DescribeOwners(MethodBase original)
        {
            if (original == null)
                return "missing";

            var patches = Harmony.GetPatchInfo(original);
            if (patches == null || patches.Owners.Count == 0)
                return "none";

            return string.Join(",", patches.Owners.ToArray());
        }
    }
}
