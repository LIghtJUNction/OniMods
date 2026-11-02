using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CycleTrim.Conformance.Memory
{
    internal static class MemoryRequest
    {
        internal static JObject Validate(JObject request, string telemetry, int pid, System.DateTime nowUtc)
        {
            JObject status;
            // JToken's DateTime-to-string conversion loses the UTC suffix.
            // Keep native ISO text intact before parsing its explicit zone.
            using (var reader = new JsonTextReader(new StringReader(telemetry)))
            {
                reader.DateParseHandling = DateParseHandling.None;
                status = JObject.Load(reader);
            }
            System.DateTime captured = System.DateTime.Parse((string)status["capturedAt"], CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind).ToUniversalTime();
            double age = (nowUtc - captured).TotalSeconds;
            if ((int?)request["pid"] != pid || (int?)status["pid"] != pid || age < 0 || age > 10
                || (bool?)status["rootVerified"] != true || (bool?)status["gameLoaded"] != true
                || (bool?)status["paused"] != true || string.IsNullOrEmpty((string)status["runNonce"])
                || (string)request["runNonce"] != (string)status["runNonce"]
                || (string)request["identity"] != (string)status["identity"])
                throw new InvalidOperationException("Memory request does not match fresh owned paused telemetry");
            return status;
        }
    }
}
