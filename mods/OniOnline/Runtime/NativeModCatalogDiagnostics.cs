using System;
using System.Collections.Generic;
using System.Linq;

namespace OniOnline.Runtime
{
    internal static class NativeModCatalogDiagnostics
    {
        private const double MaximumInitialWaitSeconds = 30;
        private static readonly HashSet<string> Temporary = new HashSet<string>(StringComparer.Ordinal)
        { "native_mod_loader_busy", "native_mod_catalog_changed", "content_source_changed", "content_changed" };

        internal static bool ShouldRetryInitial(NativeModCatalogSnapshot snapshot, double elapsedSeconds)
        {
            if (snapshot == null || double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds) ||
                elapsedSeconds < 0 || elapsedSeconds >= MaximumInitialWaitSeconds) return false;
            var blocking = snapshot.Issues.Where(issue => issue.Blocking).ToList();
            return blocking.Count != 0 && blocking.All(issue => Temporary.Contains(issue.Code));
        }

        // Only curated text and validated mod identities reach the UI, never filesystem/exception details.
        internal static string Summary(NativeModCatalogSnapshot snapshot)
        {
            if (snapshot == null) return "未能读取模组信息。";
            if (snapshot.IsComplete) return "模组已就绪。";
            var issues = snapshot.Issues.Where(issue => issue.Blocking).ToList();
            var codes = new HashSet<string>(issues.Select(issue => issue.Code), StringComparer.Ordinal);
            string message;
            if (codes.Contains("native_mod_identity_ambiguous")) message = "发现重复启用的模组。请在模组菜单检查。";
            else if (codes.Contains("native_mod_restart_required")) message = "模组已更改。请重新启动游戏。";
            else if (codes.Contains("native_mod_not_loaded") || codes.Contains("native_mod_not_installed")) message = "部分模组未能载入。请在模组菜单检查。";
            else if (codes.Contains("content_data_unclassified")) message = "部分模组尚未声明联机内容。";
            else if (codes.Contains("content_link_not_allowed")) message = "部分模组的安装位置无法核验。";
            else if (codes.Any(code => code.EndsWith("_budget", StringComparison.Ordinal))) message = "部分模组超出联机核验范围。";
            else if (codes.All(Temporary.Contains)) message = "模组仍在载入或更新。请稍后重试。";
            else message = "未能核验部分模组。请在模组菜单检查。";
            var ids = issues.Select(issue => SafeId(issue.Id)).Where(id => id.Length != 0).Distinct(StringComparer.Ordinal).Take(3).ToArray();
            return ids.Length == 0 ? message : message + "\n" + string.Join("、", ids);
        }

        private static string SafeId(string id)
        {
            // A mod can supply its own invalid identifier; never echo it as a path or markup.
            if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || id.IndexOf('/') >= 0 || id.IndexOf('\\') >= 0 ||
                id.IndexOf('<') >= 0 || id.IndexOf('>') >= 0 || id.Any(char.IsControl)) return "";
            return id;
        }
    }
}
