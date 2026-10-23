namespace OniOnline.Core
{
    // Losing focus or starting a text edit requires another deliberate press.
    internal sealed class OnlineInputGate
    {
        private bool voicePressed;

        internal void PressVoice(bool allowed) { voicePressed = allowed; }
        internal void ReleaseVoice() { voicePressed = false; }

        internal bool UpdateVoice(bool allowed, bool keyStillHeld)
        {
            if (!allowed || !keyStillHeld) voicePressed = false;
            return voicePressed;
        }
    }
}
