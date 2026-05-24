using HarmonyLib;
using KMod;
using OniMcp.Config;
using OniMcp.Server;
using OniMcp.Support;
using OniMcp.Tools;
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
    public class ModInfo : UserMod2
    {
        public override void OnLoad(Harmony harmony)
        {
            base.OnLoad(harmony);

            OniMcpPaths.Initialize(path, assembly);
            OniMcpOptions.Reload();

            // 注册 Harmony Patch
            harmony.PatchAll();
            InputSafetyPatchVerifier.EnsureInstalled(harmony, "OnLoad");
            OniMcpLog.Debug($"[OniMcp] Loaded assembly {assembly.GetName().Version} from {assembly.Location}");

            // 初始化 Tool 注册表
            OniToolRegistry.Initialize();

            // 尽早启动 MCP 服务器和主线程桥接器，使主菜单阶段即可连接
            var bridgeObj = new GameObject("OniMcp_MainThreadBridge");
            bridgeObj.AddComponent<MainThreadBridge>();
            var serverObj = new GameObject("OniMcp_HttpServer");
            serverObj.AddComponent<McpHttpServer>();

            OniMcpLog.Debug("[OniMcp] Mod loaded. MCP Server is starting...");
        }
    }

    /// <summary>
    /// 在数据库初始化后创建框选编辑工具运行时实例
    /// Db.Initialize() 是 ONI mod 最标准的游戏内切入点
    /// </summary>
    [HarmonyPatch(typeof(Db), "Initialize")]
    public static class Db_Initialize_Patch
    {
        public static void Postfix()
        {
            InputSafetyPatchVerifier.EnsureInstalled(null, "Db.Initialize");
            OniMcpLog.Debug("[OniMcp] Db.Initialize - Initializing game-specific components...");

            // 创建框选编辑工具运行时实例
            EditMarkerTool.EnsureInstance();
            PlanningViewOverlay.EnsureInstance();

            OniMcpLog.Debug("[OniMcp] Game-specific components initialized.");
        }
    }

    internal static class InputSafetyPatchVerifier
    {
        private const string HarmonyId = "LIghtJUNction.OniMcp";
        private static readonly HashSet<string> ReportedPhases = new HashSet<string>();

        public static void EnsureInstalled(Harmony harmony, string phase)
        {
            try
            {
                harmony = harmony ?? new Harmony(HarmonyId);

                var toolMenuOnKeyDown = AccessTools.Method(typeof(ToolMenu), "OnKeyDown", new[] { typeof(KButtonEvent) });
                var isAction = AccessTools.Method(typeof(KButtonEvent), nameof(KButtonEvent.IsAction), new[] { typeof(Action) });
                var buttonEventCtor = AccessTools.Constructor(typeof(KButtonEvent), new[] { typeof(KInputController), typeof(InputEventType), typeof(bool[]) });
                var actionCtor = AccessTools.Constructor(typeof(KButtonEvent), new[] { typeof(KInputController), typeof(InputEventType), typeof(Action) });

                EnsurePatch(harmony, toolMenuOnKeyDown, typeof(ToolMenu_OnKeyDown_Patch));
                EnsurePatch(harmony, isAction, typeof(KButtonEvent_IsAction_Patch));
                EnsurePatch(harmony, buttonEventCtor, typeof(KButtonEvent_BoolArrayCtor_Patch));
                EnsurePatch(harmony, actionCtor, typeof(KButtonEvent_ActionCtor_Patch));

                var toolMenuOwners = DescribeOwners(toolMenuOnKeyDown);
                var isActionOwners = DescribeOwners(isAction);
                var ctorOwners = DescribeOwners(buttonEventCtor);
                var actionCtorOwners = DescribeOwners(actionCtor);

                var healthy = OwnersContainOniMcp(toolMenuOnKeyDown)
                    && OwnersContainOniMcp(isAction)
                    && OwnersContainOniMcp(buttonEventCtor)
                    && OwnersContainOniMcp(actionCtor);
                if (!healthy || ReportedPhases.Add(phase))
                {
                    var message = $"[OniMcp] Input safety patch check at {phase}: healthy={healthy}; ToolMenu.OnKeyDown={toolMenuOwners}; KButtonEvent.IsAction={isActionOwners}; KButtonEvent(bool[])={ctorOwners}; KButtonEvent(Action)={actionCtorOwners}";
                    if (healthy)
                        OniMcpLog.Debug(message);
                    else
                        OniMcpLog.Warning(message);
                }
            }
            catch (System.Exception ex)
            {
                OniMcpLog.Warning($"[OniMcp] Failed input safety patch check at {phase}: {ex.GetType().Name}: {ex.Message}");
            }
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
            return Harmony.GetPatchInfo(original)?.Owners.Contains(HarmonyId) == true;
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

    [HarmonyPatch(typeof(ToolMenu), "CreateBasicTools")]
    public static class ToolMenu_CreateBasicTools_Patch
    {
        public static void Postfix(ToolMenu __instance)
        {
            if (__instance == null || __instance.basicTools == null)
                return;

            foreach (var collection in __instance.basicTools)
            {
                if (collection != null && collection.text == "MCP")
                    return;
            }

            EditMarkerTool.RegisterIconSprite();

            var mcpTools = ToolMenu.CreateToolCollection(
                "MCP",
                EditMarkerTool.IconName,
                // Action.NumActions is the game's no-hotkey sentinel for toolbar rows.
                // The OnKeyDown patch below keeps it away from raw action-array indexing.
                Action.NumActions,
                "OniMcpEditMarker",
                "MCP agent 工具",
                true);

            if (mcpTools.tools.Count > 0)
            {
                var tool = mcpTools.tools[0];
                tool.text = "编辑标记";
                tool.tooltip = "框选区域，输入修改提示词，然后让 MCP 客户端 agent 先计划再行动";
                tool.onSelectCallback = EditMarkerTool.ActivateFromMenu;
            }

            __instance.basicTools.Add(mcpTools);
        }
    }

    internal static class PlanningOverlayMenuInjector
    {
        private static readonly FieldInfo OverlayToggleInfosField = AccessTools.Field(typeof(OverlayMenu), "overlayToggleInfos");
        private static readonly Type OverlayToggleInfoType = typeof(OverlayMenu).GetNestedType("OverlayToggleInfo", BindingFlags.NonPublic);
        private static readonly FieldInfo SimViewField = OverlayToggleInfoType != null ? AccessTools.Field(OverlayToggleInfoType, "simView") : null;
        private static readonly ConstructorInfo OverlayToggleInfoCtor = OverlayToggleInfoType != null
            ? AccessTools.Constructor(OverlayToggleInfoType, new[] { typeof(string), typeof(string), typeof(HashedString), typeof(string), typeof(Action), typeof(string), typeof(string) })
            : null;
        private static bool suppressOverlayChanged;

        public static void AppendToggle(OverlayMenu instance)
        {
            try
            {
                if (instance == null || OverlayToggleInfosField == null || OverlayToggleInfoCtor == null)
                    return;

                var infos = OverlayToggleInfosField.GetValue(instance) as List<KIconToggleMenu.ToggleInfo>;
                if (infos == null || infos.Any(IsPlanningToggle))
                    return;

                EditMarkerTool.RegisterIconSprite();
                var toggle = OverlayToggleInfoCtor.Invoke(new object[]
                {
                    "MCP Plan",
                    EditMarkerTool.IconName,
                    PlanningViewOverlay.ModeId,
                    "",
                    Action.NumActions,
                    "Show MCP edit-mark planning areas and prompts.",
                    "MCP Plan"
                }) as KIconToggleMenu.ToggleInfo;

                if (toggle != null)
                    infos.Add(toggle);
            }
            catch (Exception ex)
            {
                OniMcpLog.Warning("[OniMcp] Failed to add planning overlay toggle: " + ex);
            }
        }

        public static bool HandleToggleSelect(KIconToggleMenu.ToggleInfo toggleInfo)
        {
            if (!IsPlanningToggle(toggleInfo))
                return true;

            bool visible = toggleInfo.toggle == null || toggleInfo.toggle.isOn;
            try
            {
                if (visible && OverlayScreen.Instance != null)
                {
                    suppressOverlayChanged = true;
                    OverlayScreen.Instance.ToggleOverlay(OverlayModes.None.ID, true);
                }
            }
            catch (Exception ex)
            {
                OniMcpLog.Warning("[OniMcp] Failed to clear active overlay for planning view: " + ex.Message);
            }
            finally
            {
                suppressOverlayChanged = false;
            }

            PlanningViewOverlay.SetVisible(visible);
            if (toggleInfo.toggle != null)
                toggleInfo.toggle.SetIsOnWithoutNotify(visible);
            return false;
        }

        public static void HandleOverlayChanged()
        {
            if (!suppressOverlayChanged && PlanningViewOverlay.IsVisible)
                PlanningViewOverlay.SetVisible(false);
        }

        private static bool IsPlanningToggle(KIconToggleMenu.ToggleInfo toggleInfo)
        {
            if (toggleInfo == null || SimViewField == null || !OverlayToggleInfoType.IsInstanceOfType(toggleInfo))
                return false;

            object value = SimViewField.GetValue(toggleInfo);
            return value is HashedString && (HashedString)value == PlanningViewOverlay.ModeId;
        }
    }

    [HarmonyPatch(typeof(OverlayMenu), "InitializeToggles")]
    public static class OverlayMenu_InitializeToggles_Patch
    {
        public static void Postfix(OverlayMenu __instance)
        {
            PlanningOverlayMenuInjector.AppendToggle(__instance);
        }
    }

    [HarmonyPatch(typeof(OverlayMenu), "OnToggleSelect")]
    public static class OverlayMenu_OnToggleSelect_Patch
    {
        public static bool Prefix(KIconToggleMenu.ToggleInfo toggle_info)
        {
            return PlanningOverlayMenuInjector.HandleToggleSelect(toggle_info);
        }
    }

    [HarmonyPatch(typeof(OverlayMenu), "OnOverlayChanged")]
    public static class OverlayMenu_OnOverlayChanged_Patch
    {
        public static void Postfix()
        {
            PlanningOverlayMenuInjector.HandleOverlayChanged();
        }
    }

    [HarmonyPatch(typeof(ToolMenu), "OnKeyDown")]
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

    [HarmonyPatch(typeof(GameUtil), nameof(GameUtil.GetHotkeyString), new[] { typeof(Action) })]
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

    [HarmonyPatch(typeof(KButtonEvent), nameof(KButtonEvent.IsAction))]
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

    [HarmonyPatch(typeof(KButtonEvent), MethodType.Constructor, new[] { typeof(KInputController), typeof(InputEventType), typeof(bool[]) })]
    public static class KButtonEvent_BoolArrayCtor_Patch
    {
        public static void Postfix(KButtonEvent __instance)
        {
            KButtonEventSafety.EnsureActionArrayCapacity(__instance);
        }
    }

    [HarmonyPatch(typeof(KButtonEvent), MethodType.Constructor, new[] { typeof(KInputController), typeof(InputEventType), typeof(Action) })]
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
