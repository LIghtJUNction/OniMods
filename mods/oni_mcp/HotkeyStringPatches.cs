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
    public static class GameUtil_GetHotkeyString_Patch
    {
        public static bool Prefix(Action action, ref string __result)
        {
            if (!ToolMenuHotkeySafety.IsDisplayableAction(action))
            {
                __result = "";
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(GameUtil), nameof(GameUtil.ReplaceHotkeyString), new[] { typeof(string), typeof(Action) })]
    public static class GameUtil_ReplaceHotkeyString_Patch
    {
        public static bool Prefix(string template, Action action, ref string __result)
        {
            if (!ToolMenuHotkeySafety.IsDisplayableAction(action))
            {
                __result = ToolMenuHotkeySafety.RemoveHotkeyPlaceholder(template);
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(GameInputMapping), nameof(GameInputMapping.CompareActionKeyCodes), new[] { typeof(Action), typeof(Action) })]
    public static class GameInputMapping_CompareActionKeyCodes_Patch
    {
        public static bool Prefix(Action a, Action b, ref bool __result)
        {
            if (!ToolMenuHotkeySafety.IsBoundAction(a) || !ToolMenuHotkeySafety.IsBoundAction(b))
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    internal static class ToolMenuHotkeySafety
    {
        public static bool IsDisplayableAction(Action action)
        {
            return IsBoundAction(action);
        }

        public static bool IsBoundAction(Action action)
        {
            var value = (int)action;
            return action != Action.Invalid
                && action != Action.NumActions
                && value >= 0
                && value < (int)Action.NumActions;
        }

        public static bool SafeCompareActionKeyCodes(Action left, Action right)
        {
            return IsBoundAction(left)
                && IsBoundAction(right)
                && GameInputMapping.CompareActionKeyCodes(left, right);
        }

        public static string RemoveHotkeyPlaceholder(string template)
        {
            if (string.IsNullOrEmpty(template))
                return template;

            return template
                .Replace("{Hotkey}", "")
                .Replace("{hotkey}", "")
                .Replace("{HOTKEY}", "");
        }
    }

public static class KButtonEvent_IsAction_Patch
    {
        public static bool Prefix(KButtonEvent __instance, Action action, ref bool __result)
        {
            if (!KButtonEventSafety.TryGetActionArray(__instance, out var isAction))
                return true;

            var index = (int)action;
            if (index >= 0 && index < isAction.Length)
                return true;

            __result = false;
            return false;
        }

        public static System.Exception Finalizer(Action action, ref bool __result, System.Exception __exception)
        {
            if (__exception is System.IndexOutOfRangeException)
            {
                __result = false;
                OniMcpLog.Debug($"[OniMcp] Suppressed out-of-range KButtonEvent.IsAction({(int)action})");
                return null;
            }

            return __exception;
        }
    }

}
