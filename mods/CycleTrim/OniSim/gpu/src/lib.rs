//! Targeted experimental operators, deliberately not enabled in the game.
//! The CPU helper is also called by native presentation; GPU code is not.
pub mod multicomponent;
pub mod pressure_projection;
#[path = "../../native/src/presentation_flow.rs"]
pub mod cpu;

use std::time::Duration;

pub struct FlowInput<'a> {
    pub old_element: &'a [u16],
    pub element: &'a [u16],
    pub mass: &'a [f32],
    pub flow: &'a [[f32; 4]],
}

pub struct FlowGpu {
    device: wgpu::Device,
    queue: wgpu::Queue,
    pipeline: wgpu::ComputePipeline,
    bindings: wgpu::BindGroup,
    inputs: [wgpu::Buffer; 4],
    output: wgpu::Buffer,
    readback: wgpu::Buffer,
    width: usize,
    height: usize,
    padded: usize,
    uploaded: bool,
    pub adapter: wgpu::AdapterInfo,
}

impl FlowGpu {
    pub async fn new(width: usize, height: usize, adapter_name: &str) -> Result<Self, String> {
        let padded = width
            .checked_add(2)
            .and_then(|s| height.checked_add(2)?.checked_mul(s))
            .ok_or("padded grid overflow")?;
        let count = width.checked_mul(height).ok_or("grid overflow")?;
        if count == 0 || count > u32::MAX as usize || padded > u32::MAX as usize {
            return Err("grid must be nonempty and fit u32 addressing".into());
        }
        let instance = wgpu::Instance::new(wgpu::InstanceDescriptor {
            backends: wgpu::Backends::VULKAN,
            ..wgpu::InstanceDescriptor::new_without_display_handle()
        });
        let adapters = instance.enumerate_adapters(wgpu::Backends::VULKAN).await;
        let adapter = adapters
            .into_iter()
            .find(|a| {
                let info = a.get_info();
                matches!(
                    info.device_type,
                    wgpu::DeviceType::DiscreteGpu | wgpu::DeviceType::IntegratedGpu
                ) && info
                    .name
                    .to_lowercase()
                    .contains(&adapter_name.to_lowercase())
            })
            .ok_or_else(|| format!("no hardware Vulkan adapter matching {adapter_name:?}"))?;
        let info = adapter.get_info();
        let limits = wgpu::Limits::default();
        if padded as u64 * 16 > limits.max_storage_buffer_binding_size
            || width.div_ceil(32) > limits.max_compute_workgroups_per_dimension as usize
            || height.div_ceil(8) > limits.max_compute_workgroups_per_dimension as usize
        {
            return Err("grid exceeds requested Vulkan buffer/dispatch limits".into());
        }
        let (device, queue) = adapter
            .request_device(&wgpu::DeviceDescriptor {
                label: Some("OniSim targeted compute"),
                required_limits: limits,
                ..Default::default()
            })
            .await
            .map_err(|e| e.to_string())?;
        // Validation failures must be returned to the bench, never ignored.
        let scope = device.push_error_scope(wgpu::ErrorFilter::Validation);
        let shader = device.create_shader_module(wgpu::ShaderModuleDescriptor {
            label: Some("OniSim flow WGSL"),
            source: wgpu::ShaderSource::Wgsl(include_str!("flow.wgsl").into()),
        });
        let pipeline = device.create_compute_pipeline(&wgpu::ComputePipelineDescriptor {
            label: Some("OniSim flow"),
            layout: None,
            module: &shader,
            entry_point: Some("main"),
            compilation_options: Default::default(),
            cache: None,
        });
        let buffer = |label, size, usage| {
            device.create_buffer(&wgpu::BufferDescriptor {
                label: Some(label),
                size,
                usage,
                mapped_at_creation: false,
            })
        };
        let input_usage = wgpu::BufferUsages::STORAGE | wgpu::BufferUsages::COPY_DST;
        let inputs = [
            buffer("flow", padded as u64 * 16, input_usage),
            buffer("mass", padded as u64 * 4, input_usage),
            buffer(
                "old elements",
                (padded as u64 * 2).next_multiple_of(4),
                input_usage,
            ),
            buffer(
                "elements",
                (padded as u64 * 2).next_multiple_of(4),
                input_usage,
            ),
        ];
        let output = buffer(
            "flow result",
            count as u64 * 8,
            wgpu::BufferUsages::STORAGE | wgpu::BufferUsages::COPY_SRC,
        );
        let readback = buffer(
            "flow readback",
            count as u64 * 8,
            wgpu::BufferUsages::MAP_READ | wgpu::BufferUsages::COPY_DST,
        );
        let shape = buffer(
            "grid shape",
            16,
            wgpu::BufferUsages::UNIFORM | wgpu::BufferUsages::COPY_DST,
        );
        queue.write_buffer(
            &shape,
            0,
            bytemuck::cast_slice(&[width as u32, height as u32, width as u32 + 2, count as u32]),
        );
        let entries: Vec<_> = inputs
            .iter()
            .chain([&output, &shape])
            .enumerate()
            .map(|(binding, buffer)| wgpu::BindGroupEntry {
                binding: binding as u32,
                resource: buffer.as_entire_binding(),
            })
            .collect();
        let bindings = device.create_bind_group(&wgpu::BindGroupDescriptor {
            label: Some("OniSim flow buffers"),
            layout: &pipeline.get_bind_group_layout(0),
            entries: &entries,
        });
        if let Some(error) = scope.pop().await {
            return Err(error.to_string());
        }
        Ok(Self {
            device,
            queue,
            pipeline,
            bindings,
            inputs,
            output,
            readback,
            width,
            height,
            padded,
            uploaded: false,
            adapter: info,
        })
    }

    /// End-to-end operation: upload current padded state, compute, synchronize,
    /// read back and copy into the existing CPU-owned presentation allocation.
    pub fn run(&mut self, input: &FlowInput<'_>, output: &mut [[f32; 2]]) -> Result<(), String> {
        if output.len() != self.width * self.height {
            return Err("output dimensions differ from allocated grid".into());
        }
        self.upload(input)?;
        self.run_resident(output)
    }

    /// Upload one complete native snapshot. Reusing it is explicit: a caller
    /// must upload again whenever any input changes.
    pub fn upload(&mut self, input: &FlowInput<'_>) -> Result<(), String> {
        self.uploaded = false;
        if [
            input.old_element.len(),
            input.element.len(),
            input.mass.len(),
            input.flow.len(),
        ]
        .iter()
        .any(|&len| len != self.padded)
        {
            return Err("input or output dimensions differ from allocated grid".into());
        }
        for (buffer, bytes) in self.inputs.iter().zip([
            bytemuck::cast_slice(input.flow),
            bytemuck::cast_slice(input.mass),
            bytemuck::cast_slice(input.old_element),
            bytemuck::cast_slice(input.element),
        ]) {
            let aligned = bytes.len() / 4 * 4;
            self.queue.write_buffer(buffer, 0, &bytes[..aligned]);
            if aligned != bytes.len() {
                let mut tail = [0; 4];
                tail[..bytes.len() - aligned].copy_from_slice(&bytes[aligned..]);
                self.queue.write_buffer(buffer, aligned as u64, &tail);
            }
        }
        self.uploaded = true;
        Ok(())
    }

    /// Diagnostic resident-input path. Includes dispatch, synchronization and
    /// readback, but excludes uploads. Not an end-to-end simulation speedup.
    pub fn run_resident(&mut self, output: &mut [[f32; 2]]) -> Result<(), String> {
        if !self.uploaded || output.len() != self.width * self.height {
            return Err(
                "resident dispatch requires an uploaded snapshot and matching output".into(),
            );
        }
        let mut encoder = self.device.create_command_encoder(&Default::default());
        {
            let mut pass = encoder.begin_compute_pass(&Default::default());
            pass.set_pipeline(&self.pipeline);
            pass.set_bind_group(0, &self.bindings, &[]);
            pass.dispatch_workgroups(
                (self.width as u32).div_ceil(32),
                (self.height as u32).div_ceil(8),
                1,
            );
        }
        encoder.copy_buffer_to_buffer(&self.output, 0, &self.readback, 0, output.len() as u64 * 8);
        let submission = self.queue.submit([encoder.finish()]);
        let (send, receive) = std::sync::mpsc::sync_channel(1);
        self.readback
            .map_async(wgpu::MapMode::Read, .., move |result| {
                let _ = send.send(result);
            });
        self.device
            .poll(wgpu::PollType::Wait {
                submission_index: Some(submission),
                timeout: Some(Duration::from_secs(10)),
            })
            .map_err(|e| e.to_string())?;
        receive
            .recv_timeout(Duration::from_secs(1))
            .map_err(|e| e.to_string())?
            .map_err(|e| e.to_string())?;
        {
            let view = self
                .readback
                .get_mapped_range(..)
                .map_err(|e| e.to_string())?;
            output.copy_from_slice(bytemuck::cast_slice(&view));
        }
        self.readback.unmap();
        Ok(())
    }
}
