//! Ordered pressure/liquid traversal from official UpdateData, ELF 0x69730.

use crate::events::EventQueues;
use crate::tables::{DiseaseTable, ElementTable};
use crate::world::{PhysicsWorld, Region};

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
    let mut changed = crate::pressure_sweep::run(world, elements, diseases, events, region);
    let (min_x, max_x, min_y, max_y) = displacement_bounds(world, region);
    for y in min_y..max_y {
        for x in min_x..max_x {
            let cell = y as usize * world.stride + x as usize;
            if old_phase(world, elements, cell) != 1 {
                continue;
            }
            changed = true;
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

/// Returns false only when no path that mutates CellSOA was reached.
/// Flow updates still execute; true is conservative and need not mean a change.
#[must_use]
pub(crate) fn liquid_phase(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    diseases: &DiseaseTable,
    events: &mut EventQueues,
    region: Region,
) -> bool {
    let mut changed = false;
    if region.min_x == region.max_x {
        return false;
    }
    for y in region.min_y..region.max_y {
        for x in region.min_x..region.max_x {
            let cell = y as usize * world.stride + x as usize;
            if old_phase(world, elements, cell) == 2 && world.read.mass[cell] > 0.0 {
                changed = true;
                crate::fluid::update_liquid(world, elements, diseases, events, cell);
            }
        }
    }
    let (min_x, max_x, min_y, max_y) = displacement_bounds(world, region);
    for y in min_y..max_y {
        for x in min_x..max_x {
            let cell = y as usize * world.stride + x as usize;
            if old_phase(world, elements, cell) != 2 {
                continue;
            }
            changed = true;
            let element = world.read.element[cell];
            let direction = world.scan_direction as isize;
            let first = cell.wrapping_add_signed(-direction);
            if world.read.element[first] != element {
                let amount = crate::fluid::do_liquid_pressure_displacement(
                    world,
                    elements,
                    diseases,
                    events,
                    element,
                    cell,
                    first,
                    first.wrapping_add_signed(-direction),
                );
                world.flow[cell][0] += amount;
                world.flow[cell - 1][1] -= amount;
            }
            let second = cell.wrapping_add_signed(direction);
            if world.read.element[second] != element {
                let amount = crate::fluid::do_liquid_pressure_displacement(
                    world,
                    elements,
                    diseases,
                    events,
                    element,
                    cell,
                    second,
                    second.wrapping_add_signed(direction),
                );
                // The destination flow slot is literally cell-1 in both
                // horizontal branches (ELF 0x6a857 and 0x6a8bd).
                world.flow[cell][0] -= amount;
                world.flow[cell - 1][1] += amount;
            }
            let upper = cell + world.stride;
            let active_element = world.write.element[cell];
            if world.read.element[upper] != element
                && elements.elements[active_element as usize].record.max_mass
                    <= world.write.mass[cell]
            {
                let amount = crate::fluid::do_liquid_pressure_displacement(
                    world,
                    elements,
                    diseases,
                    events,
                    element,
                    cell,
                    upper,
                    upper + world.stride,
                );
                world.flow[cell][3] += amount;
                world.flow[upper][2] -= amount;
            }
        }
    }
    changed
}
