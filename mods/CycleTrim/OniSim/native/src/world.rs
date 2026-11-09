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
    pub(crate) extra_mixtures: std::collections::HashMap<usize, Vec<(u16, f32)>>,
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
            extra_mixtures: std::collections::HashMap::new(),
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
        if let Some(extras) = self.extra_mixtures.get(&cell) {
            for &(element, mass) in extras {
                if mass > 0.0 && element != self.write.element[cell] && element != self.mixture_element[cell] {
                    f(element, mass);
                }
            }
        }
    }

    pub(crate) fn clear_cell_mixtures(&mut self, cell: usize) {
        self.mixture_mass[cell] = 0.0;
        self.mixture_element[cell] = self.vacuum_element;
        self.extra_mixtures.remove(&cell);
    }

    pub(crate) fn add_species_mass(&mut self, tables: &ElementTable, cell: usize, element: u16, mass: f32) {
        if mass <= 0.0 || !mass.is_finite() {
            return;
        }
        if self.write.mass[cell] <= 0.0 || self.write.element[cell] == self.vacuum_element {
            self.write.element[cell] = element;
            self.write.mass[cell] = mass;
            return;
        }
        let cur_elem = self.write.element[cell];
        let p_cur = self.element_phase.get(cur_elem as usize).copied().unwrap_or(0);
        let p_new = self.element_phase.get(element as usize).copied().unwrap_or(0);
        // 严格物态隔离：气体仅能与气体共存，液体仅能与液体共存；绝不允许气液在单格混合
        if p_cur != p_new || (p_cur != 1 && p_cur != 2) {
            return;
        }
        if p_cur == 2 {
            let r_cur = &tables.elements[cur_elem as usize].record;
            let r_new = &tables.elements[element as usize].record;
            if !crate::fluid_helpers::are_liquids_miscible(r_cur, r_new) {
                return;
            }
        }
        if self.write.element[cell] == element {
            self.write.mass[cell] += mass;
            return;
        }
        if self.mixture_mass[cell] <= 0.0 || self.mixture_element[cell] == self.vacuum_element {
            self.mixture_element[cell] = element;
            self.mixture_mass[cell] = mass;
            if self.mixture_mass[cell] > self.write.mass[cell] {
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
            if self.mixture_mass[cell] > self.write.mass[cell] {
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
        let mut components = [(0u16, 0.0f32); 16];
        let mut count = 0;
        let cur_elem = self.write.element[cell];
        let cur_phase = self.element_phase.get(cur_elem as usize).copied().unwrap_or(0);
        if self.write.mass[cell] > 0.0 {
            components[count] = (cur_elem, self.write.mass[cell]);
            count += 1;
        }
        if self.mixture_mass[cell] > 0.0 && self.mixture_element[cell] != cur_elem {
            let m_phase = self.element_phase.get(self.mixture_element[cell] as usize).copied().unwrap_or(0);
            if m_phase == cur_phase {
                components[count] = (self.mixture_element[cell], self.mixture_mass[cell]);
                count += 1;
            }
        }
        if let Some(extras) = self.extra_mixtures.get(&cell) {
            for &(elem, mass) in extras {
                let e_phase = self.element_phase.get(elem as usize).copied().unwrap_or(0);
                if mass > 0.0 && e_phase == cur_phase && count < 16 && !components[..count].iter().any(|(e, _)| *e == elem) {
                    components[count] = (elem, mass);
                    count += 1;
                }
            }
        }
        components[..count].sort_unstable_by(|a, b| b.1.partial_cmp(&a.1).unwrap_or(std::cmp::Ordering::Equal));
        if count == 0 {
            self.mixture_mass[cell] = 0.0;
            self.mixture_element[cell] = self.vacuum_element;
            self.extra_mixtures.remove(&cell);
        } else if count == 1 {
            self.write.element[cell] = components[0].0;
            self.write.mass[cell] = components[0].1;
            self.mixture_mass[cell] = 0.0;
            self.mixture_element[cell] = self.vacuum_element;
            self.extra_mixtures.remove(&cell);
        } else if count == 2 {
            self.write.element[cell] = components[0].0;
            self.write.mass[cell] = components[0].1;
            self.mixture_element[cell] = components[1].0;
            self.mixture_mass[cell] = components[1].1;
            self.extra_mixtures.remove(&cell);
        } else {
            self.write.element[cell] = components[0].0;
            self.write.mass[cell] = components[0].1;
            self.mixture_element[cell] = components[1].0;
            self.mixture_mass[cell] = components[1].1;
            let extras = self.extra_mixtures.entry(cell).or_default();
            extras.clear();
            for &(e, m) in &components[2..count] {
                extras.push((e, m));
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
        let public_count = self.width * self.height;
        let padded_count = self.stride * self.padded_height;
        // GameData owns unpublished, stable destination arrays. Copy one SoA
        // stream at a time, skipping padded side cells without per-cell /% or
        // simultaneously interleaving thirteen independent destination streams.
        macro_rules! copy_rows {
            ($target:ident, $source:expr) => {
                assert_eq!(grid.$target.len(), public_count);
                assert_eq!($source.len(), padded_count);
                for (destination, source) in grid
                    .$target
                    .chunks_exact_mut(self.width)
                    .zip($source.chunks_exact(self.stride).skip(1))
                {
                    destination.copy_from_slice(&source[1..1 + self.width]);
                }
            };
        }
        copy_rows!(element, self.write.element);
        copy_rows!(temperature, self.write.temperature);
        copy_rows!(mass, self.write.mass);
        copy_rows!(properties, self.write.properties);
        copy_rows!(insulation, self.write.insulation);
        copy_rows!(strength, self.write.strength);
        copy_rows!(radiation, self.write.radiation);
        self.publish_accumulated_flow_to(grid);
        copy_rows!(disease_idx, self.write.disease_idx);
        copy_rows!(disease_count, self.write.disease_count);
        copy_rows!(backwall_element, self.backwall_element);
        copy_rows!(backwall_mass, self.backwall_mass);
        copy_rows!(backwall_temperature, self.backwall_temperature);

        // Only infestation and growth are published into the packed disease
        // record. Its other reserved bytes, and both padding arrays, stay intact.
        for y in 0..self.height {
            let source = (y + 1) * self.stride + 1;
            let destination = y * self.width;
            for x in 0..self.width {
                grid.set_disease_runtime(
                    destination + x,
                    self.write.disease_infestation[source + x],
                    self.write.disease_growth[source + x],
                );
            }
        }
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
