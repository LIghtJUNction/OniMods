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

        internal sealed class Metadata
        {
            internal readonly bool Skip;
            internal readonly string Name;

            internal Metadata(Type type)
            {
                Skip = type.IsDefined(typeof(SkipSaveFileSerialization), false);
                Name = type.ToString();
            }
        }

        // One cursor lives in a SaveWithoutTransform stack frame. It only keeps
        // the most recently queried type while that save is running; nested
        // saves and other threads cannot replace it or retain it afterwards.
        internal struct LocalCache
        {
            internal Type Type;
            internal Metadata Metadata;
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

        internal static bool IsDefined(MemberInfo member, Type attribute, bool inherit,
            ref LocalCache local)
        {
            var type = member as Type;
            if (!ReferenceEquals(attribute, typeof(SkipSaveFileSerialization)) || inherit
                || !IsRuntimeType(type))
                return member.IsDefined(attribute, inherit);

            return GetMetadata(type, ref local).Skip;
        }

        internal static string TypeName(object value, ref LocalCache local)
        {
            var type = value as Type;
            return IsRuntimeType(type) ? GetMetadata(type, ref local).Name : value.ToString();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Metadata GetMetadata(Type type, ref LocalCache local)
        {
            if (ReferenceEquals(local.Type, type))
                return local.Metadata;

            var metadata = Entries.GetValue(type, Create);
            // A failed metadata lookup must leave the previous pair intact.
            local.Metadata = metadata;
            local.Type = type;
            return metadata;
        }

        private static bool IsRuntimeType(Type type)
        {
            return type != null && type.GetType() == RuntimeTypeClass;
        }
    }
}
