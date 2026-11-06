using System.IO;
using OniOnline.Core;

namespace OniOnline.Runtime
{
    internal static class OnlineText
    {
        private static TextCatalog catalog;

        internal static void Load(string directory)
        {
            catalog = new TextCatalog(File.ReadAllLines(Path.Combine(directory, "localization", "menu-draft.csv")));
        }

        internal static string Get(string key)
        {
            return catalog == null ? key : catalog.Get(key, Localization.GetLocale()?.Code ?? Localization.GetCurrentLanguageCode());
        }

        internal static string DefaultSystemName(string playerName)
        {
            if (!Network.Protocol.ValidText(playerName, 32)) playerName = "Player";
            return string.Format(Get("system.default_name"), playerName);
        }
    }
}
