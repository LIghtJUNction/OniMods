use crate::events::{ConsumedMassInfo, EventQueues};
use crate::fluid_helpers::{calculate_disease, clear_cell, clear_disease, notify};
use crate::numeric::{cvtt_f32_i32, smax, smin};
use crate::status::Result;
use crate::tables::{DiseaseTable, ElementTable};
use crate::world::PhysicsWorld;

// ELF 0x52f60: x advances first; each accepted cell consumes the current remainder.
pub(crate) fn rect_remove(
    world: &mut PhysicsWorld,
    _elements: &ElementTable,
    diseases: &DiseaseTable,
    events: &mut EventQueues,
    x: usize,
    y: usize,
    width: u8,
    height: u8,
    requested: f32,
    target: u16,
    info: &mut ConsumedMassInfo,
) -> Result<()> {
    let mut remaining = requested;
    for row in y..y + height as usize {
        for column in x..x + width as usize {
            let cell = row * world.stride + column;
            if world.write.element[cell] == target {
                let previous_mass = world.write.mass[cell];
                let (amount, next_mass, ratio) = if remaining > previous_mass {
                    (previous_mass, 0.0, 1.0)
                } else {
                    (
                        remaining,
                        previous_mass - remaining,
                        remaining / previous_mass,
                    )
                };
                let old_disease = world.write.disease_idx[cell];
                let removed_count = cvtt_f32_i32(world.write.disease_count[cell] as f32 * ratio);
                world.write.mass[cell] = next_mass;
                remaining -= amount;
                world.write.disease_count[cell] =
                    world.write.disease_count[cell].wrapping_sub(removed_count);
                if world.write.disease_count[cell] < 1 {
                    clear_disease(&mut world.write, cell);
                }
                let old_mass = info.mass;
                let old_temperature = info.temperature;
                let temperature = world.write.temperature[cell];
                let (minimum, maximum) = if temperature > old_temperature {
                    (old_temperature, temperature)
                } else {
                    (temperature, old_temperature)
                };
                let total = old_mass + amount;
                let weighted = (old_temperature * old_mass + temperature * amount) / total;
                info.mass = total;
                info.temperature = smax(smin(weighted, maximum), minimum);
                if old_disease != 255 {
                    let (id, count) = calculate_disease(
                        diseases,
                        info.disease_idx,
                        info.disease_count,
                        old_disease,
                        removed_count,
                    );
                    info.disease_idx = id;
                    info.disease_count = count;
                }
                if world.write.mass[cell] <= f32::MIN_POSITIVE {
                    clear_cell(&mut world.write, world.vacuum_element, cell);
                    notify(world, events, cell);
                }
            }
            if remaining <= 0.0 {
                return Ok(());
            }
        }
    }
    Ok(())
}
