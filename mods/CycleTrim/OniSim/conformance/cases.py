"""Synthetic, authored fixtures; no game assets or saves are copied."""


def cases():
    from host import elements
    from cell_cases import cell_cases
    from physics_cases import physics_cases
    from pressure_cases import pressure_cases
    from soak_cases import soak_cases
    base = dict(width=16, height=16, steps=8)
    phases = elements()
    phases[1].update(high_transition=3, high_temp=400)
    phases[2].update(low_transition=3, low_temp=250)
    phases[3].update(low_transition=1, low_temp=275, high_transition=2, high_temp=400)
    radiation_elements = elements()
    radiation_elements[1].update(radiation_absorption=.8, radiation_per_1000_mass=5)
    radiation_elements[2].update(radiation_absorption=.1)
    radiation_elements[3].update(radiation_absorption=.3)
    visual_elements = elements()
    visual_elements[1].update(light_absorption=1, color=0xff804020)
    visual_elements[2].update(light_absorption=.01)
    visual_elements[3].update(light_absorption=.1, color=0xffa06020,
                              gradient=[0x00204080, 0x80208040, 0xff804020],
                              material_properties=0x80400005)
    def cell(element, mass=1, temperature=300, **extra):
        return dict(element=element, mass=mass, temperature=temperature, **extra)
    def region(x, y, width, height, value):
        return dict(x=x, y=y, width=width, height=height, cell=value)
    building = dict(id=1739021608, format="iH2x5f4i",
                    values=[101, 1, 100, 300, 1, 1000, 0, 5, 5, 6, 6])
    legacy_elements = elements() + [dict(name="ThermalBoundary", id=0x3fe0146e, state=3,
                                         shc=1, conductivity=.1)]
    sublimating = elements()
    sublimating[1].update(sublimate_index=2, sublimate_rate=5, sublimate_efficiency=.4,
                         sublimate_probability=1, sublimate_fx=12345)
    offgassing = elements()
    offgassing[3].update(sublimate_index=2, sublimate_efficiency=.7,
                        sublimate_probability=1, off_gas_probability=.01, sublimate_fx=23456)
    partial = elements()
    partial[1].update(high_transition=3, high_temp=400, conductivity=0)
    partial[3].update(low_transition=1, low_temp=275, high_transition=2, high_temp=500,
                      conductivity=0)
    partial[2].update(conductivity=0)
    return [
        *cell_cases(),
        *physics_cases(),
        *pressure_cases(),
        *soak_cases(),
        dict(base, name="sealed-oxygen"),
        dict(base, name="sealed-water", fill=cell("Water", 1000)),
        dict(base, name="sealed-granite", fill=cell("Granite", 1000)),
        dict(base, name="sealed-vacuum", fill=cell("Vacuum", 0, 0)),
        dict(base, name="gas-pressure", regions=[region(1, 1, 7, 14, cell("Oxygen", 4))]),
        dict(base, name="gas-vacuum", regions=[region(8, 1, 7, 14, cell("Vacuum", 0, 0))]),
        dict(base, name="water-drop", fill=cell("Vacuum", 0, 0),
             regions=[region(5, 10, 5, 3, cell("Water", 1000))]),
        dict(base, name="water-gas", regions=[region(1, 1, 14, 5, cell("Water", 1000))]),
        dict(base, name="solid-heat", fill=cell("Granite", 1000),
             regions=[region(1, 1, 7, 14, cell("Granite", 1000, 600))]),
        dict(base, name="gas-heat", regions=[region(1, 1, 7, 14, cell("Oxygen", 1, 600))]),
        dict(base, name="liquid-heat", fill=cell("Water", 1000),
             regions=[region(1, 1, 7, 14, cell("Water", 1000, 600))]),
        dict(base, name="solid-heat-reverse", fill=cell("Granite", 1000),
             regions=[region(8, 1, 7, 14, cell("Granite", 1000, 600))]),
        dict(base, name="liquid-heat-reverse", fill=cell("Water", 1000),
             regions=[region(8, 1, 7, 14, cell("Water", 1000, 600))]),
        dict(base, name="gas-heat-reverse",
             regions=[region(8, 1, 7, 14, cell("Oxygen", 1, 600))]),
        dict(base, name="gas-chunk-boundary", width=35, height=35, steps=4),
        dict(base, name="batched-prepare", extra_prepare_count=3),
        dict(base, name="batched-load", loads=[
            dict(x=5, y=5, cell=cell("Water", 500)),
            dict(x=6, y=5, cell=cell("Granite", 1000)),
            dict(x=5, y=5, cell=cell("Water", 1000, 400)),
        ]),
        *[dict(base, name=name, commands=[dict(id=message, format="II", values=[0, 0xffffffff], count=2)])
          for name, message in [("enum-visible-noop", -563057023),
                                ("enum-profiler-noop", -409964931),
                                ("enum-radiation-sickness-noop", -727746602)]],
        dict(base, name="disease-diffusion", diseases=[{}], regions=[
            region(6, 6, 3, 3, cell("Oxygen", disease=0, disease_count=100000))]),
        dict(base, name="disease-conflict", diseases=[{}, dict(strength=2)], regions=[
            region(1, 1, 7, 14, cell("Oxygen", disease=0, disease_count=10000)),
            region(8, 1, 7, 14, cell("Oxygen", disease=1, disease_count=30000))]),
        dict(base, name="disease-decay", diseases=[dict(half_life=2)],
             fill=cell("Water", 1000, 450, disease=0, disease_count=100000)),
        dict(base, name="disease-diamond-parallel", width=129, height=129, steps=4,
             parallel_required=True, diseases=[{}, dict(strength=2)], commands=[
                 dict(step=0, id=1486783027, format="ii", values=[101, 102], count=2),
                 dict(step=1, id=-1899123924, format="iiBB2xfi", values=[0, 8320, 0, 255, .2, 500]),
                 dict(step=1, id=-1899123924, format="iiBB2xfi", values=[1, 8319, 1, 255, .2, 750]),
             ]),
        *[dict(base, name=name, elements=phases, fill=cell(element, mass, temperature))
          for name, element, mass, temperature in [
              ("solid-melt", "Granite", 1000, 450), ("liquid-boil", "Water", 1000, 450),
              ("liquid-freeze", "Water", 1000, 200), ("gas-condense", "Oxygen", 1, 200)]],
        dict(base, name="cell-energy", commands=[
            dict(step=0, id=818320644, format="iffi", values=[119, 100, 1000, 1]),
            dict(step=2, id=818320644, format="iffi", values=[119, -50, 1000, 2]),
        ]),
        dict(base, name="backwall-heat", commands=[
            dict(id=-605421515, format="iH2xff", values=[119, 1, 500, 600])]),
        dict(base, name="backwall-heat-reverse", fill=cell("Oxygen", 1, 600), commands=[
            dict(id=-605421515, format="iH2xff", values=[119, 1, 500, 300])]),
        dict(base, name="radiation-cell", radiation=True, commands=[
            dict(step=0, id=-1914877797, format="ifi", values=[119, 1000, 77]),
            dict(step=2, id=-1914877797, format="ifi", values=[119, -100, 78]),
        ]),
        dict(base, name="gas-heat-1000-frames", steps=1000,
             regions=[region(1, 1, 7, 14, cell("Oxygen", 1, 600))]),
        dict(base, name="water-drop-128-frames", steps=128, fill=cell("Vacuum", 0, 0),
             regions=[region(5, 10, 5, 3, cell("Water", 1000))]),
        dict(base, name="gas-save-roundtrip", save_roundtrip=True, diseases=[{}], regions=[
            region(1, 1, 7, 14, cell("Oxygen", 1, 600, disease=0, disease_count=10000))]),
        dict(base, name="liquid-save-roundtrip", save_roundtrip=True, fill=cell("Vacuum", 0, 0),
             regions=[region(5, 10, 5, 3, cell("Water", 1000))]),
        dict(base, name="building-heat", commands=[
            dict(id=1739021608, format="iH2x5f4i", values=[101, 1, 100, 600, 1, 1000, 0, 5, 5, 6, 6])]),
        dict(base, name="building-operating-heat", fill=cell("Granite", 1000), commands=[
            dict(id=1739021608, format="iH2x5f4i", values=[101, 1, 100, 300, 1, 1000, 10, 5, 5, 7, 7])]),
        dict(base, name="building-energy", commands=[
            dict(id=1739021608, format="iH2x5f4i", values=[101, 1, 100, 300, 1, 1000, 0, 5, 5, 6, 6]),
            dict(step=1, id=-1348791658, format="ifff", values=[0, 50, 0, 10000]),
            dict(step=3, id=-1348791658, format="ifff", values=[0, -30, 0, 10000]),
        ]),
        dict(base, name="building-modify-remove", commands=[
            dict(id=1739021608, format="iH2x5f4i", values=[101, 1, 100, 300, 1, 1000, 0, 5, 5, 6, 6]),
            dict(step=2, id=1818001569, format="iH2x5f4i", values=[0, 1, 200, 600, 2, 500, 10, 7, 7, 9, 9]),
            dict(step=5, id=-456116629, format="ii", values=[0, 102]),
        ]),
        dict(base, name="building-contact", commands=[
            dict(id=1739021608, format="iH2x5f4i", values=[101, 1, 100, 600, 1, 1000, 0, 5, 5, 6, 6]),
            dict(id=1739021608, format="iH2x5f4i", values=[102, 1, 100, 300, 1, 1000, 0, 9, 9, 10, 10]),
            dict(step=1, id=-1338718217, format="ii", values=[201, 0]),
            dict(step=2, id=-1586724321, format="iii", values=[0, 1, 1]),
            dict(step=4, id=-1993857213, format="ii", values=[0, 1]),
            dict(step=6, id=697100730, format="ii", values=[202, 0]),
        ]),
        dict(base, name="chunk-heat", commands=[
            dict(id=1445724082, format="ii5fH2x", values=[119, 101, 10, 600, 1, 1, 1, 1])]),
        dict(base, name="chunk-move-energy-adjust", commands=[
            dict(id=1445724082, format="ii5fH2x", values=[119, 101, 10, 600, 1, 1, 1, 1]),
            dict(step=2, id=-374911358, format="ii", values=[0, 121]),
            dict(step=3, id=1020555667, format="if", values=[0, 50]),
            dict(step=4, id=-1387601379, format="ifff", values=[0, 500, 100, 3.39]),
        ]),
        dict(base, name="chunk-set-remove-reuse", commands=[
            dict(id=1445724082, format="ii5fH2x", values=[119, 101, 10, 600, 1, 1, 1, 1]),
            dict(step=1, id=-435115907, format="iff", values=[0, 400, 20]),
            dict(step=3, id=-912908555, format="ii", values=[0, 102]),
            dict(step=5, id=1445724082, format="ii5fH2x", values=[120, 103, 20, 450, 1, 1, 1, 1]),
        ]),
        *[dict(base, name=name, commands=[building], conduits=[
            dict(op="add", name="pipe", temperature=temperature, mass=10, element="Water",
                 building=0, insulated=insulated),
            *[dict(step=step, op="update") for step in range(1, 8)],
        ]) for name, temperature, insulated in [
            ("conduit-hot", 600, False), ("conduit-cold", 200, False),
            ("conduit-insulated", 600, True)]],
        *[dict(base, name=name, commands=[building], elements=phases, conduits=[
            dict(op="add", name="pipe", temperature=temperature, mass=10, element="Water", building=0),
            dict(step=1, op="update"), dict(step=2, op="update"),
        ]) for name, temperature in [("conduit-freeze", 200), ("conduit-melt", 450)]],
        dict(base, name="conduit-remove-reuse", commands=[building], conduits=[
            dict(op="add", name="a", temperature=600, mass=10, element="Water", building=0),
            dict(op="add", name="b", temperature=400, mass=1, element="Oxygen", building=0),
            dict(step=1, op="update"), dict(step=2, op="remove", name="a"),
            dict(step=2, op="update"), dict(step=3, op="update"),
            dict(step=4, op="add", name="c", temperature=500, mass=10, element="Water", building=0),
            dict(step=4, op="update"),
        ]),
        dict(base, name="conduit-clear-reset", commands=[building], conduits=[
            dict(op="add", name="a", temperature=600, mass=10, element="Water", building=0),
            dict(step=1, op="update"),
            dict(step=2, op="set", name="a", temperature=290, mass=1, element="Oxygen"),
            dict(step=2, op="update"),
            dict(step=3, op="clear"), dict(step=3, op="update"),
            dict(step=4, op="add", name="b", temperature=400, mass=10, element="Water", building=0),
            dict(step=4, op="update"),
            dict(step=5, op="shutdown"), dict(step=5, op="initialize"),
            dict(step=6, op="add", name="c", temperature=350, mass=1, element="Oxygen", building=0),
            dict(step=6, op="update"),
        ]),
        dict(base, name="conduit-invalid-temperature-report", commands=[building],
             expected_diagnostic=dict(op="set", id=1, message_contains="invalid temperature",
                                      reference_assert="contents_temperature <= SIM_MAX_TEMPERATURE"), conduits=[
                 dict(op="add", name="a", temperature=600, mass=10, element="Water", building=0),
                 dict(step=1, op="set", name="a", temperature=10001, mass=1, element="Water"),
             ]),
        dict(base, name="conduit-invalid-add-temperature-report", commands=[building],
             expected_diagnostic=dict(op="add", id=1, message_contains="invalid temperature",
                                      reference_assert="contents_temperature <= SIM_MAX_TEMPERATURE"), conduits=[
                 dict(op="add", name="a", temperature=10001, mass=10, element="Water", building=0),
             ]),
        *[dict(base, name=name, fill=cell(element, mass), commands=[
            dict(id=2024405073, format="iiBBH", values=[119, 101, 2, configuration, index]),
            dict(step=1, id=1575539738, format="iif", values=[0, 119, .5]),
            dict(step=5, id=894417742, format="ii", values=[0, 102]),
        ]) for name, element, mass, configuration, index in [
            ("consumer-specific-gas", "Oxygen", 1, 0, 2),
            ("consumer-any-gas", "Oxygen", 1, 2, 2),
            ("consumer-any-liquid", "Water", 1000, 1, 3)]],
        dict(base, name="consumer-disease", diseases=[{}],
             fill=cell("Oxygen", disease=0, disease_count=10000), commands=[
                 dict(id=2024405073, format="iiBBH", values=[119, 101, 2, 0, 2]),
                 dict(step=1, id=1575539738, format="iif", values=[0, 119, .5]),
             ]),
        *[dict(base, name=name, fill=cell("Oxygen", pressure), commands=[
            dict(id=-505471181, format="fiii", values=[2, 101, 301, 302]),
            dict(step=1, id=403589164, format="ii4fiHBB", values=[0, 119, .2, .1, 300, 2, 0, 2, 1, 255]),
            dict(step=6, id=-1524118282, format="ii", values=[0, 102]),
        ]) for name, pressure in [("emitter-gas", 1), ("emitter-pressure-blocked", 4)]],
        dict(base, name="emitter-liquid", fill=cell("Vacuum", 0, 0), commands=[
            dict(id=-505471181, format="fiii", values=[100000, 101, 301, 302]),
            dict(step=1, id=403589164, format="ii4fiHBB", values=[0, 119, .2, 100, 300, 100000, 0, 3, 1, 255]),
        ]),
        dict(base, name="disease-consumer-lifecycle", diseases=[{}], commands=[
            dict(id=348345681, format="iii", values=[101, 0, 0]),
            dict(step=1, id=-1822987624, format="5i", values=[0, 0, 0, 0, 0]),
            dict(step=4, id=-781641650, format="ii", values=[0, 102]),
        ]),
        dict(base, name="cell-mass-emission-consumption", diseases=[{}], commands=[
            dict(id=797274363, format="iiffiHBx", values=[119, 101, .5, 400, 1000, 2, 0]),
            dict(step=2, id=1727657959, format="iifHBB", values=[119, 102, .2, 2, 2, 1]),
        ]),
        dict(base, name="falling-liquid-events", headless=False, fill=cell("Vacuum", 0, 0),
             regions=[region(5, 10, 5, 3, cell("Water", 1000))]),
        *[dict(base, name=f"radiation-emitter-kind{kind}", radiation=True, elements=radiation_elements,
               regions=[region(8, 5, 1, 6, cell("Granite", 1000))], commands=[
                   dict(id=-1505895314, format="iiHH5fi", values=[101, 119, 3, 2, 100, 1, .5, 0, 360, kind])
               ]) for kind in range(6)],
        dict(base, name="radiation-emitter-direction-modify", radiation=True, elements=radiation_elements, commands=[
            dict(id=-1505895314, format="iiHH5fi", values=[101, 119, 4, 4, 100, 0, 0, 45, 90, 0]),
            dict(step=3, id=-503965465, format="iiiHH5fi", values=[0, 119, 102, 4, 4, 80, 0, 0, 270, 45, 2]),
            dict(step=6, id=-704259919, format="ii", values=[0, 103]),
        ]),
        dict(base, name="radiation-cosmic", radiation=True, cosmic_radiation=100,
             elements=radiation_elements, boundary=cell("Vacuum", 0, 0)),
        dict(base, name="liquid-colored-textures", elements=visual_elements, fill=cell("Water", 500)),
        dict(base, name="sunlight-world-offset", elements=visual_elements, commands=[
            dict(id=-895846551, format="5i", values=[1, 0, 0, 16, 16])]),
        dict(base, name="sunlight-two-world-offsets", elements=visual_elements, commands=[
            dict(id=-895846551, format="9i", values=[2, 0, 0, 8, 16, 8, 0, 8, 16])],
             regions=[region(1, 10, 6, 1, cell("Granite", 1000))]),
        dict(base, name="sunlight-blocking-property", elements=visual_elements, commands=[
            dict(id=-895846551, format="5i", values=[1, 0, 0, 16, 16])],
             regions=[region(1, 10, 14, 1, cell("Oxygen", properties=0x20))]),
        dict(base, name="world-zones-inactive-decay", commands=[
            dict(id=-457308393, format="256B", values=[255 if i % 16 < 8 else 0 for i in range(256)]),
            dict(step=3, id=-449718014, format="iB3x", values=[119, 255]),
        ]),
        dict(base, name="active-region-subset", active_regions=[[2, 2, 10, 10, 0, 0]],
             regions=[region(3, 3, 5, 5, cell("Oxygen", 4, 600))]),
        dict(base, name="two-active-regions", active_regions=[[0, 0, 8, 15, 10000, 0], [8, 0, 16, 15, 20000, 0]],
             regions=[region(3, 3, 5, 5, cell("Oxygen", 4, 600))]),
        *[dict(base, name=name, dt=dt, commands=[
            dict(id=-505471181, format="fiii", values=[2, 101, 301, 302]),
            dict(step=1, id=403589164, format="ii4fiHBB", values=[0, 119, .2, .1, 300, 2, 0, 2, 1, 255]),
            dict(step=6, id=-1524118282, format="ii", values=[0, 102]),
        ]) for name, dt in [("emitter-multitick", .6), ("emitter-paused-frames", 0)]],
        *[dict(base, name=f"native-save-version{version}", steps=3, diseases=[{}],
               elements=legacy_elements, radiation=True, save_roundtrip=True, loads=[dict(
                   version=version, width=18, height=18, internal_offsets=True,
                   options=1, cell=cell("Oxygen", 1, 310, radiation=25,
                                        disease_hash=10001, disease_count=1000), regions=[
                       region(6, 6, 2, 2, cell("Granite", 1000, 500)),
                       region(7, 7, 1, 1, cell("ThermalBoundary", 1000, 10)),
                       region(11, 8, 2, 2, cell("Water", 1000, 290,
                                              disease_hash=987654321, disease_count=200)),
                   ])]) for version in range(16)],
        dict(base, name="legacy-load-retains-radiation", radiation=True, sequential_loads=True, loads=[
            dict(x=5, y=5, cell=cell("Oxygen", radiation=123)),
            dict(version=13, width=18, height=18, cell=cell("Oxygen", 1, 300)),
        ]),
        dict(base, name="world-initialize-vacuum", commands=[
            dict(id=-752676153, format="6i", values=[0, 0, 6, 6, 4, 4])]),
        dict(base, name="world-free-rectangle", commands=[
            dict(id=-895846551, format="5i", values=[1, 4, 4, 6, 6]),
            dict(step=2, id=-1167792921, format="4i", values=[6, 6, 4, 4]),
        ]),
        dict(base, name="world-free-reinitialize", commands=[
            dict(id=-895846551, format="5i", values=[1, 4, 4, 6, 6]),
            dict(step=1, id=-1167792921, format="4i", values=[6, 6, 4, 4]),
            dict(step=3, id=-752676153, format="6i", values=[0, 0, 6, 6, 4, 4]),
        ]),
        dict(base, name="world-clear-primary-retains-backwall", commands=[
            dict(id=-605421515, format="iH2xff", values=[119, 1, 500, 600]),
            dict(step=2, id=-1836204275, format="", values=[]),
        ]),
        *[dict(base, name=name, elements=sublimating, diseases=[{}],
               fill=cell("Oxygen", .1, 300), regions=[
                   region(6, 6, 3, 3, cell("Granite", mass, 300, disease=0, disease_count=10000))])
          for name, mass in [("solid-sublimation", 100), ("solid-sublimation-residue", .5)]],
        dict(base, name="solid-sublimation-vacuum", elements=sublimating,
             fill=cell("Vacuum", 0, 0), regions=[region(6, 6, 3, 3, cell("Granite", 100))]),
        *[dict(base, name=name, elements=offgassing, diseases=[{}],
               fill=cell("Oxygen", .1), regions=[
                   region(2, 1, 12, 5, cell("Water", mass, 300, disease=0, disease_count=10000))])
          for name, mass in [("liquid-offgas", 1000), ("liquid-offgas-residue", .005)]],
        *[dict(base, name=name, elements=partial, diseases=[{}], headless=headless,
               fill=cell("Oxygen", 100, 700), regions=[
                   region(6, 6, 3, 3, cell("Granite", 1000, 300, disease=0, disease_count=10000))])
          for name, headless in [("partial-solid-melt", True), ("partial-solid-melt-falling", False)]],
        dict(base, name="partial-liquid-boil", elements=partial, diseases=[{}],
             fill=cell("Oxygen", 1000, 700), regions=[
                 region(3, 1, 10, 5, cell("Water", 1000, 300, disease=0, disease_count=10000))]),
    ]
