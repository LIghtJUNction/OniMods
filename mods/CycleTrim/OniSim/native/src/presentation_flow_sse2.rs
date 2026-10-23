//! SSE2 is guaranteed by the x86-64 game ABI. No AVX or vendor-specific ISA
//! requirement is introduced. All pointer accesses stay inside validated rows.
use std::arch::x86_64::*;

pub(super) fn exceptions_masked() -> bool {
    let mut value = 0_u32;
    unsafe {
        std::arch::asm!("stmxcsr [{p}]", p = in(reg) &mut value,
            options(nostack, preserves_flags));
    }
    value & 0x1f80 == 0x1f80
}

pub(super) fn row(
    output: &mut [[f32; 2]],
    old: &[u16],
    new: &[u16],
    mass: &[f32],
    flow: &[[f32; 4]],
) -> usize {
    let count = output.len();
    assert!(old.len() >= count && new.len() >= count && mass.len() >= count && flow.len() >= count);
    let end = count / 4 * 4;
    // These bounds establish every 8-byte element, 16-byte scalar/flow load
    // and both 16-byte output stores below, including the final complete block.
    unsafe {
        let one = _mm_set1_ps(1.0);
        let infinity = _mm_set1_epi32(f32::INFINITY.to_bits() as i32);
        let absolute = _mm_set1_epi32(0x7fff_ffff);
        for x in (0..end).step_by(4) {
            let a = _mm_loadu_ps(flow.as_ptr().add(x).cast());
            let b = _mm_loadu_ps(flow.as_ptr().add(x + 1).cast());
            let c = _mm_loadu_ps(flow.as_ptr().add(x + 2).cast());
            let d = _mm_loadu_ps(flow.as_ptr().add(x + 3).cast());
            let equal16 = _mm_cmpeq_epi16(
                _mm_loadl_epi64(old.as_ptr().add(x).cast()),
                _mm_loadl_epi64(new.as_ptr().add(x).cast()),
            );
            let equal = _mm_castsi128_ps(_mm_unpacklo_epi16(equal16, equal16));
            if _mm_movemask_ps(equal) == 0 {
                // No mass comparison or load occurs in the original changed-
                // element path. Keep this block independent of that stream.
                // Pair layout directly matches the two-float output, avoiding
                // the full four-way transpose and its final interleave.
                let a = _mm_shuffle_ps::<0x9c>(a, a);
                let b = _mm_shuffle_ps::<0x9c>(b, b);
                let c = _mm_shuffle_ps::<0x9c>(c, c);
                let d = _mm_shuffle_ps::<0x9c>(d, d);
                let ab = _mm_sub_ps(_mm_movelh_ps(a, b), _mm_movehl_ps(b, a));
                let cd = _mm_sub_ps(_mm_movelh_ps(c, d), _mm_movehl_ps(d, c));
                let destination = output.as_mut_ptr().add(x).cast::<f32>();
                _mm_storeu_ps(destination, _mm_mul_ps(ab, _mm_setzero_ps()));
                _mm_storeu_ps(destination.add(4), _mm_mul_ps(cd, _mm_setzero_ps()));
                continue;
            }
            let scale = {
                let m = _mm_loadu_ps(mass.as_ptr().add(x));
                let nan = _mm_and_si128(
                    _mm_castps_si128(equal),
                    _mm_cmpgt_epi32(_mm_and_si128(_mm_castps_si128(m), absolute), infinity),
                );
                if _mm_movemask_epi8(nan) != 0 {
                    // A NaN mass must take the original comparison path,
                    // including its signaling-NaN exception behavior.
                    super::scalar_row(
                        &mut output[x..x + 4],
                        &old[x..x + 4],
                        &new[x..x + 4],
                        &mass[x..x + 4],
                        &flow[x..x + 4],
                    );
                    continue;
                }
                // The comparison must execute for active subnormal masses to
                // retain MXCSR's denormal-operand flag. Inactive lanes compare
                // zero, matching the original path that does not read mass.
                let divide = _mm_cmpgt_ps(_mm_and_ps(equal, m), one);
                if _mm_movemask_ps(divide) == 0 {
                    _mm_and_ps(equal, one)
                } else {
                    let denominator = _mm_or_ps(_mm_and_ps(divide, m), _mm_andnot_ps(divide, one));
                    _mm_and_ps(equal, _mm_div_ps(one, denominator))
                }
            };
            let ab_lo = _mm_unpacklo_ps(a, b);
            let ab_hi = _mm_unpackhi_ps(a, b);
            let cd_lo = _mm_unpacklo_ps(c, d);
            let cd_hi = _mm_unpackhi_ps(c, d);
            let right = _mm_movelh_ps(ab_lo, cd_lo);
            let left = _mm_movehl_ps(cd_lo, ab_lo);
            let down = _mm_movelh_ps(ab_hi, cd_hi);
            let up = _mm_movehl_ps(cd_hi, ab_hi);
            let horizontal = _mm_mul_ps(_mm_sub_ps(right, left), scale);
            let vertical = _mm_mul_ps(_mm_sub_ps(up, down), scale);
            let destination = output.as_mut_ptr().add(x).cast::<f32>();
            _mm_storeu_ps(destination, _mm_unpacklo_ps(horizontal, vertical));
            _mm_storeu_ps(destination.add(4), _mm_unpackhi_ps(horizontal, vertical));
        }
    }
    end
}
