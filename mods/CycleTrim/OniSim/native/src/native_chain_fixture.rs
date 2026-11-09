//! Explicit synthetic inputs shared byte-for-byte by both measured revisions.

use std::sync::Arc;
use crate::grid::Grid;
use crate::state::State;
use crate::tables::{DiseaseTable, ElementTable};
use crate::world::{PhysicsWorld, Region};

pub(super) const GAS: u16 = 3;
const SECOND_GAS: u16 = 4;
const LIQUID: u16 = 5;
const SOLID: u16 = 6;

fn elements() -> ElementTable {
    let entries = [(0x2d39bf75_u32, 0_u8), (0x6d95058c, 3), (0xa9360b34, 0),
        (0x10000003, 1), (0x10000004, 1), (0x10000005, 2), (0x10000006, 3)];
    let mut bytes = (entries.len() as i32).to_le_bytes().to_vec();
    for (index, (hash, phase)) in entries.into_iter().enumerate() {
        let mut record = [0_u8; 164];
        record[..4].copy_from_slice(&hash.to_le_bytes());
        record[4..8].fill(255);
        record[8..10].copy_from_slice(&(index as u16).to_le_bytes());
        record[10] = phase;
        record[11] = 2;
        record[84..88].fill(255);
        record[92..96].copy_from_slice(&0xff80c0ff_u32.to_le_bytes());
        record[96..100].copy_from_slice(&0x00405080_u32.to_le_bytes());
        record[100..104].copy_from_slice(&0xffa0e0ff_u32.to_le_bytes());
        for (offset, value) in [
            (12, if phase == 2 { 4.0_f32 } else { 1.0 }),
            (16, if phase == 3 { 0.5 } else { 0.1 }),
            (20, if index == SECOND_GAS as usize { 2.0 } else { 32.0 }),
            (24, 1.0), (28, 1.0), (32, 1.0), (36, 0.1), (40, 100.0),
            (44, 0.1), (48, 0.1), (52, 1000.0), (60, 10000.0),
            (64, 1.0), (140, 0.1), (152, 300.0), (156, 1000.0), (160, 1.0),
        ] {
            record[offset..offset + 4].copy_from_slice(&value.to_le_bytes());
        }
        bytes.extend_from_slice(&record);
    }
    bytes.extend_from_slice(&[0; 28]);
    ElementTable::parse(&bytes).unwrap()
}

pub(crate) fn create(width: usize, height: usize, scene: &str) -> (State, Vec<Region>) {
    assert!(width >= 16 && height >= 16);
    assert!(["pure", "liquid", "mixed", "thermal", "regions", "empty", "zero", "command", "alternating_idle"].contains(&scene));
    let elements = Arc::new(elements());
    let mut world = PhysicsWorld::new(width, height, 42, &elements).unwrap();
    world.world_offsets = vec![[0, 0, width as i32, height as i32]];
    world.simulate_all_active = true;
    world.falling_disabled = true;
    for y in 1..=height {
        for x in 1..=width {
            let cell = y * world.stride + x;
            let thermal = scene == "thermal";
            world.write.element[cell] = if thermal { SOLID } else { GAS };
            world.write.mass[cell] = if thermal { 1000.0 }
                else { 0.75 + (x % 17) as f32 * 0.03125 };
            world.write.temperature[cell] = if thermal && x < width / 2 { 600.0 } else { 300.0 };
            world.write.strength[cell] = 255;
            if scene == "mixed" && x > width / 2 {
                world.write.element[cell] = SECOND_GAS;
                world.write.mass[cell] = 0.5 + (y % 13) as f32 * 0.03125;
            }
        }
    }
    if scene == "liquid" || scene == "mixed" {
        // The pool is a bounded but genuine connected pressure workload, rather
        // than a million-node stress solver that would obscure batch controls.
        let pool_width = (width / 3).min(48);
        let pool_height = (height / 5).min(16);
        for y in 2..=pool_height + 1 {
            for x in 2..=pool_width + 1 {
                let cell = y * world.stride + x;
                world.write.element[cell] = LIQUID;
                world.write.mass[cell] = if x <= pool_width / 2 { 1000.0 } else { 700.0 };
                world.write.temperature[cell] = 300.0 + (x % 3) as f32;
            }
        }
        // A floor exercises the solid interface and prevents immediate drainage.
        for x in 1..=pool_width + 2 {
            let cell = world.stride + x;
            world.write.element[cell] = SOLID;
            world.write.mass[cell] = 1000.0;
        }
    }
    if scene == "mixed" {
        for y in height / 2..height / 2 + 3 {
            for x in width / 3..width / 3 + 5 {
                let cell = y * world.stride + x;
                world.mixture_element[cell] = SECOND_GAS;
                world.mixture_mass[cell] = 0.125;
            }
        }
    }
    world.sync_read_from_write();
    // Match the existing complete-frame conformance fixture: the last public
    // row is published but is not a pressure-sweep source adjacent to the
    // constructor's 9999-mass top vacuum padding.
    let whole = Region { min_x: 1, max_x: width as i32 + 1,
        min_y: 1, max_y: height as i32, sunlight: 1.0, cosmic_radiation: 0.0 };
    let regions = if scene == "regions" {
        vec![Region { max_x: width as i32 / 2, min_y: 8, ..whole },
            Region { min_x: width as i32 / 2, max_x: width as i32 / 2 + 2, min_y: 6, ..whole },
            Region { min_x: width as i32 / 2 + 2, min_y: 4, ..whole }]
    } else { vec![whole] };
    let mut state = State::default();
    super::ok(state.initialize(None));
    let mut grid = super::ok(Grid::new(width as i32, height as i32, 0, 1));
    grid.initialized = true;
    grid.seed = 42;
    state.grid = Some(grid);
    state.diseases = Some(Arc::new(DiseaseTable { diseases: Vec::new(), element_count: elements.len() }));
    state.elements = Some(elements);
    state.world = Some(world);
    (state, regions)
}
