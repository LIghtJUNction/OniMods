using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using Jlib.Runtime;
using OniMcp.Config;
using OniMcp.Core;
using OniMcp.Server;
using OniMcp.Tools;

internal static class ProtocolMetadataRegression
{
    public static void Run()
    {
        var bridge = new JlibDispatcher();
        Invoke(bridge, "Awake");
        Invoke(new MainThreadBridge(), "Awake");
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        OniMcpOptions.Save(new OniMcpOptions { Port = port });
        var server = new McpHttpServer();
        server.StartServer();
        try
        {
            using (var client = new HttpClient { BaseAddress = new Uri(OniMcpOptions.Current.EndpointUrl),
                Timeout = TimeSpan.FromSeconds(3) })
            {
                string owner = Initialize(client, 1);
                string other = Initialize(client, 2);
                RejectBadParametersWithoutPumping(client, owner);
                var created = (CreateTaskResult)Invoke(server, "ProcessMethod",
                    new JsonRpcRequest { Id = 3, Method = "tools/call", Params = new JObject
                    { ["name"] = "test", ["task"] = new JObject() } }, owner);
                string taskId = created.Task.TaskId;
                var taskArgs = new JObject { ["taskId"] = taskId };
                int calls = OniToolRegistry.Calls;

                // Fill every gameplay admission slot without advancing Unity.
                for (int i = 0; i < McpHttpServer.MaxPendingMainThreadHttpRequests; i++)
                {
                    using (var response = Send(client, owner, new JObject
                    { ["jsonrpc"] = "2.0", ["method"] = "notifications/test_queue" }))
                        Require(response.StatusCode == HttpStatusCode.Accepted, "Could not fill gameplay queue");
                }

                var listed = Rpc(client, owner, 4, "tasks/list", new JObject());
                Require(((JArray)listed["result"]["tasks"]).Count == 1, "Pending task was not listed");
                var status = Rpc(client, owner, 5, "tasks/get", taskArgs);
                Require((string)status["result"]["status"] == "working", "Pending task status was lost");
                var result = Rpc(client, owner, 6, "tasks/result", taskArgs);
                Require(result["result"] != null && result["error"] == null, "Pending task result was inaccessible");
                Require(((JArray)Rpc(client, other, 7, "tasks/list", new JObject())["result"]["tasks"]).Count == 0,
                    "Task listing crossed sessions");
                foreach (string method in new[] { "tasks/get", "tasks/result", "tasks/cancel" })
                    Require(Rpc(client, other, 8, method, taskArgs)["error"] != null, "Foreign task accessible: " + method);
                var cancelled = Rpc(client, owner, 9, "tasks/cancel", taskArgs);
                Require((string)cancelled["result"]["status"] == "cancelled", "Queued task was not cancelled");
                Require(OniToolRegistry.Calls == calls, "Metadata request executed game work");
                for (int i = 0; i < 10; i++) Invoke(bridge, "Update");
                Require(OniToolRegistry.Calls == calls, "Cancelled task executed when Unity resumed");
                Require((string)Rpc(client, owner, 10, "tasks/get", taskArgs)["result"]["status"] == "cancelled",
                    "Queue drain overwrote cancellation");
                var running = (CreateTaskResult)Invoke(server, "ProcessMethod",
                    new JsonRpcRequest { Id = 30, Method = "tools/call", Params = new JObject
                    { ["name"] = "test", ["task"] = new JObject() } }, owner);
                var runningArgs = new JObject { ["taskId"] = running.Task.TaskId };
                OniToolRegistry.OnCall = (name, arguments) =>
                {
                    var response = Rpc(client, owner, 31, "tasks/cancel", runningArgs);
                    Require(((string)response["result"]["statusMessage"]).Contains("started"),
                        "In-flight cancellation claimed the tool never ran");
                };
                try { Invoke(bridge, "Update"); }
                finally { OniToolRegistry.OnCall = null; }
                Require(OniToolRegistry.Calls == calls + 1, "In-flight tool did not run exactly once");
                var finished = Rpc(client, owner, 32, "tasks/result", runningArgs);
                Require(finished["result"]["result"] != null, "In-flight cancellation discarded the tool result");
                Require((string)Rpc(client, owner, 33, "tasks/get", runningArgs)["result"]["status"] == "cancelled",
                    "Completion overwrote the cancellation request");
                Console.WriteLine("PASS protocol metadata: saturated-queue reads, cancellation boundaries and malformed parameters");
            }
        }
        finally
        {
            server.StopServer();
            Invoke(bridge, "OnDestroy");
        }
    }

    private static void RejectBadParametersWithoutPumping(HttpClient client, string session)
    {
        int calls = OniToolRegistry.Calls;
        var invalid = new[]
        {
            new JObject { ["name"] = 42 },
            new JObject { ["name"] = "test", ["arguments"] = new JArray() },
            new JObject { ["name"] = "test", ["task"] = true },
            new JObject { ["name"] = "test", ["task"] = new JObject { ["ttl"] = 2147483648L } },
            new JObject { ["name"] = "test", ["task"] = new JObject { ["title"] = new JArray() } }
        };
        foreach (var args in invalid)
        {
            var response = Rpc(client, session, 20, "tools/call", args);
            Require((int)response["error"]["code"] == McpErrorCode.InvalidParams, "Bad tool envelope was accepted");
            Require((string)response["error"]["data"]["reasonCode"] == "invalid_params", "Missing recovery reason");
            Require((int)response["id"] == 20, "Invalid params lost request id");
        }
        foreach (string method in new[] { "tasks/get", "tasks/result", "tasks/cancel", "resources/read" })
        {
            var response = Rpc(client, session, 21, method, new JObject());
            Require((int)response["error"]["code"] == McpErrorCode.InvalidParams, "Missing selector was queued");
        }
        using (var response = Send(client, session, new JObject
        { ["jsonrpc"] = "2.0", ["id"] = 22, ["method"] = "tools/call", ["params"] = new JArray() }))
        {
            var body = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            Require((int)body["error"]["code"] == McpErrorCode.InvalidParams && (int)body["id"] == 22,
                "Array params were reported as a parse error or lost their id");
        }
        Require(OniToolRegistry.Calls == calls, "Bad parameters executed a game handler");
    }

    private static string Initialize(HttpClient client, int id)
    {
        using (var response = Send(client, null, new JObject
        {
            ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = "initialize",
            ["params"] = new JObject { ["protocolVersion"] = "2025-11-25", ["capabilities"] = new JObject() }
        }))
        {
            response.EnsureSuccessStatusCode();
            foreach (var value in response.Headers.GetValues("Mcp-Session-Id")) return value;
            throw new InvalidOperationException("No session id");
        }
    }

    private static JObject Rpc(HttpClient client, string session, int id, string method, JObject args)
    {
        using (var response = Send(client, session, new JObject
        { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = args }))
        {
            Require(response.StatusCode == HttpStatusCode.OK, method + " blocked behind game-thread work");
            return JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        }
    }

    private static HttpResponseMessage Send(HttpClient client, string session, JObject message)
    {
        using (var request = new HttpRequestMessage(HttpMethod.Post, client.BaseAddress))
        {
            request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
            request.Headers.TryAddWithoutValidation("Mcp-Protocol-Version", "2025-11-25");
            if (session != null) request.Headers.TryAddWithoutValidation("Mcp-Session-Id", session);
            request.Content = new StringContent(message.ToString(), Encoding.UTF8, "application/json");
            return client.SendAsync(request).GetAwaiter().GetResult();
        }
    }

    private static object Invoke(object target, string name, params object[] args)
    {
        return target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
