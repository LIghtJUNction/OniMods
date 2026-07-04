using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        static bool IsSemanticOperationHead(string head)
        {
            head = NormalizeSemanticHead(head);
            return IsMoveHead(head) || IsCaptureHead(head) || TrySemanticOrderAction(head, out _, out _);
        }

        static bool TryParseSemanticOperationLine(
            string relative,
            string line,
            out string toolName,
            out JObject arguments,
            out string error)
        {
            toolName = null;
            arguments = null;
            error = null;

            string head = NormalizeSemanticHead(FirstWord(line));
            if (!IsSemanticOperationHead(head))
                return false;

            if (IsCaptureHead(head))
                return TryParseCaptureOperation(line, out toolName, out arguments, out error);
            if (TrySemanticOrderAction(head, out string action, out bool designation))
                return TryParseOrderOperation(line, action, designation, out toolName, out arguments, out error);
            return TryParseMoveOperation(relative, line, out toolName, out arguments, out error);
        }

        static bool TryParseMoveOperation(
            string relative,
            string line,
            out string toolName,
            out JObject arguments,
            out string error)
        {
            toolName = null;
            arguments = null;
            error = null;

            var kv = ParseCommandKeyValues(line);
            SplitSemanticArrow(line, out string subject, out string target);
            subject = CleanSemanticToken(FirstNonEmpty(kv, "subject", "who", "name", "id") ?? subject);
            target = CleanSemanticToken(FirstNonEmpty(kv, "target", "to", "query", "search") ?? target);

            if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(target))
            {
                error = "move syntax: 移 人@Dig -> 打印舱 confirm=true";
                return false;
            }
            if (StartsWithAny(subject, "小动物@", "动物@", "critter@", "creature@"))
            {
                error = "Critter movement is not a direct move command. Use 捕 小动物@(x,y):priority to wrangle/capture, then configure ranch/drop-off separately.";
                return false;
            }
            if (StartsWithAny(subject, "物品@", "物@", "item@", "resource@"))
            {
                error = "Item movement is a storage/logistics workflow, not direct move. Use sweep/storage/manual-delivery operation files with a searched area or target building.";
                return false;
            }
            if (!LooksLikeDupeSubject(relative, subject))
            {
                error = "move currently supports duplicants only: 移 人@Name -> target. Critters use 捕; items use sweep/storage.";
                return false;
            }

            toolName = "dupes_control";
            arguments = new JObject
            {
                ["domain"] = "command",
                ["action"] = "move_to",
                ["query"] = target
            };
            ApplySubject(arguments, subject);
            CopyIfPresent(kv, arguments, "confirm", "nearX", "nearY", "worldId");
            return true;
        }

        static bool TryParseCaptureOperation(
            string line,
            out string toolName,
            out JObject arguments,
            out string error)
        {
            return TryParseOrderOperation(line, "capture", true, out toolName, out arguments, out error);
        }

        static bool TryParseOrderOperation(
            string line,
            string action,
            bool designation,
            out string toolName,
            out JObject arguments,
            out string error)
        {
            toolName = "orders_control";
            arguments = null;
            error = null;

            var kv = ParseCommandKeyValues(line);
            SplitSemanticArrow(line, out string subject, out _);
            subject = CleanSemanticToken(FirstNonEmpty(kv, "target", "subject", "id", "areaId") ?? subject);

            arguments = new JObject
            {
                ["domain"] = designation ? "designation" : "area",
                ["action"] = action
            };

            CopyIfPresent(kv, arguments,
                "confirm", "dryRun", "detail", "limit", "worldId", "mode", "force",
                "topPriority", "priority", "areaId", "id", "type", "previewToken", "task");

            int? priority = ParsePriority(subject);
            if (priority.HasValue)
                arguments["priority"] = priority.Value;

            if (designation)
                return ApplyDesignationTarget(kv, subject, action, arguments, out error);

            return ApplyAreaTarget(kv, subject, arguments, out error);
        }

        static bool ApplyAreaTarget(JObject kv, string subject, JObject arguments, out string error)
        {
            error = null;
            if (!string.IsNullOrWhiteSpace(arguments["areaId"]?.ToString()))
                return true;

            if (TryGetSemanticRect(kv, out int x1, out int y1, out int x2, out int y2))
            {
                arguments["x1"] = x1;
                arguments["y1"] = y1;
                arguments["x2"] = x2;
                arguments["y2"] = y2;
                return true;
            }

            if (TryExtractSemanticCoord(subject, out int x, out int y))
            {
                arguments["x1"] = x;
                arguments["y1"] = y;
                arguments["x2"] = x;
                arguments["y2"] = y;
                return true;
            }

            error = "order area syntax: 挖 土@(83,146):7, 擦 @(90,140), 收 areaId=..., or x1/y1/x2/y2.";
            return false;
        }

        static bool ApplyDesignationTarget(
            JObject kv,
            string subject,
            string action,
            JObject arguments,
            out string error)
        {
            error = null;
            if (arguments["id"] != null)
                return true;

            if (TryGetSemanticInt(kv, "x", out int x) && TryGetSemanticInt(kv, "y", out int y))
            {
                arguments["x"] = x;
                arguments["y"] = y;
                return true;
            }

            if (TryExtractSemanticCoord(subject, out x, out y))
            {
                if (action == "capture")
                {
                    arguments["x1"] = x;
                    arguments["y1"] = y;
                    arguments["x2"] = x;
                    arguments["y2"] = y;
                }
                else
                {
                    arguments["x"] = x;
                    arguments["y"] = y;
                }
                return true;
            }

            error = "designation syntax: 拆 建筑@(92,145):7, 杀 小动物@(100,137), 捕 小动物@(101,130):7, or id=<InstanceID>.";
            return false;
        }

        static bool TrySemanticOrderAction(string head, out string action, out bool designation)
        {
            action = null;
            designation = false;
            head = NormalizeSemanticHead(head);

            switch (head)
            {
            case "挖":
            case "挖掘":
            case "开挖":
            case "dig":
                action = "dig";
                return true;
            case "擦":
            case "擦拭":
            case "擦水":
            case "拖":
            case "拖地":
            case "mop":
            case "wipe":
                action = "mop";
                return true;
            case "收":
            case "收获":
            case "收割":
            case "采收":
            case "harvest":
                action = "harvest";
                return true;
            case "消":
            case "取消":
            case "取消任务":
            case "取消命令":
            case "cancel":
                action = "cancel";
                return true;
            case "毒":
            case "消毒":
            case "杀菌":
            case "灭菌":
            case "disinfect":
            case "sanitize":
                action = "disinfect";
                return true;
            case "扫":
            case "清":
            case "清扫":
            case "清理":
            case "打扫":
            case "捡":
            case "捡起":
            case "拾取":
            case "搬运":
            case "收拾":
            case "扫地":
            case "sweep":
            case "pickup":
            case "pick_up":
            case "clear":
                action = "sweep";
                return true;
            case "拆":
            case "拆除":
            case "拆建筑":
            case "拆除建筑":
            case "deconstruct":
                action = "deconstruct";
                designation = true;
                return true;
            case "杀":
            case "攻":
            case "攻击":
            case "击杀":
            case "attack":
                action = "attack";
                designation = true;
                return true;
            }

            return false;
        }

        static bool IsMoveHead(string head)
        {
            return head == "move"
                || head == "mv"
                || head == "go"
                || head == "walk"
                || head == "移"
                || head == "移动"
                || head == "移动到"
                || head == "走"
                || head == "走到"
                || head == "去"
                || head == "搬";
        }

        static bool IsCaptureHead(string head)
        {
            return head == "capture"
                || head == "cap"
                || head == "wrangle"
                || head == "catch"
                || head == "trap"
                || head == "捕"
                || head == "捕捉"
                || head == "抓捕"
                || head == "捉"
                || head == "抓"
                || head == "围捕"
                || head == "圈养";
        }

        static string NormalizeSemanticHead(string head)
        {
            return (head ?? string.Empty).Trim().ToLowerInvariant();
        }

        static void SplitSemanticArrow(string line, out string subject, out string target)
        {
            var tokens = TokenizeCommand(line).ToList();
            int arrow = tokens.FindIndex(t => t == "->" || t == "=>");
            subject = JoinSemanticTokens(tokens.Skip(1).Take(arrow < 0 ? tokens.Count : arrow - 1));
            target = arrow < 0 ? string.Empty : JoinSemanticTokens(tokens.Skip(arrow + 1));
        }

        static string JoinSemanticTokens(IEnumerable<string> tokens)
        {
            return string.Join(" ", tokens.Where(t => !t.Contains("="))).Trim();
        }

        static string CleanSemanticToken(string value)
        {
            return (value ?? string.Empty).Trim().Trim('"');
        }

        static string FirstNonEmpty(JObject obj, params string[] keys)
        {
            foreach (string key in keys)
            {
                string value = obj[key]?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
            return null;
        }

        static bool StartsWithAny(string value, params string[] prefixes)
        {
            value = value ?? string.Empty;
            return prefixes.Any(p => value.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        }

        static bool LooksLikeDupeSubject(string relative, string subject)
        {
            return relative == "ops/dupes.md" || StartsWithAny(subject, "人@", "复制人@", "dupe@", "duplicant@");
        }

        static void ApplySubject(JObject args, string subject)
        {
            int at = subject.IndexOf('@');
            string value = at >= 0 ? subject.Substring(at + 1) : subject;
            value = CleanSemanticToken(value);
            if (int.TryParse(value, out int id))
                args["id"] = id;
            else
                args["name"] = value;
        }

        static bool TryExtractSemanticCoord(string token, out int x, out int y)
        {
            x = 0;
            y = 0;
            token = token ?? string.Empty;
            int at = token.IndexOf("@(", StringComparison.Ordinal);
            int open = at >= 0 ? at + 1 : token.IndexOf('(');
            if (open < 0)
                return false;
            int comma = token.IndexOf(',', open + 1);
            int end = token.IndexOf(')', comma + 1);
            return comma > open && end > comma
                && int.TryParse(token.Substring(open + 1, comma - open - 1), out x)
                && int.TryParse(token.Substring(comma + 1, end - comma - 1), out y);
        }

        static bool TryGetSemanticRect(JObject kv, out int x1, out int y1, out int x2, out int y2)
        {
            if (TryGetSemanticInt(kv, "x1", out x1)
                && TryGetSemanticInt(kv, "y1", out y1)
                && TryGetSemanticInt(kv, "x2", out x2)
                && TryGetSemanticInt(kv, "y2", out y2))
                return true;

            if (TryGetSemanticInt(kv, "x", out x1) && TryGetSemanticInt(kv, "y", out y1))
            {
                x2 = x1;
                y2 = y1;
                return true;
            }

            x1 = y1 = x2 = y2 = 0;
            return false;
        }

        static bool TryGetSemanticInt(JObject obj, string key, out int value)
        {
            value = 0;
            JToken token = obj[key];
            if (token == null)
                return false;
            return int.TryParse(token.ToString(), out value);
        }

        static void CopyIfPresent(JObject source, JObject target, params string[] keys)
        {
            foreach (string key in keys)
            {
                if (source[key] != null)
                    target[key] = source[key].DeepClone();
            }
        }
    }
}
