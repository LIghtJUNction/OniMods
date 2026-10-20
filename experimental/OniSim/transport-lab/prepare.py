#!/usr/bin/env python3
"""Prepare exact checked-in source modules; no game binaries or external packages."""
from pathlib import Path
import hashlib
import json
import re

ROOT = Path(__file__).resolve().parent
BASE = ROOT / "baseline"
SOURCE = ROOT.parent / "native/src"
OUT = ROOT / "src/generated"


def method_body(text: str, name: str) -> str:
    """Compare these four simple, string-free adapter bodies, not arbitrary Rust."""
    begin = text.index("{", text.index("fn " + name + "("))
    depth = 0
    for index in range(begin, len(text)):
        depth += (text[index] == "{") - (text[index] == "}")
        if depth == 0:
            return re.sub(r"\s+", "", text[begin:index + 1])
    raise ValueError("Unclosed adapter body: " + name)


def check_adapters():
    for filename, names in (
        ("events.rs", ("substance_change", "substance_changed", "backwall_should_transition")),
        ("world.rs", ("mark_dirty",)),
    ):
        production = (SOURCE / filename).read_text()
        fixture = (ROOT / "src" / filename).read_text()
        for name in names:
            if method_body(production, name) != method_body(fixture, name):
                raise ValueError("Storage/event adapter drift: " + name)


def main():
    check_adapters()
    OUT.mkdir(exist_ok=True)
    expected = json.loads((BASE / "sha256.json").read_text())
    for name, digest in expected.items():
        if hashlib.sha256((BASE / name).read_bytes()).hexdigest() != digest:
            raise ValueError("Baseline hash mismatch: " + name)
        if name not in ("gas.rs", "thermal.rs", "mass_components_flood.rs", "simulation_step_fluid.rs"):
            if (SOURCE / name).read_bytes() != (BASE / name).read_bytes():
                raise ValueError("Shared helper changed: " + name)
    identities = {}
    for mode, directory in (("baseline", BASE), ("candidate", SOURCE)):
        for name in ("gas.rs", "thermal.rs", "mass_components_flood.rs"):
            original = (directory / name).read_bytes()
            identities[mode + "/" + name] = hashlib.sha256(original).hexdigest()
            text = original.decode()
            if name == "mass_components_flood.rs":
                text += "\nimpl FloodScratch {\n"
                text += "    pub(crate) fn capacities(&self) -> (usize, usize, usize) { (self.marks.capacity(), self.queue.capacity(), self.cells.capacity()) }\n"
                text += "    pub(crate) fn force_wrap(&mut self) { self.generation = u32::MAX; }\n}\n"
            (OUT / (mode + "_" + name)).write_text(text)
        # Execute the actual pressure + displacement pass, excluding the unrelated
        # liquid entry point. Rebinding selects baseline/candidate gas kernels only.
        fluid_source = (directory / "simulation_step_fluid.rs").read_text()
        gas_pass, marker, _ = fluid_source.partition("pub(crate) fn liquid_phase(")
        if not marker:
            raise ValueError("Gas phase extraction marker missing")
        gas_pass = gas_pass.replace("crate::gas::", "crate::" + mode + "_gas::")
        (OUT / (mode + "_gas_phase.rs")).write_text(gas_pass)
        identities[mode + "/gas_phase_source"] = hashlib.sha256(fluid_source.encode()).hexdigest()
    (ROOT / "source-identity.json").write_text(json.dumps(identities, indent=2) + "\n")
    print("Prepared exact production kernels and immutable baseline")

if __name__ == "__main__":
    main()
