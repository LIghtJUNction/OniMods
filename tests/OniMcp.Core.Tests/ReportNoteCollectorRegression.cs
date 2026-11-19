using System;
using System.Collections.Generic;
using OniMcp.Tools;

internal static class ReportNoteCollectorRegression
{
    private static int assertions;

    internal static void Run()
    {
        assertions = 0;
        // The game stores positive and negative contributions separately even
        // when their text is identical. Do not group, sort or filter them.
        var expected = new[]
        {
            new Note(3.25f, "Power"),
            new Note(-8.5f, "Power"),
            new Note(0f, ""),
            new Note(1.75f, "复制人\n备注"),
            new Note(-0.125f, null)
        };
        var legacy = new LegacyEntry { Notes = expected };
        var current = new CurrentEntry { Notes = expected };
        var oldResult = ReportNoteCollector<LegacyEntry, Note>.Read(legacy);
        var newResult = ReportNoteCollector<CurrentEntry, Note>.Read(current);
        Same(expected, oldResult, "U59 callback");
        Same(expected, newResult, "U60 collection");

        legacy.Notes = new Note[0];
        current.Notes = new Note[0];
        Same(new Note[0], ReportNoteCollector<LegacyEntry, Note>.Read(legacy), "U59 empty");
        Same(new Note[0], ReportNoteCollector<CurrentEntry, Note>.Read(current), "U60 empty");
        Same(expected, oldResult, "U59 previous result");
        Same(expected, newResult, "U60 previous result");
        Require(legacy.Calls == 2 && current.Calls == 2, "An entry was skipped or collected twice.");
        Same(new[] { new Note(42f, "Other context") },
            ReportNoteCollector<LegacyEntry, Note>.Read(new LegacyEntry
            {
                Notes = new[] { new Note(42f, "Other context") }
            }), "U59 independent context");
        Same(new[] { new Note(-42f, "Other context") },
            ReportNoteCollector<CurrentEntry, Note>.Read(new CurrentEntry
            {
                Notes = new[] { new Note(-42f, "Other context") }
            }), "U60 independent context");

        Same(expected, ReportNoteCollector<BothEntry, Note>.Read(new BothEntry { Notes = expected }),
            "CollectNotes takes priority");
        var broken = new BrokenEntry();
        try
        {
            ReportNoteCollector<BrokenEntry, Note>.Read(broken);
            throw new Exception("A storage failure became a successful empty report.");
        }
        catch (InvalidOperationException ex)
        {
            Require(ReferenceEquals(ex, broken.Failure), "The underlying storage failure was wrapped or replaced.");
        }

        try
        {
            ReportNoteCollector<UnsupportedEntry, Note>.Read(new UnsupportedEntry());
            throw new Exception("An unknown API became a successful empty report.");
        }
        catch (NotSupportedException ex)
        {
            Require(ex.Message.Contains("CollectNotes") && ex.Message.Contains("IterateNotes"),
                "Unsupported game API did not explain the expected methods.");
        }
        Console.WriteLine("Report notes: " + assertions
            + " assertions passed (legacy/current API, exact values/text/order, empty/context isolation, failure visibility).");
    }

    private static void Same(IReadOnlyList<Note> expected, IReadOnlyList<Note> actual, string scenario)
    {
        Require(actual.Count == expected.Count, scenario + ": note count changed.");
        for (int i = 0; i < expected.Count; i++)
        {
            Require(actual[i].Value == expected[i].Value, scenario + ": value/order changed at " + i + ".");
            Require(actual[i].Text == expected[i].Text, scenario + ": text/order changed at " + i + ".");
        }
    }

    private static void Require(bool condition, string message)
    {
        assertions++;
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private struct Note
    {
        internal readonly float Value;
        internal readonly string Text;
        internal Note(float value, string text) { Value = value; Text = text; }
    }

    private sealed class LegacyEntry
    {
        internal Note[] Notes;
        internal int Calls;
        public void IterateNotes(Action<Note> callback)
        {
            Calls++;
            foreach (var note in Notes)
                callback(note);
        }
    }

    private sealed class CurrentEntry
    {
        internal Note[] Notes;
        internal int Calls;
        public void CollectNotes(List<Note> notes) { Calls++; notes.AddRange(Notes); }
    }

    private sealed class BothEntry
    {
        internal Note[] Notes;
        public void CollectNotes(List<Note> notes) { notes.AddRange(Notes); }
        public void IterateNotes(Action<Note> callback) { throw new Exception("Legacy API should not run when CollectNotes exists."); }
    }

    private sealed class BrokenEntry
    {
        internal readonly InvalidOperationException Failure = new InvalidOperationException("Storage unavailable");
        public void CollectNotes(List<Note> notes) { throw Failure; }
        public void IterateNotes(Action<Note> callback) { throw new Exception("Do not retry a failed collection through another API."); }
    }

    private sealed class UnsupportedEntry { }
}
