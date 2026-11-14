//! Conservative implicit pressure exchange inside a pure liquid.
//!
//! A cell at elevation y stores M*h below one cell of fill and
//! M*(1 + 0.01*(h-1)) above it, where h = hydraulic head - y.
//! Thus horizontal and vertical faces share one hydrostatic equilibrium.
//! Existing gas, vacuum, mixed-liquid and solid-interface handling remains in
//! fluid.rs. Covered internal faces must not also run the explicit flow rule.

use crate::events::EventQueues;
use crate::fluid_helpers::{add_disease, clear_primary, clear_disease, notify};
use crate::tables::{DiseaseTable, ElementTable};
use crate::world::{PhysicsWorld, Region};

#[cfg(test)]
#[path = "fluid_pressure_reference.rs"]
mod reference;

const NONE: usize = usize::MAX;
const COMPRESSION: f64 = 0.01;
const MAX_SWEEPS: usize = 64;

struct Node {
    cell: usize,
    mass: f64,
    temperature: f64,
    capacity: f64,
    viscosity: f64,
    fill: f64,
    limit: f64,
    elevation: f64,
    head: f64,
    neighbors: [usize; 4],
    outgoing: f64,
    removed_mass: f64,
    received_mass: f64,
    received_heat: f64,
    disease: u8,
    germs: i32,
    outgoing_germs: i32,
}

struct Face {
    a: usize,
    b: usize,
    direction: usize,
    flux: f64,
    germs: i32,
}

#[derive(Default)]
pub(crate) struct PressureScratch {
    index: Vec<usize>,
    nodes: Vec<Node>,
    faces: Vec<Face>,
    pub(crate) covered: Vec<u8>,
    pub(crate) profile_sweeps: usize,
    pub(crate) profile_nodes: usize,
    #[cfg(test)]
    pub(crate) last_sweeps: usize,
    #[cfg(test)]
    pub(crate) last_committed_residual: f64,
    #[cfg(test)]
    pub(crate) last_scaled_donors: usize,
    #[cfg(test)]
    pub(crate) last_min_scale: f64,
    #[cfg(test)]
    pub(crate) last_noop_transfers: usize,
    #[cfg(test)]
    pub(crate) reference_coordinates: bool,
}

fn head(mass: f64, capacity: f64, elevation: f64) -> f64 {
    let fill = mass / capacity;
    elevation
        + if fill < 1.0 {
            fill
        } else {
            1.0 + (fill - 1.0) / COMPRESSION
        }
}

fn storage(head: f64, capacity: f64, elevation: f64) -> (f64, f64) {
    let fill = head - elevation;
    if fill <= 0.0 {
        (0.0, 0.0)
    } else if fill < 1.0 {
        (capacity * fill, capacity)
    } else {
        (
            capacity * (1.0 + COMPRESSION * (fill - 1.0)),
            capacity * COMPRESSION,
        )
    }
}

impl PressureScratch {
    #[cfg_attr(not(test), allow(dead_code))]
    pub(crate) fn run(
        &mut self,
        world: &mut PhysicsWorld,
        elements: &ElementTable,
        diseases: &DiseaseTable,
        events: &mut EventQueues,
        region: Region,
    ) -> bool {
        self.run_with_candidates(world, elements, diseases, events, region, None)
    }

    pub(crate) fn run_with_candidates(
        &mut self,
        world: &mut PhysicsWorld,
        elements: &ElementTable,
        diseases: &DiseaseTable,
        events: &mut EventQueues,
        region: Region,
        candidate_cells: Option<&[usize]>,
    ) -> bool {
        // Reset only the last active set; resize initializes a new world once.
        for node in &self.nodes {
            self.index[node.cell] = NONE;
            self.covered[node.cell] = 0;
        }
        self.nodes.clear();
        self.faces.clear();
        #[cfg(test)]
        {
            self.last_sweeps = 0;
        }
        #[cfg(test)]
        {
            self.last_committed_residual = 0.0;
            self.last_scaled_donors = 0;
            self.last_min_scale = 1.0;
            self.last_noop_transfers = 0;
        }
        self.index.resize(world.write.mass.len(), NONE);
        self.covered.resize(world.write.mass.len(), 0);
        let min_x = region.min_x.max(1) as usize;
        let max_x = (region.max_x as usize).min(world.width + 1);
        let min_y = region.min_y.max(1) as usize;
        let max_y = (region.max_y as usize).min(world.height + 1);
        let mut push_node = |cell: usize, y: usize| {
            let element = world.read.element[cell];
            if world.element_phase[element as usize] != 2 {
                return;
            }
            let mass = world.read.mass[cell];
            if !(mass > 0.0)
                || !mass.is_finite()
                || world.write.element[cell] != element
                || world.write.mass[cell] != mass
                || (world.read.properties[cell] | world.write.properties[cell]) & 2 != 0
                || world.mixture_mass[cell] > 0.0
                || (!world.extra_mixtures.is_empty() && world.extra_mixtures.contains_key(&cell))
                || !world.read.temperature[cell].is_finite()
            {
                return;
            }
            let record = &elements.elements[element as usize].record;
            if !(record.max_mass > 0.0 && record.max_mass.is_finite())
                || !(record.viscosity > 0.0 && record.viscosity.is_finite())
            {
                return;
            }
            let capacity = f64::from(record.max_mass);
            let mass = f64::from(mass);
            self.index[cell] = self.nodes.len();
            self.nodes.push(Node {
                cell,
                mass,
                capacity,
                fill: mass / capacity,
                limit: f64::from(record.viscosity) / capacity,
                temperature: f64::from(world.read.temperature[cell]),
                viscosity: f64::from(record.viscosity),
                elevation: y as f64,
                head: head(mass, capacity, y as f64),
                neighbors: [NONE; 4],
                outgoing: 0.0,
                removed_mass: 0.0,
                received_mass: 0.0,
                received_heat: 0.0,
                disease: world.read.disease_idx[cell],
                germs: world.read.disease_count[cell],
                outgoing_germs: 0,
            });
        };
        if let Some(cells) = candidate_cells {
            for &cell in cells {
                let y = cell / world.stride;
                let x = cell - y * world.stride;
                if y >= min_y && y < max_y && x >= min_x && x < max_x {
                    push_node(cell, y);
                }
            }
        } else {
            for y in min_y..max_y {
                let row_base = y * world.stride;
                for x in min_x..max_x {
                    push_node(row_base + x, y);
                }
            }
        }
        for a in 0..self.nodes.len() {
            let cell = self.nodes[a].cell;
            for (other, direction, opposite) in [(cell + 1, 1, 0), (cell + world.stride, 3, 2)] {
                let b = self.index[other];
                if b == NONE || world.read.element[other] != world.read.element[cell] {
                    continue;
                }
                self.nodes[a].neighbors[direction] = b;
                self.nodes[b].neighbors[opposite] = a;
                self.covered[cell] |= 1 << direction;
                self.covered[other] |= 1 << opposite;
                self.faces.push(Face {
                    a,
                    b,
                    direction,
                    flux: 0.0,
                    germs: 0,
                });
            }
        }
        if self.faces.is_empty() {
            return false;
        }

        // Iterations only solve pressures; no material is moved until the
        // final face fluxes are known. Viscosity is a per-update face budget.
        self.profile_nodes = self.nodes.len();
        for sweep in 0..MAX_SWEEPS {
            self.profile_sweeps = sweep + 1;
            for offset in 0..self.nodes.len() {
                let i = if sweep & 1 == 0 {
                    offset
                } else {
                    self.nodes.len() - offset - 1
                };
                #[cfg(test)]
                if self.reference_coordinates {
                    self.nodes[i].head = self.solve_coordinate_reference(i);
                    continue;
                }
                let next = self.solve_coordinate(i);
                self.nodes[i].head = next;
            }
            #[cfg(test)]
            {
                self.last_sweeps = sweep + 1;
            }
            // Check the mass equation, not a head epsilon smaller than the
            // f32 storage precision. Small pressure changes alone do not bound
            // flux error in a nearly incompressible liquid.
            if (sweep == 0 || sweep & 3 == 3) && self.mass_residual_converged() {
                break;
            }
        }
        for face in &mut self.faces {
            let a = &self.nodes[face.a];
            let b = &self.nodes[face.b];
            let limit = a.viscosity.min(b.viscosity);
            face.flux = (a.capacity.min(b.capacity) * (a.head - b.head)).clamp(-limit, limit);
            let source = if face.flux > 0.0 { face.a } else { face.b };
            self.nodes[source].outgoing += face.flux.abs();
        }
        #[cfg(test)]
        {
            for node in &self.nodes {
                if node.outgoing > node.mass {
                    self.last_scaled_donors += 1;
                    self.last_min_scale = self.last_min_scale.min(node.mass / node.outgoing);
                }
            }
        }
        let mut needs_germ_remainder = false;
        for face in &mut self.faces {
            let (source, destination, direction) = if face.flux >= 0.0 {
                (face.a, face.b, face.direction)
            } else {
                (face.b, face.a, if face.direction == 1 { 0 } else { 2 })
            };
            // Explicit transport cannot spend newly arriving material in this
            // same substep. The implicit pressure solution may do so at a dry
            // neck even if converged. This donor cap is a conservative split
            // approximation; its committed state need not satisfy that solve's
            // EOS residual. Scale all outgoing faces together for symmetry.
            let donor = &self.nodes[source];
            let amount = face.flux.abs() * (donor.mass / donor.outgoing.max(donor.mass));
            face.flux = amount.copysign(face.flux);
            face.germs = if donor.germs > 0 {
                ((f64::from(donor.germs) * amount / donor.mass).floor() as i32)
                    .min(donor.germs - donor.outgoing_germs)
            } else {
                0
            };
            let heat = amount * donor.temperature;
            self.nodes[source].removed_mass += amount;
            self.nodes[source].outgoing_germs += face.germs;
            needs_germ_remainder |= self.nodes[source].germs > self.nodes[source].outgoing_germs
                && self.nodes[source].removed_mass >= self.nodes[source].mass;
            self.nodes[destination].received_mass += amount;
            self.nodes[destination].received_heat += heat;
            world.flow[self.nodes[source].cell][direction] += amount as f32;
        }
        if needs_germ_remainder {
            // Per-face integer floors can leave 1..3 germs behind when all
            // material drains through several faces. Carry that remainder on
            // an actual outgoing face before the empty source is cleared.
            for face in &mut self.faces {
                let source = if face.flux >= 0.0 { face.a } else { face.b };
                let donor = &mut self.nodes[source];
                if face.flux != 0.0
                    && donor.removed_mass >= donor.mass
                    && donor.germs > donor.outgoing_germs
                {
                    face.germs += donor.germs - donor.outgoing_germs;
                    donor.outgoing_germs = donor.germs;
                }
            }
        }
        for node in &self.nodes {
            if node.removed_mass == 0.0 && node.received_mass == 0.0 {
                continue;
            }
            let remaining = (node.mass - node.removed_mass).max(0.0);
            let mass = remaining + node.received_mass;
            world.write.mass[node.cell] = mass as f32;
            world.write.temperature[node.cell] = if mass > 0.0 {
                // Avoid catastrophic cancellation when an almost empty cell
                // drains: remaining material plus incoming heat is convex.
                ((remaining * node.temperature + node.received_heat) / mass) as f32
            } else {
                0.0
            };
            world.write.disease_count[node.cell] = node.germs - node.outgoing_germs;
            if world.write.disease_count[node.cell] <= 0 {
                clear_disease(&mut world.write, node.cell);
            }
            if world.write.mass[node.cell] == 0.0 {
                world.write.temperature[node.cell] = node.temperature as f32;
                clear_primary(world, node.cell);
            }
        }
        for face in &self.faces {
            if face.germs == 0 {
                continue;
            }
            let (source, destination) = if face.flux >= 0.0 {
                (face.a, face.b)
            } else {
                (face.b, face.a)
            };
            add_disease(
                &mut world.write,
                diseases,
                self.nodes[destination].cell,
                self.nodes[source].disease,
                face.germs,
            );
        }
        // Interface flow starts from the conservatively updated state. Only
        // covered faces are suppressed there; no extra whole-world copy.
        for node in &self.nodes {
            let cell = node.cell;
            #[cfg(test)]
            {
                self.last_committed_residual = self.last_committed_residual.max(
                    (f64::from(world.write.mass[cell])
                        - storage(node.head, node.capacity, node.elevation).0)
                        .abs(),
                );
            }
            let changed = world.read.element[cell] != world.write.element[cell]
                || world.read.mass[cell] != world.write.mass[cell]
                || world.read.temperature[cell] != world.write.temperature[cell]
                || world.read.disease_idx[cell] != world.write.disease_idx[cell]
                || world.read.disease_count[cell] != world.write.disease_count[cell]
                || world.read.disease_infestation[cell] != world.write.disease_infestation[cell]
                || world.read.disease_growth[cell] != world.write.disease_growth[cell];
            if changed {
                notify(world, events, cell);
            }
            #[cfg(test)]
            {
                if !changed && (node.removed_mass > 0.0 || node.received_mass > 0.0) {
                    self.last_noop_transfers += 1;
                }
            }
            world.read.element[cell] = world.write.element[cell];
            world.read.mass[cell] = world.write.mass[cell];
            world.read.temperature[cell] = world.write.temperature[cell];
            world.read.disease_idx[cell] = world.write.disease_idx[cell];
            world.read.disease_count[cell] = world.write.disease_count[cell];
            world.read.disease_infestation[cell] = world.write.disease_infestation[cell];
            world.read.disease_growth[cell] = world.write.disease_growth[cell];
        }
        true
    }

    fn solve_coordinate(&self, i: usize) -> f64 {
        let node = &self.nodes[i];
        // Every neighbor has the same element, hence the same capacity and
        // viscosity. Normalize by capacity and load the four heads once.
        let fill = node.fill;
        let limit = node.limit;
        let mut neighbors = [0.0; 4];
        let mut count = 0;
        for &j in &node.neighbors {
            if j != NONE {
                neighbors[count] = self.nodes[j].head;
                count += 1;
            }
        }
        if count == 0 {
            return node.head;
        }
        let neighbors = &neighbors[..count];
        let sum: f64 = neighbors.iter().sum();
        let degree = count as f64;
        let unsaturated = |value: f64| neighbors.iter().all(|&h| (value - h).abs() <= limit);
        let full =
            (fill - 1.0 + COMPRESSION * (node.elevation + 1.0) + sum) / (COMPRESSION + degree);
        if full >= node.elevation + 1.0 && unsaturated(full) {
            return full;
        }
        let partial = (fill + node.elevation + sum) / (1.0 + degree);
        if partial >= node.elevation && partial <= node.elevation + 1.0 && unsaturated(partial) {
            return partial;
        }
        let empty = (fill + sum) / degree;
        if empty <= node.elevation && unsaturated(empty) {
            return empty;
        }

        let budget = degree * limit;
        let mut low = head((fill - budget).max(0.0), 1.0, node.elevation);
        if fill <= budget {
            // A draining cell can have zero stored mass with a head below its
            // floor. Bracketing at the floor alone misses that valid root.
            for &neighbor in neighbors {
                low = low.min(neighbor - limit);
            }
        }
        let mut high = head(fill + budget, 1.0, node.elevation);
        let mut value = node.head.clamp(low, high);
        for _ in 0..12 {
            let (mass, mut slope) = storage(value, 1.0, node.elevation);
            let mut residual = mass - fill;
            for &neighbor in neighbors {
                let flux = value - neighbor;
                residual += flux.clamp(-limit, limit);
                if flux.abs() < limit {
                    slope += 1.0;
                }
            }
            if residual.abs() < 1.0e-10 {
                break;
            }
            if residual > 0.0 {
                high = value;
            } else {
                low = value;
            }
            let newton = value - residual / slope;
            value = if newton > low && newton < high {
                newton
            } else {
                (low + high) * 0.5
            };
        }
        value
    }

    fn mass_residual_converged(&self) -> bool {
        self.nodes.iter().all(|node| {
            let mut residual = storage(node.head, node.capacity, node.elevation).0 - node.mass;
            for &j in &node.neighbors {
                if j == NONE {
                    continue;
                }
                let other = &self.nodes[j];
                let limit = node.viscosity.min(other.viscosity);
                residual += (node.capacity.min(other.capacity) * (node.head - other.head))
                    .clamp(-limit, limit);
            }
            let mass = node.mass as f32;
            let tolerance = (2.0 * f64::from(mass.next_up() - mass)).max(1.0e-7);
            residual.abs() <= tolerance
        })
    }
}
