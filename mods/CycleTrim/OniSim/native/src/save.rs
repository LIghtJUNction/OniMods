//! The official SIMSAVE framing, recovered from the local Linux x64 binary.
//! Addresses and version branches are documented in ../../SAVE-FORMAT.md.

use std::collections::HashMap;

use crate::bytes::MAX_PAYLOAD;
use crate::status::{Error, Result, Status};
use crate::tables::{DiseaseTable, ElementTable};
use crate::world::PhysicsWorld;

const MAGIC: &[u8; 8] = b"SIMSAVE\0";
const CURRENT_VERSION: i32 = 15;
const VACUUM: i32 = 758_759_285;
const VOID: i32 = -1_456_075_980;
const INVALID_ELEMENT: u16 = u16::MAX;
const NO_DISEASE: u8 = u8::MAX;

pub(crate) fn serialize(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    diseases: Option<&DiseaseTable>,
    x: i32,
    y: i32,
) -> Result<Vec<u8>> {
    let count = world
        .stride
        .checked_mul(world.padded_height)
        .ok_or_else(resource)?;
    let length = encoded_length(CURRENT_VERSION, count)?;
    let width = i32::try_from(world.stride).map_err(|_| resource())?;
    let height = i32::try_from(world.padded_height).map_err(|_| resource())?;
    let mut bytes = reserved(length)?;
    bytes.extend_from_slice(MAGIC);
    put_i32(&mut bytes, CURRENT_VERSION);
    put_i32(&mut bytes, width);
    put_i32(&mut bytes, height);
    put_i32(&mut bytes, x);
    put_i32(&mut bytes, y);
    bytes.push(world.saved_options);
    for index in 0..count {
        put_i32(
            &mut bytes,
            element_hash(elements, world.write.element[index])?,
        );
        put_f32(&mut bytes, world.write.temperature[index]);
        put_f32(&mut bytes, world.write.mass[index]);
        put_f32(&mut bytes, world.write.radiation[index]);
    }
    for index in 0..count {
        let disease = world.write.disease_idx[index];
        let hash = if disease == NO_DISEASE {
            0
        } else {
            diseases
                .and_then(|table| table.diseases.get(disease as usize))
                .ok_or_else(|| state("saved cell refers to an unavailable disease"))?
                .hash
        };
        put_i32(&mut bytes, hash);
        put_i32(&mut bytes, world.write.disease_count[index]);
    }
    for index in 0..count {
        let element = world.backwall_element[index];
        if element == INVALID_ELEMENT {
            put_i32(&mut bytes, VACUUM);
            put_f32(&mut bytes, 0.0);
            put_f32(&mut bytes, 0.0);
        } else {
            put_i32(&mut bytes, element_hash(elements, element)?);
            put_f32(&mut bytes, world.backwall_mass[index]);
            put_f32(&mut bytes, world.backwall_temperature[index]);
        }
    }
    // Official BackwallWrite also clears these source fields for invalid cells.
    // Defer that side effect until all serialization can complete successfully.
    for index in 0..count {
        if world.backwall_element[index] == INVALID_ELEMENT {
            world.backwall_mass[index] = 0.0;
            world.backwall_temperature[index] = 0.0;
        }
    }
    Ok(bytes)
}

pub(crate) fn load_into(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    diseases: Option<&DiseaseTable>,
    bytes: &[u8],
) -> Result<()> {
    if bytes.len() > MAX_PAYLOAD {
        return Err(resource());
    }
    let mut reader = Reader::new(bytes);
    if reader.take(8)? != MAGIC {
        return Err(invalid("save magic must be SIMSAVE followed by zero"));
    }
    let version = reader.i32()?;
    if version < 0 {
        return Err(invalid("negative save version"));
    }
    if version > CURRENT_VERSION {
        return Err(Error::new(
            Status::Unsupported,
            "save version is newer than 15",
        ));
    }
    let width = positive(reader.i32()?, "save width must be positive")?;
    let height = positive(reader.i32()?, "save height must be positive")?;
    let (x, y) = if version >= 14 {
        (offset(reader.i32()?)?, offset(reader.i32()?)?)
    } else {
        (0, 0)
    };
    let saved_options = if version >= 13 { reader.byte()? } else { 0 };
    if world.saved_options != 0 && world.saved_options != saved_options {
        return Err(invalid("loaded worlds have different saved options"));
    }
    if x.checked_add(width).is_none_or(|end| end > world.stride)
        || y.checked_add(height)
            .is_none_or(|end| end > world.padded_height)
    {
        return Err(invalid(
            "save rectangle exceeds the allocated internal grid",
        ));
    }
    let count = width.checked_mul(height).ok_or_else(resource)?;
    if bytes.len() != encoded_length(version, count)? {
        return Err(invalid(
            "save byte count does not match its version and dimensions",
        ));
    }
    let elements_by_hash = element_indices(elements)?;
    let vacuum = *elements_by_hash
        .get(&VACUUM)
        .ok_or_else(|| state("loading requires the Vacuum element"))?;
    let void = *elements_by_hash
        .get(&VOID)
        .ok_or_else(|| state("loading requires the Void element"))?;
    let mut cells: Vec<LoadedCell> = reserved(count)?;
    for row in 0..height {
        let destination = (y + row) * world.stride + x;
        for column in 0..width {
            let target = destination + column;
            let hash = match reader.i32()? {
                41_996_153 => -1_736_594_426,
                596_754_987 => 1_282_846_257,
                other => other,
            };
            let mut temperature = reader.f32()?;
            let mut mass = reader.f32()?;
            let mut radiation = if version >= 14 {
                reader.f32()?
            } else {
                world.write.radiation[target]
            };
            let mut element = elements_by_hash.get(&hash).copied().unwrap_or(vacuum);
            if !elements_by_hash.contains_key(&hash) {
                temperature = 0.0;
                mass = 0.0;
                radiation = 0.0;
            }
            if temperature <= 0.0 {
                temperature = 293.0;
            }
            if radiation <= 0.0 {
                radiation = 0.0;
            }
            if element == vacuum || element == void {
                temperature = 0.0;
                mass = 0.0;
                radiation = 0.0;
            }
            load_transition(elements, &mut element, &mut temperature)?;
            cells.push(LoadedCell {
                target,
                element,
                temperature,
                mass,
                radiation,
                disease: None,
                backwall: None,
            });
        }
    }
    if version >= 9 {
        for cell in &mut cells {
            let hash = reader.i32()?;
            let count = reader.i32()?;
            let index = if hash == 0 {
                NO_DISEASE
            } else {
                diseases
                    .and_then(|table| table.diseases.iter().position(|item| item.hash == hash))
                    .and_then(|index| u8::try_from(index).ok())
                    .filter(|&index| index != NO_DISEASE)
                    .unwrap_or(NO_DISEASE)
            };
            cell.disease = Some((index, if index == NO_DISEASE { 0 } else { count }));
        }
    }
    if version >= 15 {
        for cell in &mut cells {
            let hash = reader.i32()?;
            let mass = reader.f32()?;
            let temperature = reader.f32()?;
            let element = elements_by_hash
                .get(&hash)
                .copied()
                .unwrap_or(INVALID_ELEMENT);
            cell.backwall = Some((element, mass, temperature));
        }
    }
    if (8..=14).contains(&version) {
        reader.take(count.checked_mul(4).ok_or_else(resource)?)?;
    }
    if reader.position != bytes.len() {
        return Err(invalid("unconsumed bytes in native save"));
    }
    // Every fallible parse, lookup and allocation completes before changing the world.
    for cell in cells {
        let target = cell.target;
        world.write.element[target] = cell.element;
        world.write.temperature[target] = cell.temperature;
        world.write.mass[target] = cell.mass;
        world.write.radiation[target] = cell.radiation;
        if let Some((index, count)) = cell.disease {
            world.write.disease_idx[target] = index;
            world.write.disease_count[target] = count;
        }
        if let Some((element, mass, temperature)) = cell.backwall {
            world.backwall_element[target] = element;
            world.backwall_mass[target] = mass;
            world.backwall_temperature[target] = temperature;
        }
    }
    world.saved_options = saved_options;
    world.legacy_thermal_boundaries |= version <= 10;
    world.sync_read_from_write();
    Ok(())
}

struct LoadedCell {
    target: usize,
    element: u16,
    temperature: f32,
    mass: f32,
    radiation: f32,
    disease: Option<(u8, i32)>,
    backwall: Option<(u16, f32, f32)>,
}

fn load_transition(
    elements: &ElementTable,
    element: &mut u16,
    temperature: &mut f32,
) -> Result<()> {
    let record = &elements
        .elements
        .get(*element as usize)
        .ok_or_else(|| state("loaded element index exceeds the element table"))?
        .record;
    let transition = if *temperature < record.low_temperature - 3.0
        && record.low_transition != INVALID_ELEMENT
    {
        Some((record.low_transition, 1.5))
    } else if *temperature > record.high_temperature + 3.0
        && record.high_transition != INVALID_ELEMENT
    {
        Some((record.high_transition, -1.5))
    } else {
        None
    };
    if let Some((target, adjustment)) = transition {
        if target as usize >= elements.len() {
            return Err(state("load transition target exceeds the element table"));
        }
        *element = target;
        *temperature += adjustment;
    }
    Ok(())
}

fn element_indices(table: &ElementTable) -> Result<HashMap<i32, u16>> {
    let mut indices = HashMap::new();
    indices.try_reserve(table.len()).map_err(|_| resource())?;
    for (index, element) in table.elements.iter().enumerate() {
        let index = u16::try_from(index).map_err(|_| state("too many element indices"))?;
        if index == INVALID_ELEMENT {
            return Err(state("element table occupies the invalid element sentinel"));
        }
        indices.insert(element.record.id, index);
    }
    Ok(indices)
}

fn element_hash(table: &ElementTable, index: u16) -> Result<i32> {
    table
        .elements
        .get(index as usize)
        .map(|element| element.record.id)
        .ok_or_else(|| state("saved cell refers to an unavailable element"))
}

fn encoded_length(version: i32, count: usize) -> Result<usize> {
    let header = if version >= 14 {
        29
    } else if version >= 13 {
        21
    } else {
        20
    };
    let primary = if version >= 14 { 16 } else { 12 };
    let disease = if version >= 9 { 8 } else { 0 };
    let backwall = if version >= 15 { 12 } else { 0 };
    let legacy = if (8..=14).contains(&version) { 4 } else { 0 };
    let length = count
        .checked_mul(primary + disease + backwall + legacy)
        .and_then(|length| length.checked_add(header))
        .ok_or_else(resource)?;
    if length > MAX_PAYLOAD {
        return Err(resource());
    }
    Ok(length)
}

fn reserved<T>(count: usize) -> Result<Vec<T>> {
    let mut values = Vec::new();
    values.try_reserve_exact(count).map_err(|_| resource())?;
    Ok(values)
}

fn positive(value: i32, message: &'static str) -> Result<usize> {
    if value <= 0 {
        Err(invalid(message))
    } else {
        Ok(value as usize)
    }
}

fn offset(value: i32) -> Result<usize> {
    if value < 0 {
        Err(invalid("negative save placement offset"))
    } else {
        Ok(value as usize)
    }
}

fn put_i32(bytes: &mut Vec<u8>, value: i32) {
    bytes.extend_from_slice(&value.to_le_bytes());
}

fn put_f32(bytes: &mut Vec<u8>, value: f32) {
    bytes.extend_from_slice(&value.to_le_bytes());
}

fn invalid(message: &'static str) -> Error {
    Error::new(Status::InvalidArgument, message)
}

fn state(message: &'static str) -> Error {
    Error::new(Status::InvalidState, message)
}

fn resource() -> Error {
    Error::new(
        Status::ResourceLimit,
        "native save size or allocation exceeds the available limit",
    )
}

struct Reader<'a> {
    bytes: &'a [u8],
    position: usize,
}

impl<'a> Reader<'a> {
    fn new(bytes: &'a [u8]) -> Self {
        Self { bytes, position: 0 }
    }

    fn take(&mut self, count: usize) -> Result<&'a [u8]> {
        let end = self.position.checked_add(count).ok_or_else(resource)?;
        let field = self
            .bytes
            .get(self.position..end)
            .ok_or_else(|| invalid("truncated native save field"))?;
        self.position = end;
        Ok(field)
    }

    fn byte(&mut self) -> Result<u8> {
        Ok(self.take(1)?[0])
    }

    fn i32(&mut self) -> Result<i32> {
        let mut bytes = [0; 4];
        bytes.copy_from_slice(self.take(4)?);
        Ok(i32::from_le_bytes(bytes))
    }

    fn f32(&mut self) -> Result<f32> {
        Ok(f32::from_bits(self.i32()? as u32))
    }
}
