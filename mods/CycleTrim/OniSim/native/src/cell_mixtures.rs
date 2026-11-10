//! Sparse component lists addressed directly by bounded simulation cell indices.
//! Keep only populated lists dense; a cell lookup does not hash its integer key.

use crate::status::{Error, Result, Status};

type Components = Vec<(u16, f32)>;
const ABSENT: u32 = u32::MAX;

#[derive(Clone)]
pub(crate) struct CellMixtures {
    slots: Vec<u32>,
    entries: Vec<(usize, Components)>,
}

impl CellMixtures {
    pub(crate) fn new(cells: usize) -> Result<Self> {
        if cells >= ABSENT as usize {
            return Err(Error::new(Status::ResourceLimit, "too many mixture cells"));
        }
        let mut slots = Vec::new();
        slots.try_reserve_exact(cells).map_err(|_| {
            Error::new(
                Status::ResourceLimit,
                "could not reserve mixture cell index",
            )
        })?;
        slots.resize(cells, ABSENT);
        Ok(Self {
            slots,
            entries: Vec::new(),
        })
    }

    pub(crate) fn get(&self, cell: &usize) -> Option<&Components> {
        let slot = *self.slots.get(*cell)? as usize;
        self.entries.get(slot).map(|entry| &entry.1)
    }

    pub(crate) fn get_mut(&mut self, cell: &usize) -> Option<&mut Components> {
        let slot = *self.slots.get(*cell)? as usize;
        self.entries.get_mut(slot).map(|entry| &mut entry.1)
    }

    pub(crate) fn contains_key(&self, cell: &usize) -> bool {
        self.slots.get(*cell).is_some_and(|&slot| slot != ABSENT)
    }

    pub(crate) fn insert(&mut self, cell: usize, value: Components) -> Option<Components> {
        let slot = self.slots[cell];
        if slot != ABSENT {
            return Some(std::mem::replace(&mut self.entries[slot as usize].1, value));
        }
        self.slots[cell] = self.entries.len() as u32;
        self.entries.push((cell, value));
        None
    }

    pub(crate) fn remove(&mut self, cell: &usize) -> Option<Components> {
        let slot = *self.slots.get(*cell)?;
        if slot == ABSENT {
            return None;
        }
        self.slots[*cell] = ABSENT;
        let (_, value) = self.entries.swap_remove(slot as usize);
        if let Some((moved, _)) = self.entries.get(slot as usize) {
            self.slots[*moved] = slot;
        }
        Some(value)
    }

    pub(crate) fn entry(&mut self, cell: usize) -> Entry<'_> {
        Entry { map: self, cell }
    }

    pub(crate) fn keys(&self) -> impl Iterator<Item = &usize> {
        self.entries.iter().map(|entry| &entry.0)
    }
}

pub(crate) struct Entry<'a> {
    map: &'a mut CellMixtures,
    cell: usize,
}

impl<'a> Entry<'a> {
    pub(crate) fn or_default(self) -> &'a mut Components {
        if self.map.slots[self.cell] == ABSENT {
            self.map.insert(self.cell, Vec::new());
        }
        &mut self.map.entries[self.map.slots[self.cell] as usize].1
    }
}

impl std::ops::Index<&usize> for CellMixtures {
    type Output = Components;
    fn index(&self, cell: &usize) -> &Components {
        self.get(cell).expect("cell has no additional components")
    }
}

impl<'a> IntoIterator for &'a mut CellMixtures {
    type Item = (&'a usize, &'a mut Components);
    type IntoIter = std::iter::Map<
        std::slice::IterMut<'a, (usize, Components)>,
        fn(&'a mut (usize, Components)) -> (&'a usize, &'a mut Components),
    >;

    fn into_iter(self) -> Self::IntoIter {
        self.entries
            .iter_mut()
            .map(|entry| (&entry.0, &mut entry.1))
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::collections::HashMap;

    #[test]
    fn indexed_components_match_map_after_insert_swap_remove_and_mutation() {
        let mut indexed = CellMixtures::new(257).unwrap();
        let mut reference = HashMap::new();
        let mut seed = 17u64;
        for step in 0..20_000 {
            seed = seed.wrapping_mul(6364136223846793005).wrapping_add(1);
            let cell = ((seed >> 32) % 257) as usize;
            let value = ((seed >> 16) as u16, step as f32 * 0.001);
            match seed & 3 {
                0 => assert_eq!(
                    indexed.insert(cell, vec![value]),
                    reference.insert(cell, vec![value])
                ),
                1 => assert_eq!(indexed.remove(&cell), reference.remove(&cell)),
                2 => {
                    indexed.entry(cell).or_default().push(value);
                    reference.entry(cell).or_default().push(value);
                }
                _ => {
                    if let Some(list) = indexed.get_mut(&cell) {
                        list.clear();
                    }
                    if let Some(list) = reference.get_mut(&cell) {
                        list.clear();
                    }
                }
            }
            if step % 31 == 0 {
                for key in 0..=257 {
                    assert_eq!(
                        indexed.get(&key),
                        reference.get(&key),
                        "step={step}, cell={key}"
                    );
                    assert_eq!(indexed.contains_key(&key), reference.contains_key(&key));
                }
            }
        }
        let mut keys = indexed.keys().copied().collect::<Vec<_>>();
        keys.sort_unstable();
        let mut expected = reference.keys().copied().collect::<Vec<_>>();
        expected.sort_unstable();
        assert_eq!(keys, expected);
        for (&cell, components) in &mut indexed {
            components.push((99, 1.0));
            reference.get_mut(&cell).unwrap().push((99, 1.0));
        }
        let copy = indexed.clone();
        for cell in expected {
            assert_eq!(&copy[&cell], &reference[&cell]);
        }
    }

    #[test]
    #[ignore = "CPU lookup microbenchmark; not an in-game FPS claim"]
    fn lookup_cost() {
        use std::{hint::black_box, time::Instant};
        const CELLS: usize = 225828;
        let mut indexed = CellMixtures::new(CELLS).unwrap();
        let mut reference = HashMap::new();
        for cell in (0..CELLS).step_by(3) {
            let value = vec![(3, 0.1), (4, 0.2), (5, 0.3)];
            indexed.insert(cell, value.clone());
            reference.insert(cell, value);
        }
        for _ in 0..3 {
            let start = Instant::now();
            let mut sum_a = 0;
            for _ in 0..20 {
                for cell in 0..CELLS {
                    sum_a += black_box(reference.get(black_box(&cell))).map_or(0, Vec::len);
                }
            }
            let map_ms = start.elapsed().as_secs_f64() * 1000.0;
            let start = Instant::now();
            let mut sum_b = 0;
            for _ in 0..20 {
                for cell in 0..CELLS {
                    sum_b += black_box(indexed.get(black_box(&cell))).map_or(0, Vec::len);
                }
            }
            assert_eq!(sum_a, sum_b);
            eprintln!(
                "mixture lookups: hash={map_ms:.3}ms indexed={:.3}ms",
                start.elapsed().as_secs_f64() * 1000.0
            );
        }
    }
}
