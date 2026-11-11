//! Sunlight bytes from official ELF 0x64dd0; table setup at 0x37750.

use rayon::prelude::*;
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

#[derive(Default)]
pub(super) struct SunCache {
    tiles: Vec<Vec<u8>>,
}

#[cfg(test)]
pub(super) fn update(exposed: &mut [u8], world: &PhysicsWorld, elements: &ElementTable) {
    update_cached(exposed, world, elements, &mut SunCache::default());
}

pub(super) fn update_cached(exposed: &mut [u8], world: &PhysicsWorld,
    elements: &ElementTable, cache: &mut SunCache) {
    if !crate::float_env::FloatEnv::capture().masks_all_exceptions() {
        update_columns(exposed, world, elements);
        return;
    }
    const COLUMNS: usize = 16;
    let pool = crate::parallel::pool(world.width * world.height);
    let override_index = elements.elements.iter()
        .rposition(|element| element.record.id as u32 == OVERRIDE_HASH);
    // Rectangles remain ordered, including overlaps. Their column tiles have
    // independent accumulators and private retained output storage.
    for &[x, y, width, height] in &world.world_offsets {
        if width <= 0 { continue; }
        let top = (y as i64 + height as i64 - 1) as usize;
        let (x, y, width) = (x as usize, y as usize, width as usize);
        if height <= 1 {
            exposed[top * world.width + x..top * world.width + x + width].fill(255);
            continue;
        }
        let count = width.div_ceil(COLUMNS);
        cache.tiles.resize_with(count, Vec::new);
        for (index, tile) in cache.tiles.iter_mut().enumerate() {
            tile.resize((width - index * COLUMNS).min(COLUMNS) * (top - y + 1), 0);
        }
        let environment = crate::float_env::FloatEnv::capture();
        let render = |(index, tile): (usize, &mut Vec<u8>)| {
            let _environment = environment.install();
            render_tile(tile, world, elements, override_index,
                x + index * COLUMNS, (width - index * COLUMNS).min(COLUMNS), y, top)
        };
        let statuses: Vec<_> = if let Some(pool) = pool {
            pool.install(|| cache.tiles.par_iter_mut().enumerate().map(render).collect())
        } else { cache.tiles.iter_mut().enumerate().map(render).collect() };
        crate::float_env::FloatEnv::merge_masked_statuses(&statuses);
        let copy = |(row, output): (usize, &mut [u8])| {
            for (index, tile) in cache.tiles.iter().enumerate() {
                let columns = (width - index * COLUMNS).min(COLUMNS);
                let first = x + index * COLUMNS;
                output[first..first + columns].copy_from_slice(&tile[row * columns..(row + 1) * columns]);
            }
        };
        let output = &mut exposed[y * world.width..(top + 1) * world.width];
        if let Some(pool) = pool {
            pool.install(|| output.par_chunks_exact_mut(world.width).enumerate().with_min_len(8).for_each(copy));
        } else { output.chunks_exact_mut(world.width).enumerate().for_each(copy); }
    }
}

fn render_tile(exposed: &mut [u8], world: &PhysicsWorld, elements: &ElementTable,
    override_index: Option<usize>, first: usize, columns: usize, y: usize, top: usize)
    -> crate::float_env::FloatEnv {
    const COLUMNS: usize = 64;
    let mut materials = [Material {
        index: usize::MAX,
        mass_scale: 0.0,
        absorption: 0.0,
    }; 64];
    let mut completed = 0.0;
            // Zero/negative-height rectangles still write their top row.
            let top_start = (top - y) * columns;
            exposed[top_start..top_start + columns].fill(255);
            let mut remaining = [1.0; COLUMNS];
            for row in (y..top).rev() {
                let public_start = (row - y) * columns;
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
                        completed = value * 255.0;
                        *pixel = cvtt_f32_i32(completed) as u8;
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
                    completed = value * 255.0;
                    pixels[offset] = cvtt_f32_i32(completed) as u8;
                }
            }
    crate::float_env::FloatEnv::capture_after(completed)
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
