using System.Reflection;
using System.Linq;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static bool IsCompletedBuildFullyRegistered(BuildingDef def, PlacementDetails placement,
            int cell, Orientation orientation, GameObject completed, out string error)
        {
            error = null;
            var building = completed == null ? null : completed.GetComponent<Building>();
            var buildingComplete = completed == null ? null : completed.GetComponent<BuildingComplete>();
            var prefab = completed == null ? null : completed.GetComponent<KPrefabID>();
            if (building == null || buildingComplete == null || prefab == null
                || !EqualsIgnoreCase(building.Def?.PrefabID, def.PrefabID)
                || !EqualsIgnoreCase(buildingComplete.Def?.PrefabID, def.PrefabID)
                || !EqualsIgnoreCase(prefab.PrefabTag.Name, def.PrefabID))
            {
                error = "Existing object has inconsistent Building, BuildingComplete, or KPrefabID identity";
                return false;
            }

            var actual = ActualPlacementDetails(completed, def, placement.AnchorX, placement.AnchorY);
            if (!GetBool(ComparePlacement(placement, actual), "valid"))
            {
                error = "completed building does not match the requested prefab and anchor";
                return false;
            }

            if (!IsCompletedBuildGridRegistered(def, cell, orientation, completed, out error))
                return false;

            var manager = Game.Instance?.logicCircuitManager;
            var gate = completed.GetComponent<LogicGate>();
            if (gate != null && (manager == null
                || !HasRegisteredGateEndpoint(gate, manager, "inputOne", gate.InputCellOne)
                || !HasRegisteredGateEndpoint(gate, manager, "outputOne", gate.OutputCellOne)
                || (gate.RequiresTwoInputs && !HasRegisteredGateEndpoint(gate, manager, "inputTwo", gate.InputCellTwo))
                || (gate.RequiresFourInputs && (!HasRegisteredGateEndpoint(gate, manager, "inputTwo", gate.InputCellTwo)
                    || !HasRegisteredGateEndpoint(gate, manager, "inputThree", gate.InputCellThree)
                    || !HasRegisteredGateEndpoint(gate, manager, "inputFour", gate.InputCellFour)))
                || (gate.RequiresFourOutputs && (!HasRegisteredGateEndpoint(gate, manager, "outputTwo", gate.OutputCellTwo)
                    || !HasRegisteredGateEndpoint(gate, manager, "outputThree", gate.OutputCellThree)
                    || !HasRegisteredGateEndpoint(gate, manager, "outputFour", gate.OutputCellFour)))
                || (gate.RequiresControlInputs && (!HasRegisteredGateEndpoint(gate, manager, "controlOne", gate.ControlCellOne)
                    || !HasRegisteredGateEndpoint(gate, manager, "controlTwo", gate.ControlCellTwo)))))
            {
                error = "completed logic gate is missing one or more registered endpoints";
                return false;
            }

            var ports = completed.GetComponent<LogicPorts>();
            if (ports != null && !LogicPortReadSemantics.RegisteredEndpointsMatch(ports, manager))
            {
                error = "completed building has missing, misplaced, or unregistered physical logic endpoints";
                return false;
            }
            return true;
        }

        private static bool IsCompletedBuildGridRegistered(BuildingDef def, int cell,
            Orientation orientation, GameObject completed, out string error)
        {
            error = null;
            if (def.BuildLocationRule == BuildLocationRule.Conduit)
            {
                if (!IsConduitBridgePortRegistered(def.InputConduitType, def.UtilityInputOffset,
                        cell, orientation, completed)
                    || !IsConduitBridgePortRegistered(def.OutputConduitType, def.UtilityOutputOffset,
                        cell, orientation, completed))
                {
                    error = "completed conduit bridge is not registered on both native utility ports";
                    return false;
                }
                var conduitBridge = completed.GetComponent<ConduitBridgeBase>();
                if (conduitBridge == null || !conduitBridge.isSpawned)
                {
                    error = "completed conduit bridge runtime component is not spawned";
                    return false;
                }
                return true;
            }

            if (def.BuildLocationRule == BuildLocationRule.WireBridge)
            {
                var link = completed.GetComponent<UtilityNetworkLink>();
                if (link == null || !link.isSpawned || link.visualizeOnly)
                {
                    error = "completed wire bridge runtime link is not active";
                    return false;
                }
                link.GetCells(cell, orientation, out int linkedCellOne, out int linkedCellTwo);
                if (Grid.Objects[linkedCellOne, (int)ObjectLayer.WireConnectors] != completed
                    || Grid.Objects[linkedCellTwo, (int)ObjectLayer.WireConnectors] != completed)
                {
                    error = "completed wire bridge is not registered on both native link cells";
                    return false;
                }
                return true;
            }

            if (def.BuildLocationRule == BuildLocationRule.LogicBridge)
            {
                var link = completed.GetComponent<LogicUtilityNetworkLink>();
                var ports = completed.GetComponent<LogicPorts>();
                if (link == null || !link.isSpawned || link.visualizeOnly
                    || ports?.inputPortInfo == null || ports.inputPortInfo.Length == 0)
                {
                    error = "completed logic bridge runtime link or native input ports are missing";
                    return false;
                }
                link.GetCells(cell, orientation, out int linkedCellOne, out int linkedCellTwo);
                if (!LogicPortReadSemantics.TryBridgeRoute(completed, out int registeredCellOne, out int registeredCellTwo)
                    || registeredCellOne != linkedCellOne || registeredCellTwo != linkedCellTwo)
                {
                    error = "completed logic bridge runtime link is not currently connected and registered on its native endpoint cells";
                    return false;
                }
                foreach (var port in ports.inputPortInfo)
                {
                    var offset = Rotatable.GetRotatedCellOffset(port.cellOffset, orientation);
                    int portCell = Grid.OffsetCell(cell, offset);
                    if (Grid.Objects[portCell, (int)def.ObjectLayer] != completed)
                    {
                        error = "completed logic bridge is not registered on every native logic port";
                        return false;
                    }
                }
                return true;
            }

            bool gridRegistered = true;
            def.RunOnArea(cell, orientation, offsetCell =>
            {
                if (Grid.Objects[offsetCell, (int)def.ObjectLayer] != completed)
                    gridRegistered = false;
            });
            if (!gridRegistered)
            {
                error = "completed building is not registered on its Grid.Objects footprint";
                return false;
            }
            return true;
        }

        private static bool IsConduitBridgePortRegistered(ConduitType type, CellOffset portOffset,
            int cell, Orientation orientation, GameObject completed)
        {
            if (type == ConduitType.None)
                return true;
            var offset = Rotatable.GetRotatedCellOffset(portOffset, orientation);
            int portCell = Grid.OffsetCell(cell, offset);
            var layer = Grid.GetObjectLayerForConduitType(type);
            return Grid.Objects[portCell, (int)layer] == completed;
        }

        private static bool HasRegisteredGateEndpoint(LogicGate gate, LogicCircuitManager manager,
            string fieldName, int cell)
        {
            var field = typeof(LogicGate).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            var endpoint = field?.GetValue(gate) as ILogicUIElement;
            return endpoint != null
                && endpoint.GetLogicUICell() == cell
                && manager.GetVisElements().Contains(endpoint);
        }
    }
}
