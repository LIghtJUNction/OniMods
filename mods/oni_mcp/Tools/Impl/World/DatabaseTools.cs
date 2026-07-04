using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class DatabaseTools
    {
        private const int MaxContentChars = 8000;
        private static readonly Regex RichTextTagRegex = new Regex("<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex WhitespaceRegex = new Regex("\\s+", RegexOptions.Compiled);

    }
}
