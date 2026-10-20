//! Reproduction of the installed libstdc++ ordering used for SpawnOreInfo.
//!
//! The official comparator considers only (cell_idx, elem_idx). Tied records
//! therefore move according to the exact sort implementation; later reduction
//! preserves the first record's disease fields.

use crate::event_records::SpawnOreInfo;

const RECORD_BYTES: usize = 20;
const INSERTION_SORT_BYTES: usize = 0x141;

#[inline]
fn key(value: SpawnOreInfo) -> (i32, u16) {
    (value.cell_idx, value.elem_idx)
}

#[inline]
fn less(left: SpawnOreInfo, right: SpawnOreInfo) -> bool {
    key(left) < key(right)
}

#[inline]
fn less_equal(left: SpawnOreInfo, right: SpawnOreInfo) -> bool {
    key(left) <= key(right)
}

/// Match libstdc++ `std::sort` for this fixed 20-byte record and comparator.
pub(crate) fn sort_spawn_ore(records: &mut [SpawnOreInfo]) {
    if records.len() < 2 {
        return;
    }
    let depth_limit = 2 * (usize::BITS as usize - 1 - records.len().leading_zeros() as usize);
    introsort(records, depth_limit);
    final_insertion_sort(records);
}

fn introsort(records: &mut [SpawnOreInfo], mut depth_limit: usize) {
    let first = 0;
    let mut last = records.len();
    while (last - first) * RECORD_BYTES >= INSERTION_SORT_BYTES {
        if depth_limit == 0 {
            heap_sort(&mut records[first..last]);
            return;
        }
        depth_limit -= 1;

        let pivot = median_to_first(records, first, last);
        let cut = unguarded_partition(records, first, last, pivot);

        // FUN_00156fa0 recurses into the right partition, then tail-recurses left.
        introsort(&mut records[cut..last], depth_limit);
        last = cut;
    }
}

fn median_to_first(records: &mut [SpawnOreInfo], first: usize, last: usize) -> SpawnOreInfo {
    // FUN_00156fa0 samples first+1, midpoint, and last-1. Its strict
    // comparisons choose the same median, then exchange it with first.
    let a = first + 1;
    let b = first + (last - first) / 2;
    let c = last - 1;
    let ka = key(records[a]);
    let kb = key(records[b]);
    let kc = key(records[c]);
    let median = if ka < kb {
        if kc <= kb {
            if ka < kc {
                c
            } else {
                a
            }
        } else {
            b
        }
    } else if ka < kc {
        a
    } else if kc <= kb {
        b
    } else {
        c
    };
    records.swap(first, median);
    records[first]
}

fn unguarded_partition(
    records: &mut [SpawnOreInfo],
    first: usize,
    last: usize,
    pivot: SpawnOreInfo,
) -> usize {
    let pivot_key = key(pivot);
    // The pivot occupies `first`; the forward scan begins at its successor.
    let mut left = first + 1;
    let mut right = last;
    loop {
        while key(records[left]) < pivot_key {
            left += 1;
        }
        right -= 1;
        while pivot_key < key(records[right]) {
            right -= 1;
        }
        if right <= left {
            return left;
        }
        records.swap(left, right);
        left += 1;
    }
}

fn final_insertion_sort(records: &mut [SpawnOreInfo]) {
    let threshold = (INSERTION_SORT_BYTES - 1) / RECORD_BYTES;
    if records.len() > threshold {
        insertion_sort(&mut records[..threshold]);
        for index in threshold..records.len() {
            unguarded_linear_insert(records, index);
        }
    } else {
        insertion_sort(records);
    }
}

fn insertion_sort(records: &mut [SpawnOreInfo]) {
    for index in 1..records.len() {
        let value = records[index];
        if less(value, records[0]) {
            records.copy_within(0..index, 1);
            records[0] = value;
        } else {
            let mut hole = index;
            while less(value, records[hole - 1]) {
                records[hole] = records[hole - 1];
                hole -= 1;
            }
            records[hole] = value;
        }
    }
}

fn unguarded_linear_insert(records: &mut [SpawnOreInfo], index: usize) {
    let value = records[index];
    let mut hole = index;
    while less(value, records[hole - 1]) {
        records[hole] = records[hole - 1];
        hole -= 1;
    }
    records[hole] = value;
}

fn heap_sort(records: &mut [SpawnOreInfo]) {
    let len = records.len();
    if len < 2 {
        return;
    }
    for parent in (0..len / 2).rev() {
        let value = records[parent];
        adjust_heap(records, parent, len, value);
    }
    for end in (1..len).rev() {
        let value = records[end];
        records[end] = records[0];
        adjust_heap(records, 0, end, value);
    }
}

/// libstdc++'s adjust_heap: move the larger child upward, preferring the right
/// child on equal keys, then push the saved value back toward the root.
fn adjust_heap(records: &mut [SpawnOreInfo], top: usize, len: usize, value: SpawnOreInfo) {
    let mut hole = top;
    let last_parent = (len - 1) / 2;
    while hole < last_parent {
        let right = 2 * (hole + 1);
        let left = right - 1;
        let child = if less_equal(records[left], records[right]) {
            right
        } else {
            left
        };
        records[hole] = records[child];
        hole = child;
    }

    // An even-sized heap has one final left child below its last parent.
    if len % 2 == 0 && (len - 2) / 2 == hole {
        let child = 2 * hole + 1;
        records[hole] = records[child];
        hole = child;
    }

    while hole > top {
        let parent = (hole - 1) / 2;
        if less_equal(value, records[parent]) {
            break;
        }
        records[hole] = records[parent];
        hole = parent;
    }
    records[hole] = value;
}
