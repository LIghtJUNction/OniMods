//! Region radiation passes using a shared GPU context.
use std::sync::Arc;
use crate::gpu_context::GpuContext;

#[repr(C)]
#[derive(Clone, Copy, bytemuck::Pod, bytemuck::Zeroable)]
pub struct RadiationCell {
    pub mass: f32, pub radiation: f32, pub absorption: f32, pub emission: f32,
    pub properties: u32, pub disease: u32, pub disease_count: i32, pub padding: u32,
}
#[repr(C)]
#[derive(Clone, Copy, bytemuck::Pod, bytemuck::Zeroable)]
pub struct RadiationResult { pub radiation: f32, pub transmission: f32 }
#[repr(C)]
#[derive(Clone, Copy, bytemuck::Pod, bytemuck::Zeroable)]
pub struct RadiationParams {
    pub width: u32, pub height: u32, pub nuclear: u32, pub padding: u32,
    pub dose: f32, pub cosmic: f32, pub absorption_solid: f32, pub absorption_mass_scale: f32,
    pub mass_scale: f32, pub absorption_base: f32, pub padding0: f32, pub padding1: f32,
}
struct Storage {
    capacity: usize, cells: wgpu::Buffer, output: wgpu::Buffer, readback: wgpu::Buffer,
    uniform: wgpu::Buffer, columns: wgpu::BindGroup, gather: wgpu::BindGroup,
}
pub struct RadiationRegionGpu { context: Arc<GpuContext>, columns: wgpu::ComputePipeline, gather: wgpu::ComputePipeline,
    storage: Option<Storage>, busy: Arc<std::sync::atomic::AtomicBool> }

pub struct PendingRadiation {
    device: Arc<wgpu::Device>, readback: wgpu::Buffer, submission: wgpu::SubmissionIndex,
    receiver: std::sync::mpsc::Receiver<Result<(), wgpu::BufferAsyncError>>,
    count: usize, busy: Arc<std::sync::atomic::AtomicBool>,
}
impl PendingRadiation {
    pub fn finish(self, result: &mut [RadiationResult]) -> Result<(), String> {
        if result.len() != self.count { return Err("radiation result length mismatch".into()); }
        self.device.poll(wgpu::PollType::Wait { submission_index: Some(self.submission.clone()), timeout: Some(std::time::Duration::from_secs(5)) }).map_err(|e| e.to_string())?;
        self.receiver.recv_timeout(std::time::Duration::from_secs(5)).map_err(|e| e.to_string())?.map_err(|e| format!("radiation readback: {e:?}"))?;
        let view = self.readback.get_mapped_range(0..(self.count * 8) as u64).map_err(|e| e.to_string())?;
        result.copy_from_slice(bytemuck::cast_slice(&view));
        Ok(())
    }
}
impl Drop for PendingRadiation {
    fn drop(&mut self) { self.readback.unmap(); self.busy.store(false, std::sync::atomic::Ordering::Release); }
}

impl RadiationRegionGpu {
    pub fn new(context: Arc<GpuContext>) -> Self {
        let shader = context.device.create_shader_module(wgpu::ShaderModuleDescriptor { label: Some("OniSim radiation"),
            source: wgpu::ShaderSource::Wgsl(include_str!("radiation_region.wgsl").into()) });
        let pipeline = |name| context.device.create_compute_pipeline(&wgpu::ComputePipelineDescriptor {
            label: Some("OniSim radiation pass"), layout: None, module: &shader, entry_point: Some(name),
            compilation_options: Default::default(), cache: None,
        });
        let columns = pipeline("columns");
        let gather = pipeline("gather");
        Self { context, columns, gather, storage: None, busy: Arc::new(std::sync::atomic::AtomicBool::new(false)) }
    }

    pub fn begin(&mut self, params: RadiationParams, cells: &[RadiationCell]) -> Result<PendingRadiation, String> {
        let count = (params.width as usize).checked_mul(params.height as usize).ok_or("radiation grid overflow")?;
        if params.width < 2 || params.height < 1 || cells.len() != count { return Err("radiation region dimensions mismatch".into()); }
        if self.busy.load(std::sync::atomic::Ordering::Acquire) { return Err("radiation readback is still pending".into()); }
        let dispatch = u32::try_from(count).map_err(|_| "radiation grid too large")?.div_ceil(64);
        if dispatch > self.context.device.limits().max_compute_workgroups_per_dimension { return Err("radiation dispatch too large".into()); }
        let device = &self.context.device;
        if self.storage.as_ref().is_none_or(|s| s.capacity < count) {
            let make = |label, size: usize, usage| device.create_buffer(&wgpu::BufferDescriptor {
                label: Some(label), size: size as u64, usage, mapped_at_creation: false,
            });
            let storage = wgpu::BufferUsages::STORAGE | wgpu::BufferUsages::COPY_DST;
            let cells = make("radiation cells", count * 32, storage);
            let transmission = make("radiation transmission", count * 4, storage);
            let output = make("radiation output", count * 8, storage | wgpu::BufferUsages::COPY_SRC);
            let readback = make("radiation readback", count * 8, wgpu::BufferUsages::MAP_READ | wgpu::BufferUsages::COPY_DST);
            let uniform = make("radiation params", 48, wgpu::BufferUsages::UNIFORM | wgpu::BufferUsages::COPY_DST);
            let make_group = |pipeline: &wgpu::ComputePipeline, bindings: &[(u32, &wgpu::Buffer)]| {
                let entries: Vec<_> = bindings.iter().map(|(binding, buffer)| wgpu::BindGroupEntry { binding: *binding, resource: buffer.as_entire_binding() }).collect();
                device.create_bind_group(&wgpu::BindGroupDescriptor { label: Some("radiation bindings"), layout: &pipeline.get_bind_group_layout(0), entries: &entries })
            };
            let columns = make_group(&self.columns, &[(0, &cells), (1, &transmission), (3, &uniform)]);
            let gather = make_group(&self.gather, &[(0, &cells), (1, &transmission), (2, &output), (3, &uniform)]);
            self.storage = Some(Storage { capacity: count, cells, output, readback, uniform, columns, gather });
        }
        let s = self.storage.as_ref().ok_or("radiation storage missing")?;
        self.context.queue.write_buffer(&s.cells, 0, bytemuck::cast_slice(cells));
        self.context.queue.write_buffer(&s.uniform, 0, bytemuck::bytes_of(&params));
        let mut encoder = device.create_command_encoder(&Default::default());
        { let mut pass = encoder.begin_compute_pass(&Default::default()); pass.set_pipeline(&self.columns); pass.set_bind_group(0, &s.columns, &[]); pass.dispatch_workgroups(params.width.div_ceil(64), 1, 1); }
        { let mut pass = encoder.begin_compute_pass(&Default::default()); pass.set_pipeline(&self.gather); pass.set_bind_group(0, &s.gather, &[]); pass.dispatch_workgroups(dispatch, 1, 1); }
        encoder.copy_buffer_to_buffer(&s.output, 0, &s.readback, 0, (count * 8) as u64);
        self.busy.store(true, std::sync::atomic::Ordering::Release);
        let submission = self.context.queue.submit([encoder.finish()]);
        let slice = s.readback.slice(0..(count * 8) as u64);
        let (tx, rx) = std::sync::mpsc::channel(); slice.map_async(wgpu::MapMode::Read, move |r| { let _ = tx.send(r); });
        Ok(PendingRadiation { device: self.context.device.clone(), readback: s.readback.clone(), submission, receiver: rx, count, busy: self.busy.clone() })
    }
}
