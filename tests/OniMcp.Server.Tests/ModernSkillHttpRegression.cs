using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using OniMcp.Config;
using OniMcp.Server;

internal static class ModernSkillHttpRegression
{
    internal static void Run(
        string modernMeta,
        int port,
        Func<string, string, string, HttpResponseMessage> post)
    {
        string skillUri = OniMcpSkillCatalog.SkillUri;
        string discover = Request("server/discover", modernMeta, null);
        string list = Request("skills/list", modernMeta, null);
        string get = Request("skills/get", modernMeta, new JObject { ["uri"] = skillUri });
        string read = Request("resources/read", modernMeta, new JObject { ["uri"] = skillUri });
        string resourceList = Request("resources/list", modernMeta, null);
        var previous = OniMcpOptions.Current;
        try
        {
            using (var off = post(list, "skills/list", null))
                Require(off.StatusCode == HttpStatusCode.NotFound
                    && (int)Parse(off)["error"]["code"] == -32601,
                    "disabled Skills surface must be unavailable");
            using (var off = post(discover, "server/discover", null))
                Require(Parse(off)["result"]["capabilities"]["extensions"] == null,
                    "disabled Skills capability must not be advertised");

            OniMcpOptions.Save(new OniMcpOptions { Port = port, SkillsExtensionEnabled = true });
            using (var on = post(discover, "server/discover", null))
            {
                var capabilities = Parse(on)["result"]["capabilities"];
                Require(capabilities["resources"] != null
                    && capabilities["extensions"]?["io.modelcontextprotocol/skills"] is JObject,
                    "Skills advertisement requires Resources plus extension entry");
            }

            JObject listed;
            using (var response = post(list, "skills/list", null))
            {
                Require(response.StatusCode == HttpStatusCode.OK
                    && !response.Headers.Contains("Mcp-Session-Id"), "Skills list must be stateless");
                var payload = Parse(response)["result"];
                Require((string)payload["resultType"] == "complete"
                    && (string)payload["cacheScope"] == "public"
                    && (int)payload["ttlMs"] > 0, "Skills list cache/result contract");
                Require(((JArray)payload["skills"]).Count == 1, "one allowlisted skill must be visible");
                listed = (JObject)payload["skills"][0];
            }

            using (var response = post(get, "skills/get", null))
            {
                var payload = Parse(response)["result"];
                Require(response.StatusCode == HttpStatusCode.OK
                    && JToken.DeepEquals(payload["skill"], listed), "skills/get must equal skills/list entry");
            }

            using (var response = post(resourceList, "resources/list", null))
            {
                var resources = (JArray)Parse(response)["result"]["resources"];
                Require(resources.Any(item => (string)item["uri"] == skillUri),
                    "skills manifest files must be listed by resources/list");
            }

            foreach (JObject resource in (JArray)listed["resources"])
            {
                string uri = (string)resource["uri"];
                string request = Request("resources/read", modernMeta, new JObject { ["uri"] = uri });
                using (var response = post(request, "resources/read", uri))
                {
                    var payload = Parse(response)["result"];
                    Require(response.StatusCode == HttpStatusCode.OK
                        && !response.Headers.Contains("Mcp-Session-Id")
                        && (string)payload["contents"][0]["text"]
                            == (string)OniMcpSkillCatalog.Read(uri)["contents"][0]["text"],
                        "listed Skills resource bytes must be readable: " + uri);
                }
            }

            foreach (string unknown in new[]
            {
                "skill://oni-mcp-control/%2e%2e/private.md",
                "skill://oni-mcp-control/../private.md",
                "skill://other/SKILL.md"
            })
            {
                using (var response = post(Request("skills/get", modernMeta,
                           new JObject { ["uri"] = unknown }), "skills/get", null))
                    Require((int)Parse(response)["error"]["code"] == -32602,
                        "unknown skills/get URI must fail closed");
                using (var response = post(Request("resources/read", modernMeta,
                           new JObject { ["uri"] = unknown }), "resources/read", unknown))
                    Require(Parse(response)["error"] != null,
                        "unknown skills resource must not escape the allowlist");
            }
        }
        finally
        {
            OniMcpOptions.Save(previous);
        }
    }

    private static string Request(string method, string modernMeta, JObject args)
    {
        var body = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
            ["id"] = 880
        };
        var parameters = args == null ? new JObject() : (JObject)args.DeepClone();
        // A real modern request must retain the caller metadata and routing headers.
        parameters["_meta"] = JObject.Parse("{" + modernMeta + "}")["_meta"];
        body["params"] = parameters;
        return body.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static JObject Parse(HttpResponseMessage response)
    {
        return JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
    }

    private static void Require(bool condition, string label)
    {
        if (!condition)
            throw new InvalidOperationException(label);
    }
}
