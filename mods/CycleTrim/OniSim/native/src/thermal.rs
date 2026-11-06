//! Official temperature exchange kernels, reconstructed from libSimDLL.

use crate::float_env::FloatEnv;
use crate::fluid_helpers::game_index;
use crate::numeric::{smax, smin};
use crate::tables::{ElementRecord, ElementTable};
use crate::world::PhysicsWorld;

const MIN_TEMPERATURE: f32 = 1.0;
const MAX_TEMPERATURE: f32 = 10_000.0;
const INSULATION_GEOMETRIC_MEAN_THRESHOLD: f32 = f32::from_bits(0x477e_00fe);
const CONDUCTIVITY_SCALE: f32 = f32::from_bits(0x3781_0183);
const MAX_TRANSFER_FRACTION: f64 = f64::from_bits(0x3fc9_9999_a000_0000);
const CELL_TRANSFER_STEP: f64 = 0.25;
// Official rodata 0xa5a20: the f32 0.01 value promoted to f64.
const BACKWALL_TRANSFER_STEP: f64 = f64::from_bits(0x3f84_7ae1_4000_0000);
const GAS_MULTIPLIER: f32 = 1.0;

fn record(table: &ElementTable, index: u16) -> &ElementRecord {
    &table.elements[index as usize].record
}

fn phase_multiplier(record: &ElementRecord, phase: u8) -> f32 {
    match phase & 3 {
        1 => record.surface_area_gas,
        2 => record.surface_area_liquid,
        3 => record.surface_area_solid,
        _ => GAS_MULTIPLIER,
    }
}

#[inline]
fn ordered_min(a: f64, b: f64) -> f64 {
    #[cfg(target_arch = "x86_64")]
    unsafe {
        use core::arch::x86_64::{_mm_cvtsd_f64, _mm_min_sd, _mm_set_sd};
        _mm_cvtsd_f64(_mm_min_sd(_mm_set_sd(a), _mm_set_sd(b)))
    }
    #[cfg(not(target_arch = "x86_64"))]
    {
        if a < b { a } else { b }
    }
}

#[inline]
fn ordered_max(a: f64, b: f64) -> f64 {
    #[cfg(target_arch = "x86_64")]
    unsafe {
        use core::arch::x86_64::{_mm_cvtsd_f64, _mm_max_sd, _mm_set_sd};
        _mm_cvtsd_f64(_mm_max_sd(_mm_set_sd(a), _mm_set_sd(b)))
    }
    #[cfg(not(target_arch = "x86_64"))]
    {
        if a > b { a } else { b }
    }
}

fn insulation_conductivity(
    table: &ElementTable,
    element_a: u16,
    element_b: u16,
    insulation_a: u8,
    insulation_b: u8,
) -> (f32, f32) {
    let a = record(table, element_a);
    let b = record(table, element_b);
    let ia = f32::from(insulation_a);
    let ib = f32::from(insulation_b);
    let ia2 = ia * ia;
    let ib2 = ib * ib;
    let ka = ia2 * CONDUCTIVITY_SCALE * a.thermal_conductivity;
    // ELF 0x54a02..0x54a0c rounds the insulation factor before multiplying
    // conductivity. Reassociating these f32 products changes mixed-scene heat.
    let kb = (CONDUCTIVITY_SCALE * ib2) * b.thermal_conductivity;
    let mixed =
        if ia2 < INSULATION_GEOMETRIC_MEAN_THRESHOLD || ib2 < INSULATION_GEOMETRIC_MEAN_THRESHOLD {
            // Official 0x54c38 is MINSS(ka, kb): unordered and equal
            // operands select kb. Reversing an ordered comparison changes NaNs.
            smin(ka, kb)
        } else {
            (ka.ln() + kb.ln()) * 0.5
        };
    let effective =
        if ia2 < INSULATION_GEOMETRIC_MEAN_THRESHOLD || ib2 < INSULATION_GEOMETRIC_MEAN_THRESHOLD {
            mixed
        } else {
            mixed.exp()
        };
    (
        effective,
        phase_multiplier(a, b.state) * phase_multiplier(b, a.state),
    )
}

// Keep a single out-of-line uncached kernel. LLVM may commute a plain f32
// multiply at different inline sites, choosing different NaN payloads. Every
// cache miss and the uncached comparison call this same instruction sequence.
#[inline(never)]
fn contact_uncached(table: &ElementTable, ea: u16, eb: u16, ia: u8, ib: u8) -> f32 {
    let (conductivity, surface) = insulation_conductivity(table, ea, eb, ia, ib);
    conductivity * surface
}

#[derive(Clone, Copy)]
struct CachedContact {
    key: (u16, u16, u8, u8),
    contact: f32,
    after: FloatEnv,
}

/// One phase-local entry; the immutable table borrow prevents stale metadata.
/// A new cache is created for every region's temperature phase, including loads.
pub(crate) struct ThermalContactCache<'a> {
    elements: &'a ElementTable,
    cached: Option<CachedContact>,
    #[cfg(test)]
    hits: usize,
}

impl<'a> ThermalContactCache<'a> {
    pub(crate) fn new(elements: &'a ElementTable) -> Self {
        Self {
            elements,
            cached: None,
            #[cfg(test)]
            hits: 0,
        }
    }

    fn contact(&mut self, ea: u16, eb: u16, ia: u8, ib: u8) -> f32 {
        let key = (ea, eb, ia, ib);
        if let Some(cached) = self.cached
            && cached.key == key
            && FloatEnv::capture().retains_arithmetic_effects(cached.after)
        {
            #[cfg(test)]
            {
                self.hits += 1;
            }
            return cached.contact;
        }
        let contact = contact_uncached(self.elements, ea, eb, ia, ib);
        let a = record(self.elements, ea);
        let b = record(self.elements, eb);
        // Positive normal bounded inputs avoid libm domain/range errno effects.
        // u8 insulation is exact; nonzero effective conductivity >= 2^-48,
        // contact >= 2^-80 and <= 2^65. Other wire values use original math.
        let bounded = |value: f32, lo: u32, hi: u32| (lo..=hi).contains(&value.to_bits());
        let eligible = cfg!(all(
            target_arch = "x86_64",
            target_os = "linux",
            target_env = "gnu"
        )) && bounded(a.thermal_conductivity, 0x2f80_0000, 0x4f80_0000)
            && bounded(b.thermal_conductivity, 0x2f80_0000, 0x4f80_0000)
            && bounded(phase_multiplier(a, b.state), 0x3780_0000, 0x4780_0000)
            && bounded(phase_multiplier(b, a.state), 0x3780_0000, 0x4780_0000);
        self.cached = if eligible {
            let after = FloatEnv::capture_after(contact);
            after.masks_all_exceptions().then_some(CachedContact {
                key,
                contact,
                after,
            })
        } else {
            None
        };
        contact
    }
}

/// Update one pair using the official old-snapshot/new-write temperature rule.
/// `a` and `b` are padded internal indexes selected by the element kernel.
pub(crate) fn update_temperature(
    world: &mut PhysicsWorld,
    cache: &mut ThermalContactCache<'_>,
    a: usize,
    b: usize,
) {
    let elements = cache.elements;
    let read = &world.read;
    let write = &mut world.write;
    let ta = read.temperature[a];
    let tb = read.temperature[b];
    let insulation_a = read.insulation[a];
    let insulation_b = read.insulation[b];
    let ea = read.element[a];
    let eb = read.element[b];
    let contact = cache.contact(ea, eb, insulation_a, insulation_b);

    let ca = read.mass[a] * record(elements, ea).specific_heat_capacity;
    let cb = read.mass[b] * record(elements, eb).specific_heat_capacity;
    let (new_a, new_b) = if ta <= tb {
        let equilibrium =
            ((cb as f64 * tb as f64) + (ca as f64 * ta as f64)) / (cb as f64 + ca as f64);
        let delta = tb as f64 - ta as f64;
        let step = CELL_TRANSFER_STEP * delta;
        let cap_a = step * ca as f64;
        let cap_b = step * cb as f64;
        let conductance_cap = delta * MAX_TRANSFER_FRACTION * contact as f64;
        let mut energy = ordered_min(cap_a, conductance_cap);
        energy = ordered_min(energy, cap_b);
        let candidate_a = energy / ca as f64 + ta as f64;
        let new_a = ordered_min(candidate_a, equilibrium);
        let candidate_b = tb as f64 - energy / cb as f64;
        // The cooling cell stops at equilibrium from above (ELF 0x54bba).
        let new_b = ordered_max(candidate_b, equilibrium);
        (new_a, new_b)
    } else {
        let equilibrium =
            ((cb as f64 * tb as f64) + (ca as f64 * ta as f64)) / (cb as f64 + ca as f64);
        let delta = ta as f64 - tb as f64;
        let step = CELL_TRANSFER_STEP * delta;
        let cap_a = step * cb as f64;
        let cap_b = step * ca as f64;
        let conductance_cap = delta * MAX_TRANSFER_FRACTION * contact as f64;
        let mut energy = ordered_min(cap_a, conductance_cap);
        energy = ordered_min(energy, cap_b);
        let candidate_a = ta as f64 - energy / ca as f64;
        let new_a = ordered_max(candidate_a, equilibrium);
        let candidate_b = energy / cb as f64 + tb as f64;
        let new_b = ordered_min(candidate_b, equilibrium);
        (new_a, new_b)
    };

    let out_a = new_a as f32 + (write.temperature[a] - ta);
    let out_b = new_b as f32 + (write.temperature[b] - tb);
    // ELF 0x54c06..0x54c18 uses opposite MINSS operand orders for the
    // two endpoints, then MAXSS with 1 K as the second operand. A zero
    // heat capacity can produce NaN: Rust clamp retains it, whereas the
    // original selects 10000 K for a and 1 K for b. Preserve both orders.
    write.temperature[a] = smax(smin(out_a, MAX_TEMPERATURE), MIN_TEMPERATURE);
    write.temperature[b] = smax(smin(MAX_TEMPERATURE, out_b), MIN_TEMPERATURE);
}

/// Exchange heat between a cell and its backwall; the wall temperature is
/// updated in place in the official ABI's single backwall arrays.
pub(crate) fn update_backwall_temperature(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    events: &mut crate::events::EventQueues,
    cell: usize,
) {
    let cell_element = world.read.element[cell];
    let wall_element = world.backwall_element[cell];
    if wall_element == u16::MAX
        || wall_element == world.vacuum_element
        || cell_element == world.vacuum_element
        || wall_element == world.boundary_element
        || cell_element == world.boundary_element
    {
        return;
    }

    let cell_record = record(elements, cell_element);
    let wall_record = record(elements, wall_element);
    let insulation = f32::from(world.read.insulation[cell]);
    let insulation2 = insulation * insulation;
    let cell_k = insulation2 * CONDUCTIVITY_SCALE * cell_record.thermal_conductivity;
    let wall_k = wall_record.thermal_conductivity;
    let conductance = if insulation2 < INSULATION_GEOMETRIC_MEAN_THRESHOLD {
        // Official 0x552f8: MINSS(cell_k, wall_k), including unordered values.
        smin(cell_k, wall_k)
    } else {
        (wall_k.ln() + cell_k.ln()) * 0.5
    };
    let conductance = if insulation2 < INSULATION_GEOMETRIC_MEAN_THRESHOLD {
        conductance
    } else {
        conductance.exp()
    };
    let surface = phase_multiplier(cell_record, wall_record.state)
        * phase_multiplier(wall_record, cell_record.state);
    let contact = conductance * surface;

    let tc = world.read.temperature[cell];
    let tw = world.backwall_temperature[cell];
    let cc = world.read.mass[cell] * cell_record.specific_heat_capacity;
    let cw = world.backwall_mass[cell] * wall_record.specific_heat_capacity;
    let (next_cell, next_wall) = if tc <= tw {
        let equilibrium =
            ((cw as f64 * tw as f64) + (cc as f64 * tc as f64)) / (cw as f64 + cc as f64);
        let delta = tw as f64 - tc as f64;
        let step = BACKWALL_TRANSFER_STEP * delta;
        let cap_cell = step * cc as f64;
        let cap_wall = step * cw as f64;
        let conductance_cap = delta * MAX_TRANSFER_FRACTION * contact as f64;
        // Native MINSD selects cap_cell when conductance_cap is NaN.
        let mut energy = ordered_min(conductance_cap, cap_cell);
        energy = ordered_min(energy, cap_wall);
        let cell_candidate = tc as f64 + energy / cc as f64;
        let wall_candidate = tw as f64 - energy / cw as f64;
        (
            ordered_min(cell_candidate, equilibrium),
            ordered_max(wall_candidate, equilibrium),
        )
    } else {
        let equilibrium =
            ((cw as f64 * tw as f64) + (cc as f64 * tc as f64)) / (cw as f64 + cc as f64);
        let delta = tc as f64 - tw as f64;
        let step = BACKWALL_TRANSFER_STEP * delta;
        let cap_cell = step * cw as f64;
        let cap_wall = step * cc as f64;
        let conductance_cap = delta * MAX_TRANSFER_FRACTION * contact as f64;
        // Native MINSD selects cap_cell when conductance_cap is NaN.
        let mut energy = ordered_min(conductance_cap, cap_cell);
        // Official 0x55224 uses the previous energy as MINSD source.
        energy = ordered_min(cap_wall, energy);
        let cell_candidate = tc as f64 - energy / cc as f64;
        let wall_candidate = tw as f64 + energy / cw as f64;
        (
            ordered_max(cell_candidate, equilibrium),
            ordered_min(wall_candidate, equilibrium),
        )
    };

    let write = &mut world.write;
    write.temperature[cell] = next_cell as f32 + (write.temperature[cell] - tc);
    let next_wall = next_wall as f32;
    world.backwall_temperature[cell] = next_wall;
    if (next_wall < wall_record.low_temperature - 3.0
        || next_wall > wall_record.high_temperature + 3.0)
        && let Some(game_cell) = game_index(world, cell)
    {
        events.backwall_should_transition(game_cell);
    }
}

#[cfg(all(test, target_arch = "x86_64", target_os = "linux", target_env = "gnu"))]
#[path = "thermal_cache_tests.rs"]
mod cache_tests;

#[cfg(not(test))]
const _: () = assert!(std::mem::size_of::<ThermalContactCache<'static>>() <= 32);
