//! Metadata sent by the official managed table writers, decoded as little endian.
//!
//! Floating point values are retained without physical validation: the wire format
//! permits bit patterns (including infinity) whose meaning belongs to the simulator.

const ELEMENT_BYTES: usize = 164;
const DISEASE_HEADER_BYTES: usize = 76;
const DISEASE_ELEMENT_BYTES: usize = 29;
const MAX_ELEMENTS: usize = u16::MAX as usize + 1;
const MAX_DISEASES: usize = u8::MAX as usize; // 255 is the absent-disease index.
const MAX_TABLE_BYTES: usize = 64 * 1024 * 1024;

#[derive(Clone, Debug, Default)]
pub(crate) struct ElementTable {
    pub(crate) elements: Vec<Element>,
    pub(crate) has_zero_id: bool,
    pub(crate) trailing_zero_bytes: usize,
}

#[derive(Clone, Debug)]
pub(crate) struct Element {
    pub(crate) name: String,
    pub(crate) record: ElementRecord,
}

#[derive(Clone, Debug)]
pub(crate) struct ElementRecord {
    pub(crate) raw: [u8; ELEMENT_BYTES],
    pub(crate) id: i32,
    pub(crate) low_transition: u16,
    pub(crate) high_transition: u16,
    pub(crate) index: u16,
    pub(crate) state: u8,
    pub(crate) gradient_count: u8,
    pub(crate) specific_heat_capacity: f32,
    pub(crate) thermal_conductivity: f32,
    pub(crate) molar_mass: f32,
    pub(crate) surface_area_solid: f32,
    pub(crate) surface_area_liquid: f32,
    pub(crate) surface_area_gas: f32,
    pub(crate) flow: f32,
    pub(crate) viscosity: f32,
    pub(crate) min_horizontal_flow: f32,
    pub(crate) min_vertical_flow: f32,
    pub(crate) max_mass: f32,
    pub(crate) low_temperature: f32,
    pub(crate) high_temperature: f32,
    pub(crate) strength: f32,
    pub(crate) low_ore_id: i32,
    pub(crate) low_ore_conversion: f32,
    pub(crate) high_ore_id: i32,
    pub(crate) high_ore_conversion: f32,
    pub(crate) sublimate_index: u16,
    pub(crate) convert_index: u16,
    pub(crate) material_properties: u32,
    pub(crate) color: u32,
    pub(crate) gradient: [u32; 6],
    pub(crate) sublimate_fx: i32,
    pub(crate) sublimate_rate: f32,
    pub(crate) sublimate_efficiency: f32,
    pub(crate) sublimate_probability: f32,
    pub(crate) off_gas_probability: f32,
    pub(crate) light_absorption: f32,
    pub(crate) radiation_absorption: f32,
    pub(crate) radiation_per_1000_mass: f32,
    pub(crate) default_temperature: f32,
    pub(crate) default_mass: f32,
    pub(crate) default_pressure: f32,
}

impl ElementRecord {
    fn parse(raw: [u8; ELEMENT_BYTES]) -> Self {
        // The array establishes all offsets before these fixed-width reads.
        let u16_at = |p| u16::from_le_bytes([raw[p], raw[p + 1]]);
        let u32_at = |p| u32::from_le_bytes([raw[p], raw[p + 1], raw[p + 2], raw[p + 3]]);
        let f32_at = |p| f32::from_bits(u32_at(p));
        Self {
            id: u32_at(0) as i32,
            low_transition: u16_at(4),
            high_transition: u16_at(6),
            index: u16_at(8),
            state: raw[10],
            gradient_count: raw[11],
            specific_heat_capacity: f32_at(12),
            thermal_conductivity: f32_at(16),
            molar_mass: f32_at(20),
            surface_area_solid: f32_at(24),
            surface_area_liquid: f32_at(28),
            surface_area_gas: f32_at(32),
            flow: f32_at(36),
            viscosity: f32_at(40),
            min_horizontal_flow: f32_at(44),
            min_vertical_flow: f32_at(48),
            max_mass: f32_at(52),
            low_temperature: f32_at(56),
            high_temperature: f32_at(60),
            strength: f32_at(64),
            low_ore_id: u32_at(68) as i32,
            low_ore_conversion: f32_at(72),
            high_ore_id: u32_at(76) as i32,
            high_ore_conversion: f32_at(80),
            sublimate_index: u16_at(84),
            convert_index: u16_at(86),
            material_properties: u32_at(88),
            color: u32_at(92),
            gradient: std::array::from_fn(|i| u32_at(96 + i * 4)),
            sublimate_fx: u32_at(120) as i32,
            sublimate_rate: f32_at(124),
            sublimate_efficiency: f32_at(128),
            sublimate_probability: f32_at(132),
            off_gas_probability: f32_at(136),
            light_absorption: f32_at(140),
            radiation_absorption: f32_at(144),
            radiation_per_1000_mass: f32_at(148),
            default_temperature: f32_at(152),
            default_mass: f32_at(156),
            default_pressure: f32_at(160),
            raw,
        }
    }
}

impl ElementTable {
    pub(crate) fn parse(bytes: &[u8]) -> Result<Self, String> {
        let mut reader = Reader::new(bytes)?;
        let count = reader.count(MAX_ELEMENTS, "element count")?;
        let record_bytes = count
            .checked_mul(ELEMENT_BYTES)
            .ok_or("element table overflow")?;
        let minimum_names = count.checked_mul(4).ok_or("element names overflow")?;
        let minimum = record_bytes
            .checked_add(minimum_names)
            .ok_or("element table overflow")?;
        if minimum > reader.remaining() {
            return Err("truncated element table".into());
        }
        let records = reader.take(record_bytes)?;
        let mut elements = reserve(count)?;
        let mut has_zero_id = false;
        for raw in records.as_chunks::<ELEMENT_BYTES>().0 {
            let name = reader.string()?;
            let mut record = [0; ELEMENT_BYTES];
            record.copy_from_slice(raw);
            let record = ElementRecord::parse(record);
            has_zero_id |= record.id == 0;
            elements.push(Element { name, record });
        }
        // Elements_CreateTable sends MemoryStream.GetBuffer(), including capacity.
        let trailing_zero_bytes = reader.remaining();
        if reader.take(trailing_zero_bytes)?.iter().any(|&b| b != 0) {
            return Err("nonzero bytes after element names".into());
        }
        Ok(Self {
            elements,
            has_zero_id,
            trailing_zero_bytes,
        })
    }

    pub(crate) fn len(&self) -> usize {
        self.elements.len()
    }
    pub(crate) fn is_empty(&self) -> bool {
        self.elements.is_empty()
    }
}

#[derive(Clone, Debug, Default)]
pub(crate) struct DiseaseTable {
    pub(crate) diseases: Vec<Disease>,
    pub(crate) element_count: usize,
}

#[derive(Clone, Debug)]
pub(crate) struct Disease {
    pub(crate) name: String,
    pub(crate) raw_header: [u8; DISEASE_HEADER_BYTES],
    pub(crate) hash: i32,
    pub(crate) strength: f32,
    pub(crate) temperature_range: RangeInfo,
    pub(crate) temperature_half_lives: RangeInfo,
    pub(crate) pressure_range: RangeInfo,
    pub(crate) pressure_half_lives: RangeInfo,
    pub(crate) radiation_kill_rate: f32,
    pub(crate) elements: Vec<DiseaseElement>,
}

#[derive(Clone, Debug)]
pub(crate) struct RangeInfo {
    pub(crate) min_viable: f32,
    pub(crate) min_growth: f32,
    pub(crate) max_growth: f32,
    pub(crate) max_viable: f32,
}

impl RangeInfo {
    fn parse(reader: &mut Reader<'_>) -> Result<Self, String> {
        Ok(Self {
            min_viable: reader.f32()?,
            min_growth: reader.f32()?,
            max_growth: reader.f32()?,
            max_viable: reader.f32()?,
        })
    }
}

#[derive(Clone, Debug)]
pub(crate) struct DiseaseElement {
    pub(crate) raw: [u8; DISEASE_ELEMENT_BYTES],
    pub(crate) under_population_death_rate: f32,
    pub(crate) population_half_life: f32,
    pub(crate) over_population_half_life: f32,
    pub(crate) diffusion_scale: f32,
    pub(crate) min_count_per_kg: f32,
    pub(crate) max_count_per_kg: f32,
    pub(crate) min_diffusion_count: i32,
    pub(crate) min_infestation_tick: u8,
}

impl DiseaseElement {
    fn parse(reader: &mut Reader<'_>) -> Result<Self, String> {
        let mut raw = [0; DISEASE_ELEMENT_BYTES];
        raw.copy_from_slice(reader.take(DISEASE_ELEMENT_BYTES)?);
        let mut fields = Reader::new(&raw)?;
        Ok(Self {
            under_population_death_rate: fields.f32()?,
            population_half_life: fields.f32()?,
            over_population_half_life: fields.f32()?,
            diffusion_scale: fields.f32()?,
            min_count_per_kg: fields.f32()?,
            max_count_per_kg: fields.f32()?,
            min_diffusion_count: fields.i32()?,
            min_infestation_tick: fields.take(1)?[0],
            raw,
        })
    }
}

impl DiseaseTable {
    pub(crate) fn parse(bytes: &[u8], element_count: usize) -> Result<Self, String> {
        if element_count > MAX_ELEMENTS {
            return Err("expected element count exceeds index space".into());
        }
        let mut reader = Reader::new(bytes)?;
        let count = reader.count(MAX_DISEASES, "disease count")?;
        let encoded_elements = reader.count(MAX_ELEMENTS, "disease element count")?;
        if encoded_elements != element_count {
            return Err("disease element count does not match element table".into());
        }
        let element_bytes = element_count
            .checked_mul(DISEASE_ELEMENT_BYTES)
            .ok_or("disease element table overflow")?;
        let minimum_record = (4 + DISEASE_HEADER_BYTES)
            .checked_add(element_bytes)
            .ok_or("disease record overflow")?;
        let minimum = count
            .checked_mul(minimum_record)
            .ok_or("disease table overflow")?;
        if minimum > reader.remaining() {
            return Err("truncated disease table".into());
        }
        let mut diseases = reserve(count)?;
        for _ in 0..count {
            let name = reader.string()?;
            let mut raw_header = [0; DISEASE_HEADER_BYTES];
            raw_header.copy_from_slice(reader.take(DISEASE_HEADER_BYTES)?);
            let mut header = Reader::new(&raw_header)?;
            let hash = header.i32()?;
            let strength = header.f32()?;
            let temperature_range = RangeInfo::parse(&mut header)?;
            let temperature_half_lives = RangeInfo::parse(&mut header)?;
            let pressure_range = RangeInfo::parse(&mut header)?;
            let pressure_half_lives = RangeInfo::parse(&mut header)?;
            let radiation_kill_rate = header.f32()?;
            if reader.remaining() < element_bytes {
                return Err("truncated disease element table".into());
            }
            let mut elements = reserve(element_count)?;
            for _ in 0..element_count {
                elements.push(DiseaseElement::parse(&mut reader)?);
            }
            diseases.push(Disease {
                name,
                raw_header,
                hash,
                strength,
                temperature_range,
                temperature_half_lives,
                pressure_range,
                pressure_half_lives,
                radiation_kill_rate,
                elements,
            });
        }
        // Diseases_CreateTable uses ToArray(), so no capacity padding is valid.
        if reader.remaining() != 0 {
            return Err("bytes after disease table".into());
        }
        Ok(Self {
            diseases,
            element_count,
        })
    }

    pub(crate) fn len(&self) -> usize {
        self.diseases.len()
    }
    pub(crate) fn is_empty(&self) -> bool {
        self.diseases.is_empty()
    }
}

fn reserve<T>(count: usize) -> Result<Vec<T>, String> {
    let mut values = Vec::new();
    values
        .try_reserve_exact(count)
        .map_err(|_| "table allocation failed")?;
    Ok(values)
}

struct Reader<'a> {
    bytes: &'a [u8],
    position: usize,
}

impl<'a> Reader<'a> {
    fn new(bytes: &'a [u8]) -> Result<Self, String> {
        if bytes.len() > MAX_TABLE_BYTES {
            return Err("table exceeds transport payload limit".into());
        }
        Ok(Self { bytes, position: 0 })
    }

    fn remaining(&self) -> usize {
        self.bytes.len() - self.position
    }

    fn take(&mut self, size: usize) -> Result<&'a [u8], String> {
        let end = self
            .position
            .checked_add(size)
            .ok_or("table offset overflow")?;
        let bytes = self
            .bytes
            .get(self.position..end)
            .ok_or("truncated table field")?;
        self.position = end;
        Ok(bytes)
    }

    fn i32(&mut self) -> Result<i32, String> {
        let bytes: [u8; 4] = self.take(4)?.try_into().map_err(|_| "invalid i32 field")?;
        Ok(i32::from_le_bytes(bytes))
    }

    fn f32(&mut self) -> Result<f32, String> {
        let bytes: [u8; 4] = self.take(4)?.try_into().map_err(|_| "invalid f32 field")?;
        Ok(f32::from_le_bytes(bytes))
    }

    fn count(&mut self, maximum: usize, field: &str) -> Result<usize, String> {
        let count = self.i32()?;
        if count < 0 || count as usize > maximum {
            return Err(format!("invalid {field}"));
        }
        Ok(count as usize)
    }

    fn string(&mut self) -> Result<String, String> {
        let size = self.count(MAX_TABLE_BYTES, "UTF-8 string length")?;
        let text = std::str::from_utf8(self.take(size)?).map_err(|_| "invalid table UTF-8")?;
        let mut value = String::new();
        value
            .try_reserve_exact(size)
            .map_err(|_| "table name allocation failed")?;
        value.push_str(text);
        Ok(value)
    }
}
