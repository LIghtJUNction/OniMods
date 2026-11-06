//! DiseaseEmitter's official timer and breadth-first area emission behavior.

use std::collections::VecDeque;

use crate::fluid_helpers::add_disease;
use crate::status::{Error, Result, Status};
use crate::tables::DiseaseTable;
use crate::world::{PhysicsWorld, Region};

#[derive(Clone, Debug)]
pub(crate) struct DiseaseEmitterData {
    pub(crate) cell: i32,
    pub(crate) disease_idx: u8,
    pub(crate) max_depth: u8,
    pub(crate) emit_count: i32,
    pub(crate) emit_interval: f32,
    pub(crate) elapsed: f32,
}

impl Default for DiseaseEmitterData {
    fn default() -> Self {
        // ELF 0x61050 stores 0x000000ffffffffff then twelve zero bytes.
        Self {
            cell: -1,
            disease_idx: 255,
            max_depth: 0,
            emit_count: 0,
            emit_interval: 0.0,
            elapsed: 0.0,
        }
    }
}

fn allocation_error() -> Error {
    Error::new(
        Status::ResourceLimit,
        "could not allocate disease emitter search",
    )
}

// ELF 0x544f0, with its final argument false: solids do not stop this search.
// Coordinates and depth in FloodFillInfo are unsigned 16-bit values.
pub(crate) fn reachable_cells(
    world: &PhysicsWorld,
    start_x: i32,
    start_y: i32,
    max_depth: u8,
) -> Result<Vec<usize>> {
    let mut visited = Vec::new();
    visited
        .try_reserve_exact(world.read.element.len())
        .map_err(|_| allocation_error())?;
    visited.resize(world.read.element.len(), false);
    let mut queue = VecDeque::new();
    queue.try_reserve(1).map_err(|_| allocation_error())?;
    queue.push_back((start_x as u16, start_y as u16, 0u16));
    let mut result = Vec::new();
    while let Some((x, y, depth)) = queue.pop_front() {
        if depth >= max_depth as u16
            || x == 0
            || y == 0
            || x as usize >= world.stride
            || y as usize >= world.padded_height
        {
            continue;
        }
        let cell = y as usize * world.stride + x as usize;
        if visited[cell] {
            continue;
        }
        visited[cell] = true;
        result.try_reserve(1).map_err(|_| allocation_error())?;
        result.push(cell);
        queue.try_reserve(4).map_err(|_| allocation_error())?;
        let next = depth.wrapping_add(1);
        queue.push_back((x, y.wrapping_sub(1), next));
        queue.push_back((x.wrapping_sub(1), y, next));
        queue.push_back((x.wrapping_add(1), y, next));
        queue.push_back((x, y.wrapping_add(1), next));
    }
    Ok(result)
}

// ELF 0x60bb0. Only the emitter's center is clipped to the region; each search
// target may lie outside it. The native Region maximum is inclusive here.
pub(crate) fn update(
    world: &mut PhysicsWorld,
    diseases: &DiseaseTable,
    dt: f32,
    region: &Region,
    data: &mut [DiseaseEmitterData],
) -> Result<()> {
    for emitter in data {
        if emitter.disease_idx == 255 {
            continue;
        }
        let x = emitter.cell % world.stride as i32;
        let y = emitter.cell / world.stride as i32;
        if x < region.min_x || y < region.min_y || x > region.max_x || y > region.max_y {
            continue;
        }
        if emitter.elapsed >= emitter.emit_interval {
            let cells = reachable_cells(world, x, y, emitter.max_depth)?;
            for cell in cells {
                if emitter.emit_count >= 0 || world.write.disease_idx[cell] == emitter.disease_idx {
                    add_disease(
                        &mut world.write,
                        diseases,
                        cell,
                        emitter.disease_idx,
                        emitter.emit_count,
                    );
                }
            }
            // At most one emission per update, including zero-duration periods.
            emitter.elapsed -= emitter.emit_interval;
        }
        emitter.elapsed += dt;
    }
    // Official Update resizes its handle-indexed info vector but never fills it
    // with emission statistics. The component manager owns that resize.
    Ok(())
}
