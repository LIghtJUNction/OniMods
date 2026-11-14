use crate::status::{Error, Result, Status};

pub(crate) fn filled<T: Clone>(length: usize, value: T) -> Result<Vec<T>> {
    let mut values = Vec::new();
    values.try_reserve_exact(length).map_err(|_| {
        Error::new(
            Status::ResourceLimit,
            "could not allocate padded physics storage",
        )
    })?;
    values.resize(length, value);
    Ok(values)
}

#[cfg(test)]
mod tests {
    use super::Cells;

    #[test]
    fn region_copy_matches_full_copy_when_only_region_changed() {
        let stride = 13;
        for (x0, x1, y0, y1) in [(1, 12, 1, 8), (1, 2, 1, 2), (5, 9, 2, 6), (4, 4, 2, 6)] {
            let mut write = Cells::new(stride * 9).unwrap();
            let mut partial = write.clone();
            for y in y0..y1 { for x in x0..x1 {
                let i = y * stride + x;
                write.element[i] = i as u16;
                write.temperature[i] = i as f32 + 0.5;
                write.mass[i] = i as f32;
                write.properties[i] = 1;
                write.insulation[i] = 2;
                write.strength[i] = 3;
                write.disease_idx[i] = 4;
                write.disease_count[i] = i as i32;
                write.disease_infestation[i] = 5;
                write.disease_growth[i] = 6.0;
                write.radiation[i] = 7.0;
            }}
            partial.copy_region_from(&write, stride, x0, x1, y0, y1);
            macro_rules! check { ($($field:ident),+) => { $(assert_eq!(partial.$field, write.$field);)+ }; }
            check!(element, temperature, mass, properties, insulation, strength,
                disease_idx, disease_count, disease_infestation, disease_growth, radiation);
        }
    }
}

// These eleven arrays follow CellSOA's field order, not its C++ vector layout.
#[derive(Clone)]
pub(crate) struct Cells {
    pub(crate) element: Vec<u16>,
    pub(crate) temperature: Vec<f32>,
    pub(crate) mass: Vec<f32>,
    pub(crate) properties: Vec<u8>,
    pub(crate) insulation: Vec<u8>,
    pub(crate) strength: Vec<u8>,
    pub(crate) disease_idx: Vec<u8>,
    pub(crate) disease_count: Vec<i32>,
    pub(crate) disease_infestation: Vec<u8>,
    pub(crate) disease_growth: Vec<f32>,
    pub(crate) radiation: Vec<f32>,
}

impl Cells {
    pub(crate) fn new(length: usize) -> Result<Self> {
        // Official CellSOA constructor, ELF 0x3b2d0, memset 0x3b4b5..0x3b5a8.
        Ok(Self {
            element: filled(length, u16::MAX)?,
            temperature: filled(length, 0.0)?,
            mass: filled(length, 0.0)?,
            properties: filled(length, 0)?,
            insulation: filled(length, u8::MAX)?,
            strength: filled(length, 0x84)?,
            disease_idx: filled(length, u8::MAX)?,
            disease_count: filled(length, 0)?,
            disease_infestation: filled(length, 0)?,
            disease_growth: filled(length, 0.0)?,
            radiation: filled(length, 0.0)?,
        })
    }

    pub(crate) fn copy_from(&mut self, other: &Self) {
        if self.element.len() >= 32768
            && let Some(pool) = crate::parallel::pool(self.element.len())
        {
            pool.install(|| {
                rayon::scope(|s| {
                    s.spawn(|_| {
                        self.element.copy_from_slice(&other.element);
                        self.temperature.copy_from_slice(&other.temperature);
                        self.mass.copy_from_slice(&other.mass);
                    });
                    s.spawn(|_| {
                        self.properties.copy_from_slice(&other.properties);
                        self.insulation.copy_from_slice(&other.insulation);
                        self.strength.copy_from_slice(&other.strength);
                    });
                    s.spawn(|_| {
                        self.disease_idx.copy_from_slice(&other.disease_idx);
                        self.disease_count.copy_from_slice(&other.disease_count);
                        self.disease_infestation
                            .copy_from_slice(&other.disease_infestation);
                    });
                    self.disease_growth.copy_from_slice(&other.disease_growth);
                    self.radiation.copy_from_slice(&other.radiation);
                });
            });
            return;
        }
        self.element.copy_from_slice(&other.element);
        self.temperature.copy_from_slice(&other.temperature);
        self.mass.copy_from_slice(&other.mass);
        self.properties.copy_from_slice(&other.properties);
        self.insulation.copy_from_slice(&other.insulation);
        self.strength.copy_from_slice(&other.strength);
        self.disease_idx.copy_from_slice(&other.disease_idx);
        self.disease_count.copy_from_slice(&other.disease_count);
        self.disease_infestation
            .copy_from_slice(&other.disease_infestation);
        self.disease_growth.copy_from_slice(&other.disease_growth);
        self.radiation.copy_from_slice(&other.radiation);
    }

    /// Fluid passes change material and disease fields but not cell properties.
    pub(crate) fn copy_fluid_from(&mut self, other: &Self) {
        #[cfg(test)]
        {
            assert_eq!(self.properties, other.properties);
            assert_eq!(self.insulation, other.insulation);
            assert_eq!(self.strength, other.strength);
            assert_eq!(self.radiation, other.radiation);
        }
        self.element.copy_from_slice(&other.element);
        self.temperature.copy_from_slice(&other.temperature);
        self.mass.copy_from_slice(&other.mass);
        self.disease_idx.copy_from_slice(&other.disease_idx);
        self.disease_count.copy_from_slice(&other.disease_count);
        self.disease_infestation.copy_from_slice(&other.disease_infestation);
        self.disease_growth.copy_from_slice(&other.disease_growth);
    }

    /// Copy a rectangular portion of the padded grid after a region-local pass.
    pub(crate) fn copy_region_from(&mut self, other: &Self, stride: usize,
        min_x: usize, max_x: usize, min_y: usize, max_y: usize) {
        macro_rules! copy_field {
            ($field:ident) => {
                for y in min_y..max_y {
                    let range = y * stride + min_x..y * stride + max_x;
                    self.$field[range.clone()].copy_from_slice(&other.$field[range]);
                }
            };
        }
        copy_field!(element);
        copy_field!(temperature);
        copy_field!(mass);
        copy_field!(properties);
        copy_field!(insulation);
        copy_field!(strength);
        copy_field!(disease_idx);
        copy_field!(disease_count);
        copy_field!(disease_infestation);
        copy_field!(disease_growth);
        copy_field!(radiation);
    }

    /// Copy fluid fields for a rectangular region.
    pub(crate) fn copy_fluid_region_from(&mut self, other: &Self, stride: usize,
        min_x: usize, max_x: usize, min_y: usize, max_y: usize) {
        macro_rules! copy_field {
            ($field:ident) => {
                for y in min_y..max_y {
                    let range = y * stride + min_x..y * stride + max_x;
                    self.$field[range.clone()].copy_from_slice(&other.$field[range]);
                }
            };
        }
        copy_field!(element);
        copy_field!(temperature);
        copy_field!(mass);
        copy_field!(disease_idx);
        copy_field!(disease_count);
        copy_field!(disease_infestation);
        copy_field!(disease_growth);
    }
}
