//! Volume-based free-surface transport with simultaneous gas counterflow.
//! Every liquid inventory shares the same face flux fraction. The legacy Grid
//! element is only an output representation and never selects the donor fluid.
use crate::events::EventQueues;
use crate::fluid_helpers::{add_disease, liquid_density, notify, phase};
use crate::tables::{DiseaseTable, ElementTable};
use crate::world::{PhysicsWorld, Region};

const FLOW_EPSILON: f32 = 1.0e-7;

#[derive(Default)]
pub(crate) struct LiquidTransport {
    a: Vec<(u16, f32)>,
    b: Vec<(u16, f32)>,
    outgoing_a: Vec<(u16, f32)>,
    outgoing_b: Vec<(u16, f32)>,
    surface: Vec<f32>,
}

fn volume(species: &[(u16, f32)], elements: &ElementTable) -> f32 {
    species
        .iter()
        .filter(|entry| phase(elements, entry.0) == 2)
        .map(|&(id, mass)| mass / elements.elements[id as usize].record.max_mass.max(1.0e-6))
        .sum()
}

fn cell_volume(world: &PhysicsWorld, elements: &ElementTable, cell: usize) -> f32 {
    let mut result = 0.0;
    world.for_each_cell_species(cell, |id, mass| {
        if phase(elements, id) == 2 {
            result += mass / elements.elements[id as usize].record.max_mass.max(1.0e-6);
        }
    });
    result
}

fn capacity(species: &[(u16, f32)], elements: &ElementTable) -> f32 {
    species
        .iter()
        .map(|&(id, mass)| {
            mass * elements.elements[id as usize]
                .record
                .specific_heat_capacity
                .max(1.0e-6)
        })
        .sum()
}

fn blocked(world: &PhysicsWorld, elements: &ElementTable, cell: usize) -> bool {
    world.write.properties[cell] & 2 != 0 || phase(elements, world.write.element[cell]) == 3
}

/// Move a phase fraction using the inventories captured before either direction
/// is applied. Return transferred mass and heat capacity for thermal accounting.
fn take_phase(
    from: &mut [(u16, f32)],
    outgoing: &mut Vec<(u16, f32)>,
    elements: &ElementTable,
    state: u8,
    fraction: f32,
) -> (f32, f32) {
    let mut mass = 0.0;
    let mut heat = 0.0;
    for (id, available) in from {
        if phase(elements, *id) != state {
            continue;
        }
        let moved = *available * fraction;
        *available -= moved;
        if moved <= 0.0 {
            continue;
        }
        outgoing.push((*id, moved));
        mass += moved;
        heat += moved
            * elements.elements[*id as usize]
                .record
                .specific_heat_capacity
                .max(1.0e-6);
    }
    (mass, heat)
}

fn merge(to: &mut Vec<(u16, f32)>, incoming: &[(u16, f32)]) {
    for &(id, mass) in incoming {
        if let Some(entry) = to.iter_mut().find(|entry| entry.0 == id) {
            entry.1 += mass;
        } else {
            to.push((id, mass));
        }
    }
}

fn density(species: &[(u16, f32)], elements: &ElementTable) -> f32 {
    let mut mass = 0.0;
    let mut weight = 0.0;
    for &(id, amount) in species {
        if phase(elements, id) == 2 {
            mass += amount;
            weight += amount * liquid_density(&elements.elements[id as usize].record);
        }
    }
    weight / mass.max(f32::MIN_POSITIVE)
}

impl LiquidTransport {
    fn face(
        &mut self,
        world: &mut PhysicsWorld,
        elements: &ElementTable,
        diseases: &DiseaseTable,
        events: &mut EventQueues,
        source: usize,
        target: usize,
        requested_volume: f32,
        direction: usize,
    ) -> bool {
        if requested_volume <= FLOW_EPSILON || blocked(world, elements, target) {
            return false;
        }
        self.a.clear();
        self.b.clear();
        world.for_each_cell_species(source, |id, mass| self.a.push((id, mass)));
        world.for_each_cell_species(target, |id, mass| self.b.push((id, mass)));
        let source_volume = volume(&self.a, elements);
        let target_volume = volume(&self.b, elements);
        let sink = world.write.element[target] == world.sink_element;
        let density_exchange = direction == 2
            && target_volume >= 1.0 - FLOW_EPSILON
            && density(&self.a, elements) > density(&self.b, elements) + 1.0e-3;
        let continuous = self.a.iter().all(|entry| phase(elements, entry.0) == 2)
            && self.b.iter().all(|entry| phase(elements, entry.0) == 2)
            && source_volume > 0.0
            && target_volume > 0.0;
        let space = if direction < 2 && continuous {
            f32::INFINITY
        } else if density_exchange {
            target_volume
        } else if direction == 2 && continuous && target_volume >= 1.0 {
            // The same bounded compression law applies to the whole liquid
            // volume, including mixed columns and region-boundary faces.
            ((1.01 * source_volume).max(1.0) - target_volume).max(0.0) * 0.5
        } else {
            (1.0 - target_volume).max(0.0)
        };
        let mut moved_volume = requested_volume.min(source_volume).min(space);
        // Viscosity is a face-volume budget. All species keep the same fraction,
        // including mixtures with more than sixteen constituent liquids.
        for &(id, mass) in &self.a {
            if phase(elements, id) == 2 && mass > 0.0 {
                let viscosity = elements.elements[id as usize].record.viscosity;
                moved_volume = moved_volume.min(source_volume * viscosity.max(0.0) / mass);
            }
        }
        if moved_volume <= FLOW_EPSILON {
            return false;
        }
        let source_temperature = world.write.temperature[source];
        let target_temperature = world.write.temperature[target];
        let source_mass: f32 = self.a.iter().map(|entry| entry.1).sum();
        let target_mass: f32 = self.b.iter().map(|entry| entry.1).sum();
        let back_phase = if density_exchange { 2 } else { 1 };
        let back_volume = if density_exchange {
            target_volume
        } else {
            1.0 - target_volume
        };
        let back_fraction = (moved_volume / back_volume.max(FLOW_EPSILON)).min(1.0);
        self.outgoing_a.clear();
        self.outgoing_b.clear();
        let (liquid_mass, liquid_heat) = take_phase(
            &mut self.a,
            &mut self.outgoing_a,
            elements,
            2,
            (moved_volume / source_volume).min(1.0),
        );
        let (gas_mass, gas_heat) = take_phase(
            &mut self.b,
            &mut self.outgoing_b,
            elements,
            back_phase,
            back_fraction,
        );
        let retained_source_capacity = capacity(&self.a, elements);
        let retained_target_capacity = capacity(&self.b, elements);
        merge(&mut self.a, &self.outgoing_b);
        if !sink {
            merge(&mut self.b, &self.outgoing_a);
        }
        world.replace_cell_species(source, &self.a);
        if !sink {
            world.replace_cell_species(target, &self.b);
        }
        let final_source_capacity = retained_source_capacity + gas_heat;
        let final_target_capacity = retained_target_capacity + liquid_heat;
        world.write.temperature[source] = if final_source_capacity > 0.0 {
            (retained_source_capacity * source_temperature + gas_heat * target_temperature)
                / final_source_capacity
        } else {
            0.0
        };
        world.write.temperature[target] = if final_target_capacity > 0.0 {
            (retained_target_capacity * target_temperature + liquid_heat * source_temperature)
                / final_target_capacity
        } else {
            0.0
        };
        let source_germs = world.write.disease_count[source];
        let target_germs = world.write.disease_count[target];
        let source_disease = world.write.disease_idx[source];
        let target_disease = world.write.disease_idx[target];
        let liquid_germs =
            (source_germs as f32 * (liquid_mass / source_mass.max(f32::MIN_POSITIVE))) as i32;
        let gas_germs =
            (target_germs as f32 * (gas_mass / target_mass.max(f32::MIN_POSITIVE))) as i32;
        world.write.disease_count[source] -= liquid_germs;
        world.write.disease_count[target] -= gas_germs;
        add_disease(
            &mut world.write,
            diseases,
            source,
            target_disease,
            gas_germs,
        );
        if !sink {
            add_disease(
                &mut world.write,
                diseases,
                target,
                source_disease,
                liquid_germs,
            );
        }
        world.flow[source][direction] += liquid_mass;
        world.flow[target][direction ^ 1] -= liquid_mass;
        notify(world, events, source);
        if !sink {
            notify(world, events, target);
        }
        true
    }

    pub(crate) fn run(
        &mut self,
        world: &mut PhysicsWorld,
        elements: &ElementTable,
        diseases: &DiseaseTable,
        events: &mut EventQueues,
        region: Region,
        covered: &[u8],
    ) -> bool {
        let min_x = region.min_x.max(1) as usize;
        let max_x = (region.max_x as usize).min(world.width + 1);
        let min_y = region.min_y.max(1) as usize;
        let max_y = (region.max_y as usize).min(world.height + 1);
        let mut changed = false;
        // Bottom-up traversal moves a continuous column together and uses only
        // current inventories. No cell is later restored from a stale read image.
        for y in min_y..max_y {
            for x in min_x..max_x {
                let cell = y * world.stride + x;
                if blocked(world, elements, cell) {
                    continue;
                }
                let down = cell - world.stride;
                // A lower cell may have drained earlier in this traversal.
                // Its old pressure-face mask cannot block the following column.
                if covered[cell] & 4 != 0
                    && cell_volume(world, elements, down) >= 1.0 - FLOW_EPSILON
                {
                    continue;
                }
                let available = cell_volume(world, elements, cell);
                changed |= self.face(world, elements, diseases, events, cell, down, available, 2);
            }
        }
        self.surface.resize(world.write.mass.len(), 0.0);
        for y in min_y - 1..max_y {
            for x in min_x..=max_x {
                let cell = y * world.stride + x;
                self.surface[cell] = cell_volume(world, elements, cell);
            }
        }
        // Free-falling liquid keeps its downward path. Supported surfaces
        // equalize horizontally, visiting each face once in alternating order.
        for y in min_y..max_y {
            for offset in 0..max_x.saturating_sub(min_x) {
                let x = if world.scan_direction < 0 {
                    max_x - 1 - offset
                } else {
                    min_x + offset
                };
                let left = y * world.stride + x;
                let right = left + 1;
                if blocked(world, elements, left)
                    || blocked(world, elements, right)
                    || covered[left] & 2 != 0
                {
                    continue;
                }
                let left_volume = self.surface[left];
                let right_volume = self.surface[right];
                let (source, target, difference, direction) = if left_volume > right_volume {
                    (left, right, left_volume - right_volume, 1)
                } else {
                    (right, left, right_volume - left_volume, 0)
                };
                if difference <= FLOW_EPSILON {
                    continue;
                }
                let down = source - world.stride;
                if !blocked(world, elements, down) && self.surface[down] < 1.0 - FLOW_EPSILON {
                    continue;
                }
                changed |= self.face(
                    world,
                    elements,
                    diseases,
                    events,
                    source,
                    target,
                    difference * 0.25,
                    direction,
                );
            }
        }
        // Compressed reservoirs can raise a connected column through an open
        // upper face. Internal pure-liquid faces are owned by the head solve.
        for y in min_y..max_y {
            for x in min_x..max_x {
                let cell = y * world.stride + x;
                if blocked(world, elements, cell) || covered[cell] & 8 != 0 {
                    continue;
                }
                let excess = (cell_volume(world, elements, cell) - 1.0).max(0.0);
                changed |= self.face(
                    world,
                    elements,
                    diseases,
                    events,
                    cell,
                    cell + world.stride,
                    excess * 0.5,
                    3,
                );
            }
        }
        changed
    }
}

#[cfg(test)]
#[path = "liquid_transport_tests.rs"]
mod tests;
