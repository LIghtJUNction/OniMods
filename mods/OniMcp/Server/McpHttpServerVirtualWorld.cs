using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Config;
using OniMcp.Core;
using OniMcp.Support;
using OniMcp.Tools;
using UnityEngine;

namespace OniMcp.Server
{
    /// <summary>
    /// MCP Streamable HTTP 服务器实现
    /// 基于 System.Net.HttpListener（.NET Framework 内置）
    /// </summary>
    public partial class McpHttpServer : MonoBehaviour
{
        private void HandleGet(HttpListenerRequest request, HttpListenerResponse response, string sessionId)
        {
            if (!AcceptsEventStream(request))
            {
                SendJson(response, JsonRpcResponse.MakeError(null, McpErrorCode.InvalidRequest, "GET requires Accept: text/event-stream for server-initiated MCP messages."), 406);
                return;
            }

            McpSession session;
            lock (_sessionLock)
            {
                if (!_sessions.TryGetValue(sessionId, out session))
                {
                    SendJson(response, JsonRpcResponse.MakeError(null, McpErrorCode.InvalidRequest, "Session not found or terminated"), 404);
                    return;
                }
                session.SseConnections++;
            }

            try
            {
                response.StatusCode = 200;
                response.ContentType = "text/event-stream";
                response.SendChunked = true;
                response.KeepAlive = true;
                response.Headers["Cache-Control"] = "no-cache";

                WriteSseComment(response, "connected");

                while (_running && IsSessionActive(sessionId))
                {
                    JObject message;
                    while ((message = session.TryDequeueOutbound()) != null)
                        WriteSseMessage(response, message);

                    session.OutboundSignal.WaitOne(15000);
                    if (_running && IsSessionActive(sessionId))
                        WriteSseComment(response, "keepalive");
                }
            }
            catch (Exception ex)
            {
                OniMcpLog.Debug($"[OniMcp] SSE stream closed for session {sessionId}: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                lock (_sessionLock)
                {
                    McpSession current;
                    if (_sessions.TryGetValue(sessionId, out current) && current.SseConnections > 0)
                        current.SseConnections--;
                }
                try { response.Close(); } catch { }
            }
        }

        private static bool AcceptsEventStream(HttpListenerRequest request)
        {
            string accept = request.Headers["Accept"] ?? "";
            return accept.IndexOf("text/event-stream", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void HandleDelete(HttpListenerResponse response, string sessionId)
        {
            lock (_sessionLock)
            {
                if (_sessions.Remove(sessionId))
                {
                    sessionId = sessionId ?? "";
                    _terminatedSessions.Add(sessionId);
                }
            }
            response.StatusCode = 204;
            response.Close();
        }

        private static bool IsVirtualWorldPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return true;
            if (path.StartsWith("/mcp", StringComparison.OrdinalIgnoreCase))
                return false;
            if (path.StartsWith("/screenshots/", StringComparison.OrdinalIgnoreCase))
                return false;
            return true;
        }

        private void HandleVirtualWorldRequest(HttpListenerRequest request, HttpListenerResponse response)
        {
            string path = request.Url == null ? "/" : request.Url.AbsolutePath;
            if (path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            {
                string rawHtml = "";
                try
                {
                    rawHtml = MainThreadBridge.Invoke(() => WorldEditorTools.ReadFileDirectly(path));
                }
                catch (Exception ex)
                {
                    rawHtml = $"<h1>Error serving page</h1><p>{ex.Message}</p>";
                }
                SendHtml(response, rawHtml, 200);
                return;
            }

            string format = request.QueryString["format"] ?? string.Empty;
            bool wantsJson = format.Equals("json", StringComparison.OrdinalIgnoreCase)
                || (request.Headers["Accept"] ?? string.Empty).IndexOf("application/json", StringComparison.OrdinalIgnoreCase) >= 0;
            var model = BuildVirtualWorldListing(path);
            if (wantsJson)
            {
                SendJson(response, model, 200);
                return;
            }
            SendHtml(response, RenderVirtualWorldHtml(model), 200);
        }

        private object BuildVirtualWorldListing(string rawPath)
        {
            return MainThreadBridge.Invoke(() => WorldEditorTools.BuildBrowserListing(rawPath, EndpointUrl, CurrentProtocolVersion));
        }

        private string RenderVirtualWorldHtml(object model)
        {
            var json = JObject.FromObject(model);
            var sb = new StringBuilder();
            sb.Append("<!doctype html><html><head><meta charset=\"utf-8\"><title>OniMcp World Files</title>");
            sb.Append("<style>body{font:14px system-ui,sans-serif;margin:24px;line-height:1.45}code,pre{background:#f4f4f4;padding:2px 4px;border-radius:4px;white-space:pre-wrap;word-break:break-all}pre code{background:transparent;padding:0;color:inherit;font:inherit}table{border-collapse:collapse;width:100%;max-width:1100px}td,th{border-bottom:1px solid #ddd;padding:8px;text-align:left}a{text-decoration:none}.dir{font-weight:700}.error-banner{background:#fff0f0;border:1px solid #ffcccc;color:#cc0000;padding:12px;margin:16px 0;border-radius:4px}.back-link{font-size:16px;display:inline-block;margin-bottom:16px;font-weight:bold;color:#0066cc}</style>");
            sb.Append("</head><body>");

            string path = json["path"]?.ToString() ?? "";
            bool isFile = json["isFile"]?.Value<bool>() ?? false;

            if (isFile)
            {
                string parent = json["parent"]?.ToString() ?? "/";
                sb.Append("<h1>OniMcp virtual file view</h1>");
                sb.Append("<a class=\"back-link\" href=\"").Append(WebUtility.HtmlEncode(parent)).Append("\">← Back to directory</a>");
                sb.Append("<p>Path: <code>").Append(WebUtility.HtmlEncode(path)).Append("</code> | JSON: <a href=\"?format=json\">?format=json</a></p>");

                string error = json["error"]?.ToString();
                if (!string.IsNullOrEmpty(error))
                {
                    sb.Append("<div class=\"error-banner\"><strong>Error reading file:</strong><br/>")
                      .Append(WebUtility.HtmlEncode(error))
                      .Append("</div>");
                }
                else
                {
                    string content = json["content"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(content) && (content.TrimStart().StartsWith("{") || content.TrimStart().StartsWith("[")))
                    {
                        try
                        {
                            var parsedJson = JsonConvert.DeserializeObject(content);
                            content = JsonConvert.SerializeObject(parsedJson, Formatting.Indented);
                        }
                        catch {}
                    }

                    var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                    var sbLines = new StringBuilder();
                    for (int i = 0; i < lines.Length; i++)
                    {
                        int lineNum = i + 1;
                        sbLines.AppendFormat("<span style=\"color:#718096;user-select:none;margin-right:12px;text-align:right;display:inline-block;width:30px;\">{0}</span>{1}\n", lineNum, WebUtility.HtmlEncode(lines[i]));
                    }

                    sb.Append("<pre style=\"background:#2d3748;color:#f7fafc;padding:16px;border-radius:8px;overflow-x:auto;font-family:monospace;font-size:13px;max-width:1100px;white-space:pre-wrap;word-break:break-all;\"><code>")
                      .Append(sbLines.ToString())
                      .Append("</code></pre>");
                }
            }
            else
            {
                var entries = (JArray)json["entries"];
                sb.Append("<h1>OniMcp virtual world files</h1>");
                sb.Append("<p><code>/mcp/</code> is the MCP endpoint. This page maps save folders to code-like world files. Edits happen only through SEARCH/REPLACE blocks sent to <code>world_editor command=edit</code>.</p>");
                sb.Append("<p>Path: <code>").Append(WebUtility.HtmlEncode(path)).Append("</code> | JSON: <a href=\"?format=json\">?format=json</a> | Settings: <a href=\"/settings/\">Configure Mod</a></p>");
                sb.Append("<table><thead><tr><th>Name</th><th>Type</th><th>Description</th><th>MCP call</th></tr></thead><tbody>");
                foreach (var entry in entries)
                {
                    string name = entry["name"]?.ToString() ?? "";
                    string type = entry["type"]?.ToString() ?? "";
                    string url = entry["url"]?.ToString() ?? entry["path"]?.ToString() ?? "";
                    string desc = entry["description"]?.ToString() ?? "";
                    string command = type == "dir" ? "ls" : "read";
                    string call = entry["mcpCall"]?.ToString(Formatting.None)
                        ?? new JObject { ["tool"] = "world_editor", ["arguments"] = new JObject { ["command"] = command, ["path"] = url } }.ToString(Formatting.None);
                    sb.Append("<tr><td><a class=\"").Append(type).Append("\" href=\"").Append(WebUtility.HtmlEncode(url)).Append("\">")
                        .Append(WebUtility.HtmlEncode(name)).Append("</a></td><td>")
                        .Append(WebUtility.HtmlEncode(type)).Append("</td><td>")
                        .Append(WebUtility.HtmlEncode(desc)).Append("</td><td><code>")
                        .Append(WebUtility.HtmlEncode(call)).Append("</code></td></tr>");
                }
                sb.Append("</tbody></table>");
            }
            sb.Append("</body></html>");
            return sb.ToString();
        }

        private static string NormalizeVirtualPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "/";
            if (!path.StartsWith("/", StringComparison.Ordinal))
                path = "/" + path;
            if (!path.Contains(".") && !path.EndsWith("/", StringComparison.Ordinal))
                path += "/";
            return path;
        }

        private static void SendHtml(HttpListenerResponse response, string html, int statusCode)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(html ?? string.Empty);
            response.StatusCode = statusCode;
            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes, 0, bytes.Length);
            response.Close();
        }

        private static void HandleScreenshotRequest(HttpListenerRequest request, HttpListenerResponse response)
        {
            if (request.HttpMethod != "GET" && request.HttpMethod != "HEAD")
            {
                response.StatusCode = 405;
                response.Close();
                return;
            }

            string fileName = Path.GetFileName(request.Url.AbsolutePath);
            string path = string.Equals(fileName, "latest.png", StringComparison.OrdinalIgnoreCase)
                ? WorldEditor.LatestScreenshotPath()
                : WorldEditor.ScreenshotPathForFile(fileName);

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                response.StatusCode = 404;
                response.Close();
                return;
            }

            var info = new FileInfo(path);
            response.StatusCode = 200;
            response.ContentType = "image/png";
            response.ContentLength64 = info.Length;
            response.Headers["Cache-Control"] = "no-cache";
            if (request.HttpMethod == "HEAD")
            {
                response.Close();
                return;
            }

            using (var stream = File.OpenRead(path))
            {
                stream.CopyTo(response.OutputStream);
            }
            response.Close();
        }
}
}
