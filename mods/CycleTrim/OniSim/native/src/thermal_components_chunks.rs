use crate::component_handles::ComponentHandles;
use crate::events::{Events, MeltedInfo};
use crate::numeric::smin;
use crate::status::Result;
use crate::tables::ElementTable;
use crate::thermal_components::{ElementChunk, ThermalComponents};
use crate::world::{PhysicsWorld, Region};

fn dmin(first: f64, second: f64) -> f64 {
    if first < second {
        first
    } else {
        second
    }
}
fn dmax(first: f64, second: f64) -> f64 {
    if first > second {
        first
    } else {
        second
    }
}

// Unnamed official heat-exchange helper at ELF 0x61520. It updates the
// active cell temperature, then the chunk, and returns transferred kJ using
// the native branch-specific rounding. No state-transition call is made.
fn exchange(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    chunk: &mut ElementChunk,
    cell: usize,
    dt: f32,
    scale: f32,
) -> f32 {
    let mass = world.write.mass[cell];
    if mass <= 0.0 {
        return 0.0;
    }
    let old_chunk = chunk.temperature;
    let old_cell = world.write.temperature[cell];
    if old_chunk < 0.0 || 10_000.0 < old_chunk || old_cell < 0.0 || 10_000.0 < old_cell {
        return 0.0;
    }
    let element = &elements.elements[world.write.element[cell] as usize].record;
    if element.state & 0x10 != 0 {
        return 0.0;
    }
    let cell_capacity = (mass * element.specific_heat_capacity) as f64;
    let chunk_capacity = chunk.heat_capacity as f64;
    let cell_temperature = old_cell as f64;
    let chunk_temperature = old_chunk as f64;
    let insulation = world.write.insulation[cell] as f32;
    let cell_conductivity =
        (f32::from_bits(0x3781_0183) * element.thermal_conductivity) * (insulation * insulation);
    let conductivity = smin(cell_conductivity, chunk.conductivity) * chunk.surface_over_thickness;
    let scaled = ((scale * f32::from_bits(0x3a83_126f)) * conductivity) as f64;
    let equilibrium = (cell_capacity * cell_temperature + chunk_capacity * chunk_temperature)
        / (chunk_capacity + cell_capacity);
    let (next_chunk, next_cell, transfer) = if old_cell > old_chunk {
        let delta = cell_temperature - chunk_temperature;
        let step = dt as f64 * delta;
        let cap_chunk = delta * chunk_capacity;
        let conductance_cap = step * scaled;
        let energy = dmin(delta * cell_capacity, dmin(cap_chunk, conductance_cap));
        let next_chunk = dmin(chunk_temperature + energy / chunk_capacity, equilibrium) as f32;
        let next_cell = dmax(cell_temperature - energy / cell_capacity, equilibrium) as f32;
        let loss = ((chunk_temperature - next_chunk as f64) * chunk_capacity) as f32;
        let transfer = f32::from_bits(loss.to_bits() ^ 0x8000_0000);
        (next_chunk, next_cell, transfer)
    } else {
        let delta = chunk_temperature - cell_temperature;
        let step = dt as f64 * delta;
        let conductance_cap = step * scaled;
        let cap_cell = delta * cell_capacity;
        let energy = dmin(delta * chunk_capacity, dmin(conductance_cap, cap_cell));
        let next_cell = dmin(cell_temperature + energy / cell_capacity, equilibrium) as f32;
        let next_chunk = dmax(chunk_temperature - energy / chunk_capacity, equilibrium) as f32;
        let transfer = ((cell_temperature - next_cell as f64) * cell_capacity) as f32;
        (next_chunk, next_cell, transfer)
    };
    world.write.temperature[cell] = next_cell;
    chunk.temperature = next_chunk;
    transfer
}

// ElementChunk::Update's explicit temperature-adjuster path does not change
// the adjuster's temperature/capacity or report its energy as deltaKJ.
fn adjust(chunk: &mut ElementChunk, dt: f32) {
    let capacity = chunk.heat_capacity as f64;
    let adjust_capacity = chunk.adjust_heat_capacity as f64;
    let temperature = chunk.temperature as f64;
    let adjust_temperature = chunk.adjust_temperature as f64;
    let equilibrium = (capacity * temperature + adjust_capacity * adjust_temperature)
        / (capacity + adjust_capacity);
    let coefficient = chunk.adjust_conductivity as f64 * dt as f64;
    let next = if chunk.adjust_temperature <= chunk.temperature {
        let delta = temperature - adjust_temperature;
        let energy = dmin(
            dmin(coefficient * delta, adjust_capacity * delta),
            delta * capacity,
        );
        dmax(temperature - energy / capacity, equilibrium)
    } else {
        let delta = adjust_temperature - temperature;
        let energy = dmin(
            delta * adjust_capacity,
            dmin(delta * capacity, coefficient * delta),
        );
        dmin(temperature + energy / capacity, equilibrium)
    };
    chunk.temperature = next as f32;
}

impl ThermalComponents {
    /// ElementChunk::Update, official ELF 0x61c60. Region boundaries here
    /// are inclusive, unlike the building rectangle's exclusive max corner.
    pub(crate) fn update_chunks(
        &mut self,
        dt: f32,
        region: &Region,
        world: &mut PhysicsWorld,
        elements: &ElementTable,
        events: &mut Events,
    ) -> Result<()> {
        self.resize_infos()?;
        for dense in 0..self.chunks.data.len() {
            let handle = self.chunks.handle_at(dense);
            let chunk = &mut self.chunks.data[dense];
            let x = (chunk.cell % world.stride) as i32;
            let y = (chunk.cell / world.stride) as i32;
            let mut transferred = 0.0;
            if region.min_x <= x
                && region.min_y <= y
                && x <= region.max_x
                && y <= region.max_y
                && 0.0 < chunk.heat_capacity
            {
                if chunk.adjust_heat_capacity <= 0.0 {
                    transferred = exchange(world, elements, chunk, chunk.cell, dt, 1.0);
                    let below = chunk.cell - world.stride;
                    let element = &elements.elements[world.write.element[below] as usize].record;
                    if element.state & 3 == 3 {
                        transferred +=
                            exchange(world, elements, chunk, below, dt, chunk.ground_scale);
                    }
                } else {
                    adjust(chunk, dt);
                }
            }
            let info = &mut self.chunk_infos[ComponentHandles::<ElementChunk>::slot(handle)];
            info.temperature = chunk.temperature;
            info.delta_kj = transferred + info.delta_kj;
            if chunk.high_temperature + 3.0 <= chunk.temperature
                || chunk.temperature < chunk.low_temperature - 3.0
            {
                events
                    .element_chunk_melted_infos
                    .push(MeltedInfo { handle });
            }
        }
        Ok(())
    }
}
