use super::MassComponents;
use crate::bytes::{f32at, i32at, u16at};
use crate::component_handles::ComponentHandles;
use crate::events::{CallbackInfo, DiseaseEmittedInfo, EmittedMassInfo, EventQueues};
use crate::fluid_helpers::{add_disease, game_index, notify, phase};
use crate::numeric::smin;
use crate::status::Result;
use crate::tables::{DiseaseTable, ElementTable};
use crate::world::{PhysicsWorld, Region};

pub(super) struct Emitter {
    elapsed: f32,
    interval: f32,
    mass: f32,
    temperature: f32,
    max_pressure: f32,
    disease_count: i32,
    cell: usize,
    element: u16,
    radius: u8,
    pressure_state: u8,
    disease_idx: u8,
    on_blocked: i32,
    on_unblocked: i32,
}

impl Emitter {
    pub(super) fn new(max_pressure: f32, on_blocked: i32, on_unblocked: i32) -> Self {
        // ELF 0xa5e40: {0, FLT_MAX, 0, -1}. Native disease fields are
        // uninitialized until Modify; deterministic absent values are retained.
        Self {
            elapsed: 0.0,
            interval: f32::MAX,
            mass: 0.0,
            temperature: -1.0,
            max_pressure,
            disease_count: 0,
            cell: 0,
            element: 0,
            radius: 0,
            pressure_state: 255,
            disease_idx: 255,
            on_blocked,
            on_unblocked,
        }
    }

    pub(super) fn modify(&mut self, world: &PhysicsWorld, b: &[u8]) -> Result<()> {
        self.cell = super::active_game_cell(world, i32at(b, 4))?;
        self.elapsed = 0.0;
        self.interval = f32at(b, 8);
        self.mass = f32at(b, 12);
        self.temperature = f32at(b, 16);
        self.max_pressure = f32at(b, 20);
        self.disease_count = i32at(b, 24);
        self.element = u16at(b, 28);
        self.radius = b[30];
        self.disease_idx = b[31];
        if self.mass <= 0.0 {
            self.pressure_state = 255;
        }
        Ok(())
    }
}

pub(super) fn empty_mass() -> EmittedMassInfo {
    EmittedMassInfo {
        elem_idx: u16::MAX,
        disease_idx: 255,
        pad0: 0,
        mass: 0.0,
        temperature: 0.0,
        disease_count: 0,
    }
}
fn empty_disease() -> DiseaseEmittedInfo {
    DiseaseEmittedInfo {
        disease_idx: 255,
        pad0: 0,
        pad1: 0,
        pad2: 0,
        count: 0,
    }
}

fn resize<T: Clone>(values: &mut Vec<T>, count: usize, empty: T) -> Result<()> {
    values
        .try_reserve(count.saturating_sub(values.len()))
        .map_err(super::allocation)?;
    values.resize(count, empty);
    Ok(())
}

impl MassComponents {
    pub(crate) fn update_data_lists(&mut self, events: &mut EventQueues) -> Result<()> {
        resize(
            &mut self.emitted_mass_infos,
            self.emitters.slot_count(),
            empty_mass(),
        )?;
        for dense in 0..self.emitters.data.len() {
            let slot = ComponentHandles::<Emitter>::slot(self.emitters.handle_at(dense));
            self.emitted_mass_infos[slot] = empty_mass();
        }
        resize(
            &mut events.disease_emitted_infos,
            self.disease_emitters.slot_count(),
            empty_disease(),
        )?;
        for dense in 0..self.disease_emitters.data.len() {
            let slot = ComponentHandles::<crate::disease_emitters::DiseaseEmitterData>::slot(
                self.disease_emitters.handle_at(dense),
            );
            events.disease_emitted_infos[slot] = empty_disease();
        }
        Ok(())
    }

    pub(crate) fn update_disease_emitters(
        &mut self,
        world: &mut PhysicsWorld,
        diseases: &DiseaseTable,
        dt: f32,
        region: &Region,
        events: &mut EventQueues,
    ) -> Result<()> {
        resize(
            &mut events.disease_emitted_infos,
            self.disease_emitters.slot_count(),
            empty_disease(),
        )?;
        crate::disease_emitters::update(
            world,
            diseases,
            dt,
            region,
            &mut self.disease_emitters.data,
        )
    }

    pub(crate) fn update_emitters(
        &mut self,
        world: &mut PhysicsWorld,
        elements: &ElementTable,
        diseases: &DiseaseTable,
        dt: f32,
        region: &Region,
        events: &mut EventQueues,
    ) -> Result<()> {
        resize(
            &mut self.emitted_mass_infos,
            self.emitters.slot_count(),
            empty_mass(),
        )?;
        for dense in 0..self.emitters.data.len() {
            let slot = ComponentHandles::<Emitter>::slot(self.emitters.handle_at(dense));
            let data = &mut self.emitters.data[dense];
            let (x, y) = (
                (data.cell % world.stride) as i32,
                (data.cell / world.stride) as i32,
            );
            if x < region.min_x || y < region.min_y || x > region.max_x || y > region.max_y {
                continue;
            }
            if data.elapsed >= data.interval {
                let info = &mut self.emitted_mass_infos[slot];
                if info.elem_idx == u16::MAX
                    || info.elem_idx == world.vacuum_element
                    || info.elem_idx == data.element
                {
                    self.scratch
                        .reachable(world, elements, data.cell, data.radius)?;
                    let available = self
                        .scratch
                        .cells
                        .iter()
                        .any(|&cell| world.write.mass[cell] < data.max_pressure);
                    let next_state = if available { 0 } else { 1 };
                    if data.pressure_state != next_state {
                        data.pressure_state = next_state;
                        let callback_idx = if available {
                            data.on_unblocked
                        } else {
                            data.on_blocked
                        };
                        if callback_idx != -1 {
                            events.callback_info.push(CallbackInfo { callback_idx });
                        }
                    }
                    let result =
                        try_emit(data, &self.scratch.cells, world, elements, diseases, events);
                    if result.mass > 0.0 {
                        // Only element/mass/temperature are aggregated. Native
                        // Emit's result always reports absent disease.
                        let info = &mut self.emitted_mass_infos[slot];
                        let old_mass = info.mass;
                        let old_temperature = info.temperature;
                        let total = result.mass + old_mass;
                        let temperature = if total <= 0.0 {
                            0.0
                        } else {
                            let weighted = (old_mass * old_temperature
                                + result.mass * result.temperature)
                                / total;
                            if old_temperature > result.temperature {
                                if result.temperature <= weighted {
                                    smin(old_temperature, weighted)
                                } else {
                                    result.temperature
                                }
                            } else if old_temperature <= weighted {
                                smin(result.temperature, weighted)
                            } else {
                                old_temperature
                            }
                        };
                        info.elem_idx = result.elem_idx;
                        info.mass = total;
                        info.temperature = temperature;
                    }
                    self.scratch.cells.clear();
                }
                // A blocked or failed attempt still consumes one interval.
                data.elapsed -= data.interval;
            }
            data.elapsed += dt;
        }
        Ok(())
    }

    pub(crate) fn publish_emitted_mass(
        &mut self,
        events: &mut EventQueues,
        vacuum: u16,
    ) -> Result<()> {
        events
            .emitted_mass_entries
            .try_reserve(
                self.emitted_mass_infos
                    .len()
                    .saturating_sub(events.emitted_mass_entries.len()),
            )
            .map_err(super::allocation)?;
        events.emitted_mass_entries.clear();
        events
            .emitted_mass_entries
            .extend_from_slice(&self.emitted_mass_infos);
        // ELF 0x58d00 copies all slots; 0x58d20..0x58e5a resets only these
        // three fields. Preserve disease fields and retired-slot visibility.
        for info in &mut self.emitted_mass_infos {
            info.elem_idx = vacuum;
            info.mass = 0.0;
            info.temperature = 0.0;
        }
        Ok(())
    }
}

fn try_emit(
    data: &Emitter,
    cells: &[usize],
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    diseases: &DiseaseTable,
    events: &mut EventQueues,
) -> EmittedMassInfo {
    let temperature = if !(data.temperature >= 0.0) {
        elements.elements[data.element as usize]
            .record
            .default_temperature
    } else {
        data.temperature
    };
    for &cell in cells {
        if !(world.write.mass[cell] < data.max_pressure) {
            continue;
        }
        let previous = world.write.element[cell];
        match phase(elements, data.element) {
            1 => {
                if previous == data.element
                    || previous == world.vacuum_element
                    || crate::gas::displace_gas(world, elements, diseases, events, cell, previous)
                {
                    return emit(data, cell, temperature, world, diseases, events);
                }
            }
            2 => {
                if previous == data.element
                    || previous == world.vacuum_element
                    || crate::fluid_displacement::displace_liquid(
                        world, elements, diseases, events, cell, previous,
                    )
                    || crate::gas::displace_gas(
                        world,
                        elements,
                        diseases,
                        events,
                        cell,
                        world.write.element[cell],
                    )
                {
                    return emit(data, cell, temperature, world, diseases, events);
                }
            }
            3 if data.mass > 0.0 => {
                if let Some(public) = game_index(world, cell)
                    && (world.simulate_all_active || world.active_mask[public as usize] != 0)
                {
                    events.spawn_ore(public, data.element, 255, data.mass, temperature, 0);
                }
            }
            _ => {}
        }
    }
    empty_mass()
}

fn emit(
    data: &Emitter,
    cell: usize,
    temperature: f32,
    world: &mut PhysicsWorld,
    diseases: &DiseaseTable,
    events: &mut EventQueues,
) -> EmittedMassInfo {
    let previous_element = world.write.element[cell];
    let previous_mass = world.write.mass[cell];
    let previous_temperature = world.write.temperature[cell];
    let total = data.mass + previous_mass;
    let next_temperature = if total <= 0.0 {
        0.0
    } else {
        let weighted = (data.mass * temperature + previous_mass * previous_temperature) / total;
        if temperature < previous_temperature {
            if temperature <= weighted {
                smin(previous_temperature, weighted)
            } else {
                temperature
            }
        } else if previous_temperature <= weighted {
            smin(weighted, temperature)
        } else {
            previous_temperature
        }
    };
    world.write.element[cell] = data.element;
    world.write.temperature[cell] = next_temperature;
    world.write.mass[cell] = total;
    add_disease(
        &mut world.write,
        diseases,
        cell,
        data.disease_idx,
        data.disease_count,
    );
    if previous_element == world.vacuum_element {
        notify(world, events, cell);
    }
    EmittedMassInfo {
        elem_idx: data.element,
        disease_idx: 255,
        pad0: 0,
        mass: data.mass,
        temperature,
        disease_count: 0,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn ok<T>(result: Result<T>) -> T {
        result.unwrap_or_else(|error| panic!("{}", error.message))
    }

    #[test]
    fn emitter_publication_keeps_cleared_and_retired_slots_in_manager_storage() {
        let mut manager = MassComponents::default();
        let handle = ok(manager.emitters.insert(Emitter::new(2.0, -1, -1)));
        let mut events = EventQueues::default();
        ok(manager.update_data_lists(&mut events));
        ok(manager.publish_emitted_mass(&mut events, 7));
        assert_eq!(events.emitted_mass_entries[0].elem_idx, u16::MAX);
        events.clear();
        ok(manager.publish_emitted_mass(&mut events, 7));
        assert_eq!(events.emitted_mass_entries[0].elem_idx, 7);
        assert_eq!(events.emitted_mass_entries[0].mass.to_bits(), 0);
        ok(manager.emitters.remove(handle));
        events.clear();
        ok(manager.publish_emitted_mass(&mut events, 7));
        assert_eq!(events.emitted_mass_entries.len(), 1);
        assert_eq!(events.emitted_mass_entries[0].elem_idx, 7);
    }
}
