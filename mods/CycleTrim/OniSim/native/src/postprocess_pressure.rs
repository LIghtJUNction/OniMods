use crate::events::{EventQueues, WorldDamageInfo};
use crate::fluid_helpers::{game_index, liquid_density, phase, smin, swap_material};
use crate::tables::{ElementRecord, ElementTable};
use crate::world::PhysicsWorld;

pub(crate) fn density_displacement(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    events: &mut EventQueues,
    cell: usize,
    source: &ElementRecord,
    probability: f32,
) -> bool {
    let below = cell - world.stride;
    let below_record = &elements.elements[world.write.element[below] as usize].record;
    if source.state != below_record.state {
        return false;
    }
    // 液体与气体的重力分层置换
    let should_sink = if source.state & 3 == 2 {
        liquid_density(source) > liquid_density(below_record)
    } else {
        source.molar_mass > below_record.molar_mass
    };
    if should_sink {
        swap_material(world, events, cell, below);
        return true;
    }
    let sample = world.next_random() as f32 * f32::from_bits(0x38000100);
    if !(probability < sample) {
        return false;
    }
    if world.write.element[cell] != world.write.element[below] {
        return false;
    }
    let temperature = world.write.temperature[cell];
    let lower_temperature = world.write.temperature[below];
    if !(temperature < lower_temperature) {
        return false;
    }
    if source.state & 3 == 2 {
        let mass = world.write.mass[cell];
        let lower_mass = world.write.mass[below];
        let equilibrium =
            (temperature * mass + lower_temperature * lower_mass) / (mass + lower_mass);
        // ELF 0x6fe48..0x6fe51 clamps from the cold side first. Rounding
        // can put the weighted mean just below that endpoint; retaining the
        // hot endpoint instead creates a one-ULP warming on each convection.
        let result = if temperature <= equilibrium {
            smin(lower_temperature, equilibrium)
        } else {
            temperature
        };
        world.write.temperature[cell] = result;
        world.write.temperature[below] = result;
    } else {
        // Same-element gas swaps have no substance event or dirty-bit write.
        world.write.mass.swap(cell, below);
        world.write.temperature.swap(cell, below);
        world.write.disease_idx.swap(cell, below);
        world.write.disease_count.swap(cell, below);
        world.write.disease_infestation.swap(cell, below);
        world.write.disease_growth.swap(cell, below);
    }
    true
}

fn breakable(world: &PhysicsWorld, elements: &ElementTable, cell: usize) -> bool {
    let record = &elements.elements[world.write.element[cell] as usize].record;
    record.state & 3 == 3 && record.state & 4 == 0 && world.write.properties[cell] & 8 == 0
}

fn resistance(
    world: &PhysicsWorld,
    elements: &ElementTable,
    cell: usize,
    right_first: bool,
) -> (f32, f32) {
    let record = &elements.elements[world.write.element[cell] as usize].record;
    let packed = world.write.strength[cell];
    let scaled = (packed >> 7) as f32;
    let extra = if 1.0 <= scaled {
        0.0
    } else {
        f32::from_bits(0x3dccccd0)
    };
    let mass_scale = (1.0 - scaled) + (world.write.mass[cell] / record.max_mass) * scaled;
    let strength = if right_first {
        mass_scale * record.strength * (packed & 127) as f32 * 0.25
    } else {
        (packed & 127) as f32 * 0.25 * mass_scale * record.strength
    };
    (strength, extra)
}

fn directional_break(
    world: &PhysicsWorld,
    elements: &ElementTable,
    events: &mut EventQueues,
    cell: usize,
    pressure: f32,
    direction: isize,
) -> bool {
    let at = |distance: isize| {
        cell.checked_add_signed(direction * distance)
            .filter(|&index| index < world.write.element.len())
    };
    let Some(first) = at(1) else {
        return false;
    };
    if !breakable(world, elements, first) {
        return false;
    }
    let (strength, extra) = resistance(world, elements, first, direction == 1);
    let mut limit = strength + 1.0 + extra;
    if pressure < limit {
        return false;
    }
    let Some(second) = at(2) else {
        return false;
    };
    let (outside, wall_count) = if phase(elements, world.write.element[second]) == 3 {
        if !breakable(world, elements, second) {
            return false;
        }
        let record = &elements.elements[world.write.element[second] as usize].record;
        let packed = world.write.strength[second];
        let scaled = (packed >> 7) as f32;
        let extra = if 1.0 <= scaled {
            0.0
        } else {
            f32::from_bits(0x3dccccd0)
        };
        let scale = if direction == 1 {
            (world.write.mass[second] / record.max_mass) * scaled + (1.0 - scaled)
        } else {
            (1.0 - scaled) + (world.write.mass[second] / record.max_mass) * scaled
        };
        let added = if direction == 1 {
            scale * record.strength * 0.25 * (packed & 127) as f32
        } else {
            scale * record.strength * (packed & 127) as f32 * 0.25
        };
        limit = limit + added + extra;
        if pressure < limit {
            return false;
        }
        let Some(third) = at(3) else {
            return false;
        };
        if phase(elements, world.write.element[third]) == 3 {
            return false;
        }
        (third, 2)
    } else {
        (second, 1)
    };
    if pressure <= limit {
        return false;
    }
    if phase(elements, world.write.element[outside]) == 2 && 0.0 < world.write.mass[outside] {
        let record = &elements.elements[world.write.element[outside] as usize].record;
        if pressure - world.write.mass[outside] / record.max_mass <= limit {
            return false;
        }
    }
    if let Some(source) = game_index(world, cell) {
        for wall in [first, second].into_iter().take(wall_count) {
            if let Some(game_cell) = game_index(world, wall) {
                events.world_damage_info.push(WorldDamageInfo {
                    game_cell,
                    damage_source_offset: source,
                });
            }
        }
    }
    true
}

pub(crate) fn pressure_break(
    world: &PhysicsWorld,
    elements: &ElementTable,
    events: &mut EventQueues,
    cell: usize,
    pressure: f32,
) -> bool {
    for direction in [-(world.stride as isize), -1, 1, world.stride as isize] {
        if directional_break(world, elements, events, cell, pressure, direction) {
            return true;
        }
    }
    false
}

#[cfg(test)]
mod tests {
    use super::*;

    fn make_test_record(id: i32, max_mass: f32, molar_mass: f32, state: u8) -> ElementRecord {
        let mut record: ElementRecord = unsafe { std::mem::zeroed() };
        record.id = id;
        record.max_mass = max_mass;
        record.molar_mass = molar_mass;
        record.state = state;
        record
    }

    #[test]
    fn dirty_water_is_denser_than_clean_water_and_sinks() {
        let clean = make_test_record(1836671383, 1000.0, 18.01528, 2); // Water
        let dirty = make_test_record(1832607973, 1000.0, 18.01528, 2); // DirtyWater
        let brine = make_test_record(-324547888, 1000.0, 18.01528, 2); // Brine
        let oil = make_test_record(-1412059381, 870.0, 100.0, 2); // CrudeOil

        // 验证沉降顺序：浓盐水 > 污染水 > 洁净水 > 原油
        assert!(liquid_density(&brine) > liquid_density(&dirty));
        assert!(liquid_density(&dirty) > liquid_density(&clean));
        assert!(liquid_density(&clean) > liquid_density(&oil));
    }
}
