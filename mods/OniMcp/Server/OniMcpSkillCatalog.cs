using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

namespace OniMcp.Server
{
    // Listing and reads use the same fixed byte snapshot from embedded resources.
    internal static class OniMcpSkillCatalog
    {
        internal const string SkillUri = "skill://oni-mcp-control/SKILL.md";
        private const string Prefix = "skill://oni-mcp-control/";
        private const string LogicalPrefix = "OniMcp.Skills.oni-mcp-control.";
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private static readonly string[] Paths =
        {
            "SKILL.md",
            "references/world-editor.md",
            "references/world-editor.zh.md",
            "references/tool-reference.md",
            "references/tool-reference.zh.md"
        };

        private sealed class SkillFile
        {
            internal string Path;
            internal string Uri;
            internal string Text;
            internal int Size;
            internal string Digest;
        }

        private static readonly List<SkillFile> Files = LoadSnapshot();
        private static readonly Dictionary<string, SkillFile> ByUri =
            Files.ToDictionary(item => item.Uri, StringComparer.Ordinal);
        private static readonly JObject Entry = CreateEntry();

        internal static bool IsKnownResource(string uri)
        {
            return uri != null && ByUri.ContainsKey(uri);
        }

        internal static JObject GetSkill()
        {
            return (JObject)Entry.DeepClone();
        }

        internal static JArray ListResources()
        {
            var result = new JArray();
            foreach (var file in Files)
                result.Add(new JObject
                {
                    ["uri"] = file.Uri,
                    ["name"] = file.Path.Split('/').Last(),
                    ["mimeType"] = "text/markdown"
                });
            return result;
        }

        internal static JObject Read(string uri)
        {
            if (!IsKnownResource(uri))
                return null;
            var file = ByUri[uri];
            return new JObject
            {
                ["contents"] = new JArray(new JObject
                {
                    ["uri"] = file.Uri,
                    ["mimeType"] = "text/markdown",
                    ["text"] = file.Text
                })
            };
        }

        private static List<SkillFile> LoadSnapshot()
        {
            var result = new List<SkillFile>();
            Assembly assembly = typeof(OniMcpSkillCatalog).Assembly;
            long totalSize = 0;
            foreach (string path in Paths)
            {
                string logicalName = LogicalPrefix + path.Replace('/', '.');
                using (Stream stream = assembly.GetManifestResourceStream(logicalName))
                {
                    if (stream == null)
                        throw new InvalidOperationException("Packaged skill file is missing: " + logicalName);
                    using (var memory = new MemoryStream())
                    {
                        stream.CopyTo(memory);
                        byte[] bytes = memory.ToArray();
                        totalSize += bytes.Length;
                        result.Add(new SkillFile
                        {
                            Path = path,
                            Uri = Prefix + path,
                            Text = Utf8.GetString(bytes),
                            Size = bytes.Length,
                            Digest = Sha256Digest(bytes)
                        });
                    }
                }
            }
            if (result.Count == 0 || result.Count > 512 || totalSize > 16L * 1024L * 1024L)
                throw new InvalidOperationException("Skill manifest exceeds the supported limits");
            if (!string.Equals(result[0].Uri, SkillUri, StringComparison.Ordinal))
                throw new InvalidOperationException("Skill package must begin with SKILL.md");
            return result;
        }

        private static string Sha256Digest(byte[] bytes)
        {
            using (var hash = SHA256.Create())
                return "sha256:" + BitConverter.ToString(hash.ComputeHash(bytes))
                    .Replace("-", string.Empty).ToLowerInvariant();
        }

        private static JObject CreateEntry()
        {
            var frontmatter = ParseFrontmatter(Files[0].Text);
            if (!string.Equals((string)frontmatter["name"], "oni-mcp-control", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace((string)frontmatter["description"]))
                throw new InvalidOperationException("Skill frontmatter does not match canonical URI");
            var resources = new JArray();
            foreach (var file in Files)
                resources.Add(new JObject
                {
                    ["uri"] = file.Uri,
                    ["digest"] = file.Digest,
                    ["size"] = file.Size
                });
            return new JObject
            {
                ["uri"] = SkillUri,
                ["frontmatter"] = frontmatter,
                ["resources"] = resources
            };
        }

        private static JObject ParseFrontmatter(string text)
        {
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            if (lines.Length < 4 || lines[0] != "---")
                throw new InvalidOperationException("SKILL.md must start with YAML frontmatter");
            var result = new JObject();
            bool closed = false;
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd('\r');
                if (line == "---")
                {
                    closed = true;
                    break;
                }
                int colon = line.IndexOf(':');
                if (colon <= 0 || result.Property(line.Substring(0, colon)) != null)
                    throw new InvalidOperationException("Malformed SKILL.md frontmatter");
                string key = line.Substring(0, colon).Trim();
                string value = line.Substring(colon + 1).Trim();
                if (key.Length == 0 || value.Length == 0)
                    throw new InvalidOperationException("Incomplete SKILL.md frontmatter");
                result[key] = value;
            }
            if (!closed)
                throw new InvalidOperationException("SKILL.md frontmatter was not closed");
            return result;
        }
    }
}
