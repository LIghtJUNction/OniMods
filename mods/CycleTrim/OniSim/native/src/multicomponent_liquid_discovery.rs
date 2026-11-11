//! Liquid-channel discovery with bounded stack summaries and deterministic merging.
use crate::tables::ElementTable;
use crate::world::PhysicsWorld;
use rayon::prelude::*;

use super::MAX_REGION_LIQUIDS;

// Fixed aggregate stack scratch stays below 4 KiB at the existing 16-ID limit.
// Large regions increase each block's span instead of allocating more summaries.
const MAX_BLOCKS: usize = 64;

#[derive(Clone, Copy, Default)]
struct Summary {
    ids: [u16; MAX_REGION_LIQUIDS],
    len: usize,
    overflow: bool,
}

impl Summary {
    fn insert(&mut self, id: u16) -> bool {
        if self.ids[..self.len].contains(&id) {
            return true;
        }
        if self.len == MAX_REGION_LIQUIDS {
            self.overflow = true;
            return false;
        }
        self.ids[self.len] = id;
        self.len += 1;
        true
    }

    fn finish(self) -> Option<Vec<u16>> {
        if self.overflow || self.len < 2 {
            None
        } else {
            Some(self.ids[..self.len].to_vec())
        }
    }
}

pub(super) fn discover(
    world: &PhysicsWorld,
    elements: &ElementTable,
    region: (usize, usize, usize, usize),
) -> Option<Vec<u16>> {
    let cells = region.2 * region.3;
    // Do not initialize or enter a worker pool for a single serial block.
    let pool = if cells > crate::parallel::CHUNK_CELLS {
        crate::parallel::pool(cells)
    } else {
        None
    };
    discover_with_pool(world, elements, region, pool)
}

fn discover_with_pool(
    world: &PhysicsWorld,
    elements: &ElementTable,
    region: (usize, usize, usize, usize),
    pool: Option<&rayon::ThreadPool>,
) -> Option<Vec<u16>> {
    let cells = region.2 * region.3;
    let blocks = cells.div_ceil(crate::parallel::CHUNK_CELLS).min(MAX_BLOCKS);
    let Some(pool) = pool.filter(|_| blocks > 1) else {
        return scan(world, elements, region, 0, cells).finish();
    };

    let mut summaries = [Summary::default(); MAX_BLOCKS];
    // Consecutive linear spans include partial rows, with no padded border cells.
    // Their destinations are indexed before scheduling, independent of completion order.
    let base = cells / blocks;
    let remainder = cells % blocks;
    pool.install(|| {
        summaries[..blocks]
            .par_iter_mut()
            .enumerate()
            .for_each(|(block, summary)| {
                let start = block * base + block.min(remainder);
                let end = start + base + usize::from(block < remainder);
                *summary = scan(world, elements, region, start, end);
            });
    });

    let mut merged = Summary::default();
    for summary in &summaries[..blocks] {
        // A block with 17 distinct IDs implies global overflow regardless of duplicates
        // in other blocks. Otherwise its first-occurrence list is a lossless summary.
        if summary.overflow {
            return None;
        }
        for &id in &summary.ids[..summary.len] {
            if !merged.insert(id) {
                return None;
            }
        }
    }
    merged.finish()
}

fn scan(
    world: &PhysicsWorld,
    elements: &ElementTable,
    (min_x, min_y, width, _): (usize, usize, usize, usize),
    mut offset: usize,
    end: usize,
) -> Summary {
    let mut summary = Summary::default();
    while offset < end {
        let row = offset / width;
        let column = offset % width;
        let count = (width - column).min(end - offset);
        let first = (min_y + row) * world.stride + min_x + column;
        for cell in first..first + count {
            // Preserve the original positive-finite-mass predicate and primary-before-
            // secondary order, including for duplicate IDs within the same cell.
            for (id, mass) in [
                (world.write.element[cell], world.write.mass[cell]),
                (world.mixture_element[cell], world.mixture_mass[cell]),
            ] {
                if mass > 0.0
                    && mass.is_finite()
                    && elements
                        .elements
                        .get(id as usize)
                        .is_some_and(|element| element.record.state & 3 == 2)
                    && !summary.insert(id)
                {
                    return summary;
                }
            }
        }
        offset += count;
    }
    summary
}

#[cfg(test)]
#[path = "multicomponent_liquid_discovery_tests.rs"]
mod tests;
