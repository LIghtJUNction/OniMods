//! Flow image calculation. No physics state is modified by this pass.

#[cfg(target_arch = "x86_64")]
#[path = "presentation_flow_sse2.rs"]
mod simd;

pub fn update(
    output: &mut [[f32; 2]],
    width: usize,
    height: usize,
    old_element: &[u16],
    element: &[u16],
    mass: &[f32],
    flow: &[[f32; 4]],
) {
    #[cfg(target_arch = "x86_64")]
    if simd::exceptions_masked() {
        update_rows(
            output,
            width,
            height,
            old_element,
            element,
            mass,
            flow,
            true,
        );
        return;
    }
    update_scalar(output, width, height, old_element, element, mass, flow);
}

/// Retained scalar production fallback and incremental benchmark baseline.
pub fn update_scalar(
    output: &mut [[f32; 2]],
    width: usize,
    height: usize,
    old_element: &[u16],
    element: &[u16],
    mass: &[f32],
    flow: &[[f32; 4]],
) {
    update_rows(
        output,
        width,
        height,
        old_element,
        element,
        mass,
        flow,
        false,
    );
}

#[allow(clippy::too_many_arguments)]
fn update_rows(
    output: &mut [[f32; 2]],
    width: usize,
    height: usize,
    old_element: &[u16],
    element: &[u16],
    mass: &[f32],
    flow: &[[f32; 4]],
    vectorize: bool,
) {
    let stride = width + 2;
    assert_eq!(output.len(), width * height);
    for y in 0..height {
        let start = (y + 1) * stride + 1;
        let range = start..start + width;
        let old = &old_element[range.clone()];
        let new = &element[range.clone()];
        let mass = &mass[range.clone()];
        let flow = &flow[range];
        let out = &mut output[y * width..(y + 1) * width];
        #[cfg(target_arch = "x86_64")]
        let start = if vectorize {
            simd::row(out, old, new, mass, flow)
        } else {
            0
        };
        #[cfg(not(target_arch = "x86_64"))]
        let start = {
            let _ = vectorize;
            0
        };
        if start < width {
            scalar_row(
                &mut out[start..],
                &old[start..],
                &new[start..],
                &mass[start..],
                &flow[start..],
            );
        }
    }
}

// Both the scalar baseline and NaN fallback execute one instruction sequence:
// LLVM otherwise chooses different COMISS/UCOMISS predicates at inline sites,
// changing MXCSR's invalid-operation flag for a quiet NaN mass.
#[inline(never)]
fn scalar_row(output: &mut [[f32; 2]], old: &[u16], new: &[u16], mass: &[f32], flow: &[[f32; 4]]) {
    for x in 0..output.len() {
        let scale = if old[x] != new[x] {
            0.0
        } else if mass[x] > 1.0 {
            1.0 / mass[x]
        } else {
            1.0
        };
        let [right, left, down, up] = flow[x];
        output[x] = [(right - left) * scale, (up - down) * scale];
    }
}

#[cfg(test)]
mod tests {
    use super::update;

    fn check(width: usize, height: usize, arbitrary_bits: bool) {
        let count = (width + 2) * (height + 2);
        let mut state = 0x1234_5678_u32;
        let mut next = || {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            state
        };
        let old: Vec<u16> = (0..count).map(|_| next() as u16).collect();
        let new: Vec<_> = old
            .iter()
            .map(|&v| if next() % 7 == 0 { v ^ 1 } else { v })
            .collect();
        let mut number = || {
            let bits = next();
            if arbitrary_bits {
                f32::from_bits(bits)
            } else {
                (bits % 100_000) as f32 / 31.0
            }
        };
        let mass: Vec<_> = (0..count).map(|_| number()).collect();
        let flow: Vec<[f32; 4]> = (0..count)
            .map(|_| std::array::from_fn(|_| number()))
            .collect();
        let mut expected = vec![[0.0; 2]; width * height];
        // Exact original production loop, including padded-grid mapping.
        for (public, value) in expected.iter_mut().enumerate() {
            let cell = (public / width + 1) * (width + 2) + public % width + 1;
            let scale = if old[cell] != new[cell] {
                0.0
            } else if mass[cell] > 1.0 {
                1.0 / mass[cell]
            } else {
                1.0
            };
            let [right, left, down, up] = flow[cell];
            *value = [(right - left) * scale, (up - down) * scale];
        }
        let mut actual = vec![[f32::NAN; 2]; expected.len()];
        update(&mut actual, width, height, &old, &new, &mass, &flow);
        for (index, (expected, actual)) in expected
            .iter()
            .flatten()
            .zip(actual.iter().flatten())
            .enumerate()
        {
            assert_eq!(
                expected.to_bits(),
                actual.to_bits(),
                "{width}x{height} output word {index}"
            );
        }
    }

    #[test]
    fn matches_original_padded_layout_and_transitions() {
        for (width, height) in [(1, 1), (3, 5), (17, 19), (96, 96), (256, 384)] {
            check(width, height, false);
        }
    }

    #[test]
    fn matches_original_arbitrary_float_words() {
        for (width, height) in [(3, 5), (17, 19), (256, 384)] {
            check(width, height, true);
        }
    }

    #[test]
    #[cfg(target_arch = "x86_64")]
    fn matches_original_with_worker_flush_to_zero() {
        struct Restore(u32);
        impl Drop for Restore {
            fn drop(&mut self) {
                unsafe {
                    std::arch::asm!("ldmxcsr [{p}]", p = in(reg) &self.0,
                        options(nostack, preserves_flags));
                }
            }
        }
        let mut saved = 0_u32;
        unsafe {
            std::arch::asm!("stmxcsr [{p}]", p = in(reg) &mut saved,
                options(nostack, preserves_flags));
        }
        let _restore = Restore(saved);
        // Match the real simulation worker: enable FTZ, retain inherited DAZ,
        // rounding and exception controls, and restore even if an assertion fails.
        let enabled = saved | 0x8000;
        unsafe {
            std::arch::asm!("ldmxcsr [{p}]", p = in(reg) &enabled,
                options(nostack, preserves_flags));
        }
        check(17, 19, true);
        check(256, 384, true);
    }

    #[test]
    #[cfg(target_arch = "x86_64")]
    fn simd_preserves_edge_words_and_mxcsr_effects() {
        fn csr() -> u32 {
            let mut value = 0;
            unsafe {
                std::arch::asm!("stmxcsr [{p}]", p = in(reg) &mut value, options(nostack, preserves_flags));
            }
            value
        }
        fn install(value: u32) {
            unsafe {
                std::arch::asm!("ldmxcsr [{p}]", p = in(reg) &value, options(nostack, preserves_flags));
            }
        }
        struct Restore(u32);
        impl Drop for Restore {
            fn drop(&mut self) {
                install(self.0);
            }
        }
        let saved = csr();
        let _restore = Restore(saved);
        let edge = [
            0, 0x80000000, 0x3f800000, 0xbf800000, 1, 0x00800000, 0x7f7fffff, 0x7f800000,
            0xff800000, 0x7fc01234, 0xffc05678, 0x7f801234,
        ];
        let old = [1_u16; 18];
        let mut new = old;
        let mut mass = [0.0; 18];
        let mut values = [[0.0; 4]; 18];
        let mut expected = [[0.0; 2]; 4];
        let mut actual = expected;
        for controls in 0..16 {
            let mode = ((saved | 0x1f80) & !0xe07f)
                | ((controls & 3) << 13)
                | ((controls & 4) << 13)
                | ((controls & 8) << 3);
            for (i, &a) in edge.iter().enumerate() {
                for (j, &b) in edge.iter().enumerate() {
                    for &m in &edge {
                        for changed in [0_u8, 5, 15] {
                            for lane in 0..4 {
                                let cell = 7 + lane;
                                new[cell] = 1 ^ ((changed >> lane) & 1) as u16;
                                mass[cell] = f32::from_bits(m);
                                values[cell] = [
                                    f32::from_bits(a),
                                    f32::from_bits(b),
                                    f32::from_bits(edge[(i + lane) % edge.len()]),
                                    f32::from_bits(edge[(j + lane) % edge.len()]),
                                ];
                            }
                            install(mode);
                            super::update_scalar(
                                std::hint::black_box(&mut expected),
                                4,
                                1,
                                std::hint::black_box(&old),
                                std::hint::black_box(&new),
                                std::hint::black_box(&mass),
                                std::hint::black_box(&values),
                            );
                            let expected_csr = csr();
                            install(mode);
                            update(
                                std::hint::black_box(&mut actual),
                                4,
                                1,
                                std::hint::black_box(&old),
                                std::hint::black_box(&new),
                                std::hint::black_box(&mass),
                                std::hint::black_box(&values),
                            );
                            let actual_csr = csr();
                            assert_eq!(
                                expected_csr, actual_csr,
                                "MXCSR controls={controls} a={a:x} b={b:x} m={m:x} changed={changed}"
                            );
                            for (expected, actual) in
                                expected.iter().flatten().zip(actual.iter().flatten())
                            {
                                assert_eq!(
                                    expected.to_bits(),
                                    actual.to_bits(),
                                    "output controls={controls} a={a:x} b={b:x} m={m:x} changed={changed}"
                                );
                            }
                        }
                    }
                }
            }
        }
    }
}
