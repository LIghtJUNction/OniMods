use crate::status::{Error, Result, Status};

pub(crate) const MAX_PAYLOAD: usize = 64 * 1024 * 1024;
pub(crate) const MAX_CELLS: usize = 1_048_576;
pub(crate) const MAX_BATCH: usize = 4096;

pub(crate) fn u16at(bytes: &[u8], at: usize) -> u16 {
    u16::from_le_bytes(bytes[at..at + 2].try_into().unwrap())
}
pub(crate) fn i32at(bytes: &[u8], at: usize) -> i32 {
    i32::from_le_bytes(bytes[at..at + 4].try_into().unwrap())
}
pub(crate) fn u32at(bytes: &[u8], at: usize) -> u32 {
    u32::from_le_bytes(bytes[at..at + 4].try_into().unwrap())
}
pub(crate) fn f32at(bytes: &[u8], at: usize) -> f32 {
    f32::from_le_bytes(bytes[at..at + 4].try_into().unwrap())
}
pub(crate) fn invalid(message: impl Into<String>) -> Error {
    Error::new(Status::InvalidArgument, message)
}
pub(crate) fn dimensions(width: i32, height: i32) -> Result<usize> {
    if width <= 0 || height <= 0 {
        return Err(invalid("grid dimensions must be positive"));
    }
    let count = (width as usize)
        .checked_mul(height as usize)
        .ok_or_else(|| Error::new(Status::ResourceLimit, "grid dimension overflow"))?;
    if count > MAX_CELLS {
        return Err(Error::new(
            Status::ResourceLimit,
            "grid exceeds 1048576 cells",
        ));
    }
    Ok(count)
}
pub(crate) fn nonnegative(value: f32) -> bool {
    value.is_finite() && value >= 0.0
}

// Arbitrary C pointer validity remains the caller's responsibility. Length and
// address arithmetic checks cannot prove that a foreign allocation exists.
pub(crate) unsafe fn input<'a>(ptr: *const u8, length: usize) -> Result<&'a [u8]> {
    if length > MAX_PAYLOAD {
        return Err(Error::new(Status::ResourceLimit, "message exceeds 64 MiB"));
    }
    if length == 0 {
        return Ok(&[]);
    }
    if ptr.is_null() || (ptr as usize).checked_add(length).is_none() {
        return Err(invalid(
            "message pointer is null or address range overflows",
        ));
    }
    // SAFETY: the caller guarantees a live, readable allocation for this range.
    Ok(unsafe { std::slice::from_raw_parts(ptr, length) })
}

pub(crate) fn overlaps(a: usize, length: usize, b: usize, other_length: usize) -> bool {
    match (a.checked_add(length), b.checked_add(other_length)) {
        (Some(end), Some(other_end)) => a < other_end && b < end,
        _ => true,
    }
}
