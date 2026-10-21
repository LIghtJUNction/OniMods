"""Mixed-material and unstable-solid fixtures with authored element records."""


def physics_cases():
    from host import elements
    def cell(element, mass=1, temperature=300, **extra):
        return dict(element=element, mass=mass, temperature=temperature, **extra)
    def region(x, y, width, height, value):
        return dict(x=x, y=y, width=width, height=height, cell=value)
    mixed = elements() + [
        dict(name="AuthoredGas", id=123456789, state=1, molar_mass=64, shc=2,
             conductivity=.04, color=0xff887766),
        dict(name="AuthoredLiquid", id=123456790, state=2, molar_mass=128, shc=2,
             conductivity=.2, max_mass=2500, flow=.2, viscosity=.5, color=0xff887766),
    ]
    base = dict(width=16, height=16, steps=32, elements=mixed, diseases=[{}, dict(strength=2)])
    result = [dict(base, name=f"mixed-gas-seed{seed}", seed=seed, regions=[
        region(1, 1, 7, 14, cell("AuthoredGas", 4, 500, disease=0, disease_count=10000)),
        region(8, 1, 7, 14, cell("Oxygen", .5, 280, disease=1, disease_count=20000)),
    ]) for seed in (1, 123456789)]
    result += [dict(base, name=f"mixed-liquid-inverted{inverted}",
        regions=[region(1, 1, 14, 6, cell("Water" if inverted else "AuthoredLiquid", 1000)),
                 region(1, 7, 14, 7, cell("AuthoredLiquid" if inverted else "Water", 1000, 350))])
        for inverted in (False, True)]
    sand = elements()
    sand[1].update(state=11)
    result += [dict(base, name=f"unstable-solid-headless{headless}", elements=sand, headless=headless,
        required_events=[] if headless else ["unstable"], required_changed_arrays=["element"],
        fill=cell("Vacuum", 0, 0), regions=[
            region(5, 10, 5, 3, cell("Granite", 1000, disease=0, disease_count=10000))])
        for headless in (True, False)]
    result += [dict(base, name=f"unstable-diagonal-headless{headless}", elements=sand, headless=headless,
        required_events=[] if headless else ["unstable"], required_changed_arrays=["element" if headless else "mass"],
        fill=cell("Vacuum", 0, 0), commands=[dict(id=1154135737, format="BB", values=[0, 1])],
        regions=[region(5, 5, 5, 1, cell("Unobtanium", 10000)),
                 region(5, 6, 5, 4, cell("Granite", 1000, disease=0, disease_count=10000))])
        for headless in (True, False)]
    result += [dict(base, name="liquid-pressure-damage", required_events=["damage"], regions=[
        region(3, 3, 10, 10, cell("Granite", 1000)),
        region(4, 4, 8, 8, cell("Water", 50000)),
        region(7, 7, 2, 2, cell("Vacuum", 0, 0)),
    ])]
    # Exercise both conductivity branches: insulation 255 selects the
    # logarithmic geometric mean, while 254 (and lower) selects the minimum.
    # Unequal heat capacities and surface areas prevent symmetric fixtures
    # from hiding a swapped operand or an incorrect equilibrium clamp.
    thermal = elements() + [dict(name="AuthoredThermalSolid", id=123456791,
        state=3, shc=7, conductivity=.013, solid_surface=3, liquid_surface=.4,
        gas_surface=.2)]
    for insulation in (0, 127, 254, 255):
        for hot_left in (False, True):
            result.append(dict(base, name=f"thermal-asymmetric-i{insulation}-hot{int(hot_left)}",
                elements=thermal, steps=12, save_roundtrip=True,
                fill=cell("Granite", 1600, 280, insulation=insulation),
                regions=[region(1, 1, 7, 14,
                                cell("Granite", 1600, 900 if hot_left else 280,
                                     insulation=insulation)),
                         region(8, 1, 7, 14,
                                cell("AuthoredThermalSolid", 70, 280 if hot_left else 900,
                                     insulation=255))]))
    for width, height in ((33, 35), (65, 67)):
        for reverse in (False, True):
            split = width // 2
            active = [[0, 0, split + 1, height - 1, 0, 0],
                      [split, 0, width, height - 1, 0, 0]]
            result.append(dict(base, name=f"region-overlap-{width}x{height}-reverse{int(reverse)}",
                width=width, height=height, steps=6, save_roundtrip=True,
                active_regions=list(reversed(active)) if reverse else active,
                fill=cell("Oxygen", 1, 290, disease=0, disease_count=1000),
                regions=[region(1, 1, split, height - 2, cell("Oxygen", 4, 600,
                                                        disease=1, disease_count=5000)),
                         region(split, 2, 2, height - 4, cell("Granite", 1000, 350))],
                required_changed_arrays=["temperature"]))
    # Two rectangular native worlds occupy distinct parts of the same padded
    # storage. Free/reinitialize one while retaining the other, then serialize
    # the whole state. These are authored ABI fixtures, not full DLC colonies.
    for side in (0, 1):
        x, width = (0, 30) if side == 0 else (32, 32)
        for repeat in (False, True):
            commands = [dict(id=-895846551, format="9i",
                             values=[2, 0, 0, 30, 30, 32, 0, 32, 30]),
                        dict(step=1, id=-1167792921, format="4i", values=[width, 30, x, 0]),
                        dict(step=3, id=-752676153, format="6i", values=[0, 0, width, 30, x, 0])]
            if repeat:
                commands.extend([
                    dict(step=4, id=-1167792921, format="4i", values=[width, 30, x, 0]),
                    dict(step=5, id=-1167792921, format="4i", values=[width, 30, x, 0])])
            result.append(dict(base, name=f"world-pair-free-side{side}-repeat{int(repeat)}",
                width=64, height=32, steps=8, save_roundtrip=True,
                active_regions=[[0, 0, 32, 31, 0, 0], [32, 0, 64, 31, 0, 0]],
                fill=cell("Oxygen", 2, 310, disease=0, disease_count=1000),
                regions=[region(31, 1, 2, 30, cell("Unobtanium", 20000)),
                         region(40, 3, 16, 10, cell("Water", 800, 330))],
                commands=commands, required_changed_arrays=["mass"]))
    result.append(dict(base, name="liquid-convection-rounded-below-cold", width=8, height=8,
        steps=6, seed=123456789, save_roundtrip=True,
        elements=[dict(item, conductivity=0) for item in elements()],
        fill=cell("Granite", 1000, 300, properties=3),
        regions=[region(4, 3, 1, 1, cell("Water", 34, 329.999542236328125, properties=3)),
                 region(4, 4, 1, 1, cell("Water", 797.49560546875, 329.99951171875,
                                       properties=3))],
        required_changed_arrays=["temperature"]))
    result.append(dict(base, name="world-pair-save-lifecycle-8", width=32, height=16,
        steps=8, save_roundtrip=True, save_roundtrip_count=8,
        fill=cell("Oxygen", 2, 310, disease=0, disease_count=1000),
        regions=[region(15, 1, 2, 14, cell("Unobtanium", 20000)),
                 region(20, 3, 8, 8, cell("Water", 800, 330))],
        commands=[dict(id=-895846551, format="9i", values=[2, 0, 0, 14, 14, 16, 0, 16, 14])]))
    from thermal_boundary_cases import thermal_boundary_cases
    result.extend(thermal_boundary_cases())
    return result
