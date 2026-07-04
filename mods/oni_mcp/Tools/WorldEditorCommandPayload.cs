using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Net;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static JObject Child(JObject args, string domain, string action, params (string key, JToken value)[] extras)
        {
            var child = CopyPayload(args);
            child["domain"] = domain;
            child["action"] = action;
            foreach (var item in extras)
                child[item.key] = item.value;
            return child;
        }

        private static JObject CopyPayload(JObject args)
        {
            var result = args?["payload"] as JObject;
            result = result == null ? new JObject() : (JObject)result.DeepClone();
            if (args == null)
                return result;
            foreach (var property in args.Properties())
            {
                if (property.Name == "command" || property.Name == "op" || property.Name == "path" || property.Name == "payload" || property.Name == "content")
                    continue;
                result[property.Name] = property.Value.DeepClone();
            }
            return result;
        }

        private static string InferSearchDomain(string path)
        {
            string relative = SaveRelativePath(path);
            if (relative.StartsWith("resources/", StringComparison.Ordinal))
                return "resources";
            if (relative.StartsWith("buildings/", StringComparison.Ordinal))
                return "buildings";
            if (relative.StartsWith("dupes/", StringComparison.Ordinal))
                return "dupes";
            return "world";
        }

        private static string NormalizeSearchDomain(string domain)
        {
            domain = (domain ?? string.Empty).Trim().ToLowerInvariant();
            if (domain == "building" || domain == "planning" || domain == "build")
                return "buildings";
            if (domain == "dupe" || domain == "duplicants")
                return "dupes";
            if (domain == "resource" || domain == "items")
                return "resources";
            if (domain == "database" || domain == "guide")
                return "knowledge";
            return string.IsNullOrWhiteSpace(domain) ? "world" : domain;
        }

        private static string NormalizePath(string path, string cwd)
        {
            if (string.IsNullOrWhiteSpace(path))
                path = cwd;
            path = path.Trim();
            if (path == "~")
                return "/";
            if (!path.StartsWith("/", StringComparison.Ordinal))
                path = (string.IsNullOrWhiteSpace(cwd) ? "/" : cwd).TrimEnd('/') + "/" + path;
            while (path.Contains("//"))
                path = path.Replace("//", "/");
            if (!path.Contains(".") && !path.EndsWith("/", StringComparison.Ordinal))
                path += "/";
            return path;
        }

        private static string Text(JObject args, params string[] keys)
        {
            if (args == null)
                return string.Empty;
            foreach (var key in keys)
            {
                var value = args[key]?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }
            return string.Empty;
        }

        private static CallToolResult JsonResult(JToken token)
        {
            return CallToolResult.Text(JsonConvert.SerializeObject(token, McpJsonUtil.Settings));
        }

    }
}
