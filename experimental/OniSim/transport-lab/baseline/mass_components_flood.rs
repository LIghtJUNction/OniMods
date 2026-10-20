//! Official flood traversal: breadth first, down/left/right/up, depth < radius.
use std::collections::VecDeque;
use crate::events::{ConsumedMassInfo, EventQueues};
use crate::fluid_helpers::{calculate_disease, clear_cell, clear_disease, notify, phase};
use crate::numeric::{cvtt_f32_i32, smax, smin};
use crate::status::Result;
use crate::tables::{DiseaseTable, ElementTable};
use crate::world::PhysicsWorld;

#[derive(Default)]
pub(crate) struct FloodScratch {
    marks: Vec<u32>,
    generation: u32,
    queue: VecDeque<(u16, u16, u16)>,
    pub(super) cells: Vec<usize>,
}

impl FloodScratch {
    fn reset(&mut self, world: &PhysicsWorld, start: usize, depth: u8) -> Result<()> {
        if depth == 0 { self.cells.clear(); self.queue.clear(); return Ok(()); }
        let count = world.write.mass.len();
        // A Manhattan search accepts at most this diamond. Reserve enough for
        // every queued duplicate while retaining the official BFS order.
        let radius = depth as usize;
        let reachable = count.min(1 + 2 * radius * (radius - 1));
        if self.marks.len() != count {
            self.marks.try_reserve(count.saturating_sub(self.marks.len()))
                .map_err(super::allocation)?;
            self.marks.resize(count, 0);
        }
        self.cells.try_reserve(reachable.saturating_sub(self.cells.len()))
            .map_err(super::allocation)?;
        self.queue.try_reserve((reachable * 4 + 1).saturating_sub(self.queue.len()))
            .map_err(super::allocation)?;
        self.generation = self.generation.wrapping_add(1);
        if self.generation == 0 { self.marks.fill(0); self.generation = 1; }
        self.cells.clear();
        self.queue.clear();
        self.queue.push_back(((start % world.stride) as u16,
            (start / world.stride) as u16, 0));
        Ok(())
    }

    fn pop(&mut self, world: &PhysicsWorld, depth: u8) -> Option<(usize, u16, u16, u16)> {
        while let Some((x, y, level)) = self.queue.pop_front() {
            if level >= depth as u16 || x == 0 || y == 0
                || x as usize >= world.stride || y as usize >= world.padded_height
            { continue; }
            return Some((y as usize * world.stride + x as usize, x, y, level));
        }
        None
    }

    fn mark(&mut self, cell: usize) -> bool {
        if self.marks[cell] == self.generation { return false; }
        self.marks[cell] = self.generation;
        self.cells.push(cell);
        true
    }

    fn expand(&mut self, x: u16, y: u16, level: u16) {
        let next = level.wrapping_add(1);
        self.queue.push_back((x, y.wrapping_sub(1), next));
        self.queue.push_back((x.wrapping_sub(1), y, next));
        self.queue.push_back((x.wrapping_add(1), y, next));
        self.queue.push_back((x, y.wrapping_add(1), next));
    }

    pub(super) fn reachable(&mut self, world: &PhysicsWorld,
        elements: &ElementTable, start: usize, depth: u8) -> Result<()>
    {
        self.reset(world, start, depth)?;
        while let Some((cell, x, y, level)) = self.pop(world, depth) {
            if phase(elements, world.write.element[cell]) == 3 || !self.mark(cell) { continue; }
            self.expand(x, y, level);
        }
        Ok(())
    }

    pub(crate) fn remove_mass(&mut self, world: &mut PhysicsWorld,
        elements: &ElementTable, diseases: &DiseaseTable, events: &mut EventQueues,
        start: usize, depth: u8, requested: f32, target: u16, info: &mut ConsumedMassInfo,
    ) -> Result<()> {
        self.reset(world, start, depth)?;
        let target_phase = phase(elements, target);
        let mut remaining = requested;
        while let Some((cell, x, y, level)) = self.pop(world, depth) {
            let cell_phase = phase(elements, world.write.element[cell]);
            if (if target_phase == 3 { cell_phase == 1 || cell_phase == 2 }
                else { cell_phase == 3 }) || !self.mark(cell) { continue; }
            if world.write.element[cell] == target {
                let previous_mass = world.write.mass[cell];
                let (amount, next_mass, ratio) = if previous_mass < remaining {
                    (previous_mass, 0.0, 1.0)
                } else { (remaining, previous_mass - remaining, remaining / previous_mass) };
                let old_disease = world.write.disease_idx[cell];
                let removed_count = cvtt_f32_i32(world.write.disease_count[cell] as f32 * ratio);
                world.write.mass[cell] = next_mass;
                remaining -= amount;
                world.write.disease_count[cell] = world.write.disease_count[cell]
                    .wrapping_sub(removed_count);
                if world.write.disease_count[cell] < 1 { clear_disease(&mut world.write, cell); }
                let old_mass = info.mass;
                let old_temperature = info.temperature;
                let temperature = world.write.temperature[cell];
                let total = old_mass + amount;
                let weighted = (old_temperature * old_mass + amount * temperature) / total;
                let (minimum, maximum) = if temperature > old_temperature {
                    (old_temperature, temperature)
                } else { (temperature, old_temperature) };
                info.mass = total;
                info.temperature = smax(smin(weighted, maximum), minimum);
                if old_disease != 255 {
                    let (id, count) = calculate_disease(diseases, info.disease_idx,
                        info.disease_count, old_disease, removed_count);
                    info.disease_idx = id;
                    info.disease_count = count;
                }
                // ELF 0xa5980 is 0x00800000, the smallest normal f32.
                if world.write.mass[cell] <= f32::MIN_POSITIVE {
                    clear_cell(&mut world.write, world.vacuum_element, cell);
                    notify(world, events, cell);
                }
            }
            if remaining <= 0.0 { break; }
            self.expand(x, y, level);
        }
        self.queue.clear();
        Ok(())
    }
}
