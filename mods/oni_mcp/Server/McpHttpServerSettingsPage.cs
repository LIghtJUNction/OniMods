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
        private bool TryHandleSettingsRequest(HttpListenerRequest request, HttpListenerResponse response)
        {
            string path = request.Url == null ? string.Empty : request.Url.AbsolutePath;
            if (path != "/settings" && path != "/settings/")
                return false;

            var remoteAddress = request.RemoteEndPoint == null ? null : request.RemoteEndPoint.Address;
            if (remoteAddress == null || !IPAddress.IsLoopback(remoteAddress))
            {
                SendHtml(response, "<h1>Forbidden</h1><p>The settings page is available only from the local machine.</p>", 403);
                return true;
            }

            if (request.HttpMethod == "GET")
            {
                SendHtml(response, RenderSettingsHtml(), 200);
                return true;
            }
            if (request.HttpMethod != "POST")
            {
                response.StatusCode = 405;
                response.Close();
                return true;
            }

            try
            {
                Dictionary<string, string> form;
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                    form = ParseQueryString(reader.ReadToEnd());

                var current = OniMcpOptions.Current;
                string host = RequiredFormValue(form, "host");
                if (!IsValidSettingsHost(host))
                    throw new InvalidOperationException("Host must be localhost or an IP address.");
                int port = RequiredFormInt(form, "port", 1024, 65535);
                int retention = RequiredFormInt(form, "screenshotRetentionMinutes", 1, 10080);
                int maxFiles = RequiredFormInt(form, "screenshotMaxFiles", 1, 1000);
                string replacement = form.ContainsKey("authTokenReplacement")
                    ? (form["authTokenReplacement"] ?? string.Empty).Trim()
                    : string.Empty;

                var options = new OniMcpOptions
                {
                    SecurityMigrationVersion = current.SecurityMigrationVersion,
                    Host = host,
                    Port = port,
                    AuthEnabled = form.ContainsKey("authEnabled") && form["authEnabled"] == "true",
                    AuthToken = string.IsNullOrEmpty(replacement) ? current.AuthToken : replacement,
                    GlobalAutoDisinfectDisabled = form.ContainsKey("globalAutoDisinfectDisabled") && form["globalAutoDisinfectDisabled"] == "true",
                    ScreenshotCleanupEnabled = form.ContainsKey("screenshotCleanupEnabled") && form["screenshotCleanupEnabled"] == "true",
                    ScreenshotRetentionMinutes = retention,
                    ScreenshotMaxFiles = maxFiles
                };

                bool portChanged = port != current.Port;
                OniMcpOptions.Save(options);
                string message = portChanged
                    ? "Settings saved. The server is restarting on port " + port + ". Reopen this page at http://localhost:" + port + "/settings/."
                    : "Settings saved. The MCP listener is restarting now.";
                SendHtml(response, RenderSettingsHtml(message, true), 200);
                ScheduleSettingsRestartAfterResponse();
            }
            catch (Exception ex)
            {
                SendHtml(response, RenderSettingsHtml("Error saving settings: " + ex.Message, false), 400);
            }
            return true;
        }

        private static void ScheduleSettingsRestartAfterResponse()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Thread.Sleep(250);
                MainThreadBridge.EnqueueDeferred(() =>
                {
                    if (Instance != null)
                        Instance.RestartServer();
                });
            });
        }

        private static string RequiredFormValue(Dictionary<string, string> form, string key)
        {
            string value = form.ContainsKey(key) ? (form[key] ?? string.Empty).Trim() : string.Empty;
            if (string.IsNullOrEmpty(value))
                throw new InvalidOperationException(key + " is required.");
            return value;
        }

        private static int RequiredFormInt(Dictionary<string, string> form, string key, int minimum, int maximum)
        {
            string value = RequiredFormValue(form, key);
            if (!int.TryParse(value, out int parsed) || parsed < minimum || parsed > maximum)
                throw new InvalidOperationException(key + " must be between " + minimum + " and " + maximum + ".");
            return parsed;
        }

        private static bool IsValidSettingsHost(string host)
        {
            return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
                || IPAddress.TryParse(host, out _);
        }

        private string RenderSettingsHtml(string message = null, bool success = true)
        {
            var options = OniMcpOptions.Current;
            var sb = new StringBuilder();
            sb.Append(@"<!doctype html>
<html>
<head>
<meta charset=""utf-8"">
<title>OniMcp Settings</title>
<style>
  body { font-family: system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, Cantarell, 'Open Sans', 'Helvetica Neue', sans-serif; background: #1a202c; color: #e2e8f0; margin: 0; padding: 40px; }
  .container { max-width: 600px; margin: 0 auto; background: #2d3748; padding: 30px; border-radius: 12px; box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.1), 0 2px 4px -1px rgba(0, 0, 0, 0.06); }
  h1 { margin-top: 0; color: #fff; font-size: 24px; border-bottom: 2px solid #4a5568; padding-bottom: 12px; }
  .form-group { margin-bottom: 20px; }
  label { display: block; margin-bottom: 6px; font-weight: 600; font-size: 14px; color: #cbd5e0; }
  input[type=""text""], input[type=""number""], input[type=""password""] { width: 100%; padding: 10px; border: 1px solid #4a5568; background: #1a202c; color: #fff; border-radius: 6px; box-sizing: border-box; }
  input[type=""text""]:focus, input[type=""number""]:focus, input[type=""password""]:focus { outline: none; border-color: #3182ce; }
  .checkbox-group { display: flex; align-items: center; gap: 8px; margin-top: 10px; }
  .checkbox-group label { display: inline; margin-bottom: 0; cursor: pointer; }
  input[type=""checkbox""] { width: 16px; height: 16px; cursor: pointer; }
  .btn { display: inline-block; background: #3182ce; color: #fff; border: none; padding: 12px 20px; border-radius: 6px; cursor: pointer; font-weight: bold; width: 100%; text-align: center; }
  .btn:hover { background: #2b6cb0; }
  .alert { padding: 12px; border-radius: 6px; margin-bottom: 20px; font-size: 14px; font-weight: bold; }
  .alert-success { background: #c6f6d5; color: #22543d; border: 1px solid #9ae6b4; }
  .alert-error { background: #fed7d7; color: #742a2a; border: 1px solid #feb2b2; }
  .help-text { font-size: 12px; color: #a0aec0; margin-top: 4px; }
  .back-link { display: inline-block; margin-bottom: 20px; color: #63b3ed; text-decoration: none; font-weight: bold; }
</style>
</head>
<body>
  <div class=""container"">
    <a class=""back-link"" href=""/"">← Back to World Files</a>
    <h1>OniMcp Settings</h1>
");
            if (!string.IsNullOrEmpty(message))
            {
                string alertClass = success ? "alert-success" : "alert-error";
                sb.AppendFormat("    <div class=\"alert {0}\">{1}</div>\n", alertClass, WebUtility.HtmlEncode(message));
            }

            sb.AppendFormat(@"    <form method=""POST"" action=""/settings"">
      <div class=""form-group"">
        <label for=""host"">HTTP Listen Host</label>
        <input type=""text"" id=""host"" name=""host"" value=""{0}"" required />
        <div class=""help-text"">Use 'localhost' for local-only, or '0.0.0.0' to allow network connections.</div>
      </div>

      <div class=""form-group"">
        <label for=""port"">HTTP Port</label>
        <input type=""number"" id=""port"" name=""port"" value=""{1}"" min=""1024"" max=""65535"" required />
        <div class=""help-text"">Default: 8788. Changing the port will restart the server.</div>
      </div>

      <div class=""form-group"">
        <div class=""checkbox-group"">
          <input type=""checkbox"" id=""authEnabled"" name=""authEnabled"" value=""true"" {2} />
          <label for=""authEnabled"">Require Authentication Token</label>
        </div>
        <div class=""help-text"">Disabled by default. Authentication is enforced only after you enable this setting manually.</div>
      </div>

      <div class=""form-group"">
        <label for=""authTokenReplacement"">Replace Bearer Auth Token</label>
        <input type=""password"" id=""authTokenReplacement"" name=""authTokenReplacement"" value="""" autocomplete=""new-password"" />
        <div class=""help-text"">The current token is never displayed. Leave blank to keep it, or enter a new token to rotate it.</div>
      </div>

      <div class=""form-group"">
        <div class=""checkbox-group"">
          <input type=""checkbox"" id=""globalAutoDisinfectDisabled"" name=""globalAutoDisinfectDisabled"" value=""true"" {3} />
          <label for=""globalAutoDisinfectDisabled"">Disable Auto Disinfect Globally</label>
        </div>
        <div class=""help-text"">Keeps global auto disinfect disabled to prioritize duplicants.</div>
      </div>

      <div class=""form-group"">
        <div class=""checkbox-group"">
          <input type=""checkbox"" id=""screenshotCleanupEnabled"" name=""screenshotCleanupEnabled"" value=""true"" {4} />
          <label for=""screenshotCleanupEnabled"">Clean Up Old Screenshots</label>
        </div>
      </div>

      <div class=""form-group"">
        <label for=""screenshotRetentionMinutes"">Screenshot Retention (Minutes)</label>
        <input type=""number"" id=""screenshotRetentionMinutes"" name=""screenshotRetentionMinutes"" value=""{5}"" min=""1"" required />
      </div>

      <div class=""form-group"">
        <label for=""screenshotMaxFiles"">Max Screenshot Files</label>
        <input type=""number"" id=""screenshotMaxFiles"" name=""screenshotMaxFiles"" value=""{6}"" min=""1"" required />
      </div>

      <button type=""submit"" class=""btn"">Save Settings</button>
    </form>
  </div>
</body>
</html>",
                WebUtility.HtmlEncode(options.Host),
                options.Port,
                options.AuthEnabled ? "checked" : "",
                options.GlobalAutoDisinfectDisabled ? "checked" : "",
                options.ScreenshotCleanupEnabled ? "checked" : "",
                options.ScreenshotRetentionMinutes,
                options.ScreenshotMaxFiles
            );

            return sb.ToString();
        }
    }
}
