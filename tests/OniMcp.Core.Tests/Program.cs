using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;
using OniMcp.Tools;

internal static class Program
{
    private static int failures;

    private static int Main()
    {
        Run("registry initialization can retry after a factory failure", InitializationRetry);
        Run("JSON-RPC preserves null id and success result", JsonRpcNulls);
        Run("task descriptions require nonempty JSON strings", TaskDescriptions);
        Run("invalid tool names return errors", InvalidNames);
        Run("tool calls still enforce coordinate and task constraints", ToolConstraints);
        Run("tool cache respects visibility after all-tools access", CacheVisibility);
        Run("metadata list callers cannot corrupt cached tool count", MetadataIsolation);
        Run("all static resources dispatch to their registered operation", StaticResources);
        Run("dynamic resource routes preserve their read action", DynamicResources);
        Run("resource templates reject query operation overrides", TemplateConstraints);
        Run("resource errors return valid JSON content", ResourceErrors);
        Console.WriteLine(failures == 0 ? "All 11 core regression groups passed." : failures + " regression groups failed.");
        return failures == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try
        {
            ToolCallMiddleware.Clear();
            test();
            Console.WriteLine("PASS " + name);
        }
        catch (Exception ex)
        {
            failures++;
            Console.Error.WriteLine("FAIL " + name + ": " + ex.Message);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void InitializationRetry()
    {
        GameStubs.FailNextFactory = true;
        try
        {
            OniToolRegistry.Initialize();
            throw new Exception("Expected a factory failure.");
        }
        catch (InvalidOperationException) { }
        OniToolRegistry.Initialize();
        Require(OniToolRegistry.GetTools().Count == 7, "Retry left the public registry incomplete.");
        Require(OniToolRegistry.TryGetOperation("read_control", out _), "Retry did not initialize internal operations.");
    }

    private static void JsonRpcNulls()
    {
        var error = Serialize(JsonRpcResponse.MakeError(null, McpErrorCode.ParseError, "invalid JSON"));
        Require(error.Property("id") != null && error["id"].Type == JTokenType.Null, "Error omitted id:null.");
        Require(error.Property("result") == null && error["error"] != null, "Error must contain only the error branch.");
        var success = Serialize(JsonRpcResponse.Success(null, null));
        Require(success.Property("id") != null && success["id"].Type == JTokenType.Null, "Success omitted id:null.");
        Require(success.Property("result") != null && success["result"].Type == JTokenType.Null, "Success omitted result:null.");
        Require(success.Property("error") == null, "Success unexpectedly contains error.");
        Require((int)Serialize(JsonRpcResponse.Success(4, false))["id"] == 4, "Numeric id changed.");
    }

    private static JObject Serialize(object value) => JObject.Parse(JsonConvert.SerializeObject(value, McpJsonUtil.Settings));

    private static void TaskDescriptions()
    {
        foreach (var value in new JToken[] { JValue.CreateNull(), new JValue(42), new JValue(true), new JObject(), new JArray("x"), new JValue("  ") })
        {
            var args = new JObject { ["task"] = value };
            Require(!ToolCallMiddleware.TryGetTaskDescription(args, out _), "Accepted task type " + value.Type);
            int calls = GameStubs.HandlerCalls;
            Require(OniToolRegistry.CallTool("game_control", args).IsError, "Invalid task reached dispatch.");
            Require(calls == GameStubs.HandlerCalls, "Invalid task executed the handler.");
        }
        Require(!ToolCallMiddleware.TryGetTaskDescription(null, out _), "Accepted missing arguments.");
        Require(ToolCallMiddleware.TryGetTaskDescription(new JObject { ["task"] = "  inspect oxygen  " }, out var text)
            && text == "inspect oxygen", "Valid task was not trimmed.");
    }

    private static void InvalidNames()
    {
        foreach (var name in new[] { null, "", " ", "missing" })
        {
            Require(!OniToolRegistry.TryGetOperation(name, out _), "Invalid operation resolved.");
            Require(OniToolRegistry.CallTool(name, new JObject()).IsError, "Invalid public name did not return an error.");
            Require(OniToolRegistry.CallToolFromWorldEditor(name, new JObject(), false).IsError, "Invalid internal name did not return an error.");
        }
    }

    private static void ToolConstraints()
    {
        Require(OniToolRegistry.CallTool("game_control", new JObject()).IsError, "Task requirement was lost.");
        Require(OniToolRegistry.CallTool("game_control", new JObject
        {
            ["task"] = "inspect",
            ["target"] = new JObject { ["x"] = 4 }
        }).IsError, "Nested coordinates escaped validation.");
        Require(!OniToolRegistry.CallTool("legacy_game_control", new JObject { ["task"] = "inspect" }).IsError, "Alias dispatch failed.");
    }

    private static void CacheVisibility()
    {
        var a = new McpTool { Name = "a", Group = "one", Hidden = true };
        var b = new McpTool { Name = "b", Group = "one" };
        var c = new McpTool { Name = "c", Group = "one" };
        var tools = new[] { c, b, a };
        var cache = new OniToolRegistryCache();
        var all = cache.GetTools(tools);
        Require(all.Select(tool => tool.Name).SequenceEqual(new[] { "a", "b", "c" }), "All-tools order is unstable.");
        Require(cache.GetVisibleTools(tools, new[] { c }).Single() == c, "Previously cached all-tools ignored the supplied visible subset.");
        a.Hidden = false;
        Require(cache.GetVisibleSnapshot().Count == 3, "Visibility changes were retained in a stale snapshot.");
        all.Clear();
        cache.GetVisibleSnapshot().Clear();
        Require(cache.GetTools(tools).Count == 3, "Caller modified cached membership.");
        cache.Clear();
        Require(cache.GetTools(new[] { b }).Count == 1, "Clear did not invalidate the cache.");
    }

    private static void MetadataIsolation()
    {
        int count = OniToolRegistry.GetToolInfos().Count;
        OniToolRegistry.GetToolInfos().Clear();
        Require(count > 0 && OniToolRegistry.GetToolInfos().Count == count, "Caller removed cached metadata entries.");
    }

    private static JObject Read(string uri)
    {
        var resource = OniResourceRegistry.ReadResource(uri);
        Require(resource?.Contents?.Count == 1, "Missing resource content: " + uri);
        return JObject.Parse(resource.Contents[0].Text);
    }

    private static void StaticResources()
    {
        var resources = OniResourceRegistry.GetResourceInfos();
        Require(resources.Count > 0, "No resource routes registered.");
        ToolCallMiddleware.QueueNotification("next tool call");
        int presentations = ToolCallSpeechOverlay.Presentations;
        foreach (var resource in resources)
        {
            var result = Read(resource.Uri);
            Require(result["error"] == null, resource.Uri + ": " + (string)result["message"]);
            Require((string)result["tool"] == resource.Name, "Wrong operation for " + resource.Uri);
            Require(!string.IsNullOrEmpty((string)result["arguments"]["action"]), "Missing read action for " + resource.Uri);
        }
        Require((int)ToolCallMiddleware.Status()["pendingNotifications"] == 1, "Resource reads consumed tool notifications.");
        Require(ToolCallSpeechOverlay.Presentations == presentations, "Resource reads displayed tool-call overlays.");
        Require(OniResourceRegistry.ReadResource("oni://missing/resource") == null, "Unknown URI should remain unresolved.");
    }

    private static void DynamicResources()
    {
        var summary = Read("oni://power/summary?query=ore%20pile&action=delete&domain=invalid");
        Require((string)summary["tool"] == "read_control", "Internal read operation was not found.");
        Require((string)summary["arguments"]["action"] == "power_summary", "Query overrode the resource read action.");
        Require((string)summary["arguments"]["domain"] == "infrastructure", "Query overrode the resource domain.");
        Require((string)summary["arguments"]["query"] == "ore pile", "Query decoding failed.");
        var cell = Read("oni://world/cell/4/5");
        Require((string)cell["arguments"]["action"] == "cell_info" && (int)cell["arguments"]["x"] == 4, "World cell route failed.");
        var sandbox = Read("oni://sandbox/cell/6/7");
        Require((string)sandbox["arguments"]["kind"] == "read" && (string)sandbox["arguments"]["action"] == "sample_cell", "Sandbox sampling route changed operation.");
    }

    private static void ResourceErrors()
    {
        foreach (var query in new[] { "throw", "error", "null" })
        {
            var resource = OniResourceRegistry.ReadResource("oni://world/text-map?query=" + query);
            Require(resource.Contents[0].MimeType == "application/json", "Error has a non-JSON MIME type.");
            Require((bool)JObject.Parse(resource.Contents[0].Text)["error"], "Resource failure did not become an error response.");
        }
    }

    private static void TemplateConstraints()
    {
        int checkedRoutes = 0;
        foreach (var template in OniResourceRegistry.GetResourceTemplateInfos())
        {
            string uri = Regex.Replace(template.UriTemplate, @"\{\?[^}]*\}", "");
            uri = Regex.Replace(uri, @"\{[^}]+\}", "1");
            var baseline = OniResourceRegistry.ReadResource(uri + "?limit=1");
            if (baseline == null)
                throw new InvalidOperationException("Unresolved resource template: " + template.UriTemplate);
            var result = JObject.Parse(baseline.Contents[0].Text);
            if (result["error"] != null)
                continue; // Explicitly disabled routes and unknown generic tool names.
            var poisoned = Read(uri + "?action=delete&domain=sandbox&uiDomain=debug&rocketDomain=self_destruct&bioDomain=delete&confirm=true&force=true");
            foreach (var key in new[] { "action", "domain", "uiDomain", "rocketDomain", "bioDomain", "confirm", "force" })
                Require(JToken.DeepEquals(result["arguments"][key], poisoned["arguments"]?[key]), "Query overrode " + key + " at " + uri);
            checkedRoutes++;
        }
        Require(checkedRoutes > 50, "Insufficient resource-template coverage.");
        Require((bool)Read("oni://tools/read/game_control?action=delete")["error"], "Generic read route accepted an execute tool.");
        Console.WriteLine("Checked operation selectors for " + checkedRoutes + " resource templates.");
    }
}
