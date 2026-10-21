// SSE MINSS/MAXSS select their second operand for unordered/equal values.
// Rust f32::min/max have different NaN and signed-zero behavior.
#[inline]
pub(crate) fn smin(first: f32, second: f32) -> f32 {
    #[cfg(target_arch = "x86_64")]
    unsafe {
        use core::arch::x86_64::{_mm_cvtss_f32, _mm_min_ss, _mm_set_ss};
        _mm_cvtss_f32(_mm_min_ss(_mm_set_ss(first), _mm_set_ss(second)))
    }
    #[cfg(not(target_arch = "x86_64"))]
    {
        if first < second { first } else { second }
    }
}

#[inline]
pub(crate) fn smax(first: f32, second: f32) -> f32 {
    #[cfg(target_arch = "x86_64")]
    unsafe {
        use core::arch::x86_64::{_mm_cvtss_f32, _mm_max_ss, _mm_set_ss};
        _mm_cvtss_f32(_mm_max_ss(_mm_set_ss(first), _mm_set_ss(second)))
    }
    #[cfg(not(target_arch = "x86_64"))]
    {
        if first > second { first } else { second }
    }
}

// CVTTSS2SI returns integer-indefinite instead of a saturating conversion.
#[inline]
pub(crate) fn cvtt_f32_i32(value: f32) -> i32 {
    #[cfg(target_arch = "x86_64")]
    unsafe {
        use core::arch::x86_64::{_mm_cvttss_si32, _mm_set_ss};
        _mm_cvttss_si32(_mm_set_ss(value))
    }
    #[cfg(not(target_arch = "x86_64"))]
    {
        if !value.is_finite() || !(-2_147_483_648.0..2_147_483_648.0).contains(&value) {
            i32::MIN
        } else {
            value as i32
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::hint::black_box;

    fn compare(first: f32, second: f32) {
        let first = black_box(first);
        let second = black_box(second);
        let minimum = if first < second { first } else { second };
        let maximum = if first > second { first } else { second };
        assert_eq!(smin(first, second).to_bits(), minimum.to_bits());
        assert_eq!(smax(first, second).to_bits(), maximum.to_bits());
        let converted =
            if !first.is_finite() || !(-2_147_483_648.0..2_147_483_648.0).contains(&first) {
                i32::MIN
            } else {
                first as i32
            };
        assert_eq!(cvtt_f32_i32(first), converted);
    }

    fn corpus() {
        let boundaries = [
            0, 0x80000000, 1, 0x80000001, 0x007fffff, 0x00800000, 0x3f800000, 0xbf800000,
            0x4effffff, 0x4f000000, 0xceffffff, 0xcf000000, 0xcf000001, 0x7f800000, 0xff800000,
            0x7fc00001, 0xffc00001, 0x7f800001, 0xff800001,
        ];
        for a in boundaries {
            for b in boundaries {
                compare(f32::from_bits(a), f32::from_bits(b));
            }
        }
        let mut state = 0x2c1f_a931_u32;
        for _ in 0..1_000_000 {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            let first = state;
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            compare(f32::from_bits(first), f32::from_bits(state));
        }
    }

    #[test]
    fn instruction_results_match_scalar_bits() {
        corpus();
    }

    #[test]
    fn instruction_results_match_scalar_bits_with_simulation_ftz() {
        let _environment = crate::float_env::FloatEnvGuard::enter()
            .unwrap_or_else(|_| panic!("Unsupported floating point environment"));
        corpus();
    }
}
