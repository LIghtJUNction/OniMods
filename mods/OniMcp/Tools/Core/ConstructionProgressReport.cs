using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    internal sealed class ConstructionProgressRow
    {
        public int Id;
        public int X;
        public int Y;
        public string PrefabId;
        public string MainStatus;
    }

    // Formats observed blueprints, not a prediction of work or task completion.
    internal static class ConstructionProgressReport
    {
        internal const int DefaultLimit = 20;
        internal const int MaximumLimit = 100;
        private const int StatusTextLimit = 180;

        internal static bool TryReadPage(JObject args, out int offset, out int limit, out string error)
        {
            offset = 0;
            limit = DefaultLimit;
            error = null;
            if (!TryReadInteger(args?["offset"], 0, 0, int.MaxValue, out offset))
                error = "offset must be an integer from 0 to 2147483647";
            else if (!TryReadInteger(args?["limit"], DefaultLimit, 1, MaximumLimit, out limit))
                error = "limit must be an integer from 1 to 100";
            return error == null;
        }

        private static bool TryReadInteger(JToken token, int fallback, int min, int max, out int value)
        {
            value = fallback;
            if (token == null)
                return true;
            if (token.Type != JTokenType.Integer)
                return false;
            if (!long.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long number)
                || number < min || number > max)
                return false;
            value = (int)number;
            return true;
        }

        internal static string Unavailable(string reason)
        {
            return "## Construction progress\nobservation=unavailable; reason=" + OneLine(reason)
                + "\nTask completion: not verified. No pending count is available.\n";
        }

        internal static string Render(int worldId, int totalPending, int matchingPending,
            int offset, IList<ConstructionProgressRow> rows)
        {
            if (worldId < 0 || totalPending < 0 || matchingPending < 0 || matchingPending > totalPending
                || offset < 0 || rows == null || rows.Count > MaximumLimit
                || rows.Count > Math.Max(0L, (long)matchingPending - offset)
                || rows.Any(row => row == null))
                throw new ArgumentException("Invalid construction observation page.");

            var sb = new StringBuilder("## Construction progress\n");
            sb.Append("scope=visible active world; worldId=").Append(worldId)
                .Append("; pending=").Append(totalPending)
                .Append("; matching=").Append(matchingPending)
                .Append("; returned=").Append(rows.Count)
                .Append("; offset=").Append(offset).AppendLine();
            sb.AppendLine("All rows are unfinished blueprints. Task completion: not verified.");
            if (totalPending == 0)
                sb.AppendLine("No visible pending blueprints observed. Cancelled or missing plans are not completed buildings.");
            else if (matchingPending == 0)
                sb.AppendLine("No query matches; other pending blueprints still exist.");
            else if (rows.Count == 0)
                sb.AppendLine("This page is empty; retry with offset=0. Pending work still exists.");

            if (rows.Count > 0)
            {
                sb.AppendLine("| id | prefab | cell X,Y | native main status |");
                sb.AppendLine("| --- | --- | --- | --- |");
                foreach (var row in rows)
                {
                    string status = string.IsNullOrWhiteSpace(row.MainStatus) ? "unknown" : OneLine(row.MainStatus);
                    if (status.Length > StatusTextLimit)
                        status = status.Substring(0, StatusTextLimit) + " [truncated]";
                    sb.Append("| ").Append(row.Id).Append(" | ").Append(OneLine(row.PrefabId))
                        .Append(" | ").Append(row.X).Append(',').Append(row.Y)
                        .Append(" | ").Append(status).AppendLine(" |");
                }
            }

            long next = (long)offset + rows.Count;
            if (rows.Count > 0 && next < matchingPending)
                sb.Append("More matches: repeat the same paused read and query with offset=").Append(next).AppendLine(".");
            sb.AppendLine("Main status is not a complete blocker list; unknown does not mean ready.");
            sb.AppendLine("Inspect /active/map/cell_X_Y.md and /active/dupes/reachability.md for blocked work.");
            sb.AppendLine("Verify completed buildings, rooms and operation after a short run; do not repeat unchanged orders.");
            return sb.ToString();
        }

        private static string OneLine(string value)
        {
            return (value ?? "unknown").Replace("\\", "\\\\").Replace("|", "\\|")
                .Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
        }
    }
}
