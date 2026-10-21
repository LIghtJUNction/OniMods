use crate::component_handles::ComponentHandles;
use crate::events::{BuildingTemperatureInfo, Events, MeltedInfo};
use crate::numeric::{smax, smin};
use crate::status::Result;
use crate::tables::ElementTable;
use crate::thermal_components::{Building, ThermalComponents};
use crate::world::{PhysicsWorld, Region};

fn contains(region: &Region, extents: [i32; 4]) -> bool {
    region.min_x <= extents[0]
        && region.min_y <= extents[1]
        && extents[2] <= region.max_x
        && extents[3] <= region.max_y
}

fn clamp(value: f32, low: f32, high: f32) -> f32 {
    smax(smin(value, high), low)
}

impl ThermalComponents {
    /// BuildingHeatExchange::Update, official ELF 0x5be00. Every covered
    /// cell sees the same initial building temperature; the summed energy is
    /// applied to the whole building only after its rectangle is complete.
    pub(crate) fn update_buildings(
        &mut self,
        dt: f32,
        region: &Region,
        world: &mut PhysicsWorld,
        elements: &ElementTable,
        events: &mut Events,
    ) -> Result<()> {
        self.resize_infos()?;
        let step = dt * f32::from_bits(0x33a5_20a7);
        for dense in 0..self.buildings.data.len() {
            let building = self.buildings.data[dense];
            if !contains(region, building.extents) {
                continue;
            }
            let handle = self.buildings.handle_at(dense);
            let initial = building.temperature;
            let mut final_temperature = initial;
            if 0.0 < building.cell_heat_capacity && 0.0 <= initial {
                let [min_x, min_y, max_x, max_y] = building.extents;
                let mut low = initial;
                let mut high = initial;
                let mut energy = 0.0;
                for y in min_y..max_y {
                    for x in min_x..max_x {
                        let cell = y as usize * world.stride + x as usize;
                        if !(0.0 < world.write.mass[cell]) {
                            continue;
                        }
                        let record = &elements.elements[world.write.element[cell] as usize].record;
                        let cell_capacity = world.write.mass[cell] * record.specific_heat_capacity;
                        let cell_temperature = world.write.temperature[cell];
                        let delta = cell_temperature - initial;
                        let selected_capacity = if delta < 0.0 {
                            building.cell_heat_capacity
                        } else {
                            cell_capacity
                        };
                        let insulation = world.write.insulation[cell] as f32;
                        let scaled_step = step * world.building_heat_transfer_scale;
                        let contact = (selected_capacity * record.thermal_conductivity)
                            * (building.conductivity * delta);
                        let mut transfer = ((insulation * insulation) * scaled_step) * contact;
                        let (pair_low, pair_high) = if initial < cell_temperature {
                            (initial, cell_temperature)
                        } else {
                            (cell_temperature, initial)
                        };
                        low = smin(low, pair_low);
                        high = smax(high, pair_high);
                        let mut cell_next = clamp(
                            cell_temperature - transfer / cell_capacity,
                            pair_low,
                            pair_high,
                        );
                        let building_next = clamp(
                            initial + transfer / building.cell_heat_capacity,
                            pair_low,
                            pair_high,
                        );
                        if (building_next - cell_next) * (initial - cell_temperature) < 0.0 {
                            cell_next = clamp(
                                cell_capacity / (cell_capacity + building.cell_heat_capacity)
                                    * cell_temperature
                                    + building.cell_heat_capacity
                                        / (cell_capacity + building.cell_heat_capacity)
                                        * initial,
                                pair_low,
                                pair_high,
                            );
                            transfer = (cell_next - initial) * building.cell_heat_capacity;
                        }
                        if !(cell_next <= pair_high && pair_low <= cell_next) {
                            continue;
                        }
                        world.write.temperature[cell] = cell_next;
                        crate::transition::do_state_transition(world, elements, events, cell);
                        energy = transfer + energy;
                    }
                }
                // Use the native per-cell capacity * rectangle size order.
                let capacity =
                    building.cell_heat_capacity * ((max_y - min_y) * (max_x - min_x)) as f32;
                final_temperature = clamp(initial + energy / capacity, low, high)
                    + (dt * building.operating_kw) / capacity;
                building_events(
                    events,
                    handle,
                    initial,
                    final_temperature,
                    building.overheat_temperature,
                );
                if building.melt_temperature <= final_temperature {
                    events.building_melted_infos.push(MeltedInfo { handle });
                }
                self.buildings.data[dense].temperature = final_temperature;
            }
            self.building_infos[ComponentHandles::<Building>::slot(handle)] =
                BuildingTemperatureInfo {
                    handle,
                    temperature: final_temperature,
                };
        }
        Ok(())
    }

    /// BuildingToBuildingHeatExchange::Update, official ELF 0x5d6b0.
    pub(crate) fn update_contacts(
        &mut self,
        dt: f32,
        region: &Region,
        world: &PhysicsWorld,
        events: &mut Events,
    ) -> Result<()> {
        self.resize_infos()?;
        let step = f32::from_bits(0x3ba3_d70a) * dt;
        for group in &self.contacts.data {
            let Some(source_index) = self.buildings.index(group.building) else {
                continue;
            };
            let source = self.buildings.data[source_index];
            if !contains(region, source.extents) {
                continue;
            }
            let mut low = source.temperature;
            let mut high = source.temperature;
            for &(handle, _) in &group.contacts {
                if let Ok(other) = self.buildings.get(handle) {
                    low = smin(low, other.temperature);
                    high = smax(high, other.temperature);
                }
            }
            let inverse_capacity = 1.0 / source.heat_capacity;
            for &(handle, _) in &group.contacts {
                let Some(other_index) = self.buildings.index(handle) else {
                    continue;
                };
                let source = self.buildings.data[source_index];
                let other = self.buildings.data[other_index];
                if other.heat_capacity <= 0.0 {
                    continue;
                }
                let difference = other.temperature - source.temperature;
                let selected_capacity = if difference < 0.0 {
                    source.heat_capacity
                } else {
                    other.heat_capacity
                };
                // The native multiplies both conductivities before selecting
                // the hot-side capacity; cellsInContact is not used here.
                let conductivity = source.conductivity * other.conductivity;
                let raw = step
                    * world.building_contact_heat_transfer_scale
                    * conductivity
                    * (selected_capacity * difference);
                let source_candidate =
                    clamp(inverse_capacity * raw + source.temperature, low, high);
                let source_cap =
                    (source_candidate - source.temperature).abs() * source.heat_capacity;
                let other_candidate =
                    clamp(other.temperature - raw / other.heat_capacity, low, high);
                let other_cap = (other_candidate - other.temperature).abs() * other.heat_capacity;
                let magnitude = smin(other_cap, source_cap);
                let transfer = if raw < 0.0 { -magnitude } else { magnitude };
                let mut source_next = inverse_capacity * transfer + source.temperature;
                // Preserve the official crossing expression (ELF 0x5da1b).
                let other_next = if ((transfer / other.heat_capacity - other.temperature)
                    + source_next)
                    * (source.temperature - other.temperature)
                    < 0.0
                {
                    let equilibrium = (other.temperature * other.heat_capacity
                        + source.temperature * source.heat_capacity)
                        / (other.heat_capacity + source.heat_capacity);
                    source_next = clamp(equilibrium, low, high);
                    source_next
                } else {
                    other.temperature - transfer / other.heat_capacity
                };
                building_events(
                    events,
                    handle,
                    other.temperature,
                    other_next,
                    other.overheat_temperature,
                );
                self.buildings.data[other_index].temperature = other_next;
                self.building_infos[ComponentHandles::<Building>::slot(handle)] =
                    BuildingTemperatureInfo {
                        handle,
                        temperature: other_next,
                    };
                // A self-contact aliases the same record in the original too:
                // its second write takes precedence over the first write.
                self.buildings.data[source_index].temperature = source_next;
                self.building_infos[ComponentHandles::<Building>::slot(group.building)] =
                    BuildingTemperatureInfo {
                        handle: group.building,
                        temperature: source_next,
                    };
            }
        }
        Ok(())
    }
}

fn building_events(events: &mut Events, handle: i32, old: f32, next: f32, overheat: f32) {
    if overheat <= next {
        events.building_overheat_infos.push(MeltedInfo { handle });
    }
    if overheat <= old && next < overheat {
        events
            .building_no_longer_overheated_infos
            .push(MeltedInfo { handle });
    }
}
