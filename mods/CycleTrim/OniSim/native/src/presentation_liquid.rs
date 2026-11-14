//! Independent liquid-image equations from official ELF 0x646f0..0x64dd0.

use rayon::prelude::*;
use crate::numeric::{cvtt_f32_i32, smax, smin};
use crate::status::Result;
use crate::tables::{ElementRecord, ElementTable};
use crate::world::PhysicsWorld;
use crate::world_cells::filled;

#[cfg(test)]
#[path = "presentation_liquid_tests.rs"]
mod tests;

const MASS_SCALE: f32 = f32::from_bits(0x3a83126f);
const MASS_ALPHA_LIMIT: f32 = f32::from_bits(0x4479ffff);
const EXPONENT: f32 = f32::from_bits(0x3ee66666);
const BYTE_SCALE: f32 = f32::from_bits(0x3b808081);

/// Only the next row's already evaluated raw gradients are retained. This is
/// private worker scratch, not one of the images whose address is published.
#[derive(Default)]
pub(super) struct GradientCache {
    next_row: Vec<Option<f32>>,
}

pub(super) fn update(
    liquid: &mut [u32],
    liquid_data: &mut [u32],
    material: &mut [u32],
    exposed: &[u8],
    world: &PhysicsWorld,
    elements: &ElementTable,
    cache: &mut GradientCache,
) -> Result<()> {
    if world.height <= 1 {
        return Ok(());
    }
    let rows = world.height - 1;
    let width = world.width;
    let count = width * rows;
    let environment = crate::float_env::FloatEnv::capture();
    let reuse = environment.masks_all_exceptions();
    if reuse && let Some(pool) = crate::parallel::pool(world.width * world.height) {
        const CHUNK_ROWS: usize = 16;
        let chunk_cells = width * CHUNK_ROWS;
        let statuses: Vec<crate::float_env::FloatEnv> = pool.install(|| {
            (
                liquid[..count].par_chunks_mut(chunk_cells),
                liquid_data[..count].par_chunks_mut(chunk_cells),
                material[..count].par_chunks_mut(chunk_cells),
            )
                .into_par_iter()
                .enumerate()
                .map(|(chunk, (liq_chunk, data_chunk, mat_chunk))| {
                    let _env = environment.install();
                    let mut next_row: Vec<Option<f32>> = Vec::new();
                    let y_start = chunk * CHUNK_ROWS;
                    let chunk_rows = liq_chunk.len() / width;
                    for row_offset in 0..chunk_rows {
                        let y = y_start + row_offset;
                        let r0 = row_offset * width;
                        let r1 = r0 + width;
                        update_row(
                            y,
                            &mut liq_chunk[r0..r1],
                            &mut data_chunk[r0..r1],
                            &mut mat_chunk[r0..r1],
                            exposed,
                            &mut next_row,
                            true,
                            world,
                            elements,
                        );
                    }
                    crate::float_env::FloatEnv::capture()
                })
                .collect()
        });
        crate::float_env::FloatEnv::merge_masked_statuses(&statuses);
        return Ok(());
    }
    if cache.next_row.len() != width {
        cache.next_row = filled(width, None)?;
    } else {
        cache.next_row.fill(None);
    }
    // Officially the last public row retains its zero constructor image.
    for y in 0..rows {
        let r0 = y * width;
        let r1 = r0 + width;
        update_row(
            y,
            &mut liquid[r0..r1],
            &mut liquid_data[r0..r1],
            &mut material[r0..r1],
            exposed,
            &mut cache.next_row,
            reuse,
            world,
            elements,
        );
    }
    Ok(())
}

#[allow(clippy::too_many_arguments)]
fn update_row(
    y: usize,
    liquid_row: &mut [u32],
    liquid_data_row: &mut [u32],
    material_row: &mut [u32],
    exposed: &[u8],
    next_row: &mut Vec<Option<f32>>,
    reuse: bool,
    world: &PhysicsWorld,
    elements: &ElementTable,
) {
    let internal_row = (y + 1) * world.stride + 1;
    let row_elements = &world.write.element[internal_row..internal_row + world.width];
    let has_liquid = row_elements
        .iter()
        .any(|&id| elements.elements[id as usize].record.state & 3 == 2);
    if !has_liquid {
        if !next_row.is_empty() {
            next_row.fill(None);
        }
        for image in [liquid_row, liquid_data_row, material_row] {
            if image.iter().any(|&pixel| pixel != 0) {
                image.fill(0);
            }
        }
        return;
    }
    if next_row.len() != world.width {
        next_row.resize(world.width, None);
    }
    let single_species_only = world.extra_mixtures.is_empty();
    let exposed_start = y * world.width;
    for (x, next_gradient) in next_row.iter_mut().enumerate() {
        // Consume every column, including non-liquids, so no entry survives
        // beyond the one row for which it was calculated.
        let previous_gradient = next_gradient.take();
        let cell = internal_row + x;
        let element = &elements.elements[row_elements[x] as usize].record;
        if element.state & 3 != 2 {
            liquid_row[x] = 0;
            liquid_data_row[x] = 0;
            material_row[x] = 0;
            continue;
        }
        let public = exposed_start + x;
        let above = cell + world.stride;
        let above_state = record(world, elements, above).state;
        let mass = world.write.mass[cell];
        let alpha = if (world.read.properties[above] | above_state) & 2 == 0
            && !(mass > MASS_ALPHA_LIMIT)
        {
            cvtt_f32_i32((mass * MASS_SCALE).powf(EXPONENT) * 255.0) as u32
        } else {
            255
        };
        let gradient = if above_state & 2 == 0 {
            0.0
        } else {
            let value =
                previous_gradient.unwrap_or_else(|| raw_gradient(cell, public, exposed, world));
            if value > 0.0 {
                smin(
                    gradient_above(
                        above,
                        public + world.width,
                        exposed,
                        world,
                        elements,
                        if reuse { Some(next_gradient) } else { None },
                    ),
                    value,
                )
            } else {
                smin(1.0, value)
            }
        };
        let rgb = if !(world.mixture_mass[cell] > 0.0)
            && (single_species_only || !world.extra_mixtures.contains_key(&cell))
        {
            gradient_color(element, gradient) & 0x00ff_ffff
        } else {
            // 真实多组分混合液体视觉渲染：将所有组分液体的颜色按质量占比物理混合（支持任意数量混合）！
            let mut total_liquid_mass = 0.0f32;
            let mut total_r = 0.0f32;
            let mut total_g = 0.0f32;
            let mut total_b = 0.0f32;

            world.for_each_cell_species(cell, |elem_id, sp_mass| {
                if sp_mass <= 0.0 || elem_id as usize >= elements.elements.len() {
                    return;
                }
                let rec = &elements.elements[elem_id as usize].record;
                if rec.state & 3 != 2 {
                    return;
                }
                let sp_rgb = gradient_color(rec, gradient) & 0x00ff_ffff;
                total_liquid_mass += sp_mass;
                total_r += sp_mass * (sp_rgb & 0xff) as f32;
                total_g += sp_mass * ((sp_rgb >> 8) & 0xff) as f32;
                total_b += sp_mass * ((sp_rgb >> 16) & 0xff) as f32;
            });

            if total_liquid_mass > 0.0 {
                let r = ((total_r / total_liquid_mass).round() as u32).min(255);
                let g = ((total_g / total_liquid_mass).round() as u32).min(255);
                let b = ((total_b / total_liquid_mass).round() as u32).min(255);
                (r & 0xff) | ((g & 0xff) << 8) | ((b & 0xff) << 16)
            } else {
                gradient_color(element, gradient) & 0x00ff_ffff
            }
        };
        liquid_row[x] = (alpha << 24) | rgb;
        liquid_data_row[x] =
            transition_color(element, world.write.temperature[cell], elements);
        material_row[x] = element.material_properties & 0xff00_000f;
    }
}

fn record<'a>(world: &PhysicsWorld, elements: &'a ElementTable, cell: usize) -> &'a ElementRecord {
    &elements.elements[world.write.element[cell] as usize].record
}

fn gradient_above(
    cell: usize,
    public: usize,
    exposed: &[u8],
    world: &PhysicsWorld,
    elements: &ElementTable,
    next_gradient: Option<&mut Option<f32>>,
) -> f32 {
    let state = record(world, elements, cell).state;
    if state & 3 != 2 {
        return if state & 2 != 0 { 1.0 } else { 0.0 };
    }
    let above = cell + world.stride;
    if record(world, elements, above).state & 2 == 0 {
        return 0.0;
    }
    let value = raw_gradient(cell, public, exposed, world);
    // Preserve the raw value, before the original smin, and only cache the
    // bounded finite nonnegative-mass case. Negative/NaN/infinite masses keep
    // their original powf calls (including libm errno and exceptional effects).
    if let Some(next_gradient) = next_gradient {
        if exposed[public] == 255 || world.write.mass[cell].to_bits() < 0x7f80_0000 {
            *next_gradient = Some(value);
        }
    }
    smin(1.0, value)
}

fn raw_gradient(cell: usize, public: usize, exposed: &[u8], world: &PhysicsWorld) -> f32 {
    if exposed[public] == 255 {
        0.0
    } else {
        (1.0 - exposed[public] as f32 * BYTE_SCALE)
            * smin(world.write.mass[cell] / 1000.0, 1.0).powf(EXPONENT)
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
