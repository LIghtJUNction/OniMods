using System;
using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    internal static class GameplayRequestPolicy
    {
        internal static bool Validate(JToken args, out string error)
        {
            error = null;
            if (args == null)
                return true;
            foreach (var child in args.Children())
            {
                if (child is JProperty property)
                {
                    string key = property.Name;
                    string value = property.Value.Type == JTokenType.String
                        ? property.Value.Value<string>().Trim() : string.Empty;
                    if ((key == "domain" || key == "command" || key == "op")
                        && (value.Equals("sandbox", StringComparison.OrdinalIgnoreCase)
                            || value.Equals("sandbox_tools", StringComparison.OrdinalIgnoreCase)
                            || value.Equals("debug", StringComparison.OrdinalIgnoreCase)))
                    {
                        error = "Only normal gameplay is supported; sandbox/debug routes were removed.";
                        return false;
                    }
                    bool removedFlag = key == "instantBuild" || key == "allowSandbox" || key == "allowForce"
                        || key == "allowTerrainMutation" || key == "allowEntitySpawn" || key == "allowDestroy" || key == "force";
                    if (removedFlag && property.Value.Type != JTokenType.Null
                        && !string.Equals(property.Value.ToString(), "false", StringComparison.OrdinalIgnoreCase))
                    {
                        error = "Unsupported gameplay option: " + key + ". Use native orders and wait for the game to complete them.";
                        return false;
                    }
                    if (!Validate(property.Value, out error))
                        return false;
                }
                else if (!Validate(child, out error))
                    return false;
            }
            return true;
        }
    }
}
