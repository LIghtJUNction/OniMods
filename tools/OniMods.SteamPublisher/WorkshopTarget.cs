using System.Globalization;

internal static class WorkshopTarget
{
    internal static uint CreatorAppId =>
        checked((uint)ReadId("ONIM_PUBLISH_CREATOR_APP_ID", 636750));
    internal static uint ConsumerAppId =>
        checked((uint)ReadId("ONIM_PUBLISH_CONSUMER_APP_ID", 457140));
    private static ulong? currentOwner;
    internal static ulong WorkshopId => ReadId("ONIM_PUBLISH_WORKSHOP_ID", 0);
    internal static ulong? ConfiguredOwner => ReadOptionalId("ONIM_PUBLISH_EXPECTED_OWNER");
    internal static ulong ExpectedOwner => ConfiguredOwner ?? currentOwner
        ?? throw new InvalidOperationException("Steam owner is available only after account validation");
    internal static string DisplayName => ReadRequiredText("ONIM_PUBLISH_NAME");
    internal static string StaticId => ReadRequiredText("ONIM_PUBLISH_STATIC_ID");
    internal static string TitleContains =>
        ReadOptionalText("ONIM_PUBLISH_TITLE_CONTAINS") ?? DisplayName;
    internal static string Url =>
        $"https://steamcommunity.com/sharedfiles/filedetails/?id={WorkshopId}";

    internal static void SetCurrentOwner(ulong steamId) => currentOwner = steamId;

    private static ulong ReadId(string name, ulong fallback) =>
        ReadOptionalId(name) ?? fallback;

    private static ulong? ReadOptionalId(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null
            : ulong.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);
    }

    private static string ReadRequiredText(string name) =>
        ReadOptionalText(name) ?? throw new InvalidOperationException($"Set {name} to the selected mod name");

    private static string? ReadOptionalText(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
