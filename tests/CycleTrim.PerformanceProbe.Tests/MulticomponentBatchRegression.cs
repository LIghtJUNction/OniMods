using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using CycleTrim.Core;
using Jlib.Multicomponent;
using JAccess = Jlib.Multicomponent.MulticomponentCellAccess;
using static CycleTrim.PerformanceProbe.Tests.MulticomponentBatchFixture;

namespace CycleTrim.PerformanceProbe.Tests
{
    internal static class MulticomponentBatchRegression
    {
        internal static void Run()
        {
            BoundariesAndLegacyParity();
            RejectsInvalidDescriptorsAndOversizedSpans();
            IsolatesInvalidCellsAndPreservesFallback();
            OwnsCopiedPublicationAfterRelease();
            SupportsNestedReadersAndRejectsPrematureDispose();
            ReleasesOnExceptionalPaths();
            SeparatesConcurrentReaders();
            ReleasesReferencesAndReusesWarmScratch();
            VisualPrefixMatchesSingleCellProvider();
            VisualPrefixCopiesConsecutiveRunsAndRechecksGrid();
            Console.WriteLine("PASS multicomponent batch boundaries, fallback, publication ownership, reentrancy, concurrency, allocation and production Prefix pixels");
        }

        private static void BoundariesAndLegacyParity()
        {
            using (var fixture = new MulticomponentBatchFixture())
            {
                foreach (int count in new[] { 0, -1, 129, int.MaxValue })
                    Check(!fixture.Provider.TryGetCellBatch(0, count, out var invalid) && invalid == null,
                        "unsupported count declines before acquisition");
                Check(!fixture.Provider.TryGetCellBatch(-1, 1, out _), "negative first cell");
                Check(fixture.Acquires == 0, "invalid arguments do not acquire");
                foreach (int count in new[] { 1, 127, 128 })
                {
                    Check(fixture.Provider.TryGetCellBatch(0, count, out var batch), "supported batch size");
                    using (batch)
                    {
                        for (int cell = 0; cell < count; cell++)
                        {
                            Check(fixture.Provider.TryGetCell(cell, out var oldValue), "legacy read succeeds");
                            Check(batch.TryGetCell(cell, out var newValue), "batch read succeeds");
                            EqualSnapshot(oldValue, newValue);
                        }
                        Check(!batch.TryGetCell(-1, out _) && !batch.TryGetCell(count, out _),
                            "batch rejects outside its copied range");
                        Check(!batch.TryGetCell(int.MinValue, out _) && !batch.TryGetCell(int.MaxValue, out _),
                            "range checks cannot wrap");
                    }
                    Check(!batch.TryGetCell(0, out _), "disposed batch rejects reads");
                    batch.Dispose();
                }
                Check(fixture.Provider.TryGetCellBatch(255, 1, out var last), "last cell is readable");
                using (last) Check(last.TryGetCell(255, out _), "last copied cell succeeds");
                Check(!fixture.Provider.TryGetCellBatch(255, 2, out _), "range beyond final cell declines");
                Check(!fixture.Provider.TryGetCellBatch(int.MaxValue, 128, out _), "large first cell declines safely");
                Check(fixture.Provider.TryGetCellBatch(0, 1, out var first), "first independent reader");
                Check(fixture.Provider.TryGetCellBatch(0, 1, out var second), "second independent reader");
                Check(!ReferenceEquals(first, second), "idempotent dispose never double-returns scratch");
                first.Dispose(); second.Dispose();
            }
            using (var empty = new MulticomponentBatchFixture(1, 0))
            {
                empty.Header.Species = IntPtr.Zero;
                empty.PublishHeader();
                Check(empty.Provider.TryGetCellBatch(0, 1, out var batch), "empty species stream is supported");
                using (batch)
                {
                    Check(batch.TryGetCell(0, out var value) && value.ComponentCount == 0,
                        "zero-record cell is preserved");
                }
            }
        }

        private static void RejectsInvalidDescriptorsAndOversizedSpans()
        {
            using (var fixture = new MulticomponentBatchFixture(4))
            {
                var good = fixture.Header;
                for (int fault = 0; fault < 8; fault++)
                {
                    fixture.Header = good;
                    switch (fault)
                    {
                        case 0: fixture.Header.Version++; break;
                        case 1: fixture.Header.Flags = 1; break;
                        case 2: fixture.Header.Generation = 0; break;
                        case 3: fixture.Header.Cells = IntPtr.Zero; break;
                        case 4: fixture.Header.Species = IntPtr.Zero; break;
                        case 5: fixture.Header.Cells = new IntPtr(long.MaxValue - 8); break;
                        case 6: fixture.Header.Species = new IntPtr(long.MaxValue - 8); break;
                        case 7: fixture.Header.Cells = new IntPtr(-1); break;
                    }
                    fixture.PublishHeader();
                    int before = fixture.Releases;
                    Check(!fixture.Provider.TryGetCellBatch(0, 1, out var batch) && batch == null,
                        "invalid descriptor declines");
                    Check(fixture.Releases == before + 1, "failed descriptor releases exactly once");
                }
                fixture.Header = good; fixture.PublishHeader();
                Check(fixture.Provider.TryGetCellBatch(0, 1, out var restored), "failed scratch can be reused");
                restored.Dispose();
            }
            using (var fixture = new MulticomponentBatchFixture(1, 4097))
            {
                Check(!fixture.Provider.TryGetCellBatch(0, 1, out _), "oversized batch span declines");
                Check(fixture.Provider.TryGetCell(0, out var value) && value.ComponentCount == 4097,
                    "legacy fallback does not truncate unusually many species");
                Check(value.Components[4096].Mass == 1f, "final legacy species remains present");
            }
            var unavailable = new OniSimMulticomponentProvider(() => IntPtr.Zero,
                () => throw new InvalidOperationException("A missing lease must not be released."));
            Check(!unavailable.TryGetCellBatch(0, 1, out _), "unavailable publication releases no nonexistent lease");
        }

        private static void IsolatesInvalidCellsAndPreservesFallback()
        {
            using (var fixture = new MulticomponentBatchFixture(4))
            {
                fixture.Cells[0].SpeciesOffset = uint.MaxValue;
                fixture.Cells[1].SpeciesOffset = 6; // Legal, non-monotonic offsets.
                fixture.Cells[2].SpeciesCount = 0;
                fixture.Cells[2].Reserved = 0;
                fixture.Cells[2].TotalMass = 0f;
                fixture.Cells[3].SpeciesOffset = 0;
                Check(fixture.Provider.TryGetCellBatch(0, 4, out var batch), "bad cell does not poison batch");
                using (batch)
                {
                    Check(!batch.TryGetCell(0, out _), "bad offset declines only its cell");
                    foreach (int cell in new[] { 1, 2, 3 })
                    {
                        Check(fixture.Provider.TryGetCell(cell, out var oldValue), "legacy neighboring cell");
                        Check(batch.TryGetCell(cell, out var newValue), "valid neighbor survives");
                        EqualSnapshot(oldValue, newValue);
                    }
                }
                fixture.Cells[0].SpeciesOffset = 0;
                var goodCell = fixture.Cells[0];
                var goodSpecies = fixture.Species[0];
                WithProvider(fixture.Provider, () =>
                {
                    for (int fault = 0; fault < 9; fault++)
                    {
                        fixture.Cells[0] = goodCell; fixture.Species[0] = goodSpecies;
                        switch (fault)
                        {
                            case 0: fixture.Cells[0].Reserved = 3; break;
                            case 1: fixture.Species[0].Reserved = 1; break;
                            case 2: fixture.Species[0].Phase = 9; break;
                            case 3: fixture.Species[0].Mass = 0f; break;
                            case 4: fixture.Species[0].Mass = float.NaN; break;
                            case 5: fixture.Species[0].Temperature = float.PositiveInfinity; break;
                            case 6: fixture.Species[0].VolumeFraction = 2f; break;
                            case 7: fixture.Cells[0].TotalMass = 99f; break;
                            case 8: fixture.Species[0].ElementId = ushort.MaxValue; break;
                        }
                        Check(JAccess.TryGetCellBatch(0, 2, out var invalid), "invalid-cell copy remains bounded");
                        using (invalid)
                        {
                            int acquired = fixture.Acquires;
                            Check(!JAccess.TryGetCell(invalid, 0, out _), "bad snapshot falls back and remains invalid");
                            Check(fixture.Acquires == acquired + 1, "invalid batch cell retries legacy provider once");
                            Check(JAccess.TryGetCell(invalid, 1, out _), "valid adjacent cell remains readable");
                            Check(fixture.Acquires == acquired + 1, "valid adjacent cell needs no new native lease");
                        }
                    }
                });
            }
        }

        private static void OwnsCopiedPublicationAfterRelease()
        {
            using (var fixture = new MulticomponentBatchFixture(2))
            {
                fixture.AfterRelease = () =>
                {
                    Array.Clear(fixture.Cells); Array.Clear(fixture.Species);
                    fixture.Header.Generation++;
                    fixture.PublishHeader();
                };
                Check(fixture.Provider.TryGetCellBatch(0, 2, out var batch), "publication copied before release");
                fixture.AfterRelease = null;
                using (batch)
                {
                    for (int cell = 0; cell < 2; cell++)
                        Check(batch.TryGetCell(cell, out var value) && value.ComponentCount == 2
                            && value.TotalMass == 2f && value.Components[1].Mass == 1f,
                            "release-side mutation cannot alter the owned publication copy");
                }
                Check(fixture.Provider.TryGetCellBatch(0, 2, out var next), "new publication is copied afresh");
                using (next)
                    Check(next.TryGetCell(0, out var value) && value.ComponentCount == 0,
                        "changed generation uses the current publication");
                fixture.Cells[0].SpeciesCount = 1;
                fixture.Cells[0].Reserved = 1;
                fixture.Cells[0].TotalMass = 3f;
                fixture.Species[0].Phase = 1;
                fixture.Species[0].Mass = 3f;
                fixture.Species[0].Temperature = 300f;
                fixture.Species[0].VolumeFraction = 1f;
                Check(fixture.Provider.TryGetCellBatch(0, 1, out var sameGeneration), "same session generation is reread");
                using (sameGeneration)
                    Check(sameGeneration.TryGetCell(0, out var value) && value.ComponentCount == 1
                        && value.TotalMass == 3f, "generation is not used as a cross-frame cache");
            }
        }

        private static void SupportsNestedReadersAndRejectsPrematureDispose()
        {
            using (var fixture = new MulticomponentBatchFixture(4))
            {
                Check(fixture.Provider.TryGetCellBatch(0, 4, out var outer), "outer batch");
                bool nested = false;
                int callbacks = 0;
                fixture.BeforeResolve = id =>
                {
                    if (nested) return;
                    Check(fixture.Depth.Value == 0, "batch element resolver runs after native release");
                    nested = true;
                    try
                    {
                        callbacks++;
                        Check(!outer.TryGetCell(0, out _), "same-batch recursive read is rejected");
                        Throws<InvalidOperationException>(() => outer.Dispose(), "dispose cannot repool a reader in resolution");
                        Check(fixture.Provider.TryGetCell(3, out var oldValue) && oldValue.ComponentCount == 2,
                            "resolver can reenter the legacy provider after release");
                        Check(fixture.Provider.TryGetCellBatch(2, 2, out var inner), "nested independent batch");
                        using (inner)
                            Check(inner.TryGetCell(2, out var value) && value.ComponentCount == 2,
                                "nested batch resolves its own data");
                    }
                    finally { nested = false; }
                };
                using (outer)
                {
                    Check(outer.TryGetCell(0, out var value) && value.ComponentCount == 2
                        && ReferenceEquals(value.Components[0].Element, fixture.Elements[0])
                        && ReferenceEquals(value.Components[1].Element, fixture.Elements[1]),
                        "nested reads preserve outer component order");
                    Check(outer.TryGetCell(3, out value) && value.TotalMass == 2f,
                        "nested reads preserve remaining outer cells");
                }
                Check(callbacks == 4, "both species in both outer cells resolve");
            }
        }

        private static void ReleasesOnExceptionalPaths()
        {
            using (var fixture = new MulticomponentBatchFixture(2))
            {
                fixture.AfterRelease = () => throw new ApplicationException("release hook");
                Throws<ApplicationException>(() => fixture.Provider.TryGetCellBatch(0, 2, out _),
                    "release exception propagates after returning scratch");
                fixture.AfterRelease = null;
                Check(fixture.Acquires == 1 && fixture.Releases == 1, "failed release is not attempted twice");
                Check(fixture.Provider.TryGetCellBatch(0, 2, out var batch), "scratch recovers from release exception");
                using (batch)
                {
                    fixture.BeforeResolve = id => throw new ApplicationException("resolver hook");
                    Throws<ApplicationException>(() => batch.TryGetCell(0, out _), "resolver exception propagates");
                    fixture.BeforeResolve = null;
                    Check(batch.TryGetCell(1, out _), "reader recovers after resolver exception");
                }
                Check(fixture.Acquires == fixture.Releases, "resolver exception retains no native lease");
            }
            var broken = new FailedBatchProvider();
            WithProvider(broken, () =>
            {
                Check(!JAccess.TryGetCellBatch(0, 1, out var batch) && batch == null,
                    "provider exception clears the returned batch");
                Check(broken.Disposals == 1, "partially returned provider batch is disposed once");
            });
        }

        private static void SeparatesConcurrentReaders()
        {
            using (var fixture = new MulticomponentBatchFixture(128))
            {
                Check(fixture.Provider.TryGetCellBatch(0, 1, out var owned), "thread-owned batch");
                Check(!Task.Run(() => owned.TryGetCell(0, out _)).GetAwaiter().GetResult(),
                    "another thread cannot read borrowed scratch");
                Task.Run(() => Throws<InvalidOperationException>(() => owned.Dispose(),
                    "another thread cannot return borrowed scratch")).GetAwaiter().GetResult();
                owned.Dispose();
                Task.WaitAll(Enumerable.Range(0, 4).Select(worker => Task.Run(() =>
                {
                    for (int attempt = 0; attempt < 8; attempt++)
                    {
                        Check(fixture.Provider.TryGetCellBatch(0, 128, out var batch), "concurrent batch copy");
                        using (batch)
                        {
                            Check(batch.TryGetCell(worker, out var first) && first.ComponentCount == 2,
                                "concurrent reader first cell");
                            Check(batch.TryGetCell(127 - worker, out var last) && last.TotalMass == 2f,
                                "concurrent reader last cell");
                        }
                    }
                })).ToArray());
            }
        }

        private static void ReleasesReferencesAndReusesWarmScratch()
        {
            WeakReference[] references = MakeReleasedReferences();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Check(references.All(reference => !reference.IsAlive), "pooled scratch retains neither provider nor resolved elements");
            using (var fixture = new MulticomponentBatchFixture(128))
            {
                Action read = () =>
                {
                    Check(fixture.Provider.TryGetCellBatch(0, 128, out var batch), "warm copy");
                    using (batch) Check(batch.TryGetCell(127, out _), "warm resolve");
                };
                for (int i = 0; i < 32; i++) read();
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 64; i++) read();
                Check(GC.GetAllocatedBytesForCurrentThread() == before,
                    "warm repeated batch copy/read/dispose allocates no managed objects");
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference[] MakeReleasedReferences()
        {
            using (var fixture = new MulticomponentBatchFixture(1))
            {
                var references = new[] { new WeakReference(fixture.Provider), new WeakReference(fixture.Elements[0]) };
                Check(fixture.Provider.TryGetCellBatch(0, 1, out var batch), "lifetime batch");
                using (batch) Check(batch.TryGetCell(0, out _), "lifetime resolution");
                return references;
            }
        }

        private static void VisualPrefixMatchesSingleCellProvider()
        {
            int oldWidth = Grid.WidthInCells, oldCount = Grid.CellCount;
            Element[] oldElements = Grid.Element;
            bool[] oldActive = Grid.Active;
            bool oldEnabled = JAccess.Enabled;
            try
            {
                using (var fixture = new MulticomponentBatchFixture(300))
                {
                    ConfigureGrid(150, 2, fixture.Elements[0]);
                    var baseline = new TextureRegion(150, 2);
                    var candidate = new TextureRegion(150, 2);
                    int start = fixture.Acquires;
                    RenderFrozenWithProvider(fixture.Provider, baseline, 149, 1);
                    Check(fixture.Acquires - start == 301, "old provider acquires once per cell plus IsReady");
                    start = fixture.Acquires;
                    candidate.BeforeWrite = (x, y) => Check(fixture.Depth.Value == 0, "SetBytes runs after release");
                    RenderWithProvider(fixture.Provider, candidate, 149, 1);
                    Check(fixture.Acquires - start == 5, "batch acquires once per 128-cell row chunk plus IsReady");
                    EqualPixels(baseline, candidate);
                    var compatibilityControl = new TextureRegion(150, 2);
                    RenderWithProvider(new SingleCellProvider(fixture.Provider), compatibilityControl, 149, 1);
                    EqualPixels(baseline, compatibilityControl);

                    // Mixed, single, invalid, solid, liquid-surface, top-row
                    // liquid, inactive and end-of-chunk cells share one region.
                    fixture.Cells[0].SpeciesCount = 1; fixture.Cells[0].TotalMass = 1f;
                    fixture.Cells[1].Reserved = 3;
                    Grid.Element[2] = new Element { substance = fixture.Elements[0].substance };
                    Grid.Element[3] = new Element { IsLiquid = true, substance = fixture.Elements[0].substance };
                    Grid.Element[153] = Grid.Element[3];
                    fixture.Species[6].Phase = 2;
                    fixture.Cells[3].LiquidVolumeFraction = 0.5f;
                    fixture.Cells[3].LiquidPressureKPa = 100f;
                    fixture.Species[10].Phase = 3;
                    Grid.Active = Enumerable.Repeat(true, 300).ToArray();
                    Grid.Active[127] = false;
                    baseline = new TextureRegion(150, 2); candidate = new TextureRegion(150, 2);
                    RenderFrozenWithProvider(fixture.Provider, baseline, 149, 1);
                    RenderWithProvider(fixture.Provider, candidate, 149, 1);
                    EqualPixels(baseline, candidate);

                    WithProvider(fixture.Provider, () =>
                    {
                        var nested = new TextureRegion(150, 2);
                        bool inside = false;
                        candidate = new TextureRegion(150, 2);
                        candidate.BeforeWrite = (x, y) =>
                        {
                            Check(fixture.Depth.Value == 0, "nested draw has no held native lease");
                            if (!inside && x == 0 && y == 0)
                            {
                                inside = true;
                                Check(!Render(nested, 0, 0, 149, 1), "nested Prefix completes");
                            }
                        };
                        Check(!Render(candidate, 0, 0, 149, 1), "outer Prefix completes");
                        EqualPixels(baseline, candidate); EqualPixels(baseline, nested);
                        candidate.BeforeWrite = (x, y) => throw new ApplicationException("draw hook");
                        Throws<ApplicationException>(() => Render(candidate, 0, 0, 149, 1), "draw failure leaves through finally");
                        candidate = new TextureRegion(150, 2);
                        Check(!Render(candidate, 0, 0, 149, 1), "Prefix recovers from draw failure");
                        EqualPixels(baseline, candidate);
                        candidate = new TextureRegion(150, 2);
                        Check(!Render(candidate, 1, 1, 0, 0) && candidate.Writes == 0, "empty rectangle writes no pixels");
                        Check(!Render(candidate, 149, 1, 149, 1) && candidate.Writes == 1, "single final pixel is supported");

                        fixture.Header.Version++;
                        fixture.PublishHeader();
                        candidate = new TextureRegion(150, 2);
                        Check(Render(candidate, 0, 0, 149, 1) && candidate.Writes == 0,
                            "unready provider retains original renderer without writes");
                        fixture.Header.Version--;
                        fixture.PublishHeader();
                        Grid.Active = new bool[300];
                        int before = fixture.Acquires;
                        Check(!Render(candidate, 0, 0, 149, 1) && candidate.Writes == 300,
                            "inactive region is still cleared");
                        Check(fixture.Acquires == before + 1, "no eligible cells needs only readiness lease");
                    });
                    JAccess.Enabled = true;
                    try { Check(Render(new TextureRegion(150, 2), 0, 0, 149, 1), "no provider retains original renderer"); }
                    finally { JAccess.Enabled = false; }
                }
            }
            finally
            {
                Grid.WidthInCells = oldWidth; Grid.CellCount = oldCount;
                Grid.Element = oldElements; Grid.Active = oldActive;
                JAccess.Enabled = oldEnabled;
            }
        }

        internal static void WithProvider(IMulticomponentCellProvider provider, Action action)
        {
            Check(JAccess.RegisterProvider(provider), "exclusive fixture provider registration");
            bool previous = JAccess.Enabled;
            JAccess.Enabled = true;
            try { action(); }
            finally { JAccess.Enabled = previous; Check(JAccess.UnregisterProvider(provider), "provider unregistered"); }
        }

        private static void VisualPrefixCopiesConsecutiveRunsAndRechecksGrid()
        {
            int oldWidth = Grid.WidthInCells, oldCount = Grid.CellCount;
            Element[] oldElements = Grid.Element;
            bool[] oldActive = Grid.Active;
            try
            {
                using (var fixture = new MulticomponentBatchFixture(266))
                {
                    ConfigureGrid(133, 2, fixture.Elements[0]);
                    var solid = new Element { substance = fixture.Elements[0].substance };
                    foreach (int cell in new[] { 1, 4, 132, 261 }) Grid.Element[cell] = solid;
                    Grid.Active = Enumerable.Repeat(true, 266).ToArray();
                    Grid.Active[64] = false;
                    var oldPixels = new TextureRegion(133, 2);
                    var newPixels = new TextureRegion(133, 2);
                    int before = fixture.Acquires;
                    RenderFrozenWithProvider(fixture.Provider, oldPixels, 132, 1);
                    Check(fixture.Acquires - before == 262, "frozen renderer reads all 261 eligible cells");
                    var recorder = new RecordingBatchProvider(fixture.Provider);
                    before = fixture.Acquires;
                    RenderWithProvider(recorder, newPixels, 132, 1);
                    Check(fixture.Acquires - before == 7, "five copied runs and one isolated read plus readiness");
                    Check(recorder.Requests.SequenceEqual(new[] { (2, 2), (5, 59), (65, 67), (133, 128), (262, 4) }),
                        "batch ranges stop at solids, inactive gaps, 128 cells and row ends");
                    EqualPixels(oldPixels, newPixels);
                }
                using (var fixture = new MulticomponentBatchFixture(258))
                {
                    ConfigureGrid(129, 2, fixture.Elements[0]);
                    var oldPixels = new TextureRegion(129, 2);
                    var newPixels = new TextureRegion(129, 2);
                    RenderFrozenWithProvider(fixture.Provider, oldPixels, 128, 1);
                    var recorder = new RecordingBatchProvider(fixture.Provider);
                    int before = fixture.Acquires;
                    RenderWithProvider(recorder, newPixels, 128, 1);
                    Check(fixture.Acquires - before == 5, "129-cell rows use a batch plus a single final cell");
                    Check(recorder.Requests.SequenceEqual(new[] { (0, 128), (129, 128) }),
                        "a one-cell row tail never joins the next row");
                    EqualPixels(oldPixels, newPixels);
                }
                using (var fixture = new MulticomponentBatchFixture(16))
                {
                    var solid = new Element { substance = fixture.Elements[0].substance };
                    Action setup = () =>
                    {
                        ConfigureGrid(8, 2, fixture.Elements[0]);
                        Grid.Active = Enumerable.Repeat(true, 16).ToArray();
                        Grid.Element[5] = solid;
                    };
                    Action<int, int> changeGridAfterFirstPixel = (x, y) =>
                    {
                        if (x != 0 || y != 0) return;
                        Grid.Element[1] = solid;
                        Grid.Active[2] = false;
                        Grid.Element[5] = fixture.Elements[0];
                    };
                    var oldPixels = new TextureRegion(8, 2) { BeforeWrite = changeGridAfterFirstPixel };
                    var newPixels = new TextureRegion(8, 2) { BeforeWrite = changeGridAfterFirstPixel };
                    setup();
                    RenderFrozenWithProvider(fixture.Provider, oldPixels, 7, 1);
                    setup();
                    RenderWithProvider(fixture.Provider, newPixels, 7, 1);
                    EqualPixels(oldPixels, newPixels);
                    Check(newPixels.Bytes[4 + 3] == 0 && newPixels.Bytes[8 + 3] == 0
                        && newPixels.Bytes[20 + 3] == 255,
                        "consumption rechecks hinted cells and a newly eligible following gap");
                }
            }
            finally
            {
                Grid.WidthInCells = oldWidth; Grid.CellCount = oldCount;
                Grid.Element = oldElements; Grid.Active = oldActive;
            }
        }

        private static void RenderWithProvider(IMulticomponentCellProvider provider, TextureRegion region, int x1, int y1)
        {
            WithProvider(provider, () => Check(!Render(region, 0, 0, x1, y1), "production visual Prefix handles ready provider"));
        }

        private static void RenderFrozenWithProvider(IMulticomponentCellProvider provider, TextureRegion region, int x1, int y1)
        {
            WithProvider(provider, () => Check(!FrozenMulticomponentVisualPrefix.Prefix(region, 0, 0, x1, y1),
                "frozen production visual Prefix handles ready provider"));
        }

        internal static void EqualPixels(TextureRegion expected, TextureRegion actual)
        {
            Check(expected.Writes == actual.Writes && expected.Bytes.SequenceEqual(actual.Bytes),
                "complete pixel bytes and write count match single-cell provider");
        }

        private static void EqualSnapshot(MulticomponentCellSnapshot left, MulticomponentCellSnapshot right)
        {
            Check(left.ComponentCount == right.ComponentCount && left.TotalMass == right.TotalMass
                && left.GasPressureKPa == right.GasPressureKPa && left.LiquidPressureKPa == right.LiquidPressureKPa
                && left.LiquidVolumeFraction == right.LiquidVolumeFraction, "snapshot header equality");
            for (int i = 0; i < left.ComponentCount; i++)
            {
                var a = left.Components[i]; var b = right.Components[i];
                Check(ReferenceEquals(a.Element, b.Element) && a.Phase == b.Phase && a.Mass == b.Mass
                    && a.Temperature == b.Temperature && a.VolumeFraction == b.VolumeFraction
                    && a.PartialPressureKPa == b.PartialPressureKPa, "ordered species equality");
            }
        }

        private static void Throws<T>(Action action, string message) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException("Multicomponent batch: " + message);
        }

        private sealed class FailedBatchProvider : IMulticomponentCellProvider,
            IMulticomponentCellBatchProvider, IMulticomponentCellBatch
        {
            internal int Disposals;
            public bool IsReady => true;
            public bool TryGetCell(int cell, out MulticomponentCellSnapshot snapshot) { snapshot = default; return false; }
            public bool TryGetCellBatch(int firstCell, int count, out IMulticomponentCellBatch batch)
            {
                batch = this;
                throw new ApplicationException("batch provider hook");
            }
            public void Dispose() { Disposals++; }
        }

        private sealed class RecordingBatchProvider : IMulticomponentCellProvider, IMulticomponentCellBatchProvider
        {
            private readonly OniSimMulticomponentProvider inner;
            internal readonly List<(int First, int Count)> Requests = new List<(int, int)>();
            internal RecordingBatchProvider(OniSimMulticomponentProvider inner) { this.inner = inner; }
            public bool IsReady => inner.IsReady;
            public bool TryGetCell(int cell, out MulticomponentCellSnapshot snapshot) => inner.TryGetCell(cell, out snapshot);
            public bool TryGetCellBatch(int firstCell, int count, out IMulticomponentCellBatch batch)
            {
                Requests.Add((firstCell, count));
                return inner.TryGetCellBatch(firstCell, count, out batch);
            }
        }
    }
}
