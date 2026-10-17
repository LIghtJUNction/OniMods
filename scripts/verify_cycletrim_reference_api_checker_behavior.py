#!/usr/bin/env python3
"""Behavior regression for CycleTrim's pinned reference-API checker."""

from __future__ import annotations

import os
from pathlib import Path
import subprocess
import sys
import tempfile


ROOT = Path(__file__).resolve().parents[1]
CHECKER = ROOT / "scripts/check_cycletrim_reference_api.py"

SYNTHETIC_REFERENCE = """\
protected override void OnSpawn();
private void UpdateLogicCircuit(object data);
private bool activated;
private LogicPorts logicPorts;
private int activateValue;
private int deactivateValue;
public void UpdatePickups(Navigator navigator, int cell);
public KCompactedVector<Fetchable> fetchables;
public List<Pickup> finalPickups;
private Dictionary<int, int> cellCosts;
public HandleVector<int>.Handle Add(Pickupable pickupable);
public void Remove(Tag tag, HandleVector<int>.Handle handle);
public void UpdateStorage(Tag tag, HandleVector<int>.Handle handle, Storage storage);
public void UpdateTags(Tag tag, HandleVector<int>.Handle handle);
public void Sim1000ms(float dt);
public override void Update();
private Navigator navigator;
public bool FindNextChore(ref Chore.Precondition.Context context);
public ChoreDriver choreDriver;
public void SetPersonalPriority(ChoreGroup group, int priority);
public void PrioritizeBrain(Brain brain);
protected override void OnPrefabInit();
private class CreatureBrainGroup : BrainGroup { }
protected abstract int InitialProbeCount();
public void RenderEveryTick(float dt);
public void RenderEveryTick(float dt2);
public override void PostRenderEveryTick(float dt);
protected List<Brain> brains;
protected Queue<Brain> priorityBrains;
protected int nextUpdateBrain;
public int debugMaxPriorityBrainCountSeen;
public void UpdateProbe(bool force = false);
public NavGrid NavGrid;
public NavType CurrentNavType;
public PathFinder.PotentialPath.Flags flags;
public bool reportOccupation;
public bool executePathProbeTaskAsync;
private WorkOrder makeWorkOrder(Navigator navigator);
public void TickFrame();
public bool NextTask(out WorkOrder order, out WorkResult result);
public void Unregister(Navigator navigator);
public void Shutdown();
private Thread[] agents;
private Dictionary<Navigator, int> navigators;
private ushort activeSerialNo;
public int Reserve(string owner, int amount, float mass);
public void Unreserve(string owner, int amount);
public void ClearReservations();
public void SetAutomationOnly(bool value);
public virtual void AddChore(Chore chore);
public virtual void RemoveChore(Chore chore);
public override void AddChore(Chore chore);
public override void RemoveChore(Chore chore);
public void SetMasterPriority(PrioritySetting setting);
public void AddDirtyCell(int cell);
public void UpdateGraph();
public void UpdateGraph(List<int> cells);
private byte[] DirtyBitFlags;
private List<int> DirtyCells;
public void Execute(PathFinder.PotentialList potentials, PathFinder.PotentialScratchPad scratch, ref AsyncPathProber.WorkResult result);
"""

WORK_ORDER_EXECUTE = (
    "public void Execute(PathFinder.PotentialList potentials, "
    "PathFinder.PotentialScratchPad scratch, "
    "ref AsyncPathProber.WorkResult result);\n"
)


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def run_checker(source: str) -> subprocess.CompletedProcess[str]:
    with tempfile.TemporaryDirectory() as tmp:
        temp = Path(tmp)
        assembly = temp / "Assembly-CSharp.dll"
        assembly.write_bytes(b"synthetic metadata fixture; never executed")
        source_file = temp / "ilspy-output.txt"
        source_file.write_text(source, encoding="utf-8")
        fake_ilspy = temp / "ilspycmd"
        fake_ilspy.write_text(
            "#!/usr/bin/env python3\n"
            "import os\n"
            "from pathlib import Path\n"
            "print(Path(os.environ['ONIMODS_FAKE_ILSPY_SOURCE']).read_text(encoding='utf-8'), end='')\n",
            encoding="utf-8",
        )
        fake_ilspy.chmod(0o755)

        environment = os.environ.copy()
        environment["ONIMODS_FAKE_ILSPY_SOURCE"] = str(source_file)
        environment["PATH"] = str(temp) + os.pathsep + environment.get("PATH", "")
        return subprocess.run(
            [sys.executable, str(CHECKER), str(assembly)],
            cwd=ROOT,
            env=environment,
            check=False,
            capture_output=True,
            text=True,
            timeout=30,
        )


def main() -> int:
    try:
        complete = run_checker(SYNTHETIC_REFERENCE)
        require(
            complete.returncode == 0,
            "complete synthetic metadata surface must satisfy the reference API checker: "
            + complete.stderr,
        )

        missing_execute = run_checker(SYNTHETIC_REFERENCE.replace(WORK_ORDER_EXECUTE, ""))
        require(
            missing_execute.returncode != 0,
            "reference API checker must reject a missing AsyncPathProber.WorkOrder.Execute target",
        )
        require(
            "AsyncPathProber.WorkOrder.Execute" in missing_execute.stderr,
            "missing dynamic WorkOrder target must be identified in checker diagnostics",
        )
    except (AssertionError, OSError, subprocess.TimeoutExpired) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        return 1

    print("PASS: CycleTrim reference API checker covers AsyncPathProber.WorkOrder.Execute")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
