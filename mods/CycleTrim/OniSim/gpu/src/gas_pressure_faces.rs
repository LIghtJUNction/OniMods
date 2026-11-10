//! Conservative pressure face pass for isolated homogeneous gas regions.
use std::sync::Arc;
use crate::gpu_context::GpuContext;

#[repr(C)]
#[derive(Clone, Copy, bytemuck::Pod, bytemuck::Zeroable)]
pub struct GasCell { pub element: u32, pub phase: u32, pub blocked: u32, pub padding: u32,
    pub mass: f32, pub temperature: f32, pub coefficient: f32, pub padding1: f32,
    pub max_mass: f32, pub viscosity: f32, pub min_horizontal_flow: f32, pub min_vertical_flow: f32,
    pub gas_element: u32, pub gas_mass: f32, pub gas_heat: f32, pub phase_heat: f32 }
#[repr(C)]
#[derive(Clone, Copy, bytemuck::Pod, bytemuck::Zeroable)]
pub struct GasResult { pub element: u32, pub changed: u32, pub mass: f32, pub temperature: f32,
    pub flow_left: f32, pub flow_right: f32, pub flow_up: f32, pub flow_down: f32,
    pub mixture_element: u32, pub mixture_mass: f32 }
#[repr(C)]
#[derive(Clone, Copy, bytemuck::Pod, bytemuck::Zeroable)]
struct Params { width: u32, height: u32, vacuum: u32, phase: u32, direction: i32, head_enabled: u32, padding: [u32; 2] }
struct Storage {
    capacity: usize, cells: wgpu::Buffer, output: wgpu::Buffer, readback: wgpu::Buffer, uniform: wgpu::Buffer,
    faces_group: wgpu::BindGroup, apply_group: wgpu::BindGroup, seed_group: wgpu::BindGroup,
    unite_group: wgpu::BindGroup, accumulate_group: wgpu::BindGroup, finalize_group: wgpu::BindGroup,
}
// One settled region per scan direction. Full inputs (including halo,
// temperature, material coefficients and wall flags) are the wake-up key.
struct IdleGasPass {
    shape: (u32, u32, u32),
    cells: Vec<GasCell>,
    result: Vec<GasResult>,
}
pub struct GasPressureFacesGpu { context: Arc<GpuContext>, faces: wgpu::ComputePipeline, apply: wgpu::ComputePipeline,
    head_seed: wgpu::ComputePipeline, head_unite: wgpu::ComputePipeline, head_accumulate: wgpu::ComputePipeline,
    head_finalize: wgpu::ComputePipeline, storage: Option<Storage>, idle: [Option<IdleGasPass>; 2],
    #[cfg(test)] cache_hits: usize }

impl GasPressureFacesGpu {
    pub fn new(context: Arc<GpuContext>) -> Self {
        let shader = context.device.create_shader_module(wgpu::ShaderModuleDescriptor {
            label: Some("OniSim gas pressure faces"), source: wgpu::ShaderSource::Wgsl(include_str!("gas_pressure_faces.wgsl").into()),
        });
        let pipeline = |name| context.device.create_compute_pipeline(&wgpu::ComputePipelineDescriptor {
            label: Some("OniSim gas pressure faces"), layout: None, module: &shader, entry_point: Some(name),
            compilation_options: Default::default(), cache: None,
        });
        let faces = pipeline("compute_faces"); let apply = pipeline("apply_faces");
        let head_shader = context.device.create_shader_module(wgpu::ShaderModuleDescriptor {
            label: Some("OniSim liquid hydraulic head"), source: wgpu::ShaderSource::Wgsl(include_str!("liquid_head.wgsl").into()),
        });
        let head_pipeline = |name| context.device.create_compute_pipeline(&wgpu::ComputePipelineDescriptor {
            label: Some("OniSim liquid hydraulic head"), layout: None, module: &head_shader, entry_point: Some(name),
            compilation_options: Default::default(), cache: None,
        });
        let head_seed = head_pipeline("seed"); let head_unite = head_pipeline("unite");
        let head_accumulate = head_pipeline("accumulate"); let head_finalize = head_pipeline("finalize");
        Self { context, faces, apply, head_seed, head_unite, head_accumulate, head_finalize, storage: None,
            idle: [None, None], #[cfg(test)] cache_hits: 0 }
    }
    pub fn run(&mut self, width: u32, height: u32, vacuum: u32, phase: u32, direction: i32, head_enabled: bool, cells: &[GasCell], result: &mut [GasResult]) -> Result<(), String> {
        let count = (width as usize).checked_mul(height as usize).ok_or("pressure grid overflow")?;
        if width == 0 || height == 0 || cells.len() != count || result.len() != count { return Err("pressure dimensions mismatch".into()); }
        let slot = usize::from(direction > 0);
        if phase == 1 && !head_enabled {
            if let Some(idle) = &self.idle[slot] {
                if idle.shape == (width, height, vacuum)
                    && bytemuck::cast_slice::<GasCell, u8>(&idle.cells) == bytemuck::cast_slice::<GasCell, u8>(cells) {
                    result.copy_from_slice(&idle.result);
                    #[cfg(test)] { self.cache_hits += 1; }
                    return Ok(());
                }
            }
            self.idle[slot] = None;
        }
        let dispatch = u32::try_from(count).map_err(|_| "pressure grid too large")?.div_ceil(64);
        let device = &self.context.device;
        if dispatch > device.limits().max_compute_workgroups_per_dimension { return Err("pressure dispatch too large".into()); }
        if self.storage.as_ref().is_none_or(|s| s.capacity < count) {
            let make = |label, size: usize, usage| device.create_buffer(&wgpu::BufferDescriptor {
                label: Some(label), size: size as u64, usage, mapped_at_creation: false,
            });
            let storage = wgpu::BufferUsages::STORAGE | wgpu::BufferUsages::COPY_DST;
            let cells = make("pressure cells", count * 64, storage);
            let faces = make("pressure faces", count * 12, storage);
            let output = make("pressure output", count * 40, storage | wgpu::BufferUsages::COPY_SRC);
            let readback = make("pressure readback", count * 40, wgpu::BufferUsages::MAP_READ | wgpu::BufferUsages::COPY_DST);
            let head_labels = make("liquid connectivity labels", count * 4, storage);
            let head_maxima = make("liquid component heights", count * 4, storage);
            let head_output = make("liquid hydraulic head", count * 4, storage);
            let uniform = make("pressure face params", 32, wgpu::BufferUsages::UNIFORM | wgpu::BufferUsages::COPY_DST);
            let bind = |pipeline: &wgpu::ComputePipeline, buffers: &[(u32, &wgpu::Buffer)]| {
                let entries: Vec<_> = buffers.iter().map(|(i,b)| wgpu::BindGroupEntry { binding: *i, resource: b.as_entire_binding() }).collect();
                device.create_bind_group(&wgpu::BindGroupDescriptor { label: Some("pressure face bindings"), layout: &pipeline.get_bind_group_layout(0), entries: &entries })
            };
            let faces_group = bind(&self.faces, &[(0, &cells), (1, &faces), (3, &uniform), (4, &head_output)]);
            let apply_group = bind(&self.apply, &[(0, &cells), (1, &faces), (2, &output), (3, &uniform)]);
            let seed_group = bind(&self.head_seed, &[(0, &cells), (1, &head_labels), (2, &head_maxima), (3, &uniform)]);
            let unite_group = bind(&self.head_unite, &[(0, &cells), (1, &head_labels), (3, &uniform)]);
            let accumulate_group = bind(&self.head_accumulate, &[(0, &cells), (1, &head_labels), (2, &head_maxima), (3, &uniform)]);
            let finalize_group = bind(&self.head_finalize, &[(0, &cells), (1, &head_labels), (2, &head_maxima), (3, &uniform), (4, &head_output)]);
            self.storage = Some(Storage { capacity: count, cells, output, readback, uniform,
                faces_group, apply_group, seed_group, unite_group, accumulate_group, finalize_group });
        }
        let s = self.storage.as_ref().ok_or("pressure storage missing")?;
        self.context.queue.write_buffer(&s.cells, 0, bytemuck::cast_slice(cells));
        self.context.queue.write_buffer(&s.uniform, 0, bytemuck::bytes_of(&Params { width, height, vacuum, phase, direction, head_enabled: u32::from(head_enabled), padding: [0; 2] }));
        let mut encoder = device.create_command_encoder(&Default::default());
        if phase == 2 && head_enabled {
            { let mut p = encoder.begin_compute_pass(&Default::default()); p.set_pipeline(&self.head_seed); p.set_bind_group(0, &s.seed_group, &[]); p.dispatch_workgroups(dispatch, 1, 1); }
            { let mut p = encoder.begin_compute_pass(&Default::default()); p.set_pipeline(&self.head_unite); p.set_bind_group(0, &s.unite_group, &[]); p.dispatch_workgroups(dispatch, 1, 1); }
            { let mut p = encoder.begin_compute_pass(&Default::default()); p.set_pipeline(&self.head_accumulate); p.set_bind_group(0, &s.accumulate_group, &[]); p.dispatch_workgroups(dispatch, 1, 1); }
            { let mut p = encoder.begin_compute_pass(&Default::default()); p.set_pipeline(&self.head_finalize); p.set_bind_group(0, &s.finalize_group, &[]); p.dispatch_workgroups(dispatch, 1, 1); }
        }
        { let mut p = encoder.begin_compute_pass(&Default::default()); p.set_pipeline(&self.faces); p.set_bind_group(0, &s.faces_group, &[]); p.dispatch_workgroups(dispatch, 1, 1); }
        { let mut p = encoder.begin_compute_pass(&Default::default()); p.set_pipeline(&self.apply); p.set_bind_group(0, &s.apply_group, &[]); p.dispatch_workgroups(dispatch, 1, 1); }
        encoder.copy_buffer_to_buffer(&s.output, 0, &s.readback, 0, (count * 40) as u64);
        let submission = self.context.queue.submit([encoder.finish()]);
        let slice = s.readback.slice(0..(count * 40) as u64);
        let (tx, rx) = std::sync::mpsc::channel(); slice.map_async(wgpu::MapMode::Read, move |r| { let _ = tx.send(r); });
        device.poll(wgpu::PollType::Wait { submission_index: Some(submission), timeout: Some(std::time::Duration::from_secs(5)) }).map_err(|e| e.to_string())?;
        rx.recv_timeout(std::time::Duration::from_secs(5)).map_err(|e| e.to_string())?.map_err(|e| format!("pressure readback: {e:?}"))?;
        { let view = s.readback.get_mapped_range(0..(count * 40) as u64).map_err(|e| e.to_string())?; result.copy_from_slice(bytemuck::cast_slice(&view)); }
        s.readback.unmap();
        if phase == 1 && !head_enabled && result.iter().all(|cell| cell.changed == 0) {
            self.idle[slot] = Some(IdleGasPass { shape: (width, height, vacuum),
                cells: cells.to_vec(), result: result.to_vec() });
        }
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use bytemuck::Zeroable;

    #[test]
    fn checkerboard_settles_sleeps_and_wakes_on_input_change() {
        let context = Arc::new(pollster::block_on(GpuContext::new()).unwrap());
        let mut gpu = GasPressureFacesGpu::new(context);
        let width = 10usize;
        let mut cells = vec![GasCell::zeroed(); width * width];
        for y in 0..width { for x in 0..width {
            let c = &mut cells[y * width + x];
            c.blocked = u32::from(x == 0 || y == 0 || x + 1 == width || y + 1 == width);
            c.element = 3; c.phase = 1; c.coefficient = 0.25;
            c.mass = if (x + y) % 2 == 0 { 2.0 } else { 0.1 };
            c.temperature = 300.0; c.phase_heat = 1.0; c.max_mass = 1.0;
        }}
        let mut result = vec![GasResult::zeroed(); cells.len()];
        for tick in 0..512 {
            gpu.run(width as u32, width as u32, 0, 1, if tick % 2 == 0 { 1 } else { -1 },
                false, &cells, &mut result).unwrap();
            for (c, r) in cells.iter_mut().zip(&result) {
                if r.changed != 0 { c.mass = r.mass; c.temperature = r.temperature; c.element = r.element; }
            }
        }
        let low = cells.iter().filter(|c| c.blocked == 0).map(|c| c.mass).fold(f32::MAX, f32::min);
        let high = cells.iter().filter(|c| c.blocked == 0).map(|c| c.mass).fold(0.0, f32::max);
        assert!(high - low < 0.001, "checkerboard persists: {low}..{high}");
        assert!(gpu.cache_hits > 0, "settled gas never slept");
        let hits = gpu.cache_hits;
        let source = 4 * width + 4;
        cells[source].mass += 1.0;
        gpu.run(width as u32, width as u32, 0, 1, 1, false, &cells, &mut result).unwrap();
        assert_eq!(gpu.cache_hits, hits, "new gas failed to wake solver");
        assert!(result[source].mass < cells[source].mass);
        assert!(result[source + 1].mass > cells[source + 1].mass);
    }
}
