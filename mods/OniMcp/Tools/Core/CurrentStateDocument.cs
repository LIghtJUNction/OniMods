using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Tools
{
    // One public virtual file supplies the full document behind bounded index
    // previews. The reader is injected so routing and formatting run in the host.
    internal static class CurrentStateDocument
    {
        internal const string FileName = "current.oni";
        internal const string RelativePath = "colony/" + FileName;
        internal const string Path = "/active/" + RelativePath;
        internal const string Description = "Complete current colony state and public next-read guidance.";
        internal const string ReadCall = "world_editor command=read path=" + Path;

        internal static JObject NextRead(JObject readOptions = null)
        {
            var arguments = new JObject
            {
                ["command"] = "read",
                ["path"] = Path,
                ["task"] = "Read the complete current colony state"
            };
            if (readOptions != null)
            {
                var options = (JObject)readOptions.DeepClone();
                foreach (string key in new[] { "command", "path", "domain", "action",
                    "task", "includeState", "format", "detail" })
                    options.Remove(key);
                if (options.HasValues)
                    arguments["payload"] = options;
            }
            return new JObject { ["tool"] = "world_editor", ["arguments"] = arguments };
        }

        internal static bool TryRead(string path, JObject arguments,
            Func<JObject, CallToolResult> reader, out CallToolResult result)
        {
            result = null;
            if (!string.Equals(path, RelativePath, StringComparison.Ordinal)
                && !string.Equals(path, Path, StringComparison.Ordinal))
                return false;
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            var forwarded = arguments == null ? new JObject() : (JObject)arguments.DeepClone();
            forwarded.Remove("command");
            forwarded.Remove("path");
            forwarded["domain"] = "state";
            forwarded["action"] = "current";
            result = reader(forwarded);
            return true;
        }

        internal static string Preview(string text, int maximumCharacters, JObject readOptions = null)
        {
            if (maximumCharacters < 1024)
                throw new ArgumentOutOfRangeException(nameof(maximumCharacters));
            var state = JObject.Parse(text);
            if (text.Length <= maximumCharacters) return text;

            var omitted = new JArray(state.Properties().Select(property => property.Name));
            var preview = new JObject
            {
                ["kind"] = "current_state_preview",
                ["truncated"] = true,
                ["nextRead"] = NextRead(readOptions),
                ["omittedSections"] = omitted
            };
            // Keep complete sections, never a cut JSON string. Optional scans
            // that exceed the budget remain available through the full file.
            foreach (string key in new[] { "ok", "snapshot", "liveViewport",
                "infrastructure", "reachability", "logErrors", "progressiveDetail" })
            {
                JToken value;
                if (!state.TryGetValue(key, out value)) continue;
                preview[key] = value.DeepClone();
                if (preview.ToString(Formatting.None).Length > maximumCharacters)
                {
                    preview.Remove(key);
                    continue;
                }
                omitted.First(item => item.Type == JTokenType.String && (string)item == key).Remove();
            }
            if (preview["snapshot"] == null)
            {
                string summary = (state["snapshot"] as JObject)?["summary"]?.ToString();
                if (!string.IsNullOrEmpty(summary))
                {
                    preview["summary"] = summary.Length <= 256 ? summary : summary.Substring(0, 256) + "…";
                    if (preview.ToString(Formatting.None).Length > maximumCharacters)
                        preview.Remove("summary");
                }
            }
            // Current-state keys are bounded by the producer. Refuse a future
            // oversized envelope instead of emitting invalid or misleading JSON.
            string result = preview.ToString(Formatting.None);
            if (result.Length > maximumCharacters)
                throw new InvalidOperationException("Current-state preview metadata exceeds its character budget.");
            return result;
        }
    }
}
