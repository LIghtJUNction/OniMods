using System;
using System.Collections.Generic;
using System.Reflection;

namespace OniMcp.Tools
{
    // U60 collects notes into a list; earlier builds expose a callback instead.
    // Resolve once without referencing either version-specific method in game IL.
    internal static class ReportNoteCollector<TEntry, TNote> where TEntry : class
    {
        private static readonly Action<TEntry, List<TNote>> Collect = Bind();

        internal static List<TNote> Read(TEntry entry)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));

            var notes = new List<TNote>();
            Collect(entry, notes);
            return notes;
        }

        private static Action<TEntry, List<TNote>> Bind()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
            var collect = typeof(TEntry).GetMethod("CollectNotes", flags, null,
                new[] { typeof(List<TNote>) }, null);
            if (collect != null && collect.ReturnType == typeof(void))
                return (Action<TEntry, List<TNote>>)Delegate.CreateDelegate(
                    typeof(Action<TEntry, List<TNote>>), collect);

            var iterate = typeof(TEntry).GetMethod("IterateNotes", flags, null,
                new[] { typeof(Action<TNote>) }, null);
            if (iterate != null && iterate.ReturnType == typeof(void))
            {
                var callback = (Action<TEntry, Action<TNote>>)Delegate.CreateDelegate(
                    typeof(Action<TEntry, Action<TNote>>), iterate);
                return (entry, notes) => callback(entry, notes.Add);
            }

            // An unknown game API must be visible, not look like an empty report.
            return (entry, notes) =>
            {
                throw new NotSupportedException("Cannot read report notes: " + typeof(TEntry).FullName
                    + " exposes neither CollectNotes(List<Note>) nor IterateNotes(Action<Note>).");
            };
        }
    }
}
