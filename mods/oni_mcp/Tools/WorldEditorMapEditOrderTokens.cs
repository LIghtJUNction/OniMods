using System;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static bool IsOrderAction(string action)
        {
            return action == "dig" || action == "deconstruct" || action == "mop"
                || action == "attack" || action == "harvest" || action == "cancel"
                || action == "sweep" || action == "disinfect" || action == "capture";
        }

        private static string ParseOrderAction(string token)
        {
            string name = ExtractBuildTokenName(token);
            switch (name)
            {
                case "挖":
                case "挖掘":
                case "开挖":
                case "dig": return "dig";
                case "拆":
                case "拆除":
                case "拆建筑":
                case "拆除建筑":
                case "deconstruct": return "deconstruct";
                case "擦":
                case "擦拭":
                case "擦水":
                case "拖":
                case "拖地":
                case "mop":
                case "wipe": return "mop";
                case "杀":
                case "攻":
                case "攻击":
                case "击杀":
                case "attack": return "attack";
                case "收":
                case "收获":
                case "收割":
                case "采收":
                case "harvest": return "harvest";
                case "消":
                case "取消":
                case "取消任务":
                case "取消命令":
                case "cancel": return "cancel";
                case "毒":
                case "消毒":
                case "杀菌":
                case "灭菌":
                case "disinfect":
                case "sanitize": return "disinfect";
                case "扫":
                case "清":
                case "清扫":
                case "清理":
                case "打扫":
                case "收拾":
                case "捡":
                case "捡起":
                case "拾取":
                case "搬运":
                case "扫地":
                case "sweep":
                case "pickup":
                case "pick_up":
                case "clear": return "sweep";
                case "捕":
                case "捕捉":
                case "抓捕":
                case "capture":
                case "wrangle": return "capture";
                default: return string.Empty;
            }
        }

        private static bool IsEmptyMapSymbol(char symbol)
        {
            return symbol == '.' || symbol == '?' || symbol == '空' || symbol == '氧'
                || symbol == '碳' || symbol == '污' || symbol == '氢' || symbol == '氯'
                || symbol == '瓦' || symbol == '汽' || symbol == '水' || symbol == '盐'
                || symbol == '浆' || symbol == '油' || symbol == '炼' || symbol == '咸'
                || symbol == '深' || symbol == '潮' || symbol == '中' || symbol == '小';
        }

        private static bool IsEmptyMapToken(string token)
        {
            token = (token ?? string.Empty).Trim();
            if (token.Length == 0)
                return true;
            if (token == "?")
                return true;
            if (token.IndexOf('@') >= 0)
                return false;
            if (token.IndexOf(':') >= 0)
                return false;
            return IsEmptyMapSymbol(token[0]);
        }
    }
}
