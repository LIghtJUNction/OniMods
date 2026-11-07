using PeterHan.PLib.Options;

namespace OniMcp.Config
{
    /// <summary>Keeps the editable bearer token out of the visible options dialog.</summary>
    public sealed class MaskedTokenOptionsEntry : MaskedStringOptionsEntry
    {
        public MaskedTokenOptionsEntry(string field)
            : base(field, new OptionAttribute(
                "Token",
                "Used only when Require token is enabled. Edit here or open OniMcpConfig.json to copy it.",
                "Security"))
        {
        }
    }
}
