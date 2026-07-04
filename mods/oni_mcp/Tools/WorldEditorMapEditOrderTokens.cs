using System;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static bool IsOrderAction(string action)
        {
            return action == "dig" || action == "deconstruct" || action == "mop"
                || action == "attack" || action == "harvest" || action == "cancel"
                || action == "sweep" || action == "disinfect";
        }

        private static string ParseOrderAction(string token)
        {
            string name = ExtractBuildTokenName(token);
            switch (name)
            {
                case "挖":
                case "挖掘": return "dig";
                case "拆":
                case "拆除": return "deconstruct";
                case "擦":
                case "擦拭": return "mop";
                case "杀":
                case "攻击": return "attack";
                case "收":
                case "收获": return "harvest";
                case "消":
                case "取消": return "cancel";
                case "毒":
                case "消毒":
                case "杀菌":
                case "灭菌": return "disinfect";
                case "扫":
                case "清扫": return "sweep";
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
