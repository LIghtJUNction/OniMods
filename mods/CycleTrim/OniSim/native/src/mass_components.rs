//! Stateful element and disease components recovered from the official ABI.
use crate::bytes::{f32at, i32at, invalid, u16at};
use crate::component_handles::ComponentHandles;
use crate::disease_emitters::DiseaseEmitterData;
use crate::events::{ComponentStateChangedMessage, EventQueues};
use crate::status::{Error, Result, Status};
use crate::tables::{DiseaseTable, ElementTable};
use crate::worker_shape::WorldShape;
use crate::world::PhysicsWorld;

#[path = "mass_components_consumer.rs"]
mod consumer;
#[path = "mass_components_emitter.rs"]
mod emitter;
#[path = "mass_components_flood.rs"]
mod flood;
use consumer::Consumer;
use emitter::Emitter;
pub(crate) use flood::FloodScratch;

const ADD_CONSUMER: i32 = 2024405073;
const MODIFY_CONSUMER: i32 = 1575539738;
const REMOVE_CONSUMER: i32 = 894417742;
const ADD_EMITTER: i32 = -505471181;
const MODIFY_EMITTER: i32 = 403589164;
const REMOVE_EMITTER: i32 = -1524118282;
const ADD_DISEASE_EMITTER: i32 = 1486783027;
const MODIFY_DISEASE_EMITTER: i32 = -1899123924;
const REMOVE_DISEASE_EMITTER: i32 = 468135926;
const ADD_DISEASE_CONSUMER: i32 = 348345681;
const MODIFY_DISEASE_CONSUMER: i32 = -1822987624;
const REMOVE_DISEASE_CONSUMER: i32 = -781641650;

struct Pending {
    id: i32,
    bytes: Vec<u8>,
}

/// Commands moved from submission storage into one exclusive game frame.
#[derive(Default)]
pub(crate) struct PendingCommands {
    commands: Vec<Pending>,
}

impl PendingCommands {
    pub(crate) fn len(&self) -> usize {
        self.commands.len()
    }
    pub(crate) fn is_empty(&self) -> bool {
        self.commands.is_empty()
    }
}

#[derive(Default)]
pub(crate) struct MassComponents {
    consumers: ComponentHandles<Consumer>,
    emitters: ComponentHandles<Emitter>,
    // Native emitter output lives in the component manager across publications,
    // unlike the transient frame event queues. CopySimDataToGame clears its
    // element/mass/temperature after copying, including retired handle slots.
    emitted_mass_infos: Vec<crate::events::EmittedMassInfo>,
    disease_emitters: ComponentHandles<DiseaseEmitterData>,
    // DiseaseConsumer Register/Unregister manage real handles. Its Modify,
    // Update and UpdateDataListOnly are single-ret functions in this binary.
    disease_consumers: ComponentHandles<()>,
    pending: Vec<Pending>,
    scratch: FloodScratch,
}

pub(crate) fn is_message(id: i32) -> bool {
    payload_size(id).is_some()
}
fn payload_size(id: i32) -> Option<usize> {
    Some(match id {
        ADD_CONSUMER | MODIFY_CONSUMER | ADD_DISEASE_CONSUMER => 12,
        REMOVE_CONSUMER | REMOVE_EMITTER | REMOVE_DISEASE_EMITTER | REMOVE_DISEASE_CONSUMER => 8,
        ADD_EMITTER => 16,
        MODIFY_EMITTER => 32,
        ADD_DISEASE_EMITTER => 4,
        MODIFY_DISEASE_EMITTER | MODIFY_DISEASE_CONSUMER => 20,
        _ => return None,
    })
}

impl MassComponents {
    pub(crate) fn append_pending(&mut self, mut staged: Self) -> Result<()> {
        if self.pending.len().saturating_add(staged.pending.len())
            > crate::frames::MAX_PENDING_COMMANDS
        {
            return Err(Error::new(
                Status::ResourceLimit,
                "mass-component command queue is full",
            ));
        }
        self.pending
            .try_reserve(staged.pending.len())
            .map_err(allocation)?;
        self.pending.append(&mut staged.pending);
        Ok(())
    }

    pub(crate) fn take_engine_state(&mut self) -> Self {
        let pending = std::mem::take(&mut self.pending);
        let mut components = std::mem::take(self);
        components.pending.clear();
        self.pending = pending;
        components
    }

    pub(crate) fn queue(
        &mut self,
        id: i32,
        bytes: &[u8],
        shape: &WorldShape,
        elements: &ElementTable,
        diseases: &DiseaseTable,
    ) -> Result<bool> {
        let Some(size) = payload_size(id) else {
            return Ok(false);
        };
        if bytes.len() != size {
            return Err(invalid("incorrect mass-component payload size"));
        }
        match id {
            ADD_CONSUMER => {
                game_cell(shape, i32at(bytes, 0))?;
                element(elements, u16at(bytes, 10))?;
            }
            MODIFY_CONSUMER => {
                game_cell(shape, i32at(bytes, 4))?;
            }
            MODIFY_EMITTER => {
                game_cell(shape, i32at(bytes, 4))?;
                element(elements, u16at(bytes, 28))?;
                disease(diseases, bytes[31])?;
            }
            MODIFY_DISEASE_EMITTER => {
                game_cell(shape, i32at(bytes, 4))?;
                disease(diseases, bytes[8])?;
            }
            _ => {}
        }
        if self.pending.len() >= crate::frames::MAX_PENDING_COMMANDS {
            return Err(Error::new(
                Status::ResourceLimit,
                "mass-component command queue is full",
            ));
        }
        self.pending.try_reserve(1).map_err(allocation)?;
        let mut owned = Vec::new();
        owned.try_reserve_exact(bytes.len()).map_err(allocation)?;
        owned.extend_from_slice(bytes);
        self.pending.push(Pending { id, bytes: owned });
        Ok(true)
    }

    pub(crate) fn flood_scratch(&mut self) -> &mut FloodScratch {
        &mut self.scratch
    }

    pub(crate) fn take_pending(&mut self) -> PendingCommands {
        PendingCommands {
            commands: std::mem::take(&mut self.pending),
        }
    }

    pub(crate) fn process_pending(
        &mut self,
        mut commands: PendingCommands,
        world: &PhysicsWorld,
        events: &mut EventQueues,
    ) -> Result<()> {
        self.process_element_pending(&mut commands, world, events)?;
        self.process_disease_pending(&mut commands, world, events)
    }

    pub(crate) fn process_element_pending(
        &mut self,
        commands: &mut PendingCommands,
        world: &PhysicsWorld,
        events: &mut EventQueues,
    ) -> Result<()> {
        // ProcessFrame groups by operation type, independently of arrival order.
        for id in [
            ADD_CONSUMER,
            MODIFY_CONSUMER,
            REMOVE_CONSUMER,
            ADD_EMITTER,
            MODIFY_EMITTER,
            REMOVE_EMITTER,
        ] {
            self.apply_group(commands, id, world, events)?;
        }
        Ok(())
    }

    pub(crate) fn process_disease_pending(
        &mut self,
        commands: &mut PendingCommands,
        world: &PhysicsWorld,
        events: &mut EventQueues,
    ) -> Result<()> {
        for id in [
            ADD_DISEASE_EMITTER,
            MODIFY_DISEASE_EMITTER,
            REMOVE_DISEASE_EMITTER,
            ADD_DISEASE_CONSUMER,
            MODIFY_DISEASE_CONSUMER,
            REMOVE_DISEASE_CONSUMER,
        ] {
            self.apply_group(commands, id, world, events)?;
        }
        Ok(())
    }

    fn apply_group(
        &mut self,
        commands: &mut PendingCommands,
        id: i32,
        world: &PhysicsWorld,
        events: &mut EventQueues,
    ) -> Result<()> {
        let mut selected = Vec::new();
        selected
            .try_reserve(commands.commands.len())
            .map_err(allocation)?;
        let mut retained = Vec::new();
        retained
            .try_reserve(commands.commands.len())
            .map_err(allocation)?;
        for command in commands.commands.drain(..) {
            if command.id == id {
                selected.push(command);
            } else {
                retained.push(command);
            }
        }
        commands.commands = retained;
        for command in selected {
            let b = &command.bytes;
            match id {
                ADD_CONSUMER => {
                    let handle = self.consumers.insert(Consumer {
                        rate: 0.0,
                        radius: b[8],
                        configuration: b[9],
                        element: u16at(b, 10),
                        cursor: 0,
                        cell: active_game_cell(world, i32at(b, 0))?,
                    })?;
                    state_callback(events, i32at(b, 4), handle);
                }
                MODIFY_CONSUMER => {
                    if let Ok(data) = self.consumers.get_mut(i32at(b, 0)) {
                        data.cell = active_game_cell(world, i32at(b, 4))?;
                        data.rate = f32at(b, 8);
                    }
                }
                REMOVE_CONSUMER => {
                    remove(&mut self.consumers, i32at(b, 0))?;
                    state_callback(events, i32at(b, 4), -1);
                }
                ADD_EMITTER => {
                    let handle = self.emitters.insert(Emitter::new(
                        f32at(b, 0),
                        i32at(b, 8),
                        i32at(b, 12),
                    ))?;
                    let dense = self.emitters.data.len() - 1;
                    if let Some(info) = self.emitted_mass_infos.get_mut(dense) {
                        *info = emitter::empty_mass();
                    }
                    state_callback(events, i32at(b, 4), handle);
                }
                MODIFY_EMITTER => {
                    if let Ok(data) = self.emitters.get_mut(i32at(b, 0)) {
                        data.modify(world, b)?;
                    }
                }
                REMOVE_EMITTER => {
                    remove(&mut self.emitters, i32at(b, 0))?;
                    state_callback(events, i32at(b, 4), -1);
                }
                ADD_DISEASE_EMITTER => {
                    let handle = self
                        .disease_emitters
                        .insert(DiseaseEmitterData::default())?;
                    state_callback(events, i32at(b, 0), handle);
                }
                MODIFY_DISEASE_EMITTER => {
                    if let Ok(data) = self.disease_emitters.get_mut(i32at(b, 0)) {
                        data.cell = active_game_cell(world, i32at(b, 4))? as i32;
                        data.disease_idx = b[8];
                        data.max_depth = b[9];
                        data.emit_interval = f32at(b, 12);
                        data.emit_count = i32at(b, 16);
                    }
                }
                REMOVE_DISEASE_EMITTER => {
                    remove(&mut self.disease_emitters, i32at(b, 0))?;
                    state_callback(events, i32at(b, 4), -1);
                }
                ADD_DISEASE_CONSUMER => {
                    let handle = self.disease_consumers.insert(())?;
                    state_callback(events, i32at(b, 0), handle);
                }
                MODIFY_DISEASE_CONSUMER => {}
                REMOVE_DISEASE_CONSUMER => {
                    remove(&mut self.disease_consumers, i32at(b, 0))?;
                    state_callback(events, i32at(b, 4), -1);
                }
                _ => unreachable!(),
            }
        }
        Ok(())
    }
}

fn remove<T>(store: &mut ComponentHandles<T>, handle: i32) -> Result<()> {
    if store.contains(handle) {
        store.remove(handle)?;
    }
    Ok(())
}
fn state_callback(events: &mut EventQueues, callback_idx: i32, sim_handle: i32) {
    if callback_idx != -1 {
        events
            .component_state_changed_messages
            .push(ComponentStateChangedMessage {
                callback_idx,
                sim_handle,
            });
    }
}
fn game_cell(shape: &WorldShape, cell: i32) -> Result<usize> {
    cell_index(cell, shape.width, shape.height, shape.stride)
}

fn active_game_cell(world: &PhysicsWorld, cell: i32) -> Result<usize> {
    cell_index(cell, world.width, world.height, world.stride)
}

fn cell_index(cell: i32, width: usize, height: usize, stride: usize) -> Result<usize> {
    if cell < 0 || cell as usize >= width * height {
        return Err(invalid("component cell is outside the public grid"));
    }
    let public = cell as usize;
    let internal = (public / width + 1) * stride + public % width + 1;
    if internal % stride > u16::MAX as usize || internal / stride > u16::MAX as usize {
        return Err(invalid("component cell exceeds official coordinate space"));
    }
    Ok(internal)
}

fn element(table: &ElementTable, value: u16) -> Result<()> {
    if value as usize >= table.len() {
        Err(invalid("component element index is invalid"))
    } else {
        Ok(())
    }
}
fn disease(table: &DiseaseTable, value: u8) -> Result<()> {
    if value != 255 && value as usize >= table.len() {
        Err(invalid("component disease index is invalid"))
    } else {
        Ok(())
    }
}
fn allocation(_: std::collections::TryReserveError) -> Error {
    Error::new(
        Status::ResourceLimit,
        "could not allocate mass-component storage",
    )
}
