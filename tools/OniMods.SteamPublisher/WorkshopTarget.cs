using System.Globalization;

internal static class WorkshopTarget
{
    internal static uint AppId => checked((uint)ReadId("ONIM_PUBLISH_APP_ID", 457140));
    internal static ulong WorkshopId =>
        ReadId("ONIM_PUBLISH_WORKSHOP_ID", 3766318556);
    internal static ulong ExpectedOwner =>
        ReadId("ONIM_PUBLISH_EXPECTED_OWNER", 76561199137573787);
    internal static string DisplayName =>
        ReadText("ONIM_PUBLISH_NAME", "CycleTrim");
    internal static string TitleContains =>
        ReadText("ONIM_PUBLISH_TITLE_CONTAINS", "CycleTrim");
    internal static string Url =>
        $"https://steamcommunity.com/sharedfiles/filedetails/?id={WorkshopId}";

    private static ulong ReadId(string name, ulong fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : ulong.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);
    }

    private static string ReadText(string name, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
