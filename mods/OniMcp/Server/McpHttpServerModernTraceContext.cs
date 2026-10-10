using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace OniMcp.Server
{
    public partial class McpHttpServer
    {
        private static bool IsValidModernTraceparent(JToken traceparent)
        {
            if (traceparent?.Type != JTokenType.String)
                return false;

            string value = (string)traceparent;
            if (value.Length < 55
                || value[2] != '-'
                || value[35] != '-'
                || value[52] != '-')
            {
                return false;
            }

            if (!IsLowerHexRange(value, 0, 2, false)
                || (value[0] == 'f' && value[1] == 'f')
                || !IsLowerHexRange(value, 3, 32, true)
                || !IsLowerHexRange(value, 36, 16, true)
                || !IsLowerHexRange(value, 53, 2, false))
            {
                return false;
            }

            bool versionZero = value[0] == '0' && value[1] == '0';
            if (versionZero)
                return value.Length == 55;

            return value.Length == 55 || value[55] == '-';
        }

        private static bool IsValidModernTracestate(JToken tracestate)
        {
            if (tracestate?.Type != JTokenType.String)
                return false;

            string[] members = ((string)tracestate).Split(',');
            if (members.Length > 32)
                return false;

            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (string rawMember in members)
            {
                string member = TrimTraceOws(rawMember);
                if (member.Length == 0)
                    continue;

                int equals = member.IndexOf('=');
                if (equals <= 0 || equals != member.LastIndexOf('='))
                    return false;

                string key = member.Substring(0, equals);
                string value = member.Substring(equals + 1);
                if (!IsValidTracestateKey(key)
                    || !IsValidTracestateValue(value)
                    || !keys.Add(key))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsValidModernBaggage(JToken baggage)
        {
            if (baggage?.Type != JTokenType.String)
                return false;

            string[] members = ((string)baggage).Split(',');
            if (members.Length == 0 || members.Length > 180)
                return false;

            foreach (string rawMember in members)
            {
                string member = TrimTraceOws(rawMember);
                if (member.Length == 0)
                    return false;

                string[] parts = member.Split(';');
                if (!IsValidBaggageKeyValue(parts[0]))
                    return false;

                for (int i = 1; i < parts.Length; i++)
                {
                    if (!IsValidBaggageProperty(parts[i]))
                        return false;
                }
            }

            return true;
        }

        private static bool IsValidBaggageKeyValue(string part)
        {
            int equals = part.IndexOf('=');
            if (equals <= 0)
                return false;

            string key = TrimTraceOws(part.Substring(0, equals));
            string value = TrimTraceOws(part.Substring(equals + 1));
            return IsValidBaggageToken(key) && IsValidBaggageValue(value);
        }

        private static bool IsValidBaggageProperty(string part)
        {
            string property = TrimTraceOws(part);
            if (property.Length == 0)
                return false;

            int equals = property.IndexOf('=');
            if (equals < 0)
                return IsValidBaggageToken(property);
            if (equals == 0)
                return false;

            string key = TrimTraceOws(property.Substring(0, equals));
            string value = TrimTraceOws(property.Substring(equals + 1));
            return IsValidBaggageToken(key) && IsValidBaggageValue(value);
        }

        private static bool IsValidBaggageToken(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            for (int i = 0; i < value.Length; i++)
            {
                if (!IsBaggageTokenCharacter(value[i]))
                    return false;
            }

            return true;
        }

        private static bool IsBaggageTokenCharacter(char value)
        {
            return IsAsciiDigit(value)
                || (value >= 'A' && value <= 'Z')
                || (value >= 'a' && value <= 'z')
                || value == '!'
                || value == '#'
                || value == '$'
                || value == '%'
                || value == '&'
                || value == '\''
                || value == '*'
                || value == '+'
                || value == '-'
                || value == '.'
                || value == '^'
                || value == '_'
                || value == '`'
                || value == '|'
                || value == '~';
        }

        private static bool IsValidBaggageValue(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char current = value[i];
                if (!IsBaggageOctet(current))
                    return false;

                if (current == '%')
                {
                    if (i + 2 >= value.Length
                        || !IsAsciiHex(value[i + 1])
                        || !IsAsciiHex(value[i + 2]))
                    {
                        return false;
                    }
                    i += 2;
                }
            }

            return true;
        }

        private static bool IsBaggageOctet(char value)
        {
            return value == 0x21
                || (value >= 0x23 && value <= 0x2b)
                || (value >= 0x2d && value <= 0x3a)
                || (value >= 0x3c && value <= 0x5b)
                || (value >= 0x5d && value <= 0x7e);
        }

        private static bool IsAsciiHex(char value)
        {
            return (value >= '0' && value <= '9')
                || (value >= 'a' && value <= 'f')
                || (value >= 'A' && value <= 'F');
        }

        private static string TrimTraceOws(string value)
        {
            int start = 0;
            int end = value.Length;
            while (start < end && IsTraceOws(value[start]))
                start++;
            while (end > start && IsTraceOws(value[end - 1]))
                end--;

            return start == 0 && end == value.Length
                ? value
                : value.Substring(start, end - start);
        }

        private static bool IsTraceOws(char value)
        {
            return value == ' ' || value == '\t';
        }

        private static bool IsValidTracestateKey(string key)
        {
            int at = key.IndexOf('@');
            if (at < 0)
                return IsValidTracestateKeyPart(key, 256, false);
            if (at == 0 || at != key.LastIndexOf('@') || at == key.Length - 1)
                return false;

            string tenantId = key.Substring(0, at);
            string systemId = key.Substring(at + 1);
            return IsValidTracestateKeyPart(tenantId, 241, true)
                && IsValidTracestateKeyPart(systemId, 14, false);
        }

        private static bool IsValidTracestateKeyPart(string value, int maxLength, bool firstMayBeDigit)
        {
            if (string.IsNullOrEmpty(value) || value.Length > maxLength)
                return false;

            char first = value[0];
            if (!IsLowerAsciiLetter(first) && (!firstMayBeDigit || !IsAsciiDigit(first)))
                return false;

            for (int i = 1; i < value.Length; i++)
            {
                if (!IsValidTracestateKeyCharacter(value[i]))
                    return false;
            }

            return true;
        }

        private static bool IsValidTracestateKeyCharacter(char value)
        {
            return IsLowerAsciiLetter(value)
                || IsAsciiDigit(value)
                || value == '_'
                || value == '-'
                || value == '*'
                || value == '/';
        }

        private static bool IsLowerAsciiLetter(char value)
        {
            return value >= 'a' && value <= 'z';
        }

        private static bool IsAsciiDigit(char value)
        {
            return value >= '0' && value <= '9';
        }

        private static bool IsValidTracestateValue(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 256)
                return false;

            for (int i = 0; i < value.Length; i++)
            {
                char current = value[i];
                if (current < 0x20 || current > 0x7e || current == ',' || current == '=')
                    return false;
            }

            return value[value.Length - 1] != ' ';
        }

        private static bool IsLowerHexRange(string value, int start, int length, bool rejectAllZero)
        {
            bool hasNonZero = false;
            int end = start + length;
            for (int i = start; i < end; i++)
            {
                char current = value[i];
                bool isHex = (current >= '0' && current <= '9')
                    || (current >= 'a' && current <= 'f');
                if (!isHex)
                    return false;
                if (current != '0')
                    hasNonZero = true;
            }

            return !rejectAllZero || hasNonZero;
        }
    }
}
