using System;
using System.IO;

namespace OniOnline.Sdk
{
    // Separate save and preference directories for a second local client.
    public static class TestSlotLayout
    {
        public static string Root(string dataRoot, int slot)
        {
            if (slot < 1) throw new ArgumentOutOfRangeException(nameof(slot));
            if (string.IsNullOrWhiteSpace(dataRoot) || dataRoot.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                throw new ArgumentException("invalid_test_slot_root");
            return Path.Combine(dataRoot, "onionline-slot" + slot.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        public static string Saves(string root)
        { return Child(root, "save_files"); }

        public static string Preferences(string root)
        { return Child(root, "playerprefs"); }

        private static string Child(string root, string name)
        {
            if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("invalid_test_slot_root");
            return Path.Combine(root, name);
        }
    }
}
