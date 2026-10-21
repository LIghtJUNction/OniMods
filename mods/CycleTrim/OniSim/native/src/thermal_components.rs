//! Independent component state reconstructed from the official native ABI.
#[path = "thermal_components_buildings.rs"]
mod buildings_kernel;
#[path = "thermal_components_chunks.rs"]
mod chunks_kernel;

use crate::bytes::{f32at, i32at, invalid, u16at};
use crate::component_handles::ComponentHandles;
use crate::events::{
    BuildingTemperatureInfo, ComponentStateChangedMessage, ElementChunkInfo, Events,
};
use crate::status::{Error, Result, Status};
use crate::tables::ElementTable;
use crate::world::PhysicsWorld;

pub(crate) const ADD_BUILDING: i32 = 1_739_021_608;
pub(crate) const MODIFY_BUILDING: i32 = 1_818_001_569;
pub(crate) const BUILDING_ENERGY: i32 = -1_348_791_658;
pub(crate) const REMOVE_BUILDING: i32 = -456_116_629;
pub(crate) const ADD_CONTACTS: i32 = -1_338_718_217;
pub(crate) const ADD_CONTACT: i32 = -1_586_724_321;
pub(crate) const REMOVE_CONTACT: i32 = -1_993_857_213;
pub(crate) const REMOVE_CONTACTS: i32 = 697_100_730;
pub(crate) const ADD_CHUNK: i32 = 1_445_724_082;
pub(crate) const MOVE_CHUNK: i32 = -374_911_358;
pub(crate) const MODIFY_CHUNK: i32 = -435_115_907;
pub(crate) const CHUNK_ENERGY: i32 = 1_020_555_667;
pub(crate) const CHUNK_ADJUSTER: i32 = -1_387_601_379;
pub(crate) const REMOVE_CHUNK: i32 = -912_908_555;

#[derive(Clone, Copy)]
pub(crate) struct Building {
    pub(crate) temperature: f32,
    pub(crate) overheat_temperature: f32,
    pub(crate) operating_kw: f32,
    pub(crate) heat_capacity: f32,
    pub(crate) cell_heat_capacity: f32,
    pub(crate) conductivity: f32,
    pub(crate) melt_temperature: f32,
    pub(crate) extents: [i32; 4],
}

#[derive(Clone)]
pub(crate) struct BuildingContacts {
    pub(crate) building: i32,
    // The official update stores cellsInContact but never reads it.
    pub(crate) contacts: Vec<(i32, i32)>,
}

#[derive(Clone, Copy)]
pub(crate) struct ElementChunk {
    pub(crate) cell: usize,
    pub(crate) temperature: f32,
    pub(crate) heat_capacity: f32,
    pub(crate) conductivity: f32,
    pub(crate) surface_over_thickness: f32,
    pub(crate) ground_scale: f32,
    pub(crate) high_temperature: f32,
    pub(crate) low_temperature: f32,
    pub(crate) adjust_temperature: f32,
    pub(crate) adjust_heat_capacity: f32,
    pub(crate) adjust_conductivity: f32,
}

#[derive(Default)]
pub(crate) struct PendingCommands(Vec<(i32, Vec<u8>)>);

impl PendingCommands {
    pub(crate) fn len(&self) -> usize {
        self.0.len()
    }
    pub(crate) fn is_empty(&self) -> bool {
        self.0.is_empty()
    }
}

#[derive(Default)]
pub(crate) struct ThermalComponents {
    pub(crate) buildings: ComponentHandles<Building>,
    pub(crate) contacts: ComponentHandles<BuildingContacts>,
    pub(crate) chunks: ComponentHandles<ElementChunk>,
    pub(crate) building_infos: Vec<BuildingTemperatureInfo>,
    pub(crate) chunk_infos: Vec<ElementChunkInfo>,
    pending: Vec<(i32, Vec<u8>)>,
}

fn message_spec(id: i32) -> Option<(u8, usize)> {
    Some(match id {
        ADD_BUILDING => (0, 44),
        MODIFY_BUILDING => (1, 44),
        BUILDING_ENERGY => (2, 16),
        REMOVE_BUILDING => (3, 8),
        ADD_CONTACTS => (4, 8),
        REMOVE_CONTACT => (5, 8),
        ADD_CONTACT => (6, 12),
        REMOVE_CONTACTS => (7, 8),
        ADD_CHUNK => (8, 32),
        MOVE_CHUNK => (9, 8),
        MODIFY_CHUNK => (10, 12),
        CHUNK_ENERGY => (11, 8),
        CHUNK_ADJUSTER => (12, 16),
        REMOVE_CHUNK => (13, 8),
        _ => return None,
    })
}

pub(crate) fn is_message(id: i32) -> bool {
    message_spec(id).is_some()
}

pub(crate) fn callback(events: &mut Events, callback_idx: i32, sim_handle: i32) {
    if callback_idx != -1 {
        events
            .component_state_changed_messages
            .push(ComponentStateChangedMessage {
                callback_idx,
                sim_handle,
            });
    }
}

fn public_cell(world: &PhysicsWorld, cell: i32) -> Result<usize> {
    if cell < 0 || cell as usize >= world.width * world.height {
        return Err(invalid("element chunk cell is outside the grid"));
    }
    Ok(world.internal_index(cell as usize))
}

fn building_data(bytes: &[u8], world: &PhysicsWorld, table: &ElementTable) -> Result<Building> {
    let element = table
        .elements
        .get(u16at(bytes, 4) as usize)
        .ok_or_else(|| invalid("building element is outside the table"))?;
    let rect = [
        i32at(bytes, 28),
        i32at(bytes, 32),
        i32at(bytes, 36),
        i32at(bytes, 40),
    ];
    if rect[0] < 0
        || rect[1] < 0
        || rect[2] > world.width as i32
        || rect[3] > world.height as i32
        || rect[0] >= rect[2]
        || rect[1] >= rect[3]
    {
        return Err(invalid("building extents have zero area or leave the grid"));
    }
    let capacity = f32at(bytes, 8) * element.record.specific_heat_capacity;
    let cells = (rect[2] - rect[0]) * (rect[3] - rect[1]);
    Ok(Building {
        temperature: f32at(bytes, 12),
        overheat_temperature: f32at(bytes, 20),
        operating_kw: f32at(bytes, 24),
        heat_capacity: capacity,
        cell_heat_capacity: capacity / cells as f32,
        conductivity: f32at(bytes, 16) * element.record.thermal_conductivity,
        melt_temperature: element.record.high_temperature,
        // InitializeFromMessageData 0x5b9d0 adds the padded-grid origin.
        extents: rect.map(|coordinate| coordinate + 1),
    })
}

impl ThermalComponents {
    pub(crate) fn take_engine_state(&mut self) -> Self {
        let pending = std::mem::take(&mut self.pending);
        let mut components = std::mem::take(self);
        components.pending.clear();
        self.pending = pending;
        components
    }

    pub(crate) fn is_message(id: i32) -> bool {
        is_message(id)
    }

    pub(crate) fn queue(&mut self, id: i32, bytes: &[u8]) -> Result<()> {
        let (_, size) =
            message_spec(id).ok_or_else(|| invalid("unknown thermal component message"))?;
        if bytes.len() != size {
            return Err(invalid("thermal component message has wrong size"));
        }
        if self.pending.len() >= 65_536 {
            return Err(Error::new(
                Status::ResourceLimit,
                "thermal component message queue is full",
            ));
        }
        self.pending.try_reserve(1).map_err(|_| allocation())?;
        let mut copy = Vec::new();
        copy.try_reserve_exact(size).map_err(|_| allocation())?;
        copy.extend_from_slice(bytes);
        self.pending.push((id, copy));
        Ok(())
    }

    pub(crate) fn append_pending(&mut self, mut staged: Self) -> Result<()> {
        if self.pending.len().saturating_add(staged.pending.len())
            > crate::frames::MAX_PENDING_COMMANDS
        {
            return Err(Error::new(
                Status::ResourceLimit,
                "thermal component message queue is full",
            ));
        }
        self.pending
            .try_reserve(staged.pending.len())
            .map_err(|_| allocation())?;
        self.pending.append(&mut staged.pending);
        Ok(())
    }

    pub(crate) fn queue_energy_batch(&mut self, records: &[[u8; 16]]) -> Result<()> {
        let new_len = self
            .pending
            .len()
            .checked_add(records.len())
            .ok_or_else(allocation)?;
        if new_len > 65_536 {
            return Err(Error::new(
                Status::ResourceLimit,
                "thermal component message queue is full",
            ));
        }
        let mut batch = Vec::new();
        batch
            .try_reserve_exact(records.len())
            .map_err(|_| allocation())?;
        for record in records {
            let mut bytes = Vec::new();
            bytes
                .try_reserve_exact(record.len())
                .map_err(|_| allocation())?;
            bytes.extend_from_slice(record);
            batch.push((BUILDING_ENERGY, bytes));
        }
        self.pending
            .try_reserve(records.len())
            .map_err(|_| allocation())?;
        self.pending.extend(batch);
        Ok(())
    }

    pub(crate) fn take_pending(&mut self) -> PendingCommands {
        PendingCommands(std::mem::take(&mut self.pending))
    }

    pub(crate) fn process_pending(
        &mut self,
        mut commands: PendingCommands,
        world: &PhysicsWorld,
        table: &ElementTable,
        events: &mut Events,
    ) -> Result<()> {
        self.process_before_backwall(&mut commands, world, table, events)?;
        self.process_after_backwall(&mut commands, world, table, events)
    }

    pub(crate) fn process_before_backwall(
        &mut self,
        commands: &mut PendingCommands,
        world: &PhysicsWorld,
        table: &ElementTable,
        events: &mut Events,
    ) -> Result<()> {
        self.process_ranks(commands, 0..13, world, table, events)
    }

    pub(crate) fn process_after_backwall(
        &mut self,
        commands: &mut PendingCommands,
        world: &PhysicsWorld,
        table: &ElementTable,
        events: &mut Events,
    ) -> Result<()> {
        self.process_ranks(commands, 13..14, world, table, events)
    }

    fn process_ranks(
        &mut self,
        commands: &mut PendingCommands,
        ranks: std::ops::Range<u8>,
        world: &PhysicsWorld,
        table: &ElementTable,
        events: &mut Events,
    ) -> Result<()> {
        // ProcessFrame 0x4ad00 drains per-type vectors in this exact order,
        // preserving order within each vector rather than message arrival order.
        // BackwallData is processed between chunk adjusters and chunk removal.
        for rank in ranks.clone() {
            for (id, bytes) in &commands.0 {
                if message_spec(*id).unwrap().0 == rank {
                    self.apply(*id, bytes, world, table, events)?;
                }
            }
        }
        commands
            .0
            .retain(|(id, _)| !ranks.contains(&message_spec(*id).unwrap().0));
        Ok(())
    }

    fn apply(
        &mut self,
        id: i32,
        b: &[u8],
        world: &PhysicsWorld,
        table: &ElementTable,
        events: &mut Events,
    ) -> Result<()> {
        let handle = i32at(b, 0);
        match id {
            ADD_BUILDING => {
                let data = building_data(b, world, table)?;
                let sim_handle = self.buildings.insert(data)?;
                self.resize_infos()?;
                callback(events, handle, sim_handle);
            }
            MODIFY_BUILDING => {
                if self.buildings.contains(handle) {
                    let data = building_data(b, world, table)?;
                    let old = self.buildings.get(handle)?;
                    if old.overheat_temperature <= old.temperature
                        && data.temperature < old.overheat_temperature
                    {
                        events
                            .building_no_longer_overheated_infos
                            .push(crate::events::MeltedInfo { handle });
                    }
                    *self.buildings.get_mut(handle)? = data;
                }
            }
            BUILDING_ENERGY => {
                if let Ok(building) = self.buildings.get_mut(handle) {
                    let low = crate::numeric::smin(f32at(b, 8), building.temperature);
                    let high = crate::numeric::smax(f32at(b, 12), building.temperature);
                    if !(low < 0.0 || 10_000.0 < high) {
                        let [x0, y0, x1, y1] = building.extents;
                        let capacity = ((x1 - x0) * (y1 - y0)) as f32 * building.cell_heat_capacity;
                        let candidate = crate::numeric::smax(
                            crate::numeric::smin(
                                building.temperature + f32at(b, 4) / capacity,
                                high,
                            ),
                            low,
                        );
                        if !(candidate <= 0.0 || 10_000.0 <= candidate) {
                            building.temperature = candidate;
                        }
                    }
                }
            }
            REMOVE_BUILDING => {
                self.resize_infos()?;
                self.buildings.remove(handle)?;
                self.building_infos[ComponentHandles::<Building>::slot(handle)].temperature = -1.0;
                callback(events, i32at(b, 4), -1);
            }
            ADD_CONTACTS => {
                let building = i32at(b, 4);
                let sim_handle = if self.buildings.contains(building) {
                    self.contacts.insert(BuildingContacts {
                        building,
                        contacts: Vec::new(),
                    })?
                } else {
                    -1
                };
                // Register 0x5ddf0 emits once, and ProcessFrame emits again.
                callback(events, handle, sim_handle);
                callback(events, handle, sim_handle);
            }
            ADD_CONTACT => {
                if let Ok(group) = self.contacts.get_mut(handle) {
                    group.contacts.try_reserve(1).map_err(|_| allocation())?;
                    group.contacts.push((i32at(b, 4), i32at(b, 8)));
                }
            }
            REMOVE_CONTACT => {
                if let Ok(group) = self.contacts.get_mut(handle)
                    && let Some(at) = group
                        .contacts
                        .iter()
                        .position(|entry| entry.0 == i32at(b, 4))
                {
                    group.contacts.remove(at);
                }
            }
            REMOVE_CONTACTS => {
                self.contacts.remove(i32at(b, 4))?;
                callback(events, handle, -1);
            }
            ADD_CHUNK => {
                let element = table
                    .elements
                    .get(u16at(b, 28) as usize)
                    .ok_or_else(|| invalid("chunk element is outside the table"))?;
                let data = ElementChunk {
                    cell: public_cell(world, handle)?,
                    temperature: f32at(b, 12),
                    heat_capacity: f32at(b, 8) * element.record.specific_heat_capacity,
                    conductivity: element.record.thermal_conductivity,
                    surface_over_thickness: f32at(b, 16) / f32at(b, 20),
                    ground_scale: f32at(b, 24),
                    high_temperature: element.record.high_temperature,
                    low_temperature: element.record.low_temperature,
                    adjust_temperature: 0.0,
                    adjust_heat_capacity: 0.0,
                    adjust_conductivity: 0.0,
                };
                let sim_handle = self.chunks.insert(data)?;
                self.resize_infos()?;
                callback(events, i32at(b, 4), sim_handle);
            }
            MOVE_CHUNK => {
                if self.chunks.contains(handle) {
                    let cell = public_cell(world, i32at(b, 4))?;
                    self.chunks.get_mut(handle)?.cell = cell;
                }
            }
            MODIFY_CHUNK => {
                if let Ok(chunk) = self.chunks.get_mut(handle) {
                    chunk.temperature = f32at(b, 4);
                    chunk.heat_capacity = f32at(b, 8);
                }
            }
            CHUNK_ENERGY => {
                if let Ok(chunk) = self.chunks.get_mut(handle)
                    && 0.0 < chunk.heat_capacity
                {
                    chunk.temperature = crate::numeric::smax(
                        f32at(b, 4) / chunk.heat_capacity + chunk.temperature,
                        0.0,
                    );
                }
            }
            CHUNK_ADJUSTER => {
                if let Ok(chunk) = self.chunks.get_mut(handle) {
                    chunk.adjust_temperature = f32at(b, 4);
                    chunk.adjust_heat_capacity = f32at(b, 8);
                    chunk.adjust_conductivity = f32at(b, 12);
                }
            }
            REMOVE_CHUNK => {
                self.chunks.remove(handle)?;
                callback(events, i32at(b, 4), -1);
            }
            _ => unreachable!(),
        }
        Ok(())
    }

    pub(crate) fn resize_infos(&mut self) -> Result<()> {
        // Official BuildingTemperatureInfo default construction (0x5b490)
        // uses an invalid handle until a component update fills this slot.
        resize(
            &mut self.building_infos,
            self.buildings.slot_count(),
            BuildingTemperatureInfo {
                handle: -1,
                temperature: 0.0,
            },
        )?;
        resize(
            &mut self.chunk_infos,
            self.chunks.slot_count(),
            ElementChunkInfo::default(),
        )
    }

    pub(crate) fn update_data_list_only(&mut self) -> Result<()> {
        self.resize_infos()?;
        for (dense, building) in self.buildings.data.iter().enumerate() {
            let handle = self.buildings.handle_at(dense);
            self.building_infos[ComponentHandles::<Building>::slot(handle)] =
                BuildingTemperatureInfo {
                    handle,
                    temperature: building.temperature,
                };
        }
        for (dense, chunk) in self.chunks.data.iter().enumerate() {
            let slot = ComponentHandles::<ElementChunk>::slot(self.chunks.handle_at(dense));
            self.chunk_infos[slot] = ElementChunkInfo {
                temperature: chunk.temperature,
                delta_kj: 0.0,
            };
        }
        Ok(())
    }

    pub(crate) fn publish(&mut self, events: &mut Events) -> Result<()> {
        copy_infos(&mut events.building_temperatures, &self.building_infos)?;
        copy_infos(&mut events.element_chunk_infos, &self.chunk_infos)?;
        // CopySimDataToGame 0x58600 publishes these values and immediately
        // clears every slot's accumulated energy in the simulation buffer.
        for info in &mut self.chunk_infos {
            info.delta_kj = 0.0;
        }
        Ok(())
    }
}

fn allocation() -> Error {
    Error::new(
        Status::ResourceLimit,
        "could not allocate thermal component state",
    )
}

fn resize<T: Clone>(values: &mut Vec<T>, count: usize, value: T) -> Result<()> {
    values
        .try_reserve(count.saturating_sub(values.len()))
        .map_err(|_| allocation())?;
    values.resize(count, value);
    Ok(())
}

fn copy_infos<T: Copy>(out: &mut Vec<T>, values: &[T]) -> Result<()> {
    out.try_reserve(values.len().saturating_sub(out.len()))
        .map_err(|_| allocation())?;
    out.clear();
    out.extend_from_slice(values);
    Ok(())
}
