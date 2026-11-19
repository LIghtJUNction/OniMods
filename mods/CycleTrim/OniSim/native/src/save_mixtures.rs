//! Versioned authoritative inventory extension after the unchanged SIMSAVE v15 body.
//! The base keeps the legacy display slot; this section preserves all other slots.
use super::{
    LoadedCell, Reader, element_hash, invalid, put_f32, put_i32, reserved, resource, state,
};
use crate::bytes::MAX_PAYLOAD;
use crate::status::{Error, Result, Status};
use crate::tables::ElementTable;
use crate::world::PhysicsWorld;
use std::collections::{HashMap, HashSet};

const MAGIC: &[u8; 8] = b"ONIMIX\0\0";
const VERSION: u32 = 1;
// A distinct version word makes the extension mandatory, even if its entire
// trailer is truncated. This does not claim a future official SIMSAVE version.
pub(super) const FORMAT_VERSION: i32 = 0x4f4d0001;
const HEADER_BYTES: usize = 32;
const RECORD_BYTES: usize = 24;

pub(super) struct LoadedMixture {
    pub(super) cell: usize,
    pub(super) secondary: Option<(u16, f32)>,
    pub(super) extras: Vec<(u16, f32)>,
}

fn put_u32(bytes: &mut Vec<u8>, value: u32) {
    bytes.extend_from_slice(&value.to_le_bytes());
}

fn u32_field(reader: &mut Reader<'_>) -> Result<u32> {
    Ok(reader.i32()? as u32)
}

fn finite_mass(mass: f32) -> Result<()> {
    if !mass.is_finite() || mass < 0.0 {
        return Err(invalid(
            "mixture inventory mass must be finite and nonnegative",
        ));
    }
    Ok(())
}

fn add_species(
    id: u16,
    mass: f32,
    elements: &ElementTable,
    seen: &mut HashSet<u16>,
    total: &mut f32,
) -> Result<()> {
    finite_mass(mass)?;
    let element = elements
        .elements
        .get(id as usize)
        .ok_or_else(|| invalid("mixture inventory refers to an unavailable element"))?;
    if mass == 0.0 {
        return Ok(());
    }
    if element.record.state & 3 == 0 {
        return Err(invalid(
            "positive mixture inventory requires a material phase",
        ));
    }
    if !seen.insert(id) {
        return Err(invalid("duplicate positive element in mixture inventory"));
    }
    *total += mass;
    if !total.is_finite() {
        return Err(invalid("mixture inventory total mass overflows"));
    }
    Ok(())
}

/// Preserve the existing byte image for worlds without additional inventories.
pub(super) fn append(
    world: &PhysicsWorld,
    elements: &ElementTable,
    bytes: &mut Vec<u8>,
) -> Result<()> {
    let count = world
        .stride
        .checked_mul(world.padded_height)
        .ok_or_else(resource)?;
    if world.mixture_mass.len() != count || world.mixture_element.len() != count {
        return Err(state(
            "mixture inventory dimensions differ from the save grid",
        ));
    }
    let mut seen = HashSet::new();
    seen.try_reserve(elements.len()).map_err(|_| resource())?;
    let mut records = 0usize;
    let mut body_bytes = 0usize;
    for cell in 0..count {
        finite_mass(world.mixture_mass[cell])?;
        let mut extras = 0usize;
        if let Some(values) = world.extra_mixtures.get(&cell) {
            for &(_, mass) in values {
                finite_mass(mass)?;
                extras += usize::from(mass > 0.0);
            }
        }
        let secondary = world.mixture_mass[cell] > 0.0;
        if !secondary && extras == 0 {
            continue;
        }
        let temperature = world.write.temperature[cell];
        if !temperature.is_finite() || temperature < 0.0 {
            return Err(invalid(
                "mixture inventory temperature must be finite and nonnegative",
            ));
        }
        seen.clear();
        let mut total = 0.0;
        add_species(
            world.write.element[cell],
            world.write.mass[cell],
            elements,
            &mut seen,
            &mut total,
        )?;
        if secondary {
            add_species(
                world.mixture_element[cell],
                world.mixture_mass[cell],
                elements,
                &mut seen,
                &mut total,
            )?;
        }
        if let Some(values) = world.extra_mixtures.get(&cell) {
            for &(id, mass) in values {
                if mass > 0.0 {
                    add_species(id, mass, elements, &mut seen, &mut total)?;
                }
            }
        }
        body_bytes = extras
            .checked_add(usize::from(secondary))
            .and_then(|n| n.checked_mul(8))
            .and_then(|n| n.checked_add(RECORD_BYTES))
            .and_then(|n| body_bytes.checked_add(n))
            .ok_or_else(resource)?;
        records = records.checked_add(1).ok_or_else(resource)?;
    }
    if records == 0 {
        return Ok(());
    }
    let extension_bytes = HEADER_BYTES.checked_add(body_bytes).ok_or_else(resource)?;
    let total_bytes = bytes
        .len()
        .checked_add(extension_bytes)
        .ok_or_else(resource)?;
    if total_bytes > MAX_PAYLOAD {
        return Err(resource());
    }
    let base_bytes = bytes.len();
    bytes
        .try_reserve_exact(extension_bytes)
        .map_err(|_| resource())?;
    bytes[8..12].copy_from_slice(&FORMAT_VERSION.to_le_bytes());
    bytes.extend_from_slice(MAGIC);
    put_u32(bytes, VERSION);
    put_u32(bytes, HEADER_BYTES as u32);
    put_u32(bytes, u32::try_from(body_bytes).map_err(|_| resource())?);
    put_u32(bytes, u32::try_from(records).map_err(|_| resource())?);
    put_u32(bytes, 0); // reserved; no optional interpretation flags in v1
    put_u32(bytes, 0); // CRC32 filled after the complete image is written
    for cell in 0..count {
        let secondary = world.mixture_mass[cell] > 0.0;
        let extras = world
            .extra_mixtures
            .get(&cell)
            .map(Vec::as_slice)
            .unwrap_or(&[]);
        let extra_count = extras.iter().filter(|entry| entry.1 > 0.0).count();
        if !secondary && extra_count == 0 {
            continue;
        }
        put_u32(bytes, u32::try_from(cell).map_err(|_| resource())?);
        put_u32(bytes, u32::from(secondary));
        put_u32(bytes, u32::try_from(extra_count).map_err(|_| resource())?);
        put_f32(bytes, world.write.temperature[cell]);
        put_i32(bytes, element_hash(elements, world.write.element[cell])?);
        put_f32(bytes, world.write.mass[cell]);
        if secondary {
            put_i32(bytes, element_hash(elements, world.mixture_element[cell])?);
            put_f32(bytes, world.mixture_mass[cell]);
        }
        for &(id, mass) in extras {
            if mass > 0.0 {
                put_i32(bytes, element_hash(elements, id)?);
                put_f32(bytes, mass);
            }
        }
    }
    let checksum = image_checksum(bytes, base_bytes);
    bytes[base_bytes + 28..base_bytes + 32].copy_from_slice(&checksum.to_le_bytes());
    Ok(())
}

pub(super) fn parse(
    bytes: &[u8],
    base_bytes: usize,
    required: bool,
    elements: &ElementTable,
    indices: &HashMap<i32, u16>,
    cells: &mut [LoadedCell],
) -> Result<Vec<LoadedMixture>> {
    if !required {
        return if bytes.len() == base_bytes {
            Ok(Vec::new())
        } else {
            Err(invalid("unmarked trailing native save extension"))
        };
    }
    if bytes.len() == base_bytes {
        return Err(invalid("required mixture save extension is missing"));
    }
    let tail = bytes
        .get(base_bytes..)
        .ok_or_else(|| invalid("truncated native save body"))?;
    let mut reader = Reader::new(tail);
    if reader.take(8)? != MAGIC {
        return Err(invalid("unknown trailing native save extension"));
    }
    if u32_field(&mut reader)? != VERSION {
        return Err(Error::new(
            Status::Unsupported,
            "unsupported mixture save version",
        ));
    }
    if u32_field(&mut reader)? as usize != HEADER_BYTES {
        return Err(invalid("unsupported mixture save header size"));
    }
    let body_bytes = u32_field(&mut reader)? as usize;
    let record_count = u32_field(&mut reader)? as usize;
    if u32_field(&mut reader)? != 0 {
        return Err(invalid("unknown mixture save flags"));
    }
    let checksum = u32_field(&mut reader)?;
    if tail.len().checked_sub(HEADER_BYTES) != Some(body_bytes)
        || record_count == 0
        || record_count > cells.len()
        || record_count
            .checked_mul(RECORD_BYTES + 8)
            .is_none_or(|n| n > body_bytes)
    {
        return Err(invalid(
            "mixture save lengths or record count are inconsistent",
        ));
    }
    if checksum != image_checksum(bytes, base_bytes) {
        return Err(invalid("mixture save checksum mismatch"));
    }
    let mut records = reserved(record_count)?;
    let mut seen = HashSet::new();
    seen.try_reserve(elements.len()).map_err(|_| resource())?;
    let mut previous = None;
    for _ in 0..record_count {
        let cell = u32_field(&mut reader)? as usize;
        if cell >= cells.len() || previous.is_some_and(|last| cell <= last) {
            return Err(invalid(
                "mixture cell indices must be in range and strictly increasing",
            ));
        }
        previous = Some(cell);
        let secondary_flag = u32_field(&mut reader)?;
        let extra_count = u32_field(&mut reader)? as usize;
        if secondary_flag > 1
            || (secondary_flag == 0 && extra_count == 0)
            || extra_count >= elements.len()
        {
            return Err(invalid("invalid mixture inventory slot counts"));
        }
        let additional = extra_count
            .checked_add(secondary_flag as usize)
            .ok_or_else(resource)?;
        if additional
            .checked_mul(8)
            .is_none_or(|n| n > tail.len() - reader.position)
        {
            return Err(invalid("truncated mixture inventory slots"));
        }
        let temperature = reader.f32()?;
        let primary_hash = reader.i32()?;
        let primary_mass = reader.f32()?;
        if !temperature.is_finite() || temperature < 0.0 {
            return Err(invalid(
                "mixture inventory temperature must be finite and nonnegative",
            ));
        }
        let primary = *indices
            .get(&primary_hash)
            .ok_or_else(|| invalid("mixture primary element is unavailable"))?;
        // The authoritative duplicate must agree with the v15 display record.
        // It intentionally bypasses legacy load-time phase conversion for mixed
        // cells, which would otherwise change only one species in the inventory.
        let at = 29 + cell * 16;
        let mut base = Reader::new(&bytes[at..at + 12]);
        if base.i32()? != primary_hash
            || base.f32()?.to_bits() != temperature.to_bits()
            || base.f32()?.to_bits() != primary_mass.to_bits()
        {
            return Err(invalid(
                "mixture primary state disagrees with the native save body",
            ));
        }
        seen.clear();
        let mut total = 0.0;
        add_species(primary, primary_mass, elements, &mut seen, &mut total)?;
        let mut read_species = |reader: &mut Reader<'_>| -> Result<(u16, f32)> {
            let hash = reader.i32()?;
            let mass = reader.f32()?;
            let id = *indices
                .get(&hash)
                .ok_or_else(|| invalid("mixture element is unavailable"))?;
            if mass <= 0.0 {
                return Err(invalid(
                    "stored additional inventory must have positive mass",
                ));
            }
            add_species(id, mass, elements, &mut seen, &mut total)?;
            Ok((id, mass))
        };
        let secondary = if secondary_flag == 1 {
            Some(read_species(&mut reader)?)
        } else {
            None
        };
        let mut extras = reserved(extra_count)?;
        for _ in 0..extra_count {
            extras.push(read_species(&mut reader)?);
        }
        cells[cell].element = primary;
        cells[cell].mass = primary_mass;
        cells[cell].temperature = temperature;
        records.push(LoadedMixture {
            cell,
            secondary,
            extras,
        });
    }
    if reader.position != tail.len() {
        return Err(invalid("unconsumed mixture save bytes"));
    }
    Ok(records)
}

// IEEE CRC32 detects accidental corruption of the entire v15 body, extension
// header and inventory payload. Only the checksum word itself is excluded.
fn image_checksum(bytes: &[u8], base_bytes: usize) -> u32 {
    checksum_parts(&[
        &bytes[..base_bytes + 28],
        &bytes[base_bytes + HEADER_BYTES..],
    ])
}

fn checksum_parts(parts: &[&[u8]]) -> u32 {
    let mut crc = !0_u32;
    for part in parts {
        for &byte in *part {
            crc = CRC_TABLE[((crc ^ u32::from(byte)) & 255) as usize] ^ (crc >> 8);
        }
    }
    !crc
}

const CRC_TABLE: [u32; 256] = {
    let mut table = [0_u32; 256];
    let mut index = 0;
    while index < 256 {
        let mut crc = index as u32;
        let mut bit = 0;
        while bit < 8 {
            crc = if crc & 1 != 0 {
                (crc >> 1) ^ 0xedb88320
            } else {
                crc >> 1
            };
            bit += 1;
        }
        table[index] = crc;
        index += 1;
    }
    table
};

pub(super) fn apply(
    records: Vec<LoadedMixture>,
    world: &mut PhysicsWorld,
    origin: [usize; 2],
    width: usize,
) {
    for record in records {
        let target =
            (origin[1] + record.cell / width) * world.stride + origin[0] + record.cell % width;
        if let Some((id, mass)) = record.secondary {
            world.mixture_element[target] = id;
            world.mixture_mass[target] = mass;
        }
        if !record.extras.is_empty() {
            world.extra_mixtures.insert(target, record.extras);
        }
    }
}

pub(super) fn refresh_cell_cache(world: &mut PhysicsWorld, cell: usize) {
    let mut dense = [0.0; crate::multicomponent_flux::GAS_CHANNELS];
    world.for_each_cell_species(cell, |id, mass| {
        if let Some(&channel) = world.gas_channel_of_element.get(id as usize) {
            if (channel as usize) < dense.len() {
                dense[channel as usize] = mass;
            }
        }
    });
    let first = cell * dense.len();
    world.species_mass[first..first + dense.len()].copy_from_slice(&dense);
    world.dirty[cell] = true;
    world.dirty_flags[cell] = 0x1f;
}

#[cfg(test)]
mod tests {
    #[test]
    fn checksum_matches_ieee_known_vector() {
        assert_eq!(super::checksum_parts(&[b"123456789"]), 0xcbf43926);
    }
}
