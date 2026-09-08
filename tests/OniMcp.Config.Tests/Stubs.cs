using System;
using System.Collections.Generic;

// Only the game / PLib boundary is stubbed. Tests compile the production config code.
namespace OniMcp.Support
{
    public static class OniMcpPaths
    {
        public static string ConfigPath { get; set; }
        public static string ModPath { get; set; }
    }

    public static class OniMcpLog
    {
        public static void Warning(string message) { }
        public static void Debug(string message) { }
    }
}

namespace OniMcp.Server
{
    public sealed class McpHttpServer
    {
        public static McpHttpServer Instance { get; set; }
        public void RestartServer() { }
    }
}

namespace UnityEngine
{
    public static class Application
    {
        public static void OpenURL(string url) { }
    }
}

namespace PeterHan.PLib.Options
{
    public interface IOptions
    {
        IEnumerable<IOptionsEntry> CreateOptions();
        void OnOptionsChanged();
    }

    public interface IOptionsEntry { }

    public sealed class ConfigFileAttribute : Attribute
    {
        public ConfigFileAttribute(string filename, bool shared) { }
    }

    public sealed class ModInfoAttribute : Attribute
    {
        public ModInfoAttribute(string url, string image) { }
    }

    public sealed class OptionAttribute : Attribute
    {
        public OptionAttribute(string title, string tooltip, string category) { }
    }

    public sealed class TextBlockOptionsEntry : IOptionsEntry
    {
        public TextBlockOptionsEntry(string name, OptionAttribute option) { }
    }

    public sealed class ButtonOptionsEntry : IOptionsEntry
    {
        public object Value { get; set; }
        public ButtonOptionsEntry(string name, OptionAttribute option) { }
    }
}
