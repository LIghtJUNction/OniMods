using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace OniMcp.Tools
{
    internal static class LogicPortReadSemantics
    {
        internal static int ActualCell(LogicPorts ports, LogicPorts.Port port)
        {
            if (ports == null)
                return Grid.InvalidCell;
            var rotatable = ports.GetComponent<Rotatable>();
            var orientation = rotatable == null ? Orientation.Neutral : rotatable.GetOrientation();
            var offset = Rotatable.GetRotatedCellOffset(port.cellOffset, orientation);
            return Grid.OffsetCell(Grid.PosToCell(ports.gameObject), offset);
        }

        internal static bool TryBridgeRoute(GameObject go, out int from, out int to)
        {
            from = to = Grid.InvalidCell;
            var link = go == null ? null : go.GetComponent<LogicUtilityNetworkLink>();
            if (link == null || !link.isSpawned || link.visualizeOnly)
                return false;
            link.GetCells(out from, out to);
            return Grid.IsValidCell(from) && Grid.IsValidCell(to)
                && link.cell_one == from && link.cell_two == to
                && IsBridgeCurrentlyRegistered(link, from, to);
        }

        internal static bool IsBridgeCurrentlyRegistered(
            LogicUtilityNetworkLink link, int from, int to)
        {
            if (link == null || !PrivateConnected(link))
                return false;
            try
            {
                var manager = Game.Instance?.logicCircuitManager;
                var groupsField = FindInstanceField(manager?.GetType(), "bridgeGroups");
                if (!(groupsField?.GetValue(manager) is Array groups)
                    || (int)link.bitDepth < 0 || (int)link.bitDepth >= groups.Length
                    || !(groups.GetValue((int)link.bitDepth) is IEnumerable group)
                    || !ContainsReference(group, link))
                    return false;

                var networkManager = link.GetNetworkManager();
                var linksField = FindInstanceField(networkManager?.GetType(), "links");
                if (!(linksField?.GetValue(networkManager) is IDictionary links))
                    return false;
                return links.Contains(from) && links.Contains(to)
                    && Convert.ToInt32(links[from]) == to && Convert.ToInt32(links[to]) == from;
            }
            catch
            {
                return false;
            }
        }

        private static bool PrivateConnected(LogicUtilityNetworkLink link)
        {
            try
            {
                var field = FindInstanceField(link.GetType(), "connected");
                return field?.FieldType == typeof(bool) && (bool)field.GetValue(link);
            }
            catch
            {
                return false;
            }
        }

        private static FieldInfo FindInstanceField(Type type, string name)
        {
            while (type != null)
            {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null)
                    return field;
                type = type.BaseType;
            }
            return null;
        }

        private static bool ContainsReference(IEnumerable items, object expected)
        {
            foreach (var item in items)
                if (ReferenceEquals(item, expected))
                    return true;
            return false;
        }

        internal static GameObject BuildingAtCell(int cell)
        {
            if (!Grid.IsValidCell(cell))
                return null;
            return Grid.Objects[cell, (int)ObjectLayer.Building]
                ?? Grid.Objects[cell, (int)ObjectLayer.LogicGate];
        }

        internal static bool ConnectedAtCell(int cell)
        {
            return Grid.IsValidCell(cell)
                && Game.Instance?.logicCircuitManager?.GetNetworkForCell(cell) != null;
        }

        internal static int InputValue(LogicPorts ports, int index)
        {
            if (ports?.inputPorts == null || index < 0 || index >= ports.inputPorts.Count)
                return 0;
            var endpoint = ports.inputPorts[index];
            var value = endpoint?.GetType().GetProperty("Value",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(endpoint, null);
            return value == null ? 0 : Convert.ToInt32(value);
        }

        internal static int OutputValue(LogicPorts ports, int index)
        {
            if (ports?.outputPorts == null || index < 0 || index >= ports.outputPorts.Count)
                return 0;
            return ports.outputPorts[index] is ILogicEventSender sender ? sender.GetLogicValue() : 0;
        }

        internal static bool RegisteredEndpointsMatch(LogicPorts ports, LogicCircuitManager manager)
        {
            return RegisteredGroupMatches(ports, manager, ports?.inputPortInfo, ports?.inputPorts)
                && RegisteredGroupMatches(ports, manager, ports?.outputPortInfo, ports?.outputPorts);
        }

        private static bool RegisteredGroupMatches(LogicPorts ports, LogicCircuitManager manager,
            LogicPorts.Port[] info, IList<ILogicUIElement> endpoints)
        {
            int count = info?.Length ?? 0;
            if (manager == null || count != (endpoints?.Count ?? 0))
                return false;
            var visible = manager.GetVisElements();
            for (int i = 0; i < count; i++)
                if (endpoints[i] == null
                    || endpoints[i].GetLogicUICell() != ActualCell(ports, info[i])
                    || !visible.Contains(endpoints[i]))
                    return false;
            return true;
        }
    }
}
