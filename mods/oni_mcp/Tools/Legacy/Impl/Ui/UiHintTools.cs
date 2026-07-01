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
    public static class UiHintTools
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
                Tags = new List<string> { "ui", "feedback", "hint", "marker", "notification", "edit-mark", "planning" },
                Description = "UI 反馈/编辑标记组合入口：domain=hint/edit_mark。hint 创建通知、浮字和地图标记；edit_mark 创建、列出和清除框选区域编辑请求。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["domain"] = new McpToolParameter { Type = "string", Description = "hint 或 edit_mark，默认 hint", Required = false, EnumValues = new List<string> { "hint", "edit_mark" } },
                    ["action"] = new McpToolParameter { Type = "string", Description = "domain=hint: notification/popup/marker；domain=edit_mark: create/list/clear", Required = true },
                    ["markerAction"] = new McpToolParameter { Type = "string", Description = "domain=hint action=marker 时的子动作：create/list/clear", Required = false, EnumValues = new List<string> { "create", "list", "clear" } },
                    ["title"] = new McpToolParameter { Type = "string", Description = "domain=hint action=notification 时通知标题", Required = false },
                    ["message"] = new McpToolParameter { Type = "string", Description = "domain=hint action=notification 时通知正文", Required = false },
                    ["text"] = new McpToolParameter { Type = "string", Description = "domain=hint action=popup 时浮动提示文字", Required = false },
                    ["style"] = new McpToolParameter { Type = "string", Description = "domain=hint action=popup 时图标风格", Required = false },
                    ["label"] = new McpToolParameter { Type = "string", Description = "domain=hint action=marker markerAction=create 时标签文字", Required = false },
                    ["prompt"] = new McpToolParameter { Type = "string", Description = "domain=edit_mark action=create 时用户对框选区域的修改提示词", Required = false },
                    ["areaId"] = new McpToolParameter { Type = "string", Description = "domain=edit_mark action=create 时可选区域句柄", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "目标格 X，按 action 解释", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "目标格 Y，按 action 解释", Required = false },
                    ["x1"] = new McpToolParameter { Type = "integer", Description = "domain=edit_mark action=create 时区域起点 X", Required = false },
                    ["y1"] = new McpToolParameter { Type = "integer", Description = "domain=edit_mark action=create 时区域起点 Y", Required = false },
                    ["x2"] = new McpToolParameter { Type = "integer", Description = "domain=edit_mark action=create 时区域终点 X", Required = false },
                    ["y2"] = new McpToolParameter { Type = "integer", Description = "domain=edit_mark action=create 时区域终点 Y", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认当前激活世界", Required = false },
                    ["includeTextMap"] = new McpToolParameter { Type = "boolean", Description = "domain=edit_mark action=create 时是否内联文本地图，默认 true", Required = false },
                    ["includeScreenshot"] = new McpToolParameter { Type = "boolean", Description = "domain=edit_mark action=create 时是否附带截图路径，默认 false", Required = false },
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
                        case "edit_mark":
                        case "edit_marks":
                        case "edit_marker":
                            return EditMarkTools.ControlEditMarkRequest().Handler(forwarded);
                        default:
                            return CallToolResult.Error("domain must be hint or edit_mark");
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

        public static McpTool CreateMapMarker()
        {
            return new McpTool
            {
                Name = "map_marker_create",
                Group = "map",
                Mode = "execute",
                Risk = "low",
                Hidden = true,
                Description = "兼容入口：请优先使用 game_control domain=ui uiDomain=feedback action=marker markerAction=create。在地图格子上创建游戏原生选择标记，可选浮动标签",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["x"] = new McpToolParameter { Type = "integer", Description = "目标格子 X", Required = true },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "目标格子 Y", Required = true },
                    ["label"] = new McpToolParameter { Type = "string", Description = "可选浮动标签文字", Required = false },
                    ["duration"] = new McpToolParameter { Type = "number", Description = "标记保留秒数，默认 60，范围 1-3600", Required = false },
                    ["focus"] = new McpToolParameter { Type = "boolean", Description = "是否同时移动相机到目标，默认 true", Required = false }
                },
                Handler = args =>
                {
                    var target = ResolveRequiredCell(args);
                    if (target.Error != null)
                        return CallToolResult.Error(target.Error);
                    if (GameScreenManager.Instance == null || EntityPrefabs.Instance == null || EntityPrefabs.Instance.SelectMarker == null)
                        return CallToolResult.Error("SelectMarker prefab not available");

                    string id = "marker_" + (++markerCounter).ToString("D4");
                    string label = NormalizeText(args["label"]?.ToString(), 120);
                    float duration = Mathf.Clamp(ToolUtil.GetFloat(args, "duration") ?? 60f, 1f, 3600f);
                    bool focus = ToolUtil.GetBool(args, "focus", true);

                    var targetObject = CreateTargetObject("OniMcp_MapMarkerTarget_" + id, target.Position);
                    var marker = Util.KInstantiateUI<SelectMarker>(
                        EntityPrefabs.Instance.SelectMarker,
                        GameScreenManager.Instance.worldSpaceCanvas,
                        true);
                    marker.name = "OniMcp_MapMarker_" + id;
                    marker.SetTargetTransform(targetObject.transform);
                    marker.gameObject.SetActive(true);

                    var handle = targetObject.AddComponent<MapMarkerHandle>();
                    handle.Initialize(id, label, target, marker, targetObject, duration, RemoveMarkerByIdCallback);
                    Markers[id] = handle;

                    if (!string.IsNullOrEmpty(label) && PopFXManager.Instance != null && PopFXManager.Instance.Ready())
                    {
                        PopFXManager.Instance.SpawnFX(
                            ResolvePopFxIcon("info"),
                            label,
                            targetObject.transform,
                            Vector3.up * 0.5f,
                            Mathf.Min(duration, 8f),
                            true,
                            true);
                    }

                    if (focus)
                        FocusCell(target);

                    var result = new Dictionary<string, object>
                    {
                        ["created"] = true,
                        ["id"] = id,
                        ["label"] = label,
                        ["duration"] = duration,
                        ["focused"] = focus,
                        ["target"] = target.ToDictionary()
                    };
                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ControlMapMarker()
        {
            return new McpTool
            {
                Name = "map_marker_control",
                Group = "map",
                Mode = "execute",
                Risk = "low",
                Hidden = true,
                Aliases = new List<string> { "map_markers_control", "map_marker_manage" },
                Tags = new List<string> { "map", "marker", "create", "list", "clear", "ui" },
                Description = "兼容入口：请优先使用 game_control domain=ui uiDomain=feedback action=marker markerAction=create/list/clear。统一管理 MCP 地图标记。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "动作：create、list、clear", Required = true, EnumValues = new List<string> { "create", "list", "clear" } },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "action=create 时目标格子 X", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "action=create 时目标格子 Y", Required = false },
                    ["label"] = new McpToolParameter { Type = "string", Description = "action=create 时可选浮动标签文字", Required = false },
                    ["duration"] = new McpToolParameter { Type = "number", Description = "action=create 时标记保留秒数，默认 60，范围 1-3600", Required = false },
                    ["focus"] = new McpToolParameter { Type = "boolean", Description = "action=create 时是否同时移动相机到目标，默认 true", Required = false },
                    ["id"] = new McpToolParameter { Type = "string", Description = "action=clear 时标记 ID；留空且 all=true 时清除全部", Required = false },
                    ["all"] = new McpToolParameter { Type = "boolean", Description = "action=clear 时是否清除全部标记，默认 false", Required = false }
                },
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    switch (action)
                    {
                        case "create":
                            return CreateMapMarker().Handler(args);
                        case "list":
                            return ListMapMarkers().Handler(args);
                        case "clear":
                            return ClearMapMarker().Handler(args);
                        default:
                            return CallToolResult.Error("action must be create, list, or clear");
                    }
                }
            };
        }

        public static McpTool ListMapMarkers()
        {
            return new McpTool
            {
                Name = "map_marker_list",
                Group = "map",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Description = "兼容入口：请优先使用 game_control domain=ui uiDomain=feedback action=marker markerAction=list。列出当前由 MCP 创建的地图标记",
                Handler = args =>
                {
                    PruneDeadMarkers();
                    var markers = Markers.Values
                        .OrderBy(marker => marker.Id)
                        .Select(marker => marker.ToDictionary())
                        .ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["count"] = markers.Count,
                        ["markers"] = markers
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ClearMapMarker()
        {
            return new McpTool
            {
                Name = "map_marker_clear",
                Group = "map",
                Mode = "execute",
                Risk = "low",
                Hidden = true,
                Description = "兼容入口：请优先使用 game_control domain=ui uiDomain=feedback action=marker markerAction=clear。清除指定或全部 MCP 地图标记",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "string", Description = "标记 ID；留空且 all=true 时清除全部", Required = false },
                    ["all"] = new McpToolParameter { Type = "boolean", Description = "是否清除全部标记，默认 false", Required = false }
                },
                Handler = args =>
                {
                    bool all = ToolUtil.GetBool(args, "all", false);
                    string id = args["id"]?.ToString();
                    int removed = 0;

                    if (all)
                    {
                        foreach (var markerId in Markers.Keys.ToList())
                        {
                            if (RemoveMarkerById(markerId))
                                removed++;
                        }
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(id))
                            return CallToolResult.Error("id is required unless all=true");
                        if (RemoveMarkerById(id.Trim()))
                            removed++;
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["removed"] = removed,
                        ["remaining"] = Markers.Count
                    }, McpJsonUtil.Settings));
                }
            };
        }

        private static string TooltipText(List<Notification> notifications, object data)
        {
            return data?.ToString() ?? "";
        }

        private static void FocusNotificationTarget(object data)
        {
            var target = data as CellTarget;
            if (target != null)
                FocusCell(target);
        }

        private static void FocusCell(CellTarget target)
        {
            if (target.WorldId >= 0)
                GameUtil.FocusCameraOnWorld(target.WorldId, target.Position);
            else
                GameUtil.FocusCamera(target.Position);
        }

        private static CellTarget ResolveTarget(JObject args)
        {
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            if (!x.HasValue || !y.HasValue)
                return null;
            var target = ResolveCell(x.Value, y.Value);
            return target.Error == null ? target : null;
        }

        private static CellTarget ResolveRequiredCell(JObject args)
        {
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            if (!x.HasValue || !y.HasValue)
                return CellTarget.Invalid("x and y are required");
            return ResolveCell(x.Value, y.Value);
        }

        private static CellTarget ResolveCell(int x, int y)
        {
            int cell = Grid.XYToCell(x, y);
            if (!Grid.IsValidCell(cell) || !Grid.IsWorldValidCell(cell))
                return CellTarget.Invalid("Invalid cell");

            return new CellTarget
            {
                X = x,
                Y = y,
                Cell = cell,
                WorldId = Grid.WorldIdx[cell],
                Visible = Grid.IsVisible(cell),
                Position = Grid.CellToPosCBC(cell, Grid.SceneLayer.Move)
            };
        }

        private static GameObject CreateTargetObject(string name, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.SetPosition(position);
            return go;
        }

        private static NotificationType ParseNotificationType(string raw)
        {
            switch ((raw ?? "neutral").Trim().ToLowerInvariant().Replace("-", "_"))
            {
                case "good":
                    return NotificationType.Good;
                case "bad_minor":
                case "warning":
                    return NotificationType.BadMinor;
                case "bad":
                case "error":
                    return NotificationType.Bad;
                case "tutorial":
                    return NotificationType.Tutorial;
                case "message":
                case "messages":
                    return NotificationType.Messages;
                case "important":
                case "message_important":
                    return NotificationType.MessageImportant;
                case "event":
                    return NotificationType.Event;
                default:
                    return NotificationType.Neutral;
            }
        }

        private static Sprite ResolvePopFxIcon(string raw)
        {
            var manager = PopFXManager.Instance;
            if (manager == null)
                return null;

            switch (NormalizeStyle(raw))
            {
                case "good":
                    return manager.sprite_Plus;
                case "bad":
                    return manager.sprite_Negative;
                case "resource":
                    return manager.sprite_Resource;
                case "building":
                    return manager.sprite_Building;
                case "research":
                    return manager.sprite_Research;
                default:
                    return NotificationScreen.Instance != null ? NotificationScreen.Instance.GetNotificationIcon(NotificationType.Neutral) : manager.sprite_Plus;
            }
        }

        private static string NormalizeStyle(string raw)
        {
            string style = (raw ?? "info").Trim().ToLowerInvariant().Replace("-", "_");
            switch (style)
            {
                case "good":
                case "bad":
                case "resource":
                case "building":
                case "research":
                    return style;
                default:
                    return "info";
            }
        }

        private static string NormalizeText(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";
            value = value.Trim();
            if (value.Length > maxLength)
                value = value.Substring(0, maxLength);
            return value;
        }

        private static bool RemoveMarkerById(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return false;

            if (!Markers.TryGetValue(id, out var marker) || marker == null)
            {
                Markers.Remove(id);
                return false;
            }

            Markers.Remove(id);
            marker.DestroyMarker();
            return true;
        }

        private static void RemoveMarkerByIdCallback(string id)
        {
            RemoveMarkerById(id);
        }

        private static void PruneDeadMarkers()
        {
            foreach (var id in Markers.Keys.ToList())
            {
                var marker = Markers[id];
                if (marker == null || marker.IsDestroyed)
                    Markers.Remove(id);
            }
        }

        private class CellTarget
        {
            public int X;
            public int Y;
            public int Cell;
            public int WorldId;
            public bool Visible;
            public Vector3 Position;
            public string Error;

            public static CellTarget Invalid(string error)
            {
                return new CellTarget { Error = error };
            }

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["x"] = X,
                    ["y"] = Y,
                    ["cell"] = Cell,
                    ["worldId"] = WorldId,
                    ["visible"] = Visible,
                    ["position"] = new { x = Math.Round(Position.x, 2), y = Math.Round(Position.y, 2), z = Math.Round(Position.z, 2) }
                };
            }
        }

        private class MapMarkerHandle : MonoBehaviour
        {
            private SelectMarker marker;
            private GameObject targetObject;
            private float expiresAt;
            private Action<string> removeCallback;
            private CellTarget target;

            public string Id { get; private set; }
            public string Label { get; private set; }
            public bool IsDestroyed { get; private set; }
            public float RemainingSeconds => Mathf.Max(0f, expiresAt - Time.unscaledTime);

            public void Initialize(string id, string label, CellTarget cellTarget, SelectMarker selectMarker, GameObject target, float duration, Action<string> onRemove)
            {
                Id = id;
                Label = label;
                this.target = cellTarget;
                marker = selectMarker;
                targetObject = target;
                expiresAt = Time.unscaledTime + duration;
                removeCallback = onRemove;
            }

            private void Update()
            {
                if (!IsDestroyed && Time.unscaledTime >= expiresAt)
                    removeCallback?.Invoke(Id);
            }

            public void DestroyMarker()
            {
                if (IsDestroyed)
                    return;

                IsDestroyed = true;
                if (marker != null)
                    UnityEngine.Object.Destroy(marker.gameObject);
                if (targetObject != null)
                    UnityEngine.Object.Destroy(targetObject);
            }

            private void OnDestroy()
            {
                DestroyMarker();
            }

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["id"] = Id,
                    ["label"] = Label,
                    ["remainingSeconds"] = Math.Round(RemainingSeconds, 1),
                    ["target"] = target?.ToDictionary()
                };
            }
        }
    }
}
