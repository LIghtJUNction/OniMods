using System;
using System.IO;
using System.Text;

namespace OniMcp.Server
{
    internal static class HttpRequestBody
    {
        internal const int MaxMcpBytes = 16 * 1024 * 1024;
        internal const int MaxSettingsBytes = 64 * 1024;

        internal static string Read(Stream stream, Encoding encoding, long contentLength, int maxBytes)
        {
            if (contentLength > maxBytes)
                throw new RequestBodyTooLargeException();

            using (var body = new MemoryStream())
            {
                var buffer = new byte[8192];
                int count;
                while ((count = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, maxBytes - body.Length + 1))) > 0)
                {
                    if (body.Length + count > maxBytes)
                        throw new RequestBodyTooLargeException();
                    body.Write(buffer, 0, count);
                }
                body.Position = 0;
                using (var reader = new StreamReader(body, encoding))
                    return reader.ReadToEnd();
            }
        }
    }

    internal sealed class RequestBodyTooLargeException : IOException
    {
        internal RequestBodyTooLargeException() : base("Request body exceeds the allowed size.") { }
    }
}
