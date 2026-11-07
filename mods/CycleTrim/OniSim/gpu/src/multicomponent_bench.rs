use onisim_gpu::multicomponent::{cpu_step, MulticomponentGpu, SpeciesInfo, State};
use serde::Serialize;
use std::time::Instant;

struct Fixture {
    mass: Vec<f32>,
    enthalpy: Vec<f32>,
    vx: Vec<f32>,
    vy: Vec<f32>,
    solid: Vec<u32>,
    species: Vec<SpeciesInfo>,
}

impl Fixture {
    fn new(width: usize, height: usize, channels: usize) -> Self {
        let count = width * height;
        let mut seed = 0x12345678u32;
        let mut random = || {
            seed ^= seed << 13;
            seed ^= seed >> 17;
            seed ^= seed << 5;
            seed
        };
        let mut solid = vec![0; count];
        for x in 0..width {
            solid[x] = 1;
            solid[(height - 1) * width + x] = 1;
        }
        for y in 0..height {
            solid[y * width] = 1;
            solid[y * width + width - 1] = 1;
        }
        if width > 16 && height > 16 {
            for y in height / 3..height / 3 + 7 {
                for x in width / 3..width / 3 + 11 {
                    solid[y * width + x] = 1;
                }
            }
        }
        let species = (0..channels)
            .map(|i| SpeciesInfo {
                element_id: (i + 1) as u16,
                phase: (i % 3 + 1) as u8,
                specific_heat_capacity: 0.8 + i as f32 * 0.15,
                vertical_drift: (i as f32 - (channels as f32 - 1.0) * 0.5) * 0.0001,
            })
            .collect::<Vec<_>>();
        let mut mass = (0..count * channels)
            .map(|_| (random() % 10000) as f32 / 10000.0)
            .collect::<Vec<_>>();
        let mut enthalpy = vec![0.0; count * channels];
        for cell in 0..count {
            if solid[cell] != 0 {
                mass[cell * channels..(cell + 1) * channels].fill(0.0);
            } else {
                for c in 0..channels {
                    let slot = cell * channels + c;
                    let temperature = 250.0 + (random() % 1000) as f32 * 0.1;
                    enthalpy[slot] = mass[slot] * species[c].specific_heat_capacity * temperature;
                }
            }
        }
        let vx = (0..count)
            .map(|_| ((random() % 2001) as i32 - 1000) as f32 * 0.00002)
            .collect();
        let vy = (0..count)
            .map(|_| ((random() % 2001) as i32 - 1000) as f32 * 0.00002)
            .collect();
        Self { mass, enthalpy, vx, vy, solid, species }
    }

    fn state(&self) -> State<'_> {
        State {
            mass: &self.mass,
            enthalpy: &self.enthalpy,
            velocity_x: &self.vx,
            velocity_y: &self.vy,
            solid: &self.solid,
            species: &self.species,
        }
    }
}

#[derive(Serialize)]
struct Row {
    width: usize,
    height: usize,
    channels: usize,
    cpu_mass_enthalpy_median_us: f64,
    gpu_upload_dispatch_sync_readback_median_us: f64,
    gpu_resident_32_steps_total_us: f64,
    gpu_resident_per_step_us: f64,
    max_mass_abs_error: f64,
    max_enthalpy_abs_error: f64,
    max_mass_relative_error: f64,
    max_enthalpy_relative_error: f64,
    mass_conservation_relative_error: f64,
    enthalpy_conservation_relative_error: f64,
    nonnegative: bool,
    solid_zero: bool,
}

#[derive(Serialize)]
struct Report {
    stage: &'static str,
    adapter: String,
    vendor: u32,
    device_type: String,
    backend: String,
    warmup: usize,
    samples: usize,
    results: Vec<Row>,
}

fn median(values: &mut [f64]) -> f64 {
    values.sort_by(f64::total_cmp);
    values[values.len() / 2]
}

fn main() -> Result<(), String> {
    let mut rows = Vec::new();
    let (mut adapter, mut vendor, mut kind, mut backend) =
        (String::new(), 0, String::new(), String::new());

    for (width, height, channels) in [(96, 96, 4), (256, 384, 4)] {
        let fixture = Fixture::new(width, height, channels);
        let state = fixture.state();
        let values = width * height * channels;
        let dt = 0.02;
        let mut cpu_mass = vec![0.0; values];
        let mut cpu_enthalpy = vec![0.0; values];
        let mut gpu_mass = vec![0.0; values];
        let mut gpu_enthalpy = vec![0.0; values];

        cpu_step(&state, width, height, channels, dt, &mut cpu_mass, &mut cpu_enthalpy);
        let mut gpu = pollster::block_on(MulticomponentGpu::new(width, height, channels))?;
        adapter = gpu.adapter.name.clone();
        vendor = gpu.adapter.vendor;
        kind = format!("{:?}", gpu.adapter.device_type);
        backend = format!("{:?}", gpu.adapter.backend);
        gpu.upload(&state)?;
        gpu.run_steps_and_readback(1, dt, &mut gpu_mass, &mut gpu_enthalpy)?;

        let mut mass_abs = 0.0f64;
        let mut enthalpy_abs = 0.0f64;
        let mut mass_rel = 0.0f64;
        let mut enthalpy_rel = 0.0f64;
        for (&cpu, &actual) in cpu_mass.iter().zip(&gpu_mass) {
            let error = (cpu as f64 - actual as f64).abs();
            mass_abs = mass_abs.max(error);
            mass_rel = mass_rel.max(error / (cpu as f64).abs().max(1e-8));
        }
        for (&cpu, &actual) in cpu_enthalpy.iter().zip(&gpu_enthalpy) {
            let error = (cpu as f64 - actual as f64).abs();
            enthalpy_abs = enthalpy_abs.max(error);
            enthalpy_rel = enthalpy_rel.max(error / (cpu as f64).abs().max(1e-8));
        }

        let mut mass_before = vec![0.0f64; channels];
        let mut mass_after = vec![0.0f64; channels];
        let mut energy_before = vec![0.0f64; channels];
        let mut energy_after = vec![0.0f64; channels];
        for cell in 0..width * height {
            for c in 0..channels {
                mass_before[c] += fixture.mass[cell * channels + c] as f64;
                mass_after[c] += gpu_mass[cell * channels + c] as f64;
                energy_before[c] += fixture.enthalpy[cell * channels + c] as f64;
                energy_after[c] += gpu_enthalpy[cell * channels + c] as f64;
            }
        }
        let mass_conservation = (0..channels)
            .map(|c| (mass_before[c] - mass_after[c]).abs() / mass_before[c].abs().max(1e-30))
            .fold(0.0, f64::max);
        let energy_conservation = (0..channels)
            .map(|c| (energy_before[c] - energy_after[c]).abs() / energy_before[c].abs().max(1e-30))
            .fold(0.0, f64::max);
        let nonnegative = gpu_mass.iter().chain(&gpu_enthalpy).all(|v| *v >= -1e-7 && v.is_finite());
        let solid_zero = (0..width * height).all(|cell| {
            fixture.solid[cell] == 0
                || (gpu_mass[cell * channels..(cell + 1) * channels].iter().all(|v| *v == 0.0)
                    && gpu_enthalpy[cell * channels..(cell + 1) * channels].iter().all(|v| *v == 0.0))
        });
        if mass_rel > 1e-5 || enthalpy_rel > 1e-5 || !nonnegative || !solid_zero
            || mass_conservation > 1e-5 || energy_conservation > 1e-5 {
            return Err(format!("mass/enthalpy parity or conservation failed for {width}x{height}x{channels}"));
        }

        let mut cpu_times = Vec::new();
        let mut end_to_end_times = Vec::new();
        let mut resident_times = Vec::new();
        for _ in 0..5 {
            gpu.upload(&state)?;
            gpu.run_steps_and_readback(1, dt, &mut gpu_mass, &mut gpu_enthalpy)?;
            gpu.upload(&state)?;
            gpu.run_steps_and_readback(32, dt, &mut gpu_mass, &mut gpu_enthalpy)?;
        }
        for _ in 0..31 {
            let start = Instant::now();
            cpu_step(&state, width, height, channels, dt, &mut cpu_mass, &mut cpu_enthalpy);
            cpu_times.push(start.elapsed().as_secs_f64() * 1e6);

            let start = Instant::now();
            gpu.upload(&state)?;
            gpu.run_steps_and_readback(1, dt, &mut gpu_mass, &mut gpu_enthalpy)?;
            end_to_end_times.push(start.elapsed().as_secs_f64() * 1e6);

            let start = Instant::now();
            gpu.upload(&state)?;
            gpu.run_steps_and_readback(32, dt, &mut gpu_mass, &mut gpu_enthalpy)?;
            resident_times.push(start.elapsed().as_secs_f64() * 1e6);
        }
        let cpu_us = median(&mut cpu_times);
        let end_to_end_us = median(&mut end_to_end_times);
        let resident_us = median(&mut resident_times);
        rows.push(Row {
            width, height, channels,
            cpu_mass_enthalpy_median_us: cpu_us,
            gpu_upload_dispatch_sync_readback_median_us: end_to_end_us,
            gpu_resident_32_steps_total_us: resident_us,
            gpu_resident_per_step_us: resident_us / 32.0,
            max_mass_abs_error: mass_abs,
            max_enthalpy_abs_error: enthalpy_abs,
            max_mass_relative_error: mass_rel,
            max_enthalpy_relative_error: enthalpy_rel,
            mass_conservation_relative_error: mass_conservation,
            enthalpy_conservation_relative_error: energy_conservation,
            nonnegative,
            solid_zero,
        });
    }

    println!("{}", serde_json::to_string_pretty(&Report {
        stage: "Stage 1 Vulkan mass and sensible-enthalpy operator acceptance",
        adapter, vendor, device_type: kind, backend, warmup: 5, samples: 31, results: rows,
    }).map_err(|e| e.to_string())?);
    Ok(())
}
