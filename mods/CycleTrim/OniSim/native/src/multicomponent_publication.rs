//! Versioned multicomponent publication image (`OMC1`) and `GameData` borrowed view (`OniSimMulticomponentUpdate`).
//!
//! ABI v1 publishes the completed simulation batch through two complementary surfaces:
//! 1. `ONISIM_GetMulticomponentUpdate`: zero-copy borrowed pointer view owned by the
//!    active `GameData` double-buffer, synchronized with `Prepare` and protected against
//!    overlapping message payloads.
//! 2. `ONISIM_CopyMulticomponentImage`: caller-owned `OMC1` byte snapshot under the
//!    native session lock.

use crate::grid::Grid;
use crate::status::{Error, Result, Status};
use crate::tables::ElementTable;
use crate::world_cells::filled;

pub(crate) const MAGIC: u32 = u32::from_le_bytes(*b"OMC1");
pub(crate) const VERSION: u16 = 1;
pub(crate) const UPDATE_VERSION: u32 = 1;
pub(crate) const HEADER_SIZE: usize = 32;
pub(crate) const CELL_SIZE: usize = 12;
pub(crate) const SPECIES_SIZE: usize = 12;
pub(crate) const FLAG_LEGACY_SINGLE_ELEMENT: u32 = 1;
pub(crate) const FLAG_MULTICOMPONENT_COEXISTENCE: u32 = 2;
pub(crate) const CELL_FLAG_HAS_SPECIES: u16 = 1;

#[repr(C)]
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct OniSimMulticomponentSpeciesRecord {
    pub element_id: u16,
    pub phase: u8,
    pub reserved: u8,
    pub mass: f32,
    pub temperature: f32,
    pub volume_fraction: f32,
    pub partial_pressure_kpa: f32,
}

#[repr(C)]
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct OniSimMulticomponentCellHeader {
    pub species_offset: u32,
    pub species_count: u16,
    pub reserved: u16,
    pub total_mass: f32,
    pub gas_pressure_kpa: f32,
    pub liquid_pressure_kpa: f32,
    pub liquid_volume_fraction: f32,
}

#[repr(C)]
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct OniSimMulticomponentUpdate {
    pub version: u32,
    pub flags: u32,
    pub generation: u64,
    pub cell_count: u32,
    pub total_species_records: u32,
    pub cells: *const OniSimMulticomponentCellHeader,
    pub species: *const OniSimMulticomponentSpeciesRecord,
}

// Raw pointers in OniSimMulticomponentUpdate reference fixed GameData buffers owned by the session.
unsafe impl Send for OniSimMulticomponentUpdate {}
unsafe impl Sync for OniSimMulticomponentUpdate {}

const _: [(); 20] = [(); std::mem::size_of::<OniSimMulticomponentSpeciesRecord>()];
const _: [(); 24] = [(); std::mem::size_of::<OniSimMulticomponentCellHeader>()];
const _: [(); 40] = [(); std::mem::size_of::<OniSimMulticomponentUpdate>()];

pub(crate) struct MulticomponentBuffers {
    cells: Box<[OniSimMulticomponentCellHeader]>,
    species: Box<[OniSimMulticomponentSpeciesRecord]>,
    species_len: usize,
    flags: u32,
    validation_error: Option<Error>,
}

const MAX_SPECIES_PER_CELL: usize = 8;

impl MulticomponentBuffers {
    pub(crate) fn new(count: usize) -> Result<Self> {
        let species_cap = count
            .checked_mul(MAX_SPECIES_PER_CELL)
            .ok_or_else(|| Error::new(Status::ResourceLimit, "multicomponent species capacity overflow"))?;
        Ok(Self {
            cells: filled(count, OniSimMulticomponentCellHeader::default())?.into_boxed_slice(),
            species: filled(species_cap, OniSimMulticomponentSpeciesRecord::default())?
                .into_boxed_slice(),
            species_len: 0,
            // Aggressive: advertise coexistence so managed shims leave the
            // fail-closed LEGACY gate. Cells may still hold one species until
            // the mixture overlay fills a second record.
            flags: FLAG_MULTICOMPONENT_COEXISTENCE,
            validation_error: None,
        })
    }

    pub(crate) fn update_from_grid(&mut self, grid: &Grid, elements: &ElementTable) {
        self.update_from_grid_with_mixture(grid, elements, None, None);
    }

    pub(crate) fn update_from_grid_with_mixture(
        &mut self,
        grid: &Grid,
        elements: &ElementTable,
        mixture_element: Option<&[u16]>,
        mixture_mass: Option<&[f32]>,
    ) {
        self.update_from_grid_with_mixture_at(grid, elements, |cell| {
            (
                mixture_element.and_then(|m| m.get(cell).copied()).unwrap_or(0),
                mixture_mass.and_then(|m| m.get(cell).copied()).unwrap_or(0.0),
            )
        });
    }

    pub(crate) fn update_from_world(
        &mut self,
        grid: &Grid,
        elements: &ElementTable,
        world: &crate::world::PhysicsWorld,
    ) {
        self.species_len = 0;
        self.flags = FLAG_MULTICOMPONENT_COEXISTENCE;
        self.validation_error = None;

        let species_cap = self.cells.len().saturating_mul(MAX_SPECIES_PER_CELL);
        if self.cells.len() != grid.len() || self.species.len() < species_cap {
            self.validation_error = Some(Error::new(
                Status::InvalidState,
                "multicomponent buffers do not match grid cell count",
            ));
            return;
        }

        let mut next_species = 0usize;
        for cell in 0..grid.len() {
            let internal = world.internal_index(cell);
            self.cells[cell] = OniSimMulticomponentCellHeader {
                species_offset: next_species as u32,
                ..Default::default()
            };
            let mass = grid.mass[cell];
            let temperature = grid.temperature[cell];
            if !mass.is_finite() || mass < 0.0 || !temperature.is_finite() {
                self.validation_error = Some(Error::new(
                    Status::InvalidState,
                    format!("cell {cell} contains invalid mass or temperature"),
                ));
                return;
            }

            let species_offset = next_species;
            let mut species_count = 0u16;
            let mut total_mass = 0.0f32;

            world.for_each_cell_species(internal, |elem_id, elem_mass| {
                if elem_mass <= 0.0 || !elem_mass.is_finite() {
                    return;
                }
                let element_index = elem_id as usize;
                let Some(element) = elements.elements.get(element_index) else {
                    return;
                };
                let phase = element.record.state & 3;
                if phase == 0 || phase > 3 {
                    return;
                }
                if next_species >= self.species.len() {
                    return;
                }
                self.species[next_species] = OniSimMulticomponentSpeciesRecord {
                    element_id: elem_id,
                    phase,
                    reserved: 0,
                    mass: elem_mass,
                    temperature,
                    volume_fraction: 0.0,
                    partial_pressure_kpa: 0.0,
                };
                next_species += 1;
                species_count += 1;
                total_mass += elem_mass;
            });

            self.cells[cell] = OniSimMulticomponentCellHeader {
                species_offset: species_offset as u32,
                species_count,
                reserved: if species_count == 0 {
                    0
                } else {
                    CELL_FLAG_HAS_SPECIES
                },
                total_mass,
                gas_pressure_kpa: 0.0,
                liquid_pressure_kpa: 0.0,
                liquid_volume_fraction: 0.0,
            };
        }
        self.species_len = next_species;
    }

    pub(crate) fn update_from_grid_with_mixture_at(
        &mut self,
        grid: &Grid,
        elements: &ElementTable,
        mixture_at: impl Fn(usize) -> (u16, f32),
    ) {
        self.species_len = 0;
        self.flags = FLAG_MULTICOMPONENT_COEXISTENCE;
        self.validation_error = None;

        let species_cap = self.cells.len().saturating_mul(MAX_SPECIES_PER_CELL);
        if self.cells.len() != grid.len() || self.species.len() < species_cap {
            self.validation_error = Some(Error::new(
                Status::InvalidState,
                "multicomponent buffers do not match grid cell count",
            ));
            return;
        }

        let mut next_species = 0usize;
        for cell in 0..grid.len() {
            // Always replace the previous publication, even when a boundary
            // element has no publishable phase in this batch.
            self.cells[cell] = OniSimMulticomponentCellHeader {
                species_offset: next_species as u32,
                ..Default::default()
            };
            let mass = grid.mass[cell];
            let temperature = grid.temperature[cell];
            if !mass.is_finite() || mass < 0.0 || !temperature.is_finite() {
                self.validation_error = Some(Error::new(
                    Status::InvalidState,
                    format!("cell {cell} contains invalid mass or temperature"),
                ));
                return;
            }

            let (mix_element, mix_mass) = mixture_at(cell);
            if !mix_mass.is_finite() || mix_mass < 0.0 {
                self.validation_error = Some(Error::new(
                    Status::InvalidState,
                    format!("cell {cell} contains invalid mixture mass"),
                ));
                return;
            }

            if mass == 0.0 && mix_mass == 0.0 {
                continue;
            }

            let species_offset = next_species;
            let mut species_count = 0u16;
            let mut total_mass = 0.0f32;

            if mass > 0.0 {
                let element_index = grid.element[cell] as usize;
                let Some(element) = elements.elements.get(element_index) else {
                    self.validation_error = Some(Error::new(
                        Status::InvalidState,
                        format!("cell {cell} references missing element index {element_index}"),
                    ));
                    return;
                };
                let phase = element.record.state & 3;
                if phase == 0 || phase > 3 {
                    next_species = species_offset;
                    continue;
                }
                if next_species >= self.species.len() {
                    self.validation_error = Some(Error::new(
                        Status::ResourceLimit,
                        "multicomponent species table exhausted",
                    ));
                    return;
                }
                self.species[next_species] = OniSimMulticomponentSpeciesRecord {
                    element_id: grid.element[cell],
                    phase,
                    reserved: 0,
                    mass,
                    temperature,
                    volume_fraction: 0.0,
                    partial_pressure_kpa: 0.0,
                };
                next_species += 1;
                species_count += 1;
                total_mass += mass;
            }

            if mix_mass > 0.0 {
                let element_index = mix_element as usize;
                let Some(element) = elements.elements.get(element_index) else {
                    self.validation_error = Some(Error::new(
                        Status::InvalidState,
                        format!("cell {cell} mixture references missing element index {element_index}"),
                    ));
                    return;
                };
                let phase = element.record.state & 3;
                if phase == 0 || phase > 3 {
                    next_species = species_offset;
                    continue;
                }
                if next_species >= self.species.len() {
                    self.validation_error = Some(Error::new(
                        Status::ResourceLimit,
                        "multicomponent species table exhausted",
                    ));
                    return;
                }
                self.species[next_species] = OniSimMulticomponentSpeciesRecord {
                    element_id: mix_element,
                    phase,
                    reserved: 0,
                    mass: mix_mass,
                    temperature,
                    volume_fraction: 0.0,
                    partial_pressure_kpa: 0.0,
                };
                next_species += 1;
                species_count += 1;
                total_mass += mix_mass;
            }

            self.cells[cell] = OniSimMulticomponentCellHeader {
                species_offset: species_offset as u32,
                species_count,
                reserved: if species_count == 0 {
                    0
                } else {
                    CELL_FLAG_HAS_SPECIES
                },
                total_mass,
                gas_pressure_kpa: 0.0,
                liquid_pressure_kpa: 0.0,
                liquid_volume_fraction: 0.0,
            };
        }
        self.species_len = next_species;
    }

    pub(crate) fn descriptor(&self, generation: u64) -> Result<OniSimMulticomponentUpdate> {
        if let Some(error) = &self.validation_error {
            return Err(error.clone());
        }
        Ok(OniSimMulticomponentUpdate {
            version: UPDATE_VERSION,
            flags: self.flags,
            generation,
            cell_count: self.cells.len() as u32,
            total_species_records: self.species_len as u32,
            cells: self.cells.as_ptr(),
            species: self.species.as_ptr(),
        })
    }

    pub(crate) fn storage_ranges(&self) -> [(usize, usize); 2] {
        [
            (
                self.cells.as_ptr() as usize,
                std::mem::size_of_val(&*self.cells),
            ),
            (
                self.species.as_ptr() as usize,
                std::mem::size_of_val(&*self.species),
            ),
        ]
    }
}

pub(crate) fn image_len(grid: &Grid) -> Result<usize> {
    let cell_count = grid.len();
    if cell_count > u32::MAX as usize {
        return Err(Error::new(
            Status::ResourceLimit,
            "multicomponent cell count exceeds the OMC1 field width",
        ));
    }

    let mut species_count = 0usize;
    for &mass in grid.mass.iter() {
        if !mass.is_finite() || mass < 0.0 {
            return Err(Error::new(
                Status::InvalidState,
                "published grid contains negative or non-finite mass",
            ));
        }
        if mass > 0.0 {
            species_count = species_count.checked_add(1).ok_or_else(|| {
                Error::new(
                    Status::ResourceLimit,
                    "multicomponent species count overflow",
                )
            })?;
        }
    }
    if species_count > u32::MAX as usize {
        return Err(Error::new(
            Status::ResourceLimit,
            "multicomponent species count exceeds the OMC1 field width",
        ));
    }

    let cells = cell_count.checked_mul(CELL_SIZE).ok_or_else(|| {
        Error::new(
            Status::ResourceLimit,
            "multicomponent cell table size overflow",
        )
    })?;
    let species = species_count.checked_mul(SPECIES_SIZE).ok_or_else(|| {
        Error::new(
            Status::ResourceLimit,
            "multicomponent species table size overflow",
        )
    })?;
    HEADER_SIZE
        .checked_add(cells)
        .and_then(|n| n.checked_add(species))
        .ok_or_else(|| Error::new(Status::ResourceLimit, "multicomponent image size overflow"))
}

/// Validate every source cell before touching the caller-owned output buffer.
///
/// This makes failed snapshots atomic from the caller's perspective: an invalid
/// late cell cannot leave an apparently usable but partially overwritten image.
fn validate_cells(grid: &Grid, elements: &ElementTable) -> Result<usize> {
    let mut species_count = 0usize;
    for cell in 0..grid.len() {
        let mass = grid.mass[cell];
        let temperature = grid.temperature[cell];
        if !mass.is_finite() || mass < 0.0 || !temperature.is_finite() {
            return Err(Error::new(
                Status::InvalidState,
                format!("cell {cell} contains invalid mass or temperature"),
            ));
        }
        if mass == 0.0 {
            continue;
        }

        let element_index = grid.element[cell] as usize;
        let element = elements.elements.get(element_index).ok_or_else(|| {
            Error::new(
                Status::InvalidState,
                format!("cell {cell} references missing element index {element_index}"),
            )
        })?;
        let phase = element.record.state & 3;
        if !(1..=3).contains(&phase) {
            return Err(Error::new(
                Status::InvalidState,
                format!("cell {cell} has positive mass but no supported material phase"),
            ));
        }
        species_count += 1;
    }
    Ok(species_count)
}

pub(crate) fn write_image(
    grid: &Grid,
    elements: &ElementTable,
    lifecycle_generation: u64,
    output: &mut [u8],
) -> Result<()> {
    let required = image_len(grid)?;
    if output.len() != required {
        return Err(Error::new(
            Status::InvalidArgument,
            "multicomponent image buffer has incorrect length",
        ));
    }

    // Complete all fallible source validation before writing any output bytes.
    let species_count = validate_cells(grid, elements)?;
    let cell_table_end = HEADER_SIZE + grid.len() * CELL_SIZE;

    output.fill(0);
    put_u32(output, 0, MAGIC);
    put_u16(output, 4, VERSION);
    put_u16(output, 6, HEADER_SIZE as u16);
    put_u64(output, 8, lifecycle_generation);
    put_u32(output, 16, grid.len() as u32);
    put_u32(output, 20, species_count as u32);
    put_u16(output, 24, CELL_SIZE as u16);
    put_u16(output, 26, SPECIES_SIZE as u16);
    put_u32(output, 28, FLAG_MULTICOMPONENT_COEXISTENCE);

    let mut next_species = 0usize;
    for cell in 0..grid.len() {
        let at = HEADER_SIZE + cell * CELL_SIZE;
        let mass = grid.mass[cell];
        put_f32(output, at, mass);
        if mass == 0.0 {
            continue;
        }

        let species_at = cell_table_end + next_species * SPECIES_SIZE;
        put_u32(output, at + 4, next_species as u32);
        put_u16(output, at + 8, 1);
        put_u16(output, at + 10, CELL_FLAG_HAS_SPECIES);
        put_u16(output, species_at, grid.element[cell]);
        output[species_at + 2] = (elements.elements[grid.element[cell] as usize].record.state & 3) as u8;
        output[species_at + 3] = 0; // no partial-pressure/EOS value in ABI v1
        put_f32(output, species_at + 4, mass);
        put_f32(output, species_at + 8, grid.temperature[cell]);
        next_species += 1;
    }

    debug_assert_eq!(next_species, species_count);
    Ok(())
}

fn put_u16(bytes: &mut [u8], at: usize, value: u16) {
    bytes[at..at + 2].copy_from_slice(&value.to_le_bytes());
}
fn put_u32(bytes: &mut [u8], at: usize, value: u32) {
    bytes[at..at + 4].copy_from_slice(&value.to_le_bytes());
}
fn put_u64(bytes: &mut [u8], at: usize, value: u64) {
    bytes[at..at + 8].copy_from_slice(&value.to_le_bytes());
}
fn put_f32(bytes: &mut [u8], at: usize, value: f32) {
    put_u32(bytes, at, value.to_bits());
}

#[cfg(test)]
mod tests {
    use super::*;

    fn elements() -> ElementTable {
        let mut bytes = 3_i32.to_le_bytes().to_vec();
        for (index, (hash, phase)) in [
            (0x2d39bf75_u32, 0_u8),
            (0x11111111, 1_u8),
            (0x22222222, 2_u8),
        ]
        .into_iter()
        .enumerate()
        {
            let mut record = [0_u8; 164];
            record[..4].copy_from_slice(&hash.to_le_bytes());
            record[8..10].copy_from_slice(&(index as u16).to_le_bytes());
            record[10] = phase;
            record[52..56].copy_from_slice(&1000.0_f32.to_le_bytes());
            bytes.extend_from_slice(&record);
        }
        bytes.extend_from_slice(&[0_u8; 12]);
        ElementTable::parse(&bytes).unwrap()
    }

    fn grid() -> Grid {
        let mut grid = Grid::new(2, 1, 0, 0).unwrap();
        grid.element[0] = 1;
        grid.mass[0] = 1.25;
        grid.temperature[0] = 300.0;
        grid.element[1] = 2;
        grid.mass[1] = 250.0;
        grid.temperature[1] = 450.0;
        grid
    }

    #[test]
    fn padded_mixture_publication_matches_compact_grid_across_rows() {
        let elements = elements();
        let mut grid = Grid::new(2, 2, 0, 0).unwrap();
        grid.element.copy_from_slice(&[1, 2, 1, 2]);
        grid.mass.copy_from_slice(&[1.0, 2.0, 0.0, 4.0]);
        grid.temperature.fill(300.0);
        let mix_elements = [2, 1, 2, 1];
        let mix_masses = [0.5, 0.0, 3.0, 0.25];
        let padded_elements = [0, 0, 0, 0, 0, 2, 1, 0, 0, 2, 1, 0, 0, 0, 0, 0];
        let padded_masses = [0.0, 0.0, 0.0, 0.0, 0.0, 0.5, 0.0, 0.0, 0.0, 3.0, 0.25, 0.0, 0.0, 0.0, 0.0, 0.0];
        let mut compact = MulticomponentBuffers::new(grid.len()).unwrap();
        let mut padded = MulticomponentBuffers::new(grid.len()).unwrap();
        compact.update_from_grid_with_mixture(&grid, &elements, Some(&mix_elements), Some(&mix_masses));
        padded.update_from_grid_with_mixture_at(&grid, &elements, |cell| {
            let index = (cell / 2 + 1) * 4 + cell % 2 + 1;
            (padded_elements[index], padded_masses[index])
        });
        assert!(padded.descriptor(1).is_ok());
        assert_eq!(padded.cells, compact.cells);
        assert_eq!(padded.species_len, compact.species_len);
        assert_eq!(&padded.species[..padded.species_len], &compact.species[..compact.species_len]);
    }

    #[test]
    fn invalid_phase_replaces_old_cell_and_discards_partial_records() {
        let mut grid = grid();
        let elements = elements();
        let mut buffers = MulticomponentBuffers::new(grid.len()).unwrap();
        buffers.update_from_grid(&grid, &elements);
        grid.element[0] = 0;
        buffers.update_from_grid(&grid, &elements);
        assert_eq!(buffers.cells[0].species_count, 0);
        assert_eq!(buffers.cells[0].total_mass, 0.0);
        assert_eq!(buffers.cells[1].species_offset, 0);
        assert_eq!(buffers.species_len, 1);

        grid.element[0] = 1;
        buffers.update_from_grid_with_mixture(&grid, &elements, Some(&[0, 0]), Some(&[1.0, 0.0]));
        assert_eq!(buffers.cells[0].species_count, 0);
        assert_eq!(buffers.cells[1].species_offset, 0);
        assert_eq!(buffers.species_len, 1);
    }

    #[test]
    fn publishes_checked_sparse_coexistence_projection() {
        let grid = grid();
        let elements = elements();
        let mut image = vec![0; image_len(&grid).unwrap()];
        write_image(&grid, &elements, 42, &mut image).unwrap();
        assert_eq!(u32::from_le_bytes(image[0..4].try_into().unwrap()), MAGIC);
        assert_eq!(u16::from_le_bytes(image[4..6].try_into().unwrap()), VERSION);
        assert_eq!(u64::from_le_bytes(image[8..16].try_into().unwrap()), 42);
        assert_eq!(u32::from_le_bytes(image[16..20].try_into().unwrap()), 2);
        assert_eq!(u32::from_le_bytes(image[20..24].try_into().unwrap()), 2);
        assert_eq!(
            u32::from_le_bytes(image[28..32].try_into().unwrap()),
            FLAG_MULTICOMPONENT_COEXISTENCE
        );
        let first_cell = HEADER_SIZE;
        let second_cell = HEADER_SIZE + CELL_SIZE;
        let species_start = HEADER_SIZE + 2 * CELL_SIZE;
        assert_eq!(
            f32::from_bits(u32::from_le_bytes(
                image[first_cell..first_cell + 4].try_into().unwrap()
            )),
            1.25
        );
        assert_eq!(
            u32::from_le_bytes(image[first_cell + 4..first_cell + 8].try_into().unwrap()),
            0
        );
        assert_eq!(
            u16::from_le_bytes(image[first_cell + 8..first_cell + 10].try_into().unwrap()),
            1
        );
        assert_eq!(image[species_start + 2], 1);
        assert_eq!(
            u16::from_le_bytes(image[species_start..species_start + 2].try_into().unwrap()),
            1
        );
        assert_eq!(
            u16::from_le_bytes(
                image[species_start + SPECIES_SIZE..species_start + SPECIES_SIZE + 2]
                    .try_into()
                    .unwrap()
            ),
            2
        );
        assert_eq!(image[species_start + SPECIES_SIZE + 2], 2);
        assert_eq!(
            u32::from_le_bytes(image[second_cell + 4..second_cell + 8].try_into().unwrap()),
            1
        );
    }

    #[test]
    fn multicomponent_buffers_publish_valid_borrowed_update() {
        let grid = grid();
        let elements = elements();
        let mut buffers = MulticomponentBuffers::new(grid.len()).unwrap();
        buffers.update_from_grid(&grid, &elements);
        let desc = buffers.descriptor(77).unwrap();
        assert_eq!(desc.version, UPDATE_VERSION);
        assert_eq!(desc.flags, FLAG_MULTICOMPONENT_COEXISTENCE);
        assert_eq!(desc.generation, 77);
        assert_eq!(desc.cell_count, 2);
        assert_eq!(desc.total_species_records, 2);
        let cells = unsafe { std::slice::from_raw_parts(desc.cells, desc.cell_count as usize) };
        let species =
            unsafe { std::slice::from_raw_parts(desc.species, desc.total_species_records as usize) };
        assert_eq!(cells[0].species_offset, 0);
        assert_eq!(cells[0].species_count, 1);
        assert_eq!(cells[0].total_mass, 1.25);
        assert_eq!(species[0].element_id, 1);
        assert_eq!(species[0].phase, 1);
        assert_eq!(species[0].volume_fraction, 0.0);
        assert_eq!(species[0].partial_pressure_kpa, 0.0);
        assert_eq!(cells[0].gas_pressure_kpa, 0.0);
        assert_eq!(cells[1].species_offset, 1);
        assert_eq!(cells[1].species_count, 1);
        assert_eq!(cells[1].liquid_volume_fraction, 0.0);
        assert_eq!(cells[1].liquid_pressure_kpa, 0.0);
        assert_eq!(species[1].element_id, 2);
        assert_eq!(species[1].phase, 2);
        assert_eq!(species[1].volume_fraction, 0.0);
        assert_eq!(species[1].partial_pressure_kpa, 0.0);
    }

    #[test]
    fn rejects_missing_element_and_phase_zero_with_positive_mass() {
        let mut grid = grid();
        let elements = elements();
        let mut image = vec![0; image_len(&grid).unwrap()];
        grid.element[1] = 99;
        assert_eq!(
            write_image(&grid, &elements, 1, &mut image)
                .unwrap_err()
                .status as i32,
            Status::InvalidState as i32
        );

        let mut buffers = MulticomponentBuffers::new(grid.len()).unwrap();
        buffers.update_from_grid(&grid, &elements);
        assert_eq!(
            buffers.descriptor(1).unwrap_err().status as i32,
            Status::InvalidState as i32
        );

        grid.element[1] = 0;
        assert_eq!(
            write_image(&grid, &elements, 1, &mut image)
                .unwrap_err()
                .status as i32,
            Status::InvalidState as i32
        );
    }

    #[test]
    fn rejects_invalid_late_cell_without_modifying_output() {
        let mut grid = grid();
        let elements = elements();
        let mut image = vec![0xa5; image_len(&grid).unwrap()];
        grid.element[1] = 99;

        assert_eq!(
            write_image(&grid, &elements, 7, &mut image).unwrap_err().status as i32,
            Status::InvalidState as i32
        );
        assert!(image.iter().all(|&byte| byte == 0xa5));
    }

    #[test]
    fn rejects_non_finite_and_negative_mass() {
        let mut grid = grid();
        let elements = elements();
        let mut image = vec![0; image_len(&grid).unwrap()];
        grid.mass[0] = f32::NAN;
        assert_eq!(
            image_len(&grid).unwrap_err().status as i32,
            Status::InvalidState as i32
        );
        grid.mass[0] = -1.0;
        assert_eq!(
            image_len(&grid).unwrap_err().status as i32,
            Status::InvalidState as i32
        );
        grid.mass[0] = 1.0;
        grid.temperature[0] = f32::INFINITY;
        assert_eq!(
            write_image(&grid, &elements, 1, &mut image)
                .unwrap_err()
                .status as i32,
            Status::InvalidState as i32
        );
    }

    #[test]
    fn rejects_wrong_output_size() {
        let grid = grid();
        let elements = elements();
        let mut image = vec![0; image_len(&grid).unwrap() - 1];
        assert_eq!(
            write_image(&grid, &elements, 1, &mut image)
                .unwrap_err()
                .status as i32,
            Status::InvalidArgument as i32
        );
    }
}
