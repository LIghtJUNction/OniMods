//! Experimental GPU simulation for the worker. Gas pressure, transport and
//! heat share device buffers; CPU game systems still require final readback.

use std::{collections::VecDeque, sync::Arc};
use onisim_gpu::{
    gpu_context::GpuContext,
    multicomponent::{MulticomponentGpu, State},
    pressure_projection::PressureProjectionGpu,
    thermal_diffusion::ThermalDiffusionGpu,
    thermal_enthalpy::ThermalEnthalpyGpu,
};
use crate::tables::ElementTable;
use crate::world::PhysicsWorld;

pub(crate) struct GpuSimulation {
    pub(crate) context: Arc<GpuContext>,
    pub(crate) thermal: ThermalDiffusionGpu,
    region_map: Vec<u32>,
    region_bounds: Vec<(i32,i32,i32,i32)>,
    region_map_initialized: bool,
    pub(crate) total_steps: u64,
    gas: VecDeque<GasPipeline>,
    liquid_diffusion: VecDeque<MulticomponentGpu>,
}

struct GasPipeline {
    transport: MulticomponentGpu,
    pressure: PressureProjectionGpu,
    thermal: ThermalDiffusionGpu,
    enthalpy: ThermalEnthalpyGpu,
}

impl GpuSimulation {
    fn liquid_pipeline(&mut self, width: usize, height: usize, channels: usize) -> Result<&mut MulticomponentGpu, String> {
        if let Some(index) = self.liquid_diffusion.iter().position(|p| p.dimensions() == (width, height, channels)) {
            let pipeline = self.liquid_diffusion.remove(index).ok_or("liquid cache entry disappeared")?;
            self.liquid_diffusion.push_front(pipeline);
        } else {
            const CACHE_BYTES: usize = 128 * 1024 * 1024;
            let estimate = |w: usize, h: usize, c: usize| w.saturating_mul(h).saturating_mul(c.saturating_mul(24).saturating_add(128));
            let requested = estimate(width, height, channels);
            while !self.liquid_diffusion.is_empty() && (self.liquid_diffusion.len() >= 8 ||
                self.liquid_diffusion.iter().map(|p| { let (w,h,c) = p.dimensions(); estimate(w,h,c) })
                    .fold(requested, usize::saturating_add) > CACHE_BYTES) {
                self.liquid_diffusion.pop_back();
            }
            let pipeline = onisim_gpu::pollster::block_on(MulticomponentGpu::new_with_context(
                self.context.clone(), width, height, channels))?;
            self.liquid_diffusion.push_front(pipeline);
        }
        self.liquid_diffusion.front_mut().ok_or("liquid pipeline unavailable".into())
    }

    pub(crate) fn new(width: usize, height: usize) -> Result<Self, String> {
        let context = Arc::new(onisim_gpu::pollster::block_on(GpuContext::new())?);
        let adapter_name = context.adapter.name.clone();
        let thermal = onisim_gpu::pollster::block_on(ThermalDiffusionGpu::new_with_context(
            context.clone(),
            width,
            height,
        ))?;
        let cells = width * height;
        eprintln!(
            "[CycleTrim OniSim] GPU acceleration active on Vulkan device: {} (grid {}x{})",
            adapter_name, width, height
        );
        Ok(Self {
            context,
            thermal,
            region_map: vec![0; cells],
            region_bounds: Vec::new(),
            region_map_initialized: false,
            total_steps: 0,
            gas: VecDeque::new(),
            liquid_diffusion: VecDeque::new(),
        })
    }

    #[cfg(test)]
    pub(crate) fn diffuse_liquid(&mut self, width: usize, height: usize, channels: usize,
        state: &State<'_>, temperature: &[f32], output: &mut [onisim_gpu::multicomponent::ProjectedLiquidCell]) -> Result<bool, String> {
        let pipeline = self.liquid_pipeline(width, height, channels)?;
        let valid = if channels >= 4 {
            let valid = pipeline.diffuse_liquids_from_temperature_projected(state, temperature, output)?;
            if valid {
                static LOG_PACKED_LIQUID: std::sync::Once = std::sync::Once::new();
                LOG_PACKED_LIQUID.call_once(|| eprintln!("[CycleTrim OniSim] GPU liquid temperature upload and projected readback (region {width}x{height}, {channels} channels, 16 bytes/cell readback)"));
            }
            valid
        } else {
            let mut mass = vec![0.0; width * height * channels];
            let mut energy = vec![0.0; mass.len()];
            pipeline.diffuse_liquids_from_temperature(state, temperature, &mut mass, &mut energy)?;
            if mass.chunks_exact(channels).any(|cell| cell.iter().filter(|&&m| m > 0.0).take(3).count() > 2) {
                false
            } else {
                for (cell, result) in output.iter_mut().enumerate() {
                    let mut first=(0usize,0.0f32); let mut second=(0usize,0.0f32); let mut heat=0.0;
                    for c in 0..channels { let m=mass[cell*channels+c].max(0.0); heat+=energy[cell*channels+c];
                        if m>first.1 {second=first;first=(c,m);} else if m>second.1 {second=(c,m);} }
                    *result=onisim_gpu::multicomponent::ProjectedLiquidCell {first:first.0 as u32,second:second.0 as u32,
                        first_mass:first.1,second_mass:second.1,enthalpy:heat,overflow:0};
                }
                true
            }
        };
        static LOG_FIRST_LIQUID: std::sync::Once = std::sync::Once::new();
        LOG_FIRST_LIQUID.call_once(|| eprintln!("[CycleTrim OniSim] GPU liquid concentration diffusion completed (region {width}x{height}, {channels} channels)"));
        Ok(valid)
    }

    #[cfg(test)]
    pub(crate) fn diffuse_sparse_liquid(&mut self, width:usize, height:usize, channels:usize,
        cells:&[onisim_gpu::multicomponent::SparseLiquidCell], solid:&[u32], species:&[onisim_gpu::multicomponent::SpeciesInfo],
        output:&mut [onisim_gpu::multicomponent::ProjectedLiquidCell]) -> Result<bool,String> {
        let valid=self.liquid_pipeline(width, height, channels)?
            .diffuse_sparse_liquids_projected(cells,solid,species,output)?;
        if valid {
            static LOG_SPARSE:std::sync::Once=std::sync::Once::new();
            LOG_SPARSE.call_once(||eprintln!("[CycleTrim OniSim] GPU liquid sparse upload and projected readback (region {width}x{height}, {channels} channels, 16 bytes/cell upload and readback)"));
        }
        Ok(valid)
    }

    pub(crate) fn begin_sparse_liquid(&mut self, width:usize, height:usize, channels:usize,
        cells:&[onisim_gpu::multicomponent::PackedSparseCell], solid:&[u32], species:&[onisim_gpu::multicomponent::SpeciesInfo])
        -> Result<onisim_gpu::multicomponent::PendingLiquidProjection,String> {
        let pending=self.liquid_pipeline(width,height,channels)?.begin_packed_sparse_liquids_projected(cells,solid,species)?;
        static LOG:std::sync::Once=std::sync::Once::new();
        LOG.call_once(||eprintln!("[CycleTrim OniSim] GPU liquid diffusion submitted before gas transport (region {width}x{height}, {channels} channels, 16 bytes/cell readback)"));
        Ok(pending)
    }

    /// Project and transport a gas region using shared device buffers. The
    /// surrounding simulation still needs the final tensors on the CPU.
    pub(crate) fn step_gas(
        &mut self,
        width: usize,
        height: usize,
        channels: usize,
        state: &State<'_>,
        temperature: &[f32],
        species_conductivity: &[f32],
        diffuse: bool,
        dt: f32,
        mass_output: &mut [f32],
        temperature_output: &mut [f32],
    ) -> Result<(), String> {
        self.step_gas_impl(width,height,channels,state,temperature,species_conductivity,diffuse,dt,mass_output,temperature_output,None,None).map(|_| ())
    }

    pub(crate) fn step_gas_projected(
        &mut self, width:usize, height:usize, channels:usize, state:&State<'_>, temperature:&[f32],
        species_conductivity:&[f32], diffuse:bool, dt:f32,
        output:&mut [onisim_gpu::multicomponent::ProjectedLiquidCell],
    ) -> Result<bool,String> {
        self.step_gas_impl(width,height,channels,state,temperature,species_conductivity,diffuse,dt,&mut [],&mut [],Some(output),None)
    }

    pub(crate) fn step_sparse_gas_projected(
        &mut self, width:usize, height:usize, channels:usize, state:&State<'_>, temperature:&[f32],
        species_conductivity:&[f32], diffuse:bool, dt:f32,
        cells:&[onisim_gpu::multicomponent::PackedSparseCell],
        output:&mut [onisim_gpu::multicomponent::ProjectedLiquidCell],
    ) -> Result<bool,String> {
        self.step_gas_impl(width,height,channels,state,temperature,species_conductivity,diffuse,dt,&mut [],&mut [],Some(output),Some(cells))
    }

    fn step_gas_impl(
        &mut self, width:usize, height:usize, channels:usize, state:&State<'_>, temperature:&[f32],
        species_conductivity:&[f32], diffuse:bool, dt:f32, mass_output:&mut [f32], temperature_output:&mut [f32],
        projected_output:Option<&mut [onisim_gpu::multicomponent::ProjectedLiquidCell]>,
        sparse:Option<&[onisim_gpu::multicomponent::PackedSparseCell]>,
    ) -> Result<bool,String> {
        static PROFILE: std::sync::OnceLock<bool> = std::sync::OnceLock::new();
        static SAMPLE: std::sync::atomic::AtomicUsize = std::sync::atomic::AtomicUsize::new(0);
        let profiling = *PROFILE.get_or_init(|| std::env::var_os("CYCLETRIM_GPU_PROFILE").is_some())
            && SAMPLE.fetch_add(1,std::sync::atomic::Ordering::Relaxed) % 61 == 0;
        let started = std::time::Instant::now();
        if let Some(index) = self.gas.iter().position(|p| p.transport.dimensions() == (width, height, channels)) {
            let pipeline = self.gas.remove(index).ok_or("GPU gas cache entry disappeared")?;
            self.gas.push_front(pipeline);
        } else {
            // Bound retained regions: multiple active worlds often alternate
            // dimensions, but retaining unlimited device buffers is expensive.
            const CACHE_BYTES: usize = 96 * 1024 * 1024;
            let estimate = |w: usize, h: usize, c: usize| {
                w.saturating_mul(h).saturating_mul(c.saturating_mul(24).saturating_add(128))
            };
            let new_bytes = estimate(width, height, channels);
            while !self.gas.is_empty() && (self.gas.len() >= 8 ||
                self.gas.iter().map(|p| { let (w,h,c)=p.transport.dimensions(); estimate(w,h,c) })
                    .fold(new_bytes, usize::saturating_add) > CACHE_BYTES) {
                self.gas.pop_back();
            }
            let transport = onisim_gpu::pollster::block_on(MulticomponentGpu::new_with_context(
                self.context.clone(), width, height, channels,
            ))?;
            let pressure = onisim_gpu::pollster::block_on(PressureProjectionGpu::new_with_context(
                self.context.clone(), width, height,
            ))?;
            let thermal = onisim_gpu::pollster::block_on(ThermalDiffusionGpu::new_with_context(
                self.context.clone(), width, height,
            ))?;
            let enthalpy = ThermalEnthalpyGpu::new(self.context.clone());
            self.gas.push_front(GasPipeline { transport, pressure, thermal, enthalpy });
        }
        let pipeline = self.gas.front_mut().ok_or("GPU gas pipeline unavailable")?;
        let mut encoder = self.context.command_encoder();
        if let Some(cells)=sparse {
            pipeline.transport.encode_packed_sparse_gas_into_temperature(&mut encoder,cells,state.solid,state.species,pipeline.thermal.temperature_buffer())?;
            static LOG_SPARSE_GAS: std::sync::Once = std::sync::Once::new();
            LOG_SPARSE_GAS.call_once(|| eprintln!("[CycleTrim OniSim] GPU gas sparse upload (region {width}x{height}, {channels} channels, 16 bytes/cell)"));
        } else {
            pipeline.transport.upload_for_temperature(state, temperature)?;
            pipeline.thermal.upload_temperature(temperature)?;
        }
        if sparse.is_none() { pipeline.enthalpy.encode(&mut encoder,width * height, channels, pipeline.thermal.temperature_buffer(),
            pipeline.transport.current_mass_buffer(), pipeline.transport.species_heat_buffer(),
            pipeline.transport.current_enthalpy_buffer())?; }
        pipeline.transport.encode_diffuse_and_generate_density_velocity(&mut encoder, diffuse && channels > 1)?;
        pipeline.enthalpy.encode_conductivity(&mut encoder, width * height, channels, pipeline.transport.current_mass_buffer(),
            species_conductivity, pipeline.thermal.conductivity_buffer())?;
        let uploaded = started.elapsed();
        let gauge = state.solid.iter().enumerate().find_map(|(cell, &solid)| {
            let start = cell * channels;
            let nonempty = if let Some(sparse)=sparse { sparse[cell].first_mass>0.0 || sparse[cell].second_mass>0.0 }
                else { state.mass[start..start + channels].iter().any(|&m| m > 0.0) };
            (solid == 0 && nonempty).then_some(cell)
        }).ok_or("gas region has no nonempty fluid cell")?;
        let (vx, vy) = pipeline.transport.velocity_buffers();
        pipeline.pressure.encode_with_shared_state(
            &mut encoder,
            pipeline.transport.current_mass_buffer(), vx, vy, pipeline.transport.solid_buffer(),
            channels, 1.0, 1.0, gauge, dt, 1.0, 1.0, 4, 1.0,
        )?;
        pipeline.transport.encode_with_deferred_cfl(&mut encoder, dt)?;
        let projected = started.elapsed();
        let cells = width * height;
        let (temperature, total_mass, heat_capacity) = pipeline.thermal.state_buffers();
        pipeline.enthalpy.encode_prepare(&mut encoder, cells, channels, pipeline.transport.current_mass_buffer(),
            pipeline.transport.current_enthalpy_buffer(), pipeline.transport.species_heat_buffer(),
            temperature, total_mass, heat_capacity)?;
        pipeline.thermal.encode_resident(&mut encoder, dt * 4.0, 0.25)?;
        let valid = if let Some(output)=projected_output {
            let valid=pipeline.transport.readback_projected_gas_after(encoder,pipeline.thermal.temperature_buffer(),output)?;
            if valid {
                static LOG_GAS_PROJECTION:std::sync::Once=std::sync::Once::new();
                LOG_GAS_PROJECTION.call_once(||eprintln!("[CycleTrim OniSim] GPU gas projected readback (region {width}x{height}, {channels} channels, 16 bytes/cell)"));
            }
            valid
        } else {
            pipeline.transport.readback_mass_and_temperature_after(encoder,pipeline.thermal.temperature_buffer(), mass_output, temperature_output)?;
            true
        };
        if diffuse && channels > 1 {
            static LOG_FIRST_DIFFUSION: std::sync::Once = std::sync::Once::new();
            LOG_FIRST_DIFFUSION.call_once(|| eprintln!("[CycleTrim OniSim] GPU concentration diffusion dispatch completed (region {width}x{height}, {channels} channels; unmapped gases may be excluded)"));
        }
        if profiling {
            eprintln!("[CycleTrim OniSim] GPU gas profile {width}x{height}x{channels}: upload/setup={:.3}ms projection+CFL-submit={:.3}ms remaining-submit+GPU-wait+readback={:.3}ms", uploaded.as_secs_f64()*1000.0, (projected-uploaded).as_secs_f64()*1000.0, (started.elapsed()-projected).as_secs_f64()*1000.0);
        }
        static LOG_FIRST_GAS_CHAIN: std::sync::Once = std::sync::Once::new();
        LOG_FIRST_GAS_CHAIN.call_once(|| {
            eprintln!("[CycleTrim OniSim] GPU gas pressure, transport and heat chain completed (region {width}x{height}, {channels} channels)");
        });
        Ok(valid)
    }

    /// Execute GPU thermal diffusion step for the entire padded grid.
    pub(crate) fn step_thermal(
        &mut self,
        world: &mut PhysicsWorld,
        elements: &ElementTable,
        dt: f32,
    ) -> Result<(), String> {
        static PROFILE: std::sync::OnceLock<bool> = std::sync::OnceLock::new();
        static SAMPLE: std::sync::atomic::AtomicUsize = std::sync::atomic::AtomicUsize::new(0);
        let profiling = *PROFILE.get_or_init(|| std::env::var_os("CYCLETRIM_GPU_PROFILE").is_some())
            && SAMPLE.fetch_add(1,std::sync::atomic::Ordering::Relaxed) % 8 == 0;
        let started = std::time::Instant::now();
        let count = world.read.temperature.len();
        if self.region_map.len() != count {
            return Err("grid dimension mismatch".into());
        }

        let properties: Vec<_> = elements.elements.iter().map(|element| {
            [element.record.thermal_conductivity.to_bits(), element.record.specific_heat_capacity.to_bits()]
        }).collect();
        self.thermal.prepare_materials_by_element(&world.read.element, &properties)?;
        static LOG_MATERIAL:std::sync::Once=std::sync::Once::new();
        LOG_MATERIAL.call_once(||eprintln!("[CycleTrim OniSim] GPU thermal material lookup enabled ({count} cells)"));
        self.thermal.upload_state(&world.read.temperature, &world.read.mass)?;
        let bounds:Vec<_>=world.regions.iter().map(|r|(r.min_x,r.min_y,r.max_x,r.max_y)).collect();
        if !self.region_map_initialized || self.region_bounds!=bounds {
        self.region_map.fill(0);
        for (index, region) in world.regions.iter().enumerate() {
            let min_x = region.min_x.max(1) as usize;
            let min_y = region.min_y.max(1) as usize;
            let max_x = (region.max_x as usize).min(world.width);
            let max_y = (region.max_y as usize).min(world.height);
            if max_x.saturating_sub(min_x) < 2 || max_y.saturating_sub(min_y) < 2 { continue; }
            for y in min_y..max_y {
                self.region_map[y*world.stride+min_x..y*world.stride+max_x].fill((index+1) as u32);
            }
        }
        self.region_bounds=bounds; self.region_map_initialized=true;
        }
        let flags:Vec<u32>=(0..elements.len().max(world.gas_channel_of_element.len())).map(|id| {
            let mapped=world.gas_channel_of_element.get(id).is_some_and(|&c|c!=u8::MAX);
            let primary=mapped && elements.elements.get(id).is_some_and(|e|e.record.state&3==1);
            u32::from(primary) | (u32::from(mapped)<<1)
        }).collect();
        self.thermal.prepare_deferred_groups_on_gpu(&world.mixture_element,&world.mixture_mass,&world.read.properties,&self.region_map,&flags)?;
        let prepared = started.elapsed();
        // Keep thermal dispatch and the CPU-required result copy in one
        // caller-owned submission. This is also the insertion point for other
        // GPU-resident operators that depend on the updated temperature.
        let mut encoder = self.context.command_encoder();
        self.thermal.encode_resident(&mut encoder, dt, 0.25)?;
        self.thermal.encode_readback(&mut encoder);
        let submission = self.context.queue.submit([encoder.finish()]);
        self.thermal.finish_readback_after_submission(
            &mut world.write.temperature,
            submission,
        )?;
        if profiling {
            eprintln!("[CycleTrim OniSim] GPU thermal profile cells={count} deferred_groups=gpu mass_upload_bytes={} temperature_upload_bytes={} preparation={:.3}ms submit+wait+readback={:.3}ms", self.thermal.last_mass_upload_bytes(),self.thermal.last_temperature_upload_bytes(),prepared.as_secs_f64()*1000.0, (started.elapsed()-prepared).as_secs_f64()*1000.0);
        }

        self.total_steps += 1;
        Ok(())
    }
}

#[cfg(test)]
const MATERIAL_BLOCK: usize = 4096;

#[cfg(test)]
fn material_upload_ranges<T: PartialEq>(previous: &[T], current: &[T], table_changed: bool) -> Vec<(usize, usize)> {
    debug_assert_eq!(previous.len(), current.len());
    let count = current.len();
    let mut ranges = Vec::new();
    for start in (0..count).step_by(MATERIAL_BLOCK) {
        let end = (start + MATERIAL_BLOCK).min(count);
        if table_changed || previous[start..end] != current[start..end] {
            if let Some((_, last_end)) = ranges.last_mut() {
                if *last_end == start {
                    *last_end = end;
                    continue;
                }
            }
            ranges.push((start, end));
        }
    }
    if ranges.len() > 16 { vec![(0, count)] } else { ranges }
}

#[cfg(test)]
mod material_range_tests {
    use super::material_upload_ranges;

    #[test]
    fn liquid_pipeline_cache_retains_multiple_region_shapes() {
        let mut gpu = super::GpuSimulation::new(8, 8).unwrap();
        for width in [3, 4, 5, 6] { gpu.liquid_pipeline(width, 3, 2).unwrap(); }
        assert_eq!(gpu.liquid_diffusion.len(), 4);
        gpu.liquid_pipeline(3, 3, 2).unwrap();
        assert_eq!(gpu.liquid_diffusion.len(), 4);
        assert_eq!(gpu.liquid_diffusion.front().unwrap().dimensions(), (3, 3, 2));
        assert!(gpu.liquid_diffusion.iter().any(|p| p.dimensions() == (6, 3, 2)));
    }

    #[test]
    fn liquid_dispatch_before_gas_pressure_matches_serial_order() {
        use crate::{events::EventQueues, multicomponent_flux as flux, tables::{DiseaseTable, ElementTable}, world::{PhysicsWorld, Region}};
        let mut bytes = 6_i32.to_le_bytes().to_vec();
        for index in 0..6usize {
            let id = match index { 0 => 0xa9360b34u32, 1 => 0x2d39bf75, 2 => 0x6d95058c, _ => 0x11110000 + index as u32 };
            let mut record = [0u8; 164];
            record[..4].copy_from_slice(&id.to_le_bytes());
            record[8..10].copy_from_slice(&(index as u16).to_le_bytes());
            record[10] = if index < 2 { 0 } else if index == 2 { 3 } else if index == 5 { 1 } else { 2 };
            record[12..16].copy_from_slice(&1.0f32.to_le_bytes());
            record[36..40].copy_from_slice(&0.5f32.to_le_bytes());
            bytes.extend_from_slice(&record);
        }
        bytes.extend_from_slice(&[0u8; 24]);
        let elements=ElementTable::parse(&bytes).unwrap();
        let diseases=DiseaseTable { diseases: Vec::new(), element_count: elements.len() };
        let make_world=|| {
            let mut world=PhysicsWorld::new(8,8,0,&elements).unwrap();
            world.write.element.fill(world.vacuum_element);
            world.write.temperature.fill(300.0);
            for (x,y,id,mass) in [(3,3,3,1.0),(4,3,4,1.0),(3,4,5,3.0),(4,4,5,0.5)] {
                let i=y*world.stride+x; world.write.element[i]=id; world.write.mass[i]=mass;
            }
            // A liquid secondary in a gas-primary cell must remain CPU-owned.
            let i=4*world.stride+3; world.mixture_element[i]=3; world.mixture_mass[i]=0.2;
            world.sync_read_from_write(); world
        };
        let region=Region {min_x:2,min_y:2,max_x:7,max_y:7,..Default::default()};
        let mut serial=make_world(); let mut overlap=make_world();
        let mut gpu=super::GpuSimulation::new(serial.stride,serial.padded_height).unwrap();
        let mut a=EventQueues::default(); let mut b=EventQueues::default();
        let before_gas=serial.write.mass.clone();
        assert!(crate::simulation_step_fluid::gas_phase(&mut serial,&elements,&diseases,&mut a,region));
        assert_ne!(before_gas,serial.write.mass, "fixture must exercise an actual gas transfer");
        let serial_pending=flux::begin_sparse_liquid_region(&serial,&elements,region,&mut gpu).unwrap().unwrap();
        let mut serial_mask=Vec::new();
        assert!(flux::finish_sparse_liquid_region(serial_pending,&mut serial,&mut a,&mut serial_mask).unwrap());
        let pending=flux::begin_sparse_liquid_region(&overlap,&elements,region,&mut gpu).unwrap().unwrap();
        assert!(crate::simulation_step_fluid::gas_phase(&mut overlap,&elements,&diseases,&mut b,region));
        let mut overlap_mask=Vec::new();
        assert!(flux::finish_sparse_liquid_region(pending,&mut overlap,&mut b,&mut overlap_mask).unwrap());
        assert_eq!(serial_mask,overlap_mask);
        assert_eq!(serial.write.element,overlap.write.element);
        assert_eq!(serial.write.mass,overlap.write.mass);
        assert_eq!(serial.write.temperature,overlap.write.temperature);
        assert_eq!(serial.mixture_element,overlap.mixture_element);
        assert_eq!(serial.mixture_mass,overlap.mixture_mass);
        assert_eq!(serial.flow,overlap.flow);
    }

    #[test]
    fn liquid_region_diffuses_on_gpu_and_preserves_mass_and_energy() {
        use crate::{events::EventQueues, multicomponent_flux, tables::ElementTable, world::{PhysicsWorld, Region}};
        use super::GpuSimulation;
        let mut bytes = 8_i32.to_le_bytes().to_vec();
        for index in 0..8usize {
            let id = match index { 0 => 0xa9360b34u32, 1 => 0x2d39bf75, 2 => 0x6d95058c, _ => 0x11110000 + index as u32 };
            let mut record = [0u8; 164]; record[..4].copy_from_slice(&id.to_le_bytes());
            record[8..10].copy_from_slice(&(index as u16).to_le_bytes());
            record[10] = if index < 2 { 0 } else if index == 2 { 3 } else { 2 };
            record[12..16].copy_from_slice(&1.0f32.to_le_bytes()); bytes.extend_from_slice(&record);
        }
        bytes.extend_from_slice(&[0u8; 32]);
        let elements = ElementTable::parse(&bytes).unwrap();
        let mut world = PhysicsWorld::new(4, 4, 0, &elements).unwrap();
        world.write.element.fill(world.vacuum_element); world.write.temperature.fill(300.0);
        let a=world.stride+1; let b=a+1;
        world.write.element[a]=3; world.write.mass[a]=1.0; world.write.temperature[a]=400.0;
        world.write.element[b]=4; world.write.mass[b]=1.0; world.write.temperature[b]=200.0;
        let region=Region {min_x:1,min_y:1,max_x:4,max_y:4,..Default::default()};
        let mut gpu=GpuSimulation::new(world.stride,world.padded_height).unwrap();
        let mut events=EventQueues::default(); let mut solid=Vec::new();
        assert!(multicomponent_flux::diffuse_liquid_region(&mut world,&elements,&mut events,region,&mut gpu,&mut solid).unwrap());
        assert!(world.mixture_mass[a]>0.0 && world.mixture_mass[b]>0.0);
        assert!((world.write.mass[a]+world.mixture_mass[a]+world.write.mass[b]+world.mixture_mass[b]-2.0).abs()<1e-5);
        assert!((world.write.temperature[a]+world.write.temperature[b]-600.0).abs()<1e-3);
        let mut overlap=PhysicsWorld::new(4,4,0,&elements).unwrap();
        overlap.write.element.fill(overlap.vacuum_element); overlap.write.temperature.fill(300.0);
        overlap.write.element[a]=3; overlap.write.mass[a]=1.0; overlap.write.temperature[a]=400.0;
        overlap.write.element[b]=4; overlap.write.mass[b]=1.0; overlap.write.temperature[b]=200.0;
        let pending=multicomponent_flux::begin_sparse_liquid_region(&overlap,&elements,region,&mut gpu).unwrap().unwrap();
        let mut overlap_solid=Vec::new();
        assert!(multicomponent_flux::finish_sparse_liquid_region(pending,&mut overlap,&mut events,&mut overlap_solid).unwrap());
        assert_eq!(solid,overlap_solid);
        for cell in [a,b] {
            assert_eq!(world.write.element[cell],overlap.write.element[cell]);
            assert_eq!(world.mixture_element[cell],overlap.mixture_element[cell]);
            assert!((world.write.mass[cell]-overlap.write.mass[cell]).abs()<1e-6);
            assert!((world.mixture_mass[cell]-overlap.mixture_mass[cell]).abs()<1e-6);
            assert!((world.write.temperature[cell]-overlap.write.temperature[cell]).abs()<1e-4);
        }
        let before=(world.mixture_mass[a],world.mixture_mass[b]);
        crate::gas::diffuse_region(&mut world,&elements,&mut events,region,None,Some(&solid));
        assert_eq!((world.mixture_mass[a],world.mixture_mass[b]),before);

        let mut world = PhysicsWorld::new(8, 4, 0, &elements).unwrap();
        world.write.element.fill(world.vacuum_element); world.write.temperature.fill(300.0);
        for (x,id) in [(1,3), (2,4), (3,5), (5,3), (6,4)] {
            let cell=world.stride+x; world.write.element[cell]=id; world.write.mass[cell]=1.0;
        }
        let region=Region {min_x:1,min_y:1,max_x:8,max_y:4,..Default::default()};
        let mut solid=Vec::new();
        assert!(multicomponent_flux::diffuse_liquid_region(&mut world,&elements,&mut events,region,&mut gpu,&mut solid).unwrap());
        assert!(solid[1] != 0 && solid[4] == 0, "three-species stencil must be isolated without blocking other cells");
        let mut overlap=PhysicsWorld::new(8,4,0,&elements).unwrap();
        overlap.write.element.fill(overlap.vacuum_element); overlap.write.temperature.fill(300.0);
        for (x,id) in [(1,3), (2,4), (3,5), (5,3), (6,4)] {
            let cell=overlap.stride+x; overlap.write.element[cell]=id; overlap.write.mass[cell]=1.0;
        }
        let pending=multicomponent_flux::begin_sparse_liquid_region(&overlap,&elements,region,&mut gpu).unwrap().unwrap();
        let mut overlap_solid=Vec::new();
        assert!(multicomponent_flux::finish_sparse_liquid_region(pending,&mut overlap,&mut events,&mut overlap_solid).unwrap());
        assert_eq!(solid,overlap_solid);
        for x in 1..8 {
            let cell=world.stride+x;
            assert_eq!(world.write.element[cell],overlap.write.element[cell]);
            assert_eq!(world.mixture_element[cell],overlap.mixture_element[cell]);
            assert!((world.write.mass[cell]-overlap.write.mass[cell]).abs()<1e-6);
            assert!((world.mixture_mass[cell]-overlap.mixture_mass[cell]).abs()<1e-6);
        }
        for id in 3..6 {
            let total:f32=(1..4).flat_map(|y|(1..8).map(move |x|(x,y))).map(|(x,y)| {
                let c=y*world.stride+x;
                (if world.write.element[c]==id {world.write.mass[c]} else {0.0})+
                (if world.mixture_element[c]==id {world.mixture_mass[c]} else {0.0})
            }).sum();
            let expected=if id==5 {1.0} else {2.0};
            assert!((total-expected).abs()<1e-5);
        }
        let fourth=world.stride+7;
        world.write.element[fourth]=6; world.write.mass[fourth]=1.0;
        let pending=multicomponent_flux::begin_sparse_liquid_region(&world,&elements,region,&mut gpu).unwrap().unwrap();
        assert!(multicomponent_flux::finish_sparse_liquid_region(pending,&mut world,&mut events,&mut solid).unwrap());
        assert_eq!(gpu.liquid_diffusion.front().unwrap().dimensions(), (7,3,4));
        for id in 3..7 {
            let total:f32=(1..4).flat_map(|y|(1..8).map(move |x|(x,y))).map(|(x,y)| {
                let c=y*world.stride+x;
                (if world.write.element[c]==id {world.write.mass[c]} else {0.0})+
                (if world.mixture_element[c]==id {world.mixture_mass[c]} else {0.0})
            }).sum();
            let expected=if id==3 || id==4 {2.0} else {1.0};
            assert!((total-expected).abs()<1e-5, "four-liquid region {id} mass {total}");
        }
        for (x,y,id) in [(7,2,7)] {
            let c=y*world.stride+x; world.write.element[c]=id; world.write.mass[c]=1.0;
        }
        assert!(multicomponent_flux::diffuse_liquid_region(&mut world,&elements,&mut events,region,&mut gpu,&mut solid).unwrap());
        assert_eq!(gpu.liquid_diffusion.front().unwrap().dimensions(), (7,3,5));
        for id in 3..8 {
            let total:f32=(1..4).flat_map(|y|(1..8).map(move |x|(x,y))).map(|(x,y)| {
                let c=y*world.stride+x;
                (if world.write.element[c]==id {world.write.mass[c]} else {0.0})+
                (if world.mixture_element[c]==id {world.mixture_mass[c]} else {0.0})
            }).sum();
            let expected=if id==3 || id==4 {2.0} else {1.0};
            assert!((total-expected).abs()<1e-5, "liquid {id} mass {total}");
        }
    }

    #[test]
    fn overlapped_liquid_matches_serial_with_gas_in_same_region() {
        use crate::{events::EventQueues, multicomponent_flux as flux, tables::ElementTable, world::{PhysicsWorld, Region}};
        let mut bytes=9_i32.to_le_bytes().to_vec();
        for index in 0..9usize {
            let id=match index {0=>0xa9360b34u32,1=>0x2d39bf75,2=>0x6d95058c,_=>0x11110000+index as u32};
            let mut record=[0u8;164]; record[..4].copy_from_slice(&id.to_le_bytes());
            record[8..10].copy_from_slice(&(index as u16).to_le_bytes());
            record[10]=if index<2 {0} else if index==2 {3} else if index<=6 {2} else {1};
            record[12..16].copy_from_slice(&1.0f32.to_le_bytes());
            record[16..20].copy_from_slice(&1.0f32.to_le_bytes());
            bytes.extend_from_slice(&record);
        }
        bytes.extend_from_slice(&[0u8;36]);
        let elements=ElementTable::parse(&bytes).unwrap();
        let make_world=|| {
            let mut world=PhysicsWorld::new(10,6,1,&elements).unwrap();
            world.write.element.fill(world.vacuum_element); world.write.temperature.fill(300.0);
            for y in 1..6 {for x in 1..10 {
                let cell=y*world.stride+x;
                let id=if x<=3 {if y<=2 {3+(x%2) as u16} else {5+((y+x)%2) as u16}} else {7+(x%2) as u16};
                world.write.element[cell]=id; world.write.mass[cell]=1.0+(x as f32)*0.1;
                world.write.temperature[cell]=280.0+(y as f32)*10.0;
            }}
            world.sync_read_from_write(); flux::rebuild_gas_channels(&mut world,&elements); world
        };
        let mut serial=make_world(); let mut overlap=make_world();
        let region=Region {min_x:1,min_y:1,max_x:10,max_y:6,..Default::default()};
        let mut gpu=super::GpuSimulation::new(serial.stride,serial.padded_height).unwrap();
        let mut serial_events=EventQueues::default(); let mut overlap_events=EventQueues::default();
        let mut serial_solid=Vec::new(); let mut overlap_solid=Vec::new();
        assert!(flux::step_region(&mut serial,&elements,&mut serial_events,region,Some(&mut gpu),true).unwrap());
        assert!(flux::diffuse_liquid_region(&mut serial,&elements,&mut serial_events,region,&mut gpu,&mut serial_solid).unwrap());
        let pending=flux::begin_sparse_liquid_region(&overlap,&elements,region,&mut gpu).unwrap().unwrap();
        assert!(flux::step_region(&mut overlap,&elements,&mut overlap_events,region,Some(&mut gpu),true).unwrap());
        assert!(flux::finish_sparse_liquid_region(pending,&mut overlap,&mut overlap_events,&mut overlap_solid).unwrap());
        assert_eq!(serial_solid,overlap_solid);
        for y in 1..6 {for x in 1..10 {
            let cell=y*serial.stride+x;
            assert_eq!(serial.write.element[cell],overlap.write.element[cell],"primary {x},{y}");
            assert_eq!(serial.mixture_element[cell],overlap.mixture_element[cell],"secondary {x},{y}");
            for (a,b) in [(serial.write.mass[cell],overlap.write.mass[cell]),
                (serial.mixture_mass[cell],overlap.mixture_mass[cell]),(serial.write.temperature[cell],overlap.write.temperature[cell])] {
                assert!((a-b).abs()<1e-4,"cell {x},{y}: {a} != {b}");
            }
        }}
    }

    #[test]
    fn region_diffuses_two_gases_on_gpu_without_losing_mass() {
        use crate::{events::EventQueues, multicomponent_flux, tables::ElementTable, world::{PhysicsWorld, Region}};
        use super::GpuSimulation;
        let mut bytes = 13_i32.to_le_bytes().to_vec();
        for index in 0..13usize {
            let (id, phase) = match index {0 => (0xa9360b34u32,0u8),1 => (0x2d39bf75,0),2 => (0x6d95058c,3),_ => (0x11110000+index as u32,1)};
            let mut record=[0u8;164]; record[..4].copy_from_slice(&id.to_le_bytes());
            record[8..10].copy_from_slice(&(index as u16).to_le_bytes()); record[10]=phase;
            record[12..16].copy_from_slice(&1.0f32.to_le_bytes()); record[16..20].copy_from_slice(&1.0f32.to_le_bytes());
            bytes.extend_from_slice(&record);
        }
        bytes.extend_from_slice(&[0u8;52]);
        let elements=ElementTable::parse(&bytes).unwrap();
        let mut world=PhysicsWorld::new(6, 4, 1, &elements).unwrap();
        world.write.element.fill(world.vacuum_element); world.write.temperature.fill(300.0);
        for y in 1..4 { for x in 1..6 { let cell=y*world.stride+x;
            world.write.element[cell]=if x<3 {4} else {12}; world.write.mass[cell]=1.0;
        }}
        world.sync_read_from_write(); multicomponent_flux::rebuild_gas_channels(&mut world,&elements);
        let region=Region {min_x:1,min_y:1,max_x:6,max_y:4,..Default::default()};
        assert_eq!(world.gas_channel_count,8);
        assert_eq!(multicomponent_flux::region_gas_species_up_to_three(&world,&elements,region),2);
        let mut gpu=GpuSimulation::new(world.stride,world.padded_height).unwrap();
        let mut events=EventQueues::default();
        assert!(multicomponent_flux::step_region(&mut world,&elements,&mut events,region,Some(&mut gpu),true).unwrap());
        assert_eq!(gpu.gas.front().unwrap().transport.dimensions(), (5, 3, 2));
        let left=world.stride*2+2; let right=left+1;
        assert!(world.mixture_mass[left]>0.0 && world.mixture_mass[right]>0.0);
        for cell in [left, right] {
            assert!([4, 12].contains(&world.write.element[cell]));
            assert!([4, 12].contains(&world.mixture_element[cell]));
            assert_ne!(world.write.element[cell], world.mixture_element[cell]);
        }
        let total:f32=(1..4).flat_map(|y|(1..6).map(move |x|(x,y)))
            .map(|(x,y)| {let c=y*world.stride+x;world.write.mass[c]+world.mixture_mass[c]}).sum();
        assert!((total-15.0).abs()<1e-4);
        world.write.element[world.stride+1]=5;
        world.write.mass[world.stride+1]=1.0;
        assert_eq!(multicomponent_flux::region_gas_species_up_to_three(&world,&elements,region),3);
        let totals = |world: &PhysicsWorld| -> Vec<f32> {
            [4u16, 5, 12].iter().map(|&id| (1..4).flat_map(|y|(1..6).map(move |x|(x,y)))
                .map(|(x,y)| { let c=y*world.stride+x;
                    (if world.write.element[c] == id { world.write.mass[c] } else { 0.0 }) +
                    (if world.mixture_element[c] == id { world.mixture_mass[c] } else { 0.0 })
                }).sum()).collect()
        };
        let before = totals(&world);
        let mut solid = Vec::new();
        assert!(multicomponent_flux::step_region_with_mask(&mut world,&elements,&mut events,region,Some(&mut gpu),true,Some(&mut solid)).unwrap());
        assert_eq!(gpu.gas.front().unwrap().transport.dimensions(), (5, 3, 3));
        assert!(solid.contains(&0) && solid.contains(&1), "exercise GPU and CPU ownership");
        crate::gas::diffuse_region(&mut world,&elements,&mut events,region,Some(&solid),None);
        for (a,b) in before.iter().zip(totals(&world)) { assert!((a-b).abs()<1e-4); }
    }

    #[test]
    fn gas_region_preserves_unmapped_primary_and_secondary_components() {
        use crate::{events::EventQueues, multicomponent_flux, tables::ElementTable, world::{PhysicsWorld, Region}};
        use super::GpuSimulation;
        let mut bytes = 13_i32.to_le_bytes().to_vec();
        for index in 0..13usize {
            let mut record = [0u8; 164];
            let id = match index { 0 => 0xa9360b34u32, 1 => 0x2d39bf75, 2 => 0x6d95058c, _ => 0x11110000 + index as u32 };
            record[..4].copy_from_slice(&id.to_le_bytes());
            record[8..10].copy_from_slice(&(index as u16).to_le_bytes());
            record[10] = if index < 2 { 0 } else if index == 2 { 3 } else { 1 };
            record[12..16].copy_from_slice(&1.0f32.to_le_bytes());
            record[84..88].fill(255);
            bytes.extend_from_slice(&record);
        }
        bytes.extend_from_slice(&[0; 52]);
        let elements = ElementTable::parse(&bytes).unwrap();
        let mut world = PhysicsWorld::new(4, 4, 0, &elements).unwrap();
        multicomponent_flux::rebuild_gas_channels(&mut world, &elements);
        world.write.element.fill(world.vacuum_element);
        let a = world.stride + 1;
        let b = world.stride + 2;
        let c = world.stride * 2 + 1;
        world.write.element[a] = 12; world.write.mass[a] = 2.0;
        world.write.element[b] = 3; world.write.mass[b] = 1.0;
        world.mixture_element[b] = 12; world.mixture_mass[b] = 0.75;
        world.write.element[c] = 3; world.write.mass[c] = 1.0;
        world.write.temperature.fill(300.0);
        world.sync_read_from_write();
        let region = Region { min_x: 1, min_y: 1, max_x: 5, max_y: 5, ..Default::default() };
        let mut events = EventQueues::default();
        let mut gpu = GpuSimulation::new(world.stride, world.padded_height).unwrap();
        assert!(multicomponent_flux::step_region(&mut world, &elements, &mut events, region, Some(&mut gpu),true).unwrap());
        let stride = world.stride;
        let species_total = |world: &PhysicsWorld, id| -> f32 {
            (1..4).flat_map(|y| (1..4).map(move |x| y * stride + x)).map(|i|
                if world.write.element[i] == id { world.write.mass[i] } else { 0.0 } +
                if world.mixture_element[i] == id { world.mixture_mass[i] } else { 0.0 }).sum()
        };
        assert!((species_total(&world, 12) - 2.75).abs() < 1e-4);
        assert!((species_total(&world, 3) - 2.0).abs() < 1e-4);
        assert!(world.write.mass[a] < 2.0, "unmapped gas should participate in GPU transport");
        world.regions.push(region);
        multicomponent_flux::refresh_active_gas_channels(&mut world, &elements);
        assert_ne!(world.gas_channel_of_element[12], u8::MAX);
        let stride = world.stride;
        let total_before: f32 = (1..4).flat_map(|y| (1..4).map(move |x| y * stride + x))
            .map(|i| world.write.mass[i] + world.mixture_mass[i]).sum();
        assert!(multicomponent_flux::step_region(&mut world, &elements, &mut events, region, Some(&mut gpu),true).unwrap());
        let total_after: f32 = (1..4).flat_map(|y| (1..4).map(move |x| y * stride + x))
            .map(|i| world.write.mass[i] + world.mixture_mass[i]).sum();
        assert!((total_after - total_before).abs() < 1e-4);
        let old_channels = world.gas_element_of_channel;
        world.write.element[c] = 4;
        world.write.mass[c] = 1.0;
        multicomponent_flux::refresh_active_gas_channels(&mut world, &elements);
        assert_eq!(world.gas_element_of_channel, old_channels);
    }

    #[test]
    fn world_gas_region_uses_gpu_chain_without_duplicate_thermal_faces() {
        use crate::{events::EventQueues, multicomponent_flux, tables::ElementTable, world::{PhysicsWorld, Region}};
        use super::GpuSimulation;
        let mut bytes = 5_i32.to_le_bytes().to_vec();
        for (index, (id, phase, heat)) in [
            (0xa9360b34u32, 0u8, 1.0f32), (0x2d39bf75, 0, 1.0),
            (0x6d95058c, 3, 1.0), (0x11111111, 1, 1.0), (0x22222222, 1, 2.0),
        ].into_iter().enumerate() {
            let mut record = [0u8; 164];
            record[..4].copy_from_slice(&id.to_le_bytes());
            record[8..10].copy_from_slice(&(index as u16).to_le_bytes());
            record[10] = phase;
            record[12..16].copy_from_slice(&heat.to_le_bytes());
            record[16..20].copy_from_slice(&1.0f32.to_le_bytes());
            bytes.extend_from_slice(&record);
        }
        bytes.extend_from_slice(&[0u8; 20]);
        let elements = ElementTable::parse(&bytes).unwrap();
        let mut world = PhysicsWorld::new(8, 6, 1, &elements).unwrap();
        let region = Region { min_x: 1, min_y: 1, max_x: 7, max_y: 5, ..Default::default() };
        world.regions.push(region);
        multicomponent_flux::rebuild_gas_channels(&mut world, &elements);
        for y in 1..5 {
            for x in 1..7 {
                let cell = y * world.stride + x;
                world.write.element[cell] = 3;
                world.write.mass[cell] = 1.0;
                world.write.temperature[cell] = if x == 3 && y == 2 { 600.0 } else { 300.0 };
                world.mixture_element[cell] = 4;
                world.mixture_mass[cell] = 1.0;
            }
        }
        world.sync_read_from_write();
        let hot = 2 * world.stride + 3;
        let before: f64 = (1..5).flat_map(|y| (1..7).map(move |x| (x, y)))
            .map(|(x,y)| f64::from(world.write.temperature[y * world.stride + x]) * 3.0).sum();
        let mut gpu = GpuSimulation::new(world.stride, world.padded_height).unwrap();
        gpu.step_thermal(&mut world, &elements, 0.2).unwrap();
        assert_eq!(world.write.temperature[hot], 600.0, "gas faces should be deferred to the region pass");
        let mut events = EventQueues::default();
        assert!(multicomponent_flux::step_region(&mut world, &elements, &mut events, region, Some(&mut gpu),true).unwrap());
        let after: f64 = (1..5).flat_map(|y| (1..7).map(move |x| (x, y)))
            .map(|(x,y)| {
                let cell = y * world.stride + x;
                let a = f64::from(world.write.mass[cell]);
                let b = f64::from(world.mixture_mass[cell]);
                let (ha,hb) = if world.write.element[cell] == 3 { (1.0,2.0) } else { (2.0,1.0) };
                f64::from(world.write.temperature[cell]) * (a * ha + b * hb)
            }).sum();
        assert!(world.write.temperature[hot] < 600.0);
        assert!((after - before).abs() / before < 1e-5);
    }

    #[test]
    fn gas_pressure_transport_and_heat_chain_conserves_mass_and_energy() {
        use super::GpuSimulation;
        use onisim_gpu::multicomponent::{SpeciesInfo, State};
        let (width, height, channels) = (6, 4, 2);
        let count = width * height;
        let mass = vec![1.0f32; count * channels];
        let mut enthalpy = vec![0.0f32; count * channels];
        let hot = width + 2;
        for cell in 0..count {
            let temperature = if cell == hot { 600.0 } else { 300.0 };
            enthalpy[cell * channels] = temperature;
            enthalpy[cell * channels + 1] = 2.0 * temperature;
        }
        let velocity = vec![0.0; count];
        let solid = vec![0; count];
        let species = [
            SpeciesInfo { element_id: 1, phase: 1, specific_heat_capacity: 1.0, vertical_drift: 0.0 },
            SpeciesInfo { element_id: 2, phase: 1, specific_heat_capacity: 2.0, vertical_drift: 0.0 },
        ];
        let state = State { mass: &mass, enthalpy: &enthalpy, velocity_x: &velocity,
            velocity_y: &velocity, solid: &solid, species: &species };
        let mut gpu = GpuSimulation::new(width, height).unwrap();
        let mut out_mass = vec![0.0; mass.len()];
        let mut out_temperature = vec![0.0; count];
        let temperature: Vec<_> = (0..count).map(|i| if i == hot { 600.0 } else { 300.0 }).collect();
        gpu.step_gas(width, height, channels, &state, &temperature, &vec![1.0; channels], true, 0.05,
            &mut out_mass, &mut out_temperature).unwrap();
        for channel in 0..channels {
            let before: f64 = mass.iter().skip(channel).step_by(channels).map(|&v| f64::from(v)).sum();
            let after: f64 = out_mass.iter().skip(channel).step_by(channels).map(|&v| f64::from(v)).sum();
            assert!((after - before).abs() < 1e-5);
        }
        let before: f64 = enthalpy.iter().map(|&v| f64::from(v)).sum();
        let after: f64 = (0..count).map(|i| f64::from(out_temperature[i]) *
            (f64::from(out_mass[i*channels]) + 2.0 * f64::from(out_mass[i*channels+1]))).sum();
        assert!((after - before).abs() / before < 1e-6);
        assert!(out_temperature[hot] < temperature[hot]);
        assert!(out_temperature[hot - 1] > temperature[hot - 1]);
        let changed_species = [SpeciesInfo { specific_heat_capacity: 4.0, ..species[0] }, species[1]];
        let changed_state = State { species: &changed_species, ..state };
        gpu.step_gas(width, height, channels, &changed_state, &temperature, &[1.0, 1.0], true, 0.05,
            &mut out_mass, &mut out_temperature).unwrap();
        let expected: f64 = temperature.iter().map(|&t| f64::from(t) * 6.0).sum();
        let actual: f64 = (0..count).map(|i| f64::from(out_temperature[i]) *
            (4.0 * f64::from(out_mass[i*channels]) + 2.0 * f64::from(out_mass[i*channels+1]))).sum();
        assert!((actual - expected).abs() / expected < 1e-6, "changed heat capacity must reach the GPU");
    }

    #[test]
    fn gas_pipeline_reuses_alternating_region_sizes() {
        use super::GpuSimulation;
        use onisim_gpu::multicomponent::{SpeciesInfo, State};
        let mut gpu = GpuSimulation::new(5, 4).unwrap();
        let species = [SpeciesInfo { element_id: 1, phase: 1, specific_heat_capacity: 1.0, vertical_drift: 0.0 }];
        for (width, height) in [(4, 3), (5, 4), (6, 5), (7, 6), (4, 3), (5, 4), (6, 5), (7, 6)] {
            let cells = width * height;
            let mass = vec![1.0; cells];
            let enthalpy = vec![300.0; cells];
            let velocity = vec![0.0; cells];
            let solid = vec![0; cells];
            let temperature = vec![300.0; cells];
            let state = State { mass: &mass, enthalpy: &enthalpy, velocity_x: &velocity,
                velocity_y: &velocity, solid: &solid, species: &species };
            let mut out_mass = vec![0.0; cells];
            let mut out_temperature = vec![0.0; cells];
            gpu.step_gas(width, height, 1, &state, &temperature, &[1.0], true, 0.05,
                &mut out_mass, &mut out_temperature).unwrap();
            assert!(out_mass.iter().all(|m| (*m - 1.0).abs() < 1e-5));
            assert!(out_temperature.iter().all(|t| (*t - 300.0).abs() < 1e-4));
        }
        assert_eq!(gpu.gas.len(), 4);
    }

    #[test]
    #[ignore = "manual GPU timing on the game-sized gas region"]
    fn profile_game_sized_gas_chain() {
        use super::GpuSimulation;
        use onisim_gpu::multicomponent::{SpeciesInfo, State};
        let (width, height, channels) = (160, 274, 8);
        let cells = width * height;
        let mut mass = vec![0.0; cells * channels];
        let mut enthalpy = vec![0.0; cells * channels];
        for cell in 0..cells {
            let first = cell % channels;
            mass[cell * channels + first] = 1.0;
            enthalpy[cell * channels + first] = 300.0;
        }
        let velocity = vec![0.0; cells];
        let solid = vec![0; cells];
        let species: Vec<_> = (0..channels).map(|i| SpeciesInfo {
            element_id: i as u16 + 1, phase: 1, specific_heat_capacity: 1.0, vertical_drift: 0.0,
        }).collect();
        let state = State { mass: &mass, enthalpy: &enthalpy, velocity_x: &velocity,
            velocity_y: &velocity, solid: &solid, species: &species };
        let mut gpu = GpuSimulation::new(width, height).unwrap();
        let mut out_mass = vec![0.0; mass.len()];
        let mut out_temperature = vec![0.0; cells];
        let conductivity = vec![1.0; channels];
        for _ in 0..6 {
            gpu.step_gas(width, height, channels, &state, &vec![300.0; cells], &conductivity, false, 0.05,
                &mut out_mass, &mut out_temperature).unwrap();
        }
    }

    #[test]
    fn gpu_region_accepts_three_four_five_six_and_sixteen_distinct_gases_without_losing_mass() {
        use crate::{events::EventQueues, multicomponent_flux, tables::ElementTable, world::{PhysicsWorld, Region}};
        use super::GpuSimulation;
        let mut bytes = 19_i32.to_le_bytes().to_vec();
        for index in 0..19usize {
            let id = match index { 0 => 0xa9360b34u32, 1 => 0x2d39bf75, 2 => 0x6d95058c, _ => 0x11110000 + index as u32 };
            let mut record = [0u8; 164];
            record[..4].copy_from_slice(&id.to_le_bytes());
            record[8..10].copy_from_slice(&(index as u16).to_le_bytes());
            record[10] = if index < 2 { 0 } else if index == 2 { 3 } else { 1 };
            record[12..16].copy_from_slice(&1.0f32.to_le_bytes());
            bytes.extend_from_slice(&record);
        }
        bytes.extend_from_slice(&[0u8; 76]);
        let elements = ElementTable::parse(&bytes).unwrap();
        for gas_count in [3usize, 4, 5, 6, 16] {
        let mut world = PhysicsWorld::new(52, 4, 0, &elements).unwrap();
        world.write.element.fill(world.vacuum_element);
        world.write.temperature.fill(300.0);
        for i in 0..gas_count { let cell = world.stride + 1 + i * 3;
            world.write.element[cell] = 3 + i as u16; world.write.mass[cell] = 1.0; }
        multicomponent_flux::rebuild_gas_channels(&mut world, &elements);
        if gas_count > 8 { assert_eq!(world.gas_channel_of_element[11], u8::MAX); }
        let region = Region { min_x:1, min_y:1, max_x:52, max_y:3, ..Default::default() };
        let mut gpu = GpuSimulation::new(world.stride, world.padded_height).unwrap();
        let mut events = EventQueues::default();
        assert!(multicomponent_flux::step_region(&mut world, &elements, &mut events, region, Some(&mut gpu), false).unwrap());
        assert_eq!(gpu.gas.front().unwrap().transport.dimensions(), (51, 2, gas_count));
        for id in 3..(3 + gas_count as u16) {
            let total: f32 = (1..3).flat_map(|y|(1..52).map(move |x|(x,y))).map(|(x,y)| {
                let cell=y*world.stride+x;
                (if world.write.element[cell] == id { world.write.mass[cell] } else { 0.0 }) +
                (if world.mixture_element[cell] == id { world.mixture_mass[cell] } else { 0.0 })
            }).sum();
            assert!((total - 1.0).abs() < 1e-4, "gas {id} mass {total}");
        }
        }
    }

    #[test]
    fn covers_changed_blocks_and_table_replacement() {
        let before = vec![1u16; 9000];
        let mut after = before.clone();
        assert!(material_upload_ranges(&before, &after, false).is_empty());
        assert_eq!(material_upload_ranges(&before, &after, true), vec![(0, 9000)]);
        after[0] = 2;
        after[8999] = 3;
        assert_eq!(material_upload_ranges(&before, &after, false), vec![(0, 4096), (8192, 9000)]);
        after[5000] = 4;
        assert_eq!(material_upload_ranges(&before, &after, false), vec![(0, 9000)]);
    }

    #[test]
    fn many_disjoint_changes_use_one_full_range() {
        let before = vec![0u16; 4096 * 34];
        let mut after = before.clone();
        for block in (0..34).step_by(2) { after[block * 4096] = 1; }
        assert_eq!(material_upload_ranges(&before, &after, false), vec![(0, before.len())]);
    }
}
