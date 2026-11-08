//! Column-owned gas bubble and liquid droplet movement.
use std::sync::Arc;
use crate::gpu_context::GpuContext;

#[repr(C)]
#[derive(Clone, Copy, bytemuck::Pod, bytemuck::Zeroable)]
pub struct Cell { pub element: u32, pub mixture_element: u32, pub properties: u32, pub flags: u32,
    pub mass: f32, pub mixture_mass: f32, pub temperature: f32, pub padding: u32 }
#[repr(C)]
#[derive(Clone, Copy, bytemuck::Pod, bytemuck::Zeroable)]
pub struct Material { pub phase: u32, pub heat: f32 }
#[repr(C)]
#[derive(Clone, Copy, bytemuck::Pod, bytemuck::Zeroable)]
pub struct ResultCell { pub element: u32, pub mixture_element: u32, pub flags: u32, pub mass: f32, pub mixture_mass: f32, pub temperature: f32 }
#[repr(C)]
#[derive(Clone, Copy, bytemuck::Pod, bytemuck::Zeroable)]
struct Params { width: u32, height: u32, stride: u32, vacuum: u32 }

struct Storage { cell_capacity: usize, material_capacity: usize, cells: wgpu::Buffer, materials: wgpu::Buffer,
    uniform: wgpu::Buffer, output: wgpu::Buffer, readback: wgpu::Buffer, group: wgpu::BindGroup, cached_materials: Vec<u8> }
pub struct BubbleSettlingGpu { context: Arc<GpuContext>, pipeline: wgpu::ComputePipeline, storage: Option<Storage>, busy: Arc<std::sync::atomic::AtomicBool> }
pub struct PendingBubble { device: Arc<wgpu::Device>, readback: wgpu::Buffer, submission: wgpu::SubmissionIndex,
    receiver: std::sync::mpsc::Receiver<Result<(), wgpu::BufferAsyncError>>, bytes: usize, busy: Arc<std::sync::atomic::AtomicBool> }
impl Drop for PendingBubble {
    fn drop(&mut self) { self.readback.unmap(); self.busy.store(false, std::sync::atomic::Ordering::Release); }
}
impl PendingBubble {
    pub fn finish(self, cells: &mut [ResultCell]) -> Result<(), String> {
        if std::mem::size_of_val(cells) != self.bytes { return Err("bubble result length mismatch".into()); }
        self.device.poll(wgpu::PollType::Wait { submission_index: Some(self.submission.clone()), timeout: Some(std::time::Duration::from_secs(5)) }).map_err(|e| e.to_string())?;
        self.receiver.recv_timeout(std::time::Duration::from_secs(5)).map_err(|e| e.to_string())?.map_err(|e| format!("bubble readback: {e:?}"))?;
        let view = self.readback.get_mapped_range(0..self.bytes as u64).map_err(|e| e.to_string())?;
        cells.copy_from_slice(bytemuck::cast_slice(&view));
        Ok(())
    }
}

impl BubbleSettlingGpu {
    pub fn new(context: Arc<GpuContext>) -> Self {
        let shader = context.device.create_shader_module(wgpu::ShaderModuleDescriptor {
            label: Some("bubble settling"), source: wgpu::ShaderSource::Wgsl(include_str!("bubble_settling.wgsl").into()) });
        let pipeline = context.device.create_compute_pipeline(&wgpu::ComputePipelineDescriptor {
            label: Some("bubble settling"), layout: None, module: &shader, entry_point: Some("main"),
            compilation_options: Default::default(), cache: None });
        Self { context, pipeline, storage: None, busy: Arc::new(std::sync::atomic::AtomicBool::new(false)) }
    }
    pub fn begin(&mut self, width: u32, height: u32, vacuum: u32, cells: &[Cell], materials: &[Material]) -> Result<PendingBubble, String> {
        let stride = width.checked_add(2).ok_or("bubble stride overflow")?;
        let count = (stride as usize).checked_mul(height as usize + 2).ok_or("bubble grid overflow")?;
        if width == 0 || height == 0 || cells.len() != count || materials.is_empty() || vacuum as usize >= materials.len() {
            return Err("bubble dimensions mismatch".into());
        }
        if width.div_ceil(64) > self.context.device.limits().max_compute_workgroups_per_dimension { return Err("bubble dispatch too large".into()); }
        if self.busy.load(std::sync::atomic::Ordering::Acquire) { return Err("bubble readback is still pending".into()); }
        let bytes = count.checked_mul(std::mem::size_of::<Cell>()).ok_or("bubble buffer overflow")?;
        let result_bytes = (width as usize).checked_mul(height as usize).and_then(|n| n.checked_mul(std::mem::size_of::<ResultCell>())).ok_or("bubble output overflow")?;
        if bytes as u64 > self.context.device.limits().max_storage_buffer_binding_size as u64 { return Err("bubble grid exceeds storage limit".into()); }
        let material_bytes: &[u8] = bytemuck::cast_slice(materials);
        let device = &self.context.device;
        if self.storage.as_ref().is_none_or(|s| s.cell_capacity < bytes || s.material_capacity < material_bytes.len()) {
            let make = |label, size, usage| device.create_buffer(&wgpu::BufferDescriptor { label: Some(label), size, usage, mapped_at_creation: false });
            let state = make("bubble cells", bytes as u64, wgpu::BufferUsages::STORAGE | wgpu::BufferUsages::COPY_DST | wgpu::BufferUsages::COPY_SRC);
            let table = make("bubble materials", material_bytes.len() as u64, wgpu::BufferUsages::STORAGE | wgpu::BufferUsages::COPY_DST);
            let uniform = make("bubble params", 16, wgpu::BufferUsages::UNIFORM | wgpu::BufferUsages::COPY_DST);
            // The padded input allocation always bounds the unpadded output.
            let output = make("bubble output", bytes as u64, wgpu::BufferUsages::STORAGE | wgpu::BufferUsages::COPY_SRC);
            let readback = make("bubble readback", bytes as u64, wgpu::BufferUsages::MAP_READ | wgpu::BufferUsages::COPY_DST);
            let buffers = [&state, &table, &uniform, &output];
            let entries: Vec<_> = buffers.iter().enumerate().map(|(i,b)| wgpu::BindGroupEntry { binding: i as u32, resource: b.as_entire_binding() }).collect();
            let group = device.create_bind_group(&wgpu::BindGroupDescriptor { label: Some("bubble bindings"), layout: &self.pipeline.get_bind_group_layout(0), entries: &entries });
            self.storage = Some(Storage { cell_capacity: bytes, material_capacity: material_bytes.len(), cells: state,
                materials: table, uniform, output, readback, group, cached_materials: Vec::new() });
        }
        let s = self.storage.as_mut().ok_or("bubble storage missing")?;
        self.context.queue.write_buffer(&s.cells, 0, bytemuck::cast_slice(cells));
        if s.cached_materials != material_bytes { self.context.queue.write_buffer(&s.materials, 0, material_bytes); s.cached_materials = material_bytes.to_vec(); }
        self.context.queue.write_buffer(&s.uniform, 0, bytemuck::bytes_of(&Params { width, height, stride, vacuum }));
        let mut encoder = device.create_command_encoder(&Default::default());
        { let mut pass = encoder.begin_compute_pass(&Default::default()); pass.set_pipeline(&self.pipeline);
            pass.set_bind_group(0, &s.group, &[]); pass.dispatch_workgroups(width.div_ceil(64), 1, 1); }
        encoder.copy_buffer_to_buffer(&s.output, 0, &s.readback, 0, result_bytes as u64);
        self.busy.store(true, std::sync::atomic::Ordering::Release);
        let submission = self.context.queue.submit([encoder.finish()]);
        let slice = s.readback.slice(0..result_bytes as u64);
        let (tx, rx) = std::sync::mpsc::channel(); slice.map_async(wgpu::MapMode::Read, move |r| { let _ = tx.send(r); });
        Ok(PendingBubble { device: device.clone(), readback: s.readback.clone(), submission, receiver: rx, bytes: result_bytes, busy: self.busy.clone() })
    }
}
