using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace CycleTrim.Core
{
    // Save metadata belongs to a CLR type, never to a component instance. Weak
    // keys also let collectible mod assemblies go away with their type metadata.
    internal static class SaveTypeMetadataCache
    {
        private static readonly Type RuntimeTypeClass = typeof(object).GetType();
        private static readonly ConditionalWeakTable<Type, Metadata> Entries =
            new ConditionalWeakTable<Type, Metadata>();
        private static readonly ConditionalWeakTable<Type, Metadata>.CreateValueCallback Create =
            type => new Metadata(type);

        private sealed class Metadata
        {
            internal readonly bool Skip;
            internal readonly string Name;

            internal Metadata(Type type)
            {
                Skip = type.IsDefined(typeof(SkipSaveFileSerialization), false);
                Name = type.ToString();
            }
        }

        internal static bool IsDefined(MemberInfo member, Type attribute, bool inherit)
        {
            var type = member as Type;
            if (!ReferenceEquals(attribute, typeof(SkipSaveFileSerialization)) || inherit
                || !IsRuntimeType(type))
            {
                // Preserve other queries, custom reflection implementations and
                // their exceptions. A TypeBuilder's metadata can still change.
                return member.IsDefined(attribute, inherit);
            }

            return Entries.GetValue(type, Create).Skip;
        }

        internal static string TypeName(object value)
        {
            var type = value as Type;
            return IsRuntimeType(type) ? Entries.GetValue(type, Create).Name : value.ToString();
        }

        private static bool IsRuntimeType(Type type)
        {
            return type != null && type.GetType() == RuntimeTypeClass;
        }
    }
}
