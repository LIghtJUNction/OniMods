//! GPU-resident variable-coefficient finite-volume thermal diffusion operator.
use std::{cell::{Cell,RefCell}, sync::{mpsc, Arc}, time::Duration};

const SHADER: &str = include_str!("thermal_diffusion.wgsl");
const MATERIAL_SHADER: &str = include_str!("thermal_materials.wgsl");
const DEFERRED_SHADER: &str = include_str!("thermal_deferred.wgsl");

struct DeferredExpansion {
    pipeline:wgpu::ComputePipeline, group:wgpu::BindGroup,
    mixture:wgpu::Buffer, mass:wgpu::Buffer, properties:wgpu::Buffer,
    region:wgpu::Buffer, flags:wgpu::Buffer, params:wgpu::Buffer,
    dirty_workgroups:wgpu::Buffer, pending_workgroups:u32,
    previous_region:Vec<u32>, previous_flags:Vec<u32>,
    previous_mixture:Vec<u8>, previous_mass:Vec<u8>, previous_properties:Vec<u8>, pending:bool,
    valid:Cell<bool>, elements_revision:Option<u64>,
}

fn upload_changed_bytes(queue:&wgpu::Queue,buffer:&wgpu::Buffer,bytes:&[u8],previous:&mut Vec<u8>,block:usize,dirty:&mut [bool]) -> usize {
    let mut ranges=Vec::<(usize,usize)>::new();
    if previous.len()!=bytes.len() {ranges.push((0,bytes.len()));}
    else {for start in (0..bytes.len()).step_by(block) {
        let end=(start+block).min(bytes.len());
        if previous[start..end]!=bytes[start..end] {
            if let Some((_,last))=ranges.last_mut() {if *last==start {*last=end;continue;}}
            ranges.push((start,end));
        }
    }}
    if ranges.len()>16 {ranges.clear();ranges.push((0,bytes.len()));}
    let uploaded=ranges.iter().map(|&(start,end)|end-start).sum();
    for &(start,end) in &ranges {
        for index in start/block..end.div_ceil(block) {dirty[index]=true;}
        let full=end & !3;
        if full>start {queue.write_buffer(buffer,start as u64,&bytes[start..full]);}
        if full<end {let mut tail=[0u8;4];tail[..end-full].copy_from_slice(&bytes[full..end]);queue.write_buffer(buffer,full as u64,&tail);}
    }
    if previous.len()!=bytes.len() {*previous=bytes.to_vec();}
    else {for &(start,end) in &ranges {previous[start..end].copy_from_slice(&bytes[start..end]);}}
    uploaded
}

struct MaterialExpansion {
    pipeline: wgpu::ComputePipeline,
    group: wgpu::BindGroup,
    elements: wgpu::Buffer,
    table: wgpu::Buffer,
    params: wgpu::Buffer,
    dirty_workgroups: wgpu::Buffer,
    pending_workgroups: u32,
    previous_table: Vec<[u32;2]>,
    previous_elements: Vec<u16>,
    pending: bool,
    valid:Cell<bool>, revision:u64,
    changed_blocks:Vec<(usize,usize)>,
}

#[repr(C)]
#[derive(Clone, Copy, bytemuck::Pod, bytemuck::Zeroable)]
struct ThermalParams {
    width: u32,
    height: u32,
    dt: f32,
    max_transfer_fraction: f32,
}

pub struct ThermalDiffusionGpu {
    device: Arc<wgpu::Device>,
    queue: Arc<wgpu::Queue>,
    #[cfg(test)] flux_pipeline: wgpu::ComputePipeline,
    #[cfg(test)] apply_pipeline: wgpu::ComputePipeline,
    #[cfg(test)] groups: [[wgpu::BindGroup; 2]; 2],
    gather_pipeline: wgpu::ComputePipeline,
    gather_groups: [wgpu::BindGroup; 2],
    temperature_in: wgpu::Buffer,
    mass: wgpu::Buffer,
    previous_mass: RefCell<Vec<u8>>,
    mass_upload_bytes: Cell<usize>,
    conductivity: wgpu::Buffer,
    specific_heat: wgpu::Buffer,
    deferred_group: wgpu::Buffer,
    material_expansion: Option<MaterialExpansion>,
    deferred_expansion: Option<DeferredExpansion>,
    temperature_out: wgpu::Buffer,
    current: usize,
    params: wgpu::Buffer,
    readback: wgpu::Buffer,
    width: usize,
    height: usize,
    cells: usize,
    pub adapter: wgpu::AdapterInfo,
}

impl ThermalDiffusionGpu {
    pub async fn new(width: usize, height: usize) -> Result<Self, String> {
        let context = Arc::new(crate::gpu_context::GpuContext::new().await?);
        Self::new_with_context(context, width, height).await
    }

    pub async fn new_with_context(
        context: Arc<crate::gpu_context::GpuContext>,
        width: usize,
        height: usize,
    ) -> Result<Self, String> {
        if width < 2 || height < 2 {
            return Err("thermal diffusion grid must be at least 2x2".into());
        }
        let cells = width.checked_mul(height).ok_or("cell count overflow")?;
        #[cfg(test)] let u_flux_count = height.checked_mul(width - 1).ok_or("u flux overflow")?;
        #[cfg(test)] let v_flux_count = (height - 1).checked_mul(width).ok_or("v flux overflow")?;

        let device = &context.device;
        let shader = device.create_shader_module(wgpu::ShaderModuleDescriptor {
            label: Some("OniSim thermal diffusion WGSL"),
            source: wgpu::ShaderSource::Wgsl(SHADER.into()),
        });

        let make_pipeline = |label: &'static str, entry_point: &'static str| {
            device.create_compute_pipeline(&wgpu::ComputePipelineDescriptor {
                label: Some(label),
                layout: None,
                module: &shader,
                entry_point: Some(entry_point),
                compilation_options: Default::default(),
                cache: None,
            })
        };

        #[cfg(test)] let flux_pipeline = make_pipeline("thermal flux", "compute_flux");
        #[cfg(test)] let apply_pipeline = make_pipeline("thermal apply flux", "apply_thermal_flux");
        let gather_pipeline = make_pipeline("thermal gather flux", "gather_thermal_flux");

        let storage = wgpu::BufferUsages::STORAGE | wgpu::BufferUsages::COPY_DST;
        let make_buffer = |label: &'static str, bytes: usize, usage: wgpu::BufferUsages| {
            device.create_buffer(&wgpu::BufferDescriptor {
                label: Some(label),
                size: (bytes.max(4)) as u64,
                usage,
                mapped_at_creation: false,
            })
        };

        let temperature_in = make_buffer("temperature in", cells * 4, storage | wgpu::BufferUsages::COPY_SRC);
        let mass = make_buffer("mass", cells * 4, storage);
        let conductivity = make_buffer("conductivity", cells * 4, storage);
        let specific_heat = make_buffer("specific heat", cells * 4, storage);
        let deferred_group = make_buffer("deferred thermal groups", cells * 4, storage);
        #[cfg(test)] let flux_u = make_buffer("flux u", u_flux_count * 4, storage);
        #[cfg(test)] let flux_v = make_buffer("flux v", v_flux_count * 4, storage);
        let temperature_out = make_buffer(
            "temperature out",
            cells * 4,
            storage | wgpu::BufferUsages::COPY_SRC,
        );
        let params = make_buffer(
            "thermal params",
            std::mem::size_of::<ThermalParams>(),
            wgpu::BufferUsages::UNIFORM | wgpu::BufferUsages::COPY_DST,
        );
        let readback = make_buffer(
            "thermal readback",
            cells * 4,
            wgpu::BufferUsages::MAP_READ | wgpu::BufferUsages::COPY_DST,
        );

        #[cfg(test)] let bind = |pipeline: &wgpu::ComputePipeline, bindings: &[u32], input: &wgpu::Buffer, output: &wgpu::Buffer| {
            let buffers = [input, &mass, &conductivity, &specific_heat, &flux_u, &flux_v, output, &params, &deferred_group];
            let entries: Vec<_> = bindings
                .iter()
                .map(|&b| wgpu::BindGroupEntry {
                    binding: b,
                    resource: buffers[b as usize].as_entire_binding(),
                })
                .collect();
            device.create_bind_group(&wgpu::BindGroupDescriptor {
                label: Some("thermal diffusion bind group"),
                layout: &pipeline.get_bind_group_layout(0),
                entries: &entries,
            })
        };

        #[cfg(test)] let groups = [
            [bind(&flux_pipeline, &[0, 1, 2, 3, 4, 5, 7, 8], &temperature_in, &temperature_out),
             bind(&apply_pipeline, &[0, 1, 3, 4, 5, 6, 7], &temperature_in, &temperature_out)],
            [bind(&flux_pipeline, &[0, 1, 2, 3, 4, 5, 7, 8], &temperature_out, &temperature_in),
             bind(&apply_pipeline, &[0, 1, 3, 4, 5, 6, 7], &temperature_out, &temperature_in)],
        ];
        let bind_gather = |input: &wgpu::Buffer, output: &wgpu::Buffer| {
            let buffers = [(0, input), (1, &mass), (2, &conductivity), (3, &specific_heat),
                (6, output), (7, &params), (8, &deferred_group)];
            let entries: Vec<_> = buffers.iter().map(|(binding, buffer)| wgpu::BindGroupEntry {
                binding: *binding, resource: buffer.as_entire_binding(),
            }).collect();
            device.create_bind_group(&wgpu::BindGroupDescriptor {
                label: Some("thermal gather bind group"), layout: &gather_pipeline.get_bind_group_layout(0), entries: &entries,
            })
        };
        let gather_groups = [bind_gather(&temperature_in, &temperature_out), bind_gather(&temperature_out, &temperature_in)];

        Ok(Self {
            device: Arc::clone(&context.device),
            queue: Arc::clone(&context.queue),
            #[cfg(test)] flux_pipeline,
            #[cfg(test)] apply_pipeline,
            #[cfg(test)] groups,
            gather_pipeline,
            gather_groups,
            temperature_in,
            mass,
            previous_mass: RefCell::new(Vec::new()),
            mass_upload_bytes: Cell::new(0),
            conductivity,
            specific_heat,
            deferred_group,
            material_expansion: None,
            deferred_expansion: None,
            temperature_out,
            current: 0,
            params,
            readback,
            width,
            height,
            cells,
            adapter: context.adapter.clone(),
        })
    }

    pub fn prepare_deferred_groups_on_gpu(&mut self, mixture:&[u16], mass:&[f32], properties:&[u8], region:&[u32], flags:&[u32]) -> Result<(),String> {
        if mixture.len()!=self.cells || mass.len()!=self.cells || properties.len()!=self.cells || region.len()!=self.cells || flags.is_empty() || flags.len()>65536 {
            return Err("invalid deferred group input dimensions".into());
        }
        let material=self.material_expansion.as_ref().ok_or("material elements must be prepared first")?;
        let elements=material.elements.clone(); let revision=material.revision;
        let element_ranges=material.changed_blocks.clone();
        if self.deferred_expansion.is_none() {
            let shader=self.device.create_shader_module(wgpu::ShaderModuleDescriptor {label:Some("thermal deferred grouping"),source:wgpu::ShaderSource::Wgsl(DEFERRED_SHADER.into())});
            let pipeline=self.device.create_compute_pipeline(&wgpu::ComputePipelineDescriptor {label:Some("thermal deferred grouping"),layout:None,module:&shader,entry_point:Some("main"),compilation_options:Default::default(),cache:None});
            let storage=wgpu::BufferUsages::STORAGE|wgpu::BufferUsages::COPY_DST;
            let make=|label,size,usage|self.device.create_buffer(&wgpu::BufferDescriptor {label:Some(label),size,usage,mapped_at_creation:false});
            let mixture=make("thermal mixture elements",(self.cells.div_ceil(2)*4) as u64,storage);
            let mass=make("thermal mixture mass",(self.cells*4) as u64,storage);
            let properties=make("thermal cell properties",(self.cells.div_ceil(4)*4) as u64,storage);
            let region=make("thermal cell region",(self.cells*4) as u64,storage);
            let flags=make("thermal gas eligibility",65536*4,storage);
            let params=make("thermal deferred parameters",16,wgpu::BufferUsages::UNIFORM|wgpu::BufferUsages::COPY_DST);
            let dirty_workgroups=make("thermal deferred dirty workgroups",(self.cells.div_ceil(64)*4) as u64,storage);
            let buffers=[&elements,&mixture,&mass,&properties,&region,&flags,&self.deferred_group,&params,&dirty_workgroups];
            let entries:Vec<_>=buffers.iter().enumerate().map(|(i,b)|wgpu::BindGroupEntry {binding:i as u32,resource:b.as_entire_binding()}).collect();
            let group=self.device.create_bind_group(&wgpu::BindGroupDescriptor {label:Some("thermal deferred bindings"),layout:&pipeline.get_bind_group_layout(0),entries:&entries});
            self.deferred_expansion=Some(DeferredExpansion {pipeline,group,mixture,mass,properties,region,flags,params,dirty_workgroups,pending_workgroups:0,previous_region:Vec::new(),previous_flags:Vec::new(),previous_mixture:Vec::new(),previous_mass:Vec::new(),previous_properties:Vec::new(),pending:false,valid:Cell::new(false),elements_revision:None});
        }
        let data=self.deferred_expansion.as_mut().ok_or("missing deferred pipeline")?;
        let mut dirty=vec![false;self.cells.div_ceil(4096)];
        let mut changed=upload_changed_bytes(&self.queue,&data.mixture,bytemuck::cast_slice(mixture),&mut data.previous_mixture,8192,&mut dirty)>0;
        changed|=upload_changed_bytes(&self.queue,&data.mass,bytemuck::cast_slice(mass),&mut data.previous_mass,16384,&mut dirty)>0;
        changed|=upload_changed_bytes(&self.queue,&data.properties,properties,&mut data.previous_properties,4096,&mut dirty)>0;
        let mut full=!data.valid.get();
        if data.previous_region!=region {self.queue.write_buffer(&data.region,0,bytemuck::cast_slice(region));data.previous_region.clear();data.previous_region.extend_from_slice(region);changed=true;full=true;}
        if data.previous_flags!=flags {self.queue.write_buffer(&data.flags,0,bytemuck::cast_slice(flags));data.previous_flags.clear();data.previous_flags.extend_from_slice(flags);changed=true;full=true;}
        if data.elements_revision!=Some(revision) {
            if data.elements_revision.is_some_and(|old|old.wrapping_add(1)==revision) {
                for &(start,end) in &element_ranges {for i in start/4096..end.div_ceil(4096) {dirty[i]=true;}}
            } else {full=true;}
            data.elements_revision=Some(revision);changed=true;
        }
        if changed || full {
            if data.pending {full=true;}
            let mut workgroups=Vec::<u32>::new();
            if full {workgroups.extend(0..self.cells.div_ceil(64) as u32);}
            else {for (block,&active) in dirty.iter().enumerate() {if active {
                let start=block*4096;let end=(start+4096).min(self.cells);
                workgroups.extend((start/64) as u32..end.div_ceil(64) as u32);
            }}}
            self.queue.write_buffer(&data.dirty_workgroups,0,bytemuck::cast_slice(&workgroups));
            data.pending_workgroups=workgroups.len() as u32;
            self.queue.write_buffer(&data.params,0,bytemuck::cast_slice(&[self.cells as u32,flags.len() as u32,0,0]));
            data.pending=true;
        }
        Ok(())
    }

    /// Defer faces within each nonzero group to a separate thermal pass.
    pub fn prepare_materials_by_element(&mut self, elements:&[u16], table:&[[u32;2]]) -> Result<(),String> {
        if cfg!(target_endian="big") { return Err("packed material elements require a little-endian host".into()); }
        if elements.len()!=self.cells || table.is_empty() || table.len()>65536 ||
            self.cells.div_ceil(64)>self.device.limits().max_compute_workgroups_per_dimension as usize {
            return Err("invalid thermal material input dimensions".into());
        }
        if self.material_expansion.is_none() {
            let shader=self.device.create_shader_module(wgpu::ShaderModuleDescriptor {label:Some("thermal material expansion"),source:wgpu::ShaderSource::Wgsl(MATERIAL_SHADER.into())});
            let pipeline=self.device.create_compute_pipeline(&wgpu::ComputePipelineDescriptor {label:Some("thermal material expansion"),layout:None,module:&shader,entry_point:Some("main"),compilation_options:Default::default(),cache:None});
            let storage=wgpu::BufferUsages::STORAGE|wgpu::BufferUsages::COPY_DST;
            let make=|label,size,usage|self.device.create_buffer(&wgpu::BufferDescriptor {label:Some(label),size,usage,mapped_at_creation:false});
            let packed=make("thermal material element ids",(self.cells.div_ceil(2)*4) as u64,storage);
            let table=make("thermal material property table",65536*8,storage);
            let params=make("thermal material params",16,wgpu::BufferUsages::UNIFORM|wgpu::BufferUsages::COPY_DST);
            let dirty_workgroups=make("thermal material dirty workgroups",(self.cells.div_ceil(64)*4) as u64,storage);
            let buffers=[&packed,&table,&self.conductivity,&self.specific_heat,&params,&dirty_workgroups];
            let entries:Vec<_>=buffers.iter().enumerate().map(|(i,b)|wgpu::BindGroupEntry {binding:i as u32,resource:b.as_entire_binding()}).collect();
            let group=self.device.create_bind_group(&wgpu::BindGroupDescriptor {label:Some("thermal material expansion bindings"),layout:&pipeline.get_bind_group_layout(0),entries:&entries});
            self.material_expansion=Some(MaterialExpansion {pipeline,group,elements:packed,table,params,dirty_workgroups,pending_workgroups:0,previous_table:Vec::new(),previous_elements:Vec::new(),pending:false,valid:Cell::new(false),revision:0,changed_blocks:Vec::new()});
        }
        let expansion=self.material_expansion.as_mut().ok_or("missing thermal material expansion")?;
        let mut ranges=Vec::<(usize,usize)>::new();
        if expansion.previous_elements.len()!=elements.len() {ranges.push((0,elements.len()));}
        else {
            for start in (0..elements.len()).step_by(4096) {
                let end=(start+4096).min(elements.len());
                if expansion.previous_elements[start..end]!=elements[start..end] {
                    if let Some((_,last))=ranges.last_mut() {if *last==start {*last=end;continue;}}
                    ranges.push((start,end));
                }
            }
            if ranges.len()>16 {ranges.clear();ranges.push((0,elements.len()));}
        }
        let elements_changed=!ranges.is_empty();
        if elements_changed {expansion.revision=expansion.revision.wrapping_add(1);expansion.changed_blocks=ranges.clone();}
        for &(start,end) in &ranges {
            let even=end & !1;
            if even>start {self.queue.write_buffer(&expansion.elements,(start*2) as u64,bytemuck::cast_slice(&elements[start..even]));}
            if even<end {self.queue.write_buffer(&expansion.elements,(even*2) as u64,bytemuck::bytes_of(&(elements[even] as u32)));}
        }
        if expansion.previous_elements.len()!=elements.len() {expansion.previous_elements=elements.to_vec();}
        else {for &(start,end) in &ranges {expansion.previous_elements[start..end].copy_from_slice(&elements[start..end]);}}
        let table_changed=expansion.previous_table!=table;
        if table_changed {
            self.queue.write_buffer(&expansion.table,0,bytemuck::cast_slice(table));
            expansion.previous_table.clear(); expansion.previous_table.extend_from_slice(table);
        }
        if elements_changed || table_changed || !expansion.valid.get() {
            let mut workgroups=Vec::<u32>::new();
            if table_changed || !expansion.valid.get() || expansion.pending {
                workgroups.extend(0..self.cells.div_ceil(64) as u32);
            } else {
                for &(start,end) in &ranges {workgroups.extend((start/64) as u32..end.div_ceil(64) as u32);}
            }
            self.queue.write_buffer(&expansion.dirty_workgroups,0,bytemuck::cast_slice(&workgroups));
            expansion.pending_workgroups=workgroups.len() as u32;
            self.queue.write_buffer(&expansion.params,0,bytemuck::cast_slice(&[self.cells as u32,table.len() as u32,0,0]));
            expansion.pending=true;
        }
        Ok(())
    }

    /// Defer faces within each nonzero group to a separate thermal pass.
    pub fn upload_deferred_groups(&self, groups: &[u32]) -> Result<(), String> {
        if groups.len() != self.cells {
            return Err("thermal group length mismatch".into());
        }
        if let Some(data)=&self.deferred_expansion {data.valid.set(false);}
        self.queue.write_buffer(&self.deferred_group, 0, bytemuck::cast_slice(groups));
        Ok(())
    }

    pub fn upload_deferred_group_range(&self, start: usize, groups: &[u32]) -> Result<(), String> {
        let end = start.checked_add(groups.len()).ok_or("thermal group range overflow")?;
        if end > self.cells { return Err("thermal group range exceeds grid".into()); }
        if !groups.is_empty() {
            if let Some(data)=&self.deferred_expansion {data.valid.set(false);}
            self.queue.write_buffer(&self.deferred_group, (start * 4) as u64, bytemuck::cast_slice(groups));
        }
        Ok(())
    }

    pub fn upload(
        &self,
        temperature: &[f32],
        mass: &[f32],
        conductivity: &[f32],
        specific_heat: &[f32],
    ) -> Result<(), String> {
        if temperature.len() != self.cells
            || mass.len() != self.cells
            || conductivity.len() != self.cells
            || specific_heat.len() != self.cells
        {
            return Err("input length mismatch".into());
        }
        if let Some(data)=&self.material_expansion {data.valid.set(false);}
        self.previous_mass.borrow_mut().clear();
        self.queue
            .write_buffer(self.temperature_buffer(), 0, bytemuck::cast_slice(temperature));
        self.queue
            .write_buffer(&self.mass, 0, bytemuck::cast_slice(mass));
        self.queue
            .write_buffer(&self.conductivity, 0, bytemuck::cast_slice(conductivity));
        self.queue
            .write_buffer(&self.specific_heat, 0, bytemuck::cast_slice(specific_heat));
        Ok(())
    }

    /// Update changing cell state without retransferring unchanged material properties.
    pub fn upload_state(&self, temperature: &[f32], mass: &[f32]) -> Result<(), String> {
        if temperature.len() != self.cells || mass.len() != self.cells {
            return Err("input length mismatch".into());
        }
        self.queue.write_buffer(self.temperature_buffer(), 0, bytemuck::cast_slice(temperature));
        let mut dirty=vec![false;self.cells.div_ceil(4096)];
        let bytes=upload_changed_bytes(&self.queue,&self.mass,bytemuck::cast_slice(mass),&mut self.previous_mass.borrow_mut(),16384,&mut dirty);
        self.mass_upload_bytes.set(bytes);
        Ok(())
    }

    pub fn last_mass_upload_bytes(&self) -> usize { self.mass_upload_bytes.get() }

    /// Replace a contiguous range of material properties in device-local storage.
    pub fn upload_material_range(&self, start: usize, conductivity: &[f32], specific_heat: &[f32]) -> Result<(), String> {
        if conductivity.len() != specific_heat.len()
            || start.checked_add(conductivity.len()).map_or(true, |end| end > self.cells) {
            return Err("material range mismatch".into());
        }
        if !conductivity.is_empty() {
            if let Some(data)=&self.material_expansion {data.valid.set(false);}
            let offset = (start * std::mem::size_of::<f32>()) as u64;
            self.queue.write_buffer(&self.conductivity, offset, bytemuck::cast_slice(conductivity));
            self.queue.write_buffer(&self.specific_heat, offset, bytemuck::cast_slice(specific_heat));
        }
        Ok(())
    }

    /// Upload conductivity when another GPU operator supplies heat capacity.
    pub fn upload_conductivity(&self, conductivity: &[f32]) -> Result<(), String> {
        if conductivity.len() != self.cells {
            return Err("conductivity length mismatch".into());
        }
        if !conductivity.is_empty() {
            if let Some(data)=&self.material_expansion {data.valid.set(false);}
            self.queue.write_buffer(&self.conductivity, 0, bytemuck::cast_slice(conductivity));
        }
        Ok(())
    }

    pub fn step(
        &mut self,
        dt: f32,
        max_transfer_fraction: f32,
        output_temp: &mut [f32],
    ) -> Result<(), String> {
        if output_temp.len() != self.cells {
            return Err("output slice mismatch".into());
        }
        let submission = self.submit_diffusion(dt, max_transfer_fraction, true)?;
        self.finish_readback(output_temp, submission)
    }

    /// Submit diffusion without waiting or copying the output to host memory.
    /// A subsequent operator on the same queue can consume `temperature_buffer`.
    pub fn dispatch_resident(&mut self, dt: f32, max_transfer_fraction: f32) -> Result<(), String> {
        self.submit_diffusion(dt, max_transfer_fraction, false)?;
        Ok(())
    }

    fn submit_diffusion(&mut self, dt: f32, max_transfer_fraction: f32, copy_for_readback: bool) -> Result<wgpu::SubmissionIndex, String> {
        let mut encoder = self.device.create_command_encoder(&wgpu::CommandEncoderDescriptor { label: Some("Thermal step encoder") });
        self.encode_resident(&mut encoder, dt, max_transfer_fraction)?;
        if copy_for_readback {
            encoder.copy_buffer_to_buffer(self.temperature_buffer(), 0, &self.readback, 0, (self.cells * 4) as u64);
        }
        Ok(self.queue.submit(Some(encoder.finish())))
    }

    pub fn encode_resident(&mut self, encoder:&mut wgpu::CommandEncoder, dt:f32, max_transfer_fraction:f32) -> Result<(), String> {
        let uniform = ThermalParams {
            width: self.width as u32,
            height: self.height as u32,
            dt,
            max_transfer_fraction,
        };
        self.queue
            .write_buffer(&self.params, 0, bytemuck::bytes_of(&uniform));

        if let Some(expansion)=self.deferred_expansion.as_mut().filter(|p|p.pending) {
            let mut pass=encoder.begin_compute_pass(&Default::default());
            pass.set_pipeline(&expansion.pipeline);pass.set_bind_group(0,&expansion.group,&[]);
            pass.dispatch_workgroups(expansion.pending_workgroups,1,1);expansion.pending=false;expansion.valid.set(true);
        }
        if let Some(expansion)=self.material_expansion.as_mut().filter(|p|p.pending) {
            let mut pass=encoder.begin_compute_pass(&Default::default());
            pass.set_pipeline(&expansion.pipeline); pass.set_bind_group(0,&expansion.group,&[]);
            pass.dispatch_workgroups(expansion.pending_workgroups,1,1);
            expansion.pending=false;expansion.valid.set(true);
        }
        let wg_x = (self.width as u32 + 7) / 8;
        let wg_y = (self.height as u32 + 7) / 8;

        {
            let mut pass = encoder.begin_compute_pass(&wgpu::ComputePassDescriptor {
                label: Some("Thermal gather flux pass"),
                timestamp_writes: None,
            });
            pass.set_pipeline(&self.gather_pipeline);
            pass.set_bind_group(0, &self.gather_groups[self.current], &[]);
            pass.dispatch_workgroups(wg_x, wg_y, 1);
        }

        self.current ^= 1;
        Ok(())
    }

    /// Read the most recently submitted result after any queued GPU consumers.
    pub fn readback_current(&mut self, output_temp: &mut [f32]) -> Result<(), String> {
        if output_temp.len() != self.cells {
            return Err("output slice mismatch".into());
        }
        let mut encoder = self.device.create_command_encoder(&wgpu::CommandEncoderDescriptor {
            label: Some("Thermal readback encoder"),
        });
        encoder.copy_buffer_to_buffer(self.temperature_buffer(), 0, &self.readback, 0, (self.cells * 4) as u64);
        let submission = self.queue.submit(Some(encoder.finish()));
        self.finish_readback(output_temp, submission)
    }

    fn finish_readback(&mut self, output_temp: &mut [f32], submission: wgpu::SubmissionIndex) -> Result<(), String> {
        let slice = self.readback.slice(..);
        let (sender, receiver) = mpsc::channel();
        slice.map_async(wgpu::MapMode::Read, move |result| {
            let _ = sender.send(result);
        });
        self.device
            .poll(wgpu::PollType::Wait {
                submission_index: Some(submission),
                timeout: Some(Duration::from_secs(5)),
            })
            .map_err(|e| e.to_string())?;
        receiver
            .recv_timeout(Duration::from_millis(5000))
            .map_err(|_| "thermal readback timeout".to_string())?
            .map_err(|e| format!("thermal map failed: {e:?}"))?;

        {
            let view = self
                .readback
                .get_mapped_range(..)
                .map_err(|e| e.to_string())?;
            output_temp.copy_from_slice(bytemuck::cast_slice(&view));
        }
        self.readback.unmap();
        Ok(())
    }

    /// Access the device temperature buffer directly for GPU-resident chaining
    pub fn temperature_buffer(&self) -> &wgpu::Buffer {
        if self.current == 0 { &self.temperature_in } else { &self.temperature_out }
    }

    pub fn upload_temperature(&self, temperature: &[f32]) -> Result<(), String> {
        if temperature.len() != self.cells { return Err("temperature length mismatch".into()); }
        self.queue.write_buffer(self.temperature_buffer(), 0, bytemuck::cast_slice(temperature));
        Ok(())
    }

    pub fn conductivity_buffer(&self) -> &wgpu::Buffer {
        if let Some(data)=&self.material_expansion {data.valid.set(false);}
        &self.conductivity
    }

    /// Device-local inputs for constructing a thermal state from another operator.
    pub fn state_buffers(&self) -> (&wgpu::Buffer, &wgpu::Buffer, &wgpu::Buffer) {
        if let Some(data)=&self.material_expansion {data.valid.set(false);}
        self.previous_mass.borrow_mut().clear();
        (self.temperature_buffer(), &self.mass, &self.specific_heat)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn gathered_faces_match_two_pass_gpu_with_deferred_groups() {
        for (width, height) in [(2, 2), (31, 19), (128, 73)] {
            let mut gpu = pollster::block_on(ThermalDiffusionGpu::new(width, height)).unwrap();
            let n = width * height;
            let temp: Vec<_> = (0..n).map(|i| 250.0 + (i % 97) as f32 * 3.0).collect();
            let mass: Vec<_> = (0..n).map(|i| if i % 17 == 0 { 0.0 } else { 0.5 + (i % 11) as f32 }).collect();
            let cond: Vec<_> = (0..n).map(|i| 0.1 + (i % 7) as f32).collect();
            let heat: Vec<_> = (0..n).map(|i| 0.5 + (i % 5) as f32).collect();
            let groups: Vec<_> = (0..n).map(|i| if i % 13 < 6 { 1 } else { 0 }).collect();
            gpu.upload(&temp, &mass, &cond, &heat).unwrap();
            gpu.upload_deferred_groups(&groups).unwrap();
            gpu.queue.write_buffer(&gpu.params, 0, bytemuck::bytes_of(&ThermalParams {
                width: width as u32, height: height as u32, dt: 0.2, max_transfer_fraction: 0.25,
            }));
            let mut encoder = gpu.device.create_command_encoder(&Default::default());
            for (pipeline, group) in [(&gpu.flux_pipeline, &gpu.groups[0][0]), (&gpu.apply_pipeline, &gpu.groups[0][1])] {
                let mut pass = encoder.begin_compute_pass(&Default::default());
                pass.set_pipeline(pipeline);
                pass.set_bind_group(0, group, &[]);
                pass.dispatch_workgroups((width as u32).div_ceil(8), (height as u32).div_ceil(8), 1);
            }
            encoder.copy_buffer_to_buffer(&gpu.temperature_out, 0, &gpu.readback, 0, (n * 4) as u64);
            let submission = gpu.queue.submit(Some(encoder.finish()));
            let mut expected = vec![0.0; n];
            gpu.finish_readback(&mut expected, submission).unwrap();
            let mut actual = vec![0.0; n];
            gpu.step(0.2, 0.25, &mut actual).unwrap();
            for (index, (a, b)) in actual.iter().zip(&expected).enumerate() {
                assert!((a - b).abs() <= 0.0001, "cell {index}: {a} vs {b}");
            }
        }
    }

    #[test]
    #[ignore = "manual GPU dispatch benchmark"]
    fn benchmark_gather_against_two_pass() {
        let (width, height) = (738usize, 306usize);
        let gpu = pollster::block_on(ThermalDiffusionGpu::new(width, height)).unwrap();
        let n = width * height;
        let temp: Vec<_> = (0..n).map(|i| 250.0 + (i % 97) as f32).collect();
        gpu.upload(&temp, &vec![1.0; n], &vec![1.0; n], &vec![2.0; n]).unwrap();
        gpu.queue.write_buffer(&gpu.params, 0, bytemuck::bytes_of(&ThermalParams {
            width: width as u32, height: height as u32, dt: 0.2, max_transfer_fraction: 0.25,
        }));
        let run = |gather: bool, iterations: usize| {
            let started = std::time::Instant::now();
            let mut encoder = gpu.device.create_command_encoder(&Default::default());
            for _ in 0..iterations {
                if gather {
                    let mut pass = encoder.begin_compute_pass(&Default::default());
                    pass.set_pipeline(&gpu.gather_pipeline);
                    pass.set_bind_group(0, &gpu.gather_groups[0], &[]);
                    pass.dispatch_workgroups((width as u32).div_ceil(8), (height as u32).div_ceil(8), 1);
                } else {
                    for (pipeline, group) in [(&gpu.flux_pipeline, &gpu.groups[0][0]), (&gpu.apply_pipeline, &gpu.groups[0][1])] {
                        let mut pass = encoder.begin_compute_pass(&Default::default());
                        pass.set_pipeline(pipeline);
                        pass.set_bind_group(0, group, &[]);
                        pass.dispatch_workgroups((width as u32).div_ceil(8), (height as u32).div_ceil(8), 1);
                    }
                }
            }
            let submission = gpu.queue.submit(Some(encoder.finish()));
            gpu.device.poll(wgpu::PollType::Wait { submission_index: Some(submission), timeout: Some(Duration::from_secs(30)) }).unwrap();
            started.elapsed().as_secs_f64() * 1000.0 / iterations as f64
        };
        run(false, 10); run(true, 10);
        for gather in [false, true, true, false] {
            eprintln!("thermal {}: {:.4} ms/step ({:?})", if gather { "gather" } else { "two-pass" }, run(gather, 100), gpu.adapter.name);
        }
    }

    #[test]
    fn mass_upload_tracks_changed_blocks_and_external_overwrites() {
        let count=4101;let mut gpu=pollster::block_on(ThermalDiffusionGpu::new(3,1367)).unwrap();
        let mut reference=pollster::block_on(ThermalDiffusionGpu::new(3,1367)).unwrap();
        let mut mass=vec![1.0f32;count];let mut temp=vec![300.0f32;count];temp[count-1]=500.0;
        let cond=vec![1.0f32;count];let heat=vec![2.0f32;count];
        gpu.upload_material_range(0,&cond,&heat).unwrap();
        for iteration in 0..4 {
            if iteration==2 {mass[count-1]=2.0;}
            if iteration==3 {gpu.upload(&temp,&vec![3.0;count],&cond,&heat).unwrap();}
            gpu.upload_state(&temp,&mass).unwrap();
            assert_eq!(gpu.last_mass_upload_bytes(),match iteration {0|3=>count*4,1=>0,_=>20});
            reference.upload(&temp,&mass,&cond,&heat).unwrap();
            let mut actual=vec![0.0;count];let mut expected=vec![0.0;count];
            gpu.step(0.2,0.25,&mut actual).unwrap();reference.step(0.2,0.25,&mut expected).unwrap();
            assert_eq!(actual,expected);
        }
    }

    #[test]
    fn gpu_material_lookup_matches_cpu_for_odd_grid_and_table_changes() {
        let mut gpu=pollster::block_on(ThermalDiffusionGpu::new(3,3)).unwrap();
        let mut reference=pollster::block_on(ThermalDiffusionGpu::new(3,3)).unwrap();
        let mut ids=[0u16,1,0,1,9,0,1,0,1];
        let temperature=[300.0,350.0,400.0,330.0,500.0,290.0,310.0,320.0,450.0];
        let mass=[1.0f32;9];
        for iteration in 0..3 {
            let values=[[1.0f32,2.0f32],[2.0+iteration as f32,4.0]];
            let bits=values.map(|v|[v[0].to_bits(),v[1].to_bits()]);
            if iteration==2 {ids[8]=0;}
            let cond:Vec<_>=ids.iter().map(|&id|values.get(id as usize).map_or(0.0,|v|v[0])).collect();
            let heat:Vec<_>=ids.iter().map(|&id|values.get(id as usize).map_or(1.0,|v|v[1])).collect();
            gpu.upload_state(&temperature,&mass).unwrap();
            gpu.prepare_materials_by_element(&ids,&bits).unwrap();
            reference.upload(&temperature,&mass,&cond,&heat).unwrap();
            let mut actual=[0.0;9]; let mut expected=[0.0;9];
            gpu.step(0.2,0.25,&mut actual).unwrap();
            reference.step(0.2,0.25,&mut expected).unwrap();
            assert_eq!(actual,expected);
        }
    }

    #[test]
    fn gpu_material_lookup_updates_last_partial_block() {
        let count=4101;
        let mut gpu=pollster::block_on(ThermalDiffusionGpu::new(3,1367)).unwrap();
        let mut reference=pollster::block_on(ThermalDiffusionGpu::new(3,1367)).unwrap();
        let mut ids=vec![0u16;count];
        let values=[[1.0f32,2.0f32],[4.0,5.0]];
        let bits=values.map(|v|[v[0].to_bits(),v[1].to_bits()]);
        let mass=vec![1.0;count]; let mut temperature=vec![300.0;count]; temperature[count-1]=500.0;
        for iteration in 0..2 {
            if iteration==1 {ids[count-1]=1;}
            let cond:Vec<_>=ids.iter().map(|&id|values[id as usize][0]).collect();
            let heat:Vec<_>=ids.iter().map(|&id|values[id as usize][1]).collect();
            gpu.upload_state(&temperature,&mass).unwrap(); gpu.prepare_materials_by_element(&ids,&bits).unwrap();
            assert_eq!(gpu.material_expansion.as_ref().unwrap().pending_workgroups,if iteration==0 {65} else {1});
            reference.upload(&temperature,&mass,&cond,&heat).unwrap();
            let mut actual=vec![0.0;count]; let mut expected=vec![0.0;count];
            gpu.step(0.2,0.25,&mut actual).unwrap(); reference.step(0.2,0.25,&mut expected).unwrap();
            assert_eq!(actual,expected);
        }
    }

    #[test]
    fn gpu_deferred_groups_match_cpu_with_packed_tails_and_changing_inputs() {
        let mut gpu=pollster::block_on(ThermalDiffusionGpu::new(3,3)).unwrap();
        let mut reference=pollster::block_on(ThermalDiffusionGpu::new(3,3)).unwrap();
        let ids=[0u16,1,0,1,2,0,1,0,1];
        let mut mixture=[0u16,1,2,1,0,1,0,1,9];
        let mut mixmass=[0.0f32,0.5,0.5,0.0,0.0,0.5,0.0,f32::NAN,1.0];
        let mut properties=[0u8,0,0,1,0,0,0,0,0];
        let region=[1u32,1,1,1,1,2,2,2,2];
        let flags=[3u32,0,2];
        let temp=[300.0,350.0,400.0,330.0,500.0,290.0,310.0,320.0,450.0];
        let mass=[1.0f32;9]; let table=[[1.0f32.to_bits(),2.0f32.to_bits()];3];
        for iteration in 0..3 {
            if iteration==2 {mixture[8]=0;mixmass[8]=0.25;properties[3]=0;}
            let groups:Vec<u32>=(0..9).map(|i| {
                let id=ids[i] as usize; let mix=mixture[i] as usize;
                let eligible=region[i]!=0 && properties[i]&1==0 && flags.get(id).is_some_and(|x|x&1!=0)
                    && (mixmass[i]<=0.0 || flags.get(mix).is_some_and(|x|x&2!=0));
                if eligible {region[i]} else {0}
            }).collect();
            gpu.upload_state(&temp,&mass).unwrap();gpu.prepare_materials_by_element(&ids,&table).unwrap();
            gpu.prepare_deferred_groups_on_gpu(&mixture,&mixmass,&properties,&region,&flags).unwrap();
            assert_eq!(gpu.material_expansion.as_ref().unwrap().pending,iteration==0);
            assert_eq!(gpu.deferred_expansion.as_ref().unwrap().pending,iteration!=1);
            reference.upload(&temp,&mass,&[1.0;9],&[2.0;9]).unwrap();reference.upload_deferred_groups(&groups).unwrap();
            let mut actual=[0.0;9];let mut expected=[0.0;9];
            gpu.step(0.2,0.25,&mut actual).unwrap();reference.step(0.2,0.25,&mut expected).unwrap();
            assert_eq!(actual,expected);
        }
        gpu.upload_material_range(0,&[9.0],&[9.0]).unwrap();
        gpu.upload_deferred_group_range(0,&[0]).unwrap();
        gpu.prepare_materials_by_element(&ids,&table).unwrap();
        gpu.prepare_deferred_groups_on_gpu(&mixture,&mixmass,&properties,&region,&flags).unwrap();
        assert!(gpu.material_expansion.as_ref().unwrap().pending);
        assert!(gpu.deferred_expansion.as_ref().unwrap().pending);
    }

    #[test]
    fn deferred_groups_recompute_only_changed_blocks_and_handle_skipped_revisions() {
        let count=4101;
        let mut gpu=pollster::block_on(ThermalDiffusionGpu::new(3,1367)).unwrap();
        let mut reference=pollster::block_on(ThermalDiffusionGpu::new(3,1367)).unwrap();
        let mut ids=vec![0u16;count];let mixture=vec![0u16;count];
        let mut mixture_mass=vec![0.0f32;count];let properties=vec![0u8;count];
        let region=vec![1u32;count];let flags=[3u32,0u32];
        let materials=[[1.0f32.to_bits(),2.0f32.to_bits()];2];
        let mut temp=vec![300.0f32;count];temp[count-1]=500.0;
        let mass=vec![1.0f32;count];let cond=vec![1.0f32;count];let heat=vec![2.0f32;count];
        for iteration in 0..4 {
            if iteration==1 {mixture_mass[count-1]=1.0;}
            if iteration==2 {ids[count-1]=1;}
            if iteration==3 {
                ids[0]=1;gpu.prepare_materials_by_element(&ids,&materials).unwrap();ids[4096]=1;
            }
            gpu.upload_state(&temp,&mass).unwrap();gpu.prepare_materials_by_element(&ids,&materials).unwrap();
            gpu.prepare_deferred_groups_on_gpu(&mixture,&mixture_mass,&properties,&region,&flags).unwrap();
            assert_eq!(gpu.deferred_expansion.as_ref().unwrap().pending_workgroups,if iteration==1 || iteration==2 {1} else {65});
            let groups:Vec<_>=ids.iter().map(|&id|if id==0 {1u32} else {0}).collect();
            reference.upload(&temp,&mass,&cond,&heat).unwrap();reference.upload_deferred_groups(&groups).unwrap();
            let mut actual=vec![0.0;count];let mut expected=vec![0.0;count];
            gpu.step(0.2,0.25,&mut actual).unwrap();reference.step(0.2,0.25,&mut expected).unwrap();
            assert_eq!(actual,expected);
        }
    }

    #[test]
    #[ignore = "GPU timing probe; run explicitly on an idle device"]
    fn benchmark_material_upload_at_game_grid_size() {
        let (width, height) = (738, 306);
        let count = width * height;
        let temperature = vec![300.0; count];
        let mass = vec![1.0; count];
        let conductivity = vec![1.0; count];
        let heat = vec![1.0; count];
        let mut output = vec![0.0; count];
        let mut gpu = pollster::block_on(ThermalDiffusionGpu::new(width, height)).unwrap();
        gpu.upload(&temperature, &mass, &conductivity, &heat).unwrap();
        let mut samples = [Vec::new(), Vec::new()];
        for pair in 0..26 {
            for mode in [pair % 2, 1 - pair % 2] {
                let start = std::time::Instant::now();
                gpu.upload_state(&temperature, &mass).unwrap();
                let end = if mode == 0 { count } else { 4096 };
                gpu.upload_material_range(0, &conductivity[..end], &heat[..end]).unwrap();
                gpu.step(0.2, 0.25, &mut output).unwrap();
                if pair >= 6 { samples[mode].push(start.elapsed().as_secs_f64() * 1000.0); }
            }
        }
        for (mode, values) in samples.iter_mut().enumerate() {
            values.sort_by(f64::total_cmp);
            eprintln!("{}: median {:.3} ms p95 {:.3} ms (20 samples)",
                if mode == 0 { "full" } else { "4096 cells" }, values[10], values[18]);
        }
    }

    #[test]
    fn thermal_diffusion_wgsl_validates() {
        let module = naga::front::wgsl::parse_str(SHADER).expect("thermal WGSL must parse");
        let mut validator = naga::valid::Validator::new(
            naga::valid::ValidationFlags::all(),
            naga::valid::Capabilities::all(),
        );
        validator
            .validate(&module)
            .expect("thermal WGSL must validate");
    }

    #[test]
    fn deferred_groups_skip_only_faces_with_the_same_nonzero_group() {
        let mut gpu = pollster::block_on(ThermalDiffusionGpu::new(3, 2)).unwrap();
        let input = [500.0, 300.0, 300.0, 500.0, 300.0, 300.0];
        let mass = [1.0; 6];
        gpu.upload(&input, &mass, &[1.0; 6], &[1.0; 6]).unwrap();
        gpu.upload_deferred_groups(&[1, 1, 2, 1, 1, 2]).unwrap();
        let mut result = [0.0; 6];
        gpu.step(0.2, 0.25, &mut result).unwrap();
        assert_eq!(result, input);
        gpu.upload_state(&input, &mass).unwrap();
        gpu.upload_deferred_group_range(1, &[2]).unwrap();
        gpu.upload_deferred_group_range(4, &[2]).unwrap();
        assert!(gpu.upload_deferred_group_range(6, &[1]).is_err());
        gpu.step(0.2, 0.25, &mut result).unwrap();
        assert!(result[0] < input[0] && result[1] > input[1]);
        assert!((result.iter().sum::<f32>() - input.iter().sum::<f32>()).abs() < 1e-3);
    }

    #[test]
    fn thermal_diffusion_end_to_end_conserves_energy() {
        let width = 16;
        let height = 16;
        let count = width * height;
        let mut temp = vec![300.0f32; count];
        let mass = vec![1000.0f32; count];
        let cond = vec![1.0f32; count];
        let shc = vec![2.0f32; count];

        // Hotspot in the middle
        temp[8 * width + 8] = 1000.0;

        let initial_energy: f64 = temp
            .iter()
            .zip(&mass)
            .zip(&shc)
            .map(|((&t, &m), &c)| (t as f64) * (m as f64) * (c as f64))
            .sum();

        let mut gpu = pollster::block_on(ThermalDiffusionGpu::new(width, height))
            .expect("GPU creation failed");
        gpu.upload(&temp, &mass, &cond, &shc)
            .expect("upload failed");

        let mut result = vec![0.0f32; count];
        gpu.step(0.2, 0.25, &mut result).expect("step failed");

        let mut repeated = vec![0.0f32; count];
        gpu.dispatch_resident(0.2, 0.25).expect("resident dispatch failed");
        gpu.readback_current(&mut repeated).expect("resident readback failed");
        let mut sequential = vec![0.0; count];
        gpu.upload_state(&result, &mass).unwrap();
        gpu.step(0.2, 0.25, &mut sequential).unwrap();
        assert_eq!(sequential, repeated, "two resident steps differ from two synchronized steps");
        assert!(repeated[8 * width + 8] < result[8 * width + 8], "second resident step must advance the state");
        gpu.upload_state(&temp, &mass).unwrap();
        gpu.dispatch_resident(0.2, 0.25).unwrap();
        gpu.dispatch_resident(0.2, 0.25).unwrap();
        gpu.readback_current(&mut repeated).unwrap();
        assert_eq!(sequential, repeated, "chained steps must not require an intermediate readback");
        gpu.upload_state(&temp, &mass).expect("state upload failed");
        gpu.step(0.2, 0.25, &mut repeated).expect("second step failed");
        assert_eq!(result, repeated, "retained material properties changed the result");

        let mut changed_cond = cond.clone();
        let changed = 8 * width + 8;
        changed_cond[changed] = 0.0;
        gpu.upload_material_range(changed, &changed_cond[changed..changed + 1], &shc[changed..changed + 1]).unwrap();
        gpu.upload_state(&temp, &mass).unwrap();
        gpu.step(0.2, 0.25, &mut repeated).unwrap();
        let mut reference = vec![0.0; count];
        gpu.upload(&temp, &mass, &changed_cond, &shc).unwrap();
        gpu.step(0.2, 0.25, &mut reference).unwrap();
        assert_eq!(repeated, reference, "partial material upload differs from full upload");
        assert!(gpu.upload_material_range(count, &[1.0], &[1.0]).is_err());

        // Center should cool down
        assert!(result[8 * width + 8] < 1000.0);
        // Neighbors should warm up
        assert!(result[8 * width + 7] > 300.0);
        assert!(result[8 * width + 9] > 300.0);

        let final_energy: f64 = result
            .iter()
            .zip(&mass)
            .zip(&shc)
            .map(|((&t, &m), &c)| (t as f64) * (m as f64) * (c as f64))
            .sum();

        let relative_error = (final_energy - initial_energy).abs() / initial_energy;
        assert!(
            relative_error < 1e-4,
            "Thermal energy must be strictly conserved! Relative error: {relative_error}"
        );
    }
}
