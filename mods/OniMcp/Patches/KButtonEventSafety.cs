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
    public static class KButtonEvent_BoolArrayCtor_Patch
    {
        public static void Postfix(KButtonEvent __instance)
        {
            KButtonEventSafety.EnsureActionArrayCapacity(__instance);
        }
    }

public static class KButtonEvent_ActionCtor_Patch
    {
        public static void Postfix(KButtonEvent __instance)
        {
            KButtonEventSafety.EnsureActionArrayCapacity(__instance);
        }
    }

    internal static class KButtonEventSafety
    {
        private static readonly FieldInfo IsActionField = AccessTools.Field(typeof(KButtonEvent), "mIsAction");
        private const int MinimumActionArrayLength = (int)Action.NumActions + 1;

        public static bool TryGetActionArray(KButtonEvent buttonEvent, out bool[] isAction)
        {
            isAction = IsActionField?.GetValue(buttonEvent) as bool[];
            return isAction != null;
        }

        public static void EnsureActionArrayCapacity(KButtonEvent buttonEvent)
        {
            if (!TryGetActionArray(buttonEvent, out var isAction))
                return;

            if (isAction.Length >= MinimumActionArrayLength)
                return;

            var expanded = new bool[MinimumActionArrayLength];
            System.Array.Copy(isAction, expanded, isAction.Length);
            IsActionField.SetValue(buttonEvent, expanded);
        }

        public static bool SafeIsAction(KButtonEvent buttonEvent, Action action)
        {
            if (buttonEvent == null)
                return false;

            if (TryGetActionArray(buttonEvent, out var isAction))
            {
                var index = (int)action;
                return index >= 0 && index < isAction.Length && isAction[index];
            }

            return buttonEvent.GetAction() == action;
        }

        public static bool SafeTryConsume(KButtonEvent buttonEvent, Action action)
        {
            if (buttonEvent == null)
                return false;

            if (buttonEvent.Consumed)
                return false;

            if (action != Action.NumActions && SafeIsAction(buttonEvent, action))
                buttonEvent.Consumed = true;

            return buttonEvent.Consumed;
        }

        public static int GetActionArrayLength(KButtonEvent buttonEvent)
        {
            return TryGetActionArray(buttonEvent, out var isAction) ? isAction.Length : 0;
        }
        }
}
