//! Owned interaction metadata from the official pointer-bearing message.
//!
//! The installed managed writer pins 32-byte records for this call only. The
//! official native reader retains GasObliteration entries (ELF 0x36050); unknown
//! interaction types are skipped. No managed pointer survives this parser.

use crate::bytes::{f32at, i32at, invalid, u16at, u32at, MAX_PAYLOAD};
use crate::gas::GasObliteration;
use crate::status::{Error, Result, Status};

pub(crate) const MESSAGE: i32 = -930_289_787;
const GAS_OBLITERATION: u32 = 0x6826_f67d;
const RECORD_BYTES: usize = 32;

/// The pointer embedded at offset 4 must designate readable memory for all
/// records, for the duration of this call. Address bounds do not establish that
/// a foreign allocation exists. `overlaps_storage` rejects native-owned buffers.
pub(crate) unsafe fn parse(
    payload: &[u8],
    element_count: usize,
    overlaps_storage: impl Fn(usize, usize) -> bool,
) -> Result<Vec<GasObliteration>> {
    if payload.len() != 12 {
        return Err(invalid("element interactions require a 12-byte descriptor"));
    }
    let count = i32at(payload, 0);
    // The official loop is entered only for a positive signed count. Even an
    // empty replacement clears the previously retained interaction metadata.
    if count <= 0 {
        return Ok(Vec::new());
    }
    let length = (count as usize)
        .checked_mul(RECORD_BYTES)
        .filter(|&length| length <= MAX_PAYLOAD)
        .ok_or_else(|| Error::new(Status::ResourceLimit, "interaction array exceeds 64 MiB"))?;
    let address = u64::from_le_bytes(payload[4..12].try_into().unwrap()) as usize;
    if address == 0 || address.checked_add(length).is_none() {
        return Err(invalid("interaction array pointer is null or overflows"));
    }
    if overlaps_storage(address, length) {
        return Err(invalid("interaction array overlaps native-owned storage"));
    }
    let records = std::slice::from_raw_parts(address as *const u8, length);
    decode(records, element_count)
}

fn decode(records: &[u8], element_count: usize) -> Result<Vec<GasObliteration>> {
    let retained = records
        .chunks_exact(RECORD_BYTES)
        .filter(|record| u32at(record, 0) == GAS_OBLITERATION)
        .count();
    let mut rules = Vec::new();
    rules.try_reserve_exact(retained).map_err(|_| {
        Error::new(
            Status::ResourceLimit,
            "could not allocate interaction metadata",
        )
    })?;
    for record in records.chunks_exact(RECORD_BYTES) {
        if u32at(record, 0) != GAS_OBLITERATION {
            continue;
        }
        let element_a = u16at(record, 4);
        let element_b = u16at(record, 6);
        let product = u16at(record, 8);
        if [element_a, element_b, product]
            .iter()
            .any(|&index| index as usize >= element_count)
        {
            return Err(invalid("interaction references an unregistered element"));
        }
        rules.push(GasObliteration {
            element_a,
            element_b,
            product,
            min_mass: f32at(record, 12),
            probability: f32at(record, 16),
            destroy_percent: f32at(record, 20),
            required_multiplier: f32at(record, 24),
            creation_multiplier: f32at(record, 28),
        });
    }
    Ok(rules)
}
