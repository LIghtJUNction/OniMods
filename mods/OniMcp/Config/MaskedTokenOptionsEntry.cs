using System;
using PeterHan.PLib.Options;
using TMPro;
using UnityEngine;

namespace OniMcp.Config
{
    /// <summary>Keeps the editable bearer token out of the visible options dialog.</summary>
    public sealed class MaskedTokenOptionsEntry : StringOptionsEntry
    {
        public MaskedTokenOptionsEntry(string field)
            : base(field, new OptionAttribute(
                "Token",
                "Used only when Require token is enabled. Edit here or open OniMcpConfig.json to copy it.",
                "Security"))
        {
        }

        public override GameObject GetUIComponent()
        {
            GameObject field = base.GetUIComponent();
            TMP_InputField input = field.GetComponentInChildren<TMP_InputField>();
            if (input == null)
                throw new InvalidOperationException("PLib did not create the token input field.");

            input.contentType = TMP_InputField.ContentType.Password;
            input.ForceLabelUpdate();
            return field;
        }
    }
}
