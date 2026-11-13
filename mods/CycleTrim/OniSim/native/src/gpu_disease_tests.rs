use super::*;
use crate::{
    tables::{Disease, DiseaseElement, RangeInfo},
    world::Region,
};
use std::time::Instant;

fn tables() -> (ElementTable, DiseaseTable) {
    let mut bytes = 4_i32.to_le_bytes().to_vec();
    for (index, id) in [0xa9360b34_u32, 0x2d39bf75, 0x6d95058c, 0x11110003]
        .into_iter()
        .enumerate()
    {
        let mut record = [0_u8; 164];
        record[..4].copy_from_slice(&id.to_le_bytes());
        record[8..10].copy_from_slice(&(index as u16).to_le_bytes());
        record[10] = if index == 2 {
            3
        } else if index == 3 {
            1
        } else {
            0
        };
        bytes.extend_from_slice(&record);
    }
    bytes.extend_from_slice(&[0_u8; 24]);
    let elements = ElementTable::parse(&bytes).unwrap();
    let range = RangeInfo {
        min_viable: 0.0,
        min_growth: 100.0,
        max_growth: 400.0,
        max_viable: 500.0,
    };
    let diseases = DiseaseTable {
        element_count: elements.len(),
        diseases: (0..2)
            .map(|id| Disease {
                name: format!("fixture-{id}"),
                raw_header: [0; 76],
                hash: id,
                strength: (id + 1) as f32,
                temperature_range: range.clone(),
                temperature_half_lives: range.clone(),
                pressure_range: range.clone(),
                pressure_half_lives: range.clone(),
                radiation_kill_rate: 0.0,
                elements: (0..elements.len())
                    .map(|_| DiseaseElement {
                        raw: Default::default(),
                        under_population_death_rate: 0.0,
                        population_half_life: 0.0,
                        over_population_half_life: 0.0,
                        diffusion_scale: 0.5,
                        min_count_per_kg: 0.0,
                        max_count_per_kg: 1000.0,
                        min_diffusion_count: 10,
                        min_infestation_tick: 2,
                    })
                    .collect(),
            })
            .collect(),
    };
    (elements, diseases)
}

fn fixture(width: usize, height: usize, pattern: usize, elements: &ElementTable) -> PhysicsWorld {
    let mut world = PhysicsWorld::new(width, height, 0, elements).unwrap();
    for cell in 0..world.write.element.len() {
        world.write.element[cell] = if cell % 11 == 0 { 2 } else { 3 };
        world.write.disease_idx[cell] = if pattern == 0 || (pattern == 1 && cell % 997 != 0) {
            255
        } else {
            0
        };
        world.write.disease_count[cell] = [0, 1, 64, 1600, 65536, i32::MAX, i32::MIN][cell % 7];
        world.write.disease_infestation[cell] = (cell % 8) as u8;
        world.write.disease_growth[cell] = [0.0, -0.0, 0.125, -0.25][cell % 4];
    }
    world.sync_read_from_write();
    // Earlier stages may have already mutated the active buffer.
    for cell in 0..world.write.element.len() {
        if cell % 5 == 0 {
            world.write.disease_idx[cell] = 255;
            world.write.disease_count[cell] = world.write.disease_count[cell].wrapping_add(19);
        }
    }
    world
}

fn equal(a: &PhysicsWorld, b: &PhysicsWorld) {
    for cell in 0..a.write.element.len() {
        assert_eq!(
            a.write.disease_idx[cell], b.write.disease_idx[cell],
            "disease id at {cell}"
        );
        assert_eq!(
            a.write.disease_count[cell], b.write.disease_count[cell],
            "disease count at {cell}"
        );
        assert_eq!(
            a.write.disease_infestation[cell], b.write.disease_infestation[cell],
            "infestation at {cell}"
        );
        assert_eq!(
            a.write.disease_growth[cell].to_bits(),
            b.write.disease_growth[cell].to_bits(),
            "growth at {cell}"
        );
    }
    assert_eq!(a.write.element, b.write.element);
    assert_eq!(a.write.mass, b.write.mass);
    assert_eq!(a.write.temperature, b.write.temperature);
}

#[test]
fn single_disease_resident_sweep_matches_gpu_bits_and_region_halos() {
    let (elements, diseases) = tables();
    let mut gpu = GpuSimulation::new(200, 300).unwrap();
    for (width, height) in [(23, 15), (101, 91), (160, 274)] {
        for pattern in 0..3 {
            for inset in [0, 3] {
                let mut cpu = fixture(width, height, pattern, &elements);
                let mut reference = fixture(width, height, pattern, &elements);
                let region = Region {
                    min_x: 1 + inset,
                    min_y: 1 + inset,
                    max_x: width as i32,
                    max_y: height as i32,
                    ..Default::default()
                };
                for _ in 0..3 {
                    gpu.diffuse_diseases(&mut cpu, &elements, &diseases, region)
                        .unwrap();
                    gpu.diffuse_diseases_gpu_reference(
                        &mut reference,
                        &elements,
                        &diseases,
                        region,
                    )
                    .unwrap();
                    equal(&cpu, &reference);
                }
            }
        }
    }
}

#[test]
fn stronger_incoming_disease_preserves_positive_active_resident() {
    let (elements, diseases) = tables();
    let make_world = || {
        let mut world = PhysicsWorld::new(2, 1, 0, &elements).unwrap();
        world.write.element.fill(world.vacuum_element);
        let first = world.stride + 1;
        for (cell, id, count) in [(first, 0, 64), (first + 1, 1, 1600)] {
            world.write.element[cell] = 3;
            world.write.disease_idx[cell] = id;
            world.write.disease_count[cell] = count;
            world.write.disease_infestation[cell] = 2;
        }
        world.sync_read_from_write();
        world.write.disease_count[first] = 1;
        world
    };
    let mut cpu = make_world();
    let mut reference = make_world();
    let mut gpu = GpuSimulation::new(cpu.stride, cpu.padded_height).unwrap();
    let region = Region {
        min_x: 1,
        min_y: 1,
        max_x: 2,
        max_y: 2,
        ..Default::default()
    };
    gpu.diffuse_diseases(&mut cpu, &elements, &diseases, region)
        .unwrap();
    gpu.diffuse_diseases_gpu_reference(&mut reference, &elements, &diseases, region)
        .unwrap();
    let first = cpu.stride + 1;
    assert_eq!(
        (cpu.write.disease_idx[first], cpu.write.disease_count[first]),
        (0, 1)
    );
    assert_eq!(
        (
            cpu.write.disease_idx[first + 1],
            cpu.write.disease_count[first + 1]
        ),
        (1, 1500)
    );
    equal(&cpu, &reference);
}

#[test]
#[ignore = "manual GPU round-trip versus resident CPU benchmark"]
fn benchmark_resident_disease_sweep() {
    let (elements, diseases) = tables();
    let mut gpu = GpuSimulation::new(162, 276).unwrap();
    for pattern in 0..3 {
        let region = Region {
            min_x: 1,
            min_y: 1,
            max_x: 160,
            max_y: 274,
            ..Default::default()
        };
        let mut cpu = fixture(160, 274, pattern, &elements);
        let mut reference = fixture(160, 274, pattern, &elements);
        for _ in 0..5 {
            gpu.diffuse_diseases(&mut cpu, &elements, &diseases, region)
                .unwrap();
            gpu.diffuse_diseases_gpu_reference(&mut reference, &elements, &diseases, region)
                .unwrap();
        }
        let start = Instant::now();
        for _ in 0..100 {
            gpu.diffuse_diseases(&mut cpu, &elements, &diseases, region)
                .unwrap();
        }
        let cpu_ms = start.elapsed().as_secs_f64() * 10.0;
        let start = Instant::now();
        for _ in 0..100 {
            gpu.diffuse_diseases_gpu_reference(&mut reference, &elements, &diseases, region)
                .unwrap();
        }
        let gpu_ms = start.elapsed().as_secs_f64() * 10.0;
        equal(&cpu, &reference);
        println!(
            "disease_diffusion pattern={pattern} cpu_ms={cpu_ms:.6} gpu_round_trip_ms={gpu_ms:.6}"
        );
    }
}
