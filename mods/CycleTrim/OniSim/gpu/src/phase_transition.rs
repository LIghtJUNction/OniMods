//! GPU phase selection; event application remains ordered on the caller.
use std::sync::Arc;
use crate::gpu_context::GpuContext;

#[repr(C)]
#[derive(Clone, Copy, bytemuck::Pod, bytemuck::Zeroable)]
pub struct PhaseCell {
    pub element: u32, pub mass: f32, pub temperature: f32, pub wall_element: u32,
    pub old_temperature: f32, pub old_mass: f32, pub wall_temperature: f32, pub wall_mass: f32,
    pub insulation: u32, pub enabled: u32, pub padding: [u32; 2],
}
#[repr(C)]
#[derive(Clone, Copy, bytemuck::Pod, bytemuck::Zeroable)]
pub struct PhaseMaterial {
    pub low_target: u32, pub high_target: u32, pub low_ore: u32, pub high_ore: u32,
    pub low_temperature: f32, pub high_temperature: f32, pub low_conversion: f32, pub high_conversion: f32,
    pub conductivity: f32, pub specific_heat: f32, pub phase: u32, pub padding0: u32,
    pub surface_solid: f32, pub surface_liquid: f32, pub surface_gas: f32, pub padding1: u32,
}
#[repr(C)]
#[derive(Clone, Copy, bytemuck::Pod, bytemuck::Zeroable)]
pub struct PhaseDecision {
    pub target_id: u32, pub ore: u32, pub conversion: f32, pub temperature: f32,
    pub wall_temperature: f32, pub wall_changed: u32, pub wall_transition: u32, pub cell_temperature: f32,
}
#[repr(C)]
#[derive(Clone, Copy, bytemuck::Pod, bytemuck::Zeroable)]
struct Params { count: u32, element_count: u32, padding: [u32; 2] }
struct Storage {
    cell_bytes: usize, material_bytes: usize, cells: wgpu::Buffer, materials: wgpu::Buffer,
    output: wgpu::Buffer, readback: wgpu::Buffer, uniform: wgpu::Buffer, group: wgpu::BindGroup,
    previous_materials: Vec<u8>,
}
pub struct PhaseTransitionGpu { context: Arc<GpuContext>, pipeline: wgpu::ComputePipeline, storage: Option<Storage> }

impl PhaseTransitionGpu {
    pub fn new(context: Arc<GpuContext>) -> Self {
        let shader = context.device.create_shader_module(wgpu::ShaderModuleDescriptor {
            label: Some("OniSim phase selection"), source: wgpu::ShaderSource::Wgsl(include_str!("phase_transition.wgsl").into()),
        });
        let pipeline = context.device.create_compute_pipeline(&wgpu::ComputePipelineDescriptor {
            label: Some("OniSim phase selection"), layout: None, module: &shader, entry_point: Some("main"),
            compilation_options: Default::default(), cache: None,
        });
        Self { context, pipeline, storage: None }
    }

    pub fn select_cpu(cells: &[PhaseCell], materials: &[PhaseMaterial], output: &mut [PhaseDecision]) {
        fn surface(material: &PhaseMaterial, other_phase: u32) -> f32 {
            match other_phase {
                1 => material.surface_gas,
                2 => material.surface_liquid,
                3 => material.surface_solid,
                _ => 1.0,
            }
        }
        for (c, out) in cells.iter().zip(output.iter_mut()) {
            let mut temperature = c.temperature;
            let mut wall_temperature = c.wall_temperature;
            let mut wall_changed = 0u32;
            let mut wall_transition = 0u32;
            let element_idx = c.element as usize;
            let wall_element_idx = c.wall_element as usize;
            if c.enabled != 0 && element_idx < materials.len() && wall_element_idx < materials.len() {
                let m = &materials[element_idx];
                let wall = &materials[wall_element_idx];
                let insulation = c.insulation as f32;
                let insulation2 = insulation * insulation;
                let cell_k = insulation2 * f32::from_bits(0x37810183) * m.conductivity;
                let mut conductance = cell_k.min(wall.conductivity);
                if insulation2 >= f32::from_bits(0x477e00fe) {
                    conductance = ((wall.conductivity.ln() + cell_k.ln()) * 0.5).exp();
                }
                let contact = conductance * surface(m, wall.phase) * surface(wall, m.phase);
                let tc = c.old_temperature;
                let tw = c.wall_temperature;
                let cc = c.old_mass * m.specific_heat;
                let cw = c.wall_mass * wall.specific_heat;
                let equilibrium = (cw * tw + cc * tc) / (cw + cc);
                let next_cell;
                if tc <= tw {
                    let delta = tw - tc;
                    let step = 0.01 * delta;
                    let mut energy = (delta * 0.2 * contact).min(step * cc);
                    energy = energy.min(step * cw);
                    next_cell = (tc + energy / cc).min(equilibrium);
                    wall_temperature = (tw - energy / cw).max(equilibrium);
                } else {
                    let delta = tc - tw;
                    let step = 0.01 * delta;
                    let mut energy = (delta * 0.2 * contact).min(step * cw);
                    energy = (step * cc).min(energy);
                    next_cell = (tc - energy / cc).max(equilibrium);
                    wall_temperature = (tw + energy / cw).min(equilibrium);
                }
                temperature = next_cell + (c.temperature - tc);
                wall_changed = 1;
                if wall_temperature < wall.low_temperature - 3.0 || wall_temperature > wall.high_temperature + 3.0 {
                    wall_transition = 1;
                }
            }
            let mut result = PhaseDecision {
                target_id: 65535, ore: 65535, conversion: 0.0, temperature,
                wall_temperature, wall_changed, wall_transition, cell_temperature: temperature,
            };
            if !(c.mass <= 0.0) && element_idx < materials.len() {
                let m = &materials[element_idx];
                if !(m.low_temperature - 3.0 <= temperature || m.low_target == 65535) {
                    let shifted = if temperature > -1.5 { temperature + 1.5 } else { 0.0 };
                    result.target_id = m.low_target;
                    result.ore = m.low_ore;
                    result.conversion = m.low_conversion;
                    result.temperature = shifted;
                } else if !(temperature <= 3.0 + m.high_temperature || m.high_target == 65535) {
                    let shifted = if temperature > 1.5 { temperature - 1.5 } else { 0.0 };
                    result.target_id = m.high_target;
                    result.ore = m.high_ore;
                    result.conversion = m.high_conversion;
                    result.temperature = shifted;
                }
            }
            *out = result;
        }
    }

    pub fn select(&mut self, cells: &[PhaseCell], materials: &[PhaseMaterial], output: &mut [PhaseDecision]) -> Result<(), String> {
        if cells.len() != output.len() { return Err("phase decision dimensions mismatch".into()); }
        if cells.is_empty() { return Ok(()); }
        if cells.len() < 256 {
            Self::select_cpu(cells, materials, output);
            return Ok(());
        }
        let count = u32::try_from(cells.len()).map_err(|_| "phase cells overflow")?;
        let element_count = u32::try_from(materials.len()).map_err(|_| "phase material count overflow")?;
        if count.div_ceil(64) > self.context.device.limits().max_compute_workgroups_per_dimension { return Err("phase dispatch too large".into()); }
        let data: &[u8] = bytemuck::cast_slice(cells);
        let output_bytes = std::mem::size_of_val(output);
        let metadata: &[u8] = bytemuck::cast_slice(materials);
        let device = &self.context.device;
        if self.storage.as_ref().is_none_or(|s| s.cell_bytes < data.len() || s.material_bytes < metadata.len()) {
            let make = |label, size: usize, usage| device.create_buffer(&wgpu::BufferDescriptor {
                label: Some(label), size: size.max(16) as u64, usage, mapped_at_creation: false,
            });
            let storage = wgpu::BufferUsages::STORAGE | wgpu::BufferUsages::COPY_DST;
            let cells = make("phase cells", data.len(), storage);
            let materials = make("phase materials", metadata.len(), storage);
            let output = make("phase output", data.len(), storage | wgpu::BufferUsages::COPY_SRC);
            let readback = make("phase readback", data.len(), wgpu::BufferUsages::MAP_READ | wgpu::BufferUsages::COPY_DST);
            let uniform = make("phase params", 16, wgpu::BufferUsages::UNIFORM | wgpu::BufferUsages::COPY_DST);
            let buffers = [&cells, &materials, &output, &uniform];
            let entries: Vec<_> = buffers.iter().enumerate().map(|(i,b)| wgpu::BindGroupEntry { binding: i as u32, resource: b.as_entire_binding() }).collect();
            let group = device.create_bind_group(&wgpu::BindGroupDescriptor { label: Some("phase bindings"), layout: &self.pipeline.get_bind_group_layout(0), entries: &entries });
            self.storage = Some(Storage { cell_bytes: data.len(), material_bytes: metadata.len(), cells, materials,
                output, readback, uniform, group, previous_materials: Vec::new() });
        }
        let s = self.storage.as_mut().ok_or("phase storage missing")?;
        self.context.queue.write_buffer(&s.cells, 0, data);
        if s.previous_materials != metadata { self.context.queue.write_buffer(&s.materials, 0, metadata); s.previous_materials = metadata.to_vec(); }
        self.context.queue.write_buffer(&s.uniform, 0, bytemuck::bytes_of(&Params { count, element_count, padding: [0; 2] }));
        let mut encoder = device.create_command_encoder(&Default::default());
        { let mut pass = encoder.begin_compute_pass(&Default::default()); pass.set_pipeline(&self.pipeline); pass.set_bind_group(0, &s.group, &[]); pass.dispatch_workgroups(count.div_ceil(64), 1, 1); }
        encoder.copy_buffer_to_buffer(&s.output, 0, &s.readback, 0, output_bytes as u64);
        let submission = self.context.queue.submit([encoder.finish()]);
        let slice = s.readback.slice(0..output_bytes as u64);
        let (tx, rx) = std::sync::mpsc::channel();
        slice.map_async(wgpu::MapMode::Read, move |result| { let _ = tx.send(result); });
        device.poll(wgpu::PollType::Wait { submission_index: Some(submission), timeout: Some(std::time::Duration::from_secs(5)) }).map_err(|e| e.to_string())?;
        rx.recv_timeout(std::time::Duration::from_secs(5)).map_err(|e| e.to_string())?.map_err(|e| format!("phase readback: {e:?}"))?;
        { let view = s.readback.get_mapped_range(0..output_bytes as u64).map_err(|e| e.to_string())?; output.copy_from_slice(bytemuck::cast_slice(&view)); }
        s.readback.unmap();
        Ok(())
    }
}
