//! Ordered liquid candidates with reusable per-worker storage.
use crate::world::{PhysicsWorld, Region};
use rayon::prelude::*;

// Scanning a cell is cheap. Keep small regions serial and amortize scheduling
// over contiguous blocks instead of allocating a candidate vector for every row.
const CELLS_PER_BLOCK: usize = 2048;
const MIN_PARALLEL_CELLS: usize = 8192;
const MAX_BLOCKS: usize = 64;

#[derive(Default)]
struct Block {
    cells: Vec<usize>,
    bounds: Option<[i32; 4]>,
}

impl Block {
    fn clear(&mut self) {
        self.cells.clear();
        self.bounds = None;
    }

    fn include(&mut self, x: i32, y: i32) {
        self.bounds = Some(match self.bounds {
            Some([left, bottom, right, top]) => {
                [left.min(x), bottom.min(y), right.max(x + 1), top.max(y + 1)]
            }
            None => [x, y, x + 1, y + 1],
        });
    }
}

#[derive(Default)]
pub(super) struct LiquidCandidates {
    blocks: Vec<Block>,
    merged: Block,
}

impl LiquidCandidates {
    pub(super) fn cells(&self) -> &[usize] {
        &self.merged.cells
    }

    pub(super) fn bounds(&self) -> Option<[i32; 4]> {
        self.merged.bounds
    }

    pub(super) fn scan(&mut self, world: &PhysicsWorld, region: Region) {
        let width = (region.max_x - region.min_x).max(0) as usize;
        let height = (region.max_y - region.min_y).max(0) as usize;
        let count = width.saturating_mul(height);
        let pool = if count > MIN_PARALLEL_CELLS {
            crate::parallel::pool(count)
        } else {
            None
        };
        self.scan_with_pool(world, region, pool);
    }

    fn scan_with_pool(
        &mut self,
        world: &PhysicsWorld,
        region: Region,
        pool: Option<&rayon::ThreadPool>,
    ) {
        let width = (region.max_x - region.min_x).max(0) as usize;
        let height = (region.max_y - region.min_y).max(0) as usize;
        let count = width.saturating_mul(height);
        let blocks = count.div_ceil(CELLS_PER_BLOCK).min(MAX_BLOCKS);
        let Some(pool) = pool.filter(|_| blocks > 1) else {
            scan_block(&mut self.merged, world, region, width, 0, count);
            return;
        };

        if self.blocks.len() < blocks {
            self.blocks.resize_with(blocks, Block::default);
        }
        let span = count / blocks;
        let remainder = count % blocks;
        pool.install(|| {
            self.blocks[..blocks]
                .par_iter_mut()
                .enumerate()
                .for_each(|(index, block)| {
                    let start = index * span + index.min(remainder);
                    let end = start + span + usize::from(index < remainder);
                    scan_block(block, world, region, width, start, end);
                });
        });

        self.merged.clear();
        let total = self.blocks[..blocks]
            .iter()
            .map(|block| block.cells.len())
            .sum();
        self.merged.cells.reserve(total);
        // Indexed blocks preserve row-major candidate order regardless of worker
        // completion order. Pressure discovery relies on that stable traversal.
        for block in &self.blocks[..blocks] {
            self.merged.cells.extend_from_slice(&block.cells);
            if let Some([left, bottom, right, top]) = block.bounds {
                self.merged.include(left, bottom);
                self.merged.include(right - 1, top - 1);
            }
        }
    }
}

fn scan_block(
    output: &mut Block,
    world: &PhysicsWorld,
    region: Region,
    width: usize,
    mut offset: usize,
    end: usize,
) {
    // Keep hot vector metadata local: neighboring blocks may share a cache
    // line, so workers publish their lengths only after finishing the scan.
    let mut cells = std::mem::take(&mut output.cells);
    cells.clear();
    let has_extras = !world.extra_mixtures.is_empty();
    let mut bounds = [i32::MAX, i32::MAX, i32::MIN, i32::MIN];
    while offset < end {
        let row = offset / width;
        let column = offset % width;
        let length = (width - column).min(end - offset);
        let x = region.min_x as usize + column;
        let y = region.min_y as usize + row;
        let first = y * world.stride + x;
        for cell in first..first + length {
            let liquid = (world.write.mass[cell] > 0.0
                && world.element_phase[world.write.element[cell] as usize] == 2)
                || (world.mixture_mass[cell] > 0.0
                    && world.element_phase[world.mixture_element[cell] as usize] == 2)
                || (has_extras
                    && world.extra_mixtures.get(&cell).is_some_and(|species| {
                        species
                            .iter()
                            .any(|&(id, mass)| mass > 0.0 && world.element_phase[id as usize] == 2)
                    }));
            if liquid {
                cells.push(cell);
                let cell_x = (x + cell - first) as i32;
                let cell_y = y as i32;
                bounds[0] = bounds[0].min(cell_x);
                bounds[1] = bounds[1].min(cell_y);
                bounds[2] = bounds[2].max(cell_x + 1);
                bounds[3] = bounds[3].max(cell_y + 1);
            }
        }
        offset += length;
    }
    output.cells = cells;
    output.bounds = (bounds[0] != i32::MAX).then_some(bounds);
}

#[cfg(test)]
#[path = "liquid_candidates_tests.rs"]
mod tests;
