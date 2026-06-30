using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static class NotificationTools
    {
        private static readonly FieldInfo ScreenNotificationsField = AccessTools.Field(typeof(NotificationScreen), "notifications");
        private static readonly FieldInfo ManagerNotificationsField = AccessTools.Field(typeof(NotificationManager), "notifications");
        private static readonly MethodInfo ShowMessageMethod = AccessTools.Method(typeof(NotificationScreen), "ShowMessage");

        public static McpTool ListNotifications()
        {
            return new McpTool
            {
                Name = "notifications_list",
                Group = "colony",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "notification_screen_list", "hud_notifications_list" },
                Tags = new List<string> { "notifications", "alerts", "hud", "messages", "focus" },
                Description = "兼容入口：请优先使用 colony_control domain=notification action=list。列出当前 NotificationScreen/NotificationManager 中的玩家通知，包括可聚焦目标、消息、可清除状态和类型",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按标题、类型、目标名称、prefabId 或 notifier 筛选", Required = false },
                    ["includePending"] = new McpToolParameter { Type = "boolean", Description = "是否包含尚未显示的 pending 通知；默认 false", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 80，最大 200", Required = false }
                },
                Handler = args =>
                {
                    string query = args["query"]?.ToString();
                    bool includePending = ToolUtil.GetBool(args, "includePending", false);
                    int limit = ToolUtil.ClampLimit(args, 80, 200);
                    var notifications = GetNotifications(includePending)
                        .Select((notification, index) => NotificationInfo(notification, index))
                        .Where(info => MatchesQuery(info, query))
                        .Take(limit)
                        .ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["returned"] = notifications.Count,
                        ["notifications"] = notifications,
                        ["notes"] = new[]
                        {
                            "Use colony_control domain=notification action=click to reproduce a notification row click: focus/select target, invoke custom callbacks, or open message dialogs.",
                            "Use colony_control domain=notification action=dismiss only for notifications with dismissable=true unless force=true."
                        }
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ControlNotification()
        {
            return new McpTool
            {
                Name = "notification_control",
                Group = "colony",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "notifications_control", "hud_notification_control" },
                Tags = new List<string> { "notifications", "alerts", "hud", "messages", "focus", "dismiss" },
                Description = "统一读取、点击和清除 HUD 通知。action=list/click/dismiss；click 会聚焦/选择目标或打开消息，dismiss 会清除可清除通知。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "动作：list、click、dismiss", Required = true, EnumValues = new List<string> { "list", "click", "dismiss" } },
                    ["query"] = new McpToolParameter { Type = "string", Description = "action=list 时按标题、类型、目标名称、prefabId 或 notifier 筛选", Required = false },
                    ["includePending"] = new McpToolParameter { Type = "boolean", Description = "action=list 时是否包含尚未显示的 pending 通知；默认 false", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "action=list 时最多返回数量，默认 80，最大 200", Required = false },
                    ["index"] = new McpToolParameter { Type = "integer", Description = "action=click/dismiss 时 notifications_list 返回的 index；默认 0", Required = false },
                    ["title"] = new McpToolParameter { Type = "string", Description = "action=click/dismiss 时按通知标题精确或模糊匹配；优先于 index", Required = false },
                    ["type"] = new McpToolParameter { Type = "string", Description = "action=click/dismiss 时按 NotificationType 筛选，如 Bad、Messages、Event", Required = false },
                    ["allWithSameTitle"] = new McpToolParameter { Type = "boolean", Description = "action=dismiss 时清除同标题通知组，默认 true", Required = false },
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "action=dismiss 时允许清除 showDismissButton=false 的通知，默认 false", Required = false }
                },
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    switch (action)
                    {
                        case "list":
                            return ListNotifications().Handler(args);
                        case "click":
                            return ClickNotification().Handler(args);
                        case "dismiss":
                            return DismissNotification().Handler(args);
                        default:
                            return CallToolResult.Error("action must be list, click, or dismiss");
                    }
                }
            };
        }

        public static McpTool ClickNotification()
        {
            return new McpTool
            {
                Name = "notification_click",
                Group = "colony",
                Mode = "write",
                Risk = "low",
                Hidden = true,
                Aliases = new List<string> { "notification_focus", "hud_notification_click" },
                Tags = new List<string> { "notifications", "alerts", "hud", "messages", "focus", "selection" },
                Description = "兼容入口：请优先使用 colony_control domain=notification action=click。复现玩家点击通知：聚焦/选择通知目标，执行 custom click callback，或打开消息通知对话框；clearOnClick 通知会被清除",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["index"] = new McpToolParameter { Type = "integer", Description = "notifications_list 返回的 index；默认 0", Required = false },
                    ["title"] = new McpToolParameter { Type = "string", Description = "按通知标题精确或模糊匹配；优先于 index", Required = false },
                    ["type"] = new McpToolParameter { Type = "string", Description = "按 NotificationType 筛选，如 Bad、Messages、Event", Required = false }
                },
                Handler = args =>
                {
                    var notification = FindNotification(args);
                    if (notification == null)
                        return CallToolResult.Error("Notification not found");

                    var before = NotificationInfo(notification, IndexOf(notification));
                    string action = Click(notification);
                    var after = GetNotifications(includePending: true).Contains(notification)
                        ? NotificationInfo(notification, IndexOf(notification))
                        : null;

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["action"] = action,
                        ["before"] = before,
                        ["after"] = after
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool DismissNotification()
        {
            return new McpTool
            {
                Name = "notification_dismiss",
                Group = "colony",
                Mode = "write",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "notification_clear", "hud_notification_dismiss" },
                Tags = new List<string> { "notifications", "alerts", "hud", "messages", "dismiss" },
                Description = "兼容入口：请优先使用 colony_control domain=notification action=dismiss。清除可 dismiss 的通知；allWithSameTitle=true 等价于点击通知组的 dismiss 按钮",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["index"] = new McpToolParameter { Type = "integer", Description = "notifications_list 返回的 index；默认 0", Required = false },
                    ["title"] = new McpToolParameter { Type = "string", Description = "按通知标题精确或模糊匹配；优先于 index", Required = false },
                    ["type"] = new McpToolParameter { Type = "string", Description = "按 NotificationType 筛选，如 Bad、Messages、Event", Required = false },
                    ["allWithSameTitle"] = new McpToolParameter { Type = "boolean", Description = "清除同标题通知组，默认 true", Required = false },
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "允许清除 showDismissButton=false 的通知，默认 false", Required = false }
                },
                Handler = args =>
                {
                    var notification = FindNotification(args);
                    if (notification == null)
                        return CallToolResult.Error("Notification not found");

                    bool allWithSameTitle = ToolUtil.GetBool(args, "allWithSameTitle", true);
                    bool force = ToolUtil.GetBool(args, "force", false);
                    var targets = allWithSameTitle
                        ? GetNotifications(includePending: true).Where(item => item.titleText == notification.titleText).ToList()
                        : new List<Notification> { notification };

                    var dismissed = new List<Dictionary<string, object>>();
                    foreach (var target in targets)
                    {
                        if (!target.showDismissButton && !force)
                            continue;
                        dismissed.Add(NotificationInfo(target, IndexOf(target)));
                        target.Clear();
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["dismissed"] = dismissed.Count,
                        ["notifications"] = dismissed,
                        ["remaining"] = GetNotifications(includePending: true).Count
                    }, McpJsonUtil.Settings));
                }
            };
        }

        private static List<Notification> GetNotifications(bool includePending)
        {
            var result = new List<Notification>();
            AddRange(result, ScreenNotificationsField?.GetValue(NotificationScreen.Instance) as IEnumerable<Notification>);
            AddRange(result, ManagerNotificationsField?.GetValue(NotificationManager.Instance) as IEnumerable<Notification>);
            if (includePending)
            {
                var screenPending = AccessTools.Field(typeof(NotificationScreen), "pendingNotifications")?.GetValue(NotificationScreen.Instance) as IEnumerable<Notification>;
                var managerPending = AccessTools.Field(typeof(NotificationManager), "pendingNotifications")?.GetValue(NotificationManager.Instance) as IEnumerable<Notification>;
                AddRange(result, screenPending);
                AddRange(result, managerPending);
            }

            return result
                .Where(item => item != null)
                .GroupBy(item => item)
                .Select(group => group.Key)
                .OrderBy(item => item.Type)
                .ThenBy(item => item.Idx)
                .ToList();
        }

        private static void AddRange(List<Notification> result, IEnumerable<Notification> items)
        {
            if (items == null)
                return;
            result.AddRange(items.Where(item => item != null));
        }

        private static Notification FindNotification(JObject args)
        {
            string title = args["title"]?.ToString();
            string type = args["type"]?.ToString();
            int index = Math.Max(0, ToolUtil.GetInt(args, "index") ?? 0);
            var notifications = GetNotifications(includePending: true)
                .Where(item => string.IsNullOrWhiteSpace(type) || string.Equals(item.Type.ToString(), type.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (!string.IsNullOrWhiteSpace(title))
            {
                var exact = notifications.FirstOrDefault(item => string.Equals(item.titleText, title.Trim(), StringComparison.OrdinalIgnoreCase));
                if (exact != null)
                    return exact;
                return notifications.FirstOrDefault(item => item.titleText != null && item.titleText.IndexOf(title.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);
            }

            return index < notifications.Count ? notifications[index] : null;
        }

        private static int IndexOf(Notification notification)
        {
            return GetNotifications(includePending: true).IndexOf(notification);
        }

        private static string Click(Notification notification)
        {
            if (notification.customClickCallback != null)
            {
                notification.customClickCallback(notification.customClickData);
                if (notification.clearOnClick)
                    notification.Clear();
                return "custom_callback";
            }

            if (notification is MessageNotification messageNotification && NotificationScreen.Instance != null && ShowMessageMethod != null)
            {
                ShowMessageMethod.Invoke(NotificationScreen.Instance, new object[] { messageNotification });
                return "message_dialog";
            }

            string action = FocusNotificationTarget(notification);
            if (notification.clearOnClick)
                notification.Clear();
            return action;
        }

        private static string FocusNotificationTarget(Notification notification)
        {
            Transform focus = notification.clickFocus;
            if (focus != null)
            {
                var position = focus.GetPosition();
                position.z = -40f;
                var clusterEntity = focus.GetComponent<ClusterGridEntity>();
                var selectable = focus.GetComponent<KSelectable>();
                int worldId = focus.gameObject.GetMyWorldId();
                if (worldId != -1)
                {
                    GameUtil.FocusCameraOnWorld(worldId, position);
                }
                else if (DlcManager.FeatureClusterSpaceEnabled() && clusterEntity != null && clusterEntity.IsVisible)
                {
                    ManagementMenu.Instance?.OpenClusterMap();
                    ClusterMapScreen.Instance?.SetTargetFocusPosition(clusterEntity.Location);
                }

                if (selectable != null)
                {
                    if (DlcManager.FeatureClusterSpaceEnabled() && clusterEntity != null && clusterEntity.IsVisible)
                        ClusterMapSelectTool.Instance?.Select(selectable);
                    else
                        SelectTool.Instance?.Select(selectable);
                }
                return "focus_click_target";
            }

            if (notification.Notifier != null)
            {
                var selectable = notification.Notifier.GetComponent<KSelectable>();
                if (selectable != null)
                {
                    SelectTool.Instance?.Select(selectable);
                    return "select_notifier";
                }
            }

            return "no_focus_target";
        }

        private static Dictionary<string, object> NotificationInfo(Notification notification, int index)
        {
            Transform focus = notification.clickFocus;
            GameObject focusGo = focus != null ? focus.gameObject : null;
            var focusKpid = focusGo?.GetComponent<KPrefabID>();
            var focusBuilding = focusGo?.GetComponent<Building>();
            int focusCell = focusGo != null ? Grid.PosToCell(focusGo) : Grid.InvalidCell;
            var notifierGo = notification.Notifier != null ? notification.Notifier.gameObject : null;
            var notifierKpid = notifierGo?.GetComponent<KPrefabID>();
            return new Dictionary<string, object>
            {
                ["index"] = index,
                ["title"] = notification.titleText,
                ["type"] = notification.Type.ToString(),
                ["idx"] = notification.Idx,
                ["notifierName"] = notification.NotifierName,
                ["notifier"] = notifierGo == null ? null : new Dictionary<string, object>
                {
                    ["id"] = notifierKpid?.InstanceID ?? notifierGo.GetInstanceID(),
                    ["prefabId"] = notifierKpid?.PrefabTag.Name ?? notifierGo.name,
                    ["name"] = ToolUtil.CleanName(notifierGo.GetProperName())
                },
                ["focus"] = focusGo == null ? null : new Dictionary<string, object>
                {
                    ["id"] = focusKpid?.InstanceID ?? focusGo.GetInstanceID(),
                    ["prefabId"] = focusBuilding?.Def?.PrefabID ?? focusKpid?.PrefabTag.Name ?? focusGo.name,
                    ["name"] = ToolUtil.CleanName(focusGo.GetProperName()),
                    ["x"] = Grid.IsValidCell(focusCell) ? Grid.CellColumn(focusCell) : -1,
                    ["y"] = Grid.IsValidCell(focusCell) ? Grid.CellRow(focusCell) : -1,
                    ["worldId"] = Grid.IsValidCell(focusCell) && Grid.IsWorldValidCell(focusCell) ? Grid.WorldIdx[focusCell] : -1
                },
                ["isMessage"] = notification is MessageNotification,
                ["hasCustomClick"] = notification.customClickCallback != null,
                ["clearOnClick"] = notification.clearOnClick,
                ["dismissable"] = notification.showDismissButton,
                ["expires"] = notification.expires,
                ["ready"] = notification.IsReady()
            };
        }

        private static bool MatchesQuery(Dictionary<string, object> info, string query)
        {
            return string.IsNullOrWhiteSpace(query)
                   || JsonConvert.SerializeObject(info).IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
