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
    public static class ToolMenu_OnKeyDown_Patch
    {
        private static readonly FieldInfo RowsField = AccessTools.Field(typeof(ToolMenu), "rows");
        private static readonly MethodInfo ChooseCollectionMethod = AccessTools.Method(typeof(ToolMenu), "ChooseCollection");
        private static readonly MethodInfo ChooseToolMethod = AccessTools.Method(typeof(ToolMenu), "ChooseTool");

        public static bool Prefix(ToolMenu __instance, KButtonEvent e)
        {
            KButtonEventSafety.EnsureActionArrayCapacity(e);
            var actionArrayLength = KButtonEventSafety.GetActionArrayLength(e);

            if (__instance == null)
                return true;

            try
            {
                SanitizeToolHotkeys(__instance.basicTools, actionArrayLength);
                SanitizeToolHotkeys(__instance.sandboxTools, actionArrayLength);

                var rows = RowsField?.GetValue(__instance) as List<List<ToolMenu.ToolCollection>>;
                if (rows == null)
                    return true;

                foreach (var row in rows)
                    SanitizeToolHotkeys(row, actionArrayLength);

                HandleOnKeyDown(__instance, e, rows);
                return false;
            }
            catch (System.Exception ex)
            {
                OniMcpLog.Debug($"[OniMcp] Failed safe ToolMenu key handling: {ex.GetType().Name}: {ex.Message}");
            }
            return true;
        }

        private static void HandleOnKeyDown(ToolMenu instance, KButtonEvent e, List<List<ToolMenu.ToolCollection>> rows)
        {
            if (!e.Consumed)
            {
                if (KButtonEventSafety.SafeIsAction(e, Action.ToggleSandboxTools))
                {
                    if (Application.isEditor)
                    {
                        DebugUtil.LogArgs("Force-enabling sandbox mode because we're in editor.");
                        SaveGame.Instance.sandboxEnabled = true;
                    }

                    if (SaveGame.Instance.sandboxEnabled)
                    {
                        Game.Instance.SandboxModeActive = !Game.Instance.SandboxModeActive;
                        KMonoBehaviour.PlaySound(Game.Instance.SandboxModeActive ? GlobalAssets.GetSound("SandboxTool_Toggle_On") : GlobalAssets.GetSound("SandboxTool_Toggle_Off"));
                    }
                }

                foreach (var row in rows)
                {
                    if (row == instance.sandboxTools && !Game.Instance.SandboxModeActive)
                        continue;

                    for (var i = 0; i < row.Count; i++)
                    {
                        var collection = row[i];
                        var toolHotkey = collection.hotkey;
                        if (toolHotkey != Action.NumActions && KButtonEventSafety.SafeIsAction(e, toolHotkey) && !CurrentCollectionHasHotkey(instance, toolHotkey))
                        {
                            if (instance.currentlySelectedCollection != collection)
                            {
                                ChooseCollection(instance, collection, false);
                                ChooseTool(instance, collection.tools[0]);
                            }
                            else if (instance.currentlySelectedCollection.tools.Count > 1)
                            {
                                e.Consumed = true;
                                ChooseCollection(instance, null, true);
                                ChooseTool(instance, null);
                                var sound = GlobalAssets.GetSound(PlayerController.Instance.ActiveTool.GetDeactivateSound());
                                if (sound != null)
                                    KMonoBehaviour.PlaySound(sound);
                            }

                            break;
                        }

                        for (var num = 0; num < collection.tools.Count; num++)
                        {
                            if ((instance.currentlySelectedCollection != null || collection.tools.Count != 1) && instance.currentlySelectedCollection != collection && (instance.currentlySelectedCollection == null || instance.currentlySelectedCollection.tools.Count != 1 || collection.tools.Count != 1))
                                continue;

                            var tool = collection.tools[num];
                            var hotkey = tool.hotkey;
                            if (KButtonEventSafety.SafeIsAction(e, hotkey) && KButtonEventSafety.SafeTryConsume(e, hotkey))
                            {
                                if (collection.tools.Count == 1 && instance.currentlySelectedCollection != collection)
                                    ChooseCollection(instance, collection, false);
                                else if (instance.currentlySelectedTool != tool)
                                    ChooseTool(instance, tool);
                            }
                            else if (ToolMenuHotkeySafety.SafeCompareActionKeyCodes(e.GetAction(), hotkey))
                            {
                                e.Consumed = true;
                            }
                        }
                    }
                }

                if ((instance.currentlySelectedTool != null || instance.currentlySelectedCollection != null) && !e.Consumed)
                {
                    if (KButtonEventSafety.SafeTryConsume(e, Action.Escape))
                    {
                        var sound = GlobalAssets.GetSound(PlayerController.Instance.ActiveTool.GetDeactivateSound());
                        if (sound != null)
                            KMonoBehaviour.PlaySound(sound);

                        if (instance.currentlySelectedCollection != null)
                            ChooseCollection(instance, null, true);

                        if (instance.currentlySelectedTool != null)
                            ChooseTool(instance, null);

                        SelectTool.Instance.Activate();
                    }
                }
                else if (!PlayerController.Instance.IsUsingDefaultTool() && !e.Consumed && KButtonEventSafety.SafeTryConsume(e, Action.Escape))
                {
                    SelectTool.Instance.Activate();
                }
            }

            // Do not reflectively call KScreen.OnKeyDown here: ToolMenu overrides it, and
            // virtual reflection dispatch can re-enter this prefix on some runtimes.
        }

        private static bool CurrentCollectionHasHotkey(ToolMenu instance, Action toolHotkey)
        {
            if (!ToolMenuHotkeySafety.IsDisplayableAction(toolHotkey))
                return false;

            return instance.currentlySelectedCollection != null
                && instance.currentlySelectedCollection.tools.Find(tool => ToolMenuHotkeySafety.SafeCompareActionKeyCodes(tool.hotkey, toolHotkey)) != null;
        }

        private static void ChooseCollection(ToolMenu instance, ToolMenu.ToolCollection collection, bool autoSelectTool)
        {
            ChooseCollectionMethod.Invoke(instance, new object[] { collection, autoSelectTool });
        }

        private static void ChooseTool(ToolMenu instance, ToolMenu.ToolInfo tool)
        {
            ChooseToolMethod.Invoke(instance, new object[] { tool });
        }

        private static void SanitizeToolHotkeys(IEnumerable<ToolMenu.ToolCollection> collections, int actionArrayLength)
        {
            if (collections == null)
                return;

            foreach (var collection in collections)
            {
                if (collection != null && IsUnsafeCollectionHotkey(collection.hotkey, actionArrayLength))
                    collection.hotkey = Action.NumActions;

                if (collection?.tools == null)
                    continue;

                foreach (var tool in collection.tools)
                {
                    if (tool != null && IsUnsafeToolHotkey(tool.hotkey, actionArrayLength))
                        tool.hotkey = Action.Invalid;
                }
            }
        }

        private static bool IsUnsafeToolHotkey(Action action, int actionArrayLength)
        {
            var value = (int)action;
            return value < 0 || value >= SafeActionLimit(actionArrayLength);
        }

        private static bool IsUnsafeCollectionHotkey(Action action, int actionArrayLength)
        {
            var value = (int)action;
            return value < 0 || (value != (int)Action.NumActions && value >= SafeActionLimit(actionArrayLength));
        }

        private static int SafeActionLimit(int actionArrayLength)
        {
            var enumLimit = (int)Action.NumActions;
            return actionArrayLength > 0 && actionArrayLength < enumLimit ? actionArrayLength : enumLimit;
        }

        public static System.Exception Finalizer(System.Exception __exception)
        {
            if (__exception is System.IndexOutOfRangeException)
            {
                OniMcpLog.Debug($"[OniMcp] Suppressed ToolMenu hotkey index error: {__exception.Message}");
                return null;
            }

            return __exception;
        }
    }

}
