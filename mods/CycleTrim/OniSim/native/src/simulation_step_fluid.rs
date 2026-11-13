//! Ordered pressure/liquid traversal from official UpdateData, ELF 0x69730.

use crate::events::EventQueues;
use rayon::prelude::*;
use crate::tables::{DiseaseTable, ElementTable};
use crate::world::{PhysicsWorld, Region};

thread_local! {
    static LIQUID_TRANSPORT: std::cell::RefCell<crate::liquid_transport::LiquidTransport> =
        std::cell::RefCell::new(crate::liquid_transport::LiquidTransport::default());
    static LIQUID_PRESSURE: std::cell::RefCell<crate::fluid_pressure::PressureScratch> =
        std::cell::RefCell::new(crate::fluid_pressure::PressureScratch::default());
}

#[cfg(test)]
pub(crate) use crate::reference::gas_phase::{gas_phase, gas_phase_with_pressure};

/// Returns false only when no path that mutates CellSOA was reached.
/// Flow updates still execute; true is conservative and need not mean a change.
#[must_use]
#[cfg(test)]
pub(crate) fn liquid_phase(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    diseases: &DiseaseTable,
    events: &mut EventQueues,
    region: Region,
) -> bool {
    liquid_phase_with_flow(world, elements, diseases, events, region, false)
}

pub(crate) fn liquid_phase_with_flow(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    diseases: &DiseaseTable,
    events: &mut EventQueues,
    region: Region,
    flow_already_applied: bool,
) -> bool {
    let width = (region.max_x - region.min_x).max(0) as usize;
    let height = (region.max_y - region.min_y).max(0) as usize;
    let empty = (i32::MAX, i32::MAX, i32::MIN, i32::MIN);
    let merge = |a: (i32, i32, i32, i32), b: (i32, i32, i32, i32)|
        (a.0.min(b.0), a.1.min(b.1), a.2.max(b.2), a.3.max(b.3));
    let row_bounds = |y: i32| {
        let mut bounds = empty;
        for x in region.min_x..region.max_x {
            let cell = y as usize * world.stride + x as usize;
            let liquid = (world.write.mass[cell] > 0.0
                && world.element_phase[world.write.element[cell] as usize] == 2)
                || (world.mixture_mass[cell] > 0.0
                    && world.element_phase[world.mixture_element[cell] as usize] == 2)
                || world.extra_mixtures.get(&cell).is_some_and(|species|
                    species.iter().any(|&(id, mass)| mass > 0.0 && world.element_phase[id as usize] == 2));
            if liquid { bounds = merge(bounds, (x, y, x + 1, y + 1)); }
        }
        bounds
    };
    let bounds = if let Some(pool) = crate::parallel::pool(width.saturating_mul(height)) {
        pool.install(|| (region.min_y..region.max_y).into_par_iter().with_min_len(8)
            .map(row_bounds).reduce(|| empty, merge))
    } else {
        (region.min_y..region.max_y).map(row_bounds).fold(empty, merge)
    };
    if bounds.0 == i32::MAX { return flow_already_applied; }
    // One-cell halo owns the free surface and region-boundary faces. Gas-only
    // rows outside this active rectangle need no pressure or transport sweeps.
    let region = Region {
        min_x: (bounds.0 - 1).max(region.min_x),
        min_y: (bounds.1 - 1).max(region.min_y),
        max_x: (bounds.2 + 1).min(region.max_x),
        max_y: (bounds.3 + 1).min(region.max_y),
        ..region
    };
    static PROFILE: std::sync::OnceLock<bool> = std::sync::OnceLock::new();
    let profiling = *PROFILE.get_or_init(|| std::env::var_os("CYCLETRIM_GPU_PROFILE").is_some());
    let started = profiling.then(std::time::Instant::now);
    LIQUID_PRESSURE.with(|scratch| {
        let mut scratch = scratch.borrow_mut();
        let pressure_changed = scratch.run(world, elements, diseases, events, region);
        let pressure_at = started.map(|start| start.elapsed());
        let transport_changed = LIQUID_TRANSPORT.with(|transport| transport.borrow_mut()
            .run(world, elements, diseases, events, region, &scratch.covered));
        if let (Some(start), Some(pressure)) = (started, pressure_at) {
            eprintln!("[CycleTrim OniSim] liquid detail cells={} nodes={} sweeps={} pressure={:.3}ms transport={:.3}ms",
                width * height, scratch.profile_nodes, scratch.profile_sweeps, pressure.as_secs_f64() * 1000.0,
                (start.elapsed() - pressure).as_secs_f64() * 1000.0);
        }
        flow_already_applied || pressure_changed || transport_changed
    })
}

#[cfg(test)]
mod hydraulic_tests {
    use super::*;

    #[test]
    fn connected_water_rises_into_roofed_vacuum_and_conserves_mass() {
        let mut bytes = 4_i32.to_le_bytes().to_vec();
        for index in 0..4_u16 {
            let mut record = [0_u8; 164];
            let hash = [0xa9360b34_u32, 0x2d39bf75, 0x6d95058c, 0x12345678][index as usize];
            record[..4].copy_from_slice(&hash.to_le_bytes());
            record[8..10].copy_from_slice(&index.to_le_bytes());
            record[10] = [0, 0, 3, 2][index as usize];
            record[12..16].copy_from_slice(&1_f32.to_le_bytes());
            record[40..44].copy_from_slice(&1000_f32.to_le_bytes());
            record[52..56].copy_from_slice(&1000_f32.to_le_bytes());
            bytes.extend_from_slice(&record);
        }
        bytes.extend_from_slice(&[0; 16]);
        let mut elements = ElementTable::parse(&bytes).unwrap();
        let diseases = DiseaseTable { diseases: Vec::new(), element_count: elements.len() };
        let mut world = PhysicsWorld::new(8, 8, 0, &elements).unwrap();
        world.write.element.fill(2);
        world.write.temperature.fill(300.0);
        // A U-shaped connected pool, with a vacuum above the right arm and
        // solid above that. The left arm supplies a higher water surface.
        for (x, y) in [(3, 2), (4, 2), (5, 2), (3, 3), (3, 4), (3, 5)] {
            let cell = y * world.stride + x;
            world.write.element[cell] = 3;
            world.write.mass[cell] = 1000.0;
        }
        let cavity = 3 * world.stride + 5;
        world.write.element[cavity] = world.vacuum_element;
        world.sync_read_from_write();
        let region = Region { min_x: 2, min_y: 2, max_x: 7, max_y: 7, ..Default::default() };
        let before: f32 = world.write.mass.iter().sum();
        let mut events = EventQueues::default();
        assert!(liquid_phase(&mut world, &elements, &diseases, &mut events, region));
        let after: f32 = world.write.mass.iter().sum();
        assert!((before - after).abs() < 0.01, "mass changed: {before} -> {after}");
        for _ in 0..200 {
            world.sync_read_from_write();
            world.flow.fill([0.0; 4]);
            let _ = liquid_phase(&mut world, &elements, &diseases, &mut events, region);
            let mass: f32 = world.write.mass.iter().sum();
            assert!((before - mass).abs() < 0.05, "mass changed during equilibration: {before} -> {mass}");
        }
        assert!(world.write.mass[cavity] > 900.0, "enclosed vacuum should fill, got {}", world.write.mass[cavity]);

        // A disconnected single full cell has no higher reservoir and must
        // not climb into the vacuum above it just because another pool exists.
        let isolated = 4 * world.stride + 6;
        let empty_above = 5 * world.stride + 6;
        world.write.element[isolated] = 3;
        world.write.mass[isolated] = 1000.0;
        world.write.element[empty_above] = world.vacuum_element;
        world.write.mass[empty_above] = 0.0;
        world.sync_read_from_write();
        crate::fluid::update_liquid(&mut world, &elements, &diseases, &mut events, isolated, 0);
        assert_eq!(world.write.mass[empty_above], 0.0, "disconnected water must not borrow another pool's head");

        // Two open arms connected below a solid partition must settle to the
        // same level, not merely admit one transfer into a closed cavity.
        world.write.element.fill(2);
        world.write.mass.fill(0.0);
        for y in 3..7 {
            for x in [3, 5] { world.write.element[y * world.stride + x] = world.vacuum_element; }
        }
        for (x, y) in [(3, 2), (4, 2), (5, 2), (3, 3), (3, 4), (3, 5)] {
            let cell = y * world.stride + x;
            world.write.element[cell] = 3;
            world.write.mass[cell] = 1000.0;
        }
        for _ in 0..1000 {
            world.sync_read_from_write();
            world.flow.fill([0.0; 4]);
            let _ = liquid_phase(&mut world, &elements, &diseases, &mut events, region);
        }
        let left: f32 = (2..7).map(|y| world.write.mass[y * world.stride + 3]).sum();
        let right: f32 = (2..7).map(|y| world.write.mass[y * world.stride + 5]).sum();
        let total: f32 = world.write.mass.iter().sum();
        assert!((total - 6000.0).abs() < 0.1, "mass changed: {total}");
        assert!((left - right).abs() < 100.0, "communicating vessels did not level: left={left}, right={right}");

        // Magma uses a 50 kg horizontal-flow threshold in the game data.
        // A pressure difference below four times that threshold still has
        // to relax instead of becoming a permanently uneven liquid surface.
        elements.elements[3].record.max_mass = 1840.0;
        elements.elements[3].record.viscosity = 60.0;
        elements.elements[3].record.min_horizontal_flow = 50.0;
        world.write.element.fill(2);
        world.write.mass.fill(0.0);
        for (x, mass) in [(3, 1000.0), (4, 800.0)] {
            let cell = 3 * world.stride + x;
            world.write.element[cell] = 3;
            world.write.mass[cell] = mass;
        }
        for _ in 0..100 {
            world.sync_read_from_write();
            world.flow.fill([0.0; 4]);
            let _ = liquid_phase(&mut world, &elements, &diseases, &mut events, region);
        }
        let first = world.write.mass[3 * world.stride + 3];
        let second = world.write.mass[3 * world.stride + 4];
        assert!((first - second).abs() < 2.0, "magma surface stayed uneven: {first} vs {second}");

        world.write.element.fill(2);
        world.write.mass.fill(0.0);
        for y in 3..7 {
            for x in [3, 5] { world.write.element[y * world.stride + x] = world.vacuum_element; }
        }
        for (x, y) in [(3, 2), (4, 2), (5, 2), (3, 3), (3, 4), (3, 5)] {
            let cell = y * world.stride + x;
            world.write.element[cell] = 3;
            world.write.mass[cell] = 1840.0;
        }
        for _ in 0..2000 {
            world.sync_read_from_write();
            world.flow.fill([0.0; 4]);
            let _ = liquid_phase(&mut world, &elements, &diseases, &mut events, region);
        }
        let left: f32 = (2..7).map(|y| world.write.mass[y * world.stride + 3]).sum();
        let right: f32 = (2..7).map(|y| world.write.mass[y * world.stride + 5]).sum();
        assert!((left - right).abs() < 50.0, "magma vessels did not level: left={left}, right={right}");
        let total: f32 = world.write.mass.iter().sum();
        assert!((total - 11040.0).abs() < 0.5, "magma mass changed: {total}");

        // A tall connected column cannot make a mostly empty lower cell
        // eject liquid upward and leave a floating layer.
        world.write.element.fill(2);
        world.write.mass.fill(0.0);
        for (x, y, mass) in [(3, 2, 1840.0), (4, 2, 1840.0), (5, 2, 1840.0),
            (3, 3, 1840.0), (3, 4, 1840.0), (3, 5, 1840.0), (5, 3, 100.0)] {
            let cell = y * world.stride + x;
            world.write.element[cell] = 3;
            world.write.mass[cell] = mass;
        }
        let donor = 3 * world.stride + 5;
        let above = 4 * world.stride + 5;
        world.write.element[above] = world.vacuum_element;
        world.sync_read_from_write();
        crate::fluid::update_liquid(&mut world, &elements, &diseases, &mut events, donor, 0);
        assert_eq!(world.write.mass[above], 0.0, "underfilled magma must settle before rising");

        // A broad pool exposes convergence failures that a two-cell test
        // cannot: the left half starts deeper than the right half.
        let mut pool = PhysicsWorld::new(36, 8, 0, &elements).unwrap();
        pool.write.element.fill(2);
        pool.write.mass.fill(0.0);
        for x in 2..34 {
            let cell = 3 * pool.stride + x;
            pool.write.element[cell] = 3;
            pool.write.mass[cell] = if x < 18 { 1400.0 } else { 400.0 };
            pool.write.element[4 * pool.stride + x] = pool.vacuum_element;
        }
        let pool_region = Region { min_x: 2, min_y: 2, max_x: 34, max_y: 5, ..Default::default() };
        for _ in 0..1000 {
            pool.sync_read_from_write();
            pool.flow.fill([0.0; 4]);
            let _ = liquid_phase(&mut pool, &elements, &diseases, &mut events, pool_region);
        }
        let left = pool.write.mass[3 * pool.stride + 2];
        let right = pool.write.mass[3 * pool.stride + 33];
        assert!((left - right).abs() < 50.0, "broad magma pool did not level: {left} vs {right}");
        let total: f32 = pool.write.mass.iter().sum();
        assert!((total - 28800.0).abs() < 0.5, "broad pool mass changed: {total}");

        // Equal neighbours must receive equal amounts in a single update;
        // traversal order must not make the liquid prefer one direction.
        world.write.element.fill(2);
        world.write.mass.fill(0.0);
        for (x, mass) in [(3, 200.0), (4, 300.0), (5, 200.0)] {
            let cell = 3 * world.stride + x;
            world.write.element[cell] = 3;
            world.write.mass[cell] = mass;
        }
        world.sync_read_from_write();
        world.flow.fill([0.0; 4]);
        let center = 3 * world.stride + 4;
        crate::fluid::update_liquid(&mut world, &elements, &diseases, &mut events, center, 0);
        let left = world.write.mass[center - 1];
        let right = world.write.mass[center + 1];
        assert!((left - right).abs() < 0.001, "horizontal flow is biased: {left} vs {right}");

        // Deep, wide pool: pressure must travel through full lower rows as
        // well as diffuse along a single partially-filled surface row.
        let mut deep = PhysicsWorld::new(36, 10, 0, &elements).unwrap();
        deep.write.element.fill(2);
        deep.write.mass.fill(0.0);
        for x in 2..34 {
            for y in 2..9 {
                let cell = y * deep.stride + x;
                deep.write.element[cell] = deep.vacuum_element;
                if y < if x < 18 { 4 } else { 6 } {
                    deep.write.element[cell] = 3;
                    deep.write.mass[cell] = 1840.0;
                }
            }
        }
        let deep_region = Region { min_x: 2, min_y: 2, max_x: 34, max_y: 9, ..Default::default() };
        for _ in 0..1000 {
            deep.sync_read_from_write();
            deep.flow.fill([0.0; 4]);
            let _ = liquid_phase(&mut deep, &elements, &diseases, &mut events, deep_region);
        }
        let column_mass = |x: usize| -> f32 { (2..9).map(|y| deep.write.mass[y * deep.stride + x]).sum() };
        let left = column_mass(3);
        let right = column_mass(32);
        assert!((left - right).abs() < 500.0, "deep pool did not level: {left} vs {right}");
        let total: f32 = deep.write.mass.iter().sum();
        assert!((total - 176640.0).abs() < 2.0, "deep pool mass changed: {total}");
    }
}
