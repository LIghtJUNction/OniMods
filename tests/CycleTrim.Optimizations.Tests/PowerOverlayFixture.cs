using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

// Minimal original host fixtures. They expose the call signatures used by the
// production patch; they are neither game DLLs nor copies of the game methods.
public interface IEnergyConsumer { float WattsNeededWhenActive { get; } }
public interface IBridgedNetworkItem
{
    int GetNetworkCell();
    bool IsConnectedToNetworks(HashSet<int> networks);
}

public interface ICircuitConnected
{
    bool IsVirtual { get; }
    int PowerCell { get; }
    object VirtualCircuitKey { get; }
}

public class EnergyConsumer : IEnergyConsumer
{
    public float Watts;
    public float WattsNeededWhenActive
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        get { return Watts; }
    }
}

public sealed class SyntheticWire : IBridgedNetworkItem
{
    public int GetNetworkCell() { return 0; }
    public bool IsConnectedToNetworks(HashSet<int> networks) { return false; }
}

public sealed class Generator : ICircuitConnected
{
    public float WattageRating { get { return 1f * Efficiency; } }
    private float Efficiency { get { return 1f; } }
    public bool Virtual;
    public int Cell;
    public object VirtualKey;
    public bool IsVirtual
    {
        [MethodImpl(MethodImplOptions.NoInlining)] get { return Virtual; }
    }
    public int PowerCell
    {
        [MethodImpl(MethodImplOptions.NoInlining)] get { return Cell; }
    }
    public object VirtualCircuitKey
    {
        [MethodImpl(MethodImplOptions.NoInlining)] get { return VirtualKey; }
    }
    public ushort CircuitID
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        get { return Game.Instance.circuitManager.GetCircuitID(this); }
    }
}

// Original, deliberately small routing fixtures expose the actual dependency
// signatures. They do not model the game's electrical simulation or its graph.
public sealed class Game
{
    [ThreadStatic] private static Game current;
    public static Game Instance
    {
        [MethodImpl(MethodImplOptions.NoInlining)] get { return current; }
    }
    internal static void SetInstanceForTest(Game instance) { current = instance; }
    public CircuitManager circuitManager;
    public UtilityNetworkManager<ElectricalUtilityNetwork, Wire> electricalConduitSystem;
}

public class UtilityNetwork { public int id; }
public sealed class ElectricalUtilityNetwork : UtilityNetwork { }
public sealed class Wire { }

public sealed class UtilityNetworkManager<NetworkType, ItemType>
    where NetworkType : UtilityNetwork, new()
{
    public readonly Dictionary<int, UtilityNetwork> Cells = new Dictionary<int, UtilityNetwork>();
    public readonly Dictionary<object, UtilityNetwork> VirtualKeys = new Dictionary<object, UtilityNetwork>();

    [MethodImpl(MethodImplOptions.NoInlining)]
    public UtilityNetwork GetNetworkForCell(int cell)
    {
        UtilityNetwork network;
        return Grid.IsValidCell(cell) && Cells.TryGetValue(cell, out network) ? network : null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public UtilityNetwork GetNetworkForVirtualKey(object key)
    {
        UtilityNetwork network;
        return VirtualKeys.TryGetValue(key, out network) ? network : null;
    }
}

public static class Grid
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool IsValidCell(int cell) { return cell >= 0; }
}

namespace Klei.AI
{
    public sealed class AttributeInstance
    {
        public float GetTotalValue() { return 1f; }
    }
}

public sealed class CircuitManager
{
    public readonly Dictionary<ushort, float> Values = new Dictionary<ushort, float>();
    public int Calls;
    public Action BeforeRead;
    public int SyntheticWork;
    public int SyntheticAllocation;
    public long WorkChecksum;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public ushort GetCircuitID(int cell)
    {
        var network = Game.Instance.electricalConduitSystem.GetNetworkForCell(cell);
        return network == null ? ushort.MaxValue : (ushort)network.id;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public ushort GetVirtualCircuitID(object key)
    {
        var network = Game.Instance.electricalConduitSystem.GetNetworkForVirtualKey(key);
        return network == null ? ushort.MaxValue : (ushort)network.id;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public ushort GetCircuitID(ICircuitConnected item)
    {
        return item.IsVirtual ? GetVirtualCircuitID(item.VirtualCircuitKey) : GetCircuitID(item.PowerCell);
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public float GetWattsNeededWhenActive(ushort originCircuitId)
    {
        Calls++;
        BeforeRead?.Invoke();
        // Explicitly synthetic load: integer arithmetic and a temporary byte
        // array approximate neither ONI's graph nor the game's frame time.
        var work = (uint)originCircuitId + 1u;
        for (var index = 0; index < SyntheticWork; index++)
            work = unchecked(work * 1664525u + 1013904223u);
        WorkChecksum += work;
        if (SyntheticAllocation != 0)
        {
            var temporary = new byte[SyntheticAllocation];
            temporary[0] = (byte)work;
            WorkChecksum += temporary[0];
            GC.KeepAlive(temporary);
        }
        return Values[originCircuitId];
    }
}

public static class OverlayModes
{
    public sealed class Power
    {
        public struct Query
        {
            public CircuitManager Manager;
            public ushort Circuit;
        }

        public Query[] Queries = new Query[0];
        public float[] Results = new float[0];
        public Action<int> AfterRead;

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public void Update()
        {
            for (var index = 0; index < Queries.Length; index++)
            {
                var query = Queries[index];
                Results[index] = query.Manager.GetWattsNeededWhenActive(query.Circuit);
                AfterRead?.Invoke(index);
            }
        }
    }
}
