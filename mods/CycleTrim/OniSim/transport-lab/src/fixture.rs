use crate::{events::*, tables::*, world::*};

pub struct Rng(pub u64);
impl Rng {
    pub fn next(&mut self) -> u64 {
        self.0 ^= self.0 << 13;
        self.0 ^= self.0 >> 7;
        self.0 ^= self.0 << 17;
        self.0
    }
    pub fn below(&mut self, bound: usize) -> usize {
        self.next() as usize % bound
    }
    pub fn special(&mut self) -> f32 {
        const B: [u32; 18] = [
            0, 0x80000000, 1, 0x80000001, 0x7fffff, 0x800000, 0x807fffff, 0x80800000, 0x3f800000,
            0xbf800000, 0x43960000, 0x7f7fffff, 0x7f800000, 0xff800000, 0x7fc00000, 0x7fc12345,
            0xffc23456, 0x7f812345,
        ];
        let bits = if self.below(3) == 0 {
            self.next() as u32
        } else {
            B[self.below(B.len())]
        };
        f32::from_bits(bits)
    }
}
pub fn elements(count: usize) -> ElementTable {
    let mut bytes = (count as i32).to_le_bytes().to_vec();
    for i in 0..count {
        let mut r = [0u8; 164];
        r[8..10].copy_from_slice(&(i as u16).to_le_bytes());
        r[10] = match i {
            0 | 4 => 0,
            1 => 1,
            2 => 2,
            3 => 3,
            _ => 1 + (i % 3) as u8,
        };
        for (offset, value) in [
            (12, 0.2 + (i % 13) as f32 * 0.5),
            (16, 0.05 * (1 + (i % 19)) as f32),
            (20, 1.0 + i as f32),
            (24, 1.0),
            (28, 0.8),
            (32, 0.4),
            (36, 0.2),
            (52, 1000.0),
            (56, 1.0),
            (60, 10000.0),
        ] {
            r[offset..offset + 4].copy_from_slice(&value.to_le_bytes());
        }
        bytes.extend_from_slice(&r);
    }
    for _ in 0..count {
        bytes.extend_from_slice(&0i32.to_le_bytes());
    }
    ElementTable::parse(&bytes).expect("valid element fixture")
}
pub fn diseases() -> DiseaseTable {
    let mut b = 2i32.to_le_bytes().to_vec();
    b.extend_from_slice(&0i32.to_le_bytes());
    for i in 0..2 {
        b.extend_from_slice(&0i32.to_le_bytes());
        let mut h = [0u8; 76];
        h[4..8].copy_from_slice(&(1.0 + i as f32).to_le_bytes());
        b.extend_from_slice(&h);
    }
    DiseaseTable::parse(&b, 0).expect("valid disease fixture")
}
pub fn populate(w: &mut PhysicsWorld, rng: &mut Rng, kinds: usize, special: bool) {
    for i in 0..w.write.mass.len() {
        let edge = i % w.stride == 0
            || i % w.stride == w.stride - 1
            || i < w.stride
            || i >= w.stride * (w.padded_height - 1);
        w.write.element[i] = if edge { 3 } else { rng.below(kinds) as u16 };
        w.write.mass[i] = if special {
            rng.special()
        } else {
            0.1 + rng.below(10000) as f32 * 0.002
        };
        w.write.temperature[i] = if special {
            rng.special()
        } else {
            150.0 + rng.below(10000) as f32 * 0.1
        };
        w.write.insulation[i] = if special { rng.next() as u8 } else { 255 };
        w.write.properties[i] = rng.below(16) as u8;
        w.write.disease_idx[i] = [0, 1, 255][rng.below(3)];
        w.write.disease_count[i] = if special {
            rng.next() as i32
        } else {
            rng.below(5000) as i32
        };
        w.write.disease_growth[i] = if special { rng.special() } else { 0.0 };
        w.write.disease_infestation[i] = rng.next() as u8;
        w.write.strength[i] = rng.next() as u8;
        w.write.radiation[i] = rng.special();
        w.backwall_element[i] = rng.below(kinds) as u16;
        w.backwall_mass[i] = if special { rng.special() } else { 500.0 };
        w.backwall_temperature[i] = if special {
            rng.special()
        } else {
            200.0 + rng.below(5000) as f32 * 0.1
        };
        w.flow[i] = [0.0; 4];
        w.dirty[i] = false;
        w.dirty_flags[i] = 0;
    }
    w.read.copy_from(&w.write);
}
pub fn bits(a: f32, b: f32, label: &str) {
    assert_eq!(a.to_bits(), b.to_bits(), "{label}: {a:?} != {b:?}");
}
pub fn same_cells(a: &Cells, b: &Cells) {
    assert_eq!(a.element, b.element, "element");
    assert_eq!(a.properties, b.properties, "properties");
    assert_eq!(a.insulation, b.insulation);
    assert_eq!(a.strength, b.strength);
    assert_eq!(a.disease_idx, b.disease_idx);
    assert_eq!(a.disease_count, b.disease_count);
    assert_eq!(a.disease_infestation, b.disease_infestation);
    for (name, x, y) in [
        ("mass", &a.mass, &b.mass),
        ("temperature", &a.temperature, &b.temperature),
        ("growth", &a.disease_growth, &b.disease_growth),
        ("radiation", &a.radiation, &b.radiation),
    ] {
        for (i, (&xx, &yy)) in x.iter().zip(y).enumerate() {
            assert_eq!(xx.to_bits(), yy.to_bits(), "{name}[{i}]: {xx:?} != {yy:?}");
        }
    }
}
pub fn same_world(a: &PhysicsWorld, b: &PhysicsWorld) {
    same_cells(&a.read, &b.read);
    same_cells(&a.write, &b.write);
    assert_eq!(a.dirty, b.dirty);
    assert_eq!(a.dirty_flags, b.dirty_flags);
    for (&x, &y) in a.backwall_temperature.iter().zip(&b.backwall_temperature) {
        bits(x, y, "wall");
    }
    for (x, y) in a.flow.iter().zip(&b.flow) {
        for i in 0..4 {
            bits(x[i], y[i], "flow");
        }
    }
    assert_eq!(
        (a.rng, a.tick_seed, a.scan_direction),
        (b.rng, b.tick_seed, b.scan_direction)
    );
}
pub fn same_events(a: &EventQueues, b: &EventQueues) {
    assert_eq!(a.substance_change_info.len(), b.substance_change_info.len());
    for (x, y) in a.substance_change_info.iter().zip(&b.substance_change_info) {
        assert_eq!(
            (x.cell_idx, x.old_elem_idx, x.new_elem_idx),
            (y.cell_idx, y.old_elem_idx, y.new_elem_idx)
        );
    }
    assert_eq!(
        a.backwall_should_transition_infos.len(),
        b.backwall_should_transition_infos.len()
    );
    for (x, y) in a
        .backwall_should_transition_infos
        .iter()
        .zip(&b.backwall_should_transition_infos)
    {
        assert_eq!(x.game_cell, y.game_cell);
    }
}
pub fn same_info(a: ConsumedMassInfo, b: ConsumedMassInfo) {
    assert_eq!(
        (
            a.sim_handle,
            a.removed_elem_idx,
            a.disease_idx,
            a.disease_count
        ),
        (
            b.sim_handle,
            b.removed_elem_idx,
            b.disease_idx,
            b.disease_count
        )
    );
    bits(a.mass, b.mass, "consumed mass");
    bits(a.temperature, b.temperature, "consumed temperature");
}
pub fn success<T>(r: crate::status::Result<T>) -> T {
    r.unwrap_or_else(|e| panic!("{}", e.message))
}
