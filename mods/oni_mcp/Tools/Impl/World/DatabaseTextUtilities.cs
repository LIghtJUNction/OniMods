using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class DatabaseTools
    {
        private static string CleanQuery(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
        }

        private static string CleanText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            string decoded = WebUtility.HtmlDecode(value);
            decoded = RichTextTagRegex.Replace(decoded, "");
            decoded = WhitespaceRegex.Replace(decoded, " ");
            return decoded.Trim();
        }

        private static string LimitText(string value, int maxChars)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
                return value;
            return value.Substring(0, maxChars) + "...";
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            return null;
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value)
                && !string.IsNullOrEmpty(query)
                && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool EqualsIgnoreCase(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
}
