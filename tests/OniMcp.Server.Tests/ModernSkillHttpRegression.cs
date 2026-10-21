using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
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
        string resourceList = Request("resources/list", modernMeta, null);
        var previous = OniMcpOptions.Current;
        try
        {
            using (var off = post(list, "skills/list", null))
                Require(off.StatusCode == HttpStatusCode.NotFound
                    && (int)Parse(off)["error"]["code"] == -32601,
                    "disabled Skills surface must be unavailable");
            using (var off = post(get, "skills/get", null))
                Require(off.StatusCode == HttpStatusCode.NotFound
                    && (int)Parse(off)["error"]["code"] == -32601,
                    "disabled direct skill lookup must be unavailable");
            using (var off = post(resourceList, "resources/list", null))
                Require(!((JArray)Parse(off)["result"]["resources"])
                    .Any(item => OniMcpSkillCatalog.IsKnownResource((string)item["uri"])),
                    "disabled skill resources must not be listed");
            foreach (JObject resource in (JArray)OniMcpSkillCatalog.GetSkill()["resources"])
            {
                string uri = (string)resource["uri"];
                using (var off = post(Request("resources/read", modernMeta,
                           new JObject { ["uri"] = uri }), "resources/read", uri))
                {
                    var payload = Parse(off);
                    Require(off.StatusCode == HttpStatusCode.OK && payload["result"] == null
                        && (int)payload["error"]["code"] == -32602,
                        "disabled direct skill resource read must fail: " + uri);
                }
            }
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
                Require(((JArray)listed["resources"]).All(item =>
                    resources.Count(resource => (string)resource["uri"] == (string)item["uri"]) == 1),
                    "every skills manifest file must be listed exactly once by resources/list");
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
                "skill://oni-mcp-control/docs/../docs/gameplay-task-feedback.md",
                "skill://oni-mcp-control/docs/architecture-and-principles.md",
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

            VerifyAuthentication(modernMeta);
        }
        finally
        {
            OniMcpOptions.Save(previous);
        }
    }

    internal static void VerifySettingsPost(HttpClient client, int port)
    {
        var previous = OniMcpOptions.Current;
        try
        {
            foreach (bool enabled in new[] { false, true })
            {
                var current = new OniMcpOptions { Port = port, ScreenshotRetentionMinutes = 30, ScreenshotMaxFiles = 10 };
                if (enabled) current.SkillsExtensionEnabled = true;
                OniMcpOptions.Save(current);
                using (var form = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["host"] = "127.0.0.1", ["port"] = port.ToString(),
                    ["screenshotRetentionMinutes"] = "60", ["screenshotMaxFiles"] = "20"
                }))
                using (var response = client.PostAsync("/settings/", form).GetAwaiter().GetResult())
                {
                    Require(response.StatusCode == HttpStatusCode.OK
                        && response.Content.ReadAsStringAsync().GetAwaiter().GetResult().Contains("Settings saved."),
                        "the production settings POST must accept the unrelated form changes");
                    Require(!ReferenceEquals(OniMcpOptions.Current, current)
                        && OniMcpOptions.Current.ScreenshotRetentionMinutes == 60
                        && OniMcpOptions.Current.ScreenshotMaxFiles == 20,
                        "the production settings POST must save the changed settings");
                    Require(OniMcpOptions.Current.SkillsExtensionEnabled == enabled,
                        "settings save must preserve SkillsExtensionEnabled=" + enabled);
                }
            }
        }
        finally
        {
            OniMcpOptions.Save(previous);
        }
    }

    private static void VerifyAuthentication(string modernMeta)
    {
        // Start through the production listener so its captured auth options match Current.
        var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        int port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        var previous = OniMcpOptions.Current;
        var options = new OniMcpOptions
        {
            Port = port, SkillsExtensionEnabled = true,
            AuthEnabled = true, AuthToken = "skills-regression-token"
        };
        OniMcpOptions.Save(options);
        var server = new McpHttpServer();
        try
        {
            server.StartServer();
            using (var client = new HttpClient
            {
                BaseAddress = new Uri(options.EndpointUrl), Timeout = TimeSpan.FromSeconds(5)
            })
            {
                VerifyAccess(client, modernMeta, "skills/list", null);
                VerifyAccess(client, modernMeta, "skills/get", OniMcpSkillCatalog.SkillUri);
                VerifyAccess(client, modernMeta, "resources/list", null);
                foreach (JObject resource in (JArray)OniMcpSkillCatalog.GetSkill()["resources"])
                    VerifyAccess(client, modernMeta, "resources/read", (string)resource["uri"]);
            }
        }
        finally
        {
            server.StopServer();
            OniMcpOptions.Save(previous);
        }
    }

    private static void VerifyAccess(HttpClient client, string modernMeta, string method, string uri)
    {
        foreach (string token in new[] { null, "wrong-regression-token", "skills-regression-token" })
        using (var request = new HttpRequestMessage(HttpMethod.Post, ""))
        {
            request.Content = new StringContent(Request(method, modernMeta,
                uri == null ? null : new JObject { ["uri"] = uri }), Encoding.UTF8, "application/json");
            request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
            request.Headers.Add("Mcp-Protocol-Version", "2026-07-28");
            request.Headers.Add("Mcp-Method", method);
            if (method == "resources/read") request.Headers.Add("Mcp-Name", uri);
            if (token != null) request.Headers.Add("Authorization", "Bearer " + token);
            using (var response = client.SendAsync(request).GetAwaiter().GetResult())
            {
                bool authorized = token == "skills-regression-token";
                var payload = Parse(response);
                Require(response.StatusCode == (authorized ? HttpStatusCode.OK : HttpStatusCode.Unauthorized)
                    && (authorized ? payload["result"] != null : payload["result"] == null && payload["error"] != null),
                    method + " must enforce the same HTTP authentication gate as skill resource reads");
                if (authorized && method == "resources/read")
                    Require((string)payload["result"]["contents"][0]["text"]
                        == (string)OniMcpSkillCatalog.Read(uri)["contents"][0]["text"],
                        "authorized reads must return the exact packaged resource bytes");
            }
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
