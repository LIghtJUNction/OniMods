using System;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace CycleTrim.Conformance.Memory
{
    internal static class MemoryRequestTests
    {
        private static int checks;
        private static void Require(bool condition, string message)
        {
            checks++;
            if (!condition) throw new Exception(message);
        }
        private static void Reject(JObject request, JObject status, DateTime now)
        {
            bool rejected = false;
            try { MemoryRequest.Validate(request, status.ToString(), 123, now); }
            catch (Exception) { rejected = true; }
            Require(rejected, "invalid telemetry accepted");
        }
        public static int Main()
        {
            string oldZone = Environment.GetEnvironmentVariable("TZ");
            var oldCulture = CultureInfo.CurrentCulture;
            try
            {
                foreach (string zone in new[] { "UTC", "Asia/Taipei", "America/Los_Angeles" })
                {
                    Environment.SetEnvironmentVariable("TZ", zone);
                    TimeZoneInfo.ClearCachedData();
                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                    DateTime now = new DateTime(2026, 10, 21, 6, 49, 16, DateTimeKind.Utc).AddTicks(1234567);
                    var request = new JObject { ["pid"] = 123, ["identity"] = "owned", ["runNonce"] = "run" };
                    var status = new JObject {
                        ["pid"] = 123, ["identity"] = "owned", ["runNonce"] = "run",
                        ["rootVerified"] = true, ["gameLoaded"] = true, ["paused"] = true,
                        ["capturedAt"] = now.AddSeconds(-1).ToString("o", CultureInfo.InvariantCulture)
                    };
                    string json = status.ToString();
                    var automatic = JObject.Parse(json);
                    Require(automatic["capturedAt"].Type == JTokenType.Date, "legacy premise changed");
                    DateTime legacy = DateTime.Parse((string)automatic["capturedAt"], CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind).ToUniversalTime();
                    if (zone == "Asia/Taipei")
                        Require((now - legacy).TotalSeconds > 10, "did not reproduce UTC suffix loss");
                    var accepted = MemoryRequest.Validate(request, json, 123, now);
                    Require(accepted["capturedAt"].Type == JTokenType.String, "ISO timestamp was converted");
                    Require((string)accepted["identity"] == "owned", "identity changed");
                    foreach (double age in new[] { -1.0, 10.001, 28800.0 })
                    {
                        var changed = (JObject)status.DeepClone();
                        changed["capturedAt"] = now.AddSeconds(-age).ToString("o", CultureInfo.InvariantCulture);
                        Reject(request, changed, now);
                    }
                    foreach (string key in new[] { "rootVerified", "gameLoaded", "paused" })
                    {
                        var changed = (JObject)status.DeepClone(); changed[key] = false;
                        Reject(request, changed, now);
                    }
                    foreach (string key in new[] { "pid", "identity", "runNonce" })
                    {
                        var changed = (JObject)request.DeepClone();
                        changed[key] = key == "pid" ? JToken.FromObject(124) : JToken.FromObject("wrong");
                        Reject(changed, status, now);
                    }
                    var offset = (JObject)status.DeepClone();
                    offset["capturedAt"] = new DateTimeOffset(now.AddSeconds(-1)).ToOffset(TimeSpan.FromHours(8)).ToString("o");
                    Require(MemoryRequest.Validate(request, offset.ToString(), 123, now) != null, "explicit offset rejected");
                    var malformed = (JObject)status.DeepClone(); malformed["capturedAt"] = "invalid";
                    Reject(request, malformed, now);
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("TZ", oldZone);
                TimeZoneInfo.ClearCachedData();
                CultureInfo.CurrentCulture = oldCulture;
            }
            Console.WriteLine("Memory request protocol: " + checks + " checks passed in three time zones");
            return 0;
        }
    }
}
