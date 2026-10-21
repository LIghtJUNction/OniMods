use crate::cell_material::{Emission, PendingCommands};
use crate::cell_material_rect::rect_remove;
use crate::events::{
    BackwallElementChangedInfo, CallbackInfo, ConsumedMassInfo, EventQueues, MassConsumedCallback,
    MassEmittedCallback, SpawnOreInfo,
};
use crate::fluid_displacement::displace_liquid;
use crate::fluid_helpers::{add_disease, clear_cell, game_index, notify, phase};
use crate::gas::displace_gas;
use crate::mass_components::FloodScratch;
use crate::numeric::smin;
use crate::status::Result;
use crate::tables::{DiseaseTable, ElementTable};
use crate::world::PhysicsWorld;

fn emission(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    diseases: &DiseaseTable,
    events: &mut EventQueues,
    command: Emission,
) {
    let cell = world.internal_index(command.cell);
    let previous = world.write.element[cell];
    let allowed = if previous == command.element || previous == world.vacuum_element {
        true
    } else {
        match phase(elements, command.element) {
            1 => displace_gas(world, elements, diseases, events, cell, previous),
            2 => displace_liquid(world, elements, diseases, events, cell, previous),
            _ => false,
        }
    };
    if !allowed {
        if command.callback != -1 {
            events.mass_emitted_callbacks.push(MassEmittedCallback {
                callback_idx: command.callback,
                elem_idx: u16::MAX,
                succeeded: 0,
                disease_idx: 255,
                mass: 0.0,
                temperature: 0.0,
                disease_count: 0,
            });
        }
        return;
    }
    let old_temperature = world.write.temperature[cell];
    let old_mass = world.write.mass[cell];
    let sum = command.mass + old_mass;
    let temperature = if sum <= 0.0 {
        0.0
    } else {
        let weighted = (old_mass * old_temperature + command.mass * command.temperature) / sum;
        if old_temperature > command.temperature {
            if command.temperature <= weighted {
                smin(old_temperature, weighted)
            } else {
                command.temperature
            }
        } else if old_temperature <= weighted {
            smin(command.temperature, weighted)
        } else {
            old_temperature
        }
    };
    world.write.temperature[cell] = temperature;
    world.write.mass[cell] += command.mass;
    if command.disease != 255 {
        add_disease(
            &mut world.write,
            diseases,
            cell,
            command.disease,
            command.count,
        );
    }
    if previous != command.element {
        world.write.element[cell] = command.element;
        notify(world, events, cell);
    }
    if command.callback != -1 {
        events.mass_emitted_callbacks.push(MassEmittedCallback {
            callback_idx: command.callback,
            elem_idx: command.element,
            succeeded: 1,
            disease_idx: command.disease,
            mass: command.mass,
            temperature: command.temperature,
            disease_count: command.count,
        });
    }
}

impl PendingCommands {
    pub(crate) fn process_mass(
        &mut self,
        world: &mut PhysicsWorld,
        elements: &ElementTable,
        diseases: &DiseaseTable,
        events: &mut EventQueues,
        scratch: &mut FloodScratch,
    ) -> Result<()> {
        for command in self.consumption.drain(..) {
            let cell = world.internal_index(command.cell);
            let mut info = ConsumedMassInfo {
                sim_handle: -1,
                removed_elem_idx: command.element,
                disease_idx: 255,
                ..Default::default()
            };
            if command.height == 0 {
                scratch.remove_mass(
                    world,
                    elements,
                    diseases,
                    events,
                    cell,
                    command.radius,
                    command.mass,
                    command.element,
                    &mut info,
                )?;
            } else {
                rect_remove(
                    world,
                    elements,
                    diseases,
                    events,
                    cell % world.stride,
                    cell / world.stride,
                    command.radius,
                    command.height,
                    command.mass,
                    command.element,
                    &mut info,
                )?;
            }
            if command.callback != -1 {
                events.mass_consumed_callbacks.push(MassConsumedCallback {
                    callback_idx: command.callback,
                    elem_idx: info.removed_elem_idx,
                    disease_idx: info.disease_idx,
                    pad0: 0,
                    mass: info.mass,
                    temperature: info.temperature,
                    disease_count: info.disease_count,
                });
            }
        }
        for command in self.emission.drain(..) {
            emission(world, elements, diseases, events, command);
        }
        Ok(())
    }

    pub(crate) fn process_dig(
        &mut self,
        world: &mut PhysicsWorld,
        elements: &ElementTable,
        _diseases: &DiseaseTable,
        events: &mut EventQueues,
    ) -> Result<()> {
        for command in self.digs.drain(..) {
            let cell = world.internal_index(command.cell);
            let element = if command.backwall {
                world.backwall_element[cell]
            } else {
                world.write.element[cell]
            };
            if command.callback != -1 {
                events.callback_info.push(CallbackInfo {
                    callback_idx: command.callback,
                });
            }
            if element == u16::MAX || phase(elements, element) != 3 {
                continue;
            }
            if !command.skip_event {
                let (mass, temperature, disease, count) = if command.backwall {
                    (
                        world.backwall_mass[cell],
                        world.backwall_temperature[cell],
                        255,
                        0,
                    )
                } else {
                    (
                        world.write.mass[cell],
                        world.write.temperature[cell],
                        world.write.disease_idx[cell],
                        world.write.disease_count[cell],
                    )
                };
                if mass > 0.0
                    && let Some(game_cell) = game_index(world, cell)
                {
                    events.dig_info.push(SpawnOreInfo {
                        cell_idx: game_cell,
                        elem_idx: elements.elements[element as usize].record.index,
                        disease_idx: disease,
                        pad0: 0,
                        mass,
                        temperature,
                        disease_count: count,
                    });
                }
            }
            if command.backwall {
                world.backwall_element[cell] = world.vacuum_element;
                world.backwall_mass[cell] = 0.0;
                world.backwall_temperature[cell] = 0.0;
                if let Some(game_cell) = game_index(world, cell) {
                    events
                        .backwall_element_changed_infos
                        .push(BackwallElementChangedInfo { game_cell });
                }
            } else {
                clear_cell(&mut world.write, world.vacuum_element, cell);
            }
            notify(world, events, cell);
        }
        Ok(())
    }
}
