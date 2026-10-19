use crate::world::{Cells, PhysicsWorld};
use crate::{baseline, candidate, status, tables::DiseaseTable};

pub(crate) fn success<T>(result: status::Result<T>) -> T {
    result.unwrap_or_else(|error| panic!("status {}: {}", error.status as i32, error.message))
}

#[derive(Clone, Copy, Debug)]
pub(crate) struct Emitter {
    pub(crate) cell: i32,
    pub(crate) disease_idx: u8,
    pub(crate) max_depth: u8,
    pub(crate) emit_count: i32,
    pub(crate) emit_interval: f32,
    pub(crate) elapsed: f32,
}

impl Emitter {
    pub(crate) fn baseline(self) -> baseline::DiseaseEmitterData {
        baseline::DiseaseEmitterData {
            cell: self.cell,
            disease_idx: self.disease_idx,
            max_depth: self.max_depth,
            emit_count: self.emit_count,
            emit_interval: self.emit_interval,
            elapsed: self.elapsed,
        }
    }

    pub(crate) fn candidate(self) -> candidate::DiseaseEmitterData {
        candidate::DiseaseEmitterData {
            cell: self.cell,
            disease_idx: self.disease_idx,
            max_depth: self.max_depth,
            emit_count: self.emit_count,
            emit_interval: self.emit_interval,
            elapsed: self.elapsed,
        }
    }
}

pub(crate) struct Rng(pub(crate) u64);

impl Rng {
    pub(crate) fn next(&mut self) -> u64 {
        self.0 ^= self.0 << 13;
        self.0 ^= self.0 >> 7;
        self.0 ^= self.0 << 17;
        self.0
    }

    pub(crate) fn below(&mut self, bound: usize) -> usize {
        self.next() as usize % bound
    }

    pub(crate) fn choose<T: Copy>(&mut self, choices: &[T]) -> T {
        choices[self.below(choices.len())]
    }
}

pub(crate) fn disease_table(special_values: bool) -> DiseaseTable {
    // Exercise the real wire-table parser. All 255 usable disease indices exist.
    let mut bytes = Vec::new();
    bytes.extend_from_slice(&255i32.to_le_bytes());
    bytes.extend_from_slice(&0i32.to_le_bytes());
    let strengths = [0.0f32, 0.25, 1.0, 4.0, f32::INFINITY, f32::from_bits(0x7fc12345)];
    for index in 0..255 {
        bytes.extend_from_slice(&0i32.to_le_bytes()); // empty name
        let mut header = [0u8; 76];
        header[0..4].copy_from_slice(&(index as i32).to_le_bytes());
        let strength = if special_values { strengths[index % strengths.len()] } else { 1.0 };
        header[4..8].copy_from_slice(&strength.to_le_bytes());
        bytes.extend_from_slice(&header);
    }
    DiseaseTable::parse(&bytes, 0).unwrap_or_else(|error| panic!("fixture table: {error}"))
}

pub(crate) fn random_world(stride: usize, height: usize, rng: &mut Rng) -> PhysicsWorld {
    let mut world = PhysicsWorld::fixture(stride, height);
    let ids = [0u8, 1, 2, 3, 4, 254, 255];
    let counts = [i32::MIN, -100, -1, 0, 1, 2, 100, i32::MAX];
    for index in 0..world.write.element.len() {
        world.write.element[index] = rng.next() as u16;
        world.write.mass[index] = f32::from_bits(rng.next() as u32);
        world.write.temperature[index] = f32::from_bits(rng.next() as u32);
        world.write.properties[index] = rng.next() as u8;
        world.write.insulation[index] = rng.next() as u8;
        world.write.strength[index] = rng.next() as u8;
        world.write.disease_idx[index] = rng.choose(&ids);
        world.write.disease_count[index] = rng.choose(&counts);
        world.write.disease_infestation[index] = rng.next() as u8;
        world.write.disease_growth[index] = f32::from_bits(rng.next() as u32);
        world.write.radiation[index] = f32::from_bits(rng.next() as u32);
    }
    world.read.copy_from(&world.write);
    world
}

// A data-dependent recurrence, shared by both search consumers. The index is
// passed through black_box, so LLVM cannot replace row enumeration by a closed
// form sum. BFS and row order intentionally have different final checksums.
#[inline]
pub(crate) fn consume_index(state: u64, index: usize) -> u64 {
    let index = std::hint::black_box(index as u64);
    (state.rotate_left(9) ^ index.wrapping_mul(0x9e3779b97f4a7c15))
        .wrapping_add(0xd1b54a32d192ed03)
}

pub(crate) fn disease_checksum(cells: &Cells) -> u64 {
    let mut checksum = 0x243f6a8885a308d3u64;
    for cell in 0..cells.disease_idx.len() {
        let word = cells.disease_idx[cell] as u64
            ^ ((cells.disease_count[cell] as u32 as u64) << 8)
            ^ ((cells.disease_infestation[cell] as u64) << 40)
            ^ (cells.disease_growth[cell].to_bits() as u64).rotate_left(43);
        checksum = checksum.rotate_left(7).wrapping_add(word);
    }
    checksum
}
