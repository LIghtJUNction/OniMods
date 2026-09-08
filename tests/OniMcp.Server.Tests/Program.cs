using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OniMcp.Config;
using OniMcp.Core;
using OniMcp.Server;
using OniMcp.Tools;

internal static class Program
{
    private static int _passed;
    private static MainThreadBridge _bridge;

    private static void Main()
    {
        Check("missing main-thread bridge never executes inline", () =>
        {
            bool called = false;
            Throws<InvalidOperationException>(() => MainThreadBridge.Invoke(() => called = true));
            Throws<InvalidOperationException>(() => MainThreadBridge.Enqueue(() => called = true));
            Assert(!called, "An uninitialized bridge executed game code");
        });
        _bridge = new MainThreadBridge();
        Invoke(_bridge, "Awake");
        try
        {
            Check("main-thread invocation preserves result and exception", () =>
            {
                Assert(MainThreadBridge.Invoke(() => 42) == 42, "Inline return value");
                var expected = new InvalidOperationException("expected");
                var invocation = new MainThreadInvocation<int>(() => { throw expected; });
                invocation.Execute();
                Assert(ReferenceEquals(expected, Throws<InvalidOperationException>(() => invocation.Wait(1000))), "Exception identity was lost");
            });
            Check("queued invocation is cancelled after timeout", () =>
            {
                bool called = false;
                Task.Run(() => Throws<TimeoutException>(() => MainThreadBridge.Invoke(() => called = true, 10))).GetAwaiter().GetResult();
                Invoke(_bridge, "Update");
                Assert(!called, "Timed-out queued game action still executed");
            });
            Check("in-flight invocation completes safely after timeout", () =>
            {
                using (var started = new ManualResetEventSlim())
                using (var release = new ManualResetEventSlim())
                {
                    var invocation = new MainThreadInvocation<int>(() => { started.Set(); release.Wait(); return 9; });
                    var work = Task.Run(invocation.Execute);
                    Assert(started.Wait(1000), "Worker did not start");
                    try { Throws<TimeoutException>(() => invocation.Wait(0)); }
                    finally { release.Set(); }
                    Assert(work.Wait(1000), "Completion failed after caller timed out");
                    Assert(invocation.Wait(0) == 9, "Completion result lost");
                }
            });
            Check("queued worker receives original result", () =>
            {
                var work = Task.Run(() => MainThreadBridge.Invoke(() => 42));
                PumpUntil(work);
                Assert(work.Result == 42, "Queued return value");
            });
            Check("negative timeout is rejected before queuing", () =>
                Throws<ArgumentOutOfRangeException>(() => MainThreadBridge.Invoke(() => 1, -2)));
            Check("closed session wakes all readers and rejects enqueue", () =>
            {
                var session = new McpSession();
                using (var entered = new CountdownEvent(2))
                {
                    var readers = Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
                    {
                        entered.Signal();
                        session.WaitForOutbound(10000);
                    })).ToArray();
                    Assert(entered.Wait(1000), "Readers did not start");
                    session.Close();
                    Assert(Task.WaitAll(readers, 1000), "Session close did not wake every SSE reader");
                    Assert(!session.EnqueueOutbound(new JObject()), "Closed queue accepted a message");
                }
            });
            Check("session queue is bounded and remains FIFO", () =>
            {
                var session = new McpSession();
                for (int i = 0; i < McpSession.MaxQueuedOutboundMessages; i++)
                    Assert(session.EnqueueOutbound(new JObject { ["id"] = i }), "Queue rejected available capacity");
                Assert(!session.EnqueueOutbound(new JObject()), "Unbounded session queue");
                Assert((int)session.TryDequeueOutbound()["id"] == 0, "Oldest message lost");
                session.Close();
                Assert(session.QueuedOutboundCount == 0, "Closed session retained messages");
            });
            Check("body limit covers declared and chunked UTF-8 bytes", () =>
            {
                using (var body = new MemoryStream(Encoding.UTF8.GetBytes("你好")))
                {
                    Assert(HttpRequestBody.Read(body, Encoding.UTF8, -1, 6) == "你好", "Exact byte limit failed");
                    body.Position = 0;
                    Throws<RequestBodyTooLargeException>(() => HttpRequestBody.Read(body, Encoding.UTF8, -1, 5));
                    body.Position = 0;
                    Throws<RequestBodyTooLargeException>(() => HttpRequestBody.Read(body, Encoding.UTF8, 6, 5));
                    Assert(body.Position == 0, "Oversized Content-Length was read");
                }
            });
            Check("settings form preserves encoded plus and literal equals", () =>
            {
                var form = (Dictionary<string, string>)InvokeStatic(typeof(McpHttpServer), "ParseQueryString", "?token=a%2Bb==&space=hello+world&flag&empty=&token2=%252B&&");
                Assert(form["token"] == "a+b==", "Authentication token was corrupted");
                Assert(form["space"] == "hello world" && form["flag"] == "" && form["empty"] == "", "Form decoding failed");
                Assert(form["token2"] == "%2B" && !form.ContainsKey(""), "Form decoded twice or retained empty key");
            });
            Check("tasks are isolated by session and cancellation prevents execution", TestTaskIsolation);
            TestHttpTransport();
            Check("bridge destruction wakes an infinite pending invocation", () =>
            {
                bool called = false;
                var work = Task.Run(() => Throws<InvalidOperationException>(() => MainThreadBridge.Invoke(() => called = true, Timeout.Infinite)));
                var queueLock = typeof(MainThreadBridge).GetField("_queueLock", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_bridge);
                var cancellations = (HashSet<Action>)typeof(MainThreadBridge).GetField("_pendingCancellations", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_bridge);
                Assert(SpinWait.SpinUntil(() => { lock (queueLock) return cancellations.Count > 0; }, 1000), "Invocation was never queued");
                Invoke(_bridge, "OnDestroy");
                Assert(work.Wait(1000) && !called, "Destroyed bridge left caller blocked or executed game code");
            });
            Check("delayed settings restart survives bridge destruction", () =>
            {
                InvokeStatic(typeof(McpHttpServer), "ScheduleSettingsRestartAfterResponse");
                // The callback runs after 250 ms; an escaping ThreadPool exception terminates this process.
                Thread.Sleep(400);
            });
        }
        finally { Invoke(_bridge, "OnDestroy"); }
        Console.WriteLine("PASS: " + _passed + " OniMcp Server regressions (production server sources; game boundaries stubbed).");
    }

    private static void TestTaskIsolation()
    {
        var server = new McpHttpServer();
        typeof(McpHttpServer).GetField("_running", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(server, true);
        var sessions = (Dictionary<string, McpSession>)typeof(McpHttpServer).GetField("_sessions", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(server);
        sessions["owner"] = new McpSession { Id = "owner" };
        var created = (CreateTaskResult)Process(server, "tools/call", "owner", new JObject
        {
            ["name"] = "test", ["task"] = new JObject()
        });
        var taskId = new JObject { ["taskId"] = created.Task.TaskId };
        Assert(((ListTasksResult)Process(server, "tasks/list", "other")).Tasks.Count == 0, "Foreign task leaked through list");
        foreach (var method in new[] { "tasks/get", "tasks/result", "tasks/cancel" })
            Assert(Process(server, method, "other", taskId) is JsonRpcResponse, "Foreign task accessible through " + method);
        Assert(((ListTasksResult)Process(server, "tasks/list", "owner")).Tasks.Count == 1, "Owner cannot see task");
        Assert(((McpTaskInfo)Process(server, "tasks/get", "owner", taskId)).Status == "working", "Foreign cancellation changed task");
        Assert(((McpTaskInfo)Process(server, "tasks/cancel", "owner", taskId)).Status == "cancelled", "Owner cancellation failed");
        int calls = OniToolRegistry.Calls;
        Invoke(_bridge, "Update");
        Assert(OniToolRegistry.Calls == calls, "Cancelled task executed");
    }

    private static void TestHttpTransport()
    {
        var portProbe = new TcpListener(IPAddress.Loopback, 0);
        portProbe.Start();
        int port = ((IPEndPoint)portProbe.LocalEndpoint).Port;
        portProbe.Stop();
        OniMcpOptions.Save(new OniMcpOptions { Port = port });
        var server = new McpHttpServer();
        server.StartServer();
        using (var client = new HttpClient { BaseAddress = new Uri(OniMcpOptions.Current.EndpointUrl), Timeout = TimeSpan.FromSeconds(5) })
        {
            try
            {
                Check("HTTP initialize rejects malformed requests without allocating sessions", () =>
                {
                    foreach (var request in new[] {
                        "{\"method\":\"initialize\",\"id\":1,\"params\":{\"protocolVersion\":\"2025-11-25\"}}",
                        "{\"jsonrpc\":\"2.0\",\"method\":42,\"id\":1}",
                        "{\"jsonrpc\":\"2.0\",\"method\":\"initialize\",\"id\":{},\"params\":{\"protocolVersion\":\"2025-11-25\"}}",
                        "{\"jsonrpc\":\"2.0\",\"method\":\"initialize\",\"id\":1,\"params\":{\"protocolVersion\":\"invalid\"}}",
                        "{\"jsonrpc\":\"2.0\",\"method\":\"initialize\",\"id\":1,\"params\":{\"protocolVersion\":{}}}",
                        "{\"jsonrpc\":\"2.0\",\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-11-25\"}}"
                    })
                    using (var response = Post(client, request))
                        Assert(ReadJson(response)["error"] != null, "Malformed initialization succeeded");
                    Assert(server.GetSessionSummaries().Count == 0, "Rejected initialize leaked sessions");
                });
                string initialize = "{\"jsonrpc\":\"2.0\",\"method\":\"initialize\",\"id\":1,\"params\":{\"protocolVersion\":\"2025-11-25\"}}";
                Check("HTTP initialize rejects a client-chosen unknown session id", () =>
                {
                    using (var response = Post(client, initialize, "forged"))
                        Assert(response.StatusCode == HttpStatusCode.NotFound, "Unknown session was accepted");
                    Assert(server.GetSessionSummaries().Count == 0, "Unknown id allocated a session");
                });
                string sessionId;
                using (var response = Post(client, initialize))
                {
                    Assert(ReadJson(response)["result"] != null, "Valid initialize failed");
                    sessionId = response.Headers.GetValues("Mcp-Session-Id").Single();
                }
                Check("HTTP explicit null id returns a response", () =>
                {
                    using (var response = Post(client, "{\"jsonrpc\":\"2.0\",\"method\":\"tools/list\",\"id\":null}", sessionId))
                    {
                        Assert(response.StatusCode == HttpStatusCode.OK, "Null id was treated as notification");
                        var json = ReadJson(response);
                        Assert(json.Property("id") != null && json["result"] != null, "Null-id response envelope incomplete");
                    }
                });
                Check("HTTP delete prevents session resurrection and removes pending tasks", () =>
                {
                    Process(server, "tools/call", sessionId, new JObject { ["name"] = "test", ["task"] = new JObject() });
                    using (var request = new HttpRequestMessage(HttpMethod.Delete, ""))
                    {
                        request.Headers.Add("Mcp-Session-Id", sessionId);
                        using (var response = client.SendAsync(request).GetAwaiter().GetResult())
                            Assert(response.StatusCode == HttpStatusCode.NoContent, "Delete failed");
                    }
                    int calls = OniToolRegistry.Calls;
                    Invoke(_bridge, "Update");
                    Assert(OniToolRegistry.Calls == calls, "Deleted session task still executed");
                    Assert(((ListTasksResult)Process(server, "tasks/list", sessionId)).Tasks.Count == 0, "Deleted session retained tasks");
                    var rejected = Throws<TargetInvocationException>(() => Process(server, "tools/call", sessionId,
                        new JObject { ["name"] = "test", ["task"] = new JObject() }));
                    Assert(rejected.InnerException is InvalidOperationException, "Deleted session created a task");
                    using (var response = Post(client, initialize, sessionId))
                        Assert(response.StatusCode == HttpStatusCode.NotFound, "Deleted session was resurrected");
                });
                Check("HTTP server restarts with a working listener", () =>
                {
                    server.RestartServer();
                    using (var response = Post(client, initialize))
                        Assert(ReadJson(response)["result"] != null, "Restarted listener failed initialization");
                    Assert(server.GetSessionSummaries().Count == 1, "Restart retained old sessions");
                });
            }
            finally { server.StopServer(); }
        }
    }

    private static object Process(McpHttpServer server, string method, string sessionId, JObject parameters = null) =>
        Invoke(server, "ProcessMethod", new JsonRpcRequest { Id = 1, Method = method, Params = parameters }, sessionId);

    private static HttpResponseMessage Post(HttpClient client, string json, string sessionId = null)
    {
        using (var request = new HttpRequestMessage(HttpMethod.Post, ""))
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            if (sessionId != null)
                request.Headers.Add("Mcp-Session-Id", sessionId);
            var work = client.SendAsync(request);
            PumpUntil(work);
            return work.GetAwaiter().GetResult();
        }
    }

    private static JObject ReadJson(HttpResponseMessage response) => JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());

    private static void PumpUntil(Task work)
    {
        var elapsed = Stopwatch.StartNew();
        while (!work.IsCompleted && elapsed.ElapsedMilliseconds < 5000)
        {
            Invoke(_bridge, "Update");
            Thread.Sleep(1);
        }
        Assert(work.IsCompleted, "Work did not finish before test deadline");
    }

    private static object Invoke(object instance, string name, params object[] arguments) =>
        instance.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(instance, arguments);
    private static object InvokeStatic(Type type, string name, params object[] arguments) =>
        type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, arguments);
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static T Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T error) { return error; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private static void Check(string name, Action action)
    {
        action();
        _passed++;
        Console.WriteLine("PASS " + name);
    }
}
