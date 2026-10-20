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

fn pressure_target(
    world: &PhysicsWorld,
    elements: &ElementTable,
    source_element: u16,
    source_phase: u8,
    cell: usize,
) -> bool {
    let element = world.read.element[cell];
    let state = elements.elements[element as usize].record.state;
    world.read.properties[cell] & 1 == 0
        && world.write.element[cell] == element
        && state & 2 == 0
        && (state & 3 != source_phase || element == source_element)
}

fn pressure_cell(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    diseases: &DiseaseTable,
    events: &mut EventQueues,
    cell: usize,
) {
    let element = world.read.element[cell];
    let record = &elements.elements[element as usize].record;
    let source_phase = record.state & 3;
    let coefficient = record.flow;
    if world.read.properties[cell] & 1 != 0
        || world.write.element[cell] != element
        || record.state & 2 != 0
    {
        return;
    }
    let direction = world.scan_direction;
    let side = cell.wrapping_add_signed(direction as isize);
    let horizontal = if pressure_target(world, elements, element, source_phase, side) {
        direction as f32
            * crate::gas::update_pressure(
                world,
                elements,
                diseases,
                events,
                cell,
                element,
                source_phase,
                coefficient,
                side,
            )
    } else {
        0.0
    };
    world.flow[cell][1] += horizontal;
    world.flow[side][0] -= horizontal;
    let upper = cell + world.stride;
    let vertical = if world.write.element[cell] == element
        && pressure_target(world, elements, element, source_phase, upper)
    {
        crate::gas::update_pressure(
            world,
            elements,
            diseases,
            events,
            cell,
            element,
            source_phase,
            coefficient,
            upper,
        )
    } else {
        0.0
    };
    world.flow[cell][2] += vertical;
    world.flow[upper][3] -= vertical;
    if (old_phase(world, elements, upper) | old_phase(world, elements, side)) != 3
        && world.write.element[cell] == element
        && pressure_target(world, elements, element, source_phase, side + world.stride)
    {
        crate::gas::update_pressure(
            world,
            elements,
            diseases,
            events,
            cell,
            element,
            source_phase,
            coefficient,
            side + world.stride,
        );
    }
}

/// Native pressure reverse scans begin at x=max_x and stop before x=min_x;
/// the forward scan begins at min_x and stops before max_x. This selects each
/// horizontal edge from the side indicated by scan_direction.
pub(crate) fn gas_phase(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    diseases: &DiseaseTable,
    events: &mut EventQueues,
    region: Region,
) {
    for y in region.min_y..region.max_y {
        let row = y as usize * world.stride;
        let (mut cell, stop) = if world.scan_direction > 0 {
            (row + region.min_x as usize, row + region.max_x as usize)
        } else {
            (row + region.max_x as usize, row + region.min_x as usize)
        };
        while cell != stop {
            pressure_cell(world, elements, diseases, events, cell);
            cell = cell.wrapping_add_signed(world.scan_direction as isize);
        }
    }
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

pub(crate) fn liquid_phase(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    diseases: &DiseaseTable,
    events: &mut EventQueues,
    region: Region,
) {
    if region.min_x == region.max_x {
        return;
    }
    for y in region.min_y..region.max_y {
        for x in region.min_x..region.max_x {
            let cell = y as usize * world.stride + x as usize;
            if old_phase(world, elements, cell) == 2 && world.read.mass[cell] > 0.0 {
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
}
