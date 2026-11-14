use rayon::prelude::*;
use crate::grid::Grid;
use crate::radiation::RadiationParams;
use crate::status::{Error, Result, Status};
use crate::tables::ElementTable;
pub(crate) use crate::world_cells::Cells;
use crate::world_cells::filled;

#[cfg(test)]
#[path = "world_publication_tests.rs"]
mod publication_tests;

#[derive(Clone, Copy, Default)]
pub(crate) struct Region {
    pub(crate) min_x: i32,
    pub(crate) min_y: i32,
    pub(crate) max_x: i32,
    pub(crate) max_y: i32,
    pub(crate) sunlight: f32,
    pub(crate) cosmic_radiation: f32,
}

pub(crate) struct PhysicsWorld {
    pub(crate) width: usize,
    pub(crate) height: usize,
    pub(crate) stride: usize,
    pub(crate) padded_height: usize,
    // Official +0x20 is the old read snapshot; +0x28 is active write storage.
    pub(crate) read: Cells,
    pub(crate) write: Cells,
    pub(crate) backwall_element: Vec<u16>,
    pub(crate) backwall_mass: Vec<f32>,
    pub(crate) backwall_temperature: Vec<f32>,
    pub(crate) flow: Vec<[f32; 4]>,
    pub(crate) accumulated_flow: Vec<f32>,
    pub(crate) radiation_transmission: Vec<f32>,
    pub(crate) radiation_params: RadiationParams,
    pub(crate) inactive_mask: Option<Vec<u8>>,
    pub(crate) dirty: Vec<bool>,
    pub(crate) dirty_flags: Vec<u8>,
    pub(crate) rng: u32,
    pub(crate) scan_direction: i32,
    pub(crate) gas_scan_direction: i32,
    pub(crate) regions: Vec<Region>,
    pub(crate) world_offsets: Vec<[i32; 4]>,
    pub(crate) sink_element: u16,
    pub(crate) vacuum_element: u16,
    pub(crate) boundary_element: u16,
    pub(crate) tick_seed: u16,
    // Visibility is a public-grid image, unlike the padded CellSOA arrays.
    pub(crate) active_mask: Vec<u8>,
    pub(crate) simulate_all_active: bool,
    pub(crate) falling_disabled: bool,
    pub(crate) radiation_enabled: bool,
    pub(crate) saved_options: u8,
    pub(crate) legacy_thermal_boundaries: bool,
    pub(crate) building_heat_transfer_scale: f32,
    pub(crate) building_contact_heat_transfer_scale: f32,
    // Aggressive gas coexistence overlay: second species per padded cell.
    // Legacy Grid ABI still publishes the dominant element/mass only; the
    // multicomponent publication advertises all components.
    pub(crate) mixture_element: Vec<u16>,
    pub(crate) mixture_mass: Vec<f32>,
    pub(crate) extra_mixtures: crate::cell_mixtures::CellMixtures,
    // Dense NHWC gas channels (production promotion of the GPU operator layout).
    pub(crate) species_mass: Vec<f32>,
    pub(crate) gas_channel_of_element: Vec<u8>,
    pub(crate) gas_element_of_channel: [u16; crate::multicomponent_flux::GAS_CHANNELS],
    pub(crate) gas_channel_count: usize,
    pub(crate) element_phase: Vec<u8>,
}

fn special(elements: &ElementTable, hash: u32) -> Result<u16> {
    elements
        .elements
        .iter()
        .rposition(|element| element.record.id as u32 == hash)
        .and_then(|index| u16::try_from(index).ok())
        .ok_or_else(|| {
            Error::new(
                Status::InvalidState,
                format!("required element hash 0x{hash:08x} is missing"),
            )
        })
}

impl PhysicsWorld {
    pub(crate) fn new(
        width: usize,
        height: usize,
        seed: u32,
        elements: &ElementTable,
    ) -> Result<Self> {
        let public_width = i32::try_from(width).map_err(|_| {
            Error::new(
                Status::ResourceLimit,
                "public width exceeds signed ABI range",
            )
        })?;
        let public_height = i32::try_from(height).map_err(|_| {
            Error::new(
                Status::ResourceLimit,
                "public height exceeds signed ABI range",
            )
        })?;
        crate::bytes::dimensions(public_width, public_height)?;
        let stride = width
            .checked_add(2)
            .ok_or_else(|| Error::new(Status::ResourceLimit, "padded width overflow"))?;
        let padded_height = height
            .checked_add(2)
            .ok_or_else(|| Error::new(Status::ResourceLimit, "padded height overflow"))?;
        let count = stride
            .checked_mul(padded_height)
            .ok_or_else(|| Error::new(Status::ResourceLimit, "padded cell count overflow"))?;
        let sink_element = special(elements, 0xa9360b34)?;
        let vacuum_element = special(elements, 0x2d39bf75)?;
        let boundary_element = special(elements, 0x6d95058c)?;
        let mut world = Self {
            width,
            height,
            stride,
            padded_height,
            read: Cells::new(count)?,
            write: Cells::new(count)?,
            backwall_element: filled(count, vacuum_element)?,
            backwall_mass: filled(count, 0.0)?,
            backwall_temperature: filled(count, 0.0)?,
            flow: filled(count, [0.0; 4])?,
            accumulated_flow: filled(count, 0.0)?,
            radiation_transmission: filled(count, 1.0)?,
            radiation_params: Default::default(),
            inactive_mask: None,
            dirty: filled(count, true)?,
            dirty_flags: filled(count, 0x1f)?,
            rng: seed,
            scan_direction: -1,
            gas_scan_direction: -1,
            regions: Vec::new(),
            world_offsets: Vec::new(),
            sink_element,
            vacuum_element,
            boundary_element,
            tick_seed: 0,
            active_mask: filled(width * height, 0)?,
            simulate_all_active: false,
            falling_disabled: false,
            radiation_enabled: false,
            saved_options: 0,
            legacy_thermal_boundaries: false,
            building_heat_transfer_scale: f32::from_bits(0x3a83126f),
            building_contact_heat_transfer_scale: f32::from_bits(0x3a83126f),
            mixture_element: filled(count, vacuum_element)?,
            mixture_mass: filled(count, 0.0)?,
            extra_mixtures: crate::cell_mixtures::CellMixtures::new(count)?,
            species_mass: filled(
                count
                    .checked_mul(crate::multicomponent_flux::GAS_CHANNELS)
                    .ok_or_else(|| {
                        Error::new(Status::ResourceLimit, "species mass capacity overflow")
                    })?,
                0.0,
            )?,
            gas_channel_of_element: filled(elements.elements.len().max(1), u8::MAX)?,
            gas_element_of_channel: [0; crate::multicomponent_flux::GAS_CHANNELS],
            gas_channel_count: 0,
            element_phase: elements.elements.iter().map(|e| (e.record.state & 3) as u8).collect(),
        };
        crate::multicomponent_flux::rebuild_gas_channels(&mut world, elements);
        Ok(world)
    }

    pub(crate) fn from_grid(grid: &Grid, elements: &ElementTable) -> Result<Self> {
        let mut world = Self::new(
            grid.width as usize,
            grid.height as usize,
            grid.seed,
            elements,
        )?;
        world.falling_disabled = grid.headless != 0;
        world.radiation_enabled = grid.radiation_enabled != 0;
        for public in 0..grid.len() {
            let internal = world.internal_index(public);
            world.write.element[internal] = grid.element[public];
            world.write.temperature[internal] = grid.temperature[public];
            world.write.mass[internal] = grid.mass[public];
            world.write.properties[internal] = grid.properties[public];
            world.write.insulation[internal] = grid.insulation[public];
            world.write.strength[internal] = grid.strength[public];
            world.write.disease_idx[internal] = grid.disease_idx[public];
            world.write.disease_count[internal] = grid.disease_count[public];
            world.write.disease_infestation[internal] = grid.disease_infestation(public);
            // InitializeFromCells ELF 0x3f819..0x3f820 ignores wire +8 and
            // clears this runtime accumulator rather than restoring it.
            world.write.disease_growth[internal] = 0.0;
            world.write.radiation[internal] = grid.radiation[public];
            world.backwall_element[internal] = grid.backwall_element[public];
            world.backwall_mass[internal] = grid.backwall_mass[public];
            world.backwall_temperature[internal] = grid.backwall_temperature[public];
        }
        world.initialize_boundary();
        crate::multicomponent_flux::rebuild_gas_channels(&mut world, elements);
        for public in 0..grid.len() {
            let internal = world.internal_index(public);
            crate::multicomponent_flux::ingest_write_cell(&mut world, elements, internal);
        }
        Ok(world)
    }

    pub(crate) fn internal_index(&self, public: usize) -> usize {
        (public / self.width + 1) * self.stride + public % self.width + 1
    }

    pub(crate) fn public_index(&self, internal: usize) -> Option<usize> {
        let (x, y) = (internal % self.stride, internal / self.stride);
        if x == 0 || x > self.width || y == 0 || y > self.height {
            return None;
        }
        Some((y - 1) * self.width + x - 1)
    }

    pub(crate) fn next_random(&mut self) -> u16 {
        self.rng = self.rng.wrapping_mul(214013).wrapping_add(2531011);
        ((self.rng >> 16) & 0x7fff) as u16
    }

    pub(crate) fn mark_dirty(&mut self, index: usize, flags: u8) {
        self.dirty[index] = true;
        self.dirty_flags[index] |= flags;
    }

    pub(crate) fn sync_write_from_read(&mut self) {
        self.write.copy_from(&self.read);
    }
    pub(crate) fn sync_read_from_write(&mut self) {
        self.read.copy_from(&self.write);
    }

    pub(crate) fn for_each_cell_species(&self, cell: usize, mut f: impl FnMut(u16, f32)) {
        if self.write.mass[cell] > 0.0 {
            f(self.write.element[cell], self.write.mass[cell]);
        }
        if self.mixture_mass[cell] > 0.0 && self.mixture_element[cell] != self.write.element[cell] {
            f(self.mixture_element[cell], self.mixture_mass[cell]);
        }
        if !self.extra_mixtures.is_empty() {
            if let Some(extras) = self.extra_mixtures.get(&cell) {
                for &(element, mass) in extras {
                    if mass > 0.0 && element != self.write.element[cell] && element != self.mixture_element[cell] {
                        f(element, mass);
                    }
                }
            }
        }
    }

    /// Repair only an exhausted display slot. Species slots are inventories,
    /// independent of phase and without a fixed species-count limit.
    pub(crate) fn restore_empty_primary(&mut self, cell: usize) -> bool {
        if self.write.mass[cell] > 0.0 && self.write.element[cell] != self.vacuum_element {
            return false;
        }
        let mut best = (self.vacuum_element, 0.0);
        let mut extra_index = None;
        if self.mixture_mass[cell] > 0.0 {
            best = (self.mixture_element[cell], self.mixture_mass[cell]);
        }
        if let Some(extras) = self.extra_mixtures.get(&cell) {
            for (index, &(element, mass)) in extras.iter().enumerate() {
                if mass > best.1 || (mass > 0.0 && mass == best.1 && element < best.0) {
                    best = (element, mass);
                    extra_index = Some(index);
                }
            }
        }
        if best.1 <= 0.0 {
            return false;
        }
        if let Some(index) = extra_index {
            self.extra_mixtures.get_mut(&cell).unwrap().swap_remove(index);
        } else {
            self.mixture_element[cell] = self.vacuum_element;
            self.mixture_mass[cell] = 0.0;
        }
        self.write.element[cell] = best.0;
        self.write.mass[cell] = best.1;
        // Old Vacuum/0K snapshots have lost their original thermal state.
        // Prefer a valid read image, otherwise use an explicit ambient estimate.
        if !self.write.temperature[cell].is_finite() || self.write.temperature[cell] <= 0.0 {
            let previous = self.read.temperature[cell];
            self.write.temperature[cell] = if previous.is_finite() && previous > 0.0 {
                previous
            } else {
                293.15
            };
        }
        true
    }

    pub(crate) fn clear_cell_mixtures(&mut self, cell: usize) {
        self.mixture_mass[cell] = 0.0;
        self.mixture_element[cell] = self.vacuum_element;
        self.extra_mixtures.remove(&cell);
    }

    pub(crate) fn add_species_mass(&mut self, _tables: &ElementTable, cell: usize, element: u16, mass: f32) {
        if mass <= 0.0 || !mass.is_finite() {
            return;
        }
        if self.write.mass[cell] <= 0.0 || self.write.element[cell] == self.vacuum_element {
            self.write.element[cell] = element;
            self.write.mass[cell] = mass;
            return;
        }
        // Storage accepts every fluid species. Phase separation and miscibility
        // belong to the transport model, not inventory insertion.
        let current_phase = self.element_phase[self.write.element[cell] as usize];
        let incoming_phase = self.element_phase[element as usize];
        if current_phase & 3 == 3 || incoming_phase & 3 == 3 {
            return;
        }
        if self.write.element[cell] == element {
            self.write.mass[cell] += mass;
            return;
        }
        if self.mixture_mass[cell] <= 0.0 || self.mixture_element[cell] == self.vacuum_element {
            self.mixture_element[cell] = element;
            self.mixture_mass[cell] = mass;
            if (self.element_phase[self.mixture_element[cell] as usize] & 3 == 2
                && self.element_phase[self.write.element[cell] as usize] & 3 != 2)
                || (self.element_phase[self.mixture_element[cell] as usize] & 3
                    == self.element_phase[self.write.element[cell] as usize] & 3
                    && self.mixture_mass[cell] > self.write.mass[cell]) {
                let dom_elem = self.mixture_element[cell];
                let dom_mass = self.mixture_mass[cell];
                self.mixture_element[cell] = self.write.element[cell];
                self.mixture_mass[cell] = self.write.mass[cell];
                self.write.element[cell] = dom_elem;
                self.write.mass[cell] = dom_mass;
            }
            return;
        }
        if self.mixture_element[cell] == element {
            self.mixture_mass[cell] += mass;
            if (self.element_phase[self.mixture_element[cell] as usize] & 3 == 2
                && self.element_phase[self.write.element[cell] as usize] & 3 != 2)
                || (self.element_phase[self.mixture_element[cell] as usize] & 3
                    == self.element_phase[self.write.element[cell] as usize] & 3
                    && self.mixture_mass[cell] > self.write.mass[cell]) {
                let dom_elem = self.mixture_element[cell];
                let dom_mass = self.mixture_mass[cell];
                self.mixture_element[cell] = self.write.element[cell];
                self.mixture_mass[cell] = self.write.mass[cell];
                self.write.element[cell] = dom_elem;
                self.write.mass[cell] = dom_mass;
            }
            return;
        }
        let list = self.extra_mixtures.entry(cell).or_default();
        if let Some(pos) = list.iter().position(|(e, _)| *e == element) {
            list[pos].1 += mass;
        } else {
            list.push((element, mass));
        }
        self.normalize_cell_mixtures(cell);
    }

    pub(crate) fn normalize_cell_mixtures(&mut self, cell: usize) {
        // Pure and two-species cells need no scratch allocation or sorting.
        if !self.extra_mixtures.contains_key(&cell) {
            if self.write.mass[cell] <= 0.0 {
                self.restore_empty_primary(cell);
                return;
            }
            if self.mixture_mass[cell] <= 0.0 { return; }
            if self.write.element[cell] == self.mixture_element[cell] {
                self.write.mass[cell] += self.mixture_mass[cell];
                self.mixture_mass[cell] = 0.0;
                self.mixture_element[cell] = self.vacuum_element;
                return;
            }
            let primary_phase = self.element_phase[self.write.element[cell] as usize] & 3;
            let other_phase = self.element_phase[self.mixture_element[cell] as usize] & 3;
            if (other_phase == 2 && primary_phase != 2)
                || (other_phase == primary_phase && self.mixture_mass[cell] > self.write.mass[cell]) {
                std::mem::swap(&mut self.write.element[cell], &mut self.mixture_element[cell]);
                std::mem::swap(&mut self.write.mass[cell], &mut self.mixture_mass[cell]);
            }
            return;
        }
        let mut components = self.extra_mixtures.remove(&cell).unwrap_or_default();
        for (element, mass) in [
            (self.write.element[cell], self.write.mass[cell]),
            (self.mixture_element[cell], self.mixture_mass[cell]),
        ] {
            if mass > 0.0 {
                if let Some(entry) = components.iter_mut().find(|entry| entry.0 == element) {
                    entry.1 += mass;
                } else {
                    components.push((element, mass));
                }
            }
        }
        components.retain(|entry| entry.1 > 0.0);
        components.sort_unstable_by(|a, b| b.1.total_cmp(&a.1).then(a.0.cmp(&b.0)));
        self.replace_cell_species(cell, &components);
    }

    /// Project inventories for the legacy Grid view without dropping a phase
    /// or imposing a species-count limit. Liquid selects the rendered surface.
    pub(crate) fn replace_cell_species(&mut self, cell: usize, species: &[(u16, f32)]) {
        self.clear_cell_mixtures(cell);
        let display = species.iter().position(|&(id, mass)|
            mass > 0.0 && self.element_phase[id as usize] & 3 == 2)
            .or_else(|| species.iter().position(|entry| entry.1 > 0.0));
        let Some(display) = display else {
            self.write.element[cell] = self.vacuum_element;
            self.write.mass[cell] = 0.0;
            return;
        };
        self.write.element[cell] = species[display].0;
        self.write.mass[cell] = species[display].1;
        for (index, &(element, mass)) in species.iter().enumerate() {
            if index == display || mass <= 0.0 { continue; }
            if self.mixture_mass[cell] <= 0.0 {
                self.mixture_element[cell] = element;
                self.mixture_mass[cell] = mass;
            } else {
                self.extra_mixtures.entry(cell).or_default().push((element, mass));
            }
        }
    }

    pub(crate) fn thermal_boundary_element(elements: &ElementTable) -> Result<u16> {
        special(elements, 0x3fe0146e)
    }

    pub(crate) fn settle_thermal_boundaries(&mut self, thermal_boundary: u16) {
        // ELF 0x3d3c0, constants 0xa5334=1.0 and 0xa5568=2.0. The
        // original adds the eight neighbours in this exact row-major order.
        let stride = self.stride;
        let offsets = [
            -(stride as isize) - 1,
            -(stride as isize),
            1 - stride as isize,
            -1,
            1,
            stride as isize - 1,
            stride as isize,
            stride as isize + 1,
        ];
        for y in 1..=self.height {
            for x in 1..=self.width {
                let cell = y * stride + x;
                if self.read.element[cell] != thermal_boundary {
                    continue;
                }
                let mut temperature = 0.0_f32;
                let mut count = 0.0_f32;
                for (position, offset) in offsets.into_iter().enumerate() {
                    let neighbour = (cell as isize + offset) as usize;
                    let element = self.read.element[neighbour];
                    if element != thermal_boundary
                        && element != self.boundary_element
                        && self.read.mass[neighbour] > 0.0
                    {
                        if position == 0 {
                            temperature = self.read.temperature[neighbour];
                        } else {
                            temperature += self.read.temperature[neighbour];
                        }
                        count += 1.0;
                    }
                }
                if count > 0.0 {
                    self.write.temperature[cell] = temperature / count;
                }
            }
        }
    }

    pub(crate) fn initialize_boundary(&mut self) {
        // InitializeBoundary ELF 0x3d7a0: the top row uses +0x1c; the
        // bottom row and side interiors use +0x1e. Properties are retained.
        for y in 0..self.padded_height {
            for x in 0..self.stride {
                if x != 0 && x + 1 != self.stride && y != 0 && y + 1 != self.padded_height {
                    continue;
                }
                let index = y * self.stride + x;
                let element = if y + 1 == self.padded_height {
                    self.vacuum_element
                } else {
                    self.boundary_element
                };
                for cells in [&mut self.read, &mut self.write] {
                    cells.element[index] = element;
                    cells.mass[index] = 9999.0;
                    cells.temperature[index] = 0.0;
                    cells.disease_idx[index] = u8::MAX;
                    cells.disease_count[index] = 0;
                }
                self.backwall_element[index] = self.vacuum_element;
                self.backwall_mass[index] = 0.0;
                self.backwall_temperature[index] = 0.0;
            }
        }
    }

    pub(crate) fn publish_to(&self, grid: &mut Grid) {
        assert_eq!(
            (grid.width as usize, grid.height as usize),
            (self.width, self.height)
        );
        if let Some(pool) = crate::parallel::pool(self.width * self.height) {
            pool.scope(|scope| self.copy_public_fields(grid, Some(scope)));
        } else {
            self.copy_public_fields(grid, None);
        }
        grid.copy_padded_disease_runtime(&self.write.disease_infestation,
            &self.write.disease_growth, self.stride);
    }

    fn copy_public_fields<'scope>(&'scope self, grid: &'scope mut Grid,
        scope: Option<&rayon::Scope<'scope>>) {
        let public_count = self.width * self.height;
        let padded_count = self.stride * self.padded_height;
        // Each task owns an entire SoA destination stream. One scope schedules
        // all streams together, avoiding a worker wakeup per field.
        macro_rules! copy_rows {
            ($target:ident, $source:expr) => { copy_rows!($target, $source, true); };
            ($target:ident, $source:expr, $compare:expr) => {
                assert_eq!(grid.$target.len(), public_count);
                assert_eq!($source.len(), padded_count);
                let destination = &mut grid.$target;
                let source = &$source;
                let width = self.width;
                let stride = self.stride;
                let mut copy = move || {
                    for (destination, source) in destination.chunks_exact_mut(width)
                        .zip(source.chunks_exact(stride).skip(1)) {
                        let source = &source[1..1 + width];
                        let bytes = std::mem::size_of_val(source);
                        // These fields contain initialized POD primitives.
                        // Compare bytes to preserve NaN payloads and signed zero.
                        let same = $compare && unsafe {
                            std::slice::from_raw_parts(destination.as_ptr() as *const u8, bytes)
                                == std::slice::from_raw_parts(source.as_ptr() as *const u8, bytes)
                        };
                        if !same { destination.copy_from_slice(source); }
                    }
                };
                if let Some(scope) = scope { scope.spawn(move |_| copy()); }
                else { copy(); }
            };
        }
        copy_rows!(element, self.write.element);
        copy_rows!(temperature, self.write.temperature, false);
        copy_rows!(mass, self.write.mass, false);
        copy_rows!(properties, self.write.properties);
        copy_rows!(insulation, self.write.insulation);
        copy_rows!(strength, self.write.strength);
        copy_rows!(radiation, self.write.radiation);
        copy_rows!(accumulated_flow, self.accumulated_flow);
        copy_rows!(disease_idx, self.write.disease_idx);
        copy_rows!(disease_count, self.write.disease_count);
        copy_rows!(backwall_element, self.backwall_element);
        copy_rows!(backwall_mass, self.backwall_mass);
        copy_rows!(backwall_temperature, self.backwall_temperature);
    }

    pub(crate) fn publish_accumulated_flow_to(&self, grid: &mut Grid) {
        assert_eq!(
            (grid.width as usize, grid.height as usize),
            (self.width, self.height)
        );
        assert_eq!(grid.accumulated_flow.len(), self.width * self.height);
        assert_eq!(self.accumulated_flow.len(), self.stride * self.padded_height);
        for (destination, source) in grid
            .accumulated_flow
            .chunks_exact_mut(self.width)
            .zip(self.accumulated_flow.chunks_exact(self.stride).skip(1))
        {
            destination.copy_from_slice(&source[1..1 + self.width]);
        }
    }
}
