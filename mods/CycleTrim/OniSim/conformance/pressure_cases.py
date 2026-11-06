"""Authored numeric boundaries for pressure transfer; no game data is copied."""

import random
import struct


def _word(value):
    return struct.unpack("<f", struct.pack("<I", value))[0]


def pressure_cases():
    from host import elements

    pairs = [
        (0.0, 1.0), (1.0, 0.0),
        (_word(0x3F7FFFFF), 1.0), (1.0, _word(0x3F800001)),
        (0.000001, 1000.0), (1000.0, 0.000001),
        (_word(0x4479FFFF), _word(0x447A0001)),
        (16000.0, 20000.0),
    ]
    result = []
    for zero_first in (False, True):
        for hot_first in (False, True):
            result.append(dict(
                name=f"thermal-zero-capacity-first{int(zero_first)}-hot{int(hot_first)}",
                width=9, height=9, steps=3, fill=dict(element="Unobtanium", mass=1000, temperature=300),
                regions=[dict(x=x, y=4, width=1, height=1, cell=dict(
                    element="Granite", mass=0 if (x == 4) == zero_first else 1000,
                    temperature=600 if (x == 4) == hot_first else 275)) for x in (4, 5)],
            ))
    for material in ("Oxygen", "Water"):
        for index, (left_mass, right_mass) in enumerate(pairs):
            # Odd widths and both flow directions exercise the actual pressure
            # loop, instead of testing MINSS/CVTTSS2SI helpers in isolation.
            width, height = (9, 11) if index % 2 else (17, 13)
            split = (width - 2) // 2
            table = elements()
            for item in table:
                item["conductivity"] = 0
            def cell(mass, temperature, germs):
                return dict(element=material, mass=mass, temperature=temperature,
                            disease=0, disease_count=germs)
            result.append(dict(
                name=f"pressure-boundary-{material.lower()}-{index}",
                width=width, height=height, steps=12, seed=42,
                diseases=[{}], elements=table, fill=cell(1, 300, 0),
                regions=[
                    dict(x=1, y=1, width=split, height=height - 2,
                         cell=cell(left_mass, _word(0x4395FFFF), 2147483647)),
                    dict(x=split + 1, y=1, width=width - 2 - split, height=height - 2,
                         cell=cell(right_mass, _word(0x43960001), 16777217)),
                ],
            ))

    rng = random.Random(0xC1C1E)
    for index in range(16):
        width, height = ((9, 11), (17, 13), (23, 9), (31, 17))[index % 4]
        regions = []
        for _ in range(12):
            material = rng.choice(("Oxygen", "Oxygen", "Water", "Granite"))
            regions.append(dict(
                x=rng.randrange(1, width - 1), y=rng.randrange(1, height - 1),
                width=1, height=1,
                cell=dict(element=material, mass=rng.choice((0.0, 0.001, 0.99, 1.01, 800.0, 20000.0)),
                          temperature=rng.choice((275.0, 300.0, _word(0x43960001), 600.0)),
                          disease=0, disease_count=rng.choice((0, 1, 16777217, 2147483647))),
            ))
        result.append(dict(
            name=f"pressure-mixed-seed-{index}", width=width, height=height,
            seed=index, steps=12, diseases=[{}], fill=dict(element="Oxygen", mass=1, temperature=300),
            regions=regions,
        ))
    # Pressure metadata keeps the exact wire word, including signaling NaNs.
    # Localized gas/liquid pairs exercise transfer and clamp ordering without
    # replacing the helper with a separate arithmetic model.
    flow_words = (0, 0x80000000, 1, 0x80000001, 0x007fffff, 0x00800000,
                  0x3dcccccd, 0x7f800000, 0xff800000, 0x7fc01234,
                  0x7f800001, 0xff800001)
    for material in ("Oxygen", "Water"):
        for word in flow_words:
            table = elements()
            for item in table:
                item["conductivity"] = 0
                if item["name"] == material:
                    item["float_word_overrides"] = dict(flow=word)
            result.append(dict(
                name=f"pressure-flow-word-{material.lower()}-{word:08x}",
                width=7, height=5, seed=42, steps=3, diseases=[{}], elements=table,
                fill=dict(element="Unobtanium", mass=1000, temperature=300),
                regions=[dict(x=x, y=2, width=1, height=1,
                              cell=dict(element=material, mass=mass, temperature=temperature,
                                        disease=0, disease_count=count))
                         for x, mass, temperature, count in [(2, 4, 330, 17), (3, 1, 300, 2)]],
            ))
    return result
