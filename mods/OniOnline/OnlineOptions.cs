using PeterHan.PLib.Options;

namespace OniOnline
{
    [ConfigFile("OniOnlineOptions.json", true)]
    public sealed class OnlineOptions
    {
        [Option("STRINGS.ONIONLINE.OPTIONS.PREFERSTEAM.NAME", "STRINGS.ONIONLINE.OPTIONS.PREFERSTEAM.DESC")]
        [RestartRequired]
        public bool PreferSteamLobbies { get; set; } = true;
    }
}
