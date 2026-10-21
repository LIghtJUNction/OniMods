use super::*;
use std::hint::black_box;

unsafe extern "C" {
    fn __errno_location() -> *mut i32;
}

fn errno() -> i32 {
    // glibc returns the current thread's live errno slot.
    unsafe { *__errno_location() }
}
fn set_errno(value: i32) {
    unsafe {
        *__errno_location() = value;
    }
}
fn csr() -> u32 {
    let mut value = 0;
    unsafe {
        std::arch::asm!("stmxcsr [{p}]", p = in(reg) &mut value,
                            options(nostack, preserves_flags));
    }
    value
}
fn install(value: u32) {
    unsafe {
        std::arch::asm!("ldmxcsr [{p}]", p = in(reg) &value,
                            options(nostack, preserves_flags));
    }
}
struct Restore(u32);
impl Drop for Restore {
    fn drop(&mut self) {
        install(self.0);
    }
}

fn table(ka: u32, kb: u32, sa: u32, sb: u32, phase: u8) -> ElementTable {
    let mut bytes = Vec::new();
    bytes.extend_from_slice(&2_i32.to_le_bytes());
    for (index, (conductivity, surface)) in [(ka, sa), (kb, sb)].into_iter().enumerate() {
        let mut record = [0_u8; 164];
        record[8..10].copy_from_slice(&(index as u16).to_le_bytes());
        record[10] = phase;
        record[12..16].copy_from_slice(&1.0_f32.to_le_bytes());
        record[16..20].copy_from_slice(&conductivity.to_le_bytes());
        for offset in [24, 28, 32] {
            record[offset..offset + 4].copy_from_slice(&surface.to_le_bytes());
        }
        bytes.extend_from_slice(&record);
    }
    bytes.extend_from_slice(&[0_u8; 8]);
    ElementTable::parse(&bytes).unwrap()
}

#[inline(never)]
fn original(table: &ElementTable, key: (u16, u16, u8, u8)) -> f32 {
    let (a, b, ia, ib) = black_box(key);
    black_box(contact_uncached(table, a, b, ia, ib))
}
#[inline(never)]
fn candidate(cache: &mut ThermalContactCache<'_>, key: (u16, u16, u8, u8)) -> f32 {
    let (a, b, ia, ib) = black_box(key);
    black_box(cache.contact(a, b, ia, ib))
}

fn check(table: &ElementTable, env: u32) -> usize {
    let keys = [
        (0, 1, 255, 255),
        (0, 1, 255, 255),
        (0, 1, 1, 255),
        (0, 1, 1, 255),
        (1, 0, 255, 255),
        (1, 0, 255, 255),
        (0, 1, 0, 0),
        (0, 1, 0, 0),
        (0, 1, 1, 1),
        (0, 1, 1, 1),
        (0, 1, 254, 254),
        (0, 1, 254, 254),
    ];
    let mut expected = Vec::with_capacity(keys.len());
    let mut actual = Vec::with_capacity(keys.len());
    let mut cache = ThermalContactCache::new(table);
    install(env);
    set_errno(37);
    for key in keys {
        let value = original(table, key).to_bits();
        expected.push((value, csr() & 0x3f, errno()));
    }
    install(env);
    set_errno(37);
    for key in keys {
        let value = candidate(&mut cache, key).to_bits();
        actual.push((value, csr() & 0x3f, errno()));
    }
    install(0x1f80);
    assert_eq!(
        actual,
        expected,
        "env={env:#x}, ka={:#x}, kb={:#x}",
        table.elements[0].record.thermal_conductivity.to_bits(),
        table.elements[1].record.thermal_conductivity.to_bits()
    );
    cache.hits
}
fn next(rng: &mut u32) -> u32 {
    *rng ^= *rng << 13;
    *rng ^= *rng >> 17;
    *rng ^= *rng << 5;
    *rng
}

#[test]
fn cached_contact_preserves_words_flags_errno_in_32_environments() {
    let _restore = Restore(csr());
    let words = [
        0,
        0x8000_0000,
        1,
        0x007f_ffff,
        0x0080_0000,
        0x2f7f_ffff,
        0x2f80_0000,
        0x3dcc_cccd,
        0x3f80_0000,
        0x4f80_0000,
        0x4f80_0001,
        0x7f7f_ffff,
        0x7f80_0000,
        0xff80_0000,
        0x7fc0_1234,
        0x7f80_0001,
        0xbf80_0000,
    ];
    let mut rng = 0x3141_5926_u32;
    let mut hits = 0;
    let mut comparisons = 0;
    for rounding in [0, 0x2000, 0x4000, 0x6000] {
        for daz in [0, 0x40] {
            for ftz in [0, 0x8000] {
                for sticky in [0, 0x3f] {
                    let env = 0x1f80 | rounding | daz | ftz | sticky;
                    for ka in words {
                        for kb in words {
                            hits += check(&table(ka, kb, 0x3f80_0000, 0x3f80_0000, 3), env);
                            comparisons += 12;
                        }
                    }
                    for _ in 0..2000 {
                        let ka = 0x2f80_0000 + next(&mut rng) % (0x4f80_0000 - 0x2f80_0000 + 1);
                        let kb = 0x2f80_0000 + next(&mut rng) % (0x4f80_0000 - 0x2f80_0000 + 1);
                        let sa = 0x3780_0000 + next(&mut rng) % (0x4780_0000 - 0x3780_0000 + 1);
                        let sb = 0x3780_0000 + next(&mut rng) % (0x4780_0000 - 0x3780_0000 + 1);
                        hits += check(&table(ka, kb, sa, sb, (next(&mut rng) % 4) as u8), env);
                        comparisons += 12;
                    }
                    for _ in 0..200 {
                        hits += check(
                            &table(
                                next(&mut rng),
                                next(&mut rng),
                                next(&mut rng),
                                next(&mut rng),
                                3,
                            ),
                            env,
                        );
                        comparisons += 12;
                    }
                }
            }
        }
    }
    assert!(
        hits > 100000,
        "The actual cache-hit path was not exercised enough"
    );
    eprintln!("CT_CACHE_CHECK comparisons={comparisons} cache_hits={hits}");
}

#[test]
fn clearing_sticky_flags_or_changing_rounding_invalidates_entry() {
    let _restore = Restore(csr());
    let table = table(0x3dcc_cccd, 0x3e4c_cccd, 0x3f80_0000, 0x3f80_0000, 3);
    let mut cache = ThermalContactCache::new(&table);
    install(0x1f80);
    candidate(&mut cache, (0, 1, 255, 255));
    assert_ne!(csr() & 0x20, 0);
    candidate(&mut cache, (0, 1, 255, 255));
    assert_eq!(cache.hits, 1);
    install(0x1f80);
    candidate(&mut cache, (0, 1, 255, 255));
    assert_eq!(
        cache.hits, 1,
        "Cleared inexact flag must force original computation"
    );
    install(0x3fa0);
    candidate(&mut cache, (0, 1, 255, 255));
    assert_eq!(
        cache.hits, 1,
        "Changed rounding must force original computation"
    );
    assert!(std::mem::size_of::<ThermalContactCache<'static>>() <= 40);
}

#[test]
fn every_unmasked_exception_disables_reuse_without_executing_fp() {
    let _restore = Restore(csr());
    install(0x1fa0);
    let after = FloatEnv::capture();
    for mask in [0x80, 0x100, 0x200, 0x400, 0x800, 0x1000] {
        install(0x1fa0 & !mask);
        let current = FloatEnv::capture();
        let disabled = !current.retains_arithmetic_effects(after);
        install(0x1f80);
        assert!(disabled);
    }
}

#[test]
fn conductivity_min_keeps_official_operand_order_for_nan_and_zero() {
    let _restore = Restore(csr());
    install(0x1f80);
    let records = table(
        1.0_f32.to_bits(),
        f32::INFINITY.to_bits(),
        1.0_f32.to_bits(),
        1.0_f32.to_bits(),
        3,
    );
    // The zero insulation turns +infinity into NaN in the second operand.
    // Native MINSS returns that second operand instead of finite ka.
    let (forward, _) = insulation_conductivity(&records, 0, 1, 255, 0);
    assert!(forward.is_nan());
    let (reverse, _) = insulation_conductivity(&records, 1, 0, 0, 255);
    let expected = ((255.0_f32 * 255.0) * CONDUCTIVITY_SCALE) * 1.0;
    assert_eq!(reverse.to_bits(), expected.to_bits());
    let zeros = table(0x8000_0000, 0, 1.0_f32.to_bits(), 1.0_f32.to_bits(), 3);
    assert_eq!(insulation_conductivity(&zeros, 0, 1, 0, 0).0.to_bits(), 0);
    assert_eq!(
        insulation_conductivity(&zeros, 1, 0, 0, 0).0.to_bits(),
        0x8000_0000
    );
}
