//! GPU disease diffusion for one rectangle including its right/top halo.
use std::sync::Arc;
use crate::gpu_context::GpuContext;

#[repr(C)]
#[derive(Clone, Copy, bytemuck::Pod, bytemuck::Zeroable)]
pub struct SnapshotCell {
    pub element: u32, pub phase: u32, pub disease: u32, pub infestation: u32,
    pub count: i32, pub padding: [u32; 3],
}
#[repr(C)]
#[derive(Clone, Copy, bytemuck::Pod, bytemuck::Zeroable)]
pub struct ActiveCell { pub disease: u32, pub count: i32, pub infestation: u32, pub growth: f32 }
#[repr(C)]
#[derive(Clone, Copy, bytemuck::Pod, bytemuck::Zeroable)]
pub struct Disease { pub strength: f32 }
#[repr(C)]
#[derive(Clone, Copy, bytemuck::Pod, bytemuck::Zeroable)]
pub struct DiseaseElement { pub diffusion_scale: f32, pub min_diffusion_count: i32, pub min_infestation_tick: u32, pub padding: u32 }
#[repr(C)]
#[derive(Clone, Copy, bytemuck::Pod, bytemuck::Zeroable)]
struct Params { stride: u32, height: u32, min_x: u32, max_x: u32, min_y: u32, max_y: u32, element_count: u32, disease_count: u32 }

struct Storage {
    capacities: [usize; 4], old: wgpu::Buffer, active: wgpu::Buffer, diseases: wgpu::Buffer,
    elements: wgpu::Buffer, output: wgpu::Buffer, readback: wgpu::Buffer,
    uniform: wgpu::Buffer, group: wgpu::BindGroup,
    cached_diseases: Vec<u8>, cached_elements: Vec<u8>,
}
pub struct DiseaseDiffusionGpu { context: Arc<GpuContext>, pipeline: wgpu::ComputePipeline, storage: Option<Storage> }

impl DiseaseDiffusionGpu {
    pub fn new(context: Arc<GpuContext>) -> Self {
        let shader = context.device.create_shader_module(wgpu::ShaderModuleDescriptor {
            label: Some("OniSim disease diffusion"), source: wgpu::ShaderSource::Wgsl(include_str!("disease_diffusion.wgsl").into()),
        });
        let pipeline = context.device.create_compute_pipeline(&wgpu::ComputePipelineDescriptor {
            label: Some("OniSim disease diffusion"), layout: None, module: &shader, entry_point: Some("main"),
            compilation_options: Default::default(), cache: None,
        });
        Self { context, pipeline, storage: None }
    }

    pub fn run(&mut self, width: usize, height: usize, old: &[SnapshotCell], active: &mut [ActiveCell],
        diseases: &[Disease], elements: &[DiseaseElement], element_count: usize) -> Result<(), String> {
        let count = width.checked_mul(height).ok_or("disease grid overflow")?;
        if width < 2 || height < 2 || old.len() != count || active.len() != count
            || diseases.len().checked_mul(element_count) != Some(elements.len()) { return Err("disease diffusion dimensions mismatch".into()); }
        let count_u32 = u32::try_from(count).map_err(|_| "disease grid too large")?;
        if count_u32.div_ceil(64) > self.context.device.limits().max_compute_workgroups_per_dimension { return Err("disease dispatch too large".into()); }
        let params = Params { stride: u32::try_from(width).map_err(|_| "disease width overflow")?,
            height: u32::try_from(height).map_err(|_| "disease height overflow")?, min_x: 0, max_x: (width - 1) as u32,
            min_y: 0, max_y: (height - 1) as u32, element_count: u32::try_from(element_count).map_err(|_| "element count overflow")?,
            disease_count: u32::try_from(diseases.len()).map_err(|_| "disease count overflow")? };
        let old_bytes: &[u8] = bytemuck::cast_slice(old);
        let active_bytes: &[u8] = bytemuck::cast_slice(active);
        let disease_bytes: &[u8] = bytemuck::cast_slice(diseases);
        let element_bytes: &[u8] = bytemuck::cast_slice(elements);
        let needed = [old_bytes.len(), active_bytes.len(), disease_bytes.len(), element_bytes.len()];
        let device = &self.context.device;
        if self.storage.as_ref().is_none_or(|s| (0..4).any(|i| s.capacities[i] < needed[i])) {
            let make = |label, size: usize, usage| device.create_buffer(&wgpu::BufferDescriptor {
                label: Some(label), size: size.max(16) as u64, usage, mapped_at_creation: false,
            });
            let storage = wgpu::BufferUsages::STORAGE | wgpu::BufferUsages::COPY_DST;
            let old = make("disease old", needed[0], storage);
            let active = make("disease active", needed[1], storage);
            let diseases = make("disease strengths", needed[2], storage);
            let elements = make("disease diffusion metadata", needed[3], storage);
            let output = make("disease diffusion result", needed[1], storage | wgpu::BufferUsages::COPY_SRC);
            let readback = make("disease diffusion readback", needed[1], wgpu::BufferUsages::MAP_READ | wgpu::BufferUsages::COPY_DST);
            let uniform = make("disease diffusion params", 32, wgpu::BufferUsages::UNIFORM | wgpu::BufferUsages::COPY_DST);
            let buffers = [&old, &active, &diseases, &elements, &output, &uniform];
            let entries: Vec<_> = buffers.iter().enumerate().map(|(i,b)| wgpu::BindGroupEntry { binding: i as u32, resource: b.as_entire_binding() }).collect();
            let group = device.create_bind_group(&wgpu::BindGroupDescriptor { label: Some("disease diffusion bindings"), layout: &self.pipeline.get_bind_group_layout(0), entries: &entries });
            self.storage = Some(Storage { capacities: needed, old, active, diseases, elements, output, readback,
                uniform, group, cached_diseases: Vec::new(), cached_elements: Vec::new() });
        }
        let s = self.storage.as_mut().ok_or("disease storage missing")?;
        self.context.queue.write_buffer(&s.old, 0, old_bytes);
        self.context.queue.write_buffer(&s.active, 0, active_bytes);
        if s.cached_diseases != disease_bytes { self.context.queue.write_buffer(&s.diseases, 0, disease_bytes); s.cached_diseases = disease_bytes.to_vec(); }
        if s.cached_elements != element_bytes { self.context.queue.write_buffer(&s.elements, 0, element_bytes); s.cached_elements = element_bytes.to_vec(); }
        self.context.queue.write_buffer(&s.uniform, 0, bytemuck::bytes_of(&params));
        let mut encoder = device.create_command_encoder(&Default::default());
        { let mut pass = encoder.begin_compute_pass(&Default::default()); pass.set_pipeline(&self.pipeline); pass.set_bind_group(0, &s.group, &[]); pass.dispatch_workgroups(count_u32.div_ceil(64), 1, 1); }
        encoder.copy_buffer_to_buffer(&s.output, 0, &s.readback, 0, needed[1] as u64);
        let submission = self.context.queue.submit([encoder.finish()]);
        let slice = s.readback.slice(0..needed[1] as u64);
        let (tx, rx) = std::sync::mpsc::channel();
        slice.map_async(wgpu::MapMode::Read, move |result| { let _ = tx.send(result); });
        device.poll(wgpu::PollType::Wait { submission_index: Some(submission), timeout: Some(std::time::Duration::from_secs(5)) }).map_err(|e| e.to_string())?;
        rx.recv_timeout(std::time::Duration::from_secs(5)).map_err(|e| e.to_string())?.map_err(|e| format!("disease diffusion readback: {e:?}"))?;
        { let view = s.readback.get_mapped_range(0..needed[1] as u64).map_err(|e| e.to_string())?; active.copy_from_slice(bytemuck::cast_slice(&view)); }
        s.readback.unmap();
        Ok(())
    }
}
