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

#[test]
#[ignore]
fn benchmark_bubble_movement_gpu_vs_cpu() {
    use std::time::Instant;
    let make = |stride_x: usize, stride_y: usize| {
        let (mut state, _) = crate::native_chain_tests::fixture::create(736, 304, "mixed");
        let mut world = state.world.take().unwrap();
        let table = state.elements.take().unwrap();
        for y in (2..world.height).step_by(stride_y) {
            for x in (2..world.width).step_by(stride_x) {
                let cell = y * world.stride + x;
                world.write.element[cell] = 5;
                world.write.mass[cell] = 1000.0;
                world.mixture_element[cell] = 3;
                world.mixture_mass[cell] = 0.5;
            }
        }
        (world, table)
    };
    let (sample, _) = make(32, 16);
    let mut gpu = GpuSimulation::new(sample.stride, sample.padded_height).unwrap();
    for (name, stride_x, stride_y) in [("sparse", 32usize, 16usize), ("dense", 2, 2)] {
        let mut gpu_times = Vec::new();
        let mut cpu_times = Vec::new();
        for round in 0..12 {
            let (mut w_gpu, table) = make(stride_x, stride_y);
            let (mut w_cpu, _) = make(stride_x, stride_y);
            let mut e_gpu = EventQueues::default();
            let mut e_cpu = EventQueues::default();
            if round % 2 == 0 {
                let t0 = Instant::now();
                if let Some(p) = gpu.begin_move_bubbles(&w_gpu, &table).unwrap() {
                    gpu.finish_move_bubbles(&mut w_gpu, &mut e_gpu, p).unwrap();
                }
                gpu_times.push(t0.elapsed().as_secs_f64() * 1e3);
                let t1 = Instant::now();
                crate::gas::rise_bubbles(&mut w_cpu, &table, &mut e_cpu);
                cpu_times.push(t1.elapsed().as_secs_f64() * 1e3);
            } else {
                let t1 = Instant::now();
                crate::gas::rise_bubbles(&mut w_cpu, &table, &mut e_cpu);
                cpu_times.push(t1.elapsed().as_secs_f64() * 1e3);
                let t0 = Instant::now();
                if let Some(p) = gpu.begin_move_bubbles(&w_gpu, &table).unwrap() {
                    gpu.finish_move_bubbles(&mut w_gpu, &mut e_gpu, p).unwrap();
                }
                gpu_times.push(t0.elapsed().as_secs_f64() * 1e3);
            }
            assert_eq!(w_gpu.write.element, w_cpu.write.element);
            assert_eq!(w_gpu.mixture_mass, w_cpu.mixture_mass);
        }
        gpu_times.sort_by(f64::total_cmp);
        cpu_times.sort_by(f64::total_cmp);
        println!(
            "BUBBLE_ABBA,{name},gpu_median_ms={:.3},cpu_median_ms={:.3},cpu_speedup={:.2}x",
            gpu_times[gpu_times.len() / 2],
            cpu_times[cpu_times.len() / 2],
            gpu_times[gpu_times.len() / 2] / cpu_times[cpu_times.len() / 2],
        );
    }
}
