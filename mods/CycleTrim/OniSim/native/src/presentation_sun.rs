//! Sunlight bytes from official ELF 0x64dd0; table setup at 0x37750.

use crate::numeric::{cvtt_f32_i32, smax, smin};
use crate::tables::ElementTable;
use crate::world::PhysicsWorld;

pub(super) const OVERRIDE_HASH: u32 = 0x2531_469c;

pub(super) fn update(exposed: &mut [u8], world: &PhysicsWorld, elements: &ElementTable) {
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
