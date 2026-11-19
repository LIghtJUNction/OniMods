//! Conservative admission for the existing sparse-face CPU thermal prepass.
//!
//! A deferred gas cell must have a nonzero region-map entry. Rectangle areas
//! therefore bound the eligible count without reading any per-cell state.
//! Overlaps intentionally overestimate: they can only retain an unnecessary
//! prepass, never reject a CPU path that the original predicate could accept.

use crate::world::Region;

pub(super) fn minimum_deferred_cells(padded_cells: usize) -> usize {
    padded_cells / 2
}

/// These bounds are also used to construct the GPU's deferred-region map.
pub(super) fn clipped_bounds(
    region: &Region,
    width: usize,
    height: usize,
) -> Option<[usize; 4]> {
    let min_x = region.min_x.max(1) as usize;
    let min_y = region.min_y.max(1) as usize;
    let max_x = (region.max_x as usize).min(width.saturating_add(1));
    let max_y = (region.max_y as usize).min(height.saturating_add(1));
    if max_x.saturating_sub(min_x) < 2 || max_y.saturating_sub(min_y) < 2 {
        return None;
    }
    Some([min_x, min_y, max_x, max_y])
}

pub(super) fn cpu_prepass_can_succeed(
    regions: &[Region],
    width: usize,
    height: usize,
    padded_cells: usize,
) -> bool {
    let required = minimum_deferred_cells(padded_cells);
    if required == 0 {
        return true;
    }
    let mut upper_bound = 0usize;
    for region in regions {
        if let Some([min_x, min_y, max_x, max_y]) = clipped_bounds(region, width, height) {
            // Saturation overestimates just like overlapping rectangles. In
            // particular, overflow must never turn a large region into a skip.
            let area = (max_x - min_x).saturating_mul(max_y - min_y);
            upper_bound = upper_bound.saturating_add(area);
            if upper_bound >= required {
                return true;
            }
        }
    }
    false
}

#[cfg(test)]
mod tests {
    use super::*;

    fn region(min_x: i32, min_y: i32, max_x: i32, max_y: i32) -> Region {
        Region { min_x, min_y, max_x, max_y, ..Region::default() }
    }

    #[test]
    fn thermal_region_gate_preserves_clipping_and_degenerate_bounds() {
        assert_eq!(clipped_bounds(&region(-2, 0, 40, 30), 8, 6), Some([1, 1, 9, 7]));
        assert_eq!(clipped_bounds(&region(2, 3, 4, 5), 8, 6), Some([2, 3, 4, 5]));
        for r in [
            region(1, 1, 2, 7), region(1, 1, 9, 2),
            region(3, 3, 3, 5), region(5, 3, 4, 5),
            region(9, 1, 20, 7), region(1, 7, 9, 20),
        ] {
            assert_eq!(clipped_bounds(&r, 8, 6), None);
        }
    }

    #[test]
    fn thermal_region_gate_preserves_integer_threshold_and_overlap() {
        let four = region(1, 1, 3, 3);
        assert_eq!(minimum_deferred_cells(9), 4);
        assert!(cpu_prepass_can_succeed(&[four], 8, 6, 9));
        assert!(!cpu_prepass_can_succeed(&[four], 8, 6, 10));
        // The union is four cells, but counting overlapping regions twice is
        // deliberately conservative and may retain the original scan.
        assert!(cpu_prepass_can_succeed(&[four, four], 8, 6, 12));
        assert!(!cpu_prepass_can_succeed(&[], 8, 6, 80));
        assert!(cpu_prepass_can_succeed(&[], 0, 0, 0));
        let large = region(1, 1, i32::MAX, i32::MAX);
        assert!(cpu_prepass_can_succeed(&[large; 5], usize::MAX, usize::MAX, usize::MAX));
        // Preserve the original signed-to-unsigned clipping behavior, while
        // ensuring a synthetic oversized area cannot wrap into a false skip.
        let overflowing = region(1, 1, -1, -1);
        assert!(cpu_prepass_can_succeed(&[overflowing], usize::MAX, usize::MAX, usize::MAX));
    }

    #[test]
    fn thermal_region_gate_never_rejects_sufficient_legacy_map_coverage() {
        for width in 2usize..=6 {
            for height in 2usize..=6 {
                let stride = width + 2;
                let count = stride * (height + 2);
                let mut candidates = Vec::new();
                for left in 0..=width + 1 {
                    for bottom in 0..=height + 1 {
                        for span in [1, 2, 4, 9] {
                            candidates.push(region(left as i32, bottom as i32,
                                (left + span) as i32, (bottom + span) as i32));
                        }
                    }
                }
                candidates.extend([
                    region(-3, -2, 4, 5),
                    region(1, 1, -1, -1),
                    region(4, 4, 2, 2),
                ]);
                // Vary overlap, clipping and a one-cell row/column. This is the
                // old map builder, independent of the shared new bounds helper.
                for (i, first) in candidates.iter().enumerate() {
                    let regions = [*first, candidates[(i * 7 + 3) % candidates.len()]];
                    let mut legacy = vec![0u32; count];
                    let mut current = vec![0u32; count];
                    for (index, r) in regions.iter().enumerate() {
                        let min_x = r.min_x.max(1) as usize;
                        let min_y = r.min_y.max(1) as usize;
                        let max_x = (r.max_x as usize).min(width + 1);
                        let max_y = (r.max_y as usize).min(height + 1);
                        if max_x.saturating_sub(min_x) >= 2 && max_y.saturating_sub(min_y) >= 2 {
                            for y in min_y..max_y {
                                legacy[y * stride + min_x..y * stride + max_x].fill((index + 1) as u32);
                            }
                        }
                        if let Some([min_x, min_y, max_x, max_y]) = clipped_bounds(r, width, height) {
                            for y in min_y..max_y {
                                current[y * stride + min_x..y * stride + max_x].fill((index + 1) as u32);
                            }
                        }
                    }
                    assert_eq!(current, legacy);
                    let maximum_eligible = legacy.iter().filter(|&&group| group != 0).count();
                    if !cpu_prepass_can_succeed(&regions, width, height, count) {
                        assert!(maximum_eligible < count / 2);
                    }
                }
            }
        }
    }
}
