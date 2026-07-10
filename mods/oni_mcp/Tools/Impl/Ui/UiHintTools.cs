using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class UiHintTools
    {
        private static readonly Dictionary<string, MapMarkerHandle> Markers = new Dictionary<string, MapMarkerHandle>();
        private static int markerCounter;

        public static McpTool ControlUiFeedback()
        {
            return new McpTool
            {
                Name = "ui_feedback_control",
                Group = "ui",
                Mode = "execute",
                Risk = "low",
                Aliases = new List<string> { "ui_signal_control", "player_feedback_control" },
                Tags = new List<string> { "ui", "feedback", "hint", "marker", "notification" },
                Description = "UI 反馈组合入口：domain=hint。hint 创建通知、浮字和地图标记。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["domain"] = new McpToolParameter { Type = "string", Description = "hint，默认 hint", Required = false, EnumValues = new List<string> { "hint" } },
                    ["action"] = new McpToolParameter { Type = "string", Description = "domain=hint: notification/popup/marker", Required = true },
                    ["markerAction"] = new McpToolParameter { Type = "string", Description = "domain=hint action=marker 时的子动作：create/list/clear", Required = false, EnumValues = new List<string> { "create", "list", "clear" } },
                    ["title"] = new McpToolParameter { Type = "string", Description = "domain=hint action=notification 时通知标题", Required = false },
                    ["message"] = new McpToolParameter { Type = "string", Description = "domain=hint action=notification 时通知正文", Required = false },
                    ["text"] = new McpToolParameter { Type = "string", Description = "domain=hint action=popup 时浮动提示文字", Required = false },
                    ["style"] = new McpToolParameter { Type = "string", Description = "domain=hint action=popup 时图标风格", Required = false },
                    ["label"] = new McpToolParameter { Type = "string", Description = "domain=hint action=marker markerAction=create 时标签文字", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "目标格 X，按 action 解释", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "目标格 Y，按 action 解释", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认当前激活世界", Required = false },
                    ["duration"] = new McpToolParameter { Type = "number", Description = "domain=hint action=popup/marker 时显示或保留秒数", Required = false },
                    ["focus"] = new McpToolParameter { Type = "boolean", Description = "是否移动相机到目标，按 action 解释", Required = false },
                    ["id"] = new McpToolParameter { Type = "string", Description = "action=clear 时目标 ID", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "action=list 时返回数量限制", Required = false },
                    ["all"] = new McpToolParameter { Type = "boolean", Description = "action=clear 时是否清除全部", Required = false },
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "强制执行，按 action 解释", Required = false }
                },
                Handler = args =>
                {
                    string domain = (args["domain"]?.ToString() ?? "hint").Trim().ToLowerInvariant();
                    var forwarded = new JObject(args);
                    forwarded.Remove("domain");
                    switch (domain)
                    {
                        case "hint":
                        case "hints":
                        case "ui_hint":
                            return ControlUiHint().Handler(forwarded);
                        default:
                            return CallToolResult.Error("domain must be hint");
                    }
                }
            };
        }

        public static McpTool ControlUiHint()
        {
            return new McpTool
            {
                Name = "ui_hint_control",
                Group = "ui",
                Mode = "execute",
                Risk = "low",
                Aliases = new List<string> { "ui_hints_control", "map_hint_control" },
                Tags = new List<string> { "ui", "hint", "notification", "popup", "marker", "map" },
                Description = "统一创建 UI 提示和管理地图标记。action=notification/popup/marker；marker 使用 markerAction=create/list/clear。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "动作：notification、popup、marker", Required = true, EnumValues = new List<string> { "notification", "popup", "marker" } },
                    ["markerAction"] = new McpToolParameter { Type = "string", Description = "action=marker 时的子动作：create、list、clear", Required = false, EnumValues = new List<string> { "create", "list", "clear" } },
                    ["title"] = new McpToolParameter { Type = "string", Description = "action=notification 时通知标题", Required = false },
                    ["message"] = new McpToolParameter { Type = "string", Description = "action=notification 时通知悬浮提示/正文", Required = false },
                    ["type"] = new McpToolParameter { Type = "string", Description = "action=notification 时通知类型：neutral、good、bad_minor、bad、tutorial、message、important、event", Required = false, EnumValues = new List<string> { "neutral", "good", "bad_minor", "bad", "tutorial", "message", "important", "event" } },
                    ["playSound"] = new McpToolParameter { Type = "boolean", Description = "action=notification 时是否播放通知音效，默认 true", Required = false },
                    ["clearOnClick"] = new McpToolParameter { Type = "boolean", Description = "action=notification 时点击通知后是否清除，默认 false", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "action=notification 可选聚焦格子 X；action=popup/marker create 时目标格子 X", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "action=notification 可选聚焦格子 Y；action=popup/marker create 时目标格子 Y", Required = false },
                    ["text"] = new McpToolParameter { Type = "string", Description = "action=popup 时浮动提示文字", Required = false },
                    ["style"] = new McpToolParameter { Type = "string", Description = "action=popup 时图标风格：info、good、bad、resource、building、research", Required = false, EnumValues = new List<string> { "info", "good", "bad", "resource", "building", "research" } },
                    ["label"] = new McpToolParameter { Type = "string", Description = "action=marker markerAction=create 时可选浮动标签文字", Required = false },
                    ["duration"] = new McpToolParameter { Type = "number", Description = "action=popup/marker create 时显示或保留秒数", Required = false },
                    ["focus"] = new McpToolParameter { Type = "boolean", Description = "action=popup/marker create 时是否移动相机到目标", Required = false },
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "action=popup 时即使目标不在当前视野也强制生成，默认 true", Required = false },
                    ["id"] = new McpToolParameter { Type = "string", Description = "action=marker markerAction=clear 时标记 ID；留空且 all=true 时清除全部", Required = false },
                    ["all"] = new McpToolParameter { Type = "boolean", Description = "action=marker markerAction=clear 时是否清除全部标记，默认 false", Required = false },
                    ["expires"] = new McpToolParameter { Type = "boolean", Description = "action=notification 时是否按游戏通知默认时长自动消失，默认 true", Required = false }
                },
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    switch (action)
                    {
                        case "notification":
                            return CreateNotification().Handler(args);
                        case "popup":
                            return CreatePopupText().Handler(args);
                        case "marker":
                            var forwarded = (JObject)args.DeepClone();
                            string markerAction = (forwarded["markerAction"]?.ToString() ?? "").Trim().ToLowerInvariant();
                            if (string.IsNullOrEmpty(markerAction))
                                return CallToolResult.Error("markerAction must be create, list, or clear when action=marker");
                            forwarded["action"] = markerAction;
                            forwarded.Remove("markerAction");
                            return ControlMapMarker().Handler(forwarded);
                        default:
                            return CallToolResult.Error("action must be notification, popup, or marker");
                    }
                }
            };
        }

        public static McpTool CreateNotification()
        {
            return new McpTool
            {
                Name = "game_notification_create",
                Group = "ui",
                Mode = "execute",
                Risk = "low",
                Hidden = true,
                Description = "兼容入口：请优先使用 game_control domain=ui uiDomain=feedback action=notification。创建游戏原生通知，可选点击后聚焦地图格子",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["title"] = new McpToolParameter { Type = "string", Description = "通知标题", Required = true },
                    ["message"] = new McpToolParameter { Type = "string", Description = "通知悬浮提示/正文", Required = false },
                    ["type"] = new McpToolParameter
                    {
                        Type = "string",
                        Description = "通知类型：neutral、good、bad_minor、bad、tutorial、message、important、event",
                        Required = false,
                        EnumValues = new List<string> { "neutral", "good", "bad_minor", "bad", "tutorial", "message", "important", "event" }
                    },
                    ["expires"] = new McpToolParameter { Type = "boolean", Description = "是否按游戏通知默认时长自动消失，默认 true", Required = false },
                    ["playSound"] = new McpToolParameter { Type = "boolean", Description = "是否播放通知音效，默认 true", Required = false },
                    ["clearOnClick"] = new McpToolParameter { Type = "boolean", Description = "点击通知后是否清除，默认 false", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "可选目标格子 X，提供后点击通知会聚焦该格子", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "可选目标格子 Y，提供后点击通知会聚焦该格子", Required = false }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");
                    if (NotificationManager.Instance == null)
                        return CallToolResult.Error("NotificationManager not available");

                    string title = NormalizeText(args["title"]?.ToString(), 120);
                    if (string.IsNullOrEmpty(title))
                        return CallToolResult.Error("title is required");

                    string message = NormalizeText(args["message"]?.ToString(), 2000);
                    NotificationType type = ParseNotificationType(args["type"]?.ToString());
                    bool expires = ToolUtil.GetBool(args, "expires", true);
                    bool playSound = ToolUtil.GetBool(args, "playSound", true);
                    bool clearOnClick = ToolUtil.GetBool(args, "clearOnClick", false);
                    var target = ResolveTarget(args);

                    Func<List<Notification>, object, string> tooltip = null;
                    if (!string.IsNullOrEmpty(message))
                        tooltip = TooltipText;

                    Notification.ClickCallback callback = null;
                    if (target != null)
                        callback = FocusNotificationTarget;

                    var notification = new Notification(
                        title,
                        type,
                        tooltip,
                        message,
                        expires,
                        0f,
                        callback,
                        target,
                        null,
                        true,
                        clearOnClick,
                        true);

                    notification.playSound = playSound;
                    notification.GameTime = Time.time;
                    notification.Time = KTime.Instance.UnscaledGameTime;
                    NotificationManager.Instance.AddNotification(notification);

                    var result = new Dictionary<string, object>
                    {
                        ["created"] = true,
                        ["title"] = title,
                        ["type"] = type.ToString(),
                        ["expires"] = expires,
                        ["playSound"] = playSound,
                        ["clearOnClick"] = clearOnClick,
                        ["target"] = target?.ToDictionary()
                    };
                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool CreatePopupText()
        {
            return new McpTool
            {
                Name = "map_popup_text",
                Group = "map",
                Mode = "execute",
                Risk = "low",
                Hidden = true,
                Description = "兼容入口：请优先使用 game_control domain=ui uiDomain=feedback action=popup。在地图格子上创建游戏原生浮动文字提示",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["x"] = new McpToolParameter { Type = "integer", Description = "目标格子 X", Required = true },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "目标格子 Y", Required = true },
                    ["text"] = new McpToolParameter { Type = "string", Description = "浮动提示文字", Required = true },
                    ["style"] = new McpToolParameter
                    {
                        Type = "string",
                        Description = "图标风格：info、good、bad、resource、building、research",
                        Required = false,
                        EnumValues = new List<string> { "info", "good", "bad", "resource", "building", "research" }
                    },
                    ["duration"] = new McpToolParameter { Type = "number", Description = "显示秒数，默认 3，范围 0.5-30", Required = false },
                    ["focus"] = new McpToolParameter { Type = "boolean", Description = "是否同时移动相机到目标，默认 false", Required = false },
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "即使目标不在当前视野也强制生成，默认 true", Required = false }
                },
                Handler = args =>
                {
                    var target = ResolveRequiredCell(args);
                    if (target.Error != null)
                        return CallToolResult.Error(target.Error);
                    if (PopFXManager.Instance == null || !PopFXManager.Instance.Ready())
                        return CallToolResult.Error("PopFXManager not available");

                    string text = NormalizeText(args["text"]?.ToString(), 120);
                    if (string.IsNullOrEmpty(text))
                        return CallToolResult.Error("text is required");

                    float duration = Mathf.Clamp(ToolUtil.GetFloat(args, "duration") ?? 3f, 0.5f, 30f);
                    bool focus = ToolUtil.GetBool(args, "focus", false);
                    bool force = ToolUtil.GetBool(args, "force", true);

                    var targetObject = CreateTargetObject("OniMcp_PopupTarget", target.Position);
                    var fx = PopFXManager.Instance.SpawnFX(
                        ResolvePopFxIcon(args["style"]?.ToString()),
                        text,
                        targetObject.transform,
                        Vector3.zero,
                        duration,
                        false,
                        force);
                    UnityEngine.Object.Destroy(targetObject, duration + 1f);

                    if (focus)
                        FocusCell(target);

                    var result = new Dictionary<string, object>
                    {
                        ["created"] = fx != null,
                        ["text"] = text,
                        ["style"] = NormalizeStyle(args["style"]?.ToString()),
                        ["duration"] = duration,
                        ["focused"] = focus,
                        ["target"] = target.ToDictionary()
                    };
                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

    }
}
