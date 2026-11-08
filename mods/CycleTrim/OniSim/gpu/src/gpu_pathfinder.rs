//! Experimental jump-flood geometric-distance estimates for standalone GPU work.
//!
//! This does not implement ONI navigation or obstacle-aware shortest paths: seeds
//! can jump across blocked cells. Keep these estimates out of game chore ranking,
//! which already has evaluated navigation costs.
use std::sync::Arc;
use crate::gpu_context::GpuContext;

#[repr(C)]
#[derive(Clone, Copy, bytemuck::Pod, bytemuck::Zeroable, Debug, Default)]
pub struct ChoreCandidate {
    pub chore_id: u32,
    pub target_cell: u32,
    pub priority: i32,
    pub cost: i32,
}

#[repr(C)]
#[derive(Clone, Copy, bytemuck::Pod, bytemuck::Zeroable)]
struct Params {
    width: u32,
    height: u32,
    stride: u32,
    chore_count: u32,
    step_size: u32,
    origin_cell: u32,
    padding0: u32,
    padding1: u32,
}

struct Storage {
    width: u32,
    height: u32,
    nav_grid: wgpu::Buffer,
    cost_field: wgpu::Buffer,
    next_cost_field: wgpu::Buffer,
    chores: wgpu::Buffer,
    chores_readback: wgpu::Buffer,
    params: wgpu::Buffer,
    group: wgpu::BindGroup,
    chore_capacity: usize,
}

pub struct GpuPathfinder {
    context: Arc<GpuContext>,
    init_pipeline: wgpu::ComputePipeline,
    step_pipeline: wgpu::ComputePipeline,
    eval_pipeline: wgpu::ComputePipeline,
    bind_group_layout: wgpu::BindGroupLayout,
    storage: Option<Storage>,
}

impl GpuPathfinder {
    pub fn new(context: Arc<GpuContext>) -> Self {
        let shader = context.device.create_shader_module(wgpu::ShaderModuleDescriptor {
            label: Some("gpu pathfinder"),
            source: wgpu::ShaderSource::Wgsl(include_str!("gpu_pathfinder.wgsl").into()),
        });
        let bind_group_layout = context.device.create_bind_group_layout(&wgpu::BindGroupLayoutDescriptor {
            label: Some("pathfinder bind group layout"),
            entries: &[
                wgpu::BindGroupLayoutEntry { binding: 0, visibility: wgpu::ShaderStages::COMPUTE, ty: wgpu::BindingType::Buffer { ty: wgpu::BufferBindingType::Storage { read_only: true }, has_dynamic_offset: false, min_binding_size: None }, count: None },
                wgpu::BindGroupLayoutEntry { binding: 1, visibility: wgpu::ShaderStages::COMPUTE, ty: wgpu::BindingType::Buffer { ty: wgpu::BufferBindingType::Storage { read_only: false }, has_dynamic_offset: false, min_binding_size: None }, count: None },
                wgpu::BindGroupLayoutEntry { binding: 2, visibility: wgpu::ShaderStages::COMPUTE, ty: wgpu::BindingType::Buffer { ty: wgpu::BufferBindingType::Storage { read_only: false }, has_dynamic_offset: false, min_binding_size: None }, count: None },
                wgpu::BindGroupLayoutEntry { binding: 3, visibility: wgpu::ShaderStages::COMPUTE, ty: wgpu::BindingType::Buffer { ty: wgpu::BufferBindingType::Storage { read_only: false }, has_dynamic_offset: false, min_binding_size: None }, count: None },
                wgpu::BindGroupLayoutEntry { binding: 4, visibility: wgpu::ShaderStages::COMPUTE, ty: wgpu::BindingType::Buffer { ty: wgpu::BufferBindingType::Uniform, has_dynamic_offset: false, min_binding_size: None }, count: None },
            ],
        });
        let pipeline_layout = context.device.create_pipeline_layout(&wgpu::PipelineLayoutDescriptor {
            label: Some("pathfinder pipeline layout"),
            bind_group_layouts: &[Some(&bind_group_layout)],
            immediate_size: 0,
        });
        let make_pipeline = |entry: &str| {
            context.device.create_compute_pipeline(&wgpu::ComputePipelineDescriptor {
                label: Some(entry),
                layout: Some(&pipeline_layout),
                module: &shader,
                entry_point: Some(entry),
                compilation_options: Default::default(),
                cache: None,
            })
        };
        let init_pipeline = make_pipeline("init_cost");
        let step_pipeline = make_pipeline("step_cost");
        let eval_pipeline = make_pipeline("evaluate_chores");

        Self {
            context,
            init_pipeline,
            step_pipeline,
            eval_pipeline,
            bind_group_layout,
            storage: None,
        }
    }

    pub fn ensure_grid(&mut self, width: u32, height: u32, nav_mask: &[u32]) -> Result<(), String> {
        let count = (width * height) as usize;
        if nav_mask.len() != count {
            return Err("nav_mask size mismatch".into());
        }
        let device = &self.context.device;
        let cell_bytes = (count * 4) as u64;
        let chore_cap = 2048usize;
        let chore_bytes = (chore_cap * std::mem::size_of::<ChoreCandidate>()) as u64;

        if self.storage.as_ref().map_or(true, |s| s.width != width || s.height != height) {
            let make = |label, size, usage| {
                device.create_buffer(&wgpu::BufferDescriptor {
                    label: Some(label),
                    size,
                    usage,
                    mapped_at_creation: false,
                })
            };
            let nav_grid = make("nav grid", cell_bytes, wgpu::BufferUsages::STORAGE | wgpu::BufferUsages::COPY_DST);
            let cost_field = make("cost field", cell_bytes, wgpu::BufferUsages::STORAGE | wgpu::BufferUsages::COPY_DST | wgpu::BufferUsages::COPY_SRC);
            let next_cost_field = make("next cost field", cell_bytes, wgpu::BufferUsages::STORAGE | wgpu::BufferUsages::COPY_DST | wgpu::BufferUsages::COPY_SRC);
            let chores = make("chores", chore_bytes, wgpu::BufferUsages::STORAGE | wgpu::BufferUsages::COPY_DST | wgpu::BufferUsages::COPY_SRC);
            let chores_readback = make("chores readback", chore_bytes, wgpu::BufferUsages::MAP_READ | wgpu::BufferUsages::COPY_DST);
            let params = make("pathfinder params", 32, wgpu::BufferUsages::UNIFORM | wgpu::BufferUsages::COPY_DST);

            let entries = [
                wgpu::BindGroupEntry { binding: 0, resource: nav_grid.as_entire_binding() },
                wgpu::BindGroupEntry { binding: 1, resource: cost_field.as_entire_binding() },
                wgpu::BindGroupEntry { binding: 2, resource: next_cost_field.as_entire_binding() },
                wgpu::BindGroupEntry { binding: 3, resource: chores.as_entire_binding() },
                wgpu::BindGroupEntry { binding: 4, resource: params.as_entire_binding() },
            ];
            let group = device.create_bind_group(&wgpu::BindGroupDescriptor {
                label: Some("pathfinder bindings"),
                layout: &self.bind_group_layout,
                entries: &entries,
            });
            self.storage = Some(Storage {
                width,
                height,
                nav_grid,
                cost_field,
                next_cost_field,
                chores,
                chores_readback,
                params,
                group,
                chore_capacity: chore_cap,
            });
        }

        let s = self.storage.as_ref().unwrap();
        self.context.queue.write_buffer(&s.nav_grid, 0, bytemuck::cast_slice(nav_mask));
        Ok(())
    }

    /// Writes Manhattan-distance estimates into the candidate cost fields.
    ///
    /// The grid masks target cells, but propagation does not follow traversable
    /// routes. A negative cost means no estimate was produced, not that an ONI
    /// navigator cannot reach the target.
    pub fn evaluate_chores(
        &mut self,
        origin_cell: u32,
        chores: &mut [ChoreCandidate],
    ) -> Result<(), String> {
        if chores.is_empty() { return Ok(()); }
        let s = self.storage.as_ref().ok_or("pathfinder grid not initialized")?;
        let chore_count = chores.len().min(s.chore_capacity);
        let count = s.width * s.height;

        let mut encoder = self.context.device.create_command_encoder(&Default::default());

        // 1. Init cost field
        let p_init = Params {
            width: s.width,
            height: s.height,
            stride: s.width,
            chore_count: chore_count as u32,
            step_size: 1,
            origin_cell,
            padding0: 0,
            padding1: 0,
        };
        self.context.queue.write_buffer(&s.params, 0, bytemuck::bytes_of(&p_init));
        self.context.queue.write_buffer(&s.chores, 0, bytemuck::cast_slice(&chores[..chore_count]));

        {
            let mut pass = encoder.begin_compute_pass(&Default::default());
            pass.set_pipeline(&self.init_pipeline);
            pass.set_bind_group(0, &s.group, &[]);
            pass.dispatch_workgroups(count.div_ceil(64), 1, 1);
        }
        self.context.queue.submit([encoder.finish()]);

        // 2. Propagate geometric-distance seeds with jump flooding.
        let mut step = (s.width.max(s.height) / 2).next_power_of_two();
        while step >= 1 {
            let p_step = Params {
                width: s.width,
                height: s.height,
                stride: s.width,
                chore_count: chore_count as u32,
                step_size: step,
                origin_cell,
                padding0: 0,
                padding1: 0,
            };
            self.context.queue.write_buffer(&s.params, 0, bytemuck::bytes_of(&p_step));

            let mut step_encoder = self.context.device.create_command_encoder(&Default::default());
            {
                let mut pass = step_encoder.begin_compute_pass(&Default::default());
                pass.set_pipeline(&self.step_pipeline);
                pass.set_bind_group(0, &s.group, &[]);
                pass.dispatch_workgroups(count.div_ceil(64), 1, 1);
            }
            step_encoder.copy_buffer_to_buffer(&s.next_cost_field, 0, &s.cost_field, 0, (count * 4) as u64);
            self.context.queue.submit([step_encoder.finish()]);

            step /= 2;
        }

        // 3. Evaluate chore candidate costs
        let mut final_encoder = self.context.device.create_command_encoder(&Default::default());
        {
            let mut pass = final_encoder.begin_compute_pass(&Default::default());
            pass.set_pipeline(&self.eval_pipeline);
            pass.set_bind_group(0, &s.group, &[]);
            pass.dispatch_workgroups((chore_count as u32).div_ceil(64), 1, 1);
        }

        let read_bytes = (chore_count * std::mem::size_of::<ChoreCandidate>()) as u64;
        final_encoder.copy_buffer_to_buffer(&s.chores, 0, &s.chores_readback, 0, read_bytes);

        self.context.queue.submit([final_encoder.finish()]);

        let (tx, rx) = std::sync::mpsc::channel();
        s.chores_readback.slice(0..read_bytes).map_async(wgpu::MapMode::Read, move |res| {
            let _ = tx.send(res);
        });
        self.context.device.poll(wgpu::PollType::Wait { submission_index: None, timeout: Some(std::time::Duration::from_secs(1)) })
            .map_err(|e| e.to_string())?;
        rx.recv_timeout(std::time::Duration::from_secs(1))
            .map_err(|e| e.to_string())?
            .map_err(|e| format!("{e:?}"))?;

        {
            let view = s.chores_readback.get_mapped_range(0..read_bytes).map_err(|e| e.to_string())?;
            let evaluated: &[ChoreCandidate] = bytemuck::cast_slice(&view);
            chores[..chore_count].copy_from_slice(evaluated);
        }
        s.chores_readback.unmap();

        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_gpu_pathfinder_evaluates_chores() {
        let context = Arc::new(pollster::block_on(GpuContext::new()).unwrap());
        let mut pathfinder = GpuPathfinder::new(context);
        let width = 16u32;
        let height = 16u32;
        let mut nav_mask = vec![1u32; (width * height) as usize];
        // Make cell (5, 5) an obstacle
        nav_mask[5 * 16 + 5] = 0;

        pathfinder.ensure_grid(width, height, &nav_mask).unwrap();

        let mut chores = vec![
            ChoreCandidate { chore_id: 1, target_cell: 0, priority: 5, cost: 0 },
            ChoreCandidate { chore_id: 2, target_cell: 10, priority: 8, cost: 0 },
            ChoreCandidate { chore_id: 3, target_cell: 5 * 16 + 5, priority: 9, cost: 0 }, // obstacle
        ];

        pathfinder.evaluate_chores(0, &mut chores).unwrap();
        eprintln!("Evaluated chores: {:?}", chores);

        assert_eq!(chores[0].cost, 0); // origin
        assert!(chores[1].cost > 0);   // reachable
        assert_eq!(chores[2].cost, -1); // unreachable obstacle
    }
}
