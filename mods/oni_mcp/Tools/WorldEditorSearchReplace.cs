using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Net;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static bool TryParseSearchReplace(string block, out string search, out string replace)
        {
            search = string.Empty;
            replace = string.Empty;
            if (string.IsNullOrWhiteSpace(block))
                return false;
            const string start = "<<<<<<< SEARCH";
            const string mid = "=======";
            const string end = ">>>>>>> REPLACE";
            int s = block.IndexOf(start, StringComparison.Ordinal);
            int m = block.IndexOf(mid, StringComparison.Ordinal);
            int e = block.IndexOf(end, StringComparison.Ordinal);
            if (s < 0 || m < 0 || e < 0 || !(s < m && m < e))
                return false;
            search = block.Substring(s + start.Length, m - (s + start.Length)).Trim();
            replace = block.Substring(m + mid.Length, e - (m + mid.Length)).Trim();
            return !string.IsNullOrWhiteSpace(replace);
        }

        private static bool TryParseSearchReplaceBlocks(string block, out List<KeyValuePair<string, string>> edits)
        {
            edits = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrWhiteSpace(block))
                return false;

            const string start = "<<<<<<< SEARCH";
            const string mid = "=======";
            const string end = ">>>>>>> REPLACE";
            int offset = 0;

            while (offset < block.Length)
            {
                int s = block.IndexOf(start, offset, StringComparison.Ordinal);
                if (s < 0)
                    break;

                int m = block.IndexOf(mid, s + start.Length, StringComparison.Ordinal);
                int e = block.IndexOf(end, m < 0 ? s + start.Length : m + mid.Length, StringComparison.Ordinal);
                if (m < 0 || e < 0 || !(s < m && m < e))
                    return false;

                string search = block.Substring(s + start.Length, m - (s + start.Length)).Trim();
                string replace = block.Substring(m + mid.Length, e - (m + mid.Length)).Trim();
                if (string.IsNullOrWhiteSpace(replace))
                    return false;

                edits.Add(new KeyValuePair<string, string>(search, replace));
                offset = e + end.Length;
            }

            return edits.Count > 0;
        }

        private static bool ValidateVirtualFileSearch(JObject request, string path, string relative, string search, out string error)
        {
            error = null;
            if (IsBlankOrTemplateSearch(search))
                return true;

            string current;
            string readError;
            if (!TryReadVirtualFileText(request, path, out current, out readError))
            {
                error = "Cannot validate SEARCH against current virtual file: " + readError;
                return false;
            }

            if (ContainsSearchText(current, search) || (IsEditableMapMarkdown(relative) && MapSearchMatchesCurrent(current, search)))
                return true;

            string hint = IsEditableMapMarkdown(relative)
                ? "Map SEARCH supports ?/* token wildcards and /regex/ or ~regex token patterns."
                : "For command-style virtual files, use an empty SEARCH block or copy the current file snippet exactly.";
            error =
                "SEARCH did not match current virtual file snapshot. The game state may have changed; read the file again and regenerate the edit block. " + hint + "\n\n" +
                "Path: " + path + "\n" +
                "Virtual file: " + relative + "\n\n" +
                "Closest/current excerpt:\n```text\n" + BestExcerpt(current, search) + "\n```";
            return false;
        }


        private static bool IsBlankOrTemplateSearch(string search)
        {
            string value = (search ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value))
                return true;
            string lower = value.ToLowerInvariant();
            return lower.Contains("observed or empty planning text")
                || lower.Contains("observed empty planning text")
                || lower.Contains("empty planning text")
                || value.StartsWith("# observed", StringComparison.OrdinalIgnoreCase);
        }


        private static bool TryReadVirtualFileText(string path, out string text, out string error)
        {
            return TryReadVirtualFileText(null, path, out text, out error);
        }

        private static bool TryReadVirtualFileText(JObject request, string path, out string text, out string error)
        {
            text = string.Empty;
            error = null;
            try
            {
                var readArgs = request == null ? new JObject() : (JObject)request.DeepClone();
                readArgs.Remove("content");
                readArgs.Remove("editCells");
                readArgs.Remove("editLines");
                readArgs["command"] = "read";
                readArgs["path"] = path;
                var result = Read(readArgs);
                if (result == null)
                {
                    error = "read returned no result";
                    return false;
                }
                if (result.IsError)
                {
                    error = result.Content?.FirstOrDefault()?.Text ?? "read failed";
                    return false;
                }
                text = result.Content?.FirstOrDefault()?.Text ?? string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }


        private static bool ContainsSearchText(string current, string search)
        {
            current = NormalizeSearchText(current);
            search = NormalizeSearchText(search);
            if (string.IsNullOrWhiteSpace(search))
                return true;
            if (current.IndexOf(search, StringComparison.Ordinal) >= 0)
                return true;
            return CollapseWhitespace(current).IndexOf(CollapseWhitespace(search), StringComparison.Ordinal) >= 0;
        }


        private static string NormalizeSearchText(string text)
        {
            return (text ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n").Trim();
        }


        private static string CollapseWhitespace(string text)
        {
            var sb = new StringBuilder();
            bool lastWasSpace = false;
            foreach (char c in text ?? string.Empty)
            {
                if (char.IsWhiteSpace(c))
                {
                    if (!lastWasSpace)
                    {
                        sb.Append(' ');
                        lastWasSpace = true;
                    }
                }
                else
                {
                    sb.Append(c);
                    lastWasSpace = false;
                }
            }
            return sb.ToString().Trim();
        }


        private static string BestExcerpt(string current, string search)
        {
            current = NormalizeSearchText(current);
            if (string.IsNullOrEmpty(current))
                return "(current virtual file is empty)";

            string needle = FirstMeaningfulLine(search);
            int index = !string.IsNullOrEmpty(needle)
                ? current.IndexOf(needle, StringComparison.OrdinalIgnoreCase)
                : -1;
            if (index < 0)
                index = 0;

            int start = Math.Max(0, index - 400);
            int length = Math.Min(current.Length - start, 1000);
            string excerpt = current.Substring(start, length);
            if (start > 0)
                excerpt = "...\n" + excerpt;
            if (start + length < current.Length)
                excerpt += "\n...";
            return excerpt;
        }


        private static string FirstMeaningfulLine(string text)
        {
            foreach (var rawLine in NormalizeSearchText(text).Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    continue;
                return line;
            }
            return string.Empty;
        }


        private static bool LooksLikeConnection(string text)
        {
            string lower = (text ?? string.Empty).ToLowerInvariant();
            return lower.Contains("connect") || lower.Contains("wire") || lower.Contains("pipe")
                || lower.Contains("conduit") || text.Contains("连接") || text.Contains("电线") || text.Contains("管");
        }

    }
}
