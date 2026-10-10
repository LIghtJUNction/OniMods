using System;
using OniMcp.Tools;

namespace OniMcp.Server
{
    public partial class McpHttpServer
    {
        private static bool IsKnownModernResourceAuthority(Uri requestedUri)
        {
            if (requestedUri == null)
                return false;

            foreach (var resource in OniResourceRegistry.GetResourceInfos())
            {
                if (resource == null || string.IsNullOrEmpty(resource.Uri)
                    || !IsModernReadOnlyResourceUri(resource.Uri))
                    continue;

                Uri resourceUri;
                if (Uri.TryCreate(resource.Uri, UriKind.Absolute, out resourceUri)
                    && string.Equals(resourceUri.Scheme, requestedUri.Scheme, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(resourceUri.Host, requestedUri.Host, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            foreach (var template in OniResourceRegistry.GetResourceTemplateInfos())
            {
                if (template == null || string.IsNullOrEmpty(template.UriTemplate)
                    || !IsModernReadOnlyResourceTemplate(template.UriTemplate))
                    continue;

                string scheme;
                string host;
                if (TryGetResourceTemplateAuthority(template.UriTemplate, out scheme, out host)
                    && string.Equals(scheme, requestedUri.Scheme, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(host, requestedUri.Host, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool TryGetResourceTemplateAuthority(string uriTemplate, out string scheme, out string host)
        {
            scheme = null;
            host = null;
            if (string.IsNullOrEmpty(uriTemplate))
                return false;

            int separator = uriTemplate.IndexOf("://", StringComparison.Ordinal);
            if (separator <= 0)
                return false;

            int authorityStart = separator + 3;
            int authorityEnd = uriTemplate.Length;
            for (int index = authorityStart; index < uriTemplate.Length; index++)
            {
                char value = uriTemplate[index];
                if (value == '/' || value == '{' || value == '?' || value == '#')
                {
                    authorityEnd = index;
                    break;
                }
            }

            if (authorityEnd <= authorityStart)
                return false;

            scheme = uriTemplate.Substring(0, separator);
            host = uriTemplate.Substring(authorityStart, authorityEnd - authorityStart);
            return host.IndexOf('{') < 0 && host.IndexOf('}') < 0;
        }
    }
}
