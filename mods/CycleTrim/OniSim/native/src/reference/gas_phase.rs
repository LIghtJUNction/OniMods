//! Legacy pressure/displacement phase retained only for reference tests.
use crate::{events::EventQueues, tables::{DiseaseTable, ElementTable}, world::{PhysicsWorld, Region}};

fn old_phase(world: &PhysicsWorld, elements: &ElementTable, cell: usize) -> u8 {
    elements.elements[world.read.element[cell] as usize]
        .record
        .state
        & 3
}

/// Native pressure reverse scans begin at x=max_x and stop before x=min_x;
/// the forward scan begins at min_x and stops before max_x. This selects each
/// horizontal edge from the side indicated by scan_direction.
/// Returns false only when no path that mutates CellSOA was reached.
/// Flow updates still execute; true is conservative and need not mean a change.
#[must_use]
pub(crate) fn gas_phase(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    diseases: &DiseaseTable,
    events: &mut EventQueues,
    region: Region,
) -> bool {
    gas_phase_with_pressure(world, elements, diseases, events, region, false, false)
}

pub(crate) fn gas_phase_with_pressure(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    diseases: &DiseaseTable,
    events: &mut EventQueues,
    region: Region,
    pressure_already_applied: bool,
    displacement_unneeded: bool,
) -> bool {
    let mut changed = pressure_already_applied || crate::pressure_sweep::run(world, elements, diseases, events, region);
    if displacement_unneeded { return changed; }
    let (min_x, max_x, min_y, max_y) = displacement_bounds(world, region);
    for y in min_y..max_y {
        for x in min_x..max_x {
            let cell = y as usize * world.stride + x as usize;
            if old_phase(world, elements, cell) != 1 {
                continue;
            }
            let element = world.read.element[cell];
            let direction = world.scan_direction as isize;
            for offset in [
                -(world.stride as isize),
                -direction,
                direction,
                world.stride as isize,
            ] {
                let target = cell.wrapping_add_signed(offset);
                if element == world.read.element[target]
                    || world.read.properties[target] & 1 != 0
                    || old_phase(world, elements, target) != 1
                {
                    continue;
                }
                // Homogeneous settled gas has no displacement work. Only a
                // reached mixed-gas helper can require the post-phase copy.
                changed = true;
                let amount = crate::gas::do_gas_pressure_displacement(
                    world,
                    elements,
                    diseases,
                    events,
                    element,
                    cell,
                    target,
                    target.wrapping_add_signed(offset),
                );
                // All four directions use these slots in the official machine
                // code, including the two horizontal displacements.
                world.flow[cell][3] -= amount;
                world.flow[target][2] += amount;
            }
        }
    }
    changed
}

// Both displacement loops use MIN for the lower y bound. Preserve the literal
// native rule rather than substituting a symmetric two-cell margin.
fn displacement_bounds(world: &PhysicsWorld, region: Region) -> (i32, i32, i32, i32) {
    let min_x = region.min_x.max(3);
    let count =
        (region.max_x - region.min_x).min(region.max_x.min(world.stride as i32 - 3) - min_x);
    (
        min_x,
        min_x + count,
        region.min_y.min(3),
        region.max_y.min(world.padded_height as i32 - 3),
    )
}
