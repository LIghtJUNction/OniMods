//! Nonblocking inactive-cell mass decay; the caller owns the CPU snapshot.
use std::sync::Arc;
use crate::gpu_context::GpuContext;

#[repr(C)]
#[derive(Clone, Copy, bytemuck::Pod, bytemuck::Zeroable)]
pub struct Cell { pub mass: f32, pub phase: u32 }
#[repr(C)]
#[derive(Clone, Copy, bytemuck::Pod, bytemuck::Zeroable)]
struct Params { count: u32, padding: [u32; 3] }

struct Storage { capacity: usize, input: wgpu::Buffer, output: wgpu::Buffer, readback: wgpu::Buffer,
    params: wgpu::Buffer, group: wgpu::BindGroup }
pub struct InactiveDecayGpu { context: Arc<GpuContext>, pipeline: wgpu::ComputePipeline, storage: Option<Storage>, busy: Arc<std::sync::atomic::AtomicBool> }
pub struct PendingDecay { device: Arc<wgpu::Device>, readback: wgpu::Buffer, submission: wgpu::SubmissionIndex,
    receiver: std::sync::mpsc::Receiver<Result<(), wgpu::BufferAsyncError>>, count: usize, busy: Arc<std::sync::atomic::AtomicBool> }

impl Drop for PendingDecay {
    fn drop(&mut self) { self.readback.unmap(); self.busy.store(false, std::sync::atomic::Ordering::Release); }
}
impl PendingDecay {
    pub fn finish(self, result: &mut [f32]) -> Result<(), String> {
        if result.len() != self.count { return Err("inactive decay result length mismatch".into()); }
        self.device.poll(wgpu::PollType::Wait { submission_index: Some(self.submission.clone()), timeout: Some(std::time::Duration::from_secs(5)) }).map_err(|e| e.to_string())?;
        self.receiver.recv_timeout(std::time::Duration::from_secs(5)).map_err(|e| e.to_string())?.map_err(|e| format!("inactive decay readback: {e:?}"))?;
        let view = self.readback.get_mapped_range(0..(self.count * 4) as u64).map_err(|e| e.to_string())?;
        result.copy_from_slice(bytemuck::cast_slice(&view));
        Ok(())
    }
}
impl InactiveDecayGpu {
    pub fn new(context: Arc<GpuContext>) -> Self {
        let shader = context.device.create_shader_module(wgpu::ShaderModuleDescriptor { label: Some("inactive decay"), source: wgpu::ShaderSource::Wgsl(include_str!("inactive_decay.wgsl").into()) });
        let pipeline = context.device.create_compute_pipeline(&wgpu::ComputePipelineDescriptor { label: Some("inactive decay"), layout: None, module: &shader,
            entry_point: Some("main"), compilation_options: Default::default(), cache: None });
        Self { context, pipeline, storage: None, busy: Arc::new(std::sync::atomic::AtomicBool::new(false)) }
    }
    pub fn begin(&mut self, cells: &[Cell]) -> Result<PendingDecay, String> {
        let count = u32::try_from(cells.len()).map_err(|_| "inactive decay grid too large")?;
        if count == 0 || count.div_ceil(64) > self.context.device.limits().max_compute_workgroups_per_dimension { return Err("invalid inactive decay dispatch".into()); }
        if self.busy.load(std::sync::atomic::Ordering::Acquire) { return Err("inactive decay still pending".into()); }
        let device = &self.context.device;
        if self.storage.as_ref().is_none_or(|s| s.capacity < cells.len()) {
            let make = |label, size, usage| device.create_buffer(&wgpu::BufferDescriptor { label: Some(label), size, usage, mapped_at_creation: false });
            let input = make("inactive input", u64::from(count)*8, wgpu::BufferUsages::STORAGE | wgpu::BufferUsages::COPY_DST);
            let output = make("inactive output", u64::from(count)*4, wgpu::BufferUsages::STORAGE | wgpu::BufferUsages::COPY_SRC);
            let readback = make("inactive readback", u64::from(count)*4, wgpu::BufferUsages::MAP_READ | wgpu::BufferUsages::COPY_DST);
            let params = make("inactive params", 16, wgpu::BufferUsages::UNIFORM | wgpu::BufferUsages::COPY_DST);
            let buffers = [&input, &output, &params];
            let entries: Vec<_> = buffers.iter().enumerate().map(|(i,b)| wgpu::BindGroupEntry { binding: i as u32, resource: b.as_entire_binding() }).collect();
            let group = device.create_bind_group(&wgpu::BindGroupDescriptor { label: Some("inactive bindings"), layout: &self.pipeline.get_bind_group_layout(0), entries: &entries });
            self.storage = Some(Storage { capacity: cells.len(), input, output, readback, params, group });
        }
        let s = self.storage.as_ref().ok_or("inactive storage missing")?;
        self.context.queue.write_buffer(&s.input, 0, bytemuck::cast_slice(cells));
        self.context.queue.write_buffer(&s.params, 0, bytemuck::bytes_of(&Params { count, padding: [0;3] }));
        let mut encoder = device.create_command_encoder(&Default::default());
        { let mut pass = encoder.begin_compute_pass(&Default::default()); pass.set_pipeline(&self.pipeline); pass.set_bind_group(0, &s.group, &[]); pass.dispatch_workgroups(count.div_ceil(64), 1, 1); }
        encoder.copy_buffer_to_buffer(&s.output, 0, &s.readback, 0, u64::from(count)*4);
        self.busy.store(true, std::sync::atomic::Ordering::Release);
        let submission = self.context.queue.submit([encoder.finish()]);
        let (tx, receiver) = std::sync::mpsc::channel();
        s.readback.slice(0..u64::from(count)*4).map_async(wgpu::MapMode::Read, move |r| { let _ = tx.send(r); });
        Ok(PendingDecay { device: device.clone(), readback: s.readback.clone(), submission, receiver, count: count as usize, busy: self.busy.clone() })
    }
}
