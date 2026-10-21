"""Authored thermal wire-word cases; these are not actual-colony acceptance."""


def thermal_boundary_cases():
    from host import elements

    result = []
    for conductivity in (0x3f800000, 0x7f800000):
        for insulation in (0, 255):
            for reverse in (False, True):
                for nan_surface in (False, True):
                    records = elements()
                    records[1]['float_word_overrides'] = dict(conductivity=0x3f800000,
                        solid_surface=0x7fc00111 if nan_surface else 0x3f800000)
                    records.append(dict(name='AuthoredBoundaryThermal', id=123456792,
                        state=3, shc=2, conductivity=1,
                        float_word_overrides=dict(conductivity=conductivity,
                            solid_surface=0xffc00222 if nan_surface else 0x3f800000)))
                    first = dict(element='Granite', mass=1000, temperature=900, insulation=255)
                    second = dict(element='AuthoredBoundaryThermal', mass=1000,
                                  temperature=300, insulation=insulation)
                    if reverse:
                        first, second = second, first
                    result.append(dict(
                        name=f'thermal-word-k{conductivity:08x}-i{insulation}-r{int(reverse)}-n{int(nan_surface)}',
                        width=8, height=8, steps=2, elements=records,
                        fill=dict(element='Unobtanium', mass=10000, temperature=300),
                        regions=[dict(x=2, y=2, width=1, height=1, cell=first),
                                 dict(x=3, y=2, width=1, height=1, cell=second)]))
    for cell_word in (0x3f800000, 0x7f800000):
        for wall_word in (0x3f800000, 0x7fc01234):
            for insulation in (0, 255):
                for reverse in (False, True):
                    records = elements()
                    records[1]['float_word_overrides'] = dict(conductivity=cell_word,
                                                               solid_surface=0x3f800000)
                    records.append(dict(name='AuthoredBoundaryWall', id=123456793,
                        state=3, shc=2, conductivity=1,
                        float_word_overrides=dict(conductivity=wall_word, solid_surface=0x3f800000)))
                    result.append(dict(
                        name=f'wall-word-c{cell_word:08x}-w{wall_word:08x}-i{insulation}-r{int(reverse)}',
                        width=8, height=8, steps=2, elements=records,
                        fill=dict(element='Unobtanium', mass=10000, temperature=300),
                        regions=[dict(x=3, y=3, width=1, height=1,
                            cell=dict(element='Granite', mass=1000,
                                      temperature=300 if reverse else 900, insulation=insulation))],
                        commands=[dict(id=-605421515, format='iH2xff',
                            values=[27, len(records) - 1, 1000, 900 if reverse else 300])]))
    return result
