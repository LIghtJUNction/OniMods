use crate::grid::Grid;
use crate::radiation::RadiationParams;
use crate::status::{Error, Result, Status};
use crate::tables::ElementTable;
use crate::world_cells::filled;
pub(crate) use crate::world_cells::Cells;

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
        Ok(Self {
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
        })
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
        for public in 0..grid.len() {
            let internal = self.internal_index(public);
            grid.element[public] = self.write.element[internal];
            grid.temperature[public] = self.write.temperature[internal];
            grid.mass[public] = self.write.mass[internal];
            grid.properties[public] = self.write.properties[internal];
            grid.insulation[public] = self.write.insulation[internal];
            grid.strength[public] = self.write.strength[internal];
            grid.radiation[public] = self.write.radiation[internal];
            grid.accumulated_flow[public] = self.accumulated_flow[internal];
            grid.disease_idx[public] = self.write.disease_idx[internal];
            grid.disease_count[public] = self.write.disease_count[internal];
            grid.set_disease_runtime(
                public,
                self.write.disease_infestation[internal],
                self.write.disease_growth[internal],
            );
            grid.backwall_element[public] = self.backwall_element[internal];
            grid.backwall_mass[public] = self.backwall_mass[internal];
            grid.backwall_temperature[public] = self.backwall_temperature[internal];
        }
    }
}
