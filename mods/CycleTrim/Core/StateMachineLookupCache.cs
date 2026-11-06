using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using LinqExpression = System.Linq.Expressions.Expression;

namespace CycleTrim.Core
{
    // A list is the weak key. Values contain indices, never controllers, lists
    // or state-machine instances, so removing an instance does not retain it.
    internal static class StateMachineLookupCache
    {
        internal const int SmallListLimit = 4;
        internal const int MaximumAnswers = 16;
        private static readonly Type RuntimeType = typeof(object).GetType();
        private static readonly ConditionalWeakTable<List<StateMachine.Instance>, Answers> Tables =
            new ConditionalWeakTable<List<StateMachine.Instance>, Answers>();
        private static readonly ConditionalWeakTable<List<StateMachine.Instance>, Answers>.CreateValueCallback
            CreateAnswers = list => new Answers();
        private static Func<List<StateMachine.Instance>, int> versionOf;
        private static int installingThread;
        private static volatile bool enabled;
        private static bool disabled;

        internal sealed class Answers
        {
            internal int Version;
            internal Type FirstType;
            internal int FirstIndex;
            internal Dictionary<Type, int> Others;
        }

        internal struct ReadState
        {
            internal List<StateMachine.Instance> List;
            internal Answers Table;
            internal int Version;
        }

        internal static bool Enabled { get { return enabled; } }
        internal static bool Disabled { get { return disabled; } }

        internal static bool BeginInstallation()
        {
            if (disabled) return false;
            // ONI installs this opt-in patch on its main thread. Workers keep
            // the game's search and never read or populate these tables.
            installingThread = Thread.CurrentThread.ManagedThreadId;
            var field = typeof(List<StateMachine.Instance>).GetField("_version",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null || field.FieldType != typeof(int)) return false;
            try
            {
                var list = LinqExpression.Parameter(typeof(List<StateMachine.Instance>), "list");
                versionOf = LinqExpression.Lambda<Func<List<StateMachine.Instance>, int>>(
                    LinqExpression.Field(list, field), list).Compile();
                return true;
            }
            catch (Exception)
            {
                // A runtime that cannot expose the version keeps the original
                // lookup. Never install a cache with incomplete invalidation.
                versionOf = null;
                return false;
            }
        }

        internal static void Enable()
        {
            enabled = !disabled && versionOf != null;
        }

        internal static void Disable()
        {
            enabled = false;
            disabled = true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool TryGet(List<StateMachine.Instance> list, Type type,
            out StateMachine.Instance result, out ReadState state)
        {
            result = null;
            state = default(ReadState);
            if (!enabled || Thread.CurrentThread.ManagedThreadId != installingThread
                || list == null || list.Count <= SmallListLimit
                || ReferenceEquals(type, null) || type.GetType() != RuntimeType)
                return false;

            var table = Tables.GetValue(list, CreateAnswers);
            int version = versionOf(list);
            if (table.Version != version)
            {
                table.FirstType = null;
                table.Others?.Clear();
                table.Version = version;
            }
            state = new ReadState { List = list, Table = table, Version = version };
            int index;
            if (ReferenceEquals(table.FirstType, type)) index = table.FirstIndex;
            else if (table.Others == null || !table.Others.TryGetValue(type, out index)) return false;
            result = index < 0 ? null : list[index];
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static StateMachine.Instance Remember(ReadState state,
            List<StateMachine.Instance> currentList, Type type, int index, StateMachine.Instance result)
        {
            if (state.Table == null || !enabled
                || !ReferenceEquals(state.List, currentList)
                || versionOf(currentList) != state.Version
                || state.Table.Version != state.Version)
                return result;

            // One query needs no dictionary. Additional distinct types share a
            // bounded table; overflow never evicts useful earlier answers.
            var table = state.Table;
            int answer = result == null ? -1 : index;
            if (table.FirstType == null || ReferenceEquals(table.FirstType, type))
            {
                table.FirstType = type;
                table.FirstIndex = answer;
            }
            else
            {
                if (table.Others == null) table.Others = new Dictionary<Type, int>(MaximumAnswers - 1);
                if (table.Others.Count < MaximumAnswers - 1) table.Others[type] = answer;
            }
            return result;
        }
    }
}
