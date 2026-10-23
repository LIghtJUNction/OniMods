using System;

namespace OniMcp.Tools
{
    internal sealed class NewGameOpeningPolicy
    {
        private float readySince = -1f;

        internal static bool IsInformationalModDifference(string screenType, string title,
            string expectedTitle, bool hasActions, bool deactivatesOnConfirm)
        {
            return screenType == "ConfirmDialogScreen"
                && !string.IsNullOrEmpty(expectedTitle)
                && string.Equals(title, expectedTitle, StringComparison.Ordinal)
                && !hasActions && deactivatesOnConfirm;
        }

        internal bool ObserveReady(float now, bool baseCreated, int liveDuplicants,
            bool gameStarted, bool openingScreenVisible, bool confirmationVisible)
        {
            if (!baseCreated || liveDuplicants != 3 || !gameStarted
                || openingScreenVisible || confirmationVisible)
            {
                readySince = -1f;
                return false;
            }
            if (readySince < 0f)
                readySince = now;
            // WattsonMessage restores the HUD on a 0.5-second UI scheduler delay.
            return now - readySince >= 0.6f;
        }
    }
}
