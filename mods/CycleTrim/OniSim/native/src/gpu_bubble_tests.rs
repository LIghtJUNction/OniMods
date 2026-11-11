use super::*;
use crate::events::EventQueues;

fn mixed_gases() -> (PhysicsWorld, Arc<ElementTable>) {
    let (mut state, _) = crate::native_chain_tests::fixture::create(16, 16, "zero");
    let mut world = state.world.take().unwrap();
    for y in 1..=world.height {
        for x in 1..=world.width {
            let cell = y * world.stride + x;
            world.write.element[cell] = 3;
            world.write.mass[cell] = 1.0;
            world.write.temperature[cell] = 300.0;
            world.mixture_element[cell] = 4;
            world.mixture_mass[cell] = 0.2;
        }
    }
    (world, state.elements.take().unwrap())
}

#[test]
fn same_phase_mixtures_never_submit_bubble_work() {
    let (mut world, table) = mixed_gases();
    let mut gpu = GpuSimulation::new(world.stride, world.padded_height).unwrap();
    assert!(gpu.begin_move_bubbles(&world, &table).unwrap().is_none());
    assert!(gpu.bubble_columns.is_empty());
    assert!(gpu.bubble_cells.is_empty());
    for y in 1..=world.height {
        for x in 1..=world.width {
            let cell = y * world.stride + x;
            world.write.element[cell] = 5;
            world.mixture_element[cell] = 5;
        }
    }
    assert!(gpu.begin_move_bubbles(&world, &table).unwrap().is_none());
}

#[test]
fn cropped_cross_phase_columns_match_ordered_cpu_moves_at_map_edges() {
    let (world, _) = mixed_gases();
    let mut gpu = GpuSimulation::new(world.stride, world.padded_height).unwrap();
    for (y, primary, secondary) in [(1, 5, 3), (8, 5, 3), (16, 5, 3),
        (1, 3, 5), (8, 3, 5), (16, 3, 5)] {
        let (mut actual, table) = mixed_gases();
        let (mut expected, _) = mixed_gases();
        let cell = y * actual.stride + 4;
        for world in [&mut actual, &mut expected] {
            world.write.element[cell] = primary;
            world.mixture_element[cell] = secondary;
            world.mixture_mass[cell] = 0.5;
            if primary == 3 && y > 1 {
                world.mixture_mass[cell - world.stride] = 0.0;
            }
        }
        let mut events = EventQueues::default();
        if let Some(pending) = gpu.begin_move_bubbles(&actual, &table).unwrap() {
            assert_eq!(gpu.bubble_columns, [4]);
            gpu.finish_move_bubbles(&mut actual, &mut events, pending).unwrap();
        }
        crate::gas::rise_bubbles(&mut expected, &table, &mut EventQueues::default());
        assert_eq!(actual.write.element, expected.write.element);
        assert_eq!(actual.write.mass, expected.write.mass);
        assert_eq!(actual.write.temperature, expected.write.temperature);
        assert_eq!(actual.mixture_element, expected.mixture_element);
        assert_eq!(actual.mixture_mass, expected.mixture_mass);
        if (primary == 5 && y < 16) || (primary == 3 && y > 1) {
            assert_eq!(actual.mixture_mass[cell], 0.0);
        }
    }
}
