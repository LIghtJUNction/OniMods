//! Conduit contents heat exchange, reconstructed from official ELF 0x2f0e0..0x306d0.
use crate::numeric::{smax, smin};
use crate::status::{Error, Result, Status};
use crate::tables::ElementRecord;

const INDEX_MASK: u32 = 0x00ff_ffff;
const TYPE_MASK: u32 = 0x00f0_0000;
const MAX_ENTRIES: usize = 0x0010_0000;

#[derive(Clone, Copy)]
struct Entry {
    temperature: f32,
    conductivity: f32,
    heat_capacity: f32,
    building: i32,
    building_heat_capacity: f32,
    building_conductivity: f32,
    insulated: bool,
    low_temperature: f32,
    high_temperature: f32,
}

impl Entry {
    fn set(&mut self, temperature: f32, mass: f32, element: &ElementRecord) {
        self.temperature = if temperature > 10000.0 {
            element.default_temperature
        } else {
            temperature
        };
        self.conductivity = element.thermal_conductivity;
        self.heat_capacity = mass * element.specific_heat_capacity;
        self.low_temperature = if element.low_transition != u16::MAX {
            element.low_temperature
        } else {
            0.0
        };
        self.high_temperature = if element.high_transition != u16::MAX {
            element.high_temperature
        } else {
            f32::MAX
        };
    }
}

#[repr(C, packed(4))]
#[derive(Default)]
pub(crate) struct UpdateData {
    count: i32,
    temperatures: usize,
    frozen_count: i32,
    frozen: usize,
    melted_count: i32,
    melted: usize,
}

const _: () = assert!(std::mem::size_of::<UpdateData>() == 36);

#[derive(Default)]
pub(crate) struct ConduitManager {
    entries: Vec<Entry>,
    handles: Vec<u32>,
    positions: Vec<usize>,
    versions: Vec<u8>,
    free: Vec<u32>,
    pending_remove: [Vec<u32>; 2],
    remove_queue: usize,
    temperatures: Vec<f32>,
    frozen: Vec<i32>,
    melted: Vec<i32>,
    published: UpdateData,
}

pub(crate) struct UpdatePlan {
    temperatures: Vec<f32>,
    entry_temperatures: Vec<f32>,
    frozen: Vec<i32>,
    melted: Vec<i32>,
    pub(crate) energy: Vec<[u8; 16]>,
}

fn allocation() -> Error {
    Error::new(
        Status::ResourceLimit,
        "could not allocate conduit temperature state",
    )
}

fn reserve<T>(values: &mut Vec<T>, additional: usize) -> Result<()> {
    values.try_reserve(additional).map_err(|_| allocation())
}

fn bad_handle() -> Error {
    Error::new(
        Status::InvalidArgument,
        "invalid or stale conduit temperature handle",
    )
}

impl ConduitManager {
    fn position(&self, handle: i32) -> Result<usize> {
        let handle = handle as u32;
        let index = (handle & INDEX_MASK) as usize;
        if handle & TYPE_MASK != 0 || self.versions.get(index) != Some(&((handle >> 24) as u8)) {
            return Err(bad_handle());
        }
        let position = self.positions[index];
        if self.handles.get(position) != Some(&handle) {
            return Err(bad_handle());
        }
        Ok(position)
    }

    pub(crate) fn add(
        &mut self,
        temperature: f32,
        mass: f32,
        element: &ElementRecord,
        building: i32,
        building_heat_capacity: f32,
        building_conductivity: f32,
        insulated: bool,
    ) -> Result<i32> {
        reserve(&mut self.entries, 1)?;
        reserve(&mut self.handles, 1)?;
        let handle = if let Some(&handle) = self.free.last() {
            handle
        } else {
            if self.positions.len() >= MAX_ENTRIES {
                return Err(allocation());
            }
            reserve(&mut self.positions, 1)?;
            reserve(&mut self.versions, 1)?;
            self.positions.len() as u32
        };
        let index = (handle & INDEX_MASK) as usize;
        if self.free.pop().is_none() {
            self.positions.push(self.entries.len());
            self.versions.push(0);
        } else {
            self.positions[index] = self.entries.len();
        }
        let mut entry = Entry {
            temperature,
            conductivity: 0.0,
            heat_capacity: 0.0,
            building,
            building_heat_capacity,
            building_conductivity,
            insulated,
            low_temperature: 0.0,
            high_temperature: f32::MAX,
        };
        entry.set(temperature, mass, element);
        self.entries.push(entry);
        self.handles.push(handle);
        Ok(handle as i32)
    }

    pub(crate) fn set(
        &mut self,
        handle: i32,
        temperature: f32,
        mass: f32,
        element: &ElementRecord,
    ) -> Result<()> {
        let position = self.position(handle)?;
        self.entries[position].set(temperature, mass, element);
        Ok(())
    }

    pub(crate) fn remove(&mut self, handle: i32) -> Result<()> {
        let position = self.position(handle)?;
        if self
            .pending_remove
            .iter()
            .any(|queue| queue.contains(&(handle as u32)))
        {
            return Err(bad_handle());
        }
        reserve(&mut self.pending_remove[self.remove_queue], 1)?;
        let entry = &mut self.entries[position];
        entry.building = -1;
        entry.building_heat_capacity = -1.0;
        entry.building_conductivity = -1.0;
        self.pending_remove[self.remove_queue].push(handle as u32);
        Ok(())
    }

    pub(crate) fn release_queued_handles(&mut self) -> Result<()> {
        let queue = (self.remove_queue + 1) % 2;
        let count = self.pending_remove[queue].len();
        reserve(&mut self.free, count)?;
        // Validate the complete release batch before compacting its dense arrays.
        for &handle in &self.pending_remove[queue] {
            self.position(handle as i32)?;
        }
        self.remove_queue = queue;
        for handle in self.pending_remove[queue].drain(..) {
            let index = (handle & INDEX_MASK) as usize;
            let position = self.positions[index];
            self.versions[index] = self.versions[index].wrapping_add(1);
            self.free
                .push((u32::from(self.versions[index]) << 24) | index as u32);
            self.positions[index] = 0;
            self.entries.swap_remove(position);
            self.handles.swap_remove(position);
            if let Some(&moved) = self.handles.get(position) {
                self.positions[(moved & INDEX_MASK) as usize] = position;
            }
        }
        Ok(())
    }

    pub(crate) fn clear(&mut self) {
        self.entries.clear();
        self.handles.clear();
        self.positions.clear();
        self.free.clear();
        self.versions.clear();
    }

    /// The caller keeps the managed BuildingTemperatureInfo array alive for this call.
    pub(crate) unsafe fn prepare_update(
        &self,
        dt: f32,
        building_data: *const u8,
    ) -> Result<UpdatePlan> {
        let mut plan = UpdatePlan {
            temperatures: Vec::new(),
            entry_temperatures: Vec::new(),
            frozen: Vec::new(),
            melted: Vec::new(),
            energy: Vec::new(),
        };
        reserve(&mut plan.temperatures, self.positions.len())?;
        reserve(&mut plan.entry_temperatures, self.entries.len())?;
        reserve(&mut plan.frozen, self.entries.len())?;
        reserve(&mut plan.melted, self.entries.len())?;
        reserve(&mut plan.energy, self.entries.len())?;
        let retained = self.temperatures.len().min(self.positions.len());
        plan.temperatures
            .extend_from_slice(&self.temperatures[..retained]);
        plan.temperatures.resize(self.positions.len(), 0.0);
        for (position, entry) in self.entries.iter().enumerate() {
            plan.entry_temperatures.push(entry.temperature);
            let index = (self.handles[position] & INDEX_MASK) as usize;
            if building_data.is_null() {
                plan.temperatures[index] = entry.temperature;
                continue;
            }
            if entry.building as u32 & TYPE_MASK != 0 {
                continue;
            }
            if !(f32::from_bits(0x38d1_b717) < entry.heat_capacity
                && f32::from_bits(0x38d1_b717) < entry.building_heat_capacity)
            {
                plan.temperatures[index] = entry.temperature;
                continue;
            }
            let building_index = (entry.building as u32 & INDEX_MASK) as usize;
            let address = (building_data as usize)
                .checked_add(building_index * 8 + 4)
                .ok_or_else(|| {
                    Error::new(
                        Status::InvalidArgument,
                        "building temperature pointer overflow",
                    )
                })?;
            let destination = unsafe { std::ptr::read_unaligned(address as *const f32) };
            if !(destination > 0.0) {
                plan.temperatures[index] = entry.temperature;
                continue;
            }
            let old = entry.temperature;
            let flow = calculate_energy_flow(
                entry.insulated,
                old,
                entry.conductivity,
                destination,
                entry.building_conductivity,
                50.0,
            );
            let transfer = clamp_energy_transfer(
                dt,
                old,
                entry.heat_capacity,
                destination,
                entry.building_heat_capacity,
                flow,
            );
            let temperature = modify_temperature(
                old,
                entry.heat_capacity,
                destination,
                entry.building_heat_capacity,
                -transfer,
            );
            plan.entry_temperatures[position] = temperature;
            plan.temperatures[index] = temperature;
            if transfer.abs() > f32::from_bits(0x3586_37bd) {
                let mut message = [0; 16];
                message[0..4].copy_from_slice(&entry.building.to_le_bytes());
                message[4..8]
                    .copy_from_slice(&((old - temperature) * entry.heat_capacity).to_le_bytes());
                message[8..12].copy_from_slice(&smin(destination, old).to_le_bytes());
                message[12..16].copy_from_slice(&smax(destination, old).to_le_bytes());
                plan.energy.push(message);
            }
            if temperature < entry.low_temperature - 3.0 {
                plan.frozen.push(index as i32);
            } else if temperature > entry.high_temperature + 3.0 {
                plan.melted.push(index as i32);
            }
        }
        Ok(plan)
    }

    // The same manager lock must cover prepare, energy admission, and commit.
    // Every allocation and fallible check precedes this operation.
    pub(crate) fn commit_update(&mut self, plan: UpdatePlan) -> *const u8 {
        for (entry, temperature) in self.entries.iter_mut().zip(plan.entry_temperatures) {
            entry.temperature = temperature;
        }
        self.temperatures = plan.temperatures;
        self.frozen = plan.frozen;
        self.melted = plan.melted;
        self.published = UpdateData {
            count: self.temperatures.len() as i32,
            temperatures: span(&self.temperatures),
            frozen_count: self.frozen.len() as i32,
            frozen: span(&self.frozen),
            melted_count: self.melted.len() as i32,
            melted: span(&self.melted),
        };
        (&self.published as *const UpdateData).cast()
    }
}

fn span<T>(values: &[T]) -> usize {
    if values.is_empty() {
        0
    } else {
        values.as_ptr() as usize
    }
}

fn calculate_energy_flow(
    insulated: bool,
    source: f32,
    source_k: f32,
    destination: f32,
    destination_k: f32,
    scale: f32,
) -> f32 {
    let difference = (source - destination) * scale;
    if insulated {
        difference * smin(source_k, destination_k)
    } else {
        difference * ((source_k + destination_k) * 0.5)
    }
}

fn clamp_energy_transfer(
    dt: f32,
    source: f32,
    source_capacity: f32,
    destination: f32,
    destination_capacity: f32,
    flow: f32,
) -> f32 {
    let amount = (dt * f32::from_bits(0x3a83_126f)) * flow;
    let high = smax(source, destination);
    let low = smin(source, destination);
    let source_delta =
        (smax(smin(source - amount / source_capacity, high), low) - source).abs() * source_capacity;
    let destination_delta =
        (smax(smin(amount / destination_capacity + destination, high), low) - destination).abs()
            * destination_capacity;
    smin(destination_delta, source_delta) * if flow < 0.0 { -1.0 } else { 1.0 }
}

fn modify_temperature(
    source: f32,
    source_capacity: f32,
    destination: f32,
    destination_capacity: f32,
    energy: f32,
) -> f32 {
    let next = smax(energy / source_capacity + source, 0.0);
    let other = smax(destination - energy / destination_capacity, 0.0);
    if (next - other) * (source - destination) < 0.0 {
        let capacity = source_capacity + destination_capacity;
        (destination_capacity / capacity) * destination + (source_capacity / capacity) * source
    } else {
        next
    }
}
