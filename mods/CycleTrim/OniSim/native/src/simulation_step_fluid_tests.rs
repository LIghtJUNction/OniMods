//! The unguarded post-pressure traversal is the frozen baseline dispatch.
//! Compare state and notifications, including native displacement's extra rows.

use super::*;
use crate::native_chain_tests::snapshot;

const VACUUM: u16 = 0;
const SOLID: u16 = 1;
const LIQUID: u16 = 2;
const GAS: u16 = 4;

fn table() -> ElementTable {
    let mut bytes = 5_i32.to_le_bytes().to_vec();
    for (index, (hash, phase)) in [
        (0x2d39bf75_u32, 0), (0x6d95058c, 3), (0x01234567, 2),
        (0xa9360b34, 0), (0x13572468, 1),
    ].into_iter().enumerate() {
        let mut record = [0; 164];
        record[..4].copy_from_slice(&hash.to_le_bytes());
        record[4..8].fill(255);
        record[8..10].copy_from_slice(&(index as u16).to_le_bytes());
        record[10] = phase;
        record[84..88].fill(255);
        for (offset, value) in [(12, 1.0_f32), (20, 32.0), (36, 1.0),
            (40, 100.0), (44, 0.1), (48, 0.1), (52, 1000.0), (60, 10000.0)]
        {
            record[offset..offset + 4].copy_from_slice(&value.to_le_bytes());
        }
        bytes.extend_from_slice(&record);
    }
    bytes.extend_from_slice(&[0; 20]);
    ElementTable::parse(&bytes).unwrap()
}

fn fixture(table: &ElementTable, case: usize) -> PhysicsWorld {
    let mut world = PhysicsWorld::new(12, 12, 42, table).unwrap();
    world.write.element.fill(SOLID);
    world.write.temperature.fill(300.0);
    world.falling_disabled = true;
    for y in 1..=12 {
        for x in 1..=12 {
            let cell = y * world.stride + x;
            world.write.element[cell] = if case % 2 == 0 { GAS } else { VACUUM };
            world.write.mass[cell] = if case % 2 == 0 { 1.0 } else { 0.0 };
            world.flow[cell] = [-0.0, 0.0, 0.125, -0.25];
        }
    }
    if case >= 2 {
        let y = if case == 2 { 4 } else { 9 };
        let cell = y * world.stride + 5;
        world.write.element[cell] = LIQUID;
        world.write.mass[cell] = match case {
            3 => 0.0,
            4 => f32::NAN,
            5 => f32::INFINITY,
            _ => 1100.0,
        };
        if case == 6 { world.write.properties[cell] = 2; }
        if case == 7 {
            world.mixture_element[cell] = GAS;
            world.mixture_mass[cell] = 0.25;
        }
        if case == 8 { world.extra_mixtures.insert(cell, vec![(GAS, 0.25)]); }
        if case == 9 { world.write.temperature[cell] = f32::NAN; }
        if case == 10 {
            world.write.element[cell + 1] = LIQUID;
            world.write.mass[cell + 1] = 800.0;
        }
    }
    world.sync_read_from_write();
    world
}

fn reference(
    world: &mut PhysicsWorld, table: &ElementTable, events: &mut EventQueues,
    region: Region, flow_already_applied: bool,
) -> bool {
    let mut pressure = crate::fluid_pressure::PressureScratch::default();
    let changed = pressure.run(world, table, &DiseaseTable::default(), events, region);
    // Original f40858e always enters this helper, regardless of pressure eligibility.
    liquid_phase_after_pressure(world, table, &DiseaseTable::default(), events,
        region, flow_already_applied || changed, &pressure.covered)
}

#[test]
fn liquid_absence_guard_matches_unguarded_phase_for_native_bounds() {
    let table = table();
    let whole = Region { min_x: 1, max_x: 13, min_y: 1, max_y: 13, ..Default::default() };
    let regions = [whole, Region { min_y: 8, max_y: 11, ..whole },
        Region { min_x: 5, max_x: 6, min_y: 8, max_y: 11, ..whole },
        Region { min_x: 5, max_x: 5, min_y: 8, max_y: 11, ..whole },
        Region { min_y: 8, max_y: 8, ..whole }];
    for case in 0..11 {
        for region in regions {
            for direction in [-1, 1] {
                for flow in [false, true] {
                    let mut actual = fixture(&table, case);
                    let mut expected = fixture(&table, case);
                    actual.scan_direction = direction;
                    expected.scan_direction = direction;
                    let mut actual_events = EventQueues::default();
                    let mut expected_events = EventQueues::default();
                    let result = liquid_phase_with_flow(&mut actual, &table, &DiseaseTable::default(),
                        &mut actual_events, region, flow);
                    let expected_result = reference(&mut expected, &table, &mut expected_events, region, flow);
                    assert_eq!(result, expected_result, "case={case} direction={direction} flow={flow}");
                    snapshot::assert_equal("liquid phase world", &snapshot::world(&actual), &snapshot::world(&expected));
                    snapshot::assert_equal("liquid phase events", &snapshot::events(&actual_events), &snapshot::events(&expected_events));
                }
            }
        }
    }
}

#[test]
fn liquid_presence_counts_ineligible_cells_and_resets_after_previous_region() {
    let table = table();
    let region = Region { min_x: 1, max_x: 13, min_y: 8, max_y: 12, ..Default::default() };
    let mut pressure = crate::fluid_pressure::PressureScratch::default();
    for case in [10, 0, 3, 0, 4, 0, 5, 0, 6, 0, 7, 0, 8, 0, 9, 0] {
        let mut world = fixture(&table, case);
        let mut events = EventQueues::default();
        pressure.run(&mut world, &table, &DiseaseTable::default(), &mut events, region);
        assert_eq!(pressure.has_primary_liquid, case >= 3, "case={case}");
        if case == 0 {
            assert_eq!(pressure.last_sweeps, 0);
            assert!(pressure.covered.iter().all(|&face| face == 0));
        }
    }
}

#[test]
fn liquid_only_in_native_expanded_rows_must_reach_displacement() {
    let table = table();
    let region = Region { min_x: 1, max_x: 13, min_y: 8, max_y: 11, ..Default::default() };
    let mut world = fixture(&table, 2);
    let mut pressure = crate::fluid_pressure::PressureScratch::default();
    let mut events = EventQueues::default();
    assert!(!pressure.run(&mut world, &table, &DiseaseTable::default(), &mut events, region));
    assert!(!pressure.has_primary_liquid);
    assert!(extra_displacement_rows_have_liquid(&world, &table, region));
    assert!(liquid_phase_with_flow(&mut world, &table, &DiseaseTable::default(), &mut events, region, false));
}
