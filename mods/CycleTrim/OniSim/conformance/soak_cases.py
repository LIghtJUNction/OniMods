"""Longer deterministic ABI workloads; authored inputs, not colony acceptance."""

import random
from copy import deepcopy


def soak_cases():
    from host import elements

    result = []
    for kind in ("gas", "liquid", "thermal", "mixed"):
        for seed in (7, 104729, 2147483647):
            rng = random.Random(seed)
            table = elements()
            table.append(dict(name="AuthoredHeavyGas", id=123456792, state=1,
                              shc=1.7, conductivity=.12, molar_mass=64))
            regions = []
            width, height = 23, 19
            for y in range(1, height - 1):
                for x in range(1, width - 1):
                    if kind == "gas":
                        material = rng.choice(("Oxygen", "AuthoredHeavyGas"))
                        mass = rng.choice((.01, .5, 1, 2, 10))
                    elif kind == "liquid":
                        material = "Water" if y < 9 else "Oxygen"
                        mass = rng.choice((100, 400, 800, 1100)) if y < 9 else 1
                    elif kind == "thermal":
                        material = "Granite"
                        mass = rng.choice((10, 100, 1000, 1600))
                    else:
                        # Interleaved phases exercise displacement, gas swaps,
                        # liquid convection and germ transport in one state.
                        material = rng.choice(("Water", "Oxygen", "AuthoredHeavyGas",
                                               "Granite", "Vacuum"))
                        if material == "Vacuum":
                            mass = 0
                        elif material == "Water":
                            mass = rng.choice((100, 400, 800, 1100))
                        elif material == "Granite":
                            mass = 1600
                        else:
                            mass = rng.choice((.01, .5, 1, 2, 10))
                    temperature = rng.choice((280, 300, 330, 370))
                    disease = rng.randrange(2)
                    disease_count = rng.choice((0, 1, 1000, 100000))
                    if material == "Vacuum":
                        temperature, disease, disease_count = 0, 255, 0
                    regions.append(dict(x=x, y=y, width=1, height=1,
                                        cell=dict(element=material, mass=mass,
                                                  temperature=temperature, disease=disease,
                                                  disease_count=disease_count)))
            result.append(dict(name=f"soak-{kind}-seed{seed}", width=width, height=height,
                               steps=96, seed=seed, elements=table,
                               diseases=[{}, dict(strength=2)], regions=regions,
                               save_roundtrip=True, save_roundtrip_count=4,
                               required_changed_arrays=(["temperature", "mass", "element"]
                                                        if kind == "mixed" else ["temperature"])))
    # Couple mixed phases and germ transport across ordered active regions,
    # including an overlap at the 32-cell boundary. These remain authored ABI
    # fixtures, not DLC-world or real-colony acceptance.
    templates = [case for case in result
                 if case["name"] in ("soak-mixed-seed7", "soak-mixed-seed104729")]
    for overlap in (False, True):
        regions = []
        for offset, template in zip((1, 35), templates):
            for source in template["regions"]:
                region = deepcopy(source)
                region["x"] += offset
                regions.append(region)
        active = ([[0, 0, 34, 31, 0, 0], [31, 0, 64, 31, 0, 0]] if overlap
                  else [[0, 0, 30, 31, 0, 0], [32, 0, 64, 31, 0, 0]])
        if overlap:
            for x, material, mass, temperature, disease in (
                (30, "Water", 800, 370, 0),
                (31, "AuthoredHeavyGas", 4, 330, 1),
                (32, "Oxygen", .5, 280, 0),
                (33, "Water", 400, 300, 1),
            ):
                regions.append(dict(x=x, y=3, width=1, height=18,
                                    cell=dict(element=material, mass=mass,
                                              temperature=temperature,
                                              disease=disease, disease_count=10000)))
        for reverse in (False, True):
            result.append(dict(name=f"soak-multi-region-overlap{int(overlap)}-reverse{int(reverse)}",
                               width=64, height=32, steps=24, seed=104729,
                               elements=deepcopy(templates[0]["elements"]),
                               diseases=[{}, dict(strength=2)],
                               fill=dict(element="Granite", mass=1600, temperature=300),
                               regions=deepcopy(regions),
                               active_regions=list(reversed(active)) if reverse else active,
                               save_roundtrip=True, save_roundtrip_count=4,
                               required_changed_arrays=["temperature", "mass", "element"]))
    return result
