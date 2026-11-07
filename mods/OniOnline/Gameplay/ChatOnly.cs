using System.Collections.Generic;
using OniOnline.Sdk;

namespace OniOnline.Gameplay
{
    // The only gameplay bundled with the framework. Lobby and chat stay available
    // without this class; it exists so a session has a rules id and refuses planet admission.
    public sealed class ChatOnly : IGameplayRules
    {
        public string Id { get { return GameplayFramework.BuiltInId; } }
        public string Version { get { return "1"; } }
        public string ValidatePlanetAdmission(PlanetClaim candidate, IReadOnlyList<PlanetClaim> current)
        { return "chat_only"; }
    }
}
