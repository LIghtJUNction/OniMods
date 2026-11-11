//! Device-local temperature to species sensible enthalpy conversion.
//! Caller must supply a thermally equilibrated cell temperature and matching
//! species masses/capacities; this does not implement phase transitions.
use crate::gpu_context::GpuContext;
use std::{cell::Cell, sync::Arc};

const SHADER: &str = r#"
struct Params { cells:u32, channels:u32, pad0:u32, pad1:u32 };
@group(0) @binding(0) var<storage,read> temperature:array<f32>;
@group(0) @binding(1) var<storage,read> mass:array<f32>;
@group(0) @binding(2) var<storage,read> heat:array<f32>;
@group(0) @binding(3) var<storage,read_write> enthalpy:array<f32>;
@group(0) @binding(4) var<uniform> params:Params;
@compute @workgroup_size(64)
fn main(@builtin(global_invocation_id) id:vec3<u32>) {
 let i=id.x;
 if i >= params.cells * params.channels { return; }
 enthalpy[i] = mass[i] * heat[i % params.channels] * temperature[i / params.channels];
}
"#;

const PREPARE_SHADER: &str = r#"
struct Params { cells:u32, channels:u32, pad0:u32, pad1:u32 };
@group(0) @binding(0) var<storage,read> species_mass:array<f32>;
@group(0) @binding(1) var<storage,read> species_enthalpy:array<f32>;
@group(0) @binding(2) var<storage,read> species_heat:array<f32>;
@group(0) @binding(3) var<storage,read_write> temperature:array<f32>;
@group(0) @binding(4) var<storage,read_write> mass:array<f32>;
@group(0) @binding(5) var<storage,read_write> heat:array<f32>;
@group(0) @binding(6) var<uniform> params:Params;
@compute @workgroup_size(64)
fn main(@builtin(global_invocation_id) id:vec3<u32>) {
 let cell=id.x;
 if cell >= params.cells { return; }
 var m=0.0; var capacity=0.0; var energy=0.0;
 for (var c=0u; c<params.channels; c++) {
   let i=cell*params.channels+c;
   m += species_mass[i];
   capacity += species_mass[i]*species_heat[c];
   energy += species_enthalpy[i];
 }
 mass[cell]=m;
 if m>0.0 && capacity>0.0 {
   heat[cell]=capacity/m; temperature[cell]=energy/capacity;
 } else {
   heat[cell]=1.0; temperature[cell]=0.0;
 }
}
"#;

const CONDUCTIVITY_SHADER: &str = r#"
struct Params { cells:u32, channels:u32, pad0:u32, pad1:u32 };
@group(0) @binding(0) var<storage,read> mass:array<f32>;
@group(0) @binding(1) var<storage,read> species_k:array<f32>;
@group(0) @binding(2) var<storage,read_write> conductivity:array<f32>;
@group(0) @binding(3) var<uniform> params:Params;
@compute @workgroup_size(64)
fn main(@builtin(global_invocation_id) id:vec3<u32>) {
 let cell=id.x; if cell>=params.cells {return;}
 var total=0.0; var weighted=0.0;
 for(var c=0u;c<params.channels;c++) {
   let m=mass[cell*params.channels+c]; total+=m; weighted+=m*species_k[c];
 }
 var result=0.0; if total>0.0 {result=weighted/total;}
 conductivity[cell]=result;
}
"#;

struct CachedGroup { buffers: Vec<wgpu::Buffer>, group: wgpu::BindGroup }

fn cached_group<'a>(cache: &'a mut Vec<CachedGroup>, device: &wgpu::Device,
                    pipeline: &wgpu::ComputePipeline, buffers: &[&wgpu::Buffer]) -> &'a wgpu::BindGroup {
    let index = if let Some(index) = cache.iter().position(|entry|
        entry.buffers.len() == buffers.len() && entry.buffers.iter().zip(buffers).all(|(a,b)| a == *b)) {
        index
    } else {
        let entries: Vec<_> = buffers.iter().enumerate().map(|(i,b)| wgpu::BindGroupEntry {
            binding:i as u32, resource:b.as_entire_binding() }).collect();
        let group=device.create_bind_group(&wgpu::BindGroupDescriptor { label:Some("thermal mixture cached bindings"),
            layout:&pipeline.get_bind_group_layout(0), entries:&entries });
        if cache.len() >= 2 { cache.remove(0); }
        cache.push(CachedGroup { buffers:buffers.iter().map(|b| (*b).clone()).collect(), group });
        cache.len()-1
    };
    &cache[index].group
}

pub struct ThermalEnthalpyGpu { context: Arc<GpuContext>, pipeline: wgpu::ComputePipeline, prepare_pipeline: wgpu::ComputePipeline, conductivity_pipeline: wgpu::ComputePipeline, params: wgpu::Buffer, params_value: Cell<Option<[u32; 4]>>, conductivity_species: Option<wgpu::Buffer>, conductivity_bits: Vec<u32>, conductivity_groups: Vec<CachedGroup>, prepare_groups: Vec<CachedGroup>, enthalpy_groups: Vec<CachedGroup> }
impl ThermalEnthalpyGpu {
    pub fn new(context: Arc<GpuContext>) -> Self {
        let shader = context.device.create_shader_module(wgpu::ShaderModuleDescriptor {
            label: Some("thermal enthalpy"), source: wgpu::ShaderSource::Wgsl(SHADER.into()) });
        let pipeline = context.device.create_compute_pipeline(&wgpu::ComputePipelineDescriptor {
            label: Some("thermal enthalpy"), layout: None, module: &shader, entry_point: Some("main"),
            compilation_options: Default::default(), cache: None });
        let prepare_shader = context.device.create_shader_module(wgpu::ShaderModuleDescriptor {
            label: Some("thermal mixture prepare"), source: wgpu::ShaderSource::Wgsl(PREPARE_SHADER.into()) });
        let prepare_pipeline = context.device.create_compute_pipeline(&wgpu::ComputePipelineDescriptor {
            label: Some("thermal mixture prepare"), layout: None, module: &prepare_shader, entry_point: Some("main"),
            compilation_options: Default::default(), cache: None });
        let shader = context.device.create_shader_module(wgpu::ShaderModuleDescriptor {
            label: Some("mixture conductivity"), source: wgpu::ShaderSource::Wgsl(CONDUCTIVITY_SHADER.into()) });
        let conductivity_pipeline = context.device.create_compute_pipeline(&wgpu::ComputePipelineDescriptor {
            label: Some("mixture conductivity"), layout: None, module: &shader, entry_point: Some("main"),
            compilation_options: Default::default(), cache: None });
        let params = context.device.create_buffer(&wgpu::BufferDescriptor {
            label: Some("thermal mixture parameters"), size: 16,
            usage: wgpu::BufferUsages::UNIFORM | wgpu::BufferUsages::COPY_DST, mapped_at_creation: false });
        Self { context, pipeline, prepare_pipeline, conductivity_pipeline, params, params_value: Cell::new(None), conductivity_species: None, conductivity_bits: Vec::new(), conductivity_groups:Vec::new(), prepare_groups:Vec::new(), enthalpy_groups:Vec::new() }
    }
    fn prepare_params(&self, cells: usize, channels: usize) {
        let value = [cells as u32, channels as u32, 0, 0];
        if self.params_value.get() != Some(value) {
            self.context.queue.write_buffer(&self.params, 0, bytemuck::cast_slice(&value));
            self.params_value.set(Some(value));
        }
    }

    pub fn conductivity(&mut self, cells: usize, channels: usize, mass: &wgpu::Buffer,
                        species: &[f32], output: &wgpu::Buffer) -> Result<(), String> {
        let mut encoder=self.context.device.create_command_encoder(&Default::default());
        self.encode_conductivity(&mut encoder,cells,channels,mass,species,output)?;
        self.context.queue.submit([encoder.finish()]);
        Ok(())
    }

    pub fn encode_conductivity(&mut self, encoder:&mut wgpu::CommandEncoder, cells: usize, channels: usize, mass: &wgpu::Buffer,
                        species: &[f32], output: &wgpu::Buffer) -> Result<(), String> {
        let values = cells.checked_mul(channels).ok_or("tensor overflow")?;
        if cells == 0 || channels == 0 || values > u32::MAX as usize || species.len() != channels
            || mass.size() < values as u64 * 4 || output.size() < cells as u64 * 4
            || cells.div_ceil(64) > self.context.device.limits().max_compute_workgroups_per_dimension as usize {
            return Err("invalid mixture conductivity dimensions or buffers".into());
        }
        let device = &self.context.device;
        if self.conductivity_species.as_ref().map_or(true, |b| b.size() < channels as u64 * 4) {
            self.conductivity_species = Some(device.create_buffer(&wgpu::BufferDescriptor {
                label: Some("species conductivity"), size: channels as u64 * 4,
                usage: wgpu::BufferUsages::STORAGE | wgpu::BufferUsages::COPY_DST, mapped_at_creation: false }));
            self.conductivity_bits.clear();
        }
        let species_buffer = self.conductivity_species.as_ref().ok_or("missing conductivity buffer")?;
        if self.conductivity_bits.len() != species.len() || self.conductivity_bits.iter().zip(species)
            .any(|(&bits, value)| bits != value.to_bits()) {
            self.conductivity_bits = species.iter().map(|value| value.to_bits()).collect();
            self.context.queue.write_buffer(species_buffer, 0, bytemuck::cast_slice(&self.conductivity_bits));
        }
        self.prepare_params(cells, channels);
        let buffers = [mass, species_buffer, output, &self.params];
        let group=cached_group(&mut self.conductivity_groups,device,&self.conductivity_pipeline,&buffers);
        { let mut pass=encoder.begin_compute_pass(&Default::default()); pass.set_pipeline(&self.conductivity_pipeline);
          pass.set_bind_group(0,group,&[]); pass.dispatch_workgroups(cells.div_ceil(64) as u32,1,1); }
        Ok(())
    }
    /// Populate thermal cell inputs on the same queue from current species state.
    pub fn prepare(&mut self, cells: usize, channels: usize, species_mass: &wgpu::Buffer,
                   species_enthalpy: &wgpu::Buffer, species_heat: &wgpu::Buffer,
                   temperature: &wgpu::Buffer, mass: &wgpu::Buffer, heat: &wgpu::Buffer) -> Result<(), String> {
        let mut encoder=self.context.device.create_command_encoder(&Default::default());
        self.encode_prepare(&mut encoder,cells,channels,species_mass,species_enthalpy,species_heat,temperature,mass,heat)?;
        self.context.queue.submit([encoder.finish()]);
        Ok(())
    }

    pub fn encode_prepare(&mut self, encoder:&mut wgpu::CommandEncoder, cells: usize, channels: usize,
                   species_mass: &wgpu::Buffer, species_enthalpy: &wgpu::Buffer, species_heat: &wgpu::Buffer,
                   temperature: &wgpu::Buffer, mass: &wgpu::Buffer, heat: &wgpu::Buffer) -> Result<(), String> {
        let values = cells.checked_mul(channels).ok_or("tensor overflow")?;
        if cells == 0 || channels == 0 || values > u32::MAX as usize ||
            species_mass.size() < values as u64 * 4 || species_enthalpy.size() < values as u64 * 4 ||
            species_heat.size() < channels as u64 * 4 || temperature.size() < cells as u64 * 4 ||
            mass.size() < cells as u64 * 4 || heat.size() < cells as u64 * 4 ||
            cells.div_ceil(64) > self.context.device.limits().max_compute_workgroups_per_dimension as usize {
            return Err("invalid thermal mixture dimensions or buffers".into());
        }
        self.prepare_params(cells, channels);
        let buffers=[species_mass,species_enthalpy,species_heat,temperature,mass,heat,&self.params];
        let group=cached_group(&mut self.prepare_groups,&self.context.device,&self.prepare_pipeline,&buffers);
        { let mut pass=encoder.begin_compute_pass(&Default::default());
          pass.set_pipeline(&self.prepare_pipeline);pass.set_bind_group(0,group,&[]);
          pass.dispatch_workgroups(cells.div_ceil(64) as u32,1,1); }
        Ok(())
    }
    pub fn dispatch(&mut self, cells: usize, channels: usize, temperature: &wgpu::Buffer,
                    mass: &wgpu::Buffer, heat: &wgpu::Buffer, enthalpy: &wgpu::Buffer) -> Result<(), String> {
        let mut encoder = self.context.device.create_command_encoder(&Default::default());
        self.encode(&mut encoder,cells,channels,temperature,mass,heat,enthalpy)?;
        self.context.queue.submit([encoder.finish()]);
        Ok(())
    }

    pub fn encode(&mut self, encoder:&mut wgpu::CommandEncoder, cells: usize, channels: usize, temperature: &wgpu::Buffer,
                  mass: &wgpu::Buffer, heat: &wgpu::Buffer, enthalpy: &wgpu::Buffer) -> Result<(), String> {
        let values = cells.checked_mul(channels).ok_or("tensor overflow")?;
        if cells == 0 || channels == 0 || values > u32::MAX as usize ||
           temperature.size() < (cells as u64)*4 || mass.size() < (values as u64)*4 ||
           enthalpy.size() < (values as u64)*4 || heat.size() < (channels as u64)*4 ||
           values.div_ceil(64) > self.context.device.limits().max_compute_workgroups_per_dimension as usize {
            return Err("invalid thermal enthalpy dimensions or buffers".into());
        }
        self.prepare_params(cells, channels);
        let resources = [temperature, mass, heat, enthalpy, &self.params];
        let group=cached_group(&mut self.enthalpy_groups,&self.context.device,&self.pipeline,&resources);
        { let mut pass = encoder.begin_compute_pass(&Default::default());
          pass.set_pipeline(&self.pipeline); pass.set_bind_group(0, group, &[]);
          pass.dispatch_workgroups(values.div_ceil(64) as u32, 1, 1); }
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::{multicomponent::{cpu_step, MulticomponentGpu, SpeciesInfo, State}, pressure_projection::PressureProjectionGpu, thermal_diffusion::ThermalDiffusionGpu};
    #[test]
    fn conductivity_uses_species_mass_weights_and_handles_empty_cells() {
        use std::{sync::mpsc, time::Duration};
        let ctx = Arc::new(pollster::block_on(GpuContext::new()).unwrap());
        let mut operator = ThermalEnthalpyGpu::new(ctx.clone());
        let buffer = |size, usage| ctx.device.create_buffer(&wgpu::BufferDescriptor {
            label: None, size, usage, mapped_at_creation:false });
        let mass = buffer(24, wgpu::BufferUsages::STORAGE | wgpu::BufferUsages::COPY_DST);
        let output = buffer(12, wgpu::BufferUsages::STORAGE | wgpu::BufferUsages::COPY_SRC);
        let read = buffer(12, wgpu::BufferUsages::MAP_READ | wgpu::BufferUsages::COPY_DST);
        ctx.queue.write_buffer(&mass, 0, bytemuck::cast_slice(&[1.0f32, 3.0, 0.0, 0.0, 2.0, 0.0]));
        operator.conductivity(3, 2, &mass, &[2.0, 10.0], &output).unwrap();
        let mut encoder=ctx.device.create_command_encoder(&Default::default());
        encoder.copy_buffer_to_buffer(&output, 0, &read, 0, 12);
        let submission=ctx.queue.submit([encoder.finish()]);
        let (send, receive)=mpsc::sync_channel(1);
        read.map_async(wgpu::MapMode::Read, .., move |r| { let _=send.send(r); });
        ctx.device.poll(wgpu::PollType::Wait { submission_index:Some(submission), timeout:Some(Duration::from_secs(30)) }).unwrap();
        receive.recv_timeout(Duration::from_secs(2)).unwrap().unwrap();
        let view=read.get_mapped_range(..).unwrap();
        assert_eq!(bytemuck::cast_slice::<u8,f32>(&view), &[8.0, 0.0, 2.0]);
        drop(view);
        read.unmap();
        operator.conductivity(3, 2, &mass, &[2.0, 10.0], &output).unwrap();
        operator.conductivity(3, 2, &mass, &[8.0, 4.0], &output).unwrap();
        let mut encoder=ctx.device.create_command_encoder(&Default::default());
        encoder.copy_buffer_to_buffer(&output, 0, &read, 0, 12);
        let submission=ctx.queue.submit([encoder.finish()]);
        let (send, receive)=mpsc::sync_channel(1);
        read.map_async(wgpu::MapMode::Read, .., move |r| { let _=send.send(r); });
        ctx.device.poll(wgpu::PollType::Wait { submission_index:Some(submission), timeout:Some(Duration::from_secs(30)) }).unwrap();
        receive.recv_timeout(Duration::from_secs(2)).unwrap().unwrap();
        let view=read.get_mapped_range(..).unwrap();
        assert_eq!(bytemuck::cast_slice::<u8,f32>(&view), &[5.0, 0.0, 8.0]);
    }
    #[test]
    fn thermal_output_updates_species_enthalpy_without_intermediate_readback() {
        let ctx = Arc::new(pollster::block_on(GpuContext::new()).unwrap());
        let (w,h,channels) = (4,4,3);
        let n=w*h;
        let species = [SpeciesInfo { element_id:1, phase:1, specific_heat_capacity:1.0, vertical_drift:0.0 }, SpeciesInfo { element_id:2, phase:1, specific_heat_capacity:2.0, vertical_drift:0.0 }, SpeciesInfo { element_id:3, phase:1, specific_heat_capacity:4.0, vertical_drift:0.0 }];
        let mut mass: Vec<f32> = (0..n*channels).map(|i| 0.25 + ((i*7)%9) as f32 * 0.125).collect();
        mass[..channels].fill(0.0); // a vacuum cell has no mass or enthalpy
        let mut temp=vec![300.0;n]; temp[5]=600.0;
        let enthalpy: Vec<f32> = (0..n*channels).map(|i| mass[i]*temp[i/channels]*species[i%channels].specific_heat_capacity).collect();
        let initial_energy: f64 = enthalpy.iter().map(|&value| f64::from(value)).sum();
        let vx: Vec<f32> = (0..n).map(|i| (i as f32 % 3.0 - 1.0) * 0.04).collect();
        let vy: Vec<f32> = (0..n).map(|i| (i as f32 % 5.0 - 2.0) * 0.02).collect();
        let solid=vec![0u32;n];
        let state = State {mass:&mass, enthalpy:&enthalpy,velocity_x:&vx,velocity_y:&vy,solid:&solid,species:&species};
        let mut expected_mass = vec![0.0; n*channels];
        let mut expected_enthalpy = vec![0.0; n*channels];
        cpu_step(&state,w,h,channels,0.05,&mut expected_mass,&mut expected_enthalpy).unwrap();
        let mut mix=pollster::block_on(MulticomponentGpu::new_with_context(ctx.clone(),w,h,channels)).unwrap();
        mix.upload(&state).unwrap();
        mix.dispatch_resident(1, 0.05).unwrap();
        let mut pressure = pollster::block_on(PressureProjectionGpu::new_with_context(ctx.clone(),w,h)).unwrap();
        let (vx, vy) = mix.velocity_buffers();
        pressure.run_with_shared_state(mix.current_mass_buffer(), vx, vy, mix.solid_buffer(), channels, 1.0, 1.0, 1, 0.05, 1.0, 1.0, 4, 1.0).unwrap();
        assert!(mix.dispatch_resident(1, 0.05).is_err(), "transport must not reuse a stale velocity CFL bound");
        let mut thermal=pollster::block_on(ThermalDiffusionGpu::new_with_context(ctx.clone(),w,h)).unwrap();
        // Conductivity is independently supplied; thermal state comes from the species buffers.
        thermal.upload(&vec![0.0;n],&vec![0.0;n],&vec![1.0;n],&vec![1.0;n]).unwrap();
        let mut bridge=ThermalEnthalpyGpu::new(ctx.clone());
        for _ in 0..2 {
            let (temperature, total_mass, effective_heat) = thermal.state_buffers();
            bridge.prepare(n,channels,mix.current_mass_buffer(),mix.current_enthalpy_buffer(),mix.species_heat_buffer(),temperature,total_mass,effective_heat).unwrap();
            thermal.dispatch_resident(0.2,0.25).unwrap();
            bridge.dispatch(n,channels,thermal.temperature_buffer(),mix.current_mass_buffer(),mix.species_heat_buffer(),mix.current_enthalpy_buffer()).unwrap();
        }
        let mut m=vec![0.0;n*channels]; let mut e=vec![0.0;n*channels];
        mix.readback_state(&mut m,&mut e).unwrap();
        for (&actual,&expected) in m.iter().zip(&expected_mass) { assert!((actual-expected).abs()<1e-6); }
        for c in 0..channels {
            let before: f64=(0..n).map(|i| f64::from(mass[i*channels+c])).sum();
            let after: f64=(0..n).map(|i| f64::from(m[i*channels+c])).sum();
            assert!((after-before).abs()<1e-5);
        }
        let mut out=vec![0.0;n]; thermal.readback_current(&mut out).unwrap();
        for i in 0..n { for c in 0..channels { assert!((e[i*channels+c] - m[i*channels+c]*species[c].specific_heat_capacity*out[i]).abs()<0.001); } }
        assert!(out[5]<600.0 && out[4]>300.0);
        let final_energy: f64 = e.iter().map(|&value| f64::from(value)).sum();
        assert!((final_energy-initial_energy).abs()/initial_energy < 1e-6);
        let rate = mix.revalidate_velocity().unwrap();
        assert!(rate.is_finite() && rate >= 0.0);
        mix.dispatch_resident(1, 0.05).unwrap();
        let (vx, vy) = mix.velocity_buffers();
        ctx.queue.write_buffer(vx, 0, bytemuck::cast_slice(&vec![12.0f32;n]));
        ctx.queue.write_buffer(vy, 0, bytemuck::cast_slice(&vec![-9.0f32;n]));
        assert_eq!(mix.revalidate_velocity().unwrap(), 21.0);
        assert!(mix.dispatch_resident(1, 0.05).is_err(), "unsafe post-projection CFL must be rejected");
        let (vx, vy) = mix.velocity_buffers();
        ctx.queue.write_buffer(vx, 0, bytemuck::cast_slice(&vec![0.0f32;n]));
        ctx.queue.write_buffer(vy, 0, bytemuck::cast_slice(&vec![f32::NAN;n]));
        assert!(mix.revalidate_velocity().is_err(), "non-finite velocity must not validate");
    }
}
