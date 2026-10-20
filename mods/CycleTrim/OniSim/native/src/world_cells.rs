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
}
