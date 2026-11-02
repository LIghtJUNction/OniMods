using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

// Original synthetic model of the three documented path-list decisions. It
// deliberately includes first-endpoint and reverse-endpoint behavior, rather
// than replacing those decisions with a conventional substring operation.
public sealed class SolidConduitFlow
{
    private readonly List<List<Conduit>> pathList = new List<List<Conduit>>();
    private readonly SOAInfo soaInfo = new SOAInfo();

    public List<List<Conduit>> Paths { get { return pathList; } }
    public SOAInfo GetSOAInfo() { return soaInfo; }
    public void AddPath(List<Conduit> path) { TryAdd(path); }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TryAdd(List<Conduit> new_path)
    {
        foreach (var existing in pathList)
        {
            if (existing.Count < new_path.Count)
                continue;
            var first = existing.FindIndex(rail => rail.idx == new_path[0].idx);
            var last = existing.FindIndex(rail => rail.idx == new_path[new_path.Count - 1].idx);
            if (SpanMatches(existing, new_path, first, last))
                return;
        }

        for (var index = pathList.Count - 1; index >= 0; index--)
            if (pathList[index].Count == 0)
                pathList.RemoveAt(index);
        for (var index = pathList.Count - 1; index >= 0; index--)
        {
            var existing = pathList[index];
            if (new_path.Count < existing.Count)
                continue;
            var first = FirstIndex(new_path, existing[0].idx);
            var last = FirstIndex(new_path, existing[existing.Count - 1].idx);
            if (SpanMatches(new_path, existing, first, last))
                pathList.RemoveAt(index);
        }

        foreach (var existing in pathList)
        {
            for (var index = new_path.Count - 1; index >= 0; index--)
            {
                var new_conduit = new_path[index];
                if (existing.FindIndex(rail => rail.idx == new_conduit.idx) != -1
                    && UnityEngine.Mathf.IsPowerOfTwo(soaInfo.GetPermittedFlowDirections(new_conduit.idx)))
                    new_path.RemoveAt(index);
            }
        }
        pathList.Add(new_path);
    }

    private static int FirstIndex(List<Conduit> path, int key)
    {
        for (var at = 0; at < path.Count; at++)
            if (path[at].idx == key)
                return at;
        return -1;
    }

    private static bool SpanMatches(List<Conduit> containing, List<Conduit> candidate, int first, int last)
    {
        if (first < 0 || last < 0)
            return false;
        for (var offset = 0; offset < last - first; offset++)
            if (containing[first + offset].idx != candidate[offset].idx)
                return false;
        return true;
    }

    public struct Conduit
    {
        public int idx;
        public Conduit(int id) { idx = id; }
    }

    public sealed class SOAInfo
    {
        public readonly int[] Directions = new int[65536];
        public List<int> Reads;
        public Action ObserveRead;
        public int ReadCount;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int GetPermittedFlowDirections(int idx)
        {
            ReadCount++;
            Reads?.Add(idx);
            ObserveRead?.Invoke();
            return Directions[idx];
        }
    }
}

namespace UnityEngine
{
    public static partial class Mathf
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool IsPowerOfTwo(int value)
        {
            return value != 0 && (value & (value - 1)) == 0;
        }
    }
}
