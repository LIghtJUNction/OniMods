use crate::bytes::{f32at, i32at, overlaps, u16at, u32at};
use crate::status::{Error, Result, Status};

pub(crate) const DESCRIPTOR_SIZE: usize = 496;

pub(crate) struct Grid {
    pub(crate) width: i32,
    pub(crate) height: i32,
    pub(crate) seed: u32,
    pub(crate) radiation_enabled: u8,
    pub(crate) headless: u8,
    pub(crate) initialized: bool,
    pub(crate) started: bool,
    pub(crate) element: Box<[u16]>,
    pub(crate) temperature: Box<[f32]>,
    pub(crate) mass: Box<[f32]>,
    pub(crate) properties: Box<[u8]>,
    pub(crate) insulation: Box<[u8]>,
    pub(crate) strength: Box<[u8]>,
    pub(crate) radiation: Box<[f32]>,
    pub(crate) disease_idx: Box<[u8]>,
    pub(crate) disease_count: Box<[i32]>,
    pub(crate) backwall_element: Box<[u16]>,
    pub(crate) backwall_mass: Box<[f32]>,
    pub(crate) backwall_temperature: Box<[f32]>,
    pub(crate) accumulated_flow: Box<[f32]>,
    cell_padding: Box<[[u8; 3]]>,
    disease_reserved: Box<[[u8; 7]]>,
    backwall_padding: Box<[[u8; 2]]>,
    descriptor: Box<[u8; DESCRIPTOR_SIZE]>,
}

fn filled<T: Clone>(count: usize, value: T) -> Result<Box<[T]>> {
    let mut values = Vec::new();
    values.try_reserve_exact(count).map_err(|_| {
        Error::new(
            Status::ResourceLimit,
            "could not allocate native grid storage",
        )
    })?;
    values.resize(count, value);
    Ok(values.into_boxed_slice())
}

impl Grid {
    pub(crate) fn new(width: i32, height: i32, radiation: u8, headless: u8) -> Result<Self> {
        let count = crate::bytes::dimensions(width, height)?;
        let mut grid = Self {
            width,
            height,
            seed: 0,
            radiation_enabled: radiation,
            headless,
            initialized: false,
            started: false,
            element: filled(count, 0)?,
            temperature: filled(count, 0.0)?,
            mass: filled(count, 0.0)?,
            properties: filled(count, 0)?,
            insulation: filled(count, 0)?,
            strength: filled(count, 0)?,
            radiation: filled(count, 0.0)?,
            disease_idx: filled(count, 0)?,
            disease_count: filled(count, 0)?,
            backwall_element: filled(count, 0)?,
            backwall_mass: filled(count, 0.0)?,
            backwall_temperature: filled(count, 0.0)?,
            accumulated_flow: filled(count, 0.0)?,
            cell_padding: filled(count, [0; 3])?,
            disease_reserved: filled(count, [0; 7])?,
            backwall_padding: filled(count, [0; 2])?,
            descriptor: Box::new([0; DESCRIPTOR_SIZE]),
        };
        grid.refresh_descriptor();
        Ok(grid)
    }

    fn refresh_descriptor(&mut self) {
        // Linux x86_64, official Pack=4: i32 then twelve unaligned pointers.
        // All event (29 count/pointer pairs), flow and texture fields stay zero.
        let pointers = [
            self.element.as_ptr() as usize,
            self.temperature.as_ptr() as usize,
            self.mass.as_ptr() as usize,
            self.properties.as_ptr() as usize,
            self.insulation.as_ptr() as usize,
            self.strength.as_ptr() as usize,
            self.radiation.as_ptr() as usize,
            self.disease_idx.as_ptr() as usize,
            self.disease_count.as_ptr() as usize,
            self.backwall_element.as_ptr() as usize,
            self.backwall_mass.as_ptr() as usize,
            self.backwall_temperature.as_ptr() as usize,
        ];
        for (index, pointer) in pointers.iter().enumerate() {
            self.descriptor[4 + index * 8..12 + index * 8]
                .copy_from_slice(&(*pointer as u64).to_le_bytes());
        }
        self.descriptor[448..456]
            .copy_from_slice(&(self.accumulated_flow.as_ptr() as u64).to_le_bytes());
    }

    pub(crate) fn len(&self) -> usize {
        self.element.len()
    }
    pub(crate) fn descriptor(&self) -> *const u8 {
        self.descriptor.as_ptr()
    }

    pub(crate) fn set_num_frames(&mut self, count: i32) {
        self.descriptor[0..4].copy_from_slice(&count.to_le_bytes());
    }

    pub(crate) fn set_event_span(&mut self, slot: usize, count: i32, pointer: usize) {
        let at = 100 + slot * 12;
        self.descriptor[at..at + 4].copy_from_slice(&count.to_le_bytes());
        self.descriptor[at + 4..at + 12].copy_from_slice(&(pointer as u64).to_le_bytes());
    }

    pub(crate) fn set_texture_pointer(&mut self, index: usize, pointer: usize) {
        let at = 456 + index * 8;
        self.descriptor[at..at + 8].copy_from_slice(&(pointer as u64).to_le_bytes());
    }

    pub(crate) fn copy_from(&mut self, other: &Self) {
        assert_eq!((self.width, self.height), (other.width, other.height));
        self.seed = other.seed;
        self.initialized = other.initialized;
        self.started = other.started;
        self.radiation_enabled = other.radiation_enabled;
        self.headless = other.headless;
        macro_rules! copy { ($($field:ident),* $(,)?) => {
            $(self.$field.copy_from_slice(&other.$field);)*
        }; }
        copy!(
            element,
            temperature,
            mass,
            properties,
            insulation,
            strength,
            radiation,
            disease_idx,
            disease_count,
            backwall_element,
            backwall_mass,
            backwall_temperature,
            accumulated_flow,
            cell_padding,
            disease_reserved,
            backwall_padding
        );
        // The twelve public SoA addresses and accumulated-flow address belong
        // to this stable view. Event/texture buffers are borrowed from GameData.
        self.descriptor[0..4].copy_from_slice(&other.descriptor[0..4]);
        self.descriptor[100..448].copy_from_slice(&other.descriptor[100..448]);
        self.descriptor[456..496].copy_from_slice(&other.descriptor[456..496]);
    }

    pub(crate) fn disease_infestation(&self, index: usize) -> u8 {
        self.disease_reserved[index][0]
    }

    pub(crate) fn disease_growth(&self, index: usize) -> f32 {
        f32::from_le_bytes(self.disease_reserved[index][3..].try_into().unwrap())
    }

    pub(crate) fn set_disease_runtime(&mut self, index: usize, infestation: u8, growth: f32) {
        self.disease_reserved[index][0] = infestation;
        self.disease_reserved[index][3..].copy_from_slice(&growth.to_le_bytes());
    }

    pub(crate) fn load_image(&mut self, bytes: &[u8]) {
        self.seed = u32at(bytes, 8);
        let cells = 14;
        let diseases = cells + self.len() * 16;
        let backwalls = diseases + self.len() * 12;
        for index in 0..self.len() {
            let at = cells + index * 16;
            self.element[index] = u16at(bytes, at);
            self.properties[index] = bytes[at + 2];
            self.insulation[index] = bytes[at + 3];
            self.strength[index] = bytes[at + 4];
            self.cell_padding[index].copy_from_slice(&bytes[at + 5..at + 8]);
            self.temperature[index] = f32at(bytes, at + 8);
            self.mass[index] = f32at(bytes, at + 12);
            let at = diseases + index * 12;
            self.disease_idx[index] = bytes[at];
            self.disease_reserved[index][..3].copy_from_slice(&bytes[at + 1..at + 4]);
            self.disease_reserved[index][3..].copy_from_slice(&bytes[at + 8..at + 12]);
            self.disease_count[index] = i32at(bytes, at + 4);
            let at = backwalls + index * 12;
            self.backwall_element[index] = u16at(bytes, at);
            self.backwall_padding[index].copy_from_slice(&bytes[at + 2..at + 4]);
            self.backwall_mass[index] = f32at(bytes, at + 4);
            self.backwall_temperature[index] = f32at(bytes, at + 8);
        }
        self.initialized = true;
    }

    pub(crate) fn image_len(&self) -> usize {
        14 + self.len() * 40
    }

    // The FFI caller validates an exact image_len() writable destination before
    // entering here. Serialize directly into that buffer without a staging image.
    pub(crate) fn write_image(&self, image: &mut [u8]) {
        image[0..4].copy_from_slice(&self.width.to_le_bytes());
        image[4..8].copy_from_slice(&self.height.to_le_bytes());
        image[8..12].copy_from_slice(&self.seed.to_le_bytes());
        image[12] = self.radiation_enabled;
        image[13] = self.headless;
        let diseases = 14 + self.len() * 16;
        let backwalls = diseases + self.len() * 12;
        for index in 0..self.len() {
            let at = 14 + index * 16;
            image[at..at + 2].copy_from_slice(&self.element[index].to_le_bytes());
            image[at + 2] = self.properties[index];
            image[at + 3] = self.insulation[index];
            image[at + 4] = self.strength[index];
            image[at + 5..at + 8].copy_from_slice(&self.cell_padding[index]);
            image[at + 8..at + 12].copy_from_slice(&self.temperature[index].to_le_bytes());
            image[at + 12..at + 16].copy_from_slice(&self.mass[index].to_le_bytes());
            let at = diseases + index * 12;
            image[at] = self.disease_idx[index];
            image[at + 1..at + 4].copy_from_slice(&self.disease_reserved[index][..3]);
            image[at + 4..at + 8].copy_from_slice(&self.disease_count[index].to_le_bytes());
            image[at + 8..at + 12].copy_from_slice(&self.disease_reserved[index][3..]);
            let at = backwalls + index * 12;
            image[at..at + 2].copy_from_slice(&self.backwall_element[index].to_le_bytes());
            image[at + 2..at + 4].copy_from_slice(&self.backwall_padding[index]);
            image[at + 4..at + 8].copy_from_slice(&self.backwall_mass[index].to_le_bytes());
            image[at + 8..at + 12].copy_from_slice(&self.backwall_temperature[index].to_le_bytes());
        }
    }

    pub(crate) fn overlaps_storage(&self, start: usize, length: usize) -> bool {
        macro_rules! overlap {
            ($field:ident) => {
                overlaps(
                    start,
                    length,
                    self.$field.as_ptr() as usize,
                    std::mem::size_of_val(&*self.$field),
                )
            };
        }
        overlap!(element)
            || overlap!(temperature)
            || overlap!(mass)
            || overlap!(properties)
            || overlap!(insulation)
            || overlap!(strength)
            || overlap!(radiation)
            || overlap!(disease_idx)
            || overlap!(disease_count)
            || overlap!(backwall_element)
            || overlap!(backwall_mass)
            || overlap!(backwall_temperature)
            || overlap!(accumulated_flow)
            || overlap!(cell_padding)
            || overlap!(disease_reserved)
            || overlap!(backwall_padding)
            || overlaps(
                start,
                length,
                self.descriptor.as_ptr() as usize,
                DESCRIPTOR_SIZE,
            )
    }
}
