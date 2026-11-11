//! New hydraulic regression scenes, independent of the user's missing fixture.
//!
//! These exercise the production CPU liquid phase (also used with GPU enabled),
//! not a replacement solver. The parameter set below is explicitly synthetic:
//! a 1840 kg capacity high-temperature liquid with a 0.1 kg flow threshold.
//! It must not be described as a captured/current game's Magma element record.
//! Real material calibration and rendered game acceptance remain separate gates.

use crate::events::EventQueues;
use crate::simulation_step_fluid;
use crate::tables::{DiseaseTable, ElementTable};
use crate::world::{PhysicsWorld, Region};

const VACUUM: u16 = 0;
const SOLID: u16 = 1;
const LIQUID: u16 = 2;
const CAPACITY: f32 = 1840.0;
const TEMPERATURE: f32 = 1800.0;

fn elements() -> ElementTable {
    let mut bytes = 4_i32.to_le_bytes().to_vec();
    for (index, (hash, phase)) in [
        (0x2d39bf75_u32, 0_u8),
        (0x6d95058c_u32, 3_u8),
        (0x01234567_u32, 2_u8),
        (0xa9360b34_u32, 0_u8),
    ]
    .into_iter()
    .enumerate()
    {
        let mut record = [0_u8; 164];
        record[..4].copy_from_slice(&hash.to_le_bytes());
        record[8..10].copy_from_slice(&(index as u16).to_le_bytes());
        record[10] = phase;
        for (offset, value) in [
            (12, 1.0_f32),
            (20, 100.0),
            (40, 100.0),
            (44, 0.1),
            (48, 0.1),
            (52, CAPACITY),
            (56, 1.0),
            (60, 10000.0),
        ] {
            record[offset..offset + 4].copy_from_slice(&value.to_le_bytes());
        }
        bytes.extend_from_slice(&record);
    }
    bytes.extend_from_slice(&[0_u8; 16]); // Four empty names.
    ElementTable::parse(&bytes).unwrap()
}

fn solid_world(width: usize, height: usize, table: &ElementTable) -> PhysicsWorld {
    let mut world = PhysicsWorld::new(width, height, 7, table).unwrap();
    world.write.element.fill(SOLID);
    world.write.mass.fill(0.0);
    world.write.temperature.fill(TEMPERATURE);
    world.falling_disabled = true;
    world.simulate_all_active = true;
    world.read.copy_from(&world.write);
    world
}

fn set(world: &mut PhysicsWorld, x: usize, y: usize, element: u16, mass: f32, t: f32) {
    let cell = y * world.stride + x;
    world.write.element[cell] = element;
    world.write.mass[cell] = mass;
    world.write.temperature[cell] = t;
}

fn advance(world: &mut PhysicsWorld, table: &ElementTable, steps: usize) {
    advance_regions(world, table, steps, true, None);
}

fn advance_regions(
    world: &mut PhysicsWorld,
    table: &ElementTable,
    steps: usize,
    clear_flow: bool,
    split: Option<i32>,
) {
    let diseases = DiseaseTable::default();
    let mut events = EventQueues::default();
    let region = Region {
        min_x: 1,
        max_x: world.width as i32 + 1,
        min_y: 1,
        max_y: world.height as i32 + 1,
        ..Region::default()
    };
    let regions = if let Some(x) = split {
        vec![Region { max_x: x, ..region }, Region { min_x: x, ..region }]
    } else {
        vec![region]
    };
    for _ in 0..steps {
        world.sync_read_from_write();
        world.scan_direction = -world.scan_direction;
        if clear_flow {
            world.flow.fill([0.0; 4]);
        }
        for &region in &regions {
            // Production refreshes each region immediately before liquid and
            // publishes its changed fluid fields afterward.
            world.read.copy_region_from(
                &world.write,
                world.stride,
                region.min_x as usize,
                region.max_x as usize,
                region.min_y as usize,
                region.max_y as usize,
            );
            let _ =
                simulation_step_fluid::liquid_phase(world, table, &diseases, &mut events, region);
            world.read.copy_fluid_region_from(
                &world.write,
                world.stride,
                region.min_x as usize,
                region.max_x as usize,
                region.min_y as usize,
                region.max_y as usize,
            );
        }
        assert!(events.spawn_falling_liquid_info.is_empty());
        events.clear();
        for (cell, &mass) in world.write.mass.iter().enumerate() {
            assert!(
                mass.is_finite() && mass >= -0.001,
                "invalid mass {mass} at {cell}"
            );
        }
    }
}

fn totals(world: &PhysicsWorld) -> (f64, f64) {
    let mut mass = 0.0;
    let mut heat = 0.0;
    for cell in 0..world.write.mass.len() {
        if world.write.element[cell] == LIQUID {
            let m = f64::from(world.write.mass[cell]);
            mass += m;
            heat += m * f64::from(world.write.temperature[cell]); // cp = 1.
        }
    }
    (mass, heat)
}

fn conservation(before: (f64, f64), world: &PhysicsWorld) {
    let after = totals(world);
    assert!(
        (after.0 - before.0).abs() <= before.0 * 0.00002 + 0.01,
        "mass changed: {} -> {}",
        before.0,
        after.0
    );
    assert!(
        (after.1 - before.1).abs() <= before.1 * 0.00002 + 0.01,
        "heat changed: {} -> {}",
        before.1,
        after.1
    );
}

fn column(world: &PhysicsWorld, x: usize) -> f64 {
    (1..=world.height)
        .filter_map(|y| {
            let cell = y * world.stride + x;
            (world.write.element[cell] == LIQUID).then_some(f64::from(world.write.mass[cell]))
        })
        .sum()
}

fn wide_pool(table: &ElementTable, mirrored: bool) -> PhysicsWorld {
    let mut world = solid_world(34, 12, table);
    for y in 2..=10 {
        for x in 2..=33 {
            let deep = (x <= 17) != mirrored;
            let filled = y <= if deep { 5 } else { 3 };
            set(
                &mut world,
                x,
                y,
                if filled { LIQUID } else { VACUUM },
                if filled { CAPACITY } else { 0.0 },
                TEMPERATURE,
            );
        }
    }
    world
}

#[test]
fn hydraulic_tests_new_32_wide_multilayer_pool_levels_and_conserves() {
    let table = elements();
    let mut results = Vec::new();
    for mirrored in [false, true] {
        let mut world = wide_pool(&table, mirrored);
        let before = totals(&world);
        advance(&mut world, &table, 1000);
        let columns: Vec<_> = (2..=33).map(|x| column(&world, x)).collect();
        let low = columns.iter().copied().fold(f64::INFINITY, f64::min);
        let high = columns.iter().copied().fold(f64::NEG_INFINITY, f64::max);
        eprintln!(
            "new synthetic 32-wide pool mirror={mirrored}: left={:.6} right={:.6} range={:.6} kg total={:.6}",
            columns[0],
            columns[31],
            high - low,
            totals(&world).0
        );
        conservation(before, &world);
        assert!(
            high - low < f64::from(CAPACITY) * 0.05,
            "32-column pool still uneven after 1000 updates: {columns:?}"
        );
        results.push(columns);
    }
    // Fixed before running the mirror comparison; 50 g per entire column.
    for x in 0..32 {
        assert!(
            (results[0][x] - results[1][31 - x]).abs() < 0.05,
            "mirror differs at column {x}: {} vs {}",
            results[0][x],
            results[1][31 - x]
        );
    }
}

#[test]
fn hydraulic_tests_new_symmetric_single_step_has_no_horizontal_bias() {
    let table = elements();
    let mut world = solid_world(7, 5, &table);
    for x in 2..=6 {
        set(&mut world, x, 2, VACUUM, 0.0, TEMPERATURE);
    }
    set(&mut world, 4, 2, LIQUID, 100.0, TEMPERATURE);
    advance(&mut world, &table, 1);
    let left = column(&world, 3);
    let right = column(&world, 5);
    eprintln!("new symmetric single-step: left={left} right={right}");
    assert!((left - right).abs() < 0.0001, "left/right transfer bias");
}

#[test]
fn hydraulic_tests_new_communicating_vessels_level_without_crossing_walls() {
    let table = elements();
    let mut world = solid_world(16, 12, &table);
    for y in 2..=10 {
        for x in 2..=15 {
            if y <= 3 || x <= 4 || x >= 13 {
                let filled = y <= 3 || (x <= 4 && y <= 8);
                set(
                    &mut world,
                    x,
                    y,
                    if filled { LIQUID } else { VACUUM },
                    if filled { CAPACITY } else { 0.0 },
                    TEMPERATURE,
                );
            }
        }
    }
    let before = totals(&world);
    advance(&mut world, &table, 1000);
    let left = (2..=4).map(|x| column(&world, x)).sum::<f64>() / 3.0;
    let right = (13..=15).map(|x| column(&world, x)).sum::<f64>() / 3.0;
    eprintln!("new communicating vessels: left={left} right={right}");
    conservation(before, &world);
    for y in 4..=10 {
        for x in 5..=12 {
            assert_eq!(world.write.element[y * world.stride + x], SOLID);
        }
    }
    assert!(
        (left - right).abs() < f64::from(CAPACITY) * 0.05,
        "communicating vessels still uneven"
    );
}

#[test]
fn hydraulic_tests_new_pressure_fills_sealed_vacuum() {
    let table = elements();
    let mut world = solid_world(7, 7, &table);
    for y in 2..=4 {
        set(&mut world, 4, y, VACUUM, 0.0, TEMPERATURE);
    }
    set(&mut world, 4, 2, LIQUID, CAPACITY * 3.2, TEMPERATURE);
    let before = totals(&world);
    advance(&mut world, &table, 1000);
    conservation(before, &world);
    for y in 2..=4 {
        let cell = y * world.stride + 4;
        assert_eq!(world.write.element[cell], LIQUID);
        assert!(
            world.write.mass[cell] >= CAPACITY * 0.99,
            "sealed vacuum not filled at y={y}: {}",
            world.write.mass[cell]
        );
    }
}

#[test]
fn hydraulic_tests_new_unequal_temperature_flow_conserves_heat() {
    let table = elements();
    let mut world = wide_pool(&table, false);
    for y in 2..=5 {
        for x in 2..=17 {
            let cell = y * world.stride + x;
            world.write.temperature[cell] = 2200.0;
        }
    }
    let before = totals(&world);
    advance(&mut world, &table, 100);
    conservation(before, &world);
}

#[test]
fn hydraulic_tests_new_equal_temperature_through_flow_conserves_germs() {
    let table = elements();
    let mut world = solid_world(9, 5, &table);
    for x in 2..=8 {
        set(
            &mut world,
            x,
            2,
            LIQUID,
            CAPACITY + (8 - x) as f32 * 100.0,
            TEMPERATURE,
        );
        let cell = 2 * world.stride + x;
        world.write.disease_idx[cell] = 0;
        world.write.disease_count[cell] = 100000;
    }
    let before: i64 = world
        .write
        .disease_count
        .iter()
        .map(|&n| i64::from(n))
        .sum();
    advance(&mut world, &table, 100);
    let after: i64 = world
        .write
        .disease_count
        .iter()
        .map(|&n| i64::from(n))
        .sum();
    assert_eq!(before, after, "germs created/lost by pressure through-flow");
}

#[test]
fn hydraulic_tests_new_implicit_faces_do_not_cross_material_or_property_boundaries() {
    let mut table = elements();
    let mut gas = table.elements[LIQUID as usize].clone();
    gas.record.state = 1;
    gas.record.id = 0x12345678;
    table.elements.push(gas);
    let mut other = table.elements[LIQUID as usize].clone();
    other.record.id = 0x23456789;
    table.elements.push(other);
    let mut world = solid_world(12, 5, &table);
    for x in 2..=11 {
        set(
            &mut world,
            x,
            2,
            LIQUID,
            CAPACITY + (12 - x) as f32 * 100.0,
            TEMPERATURE,
        );
    }
    set(&mut world, 4, 2, 4, 1.0, 300.0); // Gas boundary.
    set(&mut world, 7, 2, 5, CAPACITY, 500.0); // Immiscible liquid.
    let blocked = 2 * world.stride + 10;
    world.write.properties[blocked] = 2;
    let mixed = 2 * world.stride + 11;
    world.mixture_element[mixed] = 5;
    world.mixture_mass[mixed] = 30.0;
    world.sync_read_from_write();
    let mut scratch = crate::fluid_pressure::PressureScratch::default();
    let mut events = EventQueues::default();
    let region = Region {
        min_x: 1,
        max_x: 13,
        min_y: 1,
        max_y: 6,
        ..Region::default()
    };
    let _ = scratch.run(
        &mut world,
        &table,
        &DiseaseTable::default(),
        &mut events,
        region,
    );
    for x in [4, 7, 10, 11] {
        let cell = 2 * world.stride + x;
        assert_eq!(scratch.covered[cell], 0, "boundary was included at x={x}");
        let expected = match x {
            4 => (4, 1.0, 300.0),
            7 => (5, CAPACITY, 500.0),
            _ => (LIQUID, CAPACITY + (12 - x) as f32 * 100.0, TEMPERATURE),
        };
        assert_eq!(
            (
                world.write.element[cell],
                world.write.mass[cell],
                world.write.temperature[cell]
            ),
            expected
        );
    }
    assert_eq!(world.mixture_mass[mixed], 30.0);
    assert_eq!(world.mixture_element[mixed], 5);
}

#[test]
fn hydraulic_tests_new_production_liquid_phase_preserves_gas_and_immiscible_inventory() {
    let mut table = elements();
    let mut gas = table.elements[LIQUID as usize].clone();
    gas.record.state = 1;
    gas.record.id = 0x12345678;
    gas.record.specific_heat_capacity = 0.5;
    table.elements.push(gas);
    let mut other = table.elements[LIQUID as usize].clone();
    other.record.id = 0x23456789;
    other.record.specific_heat_capacity = 2.5;
    table.elements.push(other);
    let mut world = solid_world(34, 10, &table);
    for y in 2..=8 {
        for x in 2..=33 {
            let (element, mass, temperature) = if x == 17 || y > 5 {
                (4, 1.0, 300.0)
            } else if x < 17 {
                (LIQUID, CAPACITY * if y == 2 { 1.03 } else { 1.0 }, 1800.0)
            } else {
                (5, CAPACITY, 1200.0)
            };
            set(&mut world, x, y, element, mass, temperature);
        }
    }
    let inventory = |world: &PhysicsWorld| {
        let mut mass = [0.0_f64; 6];
        let mut heat = 0.0;
        for cell in 0..world.write.mass.len() {
            world.for_each_cell_species(cell, |element, amount| {
                let element = element as usize;
                let m = f64::from(amount);
                mass[element] += m;
                heat += m * f64::from(world.write.temperature[cell])
                    * f64::from(table.elements[element].record.specific_heat_capacity);
            });
        }
        (mass, heat)
    };
    let before = inventory(&world);
    let mut batched = solid_world(34, 10, &table);
    batched.write = world.write.clone();
    advance(&mut world, &table, 100);
    advance_regions(&mut batched, &table, 100, false, None);
    assert_eq!(world.write.element, batched.write.element);
    assert_eq!(
        world.write.mass, batched.write.mass,
        "batch flow accumulator changed physical mass"
    );
    assert_eq!(world.write.temperature, batched.write.temperature);
    let after = inventory(&world);
    for element in [LIQUID as usize, 4, 5] {
        assert!(
            (after.0[element] - before.0[element]).abs() < before.0[element] * 0.00002,
            "element {element} inventory changed: {:?} -> {:?}",
            before.0,
            after.0
        );
    }
    assert!(
        (after.1 - before.1).abs() < before.1 * 0.00002,
        "gas/immiscible boundary heat changed: {} -> {}",
        before.1,
        after.1
    );
}

#[test]
fn hydraulic_tests_new_pressure_scratch_reuse_matches_fresh_across_worlds_and_regions() {
    let table = elements();
    let mut reused = crate::fluid_pressure::PressureScratch::default();
    let diseases = DiseaseTable::default();
    for (width, height, partial_region) in
        [(34, 12, false), (7, 7, true), (49, 17, true), (7, 7, false)]
    {
        let make_world = || {
            let mut world = solid_world(width, height, &table);
            for y in 2..=4 {
                for x in 2..width {
                    set(
                        &mut world,
                        x,
                        y,
                        LIQUID,
                        CAPACITY + x as f32 * 5.0,
                        1000.0 + y as f32 * 100.0,
                    );
                }
            }
            world.sync_read_from_write();
            world
        };
        let region = Region {
            min_x: if partial_region { 3 } else { 1 },
            max_x: width as i32 + 1,
            min_y: if partial_region { 3 } else { 1 },
            max_y: height as i32 + 1,
            ..Region::default()
        };
        let mut a = make_world();
        let mut b = make_world();
        let mut fresh = crate::fluid_pressure::PressureScratch::default();
        let _ = reused.run(
            &mut a,
            &table,
            &diseases,
            &mut EventQueues::default(),
            region,
        );
        let _ = fresh.run(
            &mut b,
            &table,
            &diseases,
            &mut EventQueues::default(),
            region,
        );
        assert_eq!(a.write.element, b.write.element);
        assert_eq!(a.write.mass, b.write.mass);
        assert_eq!(a.write.temperature, b.write.temperature);
        assert_eq!(a.flow, b.flow);
        assert_eq!(reused.covered, fresh.covered);
    }
}

#[test]
fn hydraulic_tests_new_32_wide_pool_across_two_regions_levels_and_conserves() {
    let table = elements();
    let mut results = Vec::new();
    for mirrored in [false, true] {
        let mut world = wide_pool(&table, mirrored);
        let before = totals(&world);
        advance_regions(&mut world, &table, 1000, false, Some(18));
        conservation(before, &world);
        let columns: Vec<_> = (2..=33).map(|x| column(&world, x)).collect();
        let low = columns.iter().copied().fold(f64::INFINITY, f64::min);
        let high = columns.iter().copied().fold(f64::NEG_INFINITY, f64::max);
        eprintln!(
            "new two-region 32-wide pool mirror={mirrored}: left={} right={} range={}",
            columns[0],
            columns[31],
            high - low
        );
        assert!(
            high - low < f64::from(CAPACITY) * 0.05,
            "split-region pool uneven: {columns:?}"
        );
        results.push(columns);
    }
    for x in 0..32 {
        assert!(
            (results[0][x] - results[1][31 - x]).abs() < 0.05,
            "split-region mirror differs at {x}: {} vs {}",
            results[0][x],
            results[1][31 - x]
        );
    }
}

#[test]
fn hydraulic_tests_new_displacement_uses_inventory_not_batch_flow() {
    let mut table = elements();
    let mut other = table.elements[LIQUID as usize].clone();
    other.record.id = 0x23456789;
    table.elements.push(other);
    let mut amounts = Vec::new();
    for displayed in [0.0, 1000.0, 100000.0] {
        let mut world = solid_world(8, 5, &table);
        set(&mut world, 3, 2, LIQUID, 3000.0, TEMPERATURE);
        set(&mut world, 4, 2, 4, 100.0, TEMPERATURE);
        set(&mut world, 5, 2, 4, 100.0, TEMPERATURE);
        world.sync_read_from_write();
        let a = 2 * world.stride + 3;
        world.flow[a] = [displayed; 4];
        let amount = crate::fluid::do_liquid_pressure_displacement(
            &mut world,
            &table,
            &DiseaseTable::default(),
            &mut EventQueues::default(),
            LIQUID,
            a,
            a + 1,
            a + 2,
        );
        amounts.push(amount);
        assert_eq!(world.write.mass[a], 2625.0);
    }
    assert_eq!(amounts, vec![375.0; 3]);
}

#[test]
fn hydraulic_tests_new_pressure_cost_samples_preserve_inventory() {
    let table = elements();
    for transient in [false, true] {
        // ABBA order with fresh state, one warmup and 16 timed samples each.
        // Both modes share graph construction, residual gate, conservative
        // transport and notification filtering. This compares coordinates,
        // not the original main liquid phase or rendered game performance.
        // Run this test alone with --test-threads=1 for isolated costs.
        for (order, reference_coordinates) in [true, false, false, true].into_iter().enumerate() {
            let mut world = solid_world(130, 70, &table);
            for y in 2..=33 {
                for x in 2..=129 {
                    let head = if transient && x > 65 { 33.0 } else { 34.0 };
                    let h = head - y as f32;
                    let mass = if h >= 1.0 {
                        CAPACITY * (1.0 + 0.01 * (h - 1.0))
                    } else {
                        CAPACITY * h
                    };
                    set(&mut world, x, y, LIQUID, mass.max(0.0), TEMPERATURE);
                }
            }
            let before = totals(&world);
            let region = Region {
                min_x: 1,
                max_x: 131,
                min_y: 1,
                max_y: 71,
                ..Region::default()
            };
            let mut scratch = crate::fluid_pressure::PressureScratch::default();
            scratch.reference_coordinates = reference_coordinates;
            let mut events = EventQueues::default();
            let mut milliseconds = Vec::new();
            let mut sweeps = Vec::new();
            let mut maximum_residual: f64 = 0.0;
            let mut scaled = 0;
            let mut minimum_scale: f64 = 1.0;
            let mut noops = 0;
            for sample in 0..17 {
                world.sync_read_from_write();
                events.clear();
                let start = std::time::Instant::now();
                let _ = scratch.run(
                    &mut world,
                    &table,
                    &DiseaseTable::default(),
                    &mut events,
                    region,
                );
                if sample > 0 {
                    milliseconds.push(start.elapsed().as_secs_f64() * 1000.0);
                    sweeps.push(scratch.last_sweeps);
                    maximum_residual = maximum_residual.max(scratch.last_committed_residual);
                    scaled += scratch.last_scaled_donors;
                    minimum_scale = minimum_scale.min(scratch.last_min_scale);
                    noops += scratch.last_noop_transfers;
                }
            }
            conservation(before, &world);
            milliseconds.sort_by(f64::total_cmp);
            eprintln!(
                "native pressure only, ABBA order={order}, reference={reference_coordinates}, <=4096 wet cells, 9100 scanned cells, transient={transient}, 1 warmup + 16 samples: median={:.6}ms p95(nearest-rank)={:.6}ms sweeps={sweeps:?}; not whole-game FPS",
                (milliseconds[7] + milliseconds[8]) * 0.5,
                milliseconds[15]
            );
            eprintln!(
                "committed max residual={maximum_residual}kg, scaled donors={scaled}, min_scale={minimum_scale}, unchanged transfers={noops}"
            );
        }
    }
}

#[test]
fn hydraulic_tests_new_displacement_carries_current_heat_and_germs() {
    let mut table = elements();
    let mut other = table.elements[LIQUID as usize].clone();
    other.record.id = 0x23456789;
    table.elements.push(other);
    let mut world = solid_world(8, 5, &table);
    set(&mut world, 3, 2, LIQUID, 3000.0, 1000.0);
    set(&mut world, 4, 2, 4, 100.0, 500.0);
    set(&mut world, 5, 2, 4, 100.0, 500.0);
    let a = 2 * world.stride + 3;
    world.write.disease_idx[a] = 0;
    world.write.disease_count[a] = 800;
    world.sync_read_from_write();
    world.write.mass[a] = 4000.0;
    world.write.temperature[a] = 1800.0;
    world.write.disease_idx[a] = 1;
    world.write.disease_count[a] = 1600;
    let heat = |world: &PhysicsWorld| -> f64 {
        (a..=a + 2)
            .map(|cell| {
                f64::from(world.write.mass[cell]) * f64::from(world.write.temperature[cell])
            })
            .sum()
    };
    let before = heat(&world);
    let amount = crate::fluid::do_liquid_pressure_displacement(
        &mut world,
        &table,
        &DiseaseTable::default(),
        &mut EventQueues::default(),
        LIQUID,
        a,
        a + 1,
        a + 2,
    );
    assert_eq!(amount, 500.0);
    assert_eq!(world.write.temperature[a + 1], 1800.0);
    assert_eq!(world.write.disease_idx[a + 1], 1);
    assert_eq!(world.write.disease_count[a + 1], 200);
    assert_eq!(world.write.disease_count[a], 1400);
    assert_eq!(heat(&world), before);
}

#[test]
fn hydraulic_tests_new_nearly_dry_channel_conserves_and_converges() {
    let table = elements();
    let mut world = solid_world(7, 5, &table);
    for (offset, mass) in [165.6, 1.84, 18.4].into_iter().enumerate() {
        set(
            &mut world,
            offset + 3,
            2,
            LIQUID,
            mass,
            1000.0 + offset as f32 * 500.0,
        );
        let cell = 2 * world.stride + offset + 3;
        world.write.disease_idx[cell] = 0;
        world.write.disease_count[cell] = 10000;
    }
    let before = totals(&world);
    world.sync_read_from_write();
    let region = Region {
        min_x: 1,
        max_x: 8,
        min_y: 1,
        max_y: 6,
        ..Region::default()
    };
    let mut scratch = crate::fluid_pressure::PressureScratch::default();
    let _ = scratch.run(
        &mut world,
        &table,
        &DiseaseTable::default(),
        &mut EventQueues::default(),
        region,
    );
    assert!(
        scratch.last_scaled_donors > 0,
        "near-dry donor cap was not exercised"
    );
    assert!(
        scratch.last_committed_residual > 10.0,
        "split error should be reported, not hidden"
    );
    eprintln!(
        "near-dry split: scaled={}, min_scale={}, committed residual={}kg",
        scratch.last_scaled_donors, scratch.last_min_scale, scratch.last_committed_residual
    );
    advance(&mut world, &table, 1000);
    conservation(before, &world);
    let columns: Vec<_> = (3..=5).map(|x| column(&world, x)).collect();
    assert!(columns.iter().all(|&m| (m - before.0 / 3.0).abs() < 0.01));
    assert_eq!(world.write.disease_count.iter().sum::<i32>(), 30000);
    for cell in 0..world.write.mass.len() {
        if world.write.mass[cell] > 0.0 {
            assert!((1000.0..=2000.0).contains(&world.write.temperature[cell]));
        }
    }
}

#[test]
fn hydraulic_tests_new_fully_drained_cell_becomes_vacuum() {
    let table = elements();
    for germs in [1, 7] {
        let mut world = solid_world(7, 6, &table);
        set(&mut world, 4, 2, LIQUID, 1000.0, 1000.0);
        set(&mut world, 4, 3, LIQUID, 1.0, 2000.0);
        let top = 3 * world.stride + 4;
        world.write.disease_idx[top] = 0;
        world.write.disease_count[top] = germs;
        let before = totals(&world);
        advance(&mut world, &table, 2);
        conservation(before, &world);
        assert_eq!(world.write.mass[top], 0.0);
        assert_eq!(world.write.element[top], VACUUM);
        assert_eq!(world.write.disease_count.iter().sum::<i32>(), germs);

        let mut forked = solid_world(7, 6, &table);
        for x in [3, 5] {
            set(&mut forked, x, 2, LIQUID, 1000.0, 1000.0);
            set(&mut forked, x, 3, LIQUID, 0.125, 1000.0);
        }
        set(&mut forked, 4, 3, LIQUID, 1.0, 2000.0);
        forked.write.disease_idx[top] = 0;
        forked.write.disease_count[top] = germs;
        let before = totals(&forked);
        advance(&mut forked, &table, 2);
        conservation(before, &forked);
        assert_eq!(
            forked.write.disease_count.iter().sum::<i32>(),
            germs,
            "split drain lost {germs} germs; remaining top mass {}",
            forked.write.mass[top]
        );
    }
}

#[test]
fn hydraulic_tests_new_mixed_cell_uses_all_species_volume_for_flow() {
    let mut table = elements();
    table.elements[LIQUID as usize].record.id = 1836671383; // Water family for this boundary test.
    let mut dirty = table.elements[LIQUID as usize].clone();
    dirty.record.id = 1832607973;
    table.elements.push(dirty);
    let mut world = solid_world(7, 5, &table);
    set(&mut world, 3, 2, LIQUID, 100.05, TEMPERATURE);
    set(&mut world, 4, 2, LIQUID, 100.0, TEMPERATURE);
    let mixed = 2 * world.stride + 3;
    world.mixture_element[mixed] = 4;
    world.mixture_mass[mixed] = 1.0;
    advance(&mut world, &table, 1);
    let mut moved_water = 0.0;
    let mut moved_dirty = 0.0;
    world.for_each_cell_species(mixed + 1, |id, amount| {
        if id == LIQUID { moved_water += amount - 100.0; }
        if id == 4 { moved_dirty += amount; }
    });
    assert!(moved_water > 0.0 && moved_dirty > 0.0);
    assert!((moved_water / moved_dirty - 100.05).abs() < 0.01,
        "all liquid species must use one face fraction");
    let mut total = 0.0;
    for cell in [mixed, mixed + 1] {
        world.for_each_cell_species(cell, |_, amount| total += amount);
    }
    assert!((total - 201.05).abs() < 0.001);
}
