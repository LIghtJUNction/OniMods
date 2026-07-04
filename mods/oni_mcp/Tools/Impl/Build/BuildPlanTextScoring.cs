using System;
using System.Collections.Generic;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static int ScorePlanValue(string value, string normalizedQuery, int exactScore)
        {
            string normalizedValue = NormalizePlanText(value);
            if (normalizedValue.Length == 0 || normalizedQuery.Length == 0)
                return 0;
            if (normalizedValue == normalizedQuery)
                return exactScore;
            if (normalizedValue.StartsWith(normalizedQuery, StringComparison.Ordinal))
                return exactScore - 80;
            if (normalizedValue.Contains(normalizedQuery))
                return exactScore - 160;
            if (normalizedQuery.Length >= 2 && normalizedQuery.Contains(normalizedValue))
                return exactScore - 220;
            int maxDistance = normalizedQuery.Length <= 4 ? 1 : normalizedQuery.Length <= 8 ? 2 : 3;
            return BoundedPlanEditDistance(normalizedValue, normalizedQuery, maxDistance) >= 0 ? exactScore - 260 : 0;
        }

        private static string NormalizePlanText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            var chars = new List<char>(value.Length);
            foreach (char ch in value)
            {
                if (char.IsLetterOrDigit(ch))
                    chars.Add(char.ToLowerInvariant(ch));
            }
            return new string(chars.ToArray());
        }

        private static int BoundedPlanEditDistance(string left, string right, int maxDistance)
        {
            if (Math.Abs(left.Length - right.Length) > maxDistance)
                return -1;
            int[] previous = new int[right.Length + 1];
            int[] current = new int[right.Length + 1];
            for (int j = 0; j <= right.Length; j++)
                previous[j] = j;
            for (int i = 1; i <= left.Length; i++)
            {
                current[0] = i;
                int rowMin = current[0];
                for (int j = 1; j <= right.Length; j++)
                {
                    int cost = left[i - 1] == right[j - 1] ? 0 : 1;
                    current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
                    rowMin = Math.Min(rowMin, current[j]);
                }
                if (rowMin > maxDistance)
                    return -1;
                var temp = previous;
                previous = current;
                current = temp;
            }
            return previous[right.Length] <= maxDistance ? previous[right.Length] : -1;
        }
    }
}
