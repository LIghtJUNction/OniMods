//! Cell disease diffusion and growth from the official native implementation.

use crate::events::{DiseaseConsumptionCallback, Events};
use crate::float_env::FloatEnv;
use crate::fluid_helpers::{add_disease, clear_disease, phase};
use crate::numeric::cvtt_f32_i32;
use crate::status::{Error, Result, Status};
use crate::tables::{DiseaseTable, ElementTable, RangeInfo};
use crate::world::{PhysicsWorld, Region};
use rayon::prelude::*;

const DIFFUSION_FRACTION: f32 = 0.125;
const DISEASE_STEP: f32 = 0.2;

#[cfg(test)]
#[path = "disease_tests.rs"]
mod tests;

fn range_values(range: &RangeInfo) -> [f32; 4] {
    [
        range.min_viable,
        range.min_growth,
        range.max_growth,
        range.max_viable,
    ]
}

// ELF 0x34880. The central growth interval returns infinity. Infinite endpoint
// half lives also override interpolation, including at an interval endpoint.
pub(crate) fn calculate_range_half_life(
    value: f32,
    range: &RangeInfo,
    half_lives: &RangeInfo,
) -> f32 {
    let points = range_values(range);
    let lives = range_values(half_lives);
    let (low, high) = if !(value > points[0]) {
        (0, 0)
    } else if !(value > points[1]) {
        (0, 1)
    } else if !(value > points[2]) {
        return f32::INFINITY;
    } else if !(value > points[3]) {
        (2, 3)
    } else {
        (3, 3)
    };
    let first = lives[low];
    let second = lives[high];
    if first > f32::MAX || second > f32::MAX {
        return f32::INFINITY;
    }
    let span = points[high] - points[low];
    if !(span > 0.0) {
        return first;
    }
    let fraction = (value - points[low]) / span;
    first * (1.0 - fraction) + fraction * second
}

// ELF 0x34940 uses exp2f(-0.2 / half_life), including signed half lives.
// COMISS/JE preserves a NaN argument in the same branch as zero.
pub(crate) fn half_life_to_growth_rate(half_life: f32) -> f32 {
    if half_life == 0.0 || half_life.is_nan() {
        half_life
    } else if half_life > f32::MAX {
        1.0
    } else {
        (-DISEASE_STEP / half_life).exp2()
    }
}

// ELF 0x345d0 reads eligibility entirely from the old CellSOA snapshot.
fn diffusion_scale(world: &PhysicsWorld, diseases: &DiseaseTable, cell: usize) -> f32 {
    let id = world.read.disease_idx[cell];
    let disease = &diseases.diseases[id as usize];
    let element = &disease.elements[world.read.element[cell] as usize];
    if world.read.disease_count[cell] < element.min_diffusion_count
        || world.read.disease_infestation[cell] < element.min_infestation_tick
    {
        0.0
    } else {
        element.diffusion_scale
    }
}

#[inline(always)]
fn update_cells_inner(
    world: &mut PhysicsWorld,
    diseases: &DiseaseTable,
    first: usize,
    second: usize,
    first_id: u8,
    second_id: u8,
) {
    let first_count = world.read.disease_count[first];
    let second_count = world.read.disease_count[second];
    let (first_delta, second_delta, first_added, second_added) = if first_id == second_id {
        if first_id == u8::MAX {
            return;
        }
        let difference = second_count.wrapping_sub(first_count);
        let donor = if difference > 0 { second } else { first };
        let delta = cvtt_f32_i32(
            (difference as f32 * DIFFUSION_FRACTION) * diffusion_scale(world, diseases, donor),
        );
        (delta, delta.wrapping_neg(), first_id, second_id)
    } else {
        let first_stronger = if first_id == u8::MAX {
            false
        } else if second_id == u8::MAX {
            true
        } else {
            first_count as f32 * diseases.diseases[first_id as usize].strength
                > second_count as f32 * diseases.diseases[second_id as usize].strength
        };
        let donor = if first_stronger { first } else { second };
        let count = if first_stronger {
            first_count
        } else {
            second_count
        };
        let id = if first_stronger { first_id } else { second_id };
        let delta = cvtt_f32_i32(
            (count as f32 * DIFFUSION_FRACTION) * diffusion_scale(world, diseases, donor),
        );
        if first_stronger {
            (delta.wrapping_neg(), delta, id, id)
        } else {
            (delta, delta.wrapping_neg(), id, id)
        }
    };
    // Preserve this order: mixed disease writes can reset infestation counters.
    if first_delta != 0 {
        add_disease(&mut world.write, diseases, first, first_added, first_delta);
    }
    if second_delta != 0 {
        add_disease(
            &mut world.write,
            diseases,
            second,
            second_added,
            second_delta,
        );
    }
}

// ELF 0x34640. The final arguments are a pair of internal cells, not a range.
// The native implementation receives dt/events but does not read them.
pub(crate) fn update_cells(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    diseases: &DiseaseTable,
    _events: &mut Events,
    _dt: f32,
    first: usize,
    second: usize,
) -> Result<()> {
    if first >= world.read.element.len() || second >= world.read.element.len() {
        return Err(Error::new(
            Status::InvalidArgument,
            "disease cell index is out of range",
        ));
    }
    if phase(elements, world.read.element[first]) != phase(elements, world.read.element[second]) {
        return Ok(());
    }
    let first_id = world.read.disease_idx[first];
    let second_id = world.read.disease_idx[second];
    update_cells_inner(world, diseases, first, second, first_id, second_id);
    Ok(())
}

// The diffusion walker has already loaded and validated both old-snapshot
// disease IDs before deciding to call. Avoid loading them a second time and
// keep the hot pair kernel inline; all index and table validation still occurs
// in the surrounding prepared-step preflight.
#[inline(always)]
pub(crate) fn update_cells_known(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    diseases: &DiseaseTable,
    first: usize,
    second: usize,
    first_id: u8,
    second_id: u8,
) {
    if phase(elements, world.read.element[first]) != phase(elements, world.read.element[second]) {
        return;
    }
    update_cells_inner(world, diseases, first, second, first_id, second_id);
}

fn grow_cell(
    diseases: &DiseaseTable,
    id: u8,
    count: i32,
    growth: &mut f32,
    element: u16,
    temperature: f32,
    mass: f32,
    radiation: f32,
    radiation_enabled: bool,
) -> i32 {
    let disease = &diseases.diseases[id as usize];
    let element = &disease.elements[element as usize];
    let count_float = count as f32;
    let temperature_half_life = calculate_range_half_life(
        temperature,
        &disease.temperature_range,
        &disease.temperature_half_lives,
    );
    let temperature_growth = half_life_to_growth_rate(temperature_half_life);
    let mut change = (*growth - count_float) + temperature_growth * count_float;
    let min_count = element.min_count_per_kg * mass;
    let max_count = mass * element.max_count_per_kg;
    if !(max_count >= count_float) || !(count_float >= min_count) {
        if min_count > count_float {
            change -= DISEASE_STEP * element.under_population_death_rate;
        } else {
            change +=
                (half_life_to_growth_rate(element.over_population_half_life) - 1.0) * count_float;
        }
    } else {
        change += (half_life_to_growth_rate(element.population_half_life) - 1.0) * count_float;
    }
    if radiation_enabled {
        change -= radiation * disease.radiation_kill_rate;
    }
    let whole = cvtt_f32_i32(change);
    *growth = change - whole as f32;
    count.wrapping_add(whole)
}

// ELF 0x34980: growth uses the active write buffer, then a separate cleanup pass.
// Bounds are half open and already include the world's one-cell padding.
pub(crate) fn post_process_bounds(
    world: &mut PhysicsWorld,
    diseases: &DiseaseTable,
    min_x: usize,
    max_x: usize,
    min_y: usize,
    max_y: usize,
) -> Result<()> {
    post_process_cells(
        &mut world.write,
        diseases,
        world.stride,
        world.padded_height,
        world.radiation_enabled,
        (min_x, max_x, min_y, max_y),
        crate::parallel::pool(
            max_x
                .saturating_sub(min_x)
                .saturating_mul(max_y.saturating_sub(min_y)),
        ),
    )
}

fn post_process_cells(
    cells: &mut crate::world::Cells,
    diseases: &DiseaseTable,
    stride: usize,
    padded_height: usize,
    radiation_enabled: bool,
    (min_x, max_x, min_y, max_y): (usize, usize, usize, usize),
    pool: Option<&rayon::ThreadPool>,
) -> Result<()> {
    if max_x > stride || max_y > padded_height || min_x > max_x || min_y > max_y {
        return Err(Error::new(
            Status::InvalidArgument,
            "disease region is out of range",
        ));
    }
    if min_x == max_x || min_y == max_y {
        return Ok(());
    }
    if let Some(pool) = pool {
        // Size tasks by visited cells, preserving full rows for exclusive ownership.
        // A narrow rectangle must not scan the rest of the world's columns.
        let rows_per_chunk = crate::parallel::CHUNK_CELLS
            .div_ceil(max_x - min_x)
            .min(max_y - min_y);
        let chunk_cells = stride * rows_per_chunk;
        let extent = min_y * stride..max_y * stride;
        let element = &cells.element;
        let temperature = &cells.temperature;
        let mass = &cells.mass;
        let radiation = &cells.radiation;
        let environment = FloatEnv::capture();
        pool.install(|| {
            cells.disease_idx[extent.clone()]
                .par_chunks_mut(chunk_cells)
                .zip(cells.disease_count[extent.clone()].par_chunks_mut(chunk_cells))
                .zip(cells.disease_infestation[extent.clone()].par_chunks_mut(chunk_cells))
                .zip(cells.disease_growth[extent.clone()].par_chunks_mut(chunk_cells))
                .enumerate()
                .for_each(|(chunk, (((ids, counts), infestation), growth))| {
                    let _environment = environment.install();
                    let offset = extent.start + chunk * chunk_cells;
                    for row in (0..ids.len()).step_by(stride) {
                        for local in row + min_x..row + max_x {
                            let cell = offset + local;
                            if ids[local] != u8::MAX {
                                counts[local] = grow_cell(
                                    diseases,
                                    ids[local],
                                    counts[local],
                                    &mut growth[local],
                                    element[cell],
                                    temperature[cell],
                                    mass[cell],
                                    radiation[cell],
                                    radiation_enabled,
                                );
                            }
                            // Each cell reads only itself: cleanup can follow its growth
                            // immediately without changing the original two-pass result.
                            if counts[local] < 1 {
                                ids[local] = u8::MAX;
                                counts[local] = 0;
                                infestation[local] = 0;
                                growth[local] = 0.0;
                            }
                            infestation[local] = (infestation[local] as u16 + 1).min(254) as u8;
                        }
                    }
                });
        });
        return Ok(());
    }
    for y in min_y..max_y {
        for x in min_x..max_x {
            let cell = y * stride + x;
            let id = cells.disease_idx[cell];
            if id == u8::MAX {
                continue;
            }
            cells.disease_count[cell] = grow_cell(
                diseases,
                id,
                cells.disease_count[cell],
                &mut cells.disease_growth[cell],
                cells.element[cell],
                cells.temperature[cell],
                cells.mass[cell],
                cells.radiation[cell],
                radiation_enabled,
            );
        }
    }
    for y in min_y..max_y {
        for x in min_x..max_x {
            let cell = y * stride + x;
            if cells.disease_count[cell] < 1 {
                clear_disease(cells, cell);
            }
            // Even a cleared cell advances from zero to one in this pass.
            cells.disease_infestation[cell] =
                (cells.disease_infestation[cell] as u16 + 1).min(254) as u8;
        }
    }
    Ok(())
}

pub(crate) fn post_process(
    world: &mut PhysicsWorld,
    diseases: &DiseaseTable,
    _events: &mut Events,
    region: &Region,
) -> Result<()> {
    let bounds = [region.min_x, region.max_x, region.min_y, region.max_y];
    if bounds.iter().any(|&bound| bound < 0) {
        return Err(Error::new(
            Status::InvalidArgument,
            "negative disease region bound",
        ));
    }
    post_process_bounds(
        world,
        diseases,
        bounds[0] as usize,
        bounds[1] as usize,
        bounds[2] as usize,
        bounds[3] as usize,
    )
}

// ProcessFrame ELF 0x4b6c1: ConsumeDisease is a one-shot cell command, distinct
// from the DiseaseConsumer component whose official Update is empty.
pub(crate) fn consume(
    world: &mut PhysicsWorld,
    events: &mut Events,
    cell: usize,
    percent: f32,
    max_count: i32,
    callback_idx: i32,
) -> Result<()> {
    if cell >= world.write.disease_idx.len() {
        return Err(Error::new(
            Status::InvalidArgument,
            "disease cell index is out of range",
        ));
    }
    let mut removed = 0;
    if world.write.disease_idx[cell] != u8::MAX {
        let count = world.write.disease_count[cell];
        removed = cvtt_f32_i32(count as f32 * percent + 0.5).min(max_count);
        world.write.disease_count[cell] = count.wrapping_sub(removed);
        if world.write.disease_count[cell] < 1 {
            clear_disease(&mut world.write, cell);
        }
    }
    if callback_idx != -1 {
        // Native takes the index after clearing the cell, rather than saving the
        // consumed disease's index before subtraction.
        events
            .disease_consumption_callbacks
            .push(DiseaseConsumptionCallback {
                callback_idx,
                disease_idx: world.write.disease_idx[cell],
                pad0: 0,
                pad1: 0,
                pad2: 0,
                disease_count: removed,
            });
    }
    Ok(())
}

// ProcessFrame ELF 0x4b827: index 255 adds a raw count to the current disease;
// any other index uses the regular competition/infestation helper.
pub(crate) fn modify_cell(
    world: &mut PhysicsWorld,
    diseases: &DiseaseTable,
    cell: usize,
    disease_idx: u8,
    count: i32,
) -> Result<()> {
    if cell >= world.write.disease_idx.len() {
        return Err(Error::new(
            Status::InvalidArgument,
            "disease cell index is out of range",
        ));
    }
    if disease_idx == u8::MAX {
        world.write.disease_count[cell] = world.write.disease_count[cell].wrapping_add(count);
        if world.write.disease_count[cell] < 1 {
            clear_disease(&mut world.write, cell);
        }
    } else {
        add_disease(&mut world.write, diseases, cell, disease_idx, count);
    }
    Ok(())
}
