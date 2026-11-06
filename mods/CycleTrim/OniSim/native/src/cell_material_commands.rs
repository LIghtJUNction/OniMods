use crate::cell_material::PendingCommands;
use crate::cell_material_add_gas::add_gas;
use crate::cell_material_add_liquid::add_liquid;
use crate::cell_material_add_solid::add_solid;
use crate::events::{CallbackInfo, EventQueues};
use crate::fluid_displacement::displace_liquid;
use crate::fluid_helpers::{clear_cell, notify, phase};
use crate::gas::displace_gas;
use crate::numeric::{smax, smin};
use crate::status::Result;
use crate::tables::{DiseaseTable, ElementTable};
use crate::world::PhysicsWorld;

pub(crate) fn replace_element(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    events: &mut EventQueues,
    cell: usize,
    element: u16,
    mass: f32,
    temperature: f32,
    disease: u8,
    count: i32,
) {
    world.write.element[cell] = element;
    let empty = phase(elements, element) == 0;
    world.write.mass[cell] = if empty { 0.0 } else { mass };
    world.write.temperature[cell] = if empty { 0.0 } else { temperature };
    notify(world, events, cell);
    world.write.disease_idx[cell] = disease;
    world.write.disease_count[cell] = count;
    world.write.disease_infestation[cell] = 0;
    world.write.disease_growth[cell] = 0.0;
}

pub(crate) fn replace_and_displace(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    diseases: &DiseaseTable,
    events: &mut EventQueues,
    cell: usize,
    element: u16,
    mass: f32,
    temperature: f32,
    disease: u8,
    count: i32,
) {
    let previous = world.write.element[cell];
    match phase(elements, previous) {
        1 => {
            displace_gas(world, elements, diseases, events, cell, previous);
        }
        2 => {
            displace_liquid(world, elements, diseases, events, cell, previous);
        }
        _ => {}
    }
    let temperature = if phase(elements, element) == 0 {
        0.0
    } else {
        smax(smin(temperature, 10000.0), 0.0)
    };
    replace_element(
        world,
        elements,
        events,
        cell,
        element,
        mass,
        temperature,
        disease,
        count,
    );
}

pub(crate) fn remove_phase(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    events: &mut EventQueues,
    cell: usize,
    amount: f32,
    expected_phase: u8,
) {
    if phase(elements, world.write.element[cell]) != expected_phase {
        return;
    }
    let remaining = amount + world.write.mass[cell];
    if remaining > 0.0 {
        world.write.mass[cell] = remaining;
        if f32::MIN_POSITIVE < remaining {
            return;
        }
    } else {
        world.write.mass[cell] = 0.0;
    }
    clear_cell(&mut world.write, world.vacuum_element, cell);
    notify(world, events, cell);
}

impl PendingCommands {
    pub(crate) fn process_modifications(
        &mut self,
        world: &mut PhysicsWorld,
        elements: &ElementTable,
        diseases: &DiseaseTable,
        events: &mut EventQueues,
    ) -> Result<()> {
        for command in self.modifications.drain(..) {
            let cell = world.internal_index(command.cell);
            let mut mass = command.mass;
            let mut temperature = command.temperature;
            if mass > 0.0 && temperature <= 0.0 {
                temperature = smax(smin(temperature, 10000.0), 0.0);
            } else if temperature > 10000.0 {
                if mass > 0.0 || mass.is_nan() {
                    temperature = smax(smin(temperature, 10000.0), 0.0);
                } else {
                    mass = 0.0;
                    temperature = 0.0;
                }
            }
            match command.replace_type {
                1 => replace_element(
                    world,
                    elements,
                    events,
                    cell,
                    command.element,
                    mass,
                    temperature,
                    command.disease,
                    command.count,
                ),
                2 => replace_and_displace(
                    world,
                    elements,
                    diseases,
                    events,
                    cell,
                    command.element,
                    mass,
                    temperature,
                    command.disease,
                    command.count,
                ),
                0 => match phase(elements, command.element) {
                    1 => {
                        if !(mass > 0.0) {
                            remove_phase(world, elements, events, cell, mass, 1);
                        } else {
                            add_gas(
                                world,
                                elements,
                                diseases,
                                events,
                                cell,
                                command.element,
                                mass,
                                temperature,
                                command.disease,
                                command.count,
                            );
                        }
                    }
                    2 => {
                        if mass > 0.0 {
                            add_liquid(
                                world,
                                elements,
                                diseases,
                                events,
                                cell,
                                command.element,
                                mass,
                                temperature,
                                command.disease,
                                command.count,
                            );
                        } else if mass < 0.0 {
                            remove_phase(world, elements, events, cell, mass, 2);
                        }
                    }
                    3 => {
                        if mass > 0.0 {
                            add_solid(
                                world,
                                elements,
                                diseases,
                                events,
                                cell,
                                command.element,
                                mass,
                                temperature,
                                command.disease,
                                command.count,
                                command.subtype as i32,
                            );
                        } else if mass < 0.0 {
                            remove_phase(world, elements, events, cell, mass, 3);
                        }
                    }
                    _ => {}
                },
                _ => {}
            }
            if command.callback != -1 {
                events.callback_info.push(CallbackInfo {
                    callback_idx: command.callback,
                });
            }
        }
        Ok(())
    }
}
