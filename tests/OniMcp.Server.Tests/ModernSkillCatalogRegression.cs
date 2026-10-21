using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using OniMcp.Server;

internal static class ModernSkillCatalogRegression
{
    private static readonly string[] ExpectedPaths =
    {
        "SKILL.md",
        "references/world-editor.md",
        "references/world-editor.zh.md",
        "references/tool-reference.md",
        "references/tool-reference.zh.md",
        "references/interaction.md",
        "references/recording-and-archive.md",
        "docs/gameplay-task-feedback.md",
        "docs/composable-game-waits.md"
    };

    internal static void Run()
    {
        JObject entry = OniMcpSkillCatalog.GetSkill();
        Require((string)entry["uri"] == OniMcpSkillCatalog.SkillUri, "canonical skill URI");
        var frontmatter = (JObject)entry["frontmatter"];
        Require((string)frontmatter["name"] == "oni-mcp-control"
            && ((string)frontmatter["description"]).StartsWith("Control Oxygen Not Included"),
            "published frontmatter must match packaged SKILL.md");
        var manifest = (JArray)entry["resources"];
        VerifyRelativeReferences(manifest);
        Require(new HashSet<string>(manifest.Select(item => (string)item["uri"]), StringComparer.Ordinal)
            .SetEquals(ExpectedPaths.Select(path => "skill://oni-mcp-control/" + path)),
            "only the exact reviewed skill files may be published");
        var listed = OniMcpSkillCatalog.ListResources();
        Require(listed.Count == manifest.Count, "resource list and skill manifest must agree");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JObject item in manifest)
        {
            string uri = (string)item["uri"];
            Require(uri.StartsWith("skill://oni-mcp-control/", StringComparison.Ordinal)
                && seen.Add(uri), "skill URI must be unique within the canonical root");
            Require(OniMcpSkillCatalog.IsKnownResource(uri), "manifest entry must be readable");
            JObject body = OniMcpSkillCatalog.Read(uri);
            Require(body != null, "manifest resource is not served");
            string text = (string)body["contents"][0]["text"];
            byte[] data = new UTF8Encoding(false, true).GetBytes(text);
            Require((int)item["size"] == data.Length, "raw byte size mismatch for " + uri);
            string sha;
            using (var hash = SHA256.Create())
                sha = "sha256:" + BitConverter.ToString(hash.ComputeHash(data))
                    .Replace("-", "").ToLowerInvariant();
            Require((string)item["digest"] == sha, "digest mismatch for " + uri);
        }
        string skill = (string)OniMcpSkillCatalog.Read(OniMcpSkillCatalog.SkillUri)["contents"][0]["text"];
        Require(skill.StartsWith("---\nname: oni-mcp-control\n", StringComparison.Ordinal),
            "SKILL.md itself must use the advertised canonical name");
        Require(skill.Contains("description: " + (string)frontmatter["description"]),
            "SKILL.md description must equal advertised frontmatter");

        // Returned JSON must not be able to alter the cached immutable snapshot.
        frontmatter["name"] = "untrusted";
        manifest.RemoveAt(0);
        Require((string)OniMcpSkillCatalog.GetSkill()["frontmatter"]["name"] == "oni-mcp-control"
            && ((JArray)OniMcpSkillCatalog.GetSkill()["resources"]).Count == ExpectedPaths.Length,
            "caller mutation changed the catalog snapshot");

        foreach (string uri in new[]
        {
            "skill://oni-mcp-control/README.md",
            "skill://oni-mcp-control/../secrets",
            "skill://oni-mcp-control/%2e%2e/SKILL.md",
            "skill://oni-mcp-control/references/../../secret.md",
            "skill://oni-gameplay/SKILL.md",
            "skill://oni-mcp-control/references/world-editor.md?debug=true",
            "skill://oni-mcp-control/docs/../docs/gameplay-task-feedback.md",
            "skill://oni-mcp-control/docs/%2e%2e/docs/composable-game-waits.md",
            "skill://oni-mcp-control/docs/architecture-and-principles.md",
            "skill://oni-mcp-control/oni-mcp-colony-strategy/SKILL.md"
        })
            Require(!OniMcpSkillCatalog.IsKnownResource(uri)
                && OniMcpSkillCatalog.Read(uri) == null, "unlisted URI leaked content: " + uri);
    }

    private static void VerifyRelativeReferences(JArray manifest)
    {
        var skillReferences = new HashSet<string>(StringComparer.Ordinal);
        foreach (JObject item in manifest)
        {
            string uri = (string)item["uri"];
            string text = (string)OniMcpSkillCatalog.Read(uri)["contents"][0]["text"];
            foreach (Match match in Regex.Matches(text, @"\[[^\]]+\]\(([^)]+)\)"))
            {
                string target = match.Groups[1].Value;
                if (target.StartsWith("#", StringComparison.Ordinal)
                    || Uri.TryCreate(target, UriKind.Absolute, out _)) continue;
                var resolved = new Uri(new Uri(uri), target);
                Require(string.IsNullOrEmpty(resolved.Query), "packaged reference has an unexpected query: " + target);
                string resourceUri = resolved.GetLeftPart(UriPartial.Path);
                // The separately installed strategy skill remains optional, as in the original package.
                if (resourceUri == "skill://oni-mcp-control/oni-mcp-colony-strategy/SKILL.md") continue;
                Require(OniMcpSkillCatalog.Read(resourceUri) != null,
                    "packaged relative reference must be readable: " + uri + " -> " + target + " -> " + resourceUri);
                if (uri == OniMcpSkillCatalog.SkillUri) skillReferences.Add(resourceUri);
            }
        }

        foreach (string reference in new[] { "world-editor", "tool-reference" })
            Require(skillReferences.Contains("skill://oni-mcp-control/references/" + reference + ".md")
                && skillReferences.Contains("skill://oni-mcp-control/references/" + reference + ".zh.md"),
                "the entry point must resolve both language references for " + reference);
    }

    private static void Require(bool value, string message)
    {
        if (!value)
            throw new InvalidOperationException(message);
    }
}
