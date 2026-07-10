using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Server;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Tools
{
    internal static partial class AgentPointerRegistry
    {
        private static void RecyclePointersLocked(System.DateTime now)
        {
            if (LastPrunedAt != default(System.DateTime) && now - LastPrunedAt < PointerPruneInterval)
                return;

            LastPrunedAt = now;
            MigrateLegacySessionPointersLocked();
            RemoveStaleUnpositionedPointersLocked(now);
            EnforcePointerLimitLocked();
        }

        private static void MigrateLegacySessionPointersLocked()
        {
            var keys = new List<string>(SessionPointers.Keys);
            foreach (string key in keys)
            {
                AgentPointerState pointer;
                if (!SessionPointers.TryGetValue(key, out pointer))
                    continue;
                string canonicalKey = CanonicalAgentKey(pointer.AgentId);
                NormalizeAgentPointer(pointer);
                if (string.Equals(key, canonicalKey, StringComparison.Ordinal))
                    continue;

                AgentPointerState existing;
                if (!SessionPointers.TryGetValue(canonicalKey, out existing))
                {
                    SessionPointers.Remove(key);
                    SessionPointers[canonicalKey] = pointer;
                    MoveJumpPoints(key, canonicalKey);
                    continue;
                }

                NormalizeAgentPointer(existing);
                if (pointer.UpdatedAt > existing.UpdatedAt)
                    SessionPointers[canonicalKey] = pointer;
                MergeJumpPoints(key, canonicalKey);
                SessionPointers.Remove(key);
            }
        }

        private static void NormalizeAgentPointer(AgentPointerState pointer)
        {
            if (pointer == null)
                return;
            pointer.AgentId = NormalizeAgentId(pointer.AgentId);
            pointer.Scope = AgentScope;
            if (string.IsNullOrWhiteSpace(pointer.Label) || IsSessionPrefixedLabel(pointer.Label, pointer.AgentId))
                pointer.Label = pointer.AgentId;
        }

        private static bool IsSessionPrefixedLabel(string label, string agentId)
        {
            if (string.IsNullOrWhiteSpace(label))
                return false;
            string normalizedAgentId = NormalizeAgentId(agentId);
            return label.EndsWith(":" + normalizedAgentId, StringComparison.Ordinal);
        }

        private static void MoveJumpPoints(string fromKey, string toKey)
        {
            Dictionary<string, AgentPointerJumpPoint> points;
            if (!JumpPoints.TryGetValue(fromKey, out points))
                return;
            JumpPoints.Remove(fromKey);
            JumpPoints[toKey] = points;
        }

        private static void MergeJumpPoints(string fromKey, string toKey)
        {
            Dictionary<string, AgentPointerJumpPoint> source;
            if (!JumpPoints.TryGetValue(fromKey, out source))
                return;

            Dictionary<string, AgentPointerJumpPoint> target;
            if (!JumpPoints.TryGetValue(toKey, out target))
            {
                JumpPoints[toKey] = source;
                JumpPoints.Remove(fromKey);
                return;
            }

            foreach (var item in source)
            {
                AgentPointerJumpPoint existing;
                if (!target.TryGetValue(item.Key, out existing) || item.Value.UpdatedAt > existing.UpdatedAt)
                    target[item.Key] = item.Value;
            }
            JumpPoints.Remove(fromKey);
        }

        private static void RemoveStaleUnpositionedPointersLocked(System.DateTime now)
        {
            var keysToRemove = new List<string>();
            foreach (var item in SessionPointers)
            {
                var pointer = item.Value;
                if (pointer == null || pointer.IsDragging || Grid.IsValidCell(pointer.Cell))
                    continue;
                if (pointer.UpdatedAt != default(System.DateTime) && now - pointer.UpdatedAt > UnpositionedPointerTtl)
                    keysToRemove.Add(item.Key);
            }
            RemovePointerKeys(keysToRemove);
        }

        private static void EnforcePointerLimitLocked()
        {
            int overflow = SessionPointers.Count - MaxAgentPointers;
            if (overflow <= 0)
                return;

            var candidates = new List<KeyValuePair<string, AgentPointerState>>();
            foreach (var item in SessionPointers)
            {
                if (item.Value == null || item.Value.IsDragging)
                    continue;
                candidates.Add(item);
            }

            candidates.Sort((left, right) => left.Value.UpdatedAt.CompareTo(right.Value.UpdatedAt));
            var keysToRemove = new List<string>();
            for (int i = 0; i < candidates.Count && keysToRemove.Count < overflow; i++)
                keysToRemove.Add(candidates[i].Key);
            RemovePointerKeys(keysToRemove);
        }

        private static void RemovePointerKeys(List<string> keys)
        {
            foreach (string key in keys)
            {
                SessionPointers.Remove(key);
                JumpPoints.Remove(key);
            }
        }

        private static string ClientLabelPrefix(string sessionId)
        {
            string normalizedSessionId = NormalizeSessionId(sessionId);
            if (normalizedSessionId == DefaultSessionId)
                return "";

            string clientName = null;
            try
            {
                clientName = McpHttpServer.Instance?.GetSessionClientInfo(normalizedSessionId)?.Name;
            }
            catch
            {
                clientName = null;
            }

            string prefix = SanitizeLabelPart(clientName);
            string suffix = ShortSessionSuffix(normalizedSessionId);
            if (string.IsNullOrWhiteSpace(prefix))
                return string.IsNullOrWhiteSpace(suffix) ? "" : "s" + suffix;
            return string.IsNullOrWhiteSpace(suffix) ? prefix : prefix + "-" + suffix;
        }

        private static string SanitizeLabelPart(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            value = value.Trim().ToLowerInvariant();
            var chars = new List<char>();
            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c))
                    chars.Add(c);
                else if ((c == '-' || c == '_' || c == '.') && chars.Count > 0 && chars[chars.Count - 1] != '-')
                    chars.Add('-');
                else if (char.IsWhiteSpace(c) && chars.Count > 0 && chars[chars.Count - 1] != '-')
                    chars.Add('-');
                if (chars.Count >= 16)
                    break;
            }

            while (chars.Count > 0 && chars[chars.Count - 1] == '-')
                chars.RemoveAt(chars.Count - 1);
            return new string(chars.ToArray());
        }

        private static string ShortSessionSuffix(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                return "";
            sessionId = sessionId.Trim();
            return sessionId.Length <= 6 ? sessionId : sessionId.Substring(0, 6);
        }

        internal static string NormalizeJumpCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return "p1";
            code = code.Trim().ToLowerInvariant().Replace("+", "");
            if (code == "p")
                return "p1";
            if (code.StartsWith("p", StringComparison.Ordinal))
                return code;
            return "p" + code;
        }

        private static string NormalizeTool(string tool)
        {
            tool = (tool ?? "inspect").Trim().ToLowerInvariant();
            if (tool == "build" || tool == "dig" || tool == "cancel" || tool == "sweep" || tool == "mop" || tool == "disinfect" || tool == "harvest" || tool == "deconstruct")
                return tool;
            return "inspect";
        }

        private static string ToolLabel(string tool, string prefabId)
        {
            return tool == "build" && !string.IsNullOrWhiteSpace(prefabId) ? "Build " + prefabId.Trim() : char.ToUpper(tool[0]) + tool.Substring(1);
        }

        private static string ToolIcon(string tool, string prefabId)
        {
            return tool == "build" && !string.IsNullOrWhiteSpace(prefabId) ? prefabId.Trim() : tool;
        }

        private static Vector3 CellToWorld(int cell)
        {
            if (!Grid.IsValidCell(cell))
                return Vector3.zero;
            Grid.CellToXY(cell, out int x, out int y);
            return new Vector3(x + 0.5f, y + 0.5f, -100f);
        }

        private static Vector2 WorldToScreen(Vector3 world)
        {
            var camera = Camera.main;
            if (camera == null)
                return Vector2.zero;
            var screen = camera.WorldToScreenPoint(world);
            return new Vector2(screen.x, Screen.height - screen.y);
        }
    }
}
