//! Exact source comparison for sleeping gas regions, before dense channel packing.
use crate::world::PhysicsWorld;

#[derive(Clone, Copy, PartialEq)]
struct Cell {
    primary: u16, secondary: u16, mass: u32, mixture: u32, temperature: u32,
    properties: u8, extras: usize,
}

#[derive(Clone)]
pub(super) struct GasRegionSnapshot {
    cells: Vec<Cell>,
    extra: Vec<(u16, u32)>,
}

impl Cell {
    fn read(world: &PhysicsWorld, cell: usize) -> Self {
        Self {
            primary: world.write.element[cell], secondary: world.mixture_element[cell],
            mass: world.write.mass[cell].to_bits(), mixture: world.mixture_mass[cell].to_bits(),
            temperature: world.write.temperature[cell].to_bits(), properties: world.write.properties[cell],
            extras: world.extra_mixtures.get(&cell).map_or(0, Vec::len),
        }
    }
}

impl GasRegionSnapshot {
    pub(super) fn capture(world: &PhysicsWorld, key: (usize, usize, usize, usize)) -> Self {
        let (x, y, width, height) = key;
        let mut result = Self { cells: Vec::with_capacity(width * height), extra: Vec::new() };
        for row in y..y + height { for col in x..x + width {
            let cell = row * world.stride + col;
            result.cells.push(Cell::read(world, cell));
            if let Some(extra) = world.extra_mixtures.get(&cell) {
                result.extra.extend(extra.iter().map(|&(id, mass)| (id, mass.to_bits())));
            }
        }}
        result
    }

    pub(super) fn matches(&self, world: &PhysicsWorld, key: (usize, usize, usize, usize)) -> bool {
        let (x, y, width, height) = key;
        if self.cells.len() != width * height { return false; }
        let mut at = 0;
        let mut extra_at = 0;
        for row in y..y + height { for col in x..x + width {
            let cell = row * world.stride + col;
            if self.cells[at] != Cell::read(world, cell) { return false; }
            if let Some(extra) = world.extra_mixtures.get(&cell) {
                for &(id, mass) in extra {
                    if self.extra.get(extra_at) != Some(&(id, mass.to_bits())) { return false; }
                    extra_at += 1;
                }
            }
            at += 1;
        }}
        extra_at == self.extra.len()
    }
}
