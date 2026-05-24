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

        public static McpTool CreateNotification()
        {
            return new McpTool
            {
                Name = "game_notification_create",
                Group = "ui",
                Mode = "execute",
                Risk = "low",
                Description = "创建游戏原生通知，可选点击后聚焦地图格子",
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
                Description = "在地图格子上创建游戏原生浮动文字提示",
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
                Description = "在地图格子上创建游戏原生选择标记，可选浮动标签",
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

        public static McpTool ListMapMarkers()
        {
            return new McpTool
            {
                Name = "map_marker_list",
                Group = "map",
                Mode = "read",
                Risk = "none",
                Description = "列出当前由 MCP 创建的地图标记",
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
                Description = "清除指定或全部 MCP 地图标记",
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
