//! Versioned, caller-owned multicomponent publication image.
//!
//! ABI v1 deliberately publishes the currently available legacy cell state as
//! at most one species per cell. The image is marked LEGACY_SINGLE_ELEMENT;
//! consumers must not interpret it as proof that the production solver supports
//! coexisting species. A later producer can fill the same sparse cell/species
//! layout from a real multicomponent state without changing the record layout.

use crate::grid::Grid;
use crate::status::{Error, Result, Status};
use crate::tables::ElementTable;

pub(crate) const MAGIC: u32 = u32::from_le_bytes(*b"OMC1");
pub(crate) const VERSION: u16 = 1;
pub(crate) const HEADER_SIZE: usize = 32;
pub(crate) const CELL_SIZE: usize = 12;
pub(crate) const SPECIES_SIZE: usize = 12;
pub(crate) const FLAG_LEGACY_SINGLE_ELEMENT: u32 = 1;
pub(crate) const CELL_FLAG_HAS_SPECIES: u16 = 1;

pub(crate) fn image_len(grid: &Grid) -> Result<usize> {
    let species_count = grid.mass.iter().filter(|&&mass| mass > 0.0).count();
    let cells = grid.len().checked_mul(CELL_SIZE).ok_or_else(|| {
        Error::new(Status::ResourceLimit, "multicomponent cell table size overflow")
    })?;
    let species = species_count.checked_mul(SPECIES_SIZE).ok_or_else(|| {
        Error::new(Status::ResourceLimit, "multicomponent species table size overflow")
    })?;
    HEADER_SIZE.checked_add(cells).and_then(|n| n.checked_add(species)).ok_or_else(|| {
        Error::new(Status::ResourceLimit, "multicomponent image size overflow")
    })
}

pub(crate) fn write_image(
    grid: &Grid,
    elements: &ElementTable,
    lifecycle_generation: u64,
    output: &mut [u8],
) -> Result<()> {
    let required = image_len(grid)?;
    if output.len() != required {
        return Err(Error::new(Status::InvalidArgument, "multicomponent image buffer has incorrect length"));
    }

    let species_count = grid.mass.iter().filter(|&&mass| mass > 0.0).count();
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
    put_u32(output, 28, FLAG_LEGACY_SINGLE_ELEMENT);

    let mut next_species = 0usize;
    for cell in 0..grid.len() {
        let at = HEADER_SIZE + cell * CELL_SIZE;
        let mass = grid.mass[cell];
        put_f32(output, at, mass);
        if mass <= 0.0 {
            continue;
        }

        let element_index = grid.element[cell] as usize;
        let element = elements.elements.get(element_index).ok_or_else(|| {
            Error::new(Status::InvalidState, format!("cell {cell} references missing element index {element_index}"))
        })?;
        let phase = (element.record.state & 3) as u8;
        if phase == 0 {
            return Err(Error::new(Status::InvalidState, format!("cell {cell} has positive mass but no material phase")));
        }

        let species_at = cell_table_end + next_species * SPECIES_SIZE;
        put_u32(output, at + 4, next_species as u32);
        put_u16(output, at + 8, 1);
        put_u16(output, at + 10, CELL_FLAG_HAS_SPECIES);
        put_u16(output, species_at, grid.element[cell]);
        output[species_at + 2] = phase;
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
        let mut bytes = 2_i32.to_le_bytes().to_vec();
        for (index, (hash, phase)) in [(0x2d39bf75_u32, 0_u8), (0x11111111, 1_u8), (0x22222222, 2_u8)] .into_iter().enumerate() {
            let mut record = [0_u8; 164];
            record[..4].copy_from_slice(&hash.to_le_bytes());
            record[8..10].copy_from_slice(&(index as u16).to_le_bytes());
            record[10] = phase;
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
        grid.mass[1] = 0.5;
        grid.temperature[1] = 450.0;
        grid
    }

    #[test]
    fn publishes_checked_sparse_legacy_projection() {
        let grid = grid();
        let elements = elements();
        let mut image = vec![0; image_len(&grid).unwrap()];
        write_image(&grid, &elements, 42, &mut image).unwrap();
        assert_eq!(u32::from_le_bytes(image[0..4].try_into().unwrap()), MAGIC);
        assert_eq!(u16::from_le_bytes(image[4..6].try_into().unwrap()), VERSION);
        assert_eq!(u64::from_le_bytes(image[8..16].try_into().unwrap()), 42);
        assert_eq!(u32::from_le_bytes(image[16..20].try_into().unwrap()), 2);
        assert_eq!(u32::from_le_bytes(image[20..24].try_into().unwrap()), 2);
        assert_eq!(u32::from_le_bytes(image[28..32].try_into().unwrap()), FLAG_LEGACY_SINGLE_ELEMENT);
        let first_cell = HEADER_SIZE;
        let second_cell = HEADER_SIZE + CELL_SIZE;
        let species_start = HEADER_SIZE + 2 * CELL_SIZE;
        assert_eq!(f32::from_bits(u32::from_le_bytes(image[first_cell..first_cell+4].try_into().unwrap())), 1.25);
        assert_eq!(u32::from_le_bytes(image[first_cell+4..first_cell+8].try_into().unwrap()), 0);
        assert_eq!(u16::from_le_bytes(image[first_cell+8..first_cell+10].try_into().unwrap()), 1);
        assert_eq!(image[species_start+2], 1);
        assert_eq!(u16::from_le_bytes(image[species_start..species_start+2].try_into().unwrap()), 1);
        assert_eq!(u16::from_le_bytes(image[species_start+SPECIES_SIZE..species_start+SPECIES_SIZE+2].try_into().unwrap()), 2);
        assert_eq!(image[species_start+SPECIES_SIZE+2], 2);
        assert_eq!(u32::from_le_bytes(image[second_cell+4..second_cell+8].try_into().unwrap()), 1);
    }

    #[test]
    fn rejects_missing_element_and_phase_zero_with_positive_mass() {
        let mut grid = grid();
        let elements = elements();
        grid.element[0] = 99;
        let mut image = vec![0; image_len(&grid).unwrap()];
        assert_eq!(write_image(&grid, &elements, 1, &mut image).unwrap_err().status, Status::InvalidState);

        grid.element[0] = 0;
        assert_eq!(write_image(&grid, &elements, 1, &mut image).unwrap_err().status, Status::InvalidState);
    }

    #[test]
    fn rejects_wrong_output_size() {
        let grid = grid();
        let elements = elements();
        let mut image = vec![0; image_len(&grid).unwrap() - 1];
        assert_eq!(write_image(&grid, &elements, 1, &mut image).unwrap_err().status, Status::InvalidArgument);
    }
}
