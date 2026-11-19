//! Liquid occupancy uses the transport solver's mass/capacity definition.
//!
//! Raw fill is unbounded: compressed liquid may occupy more than one nominal
//! cell volume. A published occupied fraction is bounded to [0, 1]; it does not
//! discard compressed mass or represent pressure in kPa.

#[inline]
fn capacity(max_mass: f32) -> f32 {
    max_mass.max(1.0e-6)
}

/// Keep the transport solver's existing f32 arithmetic and compression behavior.
#[inline]
pub(crate) fn raw_fill(mass: f32, max_mass: f32) -> f32 {
    mass / capacity(max_mass)
}

/// Wider accumulation for a read-only publication. Finite f32 inventories can
/// exceed f32's range after division/summation without being invalid inventories.
#[inline]
pub(crate) fn raw_fill_wide(mass: f32, max_mass: f32) -> f64 {
    f64::from(mass) / f64::from(capacity(max_mass))
}

#[inline]
pub(crate) fn occupied_fraction(raw_fill: f64) -> f32 {
    raw_fill.min(1.0) as f32
}

#[inline]
pub(crate) fn species_occupied_fraction(species_raw_fill: f64, total_raw_fill: f64) -> f32 {
    (species_raw_fill / total_raw_fill.max(1.0)) as f32
}
