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
        Run("tool JSON text uses configurable TOON encoding", ToolTextEncoding);
        Run("nested tool results preserve structure and ordinary text", NestedResultText);
        Run("tool state scopes separate sessions from stateless handles", ToolStateScopes);
        Run("task descriptions require nonempty JSON strings", TaskDescriptions);
        Run("invalid tool names return errors", InvalidNames);
        Run("tool calls still enforce coordinate and task constraints", ToolConstraints);
        Run("camera coordinate exception preserves gameplay boundaries", CameraCoordinateRegression.Run);
        Run("tool cache respects visibility after all-tools access", CacheVisibility);
        Run("public catalog preserves schemas and internal operation isolation", MetadataIsolation);
        Run("all static resources dispatch to their registered operation", StaticResources);
        Run("dynamic resource routes preserve read actions and reject sandbox routes", DynamicResources);
        Run("resource templates reject query operation overrides", TemplateConstraints);
        Run("resource errors return valid JSON content", ResourceErrors);
        Run("construction progress preserves observation limits", ConstructionProgressRegression.Run);
        Run("speech Markdown keeps formatting balanced", SpeechTextRegression.Run);
        Console.WriteLine(failures == 0 ? "All 17 core regression groups passed." : failures + " regression groups failed.");
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

    private static void ToolTextEncoding()
    {
        const string json = "{\"items\":[{\"id\":1,\"name\":\"Ada\"},{\"id\":2,\"name\":\"Bob\"}]}";
        McpTextContentEncoder.Configure("toon");
        var internalResult = CallToolResult.Text("{\"ops\":[{\"action\":\"map_edit\",\"result\":{\"applied\":2}}]}");
        var internalPayload = JObject.Parse(internalResult.Content[0].Text);
        Require((int)internalPayload["ops"][0]["result"]["applied"] == 2, "Internal map-edit composition no longer receives parseable JSON text.");
        Require(internalResult.Content[0].Text.StartsWith("{", StringComparison.Ordinal), "TOON was applied before the HTTP response boundary.");

        var response = JsonRpcResponse.Success(7, new JObject
        {
            ["content"] = new JArray(new JObject { ["type"] = "text", ["text"] = json }),
            ["isError"] = false,
            ["structuredContent"] = new JObject { ["items"] = new JArray(1, 2) }
        });
        var wire = JObject.Parse(JsonConvert.SerializeObject(McpTextContentEncoder.FormatJsonRpcResponse(response), McpJsonUtil.Settings));
        Require((string)wire["jsonrpc"] == "2.0" && (int)wire["id"] == 7, "JSON-RPC envelope changed during text encoding.");
        Require((string)wire["result"]["content"][0]["text"] == "items[2]{id,name}:\n  1,Ada\n  2,Bob", "Wire text was not encoded using TOON's tabular array form.");
        Require(wire["result"]["structuredContent"].Type == JTokenType.Object
            && wire["result"]["structuredContent"]["items"].Type == JTokenType.Array,
            "structuredContent JSON types were changed.");

        McpTextContentEncoder.Configure("json");
        var jsonWire = JObject.Parse(JsonConvert.SerializeObject(McpTextContentEncoder.FormatJsonRpcResponse(response), McpJsonUtil.Settings));
        Require((string)jsonWire["result"]["content"][0]["text"] == json, "JSON mode did not preserve the original text.");

        McpTextContentEncoder.Configure("toon");
        const string plainText = "not JSON: leave this readable";
        var plainResponse = JsonRpcResponse.Success(1, CallToolResult.Text(plainText));
        var plainWire = JObject.Parse(JsonConvert.SerializeObject(McpTextContentEncoder.FormatJsonRpcResponse(plainResponse), McpJsonUtil.Settings));
        Require((string)plainWire["result"]["content"][0]["text"] == plainText, "Plain text was changed by the formatter.");
        var errorResponse = JsonRpcResponse.Success(1, CallToolResult.Error(json));
        var errorWire = JObject.Parse(JsonConvert.SerializeObject(McpTextContentEncoder.FormatJsonRpcResponse(errorResponse), McpJsonUtil.Settings));
        Require((bool)errorWire["result"]["isError"] && (string)errorWire["result"]["content"][0]["text"] == "items[2]{id,name}:\n  1,Ada\n  2,Bob",
            "Error envelope or structured text encoding changed.");

        var catalog = JsonRpcResponse.Success(8, new JObject { ["tools"] = new JArray() });
        Require(ReferenceEquals(catalog, McpTextContentEncoder.FormatJsonRpcResponse(catalog)), "Catalog responses were needlessly copied by the text formatter.");
        var markdown = JsonRpcResponse.Success(9, CallToolResult.Text("# Cell\n\nThis is plain Markdown."));
        Require(ReferenceEquals(markdown, McpTextContentEncoder.FormatJsonRpcResponse(markdown)), "Plain Markdown took the structured JSON encoding path.");
        var protocolError = JsonRpcResponse.MakeError(10, -32601, "No such method");
        Require(ReferenceEquals(protocolError, McpTextContentEncoder.FormatJsonRpcResponse(protocolError)), "Protocol errors were unnecessarily converted.");
    }

    private static void NestedResultText()
    {
        var child = McpJsonUtil.ParseResultText("{\"applied\":3,\"date\":\"2026-10-20\",\"remaining\":[1,2]}");
        Require(child.Type == JTokenType.Object && (int)child["applied"] == 3, "Child JSON was escaped as opaque text.");
        Require(child["date"].Type == JTokenType.String, "A JSON string was changed into a date value.");
        Require(McpJsonUtil.ParseResultText("[1,2]").Type == JTokenType.Array, "Child arrays lost their structure.");
        foreach (string text in new[] { "{unfinished", "[1,2] trailing text", "# Ordinary report", "42" })
        {
            var value = McpJsonUtil.ParseResultText(text);
            Require(value.Type == JTokenType.String && (string)value == text, "Invalid or ordinary child text was changed: " + text);
        }
        var nested = new JObject { ["result"] = child };
        McpTextContentEncoder.Configure("toon");
        string toon = McpTextContentEncoder.EncodeIfJson(nested.ToString(Formatting.None));
        Require(toon.Contains("applied: 3") && !toon.Contains("\\\"applied\\\""), "Nested results still contain escaped JSON in TOON output.");
    }

    private static void ToolStateScopes()
    {
        McpToolStateScope sessionScope;
        string error;
        Require(McpToolStateScope.TryResolve(" session-a ", "not-a-handle", out sessionScope, out error),
            "Legacy session scope rejected a caller-provided handle.");
        Require(sessionScope.CacheScopeId == "session:session-a", "Legacy session scope changed.");
        Require(!sessionScope.IsStateless && sessionScope.Handle == null,
            "Legacy session unexpectedly exposed a stateless handle.");

        McpToolStateScope first;
        Require(McpToolStateScope.TryResolve(null, null, out first, out error),
            "Stateless scope did not mint a handle: " + error);
        Require(first.IsStateless && first.Handle != null && first.Handle.Length == 32,
            "Stateless scope returned an invalid handle.");
        Require(first.CacheScopeId == "delta:" + first.Handle,
            "Stateless cache scope did not use the explicit handle.");

        McpToolStateScope resumed;
        Require(McpToolStateScope.TryResolve(null, first.Handle.ToUpperInvariant(), out resumed, out error),
            "Returned stateless handle could not be resumed.");
        Require(resumed.Handle == first.Handle && resumed.CacheScopeId == first.CacheScopeId,
            "Stateless handle did not resolve deterministically.");

        McpToolStateScope second;
        Require(McpToolStateScope.TryResolve(null, null, out second, out error),
            "Second stateless scope did not mint a handle.");
        Require(second.CacheScopeId != first.CacheScopeId,
            "Independent stateless callers shared a state scope.");

        McpToolStateScope invalid;
        Require(!McpToolStateScope.TryResolve(null, "global", out invalid, out error)
            && invalid == null && !string.IsNullOrEmpty(error),
            "Unsafe shared stateless handle was accepted.");
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
        Require(!OniToolRegistry.CallTool("game_control", new JObject { ["task"] = "inspect" }).IsError, "Canonical dispatch failed.");
        int calls = GameStubs.HandlerCalls;
        Require(OniToolRegistry.CallTool("legacy_game_control", new JObject { ["task"] = "inspect" }).IsError
            && GameStubs.HandlerCalls == calls, "Retired alias reached the handler.");
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
        var infos = OniToolRegistry.GetToolInfos();
        var expected = new[] { "benchmark", "building_control", "game_control", "navigation_control", "orders_control", "server_control", "world_editor" };
        Require(infos.Select(info => info.Name).OrderBy(name => name, StringComparer.Ordinal).SequenceEqual(expected),
            "Public catalog membership changed.");
        Require(infos.Select(info => info.Name).SequenceEqual(OniToolRegistry.GetVisibleTools().Select(tool => tool.Name)),
            "Catalog order differs from visible registry order.");
        foreach (string name in new[] { "mcp_server_control", "tools_call_many", "oni_editor", "map_editor", "spatial_control", "view_control", "buildings_control" })
        {
            int calls = GameStubs.HandlerCalls;
            Require(!OniToolRegistry.TryGetTool(name, out _)
                && OniToolRegistry.CallTool(name, new JObject { ["task"] = "inspect" }).IsError
                && GameStubs.HandlerCalls == calls, "Removed tool alias still dispatches: " + name);
        }
        foreach (var info in infos)
        {
            Require(info.InputSchema.Required.Count(name => name == "task") == 1
                && info.InputSchema.Required.Contains("action"), "Required schema fields changed: " + info.Name);
            Require(info.InputSchema.Properties.ContainsKey("x") == (info.Name == "world_editor" || info.Name == "navigation_control"),
                "Coordinate schema boundary changed: " + info.Name);
            Require(info.Execution.TaskSupport == "optional", "Task support changed: " + info.Name);
            if (info.Name != "benchmark" && info.Name != "world_editor")
            {
                Require(info.Description.Contains("Unified "), "Production description was not applied: " + info.Name);
                Require(info.InputSchema.Properties["query"].Description == "Search or filter text.",
                    "Production parameter description was not applied: " + info.Name);
            }
        }
        foreach (var name in new[] { "colony_control", "coordinate_control", "dupes_control", "read_control", "search_control" })
        {
            Require(OniToolRegistry.TryGetOperation(name, out _), "Internal operation was removed: " + name);
            Require(!OniToolRegistry.TryGetTool(name, out _), "Internal operation became public: " + name);
            var args = new JObject { ["task"] = "inspect", ["action"] = "status" };
            int calls = GameStubs.HandlerCalls;
            Require(OniToolRegistry.CallTool(name, args).IsError && GameStubs.HandlerCalls == calls,
                "Public dispatch executed an internal operation: " + name);
            Require(!OniToolRegistry.CallToolFromWorldEditor(name, args, false).IsError
                && GameStubs.HandlerCalls == calls + 1, "World-editor dispatch lost an internal operation: " + name);
        }
        infos.Clear();
        Require(OniToolRegistry.GetToolInfos().Count == expected.Length
            && OniToolRegistry.GetDefaultToolInfoCount() == expected.Length, "Caller removed cached metadata entries.");
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
        Require(OniResourceRegistry.ReadResource("oni://sandbox/cell/6/7") == null,
            "Removed sandbox cell route remained available.");
        Require(!OniResourceRegistry.GetResourceInfos().Any(resource =>
            resource.Uri.StartsWith("oni://sandbox/", StringComparison.OrdinalIgnoreCase)),
            "Removed sandbox resource remained advertised.");
        Require(!OniResourceRegistry.GetResourceTemplateInfos().Any(template =>
            template.UriTemplate.StartsWith("oni://sandbox/", StringComparison.OrdinalIgnoreCase)),
            "Removed sandbox resource template remained advertised.");
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
