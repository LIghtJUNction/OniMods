//! Sunlight bytes from official ELF 0x64dd0; table setup at 0x37750.

use crate::numeric::{cvtt_f32_i32, smax, smin};
use crate::tables::ElementTable;
use crate::world::PhysicsWorld;

pub(super) const OVERRIDE_HASH: u32 = 0x2531_469c;

#[derive(Clone, Copy)]
struct Material {
    index: usize,
    mass_scale: f32,
    absorption: f32,
}

pub(super) fn update(exposed: &mut [u8], world: &PhysicsWorld, elements: &ElementTable) {
    // Columns have independent accumulators. Read a short contiguous row span
    // before moving down, instead of striding through the entire world for
    // each pixel column. A bounded stack tile needs no per-frame allocation.
    // With unmasked exceptions, retain the original global arithmetic order.
    if !crate::float_env::FloatEnv::capture().masks_all_exceptions() {
        update_columns(exposed, world, elements);
        return;
    }
    const COLUMNS: usize = 64;
    // Lazy entries execute the reciprocal only when a material is actually
    // used. Its masked sticky exception effects then persist for this update.
    // A direct-mapped stack cache remains bounded for arbitrary element tables.
    let mut materials = [Material {
        index: usize::MAX,
        mass_scale: 0.0,
        absorption: 0.0,
    }; 64];
    let override_index = elements
        .elements
        .iter()
        .rposition(|element| element.record.id as u32 == OVERRIDE_HASH);
    for &[x, y, width, height] in &world.world_offsets {
        if width <= 0 {
            continue;
        }
        let top = (y as i64 + height as i64 - 1) as usize;
        let (x, y, width) = (x as usize, y as usize, width as usize);
        for first in (x..x + width).step_by(COLUMNS) {
            let columns = (x + width - first).min(COLUMNS);
            // Zero/negative-height rectangles still write their top row.
            let top_start = top * world.width + first;
            exposed[top_start..top_start + columns].fill(255);
            if height <= 1 {
                continue;
            }
            let mut remaining = [1.0; COLUMNS];
            for row in (y..top).rev() {
                let public_start = row * world.width + first;
                let internal_start = (row + 1) * world.stride + first + 1;
                let properties = &world.write.properties[internal_start..internal_start + columns];
                let above_start = internal_start + world.stride;
                let masses = &world.write.mass[above_start..above_start + columns];
                let pixels = &mut exposed[public_start..public_start + columns];
                if properties[0] & 0x30 == 0x30
                    && properties.iter().all(|flags| flags & 0x30 == 0x30)
                {
                    // A uniform override span needs neither element lookups nor
                    // a material-cache branch for each cell. Arithmetic within
                    // each independent column keeps its original operation order.
                    let record = &elements.elements[override_index.unwrap()].record;
                    let scale = if record.state & 3 == 3 {
                        f32::INFINITY
                    } else {
                        1.0 / record.default_mass
                    };
                    for ((remaining, &mass), pixel) in remaining[..columns]
                        .iter_mut().zip(masses).zip(pixels)
                    {
                        let absorption = record.light_absorption * smin(mass * scale, 1.0);
                        let value = smax(*remaining - absorption, 0.0);
                        *remaining = value;
                        *pixel = cvtt_f32_i32(value * 255.0) as u8;
                    }
                    continue;
                }
                let element_ids = &world.write.element[above_start..above_start + columns];
                for (offset, remaining) in remaining[..columns].iter_mut().enumerate() {
                    let flags = properties[offset];
                    let (mass_scale, light_absorption) = if flags & 0x30 == 0x30 {
                        // The override has one known record, so indexing a
                        // general material cache only adds work on this path.
                        let record = &elements.elements[override_index.unwrap()].record;
                        let scale = if record.state & 3 == 3 {
                            f32::INFINITY
                        } else {
                            1.0 / record.default_mass
                        };
                        (scale, record.light_absorption)
                    } else {
                        let index = element_ids[offset] as usize;
                        let material = &mut materials[index % 64];
                        if material.index != index {
                            let record = &elements.elements[index].record;
                            material.index = index;
                            material.mass_scale = if record.state & 3 == 3 {
                                f32::INFINITY
                            } else {
                                1.0 / record.default_mass
                            };
                            material.absorption = record.light_absorption;
                        }
                        (material.mass_scale, material.absorption)
                    };
                    let absorption =
                        light_absorption * smin(masses[offset] * mass_scale, 1.0);
                    let value = smax(*remaining - absorption, 0.0);
                    *remaining = if flags & 0x30 != 0x30 && flags & 0x20 != 0 {
                        0.0
                    } else {
                        value
                    };
                    pixels[offset] = cvtt_f32_i32(value * 255.0) as u8;
                }
            }
        }
    }
}

fn update_columns(exposed: &mut [u8], world: &PhysicsWorld, elements: &ElementTable) {
    let override_index = elements
        .elements
        .iter()
        .rposition(|element| element.record.id as u32 == OVERRIDE_HASH);
    // World offsets are public x/y/width/height, independent of active regions.
    // Different columns do not share an accumulator, so this uses one scalar.
    for &[x, y, width, height] in &world.world_offsets {
        if width <= 0 {
            continue;
        }
        let top = (y as i64 + height as i64 - 1) as usize;
        let (x, y, width) = (x as usize, y as usize, width as usize);
        for column in x..x + width {
            // The native top-row write also happens for a zero-height rectangle.
            exposed[top * world.width + column] = 255;
            if height <= 1 {
                continue;
            }
            let mut remaining = 1.0;
            for row in (y..top).rev() {
                let public = row * world.width + column;
                // row/column are already known and validated. Reconstructing
                // them from public would divide and take a remainder per cell.
                let current = (row + 1) * world.stride + column + 1;
                let above = current + world.stride;
                let flags = world.write.properties[current];
                let index = if flags & 0x30 == 0x30 {
                    override_index.unwrap()
                } else {
                    world.write.element[above] as usize
                };
                let record = &elements.elements[index].record;
                let mass_scale = if record.state & 3 == 3 {
                    f32::INFINITY
                } else {
                    1.0 / record.default_mass
                };
                let absorption =
                    record.light_absorption * smin(world.write.mass[above] * mass_scale, 1.0);
                let value = smax(remaining - absorption, 0.0);
                remaining = if flags & 0x30 != 0x30 && flags & 0x20 != 0 {
                    0.0
                } else {
                    value
                };
                // The current byte precedes the current cell's blocking effect.
                exposed[public] = cvtt_f32_i32(value * 255.0) as u8;
            }
        }
    }
}

#[cfg(test)]
#[path = "presentation_sun_tests.rs"]
mod tests;
