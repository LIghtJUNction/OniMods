using HarmonyLib;
using KMod;
using OniMcp.Config;
using OniMcp.Server;
using OniMcp.Support;
using OniMcp.Tools;
using PeterHan.PLib.Core;
using PeterHan.PLib.Database;
using PeterHan.PLib.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace OniMcp
{
    /// <summary>
    /// Mod 入口类。游戏加载时自动实例化。
    /// </summary>
    [HarmonyPatch(typeof(Db), "Initialize")]
    public static class Db_Initialize_Patch
    {
        public static void Postfix()
        {
            WorldEditorTools.MarkRuntimeDatabaseReady();
            InputSafetyPatchVerifier.EnsureInstalled(null, "Db.Initialize");
            AutoDisinfectPolicy.EnsureInstalled(null, "Db.Initialize");
            AutoDisinfectPolicy.ApplyToExisting();
            OniMcpLog.Debug("[OniMcp] Db.Initialize - Initializing game-specific components...");

            ToolCallSpeechOverlay.EnsureInstance();
            CoordinateGridOverlay.EnsureInstance();

            OniMcpLog.Debug("[OniMcp] Game-specific components initialized.");
        }
    }
    internal static class AutoDisinfectPolicy
    {
        private const string HarmonyId = "LIghtJUNction.OniMcp";
        private static readonly HashSet<string> ReportedPhases = new HashSet<string>();
        private static readonly FieldInfo AutoDisinfectablesField = AccessTools.Field(typeof(AutoDisinfectableManager), "autoDisinfectables");
        private static readonly FieldInfo EnabledField = AccessTools.Field(typeof(AutoDisinfectable), "enableAutoDisinfect");
        private static readonly MethodInfo DisableMethod = AccessTools.Method(typeof(AutoDisinfectable), "DisableAutoDisinfect");

        public static bool Disabled => OniMcpOptions.Current.GlobalAutoDisinfectDisabled;

        public static void SetDisabled(bool disabled, bool persist)
        {
            var options = OniMcpOptions.Current;
            options.GlobalAutoDisinfectDisabled = disabled;
            if (persist)
                OniMcpOptions.Save(options);
        }

        public static Dictionary<string, object> Status()
        {
            int total = 0;
            int enabled = 0;
            foreach (var item in Items())
            {
                if (item == null)
                    continue;
                total++;
                if (IsEnabled(item))
                    enabled++;
            }

            return new Dictionary<string, object>
            {
                ["globalAutoDisinfectDisabled"] = Disabled,
                ["autoDisinfectableCount"] = total,
                ["currentlyEnabledCount"] = enabled
            };
        }

        public static int ApplyToExisting()
        {
            if (!Disabled)
                return 0;

            int changed = 0;
            foreach (var item in Items())
            {
                if (ForceDisable(item))
                    changed++;
            }
            return changed;
        }

        public static bool ForceDisable(AutoDisinfectable item)
        {
            if (item == null || !IsEnabled(item))
                return false;

            if (DisableMethod != null)
                DisableMethod.Invoke(item, null);
            else if (EnabledField != null)
                EnabledField.SetValue(item, false);
            return true;
        }

        public static void EnsureInstalled(Harmony harmony, string phase)
        {
            try
            {
                harmony = harmony ?? new Harmony(HarmonyId);
                EnsurePatch(harmony, AccessTools.Method(typeof(AutoDisinfectable), "EnableAutoDisinfect"), typeof(AutoDisinfectable_EnableAutoDisinfect_Patch));
                EnsurePatch(harmony, AccessTools.Method(typeof(AutoDisinfectableManager), "AddAutoDisinfectable"), typeof(AutoDisinfectableManager_AddAutoDisinfectable_Patch));

                bool healthy = OwnersContainOniMcp(AccessTools.Method(typeof(AutoDisinfectable), "EnableAutoDisinfect"))
                    && OwnersContainOniMcp(AccessTools.Method(typeof(AutoDisinfectableManager), "AddAutoDisinfectable"));
                if (!healthy || ReportedPhases.Add(phase))
                {
                    var message = $"[OniMcp] Auto-disinfect policy patch check at {phase}: healthy={healthy}";
                    if (healthy)
                        OniMcpLog.Debug(message);
                    else
                        OniMcpLog.Warning(message);
                }
            }
            catch (Exception ex)
            {
                OniMcpLog.Warning($"[OniMcp] Failed auto-disinfect patch check at {phase}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static bool IsEnabled(AutoDisinfectable item)
        {
            return EnabledField != null && item != null && EnabledField.GetValue(item) is bool value && value;
        }

        private static IEnumerable<AutoDisinfectable> Items()
        {
            var manager = AutoDisinfectableManager.Instance;
            if (manager == null || AutoDisinfectablesField == null)
                return new List<AutoDisinfectable>();

            var items = AutoDisinfectablesField.GetValue(manager) as IEnumerable<AutoDisinfectable>;
            return items ?? new List<AutoDisinfectable>();
        }

        private static void EnsurePatch(Harmony harmony, MethodBase original, System.Type patchType)
        {
            if (original == null)
                return;

            var patchInfo = Harmony.GetPatchInfo(original);
            var hasOwnedPatch = patchInfo != null && patchInfo.Owners.Contains(HarmonyId);
            if (hasOwnedPatch)
                return;

            harmony.CreateClassProcessor(patchType).Patch();
        }

        private static bool OwnersContainOniMcp(MethodBase original)
        {
            return original != null && Harmony.GetPatchInfo(original)?.Owners.Contains(HarmonyId) == true;
        }
    }

    [HarmonyPatch(typeof(AutoDisinfectable), "EnableAutoDisinfect")]
    internal static class AutoDisinfectable_EnableAutoDisinfect_Patch
    {
        public static bool Prefix(AutoDisinfectable __instance)
        {
            if (!AutoDisinfectPolicy.Disabled)
                return true;

            AutoDisinfectPolicy.ForceDisable(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(AutoDisinfectableManager), "AddAutoDisinfectable")]
    internal static class AutoDisinfectableManager_AddAutoDisinfectable_Patch
    {
        public static void Postfix(AutoDisinfectable auto_disinfectable)
        {
            if (AutoDisinfectPolicy.Disabled)
                AutoDisinfectPolicy.ForceDisable(auto_disinfectable);
        }
}
}
