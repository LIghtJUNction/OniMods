//! Device-resident disease growth pass. Callers own the snapshots and metadata.
use std::sync::Arc;

use crate::gpu_context::GpuContext;

#[repr(C)]
#[derive(Clone, Copy, bytemuck::Pod, bytemuck::Zeroable)]
pub struct DiseaseCell {
    pub element: u32,
    pub disease: u32,
    pub count: i32,
    pub infestation: u32,
    pub temperature: f32,
    pub mass: f32,
    pub radiation: f32,
    pub growth: f32,
}

#[repr(C)]
#[derive(Clone, Copy, bytemuck::Pod, bytemuck::Zeroable)]
pub struct DiseaseResult { pub disease: u32, pub count: i32, pub infestation: u32, pub growth: f32 }

#[repr(C)]
#[derive(Clone, Copy, bytemuck::Pod, bytemuck::Zeroable)]
pub struct DiseaseMetadata {
    pub temperature_range: [f32; 4],
    pub temperature_half_lives: [f32; 4],
    pub radiation_kill_rate: f32,
    pub padding: [f32; 3],
}

#[repr(C)]
#[derive(Clone, Copy, bytemuck::Pod, bytemuck::Zeroable)]
pub struct DiseaseElementMetadata {
    pub min_count_per_kg: f32,
    pub max_count_per_kg: f32,
    pub under_population_death_rate: f32,
    pub over_population_half_life: f32,
    pub population_half_life: f32,
    pub padding: [f32; 3],
}

#[repr(C)]
#[derive(Clone, Copy, bytemuck::Pod, bytemuck::Zeroable)]
struct Params { count: u32, element_count: u32, disease_count: u32, radiation_enabled: u32 }

pub struct DiseaseGrowthGpu {
    context: Arc<GpuContext>,
    pipeline: wgpu::ComputePipeline,
    storage: Option<DiseaseStorage>,
}

struct DiseaseStorage {
    cell_capacity: usize,
    disease_capacity: usize,
    element_capacity: usize,
    input: wgpu::Buffer,
    diseases: wgpu::Buffer,
    elements: wgpu::Buffer,
    output: wgpu::Buffer,
    readback: wgpu::Buffer,
    uniform: wgpu::Buffer,
    group: wgpu::BindGroup,
    previous_diseases: Vec<u8>,
    previous_elements: Vec<u8>,
}

impl DiseaseGrowthGpu {
    pub fn new(context: Arc<GpuContext>) -> Self {
        let device = &context.device;
        let module = device.create_shader_module(wgpu::ShaderModuleDescriptor {
            label: Some("OniSim disease growth"),
            source: wgpu::ShaderSource::Wgsl(include_str!("disease_growth.wgsl").into()),
        });
        let pipeline = device.create_compute_pipeline(&wgpu::ComputePipelineDescriptor {
            label: Some("OniSim disease growth"), layout: None, module: &module,
            entry_point: Some("main"), compilation_options: Default::default(), cache: None,
        });
        Self { context, pipeline, storage: None }
    }

    /// Bridge CPU-owned cells until the full simulation state is GPU-resident.
    pub fn run_cells(&mut self, cells: &mut [DiseaseCell], diseases: &[DiseaseMetadata],
        elements: &[DiseaseElementMetadata], element_count: usize, radiation_enabled: bool) -> Result<(), String> {
        if cells.is_empty() { return Ok(()); }
        if diseases.len().checked_mul(element_count) != Some(elements.len()) {
            return Err("disease metadata dimensions mismatch".into());
        }
        let count = u32::try_from(cells.len()).map_err(|_| "too many disease cells")?;
        let element_count = u32::try_from(element_count).map_err(|_| "too many elements")?;
        let disease_count = u32::try_from(diseases.len()).map_err(|_| "too many diseases")?;
        let device = &self.context.device;
        let cell_bytes = std::mem::size_of_val(cells);
        let result_bytes = cells.len() * std::mem::size_of::<DiseaseResult>();
        let disease_bytes: &[u8] = bytemuck::cast_slice(diseases);
        let element_bytes: &[u8] = bytemuck::cast_slice(elements);
        if self.storage.as_ref().is_none_or(|s| s.cell_capacity < cell_bytes || s.disease_capacity < disease_bytes.len() || s.element_capacity < element_bytes.len()) {
            let create = |label, size: usize, usage| device.create_buffer(&wgpu::BufferDescriptor {
                label: Some(label), size: size.max(16) as u64, usage, mapped_at_creation: false,
            });
            let storage = wgpu::BufferUsages::STORAGE | wgpu::BufferUsages::COPY_DST;
            let input = create("disease cells", cell_bytes, storage);
            let diseases = create("disease metadata", disease_bytes.len().max(std::mem::size_of::<DiseaseMetadata>()), storage);
            let elements = create("disease element metadata", element_bytes.len().max(std::mem::size_of::<DiseaseElementMetadata>()), storage);
            let output = create("disease output", result_bytes, storage | wgpu::BufferUsages::COPY_SRC);
            let readback = create("disease readback", result_bytes, wgpu::BufferUsages::MAP_READ | wgpu::BufferUsages::COPY_DST);
            let uniform = create("disease growth params", 16, wgpu::BufferUsages::UNIFORM | wgpu::BufferUsages::COPY_DST);
            let buffers = [&input, &diseases, &elements, &output, &uniform];
            let entries: Vec<_> = buffers.iter().enumerate().map(|(i,b)| wgpu::BindGroupEntry { binding: i as u32, resource: b.as_entire_binding() }).collect();
            let group = device.create_bind_group(&wgpu::BindGroupDescriptor { label: Some("disease growth bindings"), layout: &self.pipeline.get_bind_group_layout(0), entries: &entries });
            self.storage = Some(DiseaseStorage { cell_capacity: cell_bytes, disease_capacity: disease_bytes.len(), element_capacity: element_bytes.len(),
                input, diseases, elements, output, readback, uniform, group, previous_diseases: Vec::new(), previous_elements: Vec::new() });
        }
        let s = self.storage.as_mut().ok_or("disease storage missing")?;
        self.context.queue.write_buffer(&s.input, 0, bytemuck::cast_slice(cells));
        if s.previous_diseases != disease_bytes {
            self.context.queue.write_buffer(&s.diseases, 0, disease_bytes);
            s.previous_diseases = disease_bytes.to_vec();
        }
        if s.previous_elements != element_bytes {
            self.context.queue.write_buffer(&s.elements, 0, element_bytes);
            s.previous_elements = element_bytes.to_vec();
        }
        self.context.queue.write_buffer(&s.uniform, 0, bytemuck::bytes_of(&Params {
            count, element_count, disease_count, radiation_enabled: u32::from(radiation_enabled),
        }));
        let mut encoder = device.create_command_encoder(&Default::default());
        let s = self.storage.as_ref().ok_or("disease storage missing")?;
        if count.div_ceil(64) > device.limits().max_compute_workgroups_per_dimension { return Err("disease dispatch too large".into()); }
        { let mut pass = encoder.begin_compute_pass(&Default::default()); pass.set_pipeline(&self.pipeline);
            pass.set_bind_group(0, &s.group, &[]); pass.dispatch_workgroups(count.div_ceil(64), 1, 1); }
        encoder.copy_buffer_to_buffer(&s.output, 0, &s.readback, 0, result_bytes as u64);
        let submission = self.context.queue.submit([encoder.finish()]);
        let slice = s.readback.slice(0..result_bytes as u64);
        let (sender, receiver) = std::sync::mpsc::channel();
        slice.map_async(wgpu::MapMode::Read, move |result| { let _ = sender.send(result); });
        device.poll(wgpu::PollType::Wait { submission_index: Some(submission), timeout: Some(std::time::Duration::from_secs(5)) })
            .map_err(|error| error.to_string())?;
        receiver.recv_timeout(std::time::Duration::from_secs(5)).map_err(|error| error.to_string())?
            .map_err(|error| format!("disease readback failed: {error:?}"))?;
        {
            let view = s.readback.get_mapped_range(0..result_bytes as u64).map_err(|error| error.to_string())?;
            let results: &[DiseaseResult] = bytemuck::cast_slice(&view);
            for (cell, result) in cells.iter_mut().zip(results) {
                cell.disease = result.disease; cell.count = result.count;
                cell.infestation = result.infestation; cell.growth = result.growth;
            }
        }
        s.readback.unmap();
        Ok(())
    }

    /// Encode without submitting or mapping, so subsequent operators can consume
    /// the output on the same command encoder. Buffers must remain alive.
    pub fn encode(&self, encoder: &mut wgpu::CommandEncoder, input: &wgpu::Buffer,
        diseases: &wgpu::Buffer, disease_elements: &wgpu::Buffer, output: &wgpu::Buffer,
        count: u32, element_count: u32, disease_count: u32, radiation_enabled: bool) -> Result<(), String> {
        if count == 0 { return Ok(()); }
        let cell_bytes = u64::from(count) * std::mem::size_of::<DiseaseCell>() as u64;
        let result_bytes = u64::from(count) * std::mem::size_of::<DiseaseResult>() as u64;
        let disease_bytes = u64::from(disease_count) * std::mem::size_of::<DiseaseMetadata>() as u64;
        let element_bytes = u64::from(disease_count) * u64::from(element_count)
            * std::mem::size_of::<DiseaseElementMetadata>() as u64;
        if input.size() < cell_bytes || output.size() < result_bytes || diseases.size() < disease_bytes
            || disease_elements.size() < element_bytes || count.div_ceil(64) > self.context.device.limits().max_compute_workgroups_per_dimension {
            return Err("disease growth buffers or dispatch exceed bounds".into());
        }
        let params = self.context.device.create_buffer(&wgpu::BufferDescriptor {
            label: Some("disease growth params"), size: 16,
            usage: wgpu::BufferUsages::UNIFORM | wgpu::BufferUsages::COPY_DST, mapped_at_creation: false,
        });
        self.context.queue.write_buffer(&params, 0, bytemuck::bytes_of(&Params {
            count, element_count, disease_count, radiation_enabled: u32::from(radiation_enabled),
        }));
        let buffers = [input, diseases, disease_elements, output, &params];
        let entries: Vec<_> = buffers.iter().enumerate().map(|(binding, buffer)| wgpu::BindGroupEntry {
            binding: binding as u32, resource: buffer.as_entire_binding(),
        }).collect();
        let group = self.context.device.create_bind_group(&wgpu::BindGroupDescriptor {
            label: Some("disease growth bindings"), layout: &self.pipeline.get_bind_group_layout(0), entries: &entries,
        });
        let mut pass = encoder.begin_compute_pass(&Default::default());
        pass.set_pipeline(&self.pipeline);
        pass.set_bind_group(0, &group, &[]);
        pass.dispatch_workgroups(count.div_ceil(64), 1, 1);
        Ok(())
    }
}
