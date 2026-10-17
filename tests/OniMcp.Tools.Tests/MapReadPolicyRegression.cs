using System;
using Newtonsoft.Json.Linq;
using OniMcp.Tools;

internal static class MapReadPolicyRegression
{
    internal static void Run()
    {
        int assertions = 0;
        Action<bool, string> check = (ok, message) =>
        {
            assertions++;
            if (!ok) throw new InvalidOperationException(message);
        };
        string expected = "*→←─↓┌┐┬↑└┘┴│├┤┼";
        for (int mask = 0; mask < 16; mask++)
        {
            check(MapTextReadPolicy.ConnectionGlyph(true, mask) == expected[mask], "native direction mask " + mask);
            check(MapTextReadPolicy.ConnectionGlyph(false, mask) == '?', "unknown must never become a line");
        }
        check(MapTextReadPolicy.ConnectionGlyph(true, 16) == '?', "invalid connection bits");
        for (int x = -1; x <= 4; x++)
            for (int y = -1; y <= 3; y++)
                check(MapTextReadPolicy.Inside(x, y, 4, 3) == (x >= 0 && y >= 0 && x < 4 && y < 3), "coordinate boundary");
        foreach (bool valid in new[] { false, true })
            foreach (bool visible in new[] { false, true })
                foreach (int cellWorld in new[] { -1, 0, 1 })
                    foreach (int activeWorld in new[] { -1, 0, 1 })
                        check(MapTextReadPolicy.CanRead(valid, visible, cellWorld, activeWorld)
                            == (valid && visible && activeWorld >= 0 && cellWorld == activeWorld), "world visibility boundary");
        foreach (string key in new[] { "instantBuild", "allowSandbox", "force", "allowForce", "allowDestroy", "allowTerrainMutation", "allowEntitySpawn" })
        {
            foreach (JToken value in new JToken[] { true, "true", 1, "invalid" })
            {
                var request = new JObject { ["steps"] = new JArray(new JObject { ["payload"] = new JObject { [key] = value } }) };
                check(!GameplayRequestPolicy.Validate(request, out _), "nested bypass flag must fail before execution");
            }
            check(GameplayRequestPolicy.Validate(new JObject { [key] = false }, out _), "explicit false is not a bypass");
        }
        foreach (string route in new[] { "sandbox", " DEBUG ", "sandbox_tools" })
            check(!GameplayRequestPolicy.Validate(new JObject { ["steps"] = new JArray(new JObject { ["command"] = route }) }, out _), "nested removed route");
        check(GameplayRequestPolicy.Validate(new JObject { ["command"] = "read", ["taskDescription"] = "inspect sandbox documentation" }, out _), "plain task text is not an executable bypass");
        Console.WriteLine("PASS: " + assertions + " production map-read and gameplay-policy assertions");
    }
}
