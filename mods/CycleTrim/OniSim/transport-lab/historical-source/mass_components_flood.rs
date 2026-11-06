//! Official flood traversal: breadth first, down/left/right/up, depth < radius.
use crate::events::{ConsumedMassInfo, EventQueues};
use crate::fluid_helpers::{calculate_disease, clear_cell, clear_disease, notify, phase};
use crate::numeric::{cvtt_f32_i32, smax, smin};
use crate::status::Result;
use crate::tables::{DiseaseTable, ElementTable};
use crate::world::PhysicsWorld;
use std::collections::VecDeque;

#[derive(Default)]
pub(crate) struct FloodScratch {
    // Marks mean queued (including blocked cells), not yet consumed.
    // Until a cell is processed, its eligibility cannot change in this call:
    // remove_mass writes only the current cell; cleared cells become traversable
    // vacuum. Do not reuse this admission rule for neighbor-mutating kernels.
    marks: Vec<u32>,
    generation: u32,
    queue: VecDeque<(u16, u16, u16)>,
    pub(super) cells: Vec<usize>,
}

impl FloodScratch {
    fn reset(&mut self, world: &PhysicsWorld, start: usize, depth: u8) -> Result<()> {
        if depth == 0 {
            self.cells.clear();
            self.queue.clear();
            return Ok(());
        }
        let count = world.write.mass.len();
        // Every in-bounds cell at depth < radius is queued at most once.
        // Obstacles only reduce the Manhattan bound; FIFO order is unchanged.
        let radius = depth as usize;
        let reachable = count.min(1 + 2 * radius * (radius - 1));
        if self.marks.len() != count {
            self.marks
                .try_reserve(count.saturating_sub(self.marks.len()))
                .map_err(super::allocation)?;
            self.marks.resize(count, 0);
        }
        self.cells
            .try_reserve(reachable.saturating_sub(self.cells.len()))
            .map_err(super::allocation)?;
        self.queue
            .try_reserve(reachable.saturating_sub(self.queue.len()))
            .map_err(super::allocation)?;
        self.generation = self.generation.wrapping_add(1);
        if self.generation == 0 {
            self.marks.fill(0);
            self.generation = 1;
        }
        self.cells.clear();
        self.queue.clear();
        self.enqueue(
            world,
            (start % world.stride) as u16,
            (start / world.stride) as u16,
            0,
            depth,
        );
        Ok(())
    }

    fn enqueue(&mut self, world: &PhysicsWorld, x: u16, y: u16, level: u16, depth: u8) {
        if level >= u16::from(depth)
            || x == 0
            || y == 0
            || x as usize >= world.stride
            || y as usize >= world.padded_height
        {
            return;
        }
        let cell = y as usize * world.stride + x as usize;
        if self.marks[cell] == self.generation {
            return;
        }
        self.marks[cell] = self.generation;
        self.queue.push_back((x, y, level));
    }

    fn pop(&mut self, world: &PhysicsWorld) -> Option<(usize, u16, u16, u16)> {
        self.queue
            .pop_front()
            .map(|(x, y, level)| (y as usize * world.stride + x as usize, x, y, level))
    }

    fn expand(&mut self, world: &PhysicsWorld, x: u16, y: u16, level: u16, depth: u8) {
        let next = level + 1;
        if next >= u16::from(depth) {
            return;
        }
        self.enqueue(world, x, y.wrapping_sub(1), next, depth);
        self.enqueue(world, x.wrapping_sub(1), y, next, depth);
        self.enqueue(world, x.wrapping_add(1), y, next, depth);
        self.enqueue(world, x, y.wrapping_add(1), next, depth);
    }

    pub(super) fn reachable(
        &mut self,
        world: &PhysicsWorld,
        elements: &ElementTable,
        start: usize,
        depth: u8,
    ) -> Result<()> {
        self.reset(world, start, depth)?;
        while let Some((cell, x, y, level)) = self.pop(world) {
            if phase(elements, world.write.element[cell]) == 3 {
                continue;
            }
            self.cells.push(cell);
            self.expand(world, x, y, level, depth);
        }
        Ok(())
    }

    pub(crate) fn remove_mass(
        &mut self,
        world: &mut PhysicsWorld,
        elements: &ElementTable,
        diseases: &DiseaseTable,
        events: &mut EventQueues,
        start: usize,
        depth: u8,
        requested: f32,
        target: u16,
        info: &mut ConsumedMassInfo,
    ) -> Result<()> {
        self.reset(world, start, depth)?;
        let target_phase = phase(elements, target);
        let mut remaining = requested;
        while let Some((cell, x, y, level)) = self.pop(world) {
            let cell_phase = phase(elements, world.write.element[cell]);
            let blocked = if target_phase == 3 {
                cell_phase == 1 || cell_phase == 2
            } else {
                cell_phase == 3
            };
            if blocked {
                continue;
            }
            self.cells.push(cell);
            if world.write.element[cell] == target {
                let previous_mass = world.write.mass[cell];
                let (amount, next_mass, ratio) = if previous_mass < remaining {
                    (previous_mass, 0.0, 1.0)
                } else {
                    (
                        remaining,
                        previous_mass - remaining,
                        remaining / previous_mass,
                    )
                };
                let old_disease = world.write.disease_idx[cell];
                let removed_count = cvtt_f32_i32(world.write.disease_count[cell] as f32 * ratio);
                world.write.mass[cell] = next_mass;
                remaining -= amount;
                world.write.disease_count[cell] =
                    world.write.disease_count[cell].wrapping_sub(removed_count);
                if world.write.disease_count[cell] < 1 {
                    clear_disease(&mut world.write, cell);
                }
                let old_mass = info.mass;
                let old_temperature = info.temperature;
                let temperature = world.write.temperature[cell];
                let total = old_mass + amount;
                let weighted = (old_temperature * old_mass + amount * temperature) / total;
                let (minimum, maximum) = if temperature > old_temperature {
                    (old_temperature, temperature)
                } else {
                    (temperature, old_temperature)
                };
                info.mass = total;
                info.temperature = smax(smin(weighted, maximum), minimum);
                if old_disease != 255 {
                    let (id, count) = calculate_disease(
                        diseases,
                        info.disease_idx,
                        info.disease_count,
                        old_disease,
                        removed_count,
                    );
                    info.disease_idx = id;
                    info.disease_count = count;
                }
                // ELF 0xa5980 is 0x00800000, the smallest normal f32.
                if world.write.mass[cell] <= f32::MIN_POSITIVE {
                    clear_cell(&mut world.write, world.vacuum_element, cell);
                    notify(world, events, cell);
                }
            }
            if remaining <= 0.0 {
                break;
            }
            self.expand(world, x, y, level, depth);
        }
        self.queue.clear();
        Ok(())
    }
}
