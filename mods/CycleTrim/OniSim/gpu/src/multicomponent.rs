use std::{collections::HashSet, sync::mpsc, time::Duration};

const SHADER: &str = include_str!("multicomponent_flux.wgsl");

/// Canonical metadata for one tensor channel. Phase values match ONI Element.State:
/// 1 gas, 2 liquid, 3 solid. Enthalpy is stored per cell as mass * cp * T.
#[derive(Clone, Copy, Debug)]
pub struct SpeciesInfo {
    pub element_id: u16,
    pub phase: u8,
    pub specific_heat_capacity: f32,
    pub vertical_drift: f32,
}

#[derive(Clone, Copy)]
pub struct State<'a> {
    pub mass: &'a [f32],
    pub enthalpy: &'a [f32],
    pub velocity_x: &'a [f32],
    pub velocity_y: &'a [f32],
    pub solid: &'a [u32],
    pub species: &'a [SpeciesInfo],
}

pub struct MulticomponentGpu {
    device: wgpu::Device,
    queue: wgpu::Queue,
    pipeline: wgpu::ComputePipeline,
    groups: [wgpu::BindGroup; 2],
    mass_a: wgpu::Buffer,
    mass_b: wgpu::Buffer,
    enthalpy_a: wgpu::Buffer,
    enthalpy_b: wgpu::Buffer,
    velocity_x: wgpu::Buffer,
    velocity_y: wgpu::Buffer,
    solid: wgpu::Buffer,
    drift_y: wgpu::Buffer,
    params: wgpu::Buffer,
    readback: wgpu::Buffer,
    width: usize,
    height: usize,
    channels: usize,
    count: usize,
    uploaded: bool,
    max_cfl_rate: f32,
    pub adapter: wgpu::AdapterInfo,
}

impl MulticomponentGpu {
    pub async fn new(width: usize, height: usize, channels: usize) -> Result<Self, String> {
        let count = width.checked_mul(height).ok_or("grid overflow")?;
        let values = count.checked_mul(channels).ok_or("tensor overflow")?;
        if width == 0 || height == 0 || channels == 0 || values > u32::MAX as usize {
            return Err("invalid grid dimensions".into());
        }
        let instance = wgpu::Instance::new(wgpu::InstanceDescriptor {
            backends: wgpu::Backends::VULKAN,
            ..wgpu::InstanceDescriptor::new_without_display_handle()
        });
        let adapter = instance
            .enumerate_adapters(wgpu::Backends::VULKAN)
            .await
            .into_iter()
            .find(|a| matches!(a.get_info().device_type,
                wgpu::DeviceType::DiscreteGpu | wgpu::DeviceType::IntegratedGpu))
            .ok_or("no hardware Vulkan adapter available")?;
        let info = adapter.get_info();
        let max_bytes = (values.max(count).max(channels) * 4) as u64;
        let readback_bytes = values.checked_mul(8).ok_or("readback size overflow")? as u64;
        if max_bytes > adapter.limits().max_storage_buffer_binding_size as u64 {
            return Err("tensor exceeds Vulkan storage-buffer limit".into());
        }
        if readback_bytes > adapter.limits().max_buffer_size {
            return Err("mass/enthalpy readback exceeds Vulkan buffer limit".into());
        }
        let (device, queue) = adapter
            .request_device(&wgpu::DeviceDescriptor {
                label: Some("OniSim multicomponent mass/enthalpy stage"),
                required_limits: wgpu::Limits::default(),
                ..Default::default()
            })
            .await
            .map_err(|e| e.to_string())?;
        let scope = device.push_error_scope(wgpu::ErrorFilter::Validation);
        let shader = device.create_shader_module(wgpu::ShaderModuleDescriptor {
            label: Some("multicomponent conservative mass/enthalpy flux"),
            source: wgpu::ShaderSource::Wgsl(SHADER.into()),
        });
        let pipeline = device.create_compute_pipeline(&wgpu::ComputePipelineDescriptor {
            label: Some("multicomponent mass/enthalpy flux pipeline"),
            layout: None,
            module: &shader,
            entry_point: Some("main"),
            compilation_options: Default::default(),
            cache: None,
        });
        let storage = wgpu::BufferUsages::STORAGE | wgpu::BufferUsages::COPY_DST;
        let make = |label: &str, bytes: usize, usage| {
            device.create_buffer(&wgpu::BufferDescriptor {
                label: Some(label),
                size: bytes.max(4) as u64,
                usage,
                mapped_at_creation: false,
            })
        };
        let mass_a = make("mass A", values * 4, storage | wgpu::BufferUsages::COPY_SRC);
        let mass_b = make("mass B", values * 4, storage | wgpu::BufferUsages::COPY_SRC);
        let enthalpy_a = make("enthalpy A", values * 4, storage | wgpu::BufferUsages::COPY_SRC);
        let enthalpy_b = make("enthalpy B", values * 4, storage | wgpu::BufferUsages::COPY_SRC);
        let velocity_x = make("velocity x", count * 4, storage);
        let velocity_y = make("velocity y", count * 4, storage);
        let solid = make("solid mask", count * 4, storage);
        let drift_y = make("species vertical drift", channels * 4, storage);
        let params = make("uniform parameters", 16,
            wgpu::BufferUsages::UNIFORM | wgpu::BufferUsages::COPY_DST);
        let readback = make("mass and enthalpy readback", values * 8,
            wgpu::BufferUsages::MAP_READ | wgpu::BufferUsages::COPY_DST);

        let create_group = |mass_in: &wgpu::Buffer, mass_out: &wgpu::Buffer,
                            enthalpy_in: &wgpu::Buffer, enthalpy_out: &wgpu::Buffer| {
            let entries = [
                bind(0, mass_in),
                bind(1, &velocity_x),
                bind(2, &velocity_y),
                bind(3, &solid),
                bind(4, &drift_y),
                bind(5, mass_out),
                bind(6, &params),
                bind(7, enthalpy_in),
                bind(8, enthalpy_out),
            ];
            device.create_bind_group(&wgpu::BindGroupDescriptor {
                label: Some("multicomponent mass/enthalpy bindings"),
                layout: &pipeline.get_bind_group_layout(0),
                entries: &entries,
            })
        };
        let groups = [
            create_group(&mass_a, &mass_b, &enthalpy_a, &enthalpy_b),
            create_group(&mass_b, &mass_a, &enthalpy_b, &enthalpy_a),
        ];
        if let Some(error) = scope.pop().await {
            return Err(error.to_string());
        }
        Ok(Self {
            device, queue, pipeline, groups, mass_a, mass_b, enthalpy_a, enthalpy_b,
            velocity_x, velocity_y, solid, drift_y, params, readback,
            width, height, channels, count, uploaded: false, max_cfl_rate: 0.0, adapter: info,
        })
    }

    pub fn upload(&mut self, state: &State<'_>) -> Result<(), String> {
        self.uploaded = false;
        let max_rate = validate_state(state, self.count, self.channels)?;
        let drift: Vec<f32> = state.species.iter().map(|species| species.vertical_drift).collect();
        self.queue.write_buffer(&self.mass_a, 0, bytemuck::cast_slice(state.mass));
        self.queue.write_buffer(&self.enthalpy_a, 0, bytemuck::cast_slice(state.enthalpy));
        self.queue.write_buffer(&self.velocity_x, 0, bytemuck::cast_slice(state.velocity_x));
        self.queue.write_buffer(&self.velocity_y, 0, bytemuck::cast_slice(state.velocity_y));
        self.queue.write_buffer(&self.solid, 0, bytemuck::cast_slice(state.solid));
        self.queue.write_buffer(&self.drift_y, 0, bytemuck::cast_slice(&drift));
        self.max_cfl_rate = max_rate;
        self.uploaded = true;
        Ok(())
    }

    pub fn run_steps_and_readback(
        &mut self,
        steps: usize,
        dt: f32,
        mass_output: &mut [f32],
        enthalpy_output: &mut [f32],
    ) -> Result<(), String> {
        let values = self.count * self.channels;
        if !self.uploaded || steps == 0 || mass_output.len() != values
            || enthalpy_output.len() != values || !dt.is_finite() || dt <= 0.0
            || dt * self.max_cfl_rate > 1.0 {
            return Err("resident run requires valid uploaded state, positive steps/dt and matching outputs".into());
        }
        self.queue.write_buffer(&self.params, 0, bytemuck::cast_slice(&[
            self.width as u32, self.height as u32, self.channels as u32, dt.to_bits()
        ]));
        let mut encoder = self.device.create_command_encoder(&Default::default());
        for step in 0..steps {
            let mut pass = encoder.begin_compute_pass(&Default::default());
            pass.set_pipeline(&self.pipeline);
            pass.set_bind_group(0, &self.groups[step & 1], &[]);
            pass.dispatch_workgroups((self.width as u32).div_ceil(8),
                (self.height as u32).div_ceil(8), 1);
        }
        let (mass_final, enthalpy_final) = if steps & 1 == 1 {
            (&self.mass_b, &self.enthalpy_b)
        } else {
            (&self.mass_a, &self.enthalpy_a)
        };
        let bytes = (values * 4) as u64;
        encoder.copy_buffer_to_buffer(mass_final, 0, &self.readback, 0, bytes);
        encoder.copy_buffer_to_buffer(enthalpy_final, 0, &self.readback, bytes, bytes);
        let submission = self.queue.submit([encoder.finish()]);
        let (send, receive) = mpsc::sync_channel(1);
        self.readback.map_async(wgpu::MapMode::Read, .., move |result| {
            let _ = send.send(result);
        });
        self.device.poll(wgpu::PollType::Wait {
            submission_index: Some(submission),
            timeout: Some(Duration::from_secs(30)),
        }).map_err(|e| e.to_string())?;
        receive.recv_timeout(Duration::from_secs(2)).map_err(|e| e.to_string())?
            .map_err(|e| e.to_string())?;
        {
            let view = self.readback.get_mapped_range(..).map_err(|e| e.to_string())?;
            let values_read: &[f32] = bytemuck::cast_slice(&view);
            mass_output.copy_from_slice(&values_read[..values]);
            enthalpy_output.copy_from_slice(&values_read[values..values * 2]);
        }
        self.readback.unmap();
        Ok(())
    }
}


/// Validates the CPU-side channel catalogue and conserved state before any GPU
/// buffer is mutated. This is also used by CPU-only regression tests.
fn validate_state(state: &State<'_>, count: usize, channels: usize) -> Result<f32, String> {
    let values = count.checked_mul(channels).ok_or("tensor dimensions overflow")?;
    if state.mass.len() != values || state.enthalpy.len() != values
        || state.velocity_x.len() != count || state.velocity_y.len() != count
        || state.solid.len() != count || state.species.len() != channels {
        return Err("input tensor or species metadata dimensions mismatch".into());
    }
    let mut ids = HashSet::with_capacity(channels);
    for (channel, species) in state.species.iter().enumerate() {
        if species.phase != 1 {
            return Err(format!("channel {channel} is not gas; this transport operator is gas-only"));
        }
        if !species.specific_heat_capacity.is_finite()
            || species.specific_heat_capacity <= 0.0
            || !species.vertical_drift.is_finite()
            || !ids.insert(species.element_id) {
            return Err(format!("invalid or duplicate gas species metadata at channel {channel}"));
        }
    }
    if state.mass.iter().any(|v| !v.is_finite() || *v < 0.0)
        || state.enthalpy.iter().any(|v| !v.is_finite() || *v < 0.0)
        || state.velocity_x.iter().chain(state.velocity_y).any(|v| !v.is_finite()) {
        return Err("state contains negative mass/enthalpy or non-finite values".into());
    }
    let mut max_rate = 0.0f32;
    for cell in 0..count {
        let start = cell * channels;
        let end = start + channels;
        if state.solid[cell] != 0
            && (state.mass[start..end].iter().any(|v| *v != 0.0)
                || state.enthalpy[start..end].iter().any(|v| *v != 0.0)) {
            return Err("solid cells must contain zero mass and enthalpy".into());
        }
        for channel in 0..channels {
            if state.mass[start + channel] == 0.0 && state.enthalpy[start + channel] != 0.0 {
                return Err("empty species channels must contain zero enthalpy".into());
            }
        }
        let max_vertical = state.species.iter()
            .map(|species| (state.velocity_y[cell] + species.vertical_drift).abs())
            .fold(0.0f32, f32::max);
        max_rate = max_rate.max(state.velocity_x[cell].abs() + max_vertical);
    }
    Ok(max_rate)
}

fn bind(binding: u32, buffer: &wgpu::Buffer) -> wgpu::BindGroupEntry<'_> {
    wgpu::BindGroupEntry { binding, resource: buffer.as_entire_binding() }
}

pub fn cpu_step(
    state: &State<'_>,
    width: usize,
    height: usize,
    channels: usize,
    dt: f32,
    mass_output: &mut [f32],
    enthalpy_output: &mut [f32],
) {
    let idx = |x: usize, y: usize, c: usize| (y * width + x) * channels + c;
    mass_output.copy_from_slice(state.mass);
    enthalpy_output.copy_from_slice(state.enthalpy);
    for y in 0..height {
        for x in 0..width {
            let cell = y * width + x;
            if state.solid[cell] != 0 {
                mass_output[cell * channels..(cell + 1) * channels].fill(0.0);
                enthalpy_output[cell * channels..(cell + 1) * channels].fill(0.0);
                continue;
            }
            for c in 0..channels {
                let mut mass_div = 0.0f32;
                let mut enthalpy_div = 0.0f32;
                let drift = state.species[c].vertical_drift;
                if x + 1 < width && state.solid[cell + 1] == 0 {
                    let v = 0.5 * (state.velocity_x[cell] + state.velocity_x[cell + 1]);
                    let donor = if v >= 0.0 { idx(x, y, c) } else { idx(x + 1, y, c) };
                    mass_div += v * state.mass[donor];
                    enthalpy_div += v * state.enthalpy[donor];
                }
                if x > 0 && state.solid[cell - 1] == 0 {
                    let v = 0.5 * (state.velocity_x[cell - 1] + state.velocity_x[cell]);
                    let donor = if v >= 0.0 { idx(x - 1, y, c) } else { idx(x, y, c) };
                    mass_div -= v * state.mass[donor];
                    enthalpy_div -= v * state.enthalpy[donor];
                }
                if y + 1 < height && state.solid[cell + width] == 0 {
                    let v = 0.5 * (state.velocity_y[cell] + state.velocity_y[cell + width]) + drift;
                    let donor = if v >= 0.0 { idx(x, y, c) } else { idx(x, y + 1, c) };
                    mass_div += v * state.mass[donor];
                    enthalpy_div += v * state.enthalpy[donor];
                }
                if y > 0 && state.solid[cell - width] == 0 {
                    let v = 0.5 * (state.velocity_y[cell - width] + state.velocity_y[cell]) + drift;
                    let donor = if v >= 0.0 { idx(x, y - 1, c) } else { idx(x, y, c) };
                    mass_div -= v * state.mass[donor];
                    enthalpy_div -= v * state.enthalpy[donor];
                }
                let slot = idx(x, y, c);
                mass_output[slot] = state.mass[slot] - dt * mass_div;
                enthalpy_output[slot] = state.enthalpy[slot] - dt * enthalpy_div;
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn wgsl_shader_validates() {
        let module = naga::front::wgsl::parse_str(SHADER).expect("WGSL parser");
        naga::valid::Validator::new(
            naga::valid::ValidationFlags::all(),
            naga::valid::Capabilities::all(),
        ).validate(&module).expect("WGSL validation");
    }

    #[test]
    fn cpu_reference_conserves_species_mass_and_enthalpy_and_respects_walls() {
        let (width, height, channels) = (19usize, 13usize, 4usize);
        let count = width * height;
        let mut solid = vec![0u32; count];
        for x in 0..width { solid[x] = 1; solid[(height - 1) * width + x] = 1; }
        for y in 0..height { solid[y * width] = 1; solid[y * width + width - 1] = 1; }
        for y in 4..7 { for x in 8..11 { solid[y * width + x] = 1; } }
        let mut mass = vec![0.0f32; count * channels];
        let mut enthalpy = vec![0.0f32; count * channels];
        for cell in 0..count {
            if solid[cell] == 0 {
                for c in 0..channels {
                    let m = ((cell * 17 + c * 13) % 97) as f32 / 97.0;
                    mass[cell * channels + c] = m;
                    enthalpy[cell * channels + c] = m * (1.0 + c as f32 * 0.25) * (280.0 + c as f32 * 15.0);
                }
            }
        }
        let vx = vec![0.015f32; count];
        let vy = vec![-0.01f32; count];
        let species = [
            SpeciesInfo { element_id: 1, phase: 1, specific_heat_capacity: 1.0, vertical_drift: -0.002 },
            SpeciesInfo { element_id: 2, phase: 1, specific_heat_capacity: 1.25, vertical_drift: -0.001 },
            SpeciesInfo { element_id: 3, phase: 1, specific_heat_capacity: 1.5, vertical_drift: 0.001 },
            SpeciesInfo { element_id: 4, phase: 1, specific_heat_capacity: 1.75, vertical_drift: 0.002 },
        ];
        let state = State { mass: &mass, enthalpy: &enthalpy, velocity_x: &vx,
            velocity_y: &vy, solid: &solid, species: &species };
        let mut mass_out = vec![0.0f32; count * channels];
        let mut enthalpy_out = vec![0.0f32; count * channels];
        cpu_step(&state, width, height, channels, 0.1, &mut mass_out, &mut enthalpy_out);
        for c in 0..channels {
            let before_mass: f64 = (0..count).map(|i| mass[i * channels + c] as f64).sum();
            let after_mass: f64 = (0..count).map(|i| mass_out[i * channels + c] as f64).sum();
            let before_energy: f64 = (0..count).map(|i| enthalpy[i * channels + c] as f64).sum();
            let after_energy: f64 = (0..count).map(|i| enthalpy_out[i * channels + c] as f64).sum();
            assert!((after_mass - before_mass).abs() / before_mass.max(1e-30) < 1e-6, "species mass {c}");
            assert!((after_energy - before_energy).abs() / before_energy.max(1e-30) < 1e-6, "species enthalpy {c}");
        }
        assert!(mass_out.iter().chain(enthalpy_out.iter()).all(|v| v.is_finite() && *v >= -1e-7));
        for cell in 0..count {
            if solid[cell] != 0 {
                assert!(mass_out[cell * channels..(cell + 1) * channels].iter().all(|v| *v == 0.0));
                assert!(enthalpy_out[cell * channels..(cell + 1) * channels].iter().all(|v| *v == 0.0));
            }
        }
    }

    #[test]
    fn validates_gas_species_metadata_and_enthalpy_state() {
        let mass = [1.0f32, 1.0];
        let enthalpy = [300.0f32, 600.0];
        let velocity = [0.0f32];
        let solid = [0u32];
        let valid = [
            SpeciesInfo { element_id: 1, phase: 1, specific_heat_capacity: 1.0, vertical_drift: 0.0 },
            SpeciesInfo { element_id: 2, phase: 1, specific_heat_capacity: 2.0, vertical_drift: 0.0 },
        ];
        let state = State { mass: &mass, enthalpy: &enthalpy, velocity_x: &velocity,
            velocity_y: &velocity, solid: &solid, species: &valid };
        assert_eq!(validate_state(&state, 1, 2).unwrap(), 0.0);

        let duplicate = [valid[0], valid[0]];
        let duplicate_state = State { species: &duplicate, ..state };
        assert!(validate_state(&duplicate_state, 1, 2).is_err());

        let liquid = [
            valid[0],
            SpeciesInfo { phase: 2, ..valid[1] },
        ];
        let liquid_state = State { species: &liquid, ..state };
        assert!(validate_state(&liquid_state, 1, 2).is_err());

        let bad_energy = [300.0f32, f32::NAN];
        let energy_state = State { enthalpy: &bad_energy, ..state };
        assert!(validate_state(&energy_state, 1, 2).is_err());

        let empty_mass = [0.0f32, 1.0];
        let empty_energy = [1.0f32, 600.0];
        let empty_state = State { mass: &empty_mass, enthalpy: &empty_energy, ..state };
        assert!(validate_state(&empty_state, 1, 2).is_err());
    }
}
