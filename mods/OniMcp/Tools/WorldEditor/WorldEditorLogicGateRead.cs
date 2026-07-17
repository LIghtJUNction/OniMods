using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static GameObject CellBuildingObject(int cell)
        {
            if (!Grid.IsValidCell(cell))
                return null;
            return Grid.Objects[cell, (int)ObjectLayer.Building]
                ?? Grid.Objects[cell, (int)ObjectLayer.LogicGate];
        }

        private static bool RegisteredLogicGateEndpointFlags(
            GameObject go, int cell, out bool input, out bool output)
        {
            input = false;
            output = false;
            var gate = go != null ? go.GetComponent<LogicGate>() : null;
            var manager = Game.Instance?.logicCircuitManager;
            if (gate == null || manager == null)
                return false;

            input |= IsRegisteredGateEndpoint(gate, manager, "inputOne", gate.InputCellOne, cell);
            output |= IsRegisteredGateEndpoint(gate, manager, "outputOne", gate.OutputCellOne, cell);
            if (gate.RequiresTwoInputs || gate.RequiresFourInputs)
                input |= IsRegisteredGateEndpoint(gate, manager, "inputTwo", gate.InputCellTwo, cell);
            if (gate.RequiresFourInputs)
            {
                input |= IsRegisteredGateEndpoint(gate, manager, "inputThree", gate.InputCellThree, cell);
                input |= IsRegisteredGateEndpoint(gate, manager, "inputFour", gate.InputCellFour, cell);
            }
            if (gate.RequiresFourOutputs)
            {
                output |= IsRegisteredGateEndpoint(gate, manager, "outputTwo", gate.OutputCellTwo, cell);
                output |= IsRegisteredGateEndpoint(gate, manager, "outputThree", gate.OutputCellThree, cell);
                output |= IsRegisteredGateEndpoint(gate, manager, "outputFour", gate.OutputCellFour, cell);
            }
            if (gate.RequiresControlInputs)
            {
                input |= IsRegisteredGateEndpoint(gate, manager, "controlOne", gate.ControlCellOne, cell);
                input |= IsRegisteredGateEndpoint(gate, manager, "controlTwo", gate.ControlCellTwo, cell);
            }
            return input || output;
        }

        private static bool IsRegisteredGateEndpoint(LogicGate gate, LogicCircuitManager manager,
            string fieldName, int expectedCell, int selectedCell)
        {
            if (expectedCell != selectedCell)
                return false;
            var field = typeof(LogicGate).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            var endpoint = field?.GetValue(gate) as ILogicUIElement;
            return endpoint != null
                && endpoint.GetLogicUICell() == expectedCell
                && manager.GetVisElements().Contains(endpoint);
        }

        private static string LogicGateEndpointLine(int cell, GameObject go)
        {
            if (!RegisteredLogicGateEndpointFlags(go, cell, out bool input, out bool output))
                return null;
            var gate = go.GetComponent<LogicGate>();
            var fields = new List<string>();
            AddGateEndpointField(fields, gate, "inputOne", gate.InputCellOne, cell, "logicIn", true);
            AddGateEndpointField(fields, gate, "outputOne", gate.OutputCellOne, cell, "logicOut", false);
            if (gate.RequiresTwoInputs || gate.RequiresFourInputs)
                AddGateEndpointField(fields, gate, "inputTwo", gate.InputCellTwo, cell, "logicIn2", true);
            if (gate.RequiresFourInputs)
            {
                AddGateEndpointField(fields, gate, "inputThree", gate.InputCellThree, cell, "logicIn3", true);
                AddGateEndpointField(fields, gate, "inputFour", gate.InputCellFour, cell, "logicIn4", true);
            }
            if (gate.RequiresFourOutputs)
            {
                AddGateEndpointField(fields, gate, "outputTwo", gate.OutputCellTwo, cell, "logicOut2", false);
                AddGateEndpointField(fields, gate, "outputThree", gate.OutputCellThree, cell, "logicOut3", false);
                AddGateEndpointField(fields, gate, "outputFour", gate.OutputCellFour, cell, "logicOut4", false);
            }
            if (gate.RequiresControlInputs)
            {
                AddGateEndpointField(fields, gate, "controlOne", gate.ControlCellOne, cell, "logicControl", true);
                AddGateEndpointField(fields, gate, "controlTwo", gate.ControlCellTwo, cell, "logicControl2", true);
            }
            return fields.Count == 0 ? null
                : EndpointPrefix(cell, go.GetComponent<Building>()) + string.Join(" ", fields.ToArray());
        }

        private static void AddGateEndpointField(List<string> fields, LogicGate gate, string fieldName,
            int endpointCell, int selectedCell, string label, bool input)
        {
            if (endpointCell != selectedCell || Game.Instance?.logicCircuitManager == null)
                return;
            if (!IsRegisteredGateEndpoint(gate, Game.Instance.logicCircuitManager,
                fieldName, endpointCell, selectedCell))
                return;
            fields.Add(label + "=" + (input ? "⊗" : "⊙") + CellCoord(endpointCell)
                + " identity=" + fieldName + ":registered");
        }
    }
}
