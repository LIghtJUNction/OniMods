//! Serial pressure traversal with storage borrowed once for the whole sweep.
//! No field can resize while this view is alive. All arithmetic and notification
//! ordering comes from the existing transfer and traversal implementations.
use crate::events::EventQueues;
use crate::fluid_helpers::{cvtt, smax, smin};
use crate::tables::{DiseaseTable, ElementTable};
use crate::world::{PhysicsWorld, Region};

struct ReadPressure<'a> {
    element: &'a [u16],
    mass: &'a [f32],
    properties: &'a [u8],
    disease_count: &'a [i32],
}
struct WritePressure<'a> {
    element: &'a mut [u16],
    mass: &'a mut [f32],
    temperature: &'a mut [f32],
    disease_idx: &'a mut [u8],
    disease_count: &'a mut [i32],
    disease_infestation: &'a mut [u8],
    disease_growth: &'a mut [f32],
}
struct PressureSweep<'a> {
    read: ReadPressure<'a>,
    write: WritePressure<'a>,
    flow: &'a mut [[f32; 4]],
    dirty: &'a mut [bool],
    dirty_flags: &'a mut [u8],
    width: usize,
    height: usize,
    stride: usize,
    sink_element: u16,
    scan_direction: i32,
}
impl<'a> PressureSweep<'a> {
    #[inline(always)]
    fn new(world: &'a mut PhysicsWorld) -> Self {
        let count = world.read.element.len();
        Self {
            read: ReadPressure {
                element: &world.read.element[..count],
                mass: &world.read.mass[..count],
                properties: &world.read.properties[..count],
                disease_count: &world.read.disease_count[..count],
            },
            write: WritePressure {
                element: &mut world.write.element[..count],
                mass: &mut world.write.mass[..count],
                temperature: &mut world.write.temperature[..count],
                disease_idx: &mut world.write.disease_idx[..count],
                disease_count: &mut world.write.disease_count[..count],
                disease_infestation: &mut world.write.disease_infestation[..count],
                disease_growth: &mut world.write.disease_growth[..count],
            },
            flow: &mut world.flow[..count],
            dirty: &mut world.dirty[..count],
            dirty_flags: &mut world.dirty_flags[..count],
            width: world.width,
            height: world.height,
            stride: world.stride,
            sink_element: world.sink_element,
            scan_direction: world.scan_direction,
        }
    }

    #[inline(always)]
    fn notify(&mut self, events: &mut EventQueues, cell: usize) {
        // Keep the official linear projection, including padded edge semantics.
        if let Some(index) =
            crate::fluid_helpers::game_index_for_shape(self.width, self.height, self.stride, cell)
        {
            events.substance_change(index);
        }
        self.dirty[cell] = true;
        self.dirty_flags[cell] |= 0x1f;
    }
}

#[inline(always)]
fn old_phase(world: &PressureSweep<'_>, elements: &ElementTable, cell: usize) -> u8 {
    elements.elements[world.read.element[cell] as usize]
        .record
        .state
        & 3
}

#[inline(always)]
fn pressure_target(
    world: &PressureSweep,
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

#[inline(always)]
fn pressure_cell(
    world: &mut PressureSweep,
    elements: &ElementTable,
    diseases: &DiseaseTable,
    events: &mut EventQueues,
    cell: usize,
) -> bool {
    let element = world.read.element[cell];
    let record = &elements.elements[element as usize].record;
    let source_phase = record.state & 3;
    let coefficient = record.flow;
    if world.read.properties[cell] & 1 != 0
        || world.write.element[cell] != element
        || record.state & 2 != 0
    {
        return false;
    }
    let direction = world.scan_direction;
    let side = cell.wrapping_add_signed(direction as isize);
    let horizontal = if pressure_target(world, elements, element, source_phase, side) {
        direction as f32
            * update_pressure(
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
        update_pressure(
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
        update_pressure(
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
    // All cell-mutating pressure calls are below the early-return guard.
    // Conservatively retain the copy even if no target accepted a transfer.
    true
}

// ELF 0x6f120: the returned flow retains its original sign after source swapping.
#[inline(always)]
fn update_pressure(
    world: &mut PressureSweep,
    tables: &ElementTable,
    diseases: &DiseaseTable,
    events: &mut EventQueues,
    a: usize,
    element_a: u16,
    phase_a: u8,
    coefficient: f32,
    b: usize,
) -> f32 {
    // Preflight already enforces equal CellSOA lengths. Reborrow every field
    // with one common checked length so LLVM can share index proofs. No
    // unchecked indexing, temporary cell copies or arithmetic changes.
    let count = world.read.element.len();
    assert!(
        a < count && b < count,
        "pressure pair is outside cell storage"
    );
    let read_element = &world.read.element[..count];
    let read_mass = &world.read.mass[..count];
    let read_disease_count = &world.read.disease_count[..count];
    let write_element = &mut world.write.element[..count];
    let write_mass = &mut world.write.mass[..count];
    let write_temperature = &mut world.write.temperature[..count];
    let write_disease_idx = &mut world.write.disease_idx[..count];
    let write_disease_count = &mut world.write.disease_count[..count];
    let write_disease_infestation = &mut world.write.disease_infestation[..count];
    let write_disease_growth = &mut world.write.disease_growth[..count];

    let element_b = read_element[b];
    let record_b = &tables.elements[element_b as usize].record;
    let ma = read_mass[a];
    let mb = read_mass[b];
    let coefficient = if phase_a == 1 {
        coefficient
    } else {
        record_b.flow
    };
    let flow = smax(smin(coefficient * (ma - mb), 0.125 * ma), -0.125 * mb);
    let (src, dst, old_mass, amount, source_element, dest_element, dest_phase) = if flow < 0.0 {
        (b, a, mb, -flow, element_b, element_a, phase_a)
    } else {
        (a, b, ma, flow, element_a, element_b, record_b.state & 3)
    };
    let amount = smin(amount, old_mass);
    let fraction = smax(smin(amount / old_mass, 1.0), 0.0);
    let germ_count = cvtt(read_disease_count[src] as f32 * fraction);
    let germ_id = write_disease_idx[src];
    let source_temperature = write_temperature[src];
    let dest_temperature = write_temperature[dst];
    let dest_mass = write_mass[dst];
    let combined_mass = dest_mass + amount;
    let temperature = if combined_mass <= 0.0 {
        0.0
    } else {
        let weighted = (dest_mass * dest_temperature + source_temperature * amount) / combined_mass;
        if dest_temperature > source_temperature {
            if weighted < source_temperature || weighted.is_nan() || source_temperature.is_nan() {
                source_temperature
            } else {
                smin(dest_temperature, weighted)
            }
        } else if dest_temperature <= weighted {
            smin(source_temperature, weighted)
        } else {
            dest_temperature
        }
    };
    write_mass[dst] = combined_mass;
    write_temperature[dst] = temperature;
    crate::fluid_helpers::add_disease_fields(
        diseases,
        &mut write_disease_idx[dst],
        &mut write_disease_count[dst],
        &mut write_disease_infestation[dst],
        &mut write_disease_growth[dst],
        germ_id,
        germ_count,
    );
    write_mass[src] = smax(write_mass[src] - amount, 0.0);
    let remaining = write_disease_count[src].wrapping_sub(germ_count);
    write_disease_count[src] = remaining;
    if remaining <= 0 {
        write_disease_idx[src] = 255;
        write_disease_count[src] = 0;
        write_disease_infestation[src] = 0;
        write_disease_growth[src] = 0.0;
    }
    if dest_phase == 0 {
        if dest_element == world.sink_element {
            write_mass[dst] = 0.0;
            write_temperature[dst] = 0.0;
            write_disease_idx[dst] = 255;
            write_disease_count[dst] = 0;
            write_disease_infestation[dst] = 0;
            write_disease_growth[dst] = 0.0;
        } else {
            write_element[dst] = source_element;
            world.notify(events, dst);
        }
    }
    flow
}

pub(super) fn run(
    world: &mut PhysicsWorld,
    elements: &ElementTable,
    diseases: &DiseaseTable,
    events: &mut EventQueues,
    region: Region,
) -> bool {
    let mut storage = PressureSweep::new(world);
    let world = &mut storage;
    let mut changed = false;
    for y in region.min_y..region.max_y {
        let row = y as usize * world.stride;
        let (mut cell, stop) = if world.scan_direction > 0 {
            (row + region.min_x as usize, row + region.max_x as usize)
        } else {
            (row + region.max_x as usize, row + region.min_x as usize)
        };
        while cell != stop {
            changed |= pressure_cell(world, elements, diseases, events, cell);
            cell = cell.wrapping_add_signed(world.scan_direction as isize);
        }
    }
    changed
}

#[cfg(test)]
mod tests {
    use super::*;

    fn world(width: usize, height: usize) -> PhysicsWorld {
        let mut bytes = 3_i32.to_le_bytes().to_vec();
        for (index, id) in [0x2d39_bf75_u32, 0xa936_0b34, 0x6d95_058c]
            .into_iter()
            .enumerate()
        {
            let mut raw = [0_u8; 164];
            raw[..4].copy_from_slice(&id.to_le_bytes());
            raw[8..10].copy_from_slice(&(index as u16).to_le_bytes());
            raw[10] = if index == 2 { 7 } else { 0 };
            bytes.extend_from_slice(&raw);
        }
        bytes.extend_from_slice(&[0; 12]);
        let elements = ElementTable::parse(&bytes).unwrap();
        PhysicsWorld::new(width, height, 42, &elements).unwrap_or_else(|_| panic!("test world"))
    }

    #[test]
    fn shortened_pressure_storage_is_rejected_before_writes() {
        let truncate: [fn(&mut PhysicsWorld); 14] = [
            |w| {
                w.read.mass.pop();
            },
            |w| {
                w.read.properties.pop();
            },
            |w| {
                w.read.disease_count.pop();
            },
            |w| {
                w.write.element.pop();
            },
            |w| {
                w.write.mass.pop();
            },
            |w| {
                w.write.temperature.pop();
            },
            |w| {
                w.write.disease_idx.pop();
            },
            |w| {
                w.write.disease_count.pop();
            },
            |w| {
                w.write.disease_infestation.pop();
            },
            |w| {
                w.write.disease_growth.pop();
            },
            |w| {
                w.flow.pop();
            },
            |w| {
                w.dirty.pop();
            },
            |w| {
                w.dirty_flags.pop();
            },
            |w| {
                w.read.element.push(0);
            },
        ];
        for shorten in truncate {
            let mut w = world(4, 4);
            shorten(&mut w);
            let mass = w.write.mass.clone();
            let dirty = w.dirty.clone();
            let flags = w.dirty_flags.clone();
            let rejected = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
                let _view = PressureSweep::new(&mut w);
            }));
            assert!(rejected.is_err());
            assert_eq!(w.write.mass, mass);
            assert_eq!(w.dirty, dirty);
            assert_eq!(w.dirty_flags, flags);
        }
    }

    #[test]
    fn borrowed_notifications_keep_padding_order_dirty_bits_and_dedup() {
        for (width, height) in [(1, 1), (2, 19), (9, 11)] {
            for track in [false, true] {
                let mut original = world(width, height);
                let mut candidate = world(width, height);
                let mut expected = EventQueues::default();
                let mut actual = EventQueues::default();
                if track {
                    expected.track_grid_changes(width * height);
                    actual.track_grid_changes(width * height);
                }
                let count = original.read.element.len();
                {
                    let mut view = PressureSweep::new(&mut candidate);
                    for cell in (0..count).chain((0..count).rev()).chain(0..count) {
                        crate::fluid_helpers::notify(&mut original, &mut expected, cell);
                        view.notify(&mut actual, cell);
                    }
                }
                assert_eq!(original.dirty, candidate.dirty);
                assert_eq!(original.dirty_flags, candidate.dirty_flags);
                assert_eq!(
                    expected
                        .substance_change_info
                        .iter()
                        .map(|e| e.cell_idx)
                        .collect::<Vec<_>>(),
                    actual
                        .substance_change_info
                        .iter()
                        .map(|e| e.cell_idx)
                        .collect::<Vec<_>>()
                );
            }
        }
    }
}
