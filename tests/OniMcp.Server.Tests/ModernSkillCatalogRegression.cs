using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using OniMcp.Server;

internal static class ModernSkillCatalogRegression
{
    internal static void Run()
    {
        JObject entry = OniMcpSkillCatalog.GetSkill();
        Require((string)entry["uri"] == OniMcpSkillCatalog.SkillUri, "canonical skill URI");
        var frontmatter = (JObject)entry["frontmatter"];
        Require((string)frontmatter["name"] == "oni-mcp-control"
            && ((string)frontmatter["description"]).StartsWith("Control Oxygen Not Included"),
            "published frontmatter must match packaged SKILL.md");
        var manifest = (JArray)entry["resources"];
        Require(manifest.Count == 5, "only five allowlisted skill files may be published");
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
            && ((JArray)OniMcpSkillCatalog.GetSkill()["resources"]).Count == 5,
            "caller mutation changed the catalog snapshot");

        foreach (string uri in new[]
        {
            "skill://oni-mcp-control/README.md",
            "skill://oni-mcp-control/../secrets",
            "skill://oni-mcp-control/%2e%2e/SKILL.md",
            "skill://oni-mcp-control/references/../../secret.md",
            "skill://oni-gameplay/SKILL.md",
            "skill://oni-mcp-control/references/world-editor.md?debug=true"
        })
            Require(!OniMcpSkillCatalog.IsKnownResource(uri)
                && OniMcpSkillCatalog.Read(uri) == null, "unlisted URI leaked content: " + uri);
    }

    private static void Require(bool value, string message)
    {
        if (!value)
            throw new InvalidOperationException(message);
    }
}
