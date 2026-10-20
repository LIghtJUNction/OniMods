//! Independent liquid-image equations from official ELF 0x646f0..0x64dd0.

use crate::numeric::{cvtt_f32_i32, smax, smin};
use crate::tables::{ElementRecord, ElementTable};
use crate::world::PhysicsWorld;

const MASS_SCALE: f32 = f32::from_bits(0x3a83126f);
const MASS_ALPHA_LIMIT: f32 = f32::from_bits(0x4479ffff);
const EXPONENT: f32 = f32::from_bits(0x3ee66666);
const BYTE_SCALE: f32 = f32::from_bits(0x3b808081);

pub(super) fn update(
    liquid: &mut [u32],
    liquid_data: &mut [u32],
    material: &mut [u32],
    exposed: &[u8],
    world: &PhysicsWorld,
    elements: &ElementTable,
) {
    // Officially the last public row retains its zero constructor image.
    for y in 0..world.height.saturating_sub(1) {
        for x in 0..world.width {
            let public = y * world.width + x;
            let cell = world.internal_index(public);
            let element = record(world, elements, cell);
            if element.state & 3 != 2 {
                liquid[public] = 0;
                liquid_data[public] = 0;
                material[public] = 0;
                continue;
            }
            let above = cell + world.stride;
            let mass = world.write.mass[cell];
            let alpha = if (world.read.properties[above] | record(world, elements, above).state) & 2
                == 0
                && !(mass > MASS_ALPHA_LIMIT)
            {
                cvtt_f32_i32((mass * MASS_SCALE).powf(EXPONENT) * 255.0) as u32
            } else {
                255
            };
            let gradient = estimated_gradient(cell, true, exposed, world, elements);
            liquid[public] = (alpha << 24) | (gradient_color(element, gradient) & 0x00ff_ffff);
            liquid_data[public] =
                transition_color(element, world.write.temperature[cell], elements);
            material[public] = element.material_properties & 0xff00_000f;
        }
    }
}

fn record<'a>(world: &PhysicsWorld, elements: &'a ElementTable, cell: usize) -> &'a ElementRecord {
    &elements.elements[world.write.element[cell] as usize].record
}

fn estimated_gradient(
    cell: usize,
    check_above: bool,
    exposed: &[u8],
    world: &PhysicsWorld,
    elements: &ElementTable,
) -> f32 {
    let state = record(world, elements, cell).state;
    if state & 3 != 2 {
        return if state & 2 != 0 { 1.0 } else { 0.0 };
    }
    let above = cell + world.stride;
    if record(world, elements, above).state & 2 == 0 {
        return 0.0;
    }
    let public = world.public_index(cell).unwrap();
    let value = if exposed[public] == 255 {
        0.0
    } else {
        (1.0 - exposed[public] as f32 * BYTE_SCALE)
            * smin(world.write.mass[cell] / 1000.0, 1.0).powf(EXPONENT)
    };
    if value > 0.0 && check_above {
        smin(
            estimated_gradient(above, false, exposed, world, elements),
            value,
        )
    } else {
        smin(1.0, value)
    }
}

fn gradient_color(record: &ElementRecord, value: f32) -> u32 {
    let mut color = record.color;
    let count = record.gradient_count as usize;
    for index in 0..count {
        let first = record.gradient[index];
        let threshold = (first >> 24) as f32 * BYTE_SCALE;
        if !(threshold <= value) {
            continue;
        }
        let second = record
            .gradient
            .get(index + 1)
            .copied()
            .filter(|_| index + 1 < count)
            .unwrap_or(first);
        let interval = if index + 1 < count {
            (second >> 24) as f32 * BYTE_SCALE - threshold
        } else {
            0.0
        };
        let fraction = smax(smin(1.0, (value - threshold) / interval), 0.0);
        color = lerp_color(first, second, fraction);
    }
    color
}

fn lerp_color(first: u32, second: u32, fraction: f32) -> u32 {
    let mut color = 0;
    for shift in [0, 8, 16, 24] {
        let a = if shift == 24 {
            (first as i32) >> 24
        } else {
            ((first >> shift) & 255) as i32
        };
        let b = if shift == 24 {
            (second as i32) >> 24
        } else {
            ((second >> shift) & 255) as i32
        };
        let channel = cvtt_f32_i32((b - a) as f32 * fraction).wrapping_add(a);
        color |= (channel as u32).wrapping_shl(shift);
    }
    color
}

fn transition_color(record: &ElementRecord, temperature: f32, elements: &ElementTable) -> u32 {
    let fraction = ((3.0 + temperature) - record.low_temperature)
        / ((6.0 + record.high_temperature) - record.low_temperature);
    let alpha = if fraction < 0.0 {
        0
    } else if fraction > 1.0 {
        0xff00_0000
    } else {
        (cvtt_f32_i32(255.0 * fraction) as u32) << 24
    };
    // COMISS/JB selects the low transition for an unordered fraction too.
    let transition = if fraction >= 0.5 {
        record.high_transition
    } else {
        record.low_transition
    };
    let color = if transition == u16::MAX {
        0x00ff_ffff
    } else {
        elements.elements[transition as usize].record.color & 0x00ff_ffff
    };
    alpha | color
}
