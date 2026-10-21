//! Exact integer-only table-index validation. x86-64 guarantees SSE2.

#[inline]
pub(crate) fn valid_u16<const NULL_ALLOWED: bool>(values: &[u16], count: usize) -> bool {
    if count > u16::MAX as usize {
        return true;
    }
    if count == 0 {
        return values.iter().all(|&id| NULL_ALLOWED && id == u16::MAX);
    }
    #[cfg(target_arch = "x86_64")]
    {
        use std::arch::x86_64::*;
        let (chunks, tail) = values.as_chunks::<8>();
        // Every chunk contains exactly 16 readable bytes. loadu permits any
        // slice alignment; all arithmetic is integer and leaves MXCSR alone.
        unsafe {
            let maximum = _mm_set1_epi16((count - 1) as i16);
            let zero = _mm_setzero_si128();
            let null = _mm_set1_epi16(-1);
            for chunk in chunks {
                let ids = _mm_loadu_si128(chunk.as_ptr().cast());
                let mut valid = _mm_cmpeq_epi16(_mm_subs_epu16(ids, maximum), zero);
                if NULL_ALLOWED {
                    valid = _mm_or_si128(valid, _mm_cmpeq_epi16(ids, null));
                }
                if _mm_movemask_epi8(valid) != 0xffff {
                    return false;
                }
            }
        }
        tail.iter()
            .all(|&id| (id as usize) < count || (NULL_ALLOWED && id == u16::MAX))
    }
    #[cfg(not(target_arch = "x86_64"))]
    values
        .iter()
        .all(|&id| (id as usize) < count || (NULL_ALLOWED && id == u16::MAX))
}

#[inline]
pub(crate) fn valid_u8(values: &[u8], count: usize) -> bool {
    if count >= u8::MAX as usize {
        return true;
    }
    if count == 0 {
        return values.iter().all(|&id| id == u8::MAX);
    }
    #[cfg(target_arch = "x86_64")]
    {
        use std::arch::x86_64::*;
        let (chunks, tail) = values.as_chunks::<16>();
        // as_chunks guarantees the entire unaligned 16-byte read is in bounds.
        unsafe {
            let maximum = _mm_set1_epi8((count - 1) as i8);
            let zero = _mm_setzero_si128();
            let null = _mm_set1_epi8(-1);
            for chunk in chunks {
                let ids = _mm_loadu_si128(chunk.as_ptr().cast());
                let valid = _mm_or_si128(
                    _mm_cmpeq_epi8(_mm_subs_epu8(ids, maximum), zero),
                    _mm_cmpeq_epi8(ids, null),
                );
                if _mm_movemask_epi8(valid) != 0xffff {
                    return false;
                }
            }
        }
        tail.iter()
            .all(|&id| (id as usize) < count || id == u8::MAX)
    }
    #[cfg(not(target_arch = "x86_64"))]
    values
        .iter()
        .all(|&id| (id as usize) < count || id == u8::MAX)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn check_u16<const N: bool>(values: &[u16], count: usize) {
        assert_eq!(
            valid_u16::<N>(values, count),
            values
                .iter()
                .all(|&id| (id as usize) < count || (N && id == u16::MAX)),
            "count={count} null={N} values={values:?}"
        );
    }

    #[test]
    fn every_u16_word_and_boundary_count() {
        for count in [
            0,
            1,
            2,
            127,
            255,
            256,
            32767,
            32768,
            65534,
            65535,
            65536,
            usize::MAX,
        ] {
            for id in 0..=u16::MAX {
                check_u16::<false>(&[id; 8], count);
                check_u16::<true>(&[id; 8], count);
            }
        }
        for count in 0usize..=65537 {
            for id in [
                0,
                1,
                count.saturating_sub(1).min(65535) as u16,
                count.min(65535) as u16,
                count.saturating_add(1).min(65535) as u16,
                u16::MAX,
            ] {
                check_u16::<false>(&[id; 8], count);
                check_u16::<true>(&[id; 8], count);
            }
        }
    }

    #[test]
    fn every_u8_word_and_count() {
        for count in (0..=257).chain([usize::MAX]) {
            for id in 0..=u8::MAX {
                assert_eq!(
                    valid_u8(&[id; 16], count),
                    id == u8::MAX || (id as usize) < count
                );
            }
        }
    }

    #[test]
    fn vector_boundaries_tails_and_unaligned_slices() {
        for len in 0..=49 {
            for offset in 0..16 {
                let mut words = vec![0u16; len + offset];
                let mut bytes = vec![0u8; len + offset];
                check_u16::<false>(&words[offset..], 1);
                check_u16::<true>(&words[offset..], 1);
                assert!(valid_u8(&bytes[offset..], 1));
                for pos in 0..len {
                    for bad in [1u16, 32768, u16::MAX] {
                        words[offset + pos] = bad;
                        check_u16::<false>(&words[offset..], 1);
                        check_u16::<true>(&words[offset..], 1);
                    }
                    words[offset + pos] = 0;
                    for bad in [1u8, 128, u8::MAX] {
                        bytes[offset + pos] = bad;
                        assert_eq!(valid_u8(&bytes[offset..], 1), bad == u8::MAX);
                    }
                    bytes[offset + pos] = 0;
                }
            }
        }
    }
}
