using System;
using System.Reflection;
using System.Text;
using OniMcp.Server;

internal static class ModernNameEncodingRegression
{
    private static void Main()
    {
        VerifyNameEncodingBoundary();
        ModernContentTypeRegression.Verify();
        typeof(Program).GetMethod("Main", BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, null);
    }

    private static void VerifyNameEncodingBoundary()
    {
        var decoder = typeof(McpHttpServer).GetMethod("TryDecodeModernNameHeaderValue",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert(decoder != null,
            "Modern Mcp-Name validation has no dedicated safe encoding boundary");

        object[] rawUnicode = { "Hello, 世界", null };
        Assert(!(bool)decoder.Invoke(null, rawUnicode),
            "Raw non-ASCII Mcp-Name was accepted instead of requiring MCP Base64 encoding");

        string encoded = "=?base64?" + Convert.ToBase64String(Encoding.UTF8.GetBytes("Hello, 世界")) + "?=";
        object[] encodedUnicode = { encoded, null };
        Assert((bool)decoder.Invoke(null, encodedUnicode),
            "Base64-encoded UTF-8 Mcp-Name was rejected");
        Assert(string.Equals((string)encodedUnicode[1], "Hello, 世界", StringComparison.Ordinal),
            "Base64-encoded Mcp-Name did not decode to the original value");

        object[] ascii = { "benchmark", null };
        Assert((bool)decoder.Invoke(null, ascii)
            && string.Equals((string)ascii[1], "benchmark", StringComparison.Ordinal),
            "Safe plain-ASCII Mcp-Name no longer passes unchanged");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
