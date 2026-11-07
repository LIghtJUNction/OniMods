"""Semantic snapshot comparator for intentionally changed pressure physics.

Unlike the exact conformance runner, this reports conserved budgets and
observable deltas without silently deciding acceptable gameplay thresholds.
A fixture-specific policy must be reviewed and supplied before PASS is possible.
"""
from __future__ import annotations

import argparse
import json
import struct
from pathlib import Path


def _f32(word):
    return struct.unpack("<f", struct.pack("<I", int(word) & 0xFFFFFFFF))[0]


def _relative_delta(reference, candidate):
    return abs(candidate - reference) / max(abs(reference), 1.0e-30)


def summarize(snapshot, elements):
    arrays = snapshot["arrays"]
    required = ("element", "mass", "temperature", "disease", "disease_count")
    if any(arrays.get(name) is None for name in required):
        raise ValueError("snapshot is missing required CellSOA arrays")
    n = len(arrays["element"])
    if any(len(arrays[name]) != n for name in required):
        raise ValueError("snapshot CellSOA array lengths differ")

    mass_by_element = {}
    energy_by_element = {}
    disease_by_id = {}
    for i, element_id in enumerate(arrays["element"]):
        mass = _f32(arrays["mass"][i])
        temperature = _f32(arrays["temperature"][i])
        disease_id = arrays["disease"][i]
        disease_count = arrays["disease_count"][i]
        if mass < 0.0:
            raise ValueError(f"negative mass at cell {i}: {mass}")
        mass_by_element[element_id] = mass_by_element.get(element_id, 0.0) + mass
        if element_id < len(elements):
            cp = float(elements[element_id].get("shc", 1.0))
        else:
            cp = 1.0
        energy_by_element[element_id] = (
            energy_by_element.get(element_id, 0.0) + mass * cp * temperature
        )
        if disease_count > 0:
            disease_by_id[disease_id] = disease_by_id.get(disease_id, 0) + disease_count

    total_mass = sum(mass_by_element.values())
    total_energy = sum(energy_by_element.values())
    total_disease = sum(disease_by_id.values())
    return {
        "cells": n,
        "total_mass": total_mass,
        "total_sensible_energy_proxy": total_energy,
        "total_disease_count": total_disease,
        "mass_by_element": {str(k): v for k, v in sorted(mass_by_element.items())},
        "energy_by_element": {str(k): v for k, v in sorted(energy_by_element.items())},
        "disease_by_id": {str(k): v for k, v in sorted(disease_by_id.items())},
        "event_counts": {k: len(v) for k, v in sorted(snapshot.get("events", {}).items())},
        "frames": snapshot.get("frames"),
    }


def compare(reference, candidate, elements):
    a = reference["arrays"]
    b = candidate["arrays"]
    left = summarize(reference, elements)
    right = summarize(candidate, elements)
    n = len(a["element"])
    if len(b["element"]) != n:
        raise ValueError("reference and candidate grid sizes differ")
    changed = {}
    for name in ("element", "mass", "temperature", "disease", "disease_count"):
        changed[name] = sum(x != y for x, y in zip(a[name], b[name]))
    texture_diffs = {}
    for name in ("flow_texture", "liquid_texture", "liquid_data_texture",
                 "material_texture", "exposed_texture"):
        av, bv = a.get(name), b.get(name)
        if av is not None and bv is not None and len(av) == len(bv):
            texture_diffs[name] = sum(x != y for x, y in zip(av, bv))
        else:
            texture_diffs[name] = None
    return {
        "reference": left,
        "candidate": right,
        "relative_deltas": {
            "total_mass": _relative_delta(left["total_mass"], right["total_mass"]),
            "total_sensible_energy_proxy": _relative_delta(
                left["total_sensible_energy_proxy"], right["total_sensible_energy_proxy"]),
            "total_disease_count": _relative_delta(
                left["total_disease_count"], right["total_disease_count"]),
        },
        "changed_cells": changed,
        "texture_word_differences": texture_diffs,
        "event_count_deltas": {
            key: right["event_counts"].get(key, 0) - left["event_counts"].get(key, 0)
            for key in sorted(left["event_counts"].keys() | right["event_counts"].keys())
        },
        "decision": "UNASSESSED",
        "decision_reason": "No fixture-specific reviewed acceptance policy was supplied.",
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("case", type=Path)
    parser.add_argument("reference", type=Path)
    parser.add_argument("candidate", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    case = json.loads(args.case.read_text())
    reference = json.loads(args.reference.read_text())
    candidate = json.loads(args.candidate.read_text())
    # The same element metadata used to build the ABI fixture is embedded in
    # case.json; use the canonical synthetic table when the case omits it.
    if "elements" in case:
        elements = case["elements"]
    else:
        from host import elements as default_elements
        elements = default_elements()
    report = {
        "case": case["name"],
        "comparator": "semantic-pressure-v1",
        "metrics": compare(reference, candidate, elements),
    }
    rendered = json.dumps(report, indent=2, sort_keys=True) + "\n"
    if args.output:
        args.output.write_text(rendered)
    print(rendered, end="")


if __name__ == "__main__":
    main()
