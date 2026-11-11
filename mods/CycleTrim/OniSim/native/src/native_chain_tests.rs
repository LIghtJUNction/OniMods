//! Complete worker/Engine/publication benchmark. The external driver runs this
//! identical harness in two complete source revisions and compares all digests.

#[path = "native_chain_snapshot.rs"]
pub(crate) mod snapshot;
#[path = "native_chain_fixture.rs"]
pub(crate) mod fixture;

use std::io::Write;
use std::process::{Command, Stdio};
use std::time::Instant;

pub(crate) static BENCHMARK_REFERENCE_KERNELS: std::sync::atomic::AtomicBool =
    std::sync::atomic::AtomicBool::new(false);
use crate::frames::{NewFrame, QueuedFrame};
use crate::frames_data::GameData;
use crate::state::State;
use crate::status::Result;
use crate::worker::ReadyLease;
use crate::world::Region;

fn ok<T>(result: Result<T>) -> T {
    result.unwrap_or_else(|error| panic!("{}", error.message))
}

fn sha256(image: &snapshot::Image) -> String {
    // Linux validation tool, run synchronously OUTSIDE every measured interval.
    // No non-cryptographic checksum or formatted float comparison stands in for
    // the complete canonical publication/world bytes.
    let mut child = Command::new("sha256sum").stdin(Stdio::piped()).stdout(Stdio::piped())
        .spawn().expect("sha256sum is required for the manual chain benchmark");
    child.stdin.take().unwrap().write_all(&image.0).unwrap();
    let output = child.wait_with_output().unwrap();
    assert!(output.status.success());
    let text = String::from_utf8(output.stdout).unwrap();
    text.split_whitespace().next().unwrap().to_string()
}

struct Runner {
    state: State,
    ready: Option<ReadyLease<QueuedFrame, GameData>>,
    regions: Vec<Region>,
    scene: String,
    elapsed: f32,
    step: usize,
    stable_addresses: Vec<usize>,
}

fn grid_addresses(state: &State) -> Vec<usize> {
    let grid = state.grid.as_ref().unwrap();
    let bytes = unsafe { std::slice::from_raw_parts(grid.descriptor(), crate::grid::DESCRIPTOR_SIZE) };
    (0..12).map(|index| 4 + index * 8).chain([448]).map(|at|
        u64::from_le_bytes(bytes[at..at + 8].try_into().unwrap()) as usize).collect()
}

impl Runner {
    fn new(width: usize, height: usize, scene: &str, elapsed: f32) -> Self {
        let gpu_mode = std::env::var("CYCLETRIM_ONISIM_GPU").expect("select GPU mode explicitly");
        assert!(matches!(gpu_mode.as_str(), "0" | "1"));
        let gpu_requested = gpu_mode == "1";
        if !gpu_requested {
            assert_eq!(std::env::var("CYCLETRIM_ONISIM_WORKERS").as_deref(), Ok("1"));
        }
        let (mut state, regions) = fixture::create(width, height, scene);
        state.game_publication_views = std::env::var("ONISIM_CHAIN_PUBLICATION_VIEWS").as_deref() == Ok("1");
        println!("NATIVE_CHAIN_VIEW_MODE,{}", if state.game_publication_views { "publication" } else { "stable" });
        let runner = Self::start(state, regions, scene, elapsed);
        assert_eq!(runner.state.engine.as_ref().unwrap().lock().unwrap().gpu.is_some(), gpu_requested);
        runner
    }

    fn start(mut state: State, regions: Vec<Region>, scene: &str, elapsed: f32) -> Self {
        ok(state.start_worker(None));
        let worker = state.worker.as_ref().unwrap().handle();
        let mut ready = ok(worker.wait_ready());
        ok(state.finish_prepare(ready.data_mut()));
        let stable_addresses = grid_addresses(&state);
        Self { state, ready: Some(ready), regions, scene: scene.into(), elapsed, step: 0, stable_addresses }
    }

    fn advance(&mut self, followup: bool) -> f64 {
        let worker = self.state.worker.as_ref().unwrap().handle();
        let count = self.state.grid.as_ref().unwrap().len();
        let visibility = vec![255_u8; count];
        let frame = NewFrame {
            elapsed: if followup { 0.2 } else if matches!(self.scene.as_str(), "zero" | "command" | "alternating_idle") { 0.0 } else { self.elapsed },
            regions: self.regions.clone(),
        };
        let previous = self.ready.take().unwrap();
        let start = Instant::now();
        if self.scene == "command" && !followup {
            let mut bytes = [0_u8; 28];
            let cell = count / 2 + self.step % 7;
            bytes[..4].copy_from_slice(&(cell as i32).to_le_bytes());
            bytes[4..8].copy_from_slice(&(1000 + self.step as i32).to_le_bytes());
            bytes[8..12].copy_from_slice(&(300.0 + self.step as f32).to_le_bytes());
            bytes[12..16].copy_from_slice(&(1.0 + self.step as f32 * 0.125).to_le_bytes());
            bytes[20..22].copy_from_slice(&fixture::GAS.to_le_bytes());
            bytes[22] = 1;
            bytes[23] = 255;
            bytes[24] = 1;
            ok(crate::messages::execute(&mut self.state, crate::cell_material::MODIFY, 28, 1, &bytes));
        }
        // The bootstrap is empty. Continue with zero, empty, zero, empty
        // through both warmup and measurement, without restarting the sequence.
        let submit_frame = self.scene != "empty"
            && (self.scene != "alternating_idle" || self.step % 2 == 0);
        if submit_frame || followup {
            ok(self.state.submit_frame(frame));
        }
        ok(previous.release_with(move |old| old.visible.copy_from_slice(&visibility)));
        let mut ready = ok(worker.wait_ready());
        ok(self.state.finish_prepare(ready.data_mut()));
        let elapsed_us = start.elapsed().as_secs_f64() * 1e6;
        // Address assertions and all content observations are outside timing.
        if self.state.game_publication_views {
            assert_eq!(self.state.grid().unwrap().descriptor(), ready.data().grid.descriptor());
        } else {
            assert_eq!(grid_addresses(&self.state), self.stable_addresses);
        }
        self.ready = Some(ready);
        self.step += 1;
        elapsed_us
    }

    fn observe(&self, label: &str) {
        let ready = self.ready.as_ref().unwrap();
        let public_omc = self.state.multicomponent_update.as_ref().unwrap().as_ref()
            .unwrap_or_else(|error| {
                let grid = self.state.grid.as_ref().unwrap();
                let bad = grid.mass.iter().zip(grid.temperature.iter()).enumerate()
                    .find(|(_, (mass, temperature))| !mass.is_finite() || **mass < 0.0 || !temperature.is_finite());
                panic!("{}; first invalid public cell: {bad:?}", error.message)
            });
        let owned_omc = ok(ready.data().multicomponent.descriptor(self.state.generation));
        assert_eq!(*public_omc, owned_omc);
        let published = snapshot::publication(self.state.grid.as_ref().unwrap(), ready.data(), self.state.generation);
        let engine = self.state.engine.as_ref().unwrap().lock().unwrap();
        let mut physics = snapshot::world(&engine.world);
        physics.bytes("residual_time", &engine.residual_time.to_bits().to_le_bytes());
        physics.bytes("completed_frames", &self.state.scheduler.completed_frames.to_le_bytes());
        drop(engine);
        println!("NATIVE_CHAIN_SNAPSHOT,{label},{},{},{},{}", published.0.len(), sha256(&published), physics.0.len(), sha256(&physics));
    }
}

impl Drop for Runner {
    fn drop(&mut self) {
        if let Some(worker) = &mut self.state.worker { ok(worker.shutdown()); }
        // shutdown wakes the barrier and explicitly permits a retained lease.
        self.ready.take();
    }
}

fn integer(name: &str, default: usize) -> usize {
    std::env::var(name).ok().map(|value| value.parse().unwrap()).unwrap_or(default)
}

fn process_counters() -> (u64, u64) {
    let text = std::fs::read_to_string("/proc/self/stat").unwrap();
    let fields: Vec<_> = text.rsplit_once(')').unwrap().1.split_whitespace().collect();
    let get = |index: usize| fields[index].parse::<u64>().unwrap();
    (get(11) + get(12), get(9)) // process user+system CPU ticks; major faults
}

#[test]
#[ignore = "manual complete production worker/Engine/stable-Grid benchmark; external paired driver required"]
fn benchmark_native_chain_sample() {
    assert!(cfg!(target_os = "linux"), "the manual runner records Linux process counters");
    let scene = std::env::var("ONISIM_CHAIN_SCENE").unwrap_or_else(|_| "pure".into());
    let width = integer("ONISIM_CHAIN_WIDTH", 96);
    let height = integer("ONISIM_CHAIN_HEIGHT", 96);
    let warmup = integer("ONISIM_CHAIN_WARMUP", 2);
    let batches = integer("ONISIM_CHAIN_BATCHES", 4);
    let elapsed: f32 = std::env::var("ONISIM_CHAIN_ELAPSED").unwrap_or_else(|_| "0.2".into()).parse().unwrap();
    let mut runner = Runner::new(width, height, &scene, elapsed);
    runner.observe("bootstrap");
    for step in 0..warmup {
        runner.advance(false);
        runner.observe(&format!("warmup_{step}"));
    }
    let mut times = Vec::with_capacity(batches);
    let mut ticks = 0;
    let mut major_faults = 0;
    for step in 0..batches {
        let before = process_counters();
        times.push(runner.advance(false));
        let after = process_counters();
        ticks += after.0 - before.0;
        major_faults += after.1 - before.1;
        runner.observe(&format!("timed_{step}"));
    }
    runner.advance(true);
    runner.observe("followup");
    let status = std::fs::read_to_string("/proc/self/status").unwrap();
    let hwm = status.lines().find_map(|line| line.strip_prefix("VmHWM:")).unwrap().trim();
    let values = times.iter().map(|value| format!("{value:.6}")).collect::<Vec<_>>().join(";");
    let mean = times.iter().sum::<f64>() / times.len() as f64;
    println!("NATIVE_CHAIN_SAMPLE,{scene},{width},{height},{elapsed},{warmup},{batches},{mean:.6},{ticks},{major_faults},{hwm},{values}");
}

#[test]
#[ignore = "manual paired radiation CPU/GPU cost and output comparison"]
fn benchmark_radiation_backends() {
    let mut gpu = crate::gpu_sim::GpuSimulation::new(192, 288).unwrap();
    for (width, height) in [(32, 32), (80, 174), (160, 274)] {
        let (mut state, regions) = fixture::create(width, height, "mixed");
        let elements = std::sync::Arc::get_mut(state.elements.as_mut().unwrap()).unwrap();
        elements.elements[6].record.radiation_per_1000_mass = 3.0;
        let mut world = state.world.take().unwrap();
        world.radiation_enabled = true;
        // The benchmark includes the right/top halo; unlike the complete
        // worker fixture those cells must contain a real material record.
        world.write.element.fill(fixture::GAS);
        world.write.mass.fill(1.0);
        let region = crate::world::Region { cosmic_radiation: 200.0, ..regions[0] };
        let diseases = state.diseases.as_ref().unwrap();
        for (stride, label) in [(usize::MAX, "none"), (97, "sparse"), (2, "dense")] {
            for y in 1..=height { for x in 1..=width {
                let cell = y * world.stride + x;
                world.write.element[cell] = if cell % stride == 0 { 6 } else { fixture::GAS };
                world.write.mass[cell] = if cell % stride == 0 { 1000.0 } else { 1.0 };
            }}
            let initial: Vec<_> = (0..world.write.mass.len()).map(|i| (i % 500) as f32).collect();
            let mut cpu_times = Vec::new(); let mut gpu_times = Vec::new();
            for iteration in 0..15 {
                world.write.radiation.copy_from_slice(&initial);
                world.radiation_transmission.fill(0.0);
                let start = std::time::Instant::now();
                crate::radiation::update_region(&mut world, elements, diseases, region).unwrap();
                let cpu_time = start.elapsed().as_secs_f64() * 1000.0;
                let expected = world.write.radiation.clone();
                let transmission = world.radiation_transmission.clone();
                world.write.radiation.copy_from_slice(&initial);
                world.radiation_transmission.fill(0.0);
                let start = std::time::Instant::now();
                let work = gpu.begin_radiation_region(&world, elements, diseases, region).unwrap().unwrap();
                gpu.finish_radiation_region(&mut world, work).unwrap();
                let gpu_time = start.elapsed().as_secs_f64() * 1000.0;
                for y in region.min_y..=region.max_y { for x in region.min_x..=region.max_x {
                    let cell = y as usize * world.stride + x as usize;
                    assert!((world.write.radiation[cell] - expected[cell]).abs() < 0.001,
                        "{label} {width}x{height} cell {cell}: {} != {}", world.write.radiation[cell], expected[cell]);
                    assert!((world.radiation_transmission[cell] - transmission[cell]).abs() < 1e-5);
                }}
                if iteration >= 5 { cpu_times.push(cpu_time); gpu_times.push(gpu_time); }
            }
            cpu_times.sort_by(f64::total_cmp); gpu_times.sort_by(f64::total_cmp);
            println!("RADIATION_BACKENDS,{width},{height},{label},cpu_ms={:.6},gpu_ms={:.6}",
                (cpu_times[4] + cpu_times[5]) * 0.5, (gpu_times[4] + gpu_times[5]) * 0.5);
        }
    }
}

#[test]
fn game_event_filter_preserves_physics_and_real_changes() {
    // The benchmark runner selects its backend explicitly, including CPU-only
    // host test runs. The filtered path shares every input with the legacy path.
    let (state, regions) = fixture::create(32, 32, "command");
    let mut legacy = Runner::start(state, regions, "command", 0.0);
    let (state, regions) = fixture::create(32, 32, "command");
    let mut game = Runner::start(state, regions, "command", 0.0);
    game.state.engine.as_ref().unwrap().lock().unwrap().game_element_events_only = true;
    for runner in [&mut legacy, &mut game] {
        let mut bytes = [0_u8; 28];
        bytes[..4].copy_from_slice(&532_i32.to_le_bytes());
        bytes[8..12].copy_from_slice(&300.0_f32.to_le_bytes());
        bytes[12..16].copy_from_slice(&1.0_f32.to_le_bytes());
        bytes[20..22].copy_from_slice(&4_u16.to_le_bytes());
        bytes[22] = 1;
        bytes[23] = 255;
        bytes[24] = 1;
        ok(crate::messages::execute(&mut runner.state, crate::cell_material::MODIFY, 28, 1, &bytes));
    }
    legacy.advance(false);
    game.advance(false);
    let legacy_events = &legacy.ready.as_ref().unwrap().data().events.substance_change_info;
    assert!(!legacy_events.is_empty());
    assert!(legacy_events.iter().any(|event| event.old_elem_idx == event.new_elem_idx));
    let expected: Vec<_> = legacy_events.iter()
        .filter(|event| event.old_elem_idx != event.new_elem_idx)
        .map(|event| (event.cell_idx, event.old_elem_idx, event.new_elem_idx)).collect();
    let actual: Vec<_> = game.ready.as_ref().unwrap().data().events.substance_change_info.iter()
        .map(|event| (event.cell_idx, event.old_elem_idx, event.new_elem_idx)).collect();
    assert!(!expected.is_empty());
    assert_eq!(actual, expected);
    let legacy_engine = legacy.state.engine.as_ref().unwrap().lock().unwrap();
    let game_engine = game.state.engine.as_ref().unwrap().lock().unwrap();
    assert_eq!(snapshot::world(&legacy_engine.world).0, snapshot::world(&game_engine.world).0);
}

#[test]
fn game_event_filter_requires_game_views_and_non_headless_grid() {
    for (views, headless, expected) in [(false, 0, false), (false, 1, false),
                                       (true, 1, false), (true, 0, true)] {
        let (mut state, _) = fixture::create(16, 16, "zero");
        state.game_publication_views = views;
        state.grid.as_mut().unwrap().headless = headless;
        ok(state.start_worker(None));
        assert_eq!(state.engine.as_ref().unwrap().lock().unwrap().game_element_events_only, expected);
    }
}

#[test]
fn worker_start_repairs_old_vacuum_slots_with_residual_liquid() {
    let (mut state, _) = fixture::create(16, 16, "zero");
    let cell = 5 * state.world.as_ref().unwrap().stride + 5;
    let world = state.world.as_mut().unwrap();
    world.write.element[cell] = world.vacuum_element;
    world.write.mass[cell] = 0.0;
    world.write.temperature[cell] = 0.0;
    world.read.temperature[cell] = 0.0;
    world.mixture_element[cell] = 5;
    world.mixture_mass[cell] = 0.107484758;
    ok(state.start_worker(None));
    let engine = state.engine.as_ref().unwrap().lock().unwrap();
    assert_eq!(engine.world.write.element[cell], 5);
    assert_eq!(engine.world.write.mass[cell], 0.107484758);
    assert_eq!(engine.world.write.temperature[cell], 293.15);
    assert_eq!(engine.world.read.temperature[cell], 293.15);
    assert_eq!(engine.world.mixture_mass[cell], 0.0);
}


#[test]
#[ignore = "manual same-process complete native-loop presentation comparison"]
fn benchmark_optimized_native_chain() {
    use std::sync::atomic::Ordering;
    let selected = &BENCHMARK_REFERENCE_KERNELS;
    struct Reset;
    impl Drop for Reset {
        fn drop(&mut self) {
            BENCHMARK_REFERENCE_KERNELS.store(false, Ordering::SeqCst);
        }
    }
    let _reset = Reset;
    let width = integer("ONISIM_CHAIN_WIDTH", 512);
    let height = integer("ONISIM_CHAIN_HEIGHT", 448);
    let warmup = integer("ONISIM_CHAIN_WARMUP", 17);
    let groups = integer("ONISIM_CHAIN_PAIR_GROUPS", 34);
    let mut runner = Runner::new(width, height, "mixed", 0.2);
    for step in 0..warmup {
        selected.store(step % 2 == 0, Ordering::SeqCst);
        runner.advance(false);
    }
    let mut samples = [Vec::new(), Vec::new()];
    let mut faults = [0_u64; 2];
    for group in 0..groups {
        for serial in [true, false, false, true].map(|mode| mode ^ (group % 2 != 0)) {
            selected.store(serial, Ordering::SeqCst);
            let index = usize::from(!serial);
            let before = process_counters();
            let elapsed = runner.advance(false);
            let after = process_counters();
            faults[index] += after.1 - before.1;
            samples[index].push(elapsed);
        }
    }
    for (index, samples) in samples.iter().enumerate() {
        let mean = samples.iter().sum::<f64>() / samples.len() as f64;
        let values = samples.iter().map(|value| format!("{value:.6}")).collect::<Vec<_>>().join(";");
        println!("PRESENTATION_CHAIN_SAMPLE,serial={},samples={},mean_us={mean:.6},major_faults={},values={values}",
            index == 0, samples.len(), faults[index]);
    }
}
