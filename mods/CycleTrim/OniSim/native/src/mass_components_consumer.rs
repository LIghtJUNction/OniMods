use super::MassComponents;
use crate::events::{ConsumedMassInfo, EventQueues};
use crate::status::Result;
use crate::tables::{DiseaseTable, ElementTable};
use crate::world::{PhysicsWorld, Region};

pub(super) struct Consumer {
    pub(super) rate: f32,
    pub(super) radius: u8,
    pub(super) configuration: u8,
    pub(super) element: u16,
    pub(super) cursor: u8,
    pub(super) cell: usize,
}

impl MassComponents {
    pub(crate) fn update_consumers(
        &mut self,
        world: &mut PhysicsWorld,
        elements: &ElementTable,
        diseases: &DiseaseTable,
        dt: f32,
        region: &Region,
        events: &mut EventQueues,
    ) -> Result<()> {
        for dense in 0..self.consumers.data.len() {
            let handle = self.consumers.handle_at(dense);
            let data = &mut self.consumers.data[dense];
            let (x, y) = (
                (data.cell % world.stride) as i32,
                (data.cell / world.stride) as i32,
            );
            if x < region.min_x || y < region.min_y || x > region.max_x || y > region.max_y {
                continue;
            }
            let target = match data.configuration {
                0 => Some(data.element),
                1 | 2 => {
                    self.scratch
                        .reachable(world, elements, data.cell, data.radius)?;
                    let count = self.scratch.cells.len();
                    let mask = if data.configuration == 1 { 2 } else { 1 };
                    (0..count).find_map(|offset| {
                        let cell = self.scratch.cells[(data.cursor as usize + offset) % count];
                        let element = world.write.element[cell];
                        (elements.elements[element as usize].record.state & mask != 0)
                            .then_some(element)
                    })
                }
                _ => None,
            };
            let Some(target) = target else {
                continue;
            };
            let mut info = ConsumedMassInfo {
                sim_handle: handle,
                removed_elem_idx: target,
                disease_idx: 255,
                pad0: 0,
                mass: 0.0,
                temperature: 0.0,
                disease_count: 0,
            };
            self.scratch.remove_mass(
                world,
                elements,
                diseases,
                events,
                data.cell,
                data.radius,
                data.rate * dt,
                target,
                &mut info,
            )?;
            if !(info.mass <= 0.0) {
                events.removed_mass_entries.push(info);
            }
            data.cursor = data.cursor.wrapping_add(1);
        }
        Ok(())
    }

    pub(crate) fn update_disease_consumers(
        &mut self,
        _world: &mut PhysicsWorld,
        _diseases: &DiseaseTable,
        _dt: f32,
        _region: &Region,
        _events: &mut EventQueues,
    ) -> Result<()> {
        // Official DiseaseConsumer::Update at ELF 0x606c0 is a single ret.
        Ok(())
    }
}
