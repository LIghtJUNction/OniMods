use super::Rectangle;
use crate::world::{Cells, PhysicsWorld};

fn primary(cells: &mut Cells, index: usize, element: u16, mass: f32, radiation: bool) {
    cells.element[index] = element;
    cells.mass[index] = mass;
    cells.temperature[index] = 0.0;
    cells.disease_idx[index] = u8::MAX;
    cells.disease_count[index] = 0;
    if radiation {
        cells.radiation[index] = 0.0;
    }
    // Official topology handlers retain properties, insulation, strength and
    // the infestation/growth bookkeeping arrays in these existing cells.
}

fn backwall(world: &mut PhysicsWorld, index: usize) {
    world.backwall_element[index] = world.vacuum_element;
    world.backwall_mass[index] = 0.0;
    world.backwall_temperature[index] = 0.0;
}

pub(super) fn clear_all_primary(world: &mut PhysicsWorld) {
    // ClearUnoccupiedCells 0x315d0 does not touch the backwall arrays.
    let vacuum = world.vacuum_element;
    for index in 0..world.stride * world.padded_height {
        primary(&mut world.read, index, vacuum, 0.0, true);
        primary(&mut world.write, index, vacuum, 0.0, true);
    }
}

pub(super) fn initialize_rectangle(world: &mut PhysicsWorld, rect: Rectangle) {
    let (x, y) = (rect.x as usize, rect.y as usize);
    let (width, height) = (
        (rect.width as i64 + 2) as usize,
        (rect.height as i64 + 2) as usize,
    );
    for row in 0..height {
        for column in 0..width {
            let index = (y + row) * world.stride + x + column;
            let boundary = row == 0 || row + 1 == height || column == 0 || column + 1 == width;
            let (element, mass) = if boundary {
                (world.boundary_element, 9999.0)
            } else {
                (world.vacuum_element, 0.0)
            };
            // Every rectangular halo edge is Unobtanium, including the top.
            // This differs from InitializeBoundary's global Vacuum top edge.
            primary(&mut world.read, index, element, mass, !boundary);
            primary(&mut world.write, index, element, mass, !boundary);
            backwall(world, index);
        }
    }
}

pub(super) fn free_rectangle(world: &mut PhysicsWorld, rect: Rectangle) {
    let (x, y) = (rect.x as usize, rect.y as usize);
    let (width, height) = (
        (rect.width as i64 + 2) as usize,
        (rect.height as i64 + 2) as usize,
    );
    let vacuum = world.vacuum_element;
    for row in 0..height {
        for column in 0..width {
            let index = (y + row) * world.stride + x + column;
            primary(&mut world.read, index, vacuum, 0.0, true);
            primary(&mut world.write, index, vacuum, 0.0, true);
            backwall(world, index);
        }
    }
}
