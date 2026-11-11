//! Exact source comparison for sleeping gas regions, before dense channel packing.
use crate::world::PhysicsWorld;
use rayon::prelude::*;

const ROWS_PER_BLOCK: usize = 8;

#[derive(Clone, Copy, Default, PartialEq)]
struct Cell {
    primary: u16,
    secondary: u16,
    mass: u32,
    mixture: u32,
    temperature: u32,
    properties: u8,
    extras: u16,
}

#[derive(Clone, Default)]
pub(super) struct GasRegionSnapshot {
    cells: Vec<Cell>,
    extra: Vec<(u16, u32)>,
    // Prefix storage is reused by capture_into; no per-block Vec allocations.
    extra_offsets: Vec<usize>,
    captured_width: usize,
}

impl Cell {
    fn read(world: &PhysicsWorld, cell: usize, extra: Option<&[(u16, f32)]>) -> Self {
        let nongas = |id: u16, mass: f32| {
            mass > 0.0 && world.element_phase.get(id as usize).copied().unwrap_or(0) != 1
        };
        let blocked = world.write.properties[cell] & 1 != 0
            || nongas(world.write.element[cell], world.write.mass[cell])
            || nongas(world.mixture_element[cell], world.mixture_mass[cell])
            || extra.is_some_and(|entries| entries.iter().any(|&(id, mass)| nongas(id, mass)));
        if blocked {
            // A gas-wall cell contributes only its obstruction to this pass.
            // Thermal conduction runs separately and wakes the gas if its own
            // temperature changes. Heating solid rock must not wake this cache.
            return Self {
                primary: 0,
                secondary: 0,
                mass: 0,
                mixture: 0,
                temperature: 0,
                properties: 1,
                extras: 0,
            };
        }
        Self {
            primary: world.write.element[cell],
            secondary: world.mixture_element[cell],
            mass: world.write.mass[cell].to_bits(),
            mixture: world.mixture_mass[cell].to_bits(),
            temperature: world.write.temperature[cell].to_bits(),
            properties: 0,
            // Oversized counts use a non-reusable sentinel. Their complete
            // component records remain captured below without truncation.
            extras: extra.map_or(0, |entries| {
                u16::try_from(entries.len()).unwrap_or(u16::MAX)
            }),
        }
    }
}

impl GasRegionSnapshot {
    pub(super) fn capture(world: &PhysicsWorld, key: (usize, usize, usize, usize)) -> Self {
        let mut result = Self::default();
        result.capture_into(world, key);
        result
    }

    pub(super) fn capture_into(&mut self, world: &PhysicsWorld, key: (usize, usize, usize, usize)) {
        let (_, _, width, height) = key;
        self.capture_with_pool(world, key, crate::parallel::pool(width * height));
    }

    fn capture_with_pool(
        &mut self,
        world: &PhysicsWorld,
        key: (usize, usize, usize, usize),
        pool: Option<&rayon::ThreadPool>,
    ) {
        let (x, y, width, height) = key;
        self.captured_width = width;
        self.cells.resize(width * height, Cell::default());
        self.extra_offsets.clear();
        if width == 0 || height == 0 {
            self.extra.clear();
            self.extra_offsets.push(0);
            return;
        }

        let block_count = height.div_ceil(ROWS_PER_BLOCK);
        self.extra_offsets.resize(block_count + 1, 0);
        self.extra_offsets[0] = 0;
        let counts = &mut self.extra_offsets[1..];
        let block_cells = width * ROWS_PER_BLOCK;
        // Both passes share an immutable world borrow. CellMixtures::get uses
        // its stable cell index; sparse entry order is never observed here.
        let environment = crate::float_env::FloatEnv::capture();
        if let Some(pool) = pool {
            pool.install(|| {
                self.cells
                    .par_chunks_mut(block_cells)
                    .zip(counts.par_iter_mut())
                    .enumerate()
                    .for_each(|(block, (cells, count))| {
                        let _environment = environment.install();
                        *count =
                            Self::capture_block(world, x, y + block * ROWS_PER_BLOCK, width, cells);
                    });
            });
        } else {
            for (block, (cells, count)) in self
                .cells
                .chunks_mut(block_cells)
                .zip(counts.iter_mut())
                .enumerate()
            {
                *count = Self::capture_block(world, x, y + block * ROWS_PER_BLOCK, width, cells);
            }
        }

        // Each count becomes an exclusive prefix end. Actual list lengths are
        // used even for sentinel cells, so oversized extras are never truncated.
        let mut total = 0;
        for end in &mut self.extra_offsets[1..] {
            total += *end;
            *end = total;
        }
        // Keep initialized storage when the length is unchanged, avoiding a
        // redundant zero-fill before the exact bits overwrite every entry.
        self.extra.resize(total, (0, 0));
        if total == 0 {
            return;
        }

        // Prefix boundaries split both output streams without temporary Vecs.
        // join uses stack jobs, avoiding a heap-backed scope.spawn per block.
        if let Some(pool) = pool {
            pool.install(|| {
                Self::capture_extra_blocks(
                    world,
                    (x, y, width),
                    &self.cells,
                    &self.extra_offsets,
                    &mut self.extra,
                    environment,
                );
            });
        } else {
            Self::capture_extra(world, x, y, width, &self.cells, &mut self.extra);
        }
    }

    fn capture_extra_blocks(
        world: &PhysicsWorld,
        origin: (usize, usize, usize),
        cells: &[Cell],
        offsets: &[usize],
        extra: &mut [(u16, u32)],
        environment: crate::float_env::FloatEnv,
    ) {
        if extra.is_empty() {
            return;
        }
        let (x, y, width) = origin;
        let blocks = offsets.len() - 1;
        if blocks == 1 {
            let _environment = environment.install();
            Self::capture_extra(world, x, y, width, cells, extra);
            return;
        }
        let middle = blocks / 2;
        let (left_cells, right_cells) = cells.split_at(middle * width * ROWS_PER_BLOCK);
        let (left_extra, right_extra) = extra.split_at_mut(offsets[middle] - offsets[0]);
        rayon::join(
            || {
                Self::capture_extra_blocks(
                    world,
                    origin,
                    left_cells,
                    &offsets[..=middle],
                    left_extra,
                    environment,
                )
            },
            || {
                Self::capture_extra_blocks(
                    world,
                    (x, y + middle * ROWS_PER_BLOCK, width),
                    right_cells,
                    &offsets[middle..],
                    right_extra,
                    environment,
                )
            },
        );
    }

    fn capture_block(
        world: &PhysicsWorld,
        x: usize,
        y: usize,
        width: usize,
        cells: &mut [Cell],
    ) -> usize {
        let mut count = 0;
        for (row, cells) in cells.chunks_mut(width).enumerate() {
            let start = (y + row) * world.stride + x;
            for (column, value) in cells.iter_mut().enumerate() {
                let cell = start + column;
                let extra = world.extra_mixtures.get(&cell).map(Vec::as_slice);
                *value = Cell::read(world, cell, extra);
                if value.properties == 0 {
                    count += extra.map_or(0, |entries| entries.len());
                }
            }
        }
        count
    }

    fn capture_extra(
        world: &PhysicsWorld,
        x: usize,
        y: usize,
        width: usize,
        cells: &[Cell],
        captured_extra: &mut [(u16, u32)],
    ) {
        let mut at = 0;
        for (row, cells) in cells.chunks(width).enumerate() {
            let start = (y + row) * world.stride + x;
            for (column, value) in cells.iter().enumerate() {
                if value.properties == 0
                    && let Some(extra) = world.extra_mixtures.get(&(start + column))
                {
                    for &(id, mass) in extra {
                        captured_extra[at] = (id, mass.to_bits());
                        at += 1;
                    }
                }
            }
        }
        debug_assert_eq!(at, captured_extra.len());
    }

    pub(super) fn matches(&self, world: &PhysicsWorld, key: (usize, usize, usize, usize)) -> bool {
        let (_, _, width, height) = key;
        self.matches_with_pool(world, key, crate::parallel::pool(width * height))
    }

    fn matches_with_pool(
        &self,
        world: &PhysicsWorld,
        key: (usize, usize, usize, usize),
        pool: Option<&rayon::ThreadPool>,
    ) -> bool {
        let (x, y, width, height) = key;
        if self.cells.len() != width * height {
            return false;
        }
        if width == 0 || height == 0 {
            return self.extra.is_empty();
        }
        // Preserve comparison of equal-sized, differently shaped keys. Stored
        // block boundaries describe the captured width, so use the flat stream.
        if width != self.captured_width {
            return Self::matches_block(world, x, y, width, &self.cells, &self.extra);
        }
        let block_cells = width * ROWS_PER_BLOCK;
        if let Some(pool) = pool {
            let environment = crate::float_env::FloatEnv::capture();
            return pool.install(|| {
                self.cells
                    .par_chunks(block_cells)
                    .enumerate()
                    .all(|(block, cells)| {
                        let _environment = environment.install();
                        let extra =
                            &self.extra[self.extra_offsets[block]..self.extra_offsets[block + 1]];
                        Self::matches_block(
                            world,
                            x,
                            y + block * ROWS_PER_BLOCK,
                            width,
                            cells,
                            extra,
                        )
                    })
            });
        }
        Self::matches_block(world, x, y, width, &self.cells, &self.extra)
    }

    fn matches_block(
        world: &PhysicsWorld,
        x: usize,
        y: usize,
        width: usize,
        cells: &[Cell],
        captured_extra: &[(u16, u32)],
    ) -> bool {
        let mut extra_at = 0;
        for (row, cells) in cells.chunks(width).enumerate() {
            let start = (y + row) * world.stride + x;
            for (column, value) in cells.iter().enumerate() {
                let cell = start + column;
                let extra = world.extra_mixtures.get(&cell).map(Vec::as_slice);
                let input = Cell::read(world, cell, extra);
                if input.extras == u16::MAX || *value != input {
                    return false;
                }
                if input.properties == 0
                    && let Some(extra) = extra
                {
                    for &(id, mass) in extra {
                        if captured_extra.get(extra_at) != Some(&(id, mass.to_bits())) {
                            return false;
                        }
                        extra_at += 1;
                    }
                }
            }
        }
        extra_at == captured_extra.len()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn compact_snapshot_preserves_oversized_species_without_reuse() {
        assert!(std::mem::size_of::<Cell>() <= 20);
        let (mut state, _) = crate::native_chain_tests::fixture::create(16, 16, "pure");
        let mut world = state.world.take().unwrap();
        let cell = 3 * world.stride + 3;
        let id = world.write.element[cell];
        let entries = vec![(id, 0.01); u16::MAX as usize];
        world.extra_mixtures.insert(cell, entries);
        let key = (1, 1, 6, 6);
        let snapshot = GasRegionSnapshot::capture(&world, key);
        assert_eq!(snapshot.extra.len(), u16::MAX as usize);
        assert!(!snapshot.matches(&world, key));
    }
}

#[cfg(test)]
#[path = "gas_region_snapshot_parallel_tests.rs"]
mod parallel_tests;
