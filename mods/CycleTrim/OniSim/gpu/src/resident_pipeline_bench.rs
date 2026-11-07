use onisim_gpu::{
    gpu_context::GpuContext,
    multicomponent::{cpu_step, MulticomponentGpu, SpeciesInfo, State},
    pressure_projection::PressureProjectionGpu,
};
use serde::Serialize;
use std::{hint::black_box, sync::Arc, time::Instant};

struct Fixture {
    mass: Vec<f32>,
    enthalpy: Vec<f32>,
    velocity_x: Vec<f32>,
    velocity_y: Vec<f32>,
    solid: Vec<u32>,
    species: Vec<SpeciesInfo>,
}
impl Fixture {
    fn new(width: usize, height: usize, channels: usize) -> Self {
        let cells = width * height;
        let mut solid = vec![0u32; cells];
        for x in 0..width {
            solid[x] = 1;
            solid[(height - 1) * width + x] = 1;
        }
        for y in 0..height {
            solid[y * width] = 1;
            solid[y * width + width - 1] = 1;
        }
        if width > 32 && height > 32 {
            for y in height / 3..height / 3 + 7 {
                for x in width / 3..width / 3 + 11 {
                    solid[y * width + x] = 1;
                }
            }
        }
        let species: Vec<SpeciesInfo> = (0..channels)
            .map(|c| SpeciesInfo {
                element_id: (c + 1) as u16,
                phase: 1,
                specific_heat_capacity: 1.0 + c as f32 * 0.25,
                vertical_drift: 0.0,
            })
            .collect();
        let mut mass = vec![0.0; cells * channels];
        let mut enthalpy = vec![0.0; cells * channels];
        for cell in 0..cells {
            if solid[cell] == 0 {
                for channel in 0..channels {
                    let m = 0.5 + ((cell * 17 + channel * 13) % 97) as f32 / 97.0;
                    mass[cell * channels + channel] = m;
                    enthalpy[cell * channels + channel] =
                        m * species[channel].specific_heat_capacity * 300.0;
                }
            }
        }
        let velocity_x = (0..cells).map(|i| ((i * 17 % 101) as f32 - 50.0) * 0.00002).collect();
        let velocity_y = (0..cells).map(|i| ((i * 23 % 89) as f32 - 44.0) * 0.00002).collect();
        Self { mass, enthalpy, velocity_x, velocity_y, solid, species }
    }
    fn state(&self) -> State<'_> {
        State {
            mass: &self.mass,
            enthalpy: &self.enthalpy,
            velocity_x: &self.velocity_x,
            velocity_y: &self.velocity_y,
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
    pressure_iterations: usize,
    median_us: f64,
    p95_us: f64,
    max_abs_mass_error: f64,
    max_relative_mass_error: f64,
    transport_parity_pass: bool,
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
    rows: Vec<Row>,
}

fn main() -> Result<(), String> {
    // Include the padded dimensions observed in the current ONI runtime.
    let width_height = [(96usize, 96usize), (256usize, 384usize), (738usize, 306usize)];
    let channels = 4usize;
    let dt = 0.02f32;
    let pressure_iterations = std::env::var("ONISIM_PRESSURE_ITERATIONS")
        .ok().and_then(|value| value.parse().ok()).unwrap_or(32usize);
    let context = Arc::new(pollster::block_on(GpuContext::new())?);
    let adapter = context.adapter.clone();
    let mut rows = Vec::new();

    for (width, height) in width_height {
        let fixture = Fixture::new(width, height, channels);
        let mut transport = pollster::block_on(MulticomponentGpu::new_with_context(
            context.clone(), width, height, channels,
        ))?;
        let mut pressure = pollster::block_on(PressureProjectionGpu::new_with_context(
            context.clone(), width, height,
        ))?;
        let mut expected = vec![0.0f32; fixture.mass.len()];
        let mut expected_enthalpy = vec![0.0f32; fixture.enthalpy.len()];
        cpu_step(&fixture.state(), width, height, channels, dt, &mut expected, &mut expected_enthalpy);
        let mut actual = vec![0.0f32; fixture.mass.len()];
        let mut actual_enthalpy = vec![0.0f32; fixture.enthalpy.len()];
        let gauge_cell = fixture.solid.iter().position(|&value| value == 0)
            .ok_or("fixture has no fluid cells")?;

        let mut samples = Vec::with_capacity(31);
        let mut max_abs = 0.0f64;
        let mut max_rel = 0.0f64;
        for sample in 0..36 {
            transport.upload(&fixture.state())?;
            let started = Instant::now();
            transport.dispatch_resident(1, dt)?;
            let (velocity_x, velocity_y) = transport.velocity_buffers();
            let solid = transport.solid_buffer();
            pressure.run_with_shared_state(
                transport.current_mass_buffer(),
                velocity_x,
                velocity_y,
                solid,
                channels,
                1.0,
                1.0,
                gauge_cell,
                dt,
                1.0,
                1.0,
                pressure_iterations,
                1.0,
            )?;
            transport.readback_state(&mut actual, &mut actual_enthalpy)?;
            let elapsed = started.elapsed().as_secs_f64() * 1e6;
            if sample >= 5 {
                samples.push(elapsed);
            }
            for (&reference, &candidate) in expected.iter().zip(&actual) {
                let error = (f64::from(reference) - f64::from(candidate)).abs();
                max_abs = max_abs.max(error);
                max_rel = max_rel.max(error / f64::from(reference).abs().max(1e-4));
            }
            for (&reference, &candidate) in expected_enthalpy.iter().zip(&actual_enthalpy) {
                let error = (f64::from(reference) - f64::from(candidate)).abs();
                if error / f64::from(reference).abs().max(1e-4) > 1e-4 {
                    return Err(format!("resident transport enthalpy parity failed for {width}x{height}"));
                }
            }
        }
        if max_rel > 1e-4 {
            return Err(format!(
                "resident transport mass parity failed for {width}x{height}: {max_rel:e}"
            ));
        }
        samples.sort_by(f64::total_cmp);
        let median = samples[samples.len() / 2];
        let p95 = samples[(samples.len() * 95).div_ceil(100) - 1];
        rows.push(Row {
            width, height, channels, pressure_iterations,
            median_us: black_box(median), p95_us: black_box(p95),
            max_abs_mass_error: max_abs,
            max_relative_mass_error: max_rel,
            transport_parity_pass: max_rel <= 1e-4,
        });
    }

    println!("{}", serde_json::to_string_pretty(&Report {
        stage: "OniSim GPU-resident transport + pressure projection",
        adapter: adapter.name,
        vendor: adapter.vendor,
        device_type: format!("{:?}", adapter.device_type),
        backend: format!("{:?}", adapter.backend),
        warmup: 5,
        samples: 31,
        rows,
    }).map_err(|error| error.to_string())?);
    Ok(())
}
