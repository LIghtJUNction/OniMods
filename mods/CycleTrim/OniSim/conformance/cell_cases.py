"""Authored cell-command variants, including displacement and ordered queues."""


def cell_cases():
    from host import elements
    base = dict(width=16, height=16, steps=5, diseases=[{}, dict(strength=2)])
    def cell(element, mass=1, temperature=300, **extra):
        return dict(element=element, mass=mass, temperature=temperature, **extra)
    def modify(element, mass, replace=0, subtype=0):
        return dict(id=-1252920804, format="ii2fiHBBB3x",
                    values=[119, 101, 350, mass, 100, element, replace, 0, subtype])
    result = [
        dict(base, name=f"cell-replace{replace}-element{element}",
             fill=cell("Water", 500, disease=1, disease_count=10000),
             commands=[modify(element, mass, replace)])
        for replace in (1, 2) for element, mass in ((0, 0), (1, 1000), (2, 2), (3, 500))
    ]
    result += [
        dict(base, name=name, fill=cell(fill, mass), commands=[modify(element, delta, 0, subtype)])
        for name, fill, mass, element, delta, subtype in [
            ("cell-add-gas", "Oxygen", 1, 2, 2, 0),
            ("cell-add-liquid", "Oxygen", 1, 3, 500, 0),
            ("cell-add-solid-column", "Oxygen", 1, 1, 1000, 0),
            ("cell-add-solid-direct", "Oxygen", 1, 1, 1000, 1),
            ("cell-remove-gas", "Oxygen", 1, 2, -.5, 0),
            ("cell-remove-liquid", "Water", 1000, 3, -500, 0),
            ("cell-remove-solid", "Granite", 1000, 1, -500, 0),
            ("cell-remove-all-liquid", "Water", 1000, 3, -2000, 0),
        ]
    ]
    result += [dict(base, name=name, fill=cell(fill, mass), commands=[
        dict(id=-469311643, format="iiBB2x", values=[119, 101, mask, 1]),
        dict(step=2, id=-469311643, format="iiBB2x", values=[119, 102, mask, 0]),
    ]) for name, fill, mass, mask in [("cell-property-gas", "Oxygen", 1, 1),
        ("cell-property-liquid", "Water", 1000, 2), ("cell-property-bits", "Oxygen", 1, 0x30)]]
    result += [dict(base, name=name, commands=[
        dict(id=message, format="if", values=[119, value]),
        dict(step=2, id=message, format="if", values=[119, 0]),
    ]) for name, message, value in [("cell-insulation", -898773121, .5),
                                    ("cell-strength", 1593243982, 191)]]
    result += [dict(base, name="cell-disease-modify-consume", fill=cell("Oxygen", disease=0, disease_count=10000), commands=[
        dict(id=-1853671274, format="iB3xi", values=[119, 1, 5000]),
        dict(step=1, id=-1019841536, format="iifi", values=[119, 101, .5, 1000]),
        dict(step=2, id=-1853671274, format="iB3xi", values=[119, 255, -100000]),
    ])]
    result += [dict(base, name=f"cell-dig-skip{skip}", fill=cell("Granite", 1000, disease=0, disease_count=10000),
                    commands=[dict(id=833038498, format="iiBB2x", values=[119, 101, skip, 0])])
               for skip in (0, 1)]
    result += [dict(base, name="cell-dig-backwall", commands=[
        dict(id=-605421515, format="iH2xff", values=[119, 1, 500, 600]),
        dict(step=2, id=833038498, format="iiBB2x", values=[119, 101, 0, 1]),
    ]), dict(base, name="saved-options-bits", save_roundtrip=True, commands=[
        dict(id=1154135737, format="BB", values=[0, 0x81]),
        dict(step=2, id=1154135737, format="BB", values=[1, 0x40]),
    ])]
    # The combined 0x30 property selects this stable material hash in the
    # original texture path. Omitting it makes the synthetic table incomplete.
    for case in result:
        if case["name"] == "cell-property-bits":
            case["elements"] = elements() + [dict(name="AuthoredSunlightOverride",
                id=0x2531469c, state=3, light_absorption=.5, color=0xff00ff00)]
    gas_elements = elements() + [dict(name="AuthoredGas", id=123456789, state=1,
                                     shc=1, conductivity=.01, molar_mass=64)]
    interaction_base = dict(base, elements=gas_elements, absent_element=3,
        regions=[dict(x=8, y=1, width=7, height=14, cell=cell("AuthoredGas", 2))])
    result += [
        dict(interaction_base, name="interaction-registered-normal-frames", interactions=dict(records=[{}])),
        dict(interaction_base, name="interaction-unknown-record-skipped", interactions=dict(records=[dict(kind=0x12345678, a=65535, b=65535, product=65535)])),
        dict(interaction_base, name="interaction-replace-clear", interactions=dict(records=[{}]), interaction_updates=[
            dict(step=1, records=[dict(a=6, b=2)]), dict(step=3, records=[])]),
        dict(interaction_base, name="interaction-negative-count-clears", interactions=dict(records=[{}]),
             interaction_updates=[dict(step=1, records=[], count=-1)]),
        dict(base, name="disease-emitter-remove-reuse", commands=[
            dict(id=1486783027, format="i", values=[101]),
            dict(step=1, id=-1899123924, format="iiBB2xfi", values=[0, 119, 0, 2, .2, 100]),
            dict(step=2, id=468135926, format="ii", values=[0, 102]),
            dict(step=3, id=1486783027, format="i", values=[103]),
        ]),
        dict(base, name="debug-transfer-scales", commands=[
            dict(id=-1683118492, format="ffB3x", values=[.01, .02, 1]),
            dict(id=1739021608, format="iH2x5f4i", values=[101, 1, 100, 600, 1, 1000, 0, 5, 5, 6, 6]),
            dict(step=2, id=-1683118492, format="ffB3x", values=[.002, .003, 0]),
        ]),
        dict(base, name="radiation-parameters", radiation=True, commands=[
            dict(id=377112707, format="if", values=[0, .5]),
            dict(id=377112707, format="if", values=[1, 500]),
            dict(id=377112707, format="if", values=[2, .1]),
            dict(id=377112707, format="if", values=[3, .2]),
            dict(id=377112707, format="if", values=[4, .3]),
            dict(id=377112707, format="if", values=[5, 500]),
            dict(id=-1914877797, format="ifi", values=[119, 1000, 101]),
            dict(id=-1505895314, format="iiHH5fi", values=[102, 119, 3, 2, 100, 1, .5, 0, 360, 0]),
        ]),
        dict(base, name="allocate-clear-load-roundtrip", allocate_only=True, save_roundtrip=True,
             loads=[dict(width=18, height=18, internal_offsets=True, cell=cell("Oxygen", 1, 310),
                         regions=[dict(x=6,y=6,width=3,height=3,cell=cell("Water", 500))])]),
    ]
    result += [dict(base, name="linux-system-info", export_probe="linux-system-info"),
               dict(base, name="intentional-debug-assert", export_probe="intentional-debug-assert")]
    result += [
        dict(base, name="backwall-null-clear-restore", steps=8, save_roundtrip=True, commands=[
            dict(id=-605421515, format="iH2xff", values=[119, 1, 100, 600]),
            dict(step=1, id=-605421515, format="iH2xff", values=[119, 65535, 0, 0]),
            dict(step=3, id=-605421515, format="iH2xff", values=[119, 65535, 0, 0]),
            dict(step=5, id=-605421515, format="iH2xff", values=[119, 1, 200, 450]),
        ], required_events=["backwall_changed"]),
        dict(base, name="backwall-null-save", save_roundtrip=True, commands=[
            dict(id=-605421515, format="iH2xff", values=[119, 65535, 0, 0]),
        ], required_events=["backwall_changed"]),
        dict(base, name="backwall-null-save-zero-elapsed", dt=0, save_roundtrip=True, commands=[
            dict(id=-605421515, format="iH2xff", values=[119, 65535, 7, 600]),
        ], required_events=["backwall_changed"]),
    ]
    result += [
        dict(base, name="save-buffer-replacement", save_roundtrip=True, save_replacements=8),
        dict(base, name="save-buffer-replacement-null-wall", dt=0, save_roundtrip=True,
             save_replacements=8, commands=[
                 dict(id=-605421515, format="iH2xff", values=[119, 65535, 7, 600])]),
        dict(base, name="save-header-positive-origin", save_probe=True, save_origin=[7, 11],
             save_replacements=3),
        dict(base, name="save-header-negative-origin", save_probe=True, save_origin=[-7, -11],
             save_replacements=3),
    ]
    return result
