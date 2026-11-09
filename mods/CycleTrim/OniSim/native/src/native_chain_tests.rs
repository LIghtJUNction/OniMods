//! Complete worker/Engine/publication benchmark. The external driver runs this
//! identical harness in two complete source revisions and compares all digests.

#[path = "native_chain_snapshot.rs"]
pub(crate) mod snapshot;
#[path = "native_chain_fixture.rs"]
pub(crate) mod fixture;

use std::io::Write;
use std::process::{Command, Stdio};
use std::time::Instant;
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
        assert_eq!(std::env::var("CYCLETRIM_ONISIM_GPU").as_deref(), Ok("0"),
            "manual CPU chain run must explicitly disable unavailable GPU initialization");
        assert_eq!(std::env::var("CYCLETRIM_ONISIM_WORKERS").as_deref(), Ok("1"));
        let (mut state, regions) = fixture::create(width, height, scene);
        ok(state.start_worker(None));
        let worker = state.worker.as_ref().unwrap().handle();
        let mut ready = ok(worker.wait_ready());
        ok(state.finish_prepare(ready.data_mut()));
        assert!(state.engine.as_ref().unwrap().lock().unwrap().gpu.is_none());
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
        assert_eq!(grid_addresses(&self.state), self.stable_addresses);
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
