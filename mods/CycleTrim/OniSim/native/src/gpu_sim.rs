//! Experimental GPU simulation for the worker. Most operators still pack
//! CPU-owned state and read results back between stages.

use rayon::prelude::*;
use std::{collections::VecDeque, sync::Arc};
use onisim_gpu::{
    disease_diffusion::{Disease as DiffusionDisease, DiseaseElement as DiffusionElement},
    disease_growth::{DiseaseCell, DiseaseElementMetadata, DiseaseGrowthGpu, DiseaseMetadata},
    gas_pressure_faces::{GasCell, GasPressureFacesGpu, GasResult},
    bubble_settling::{Cell as BubbleCell, Material as BubbleMaterial, ResultCell as BubbleResult, BubbleSettlingGpu, PendingBubble},
    phase_transition::{PhaseCell, PhaseDecision, PhaseMaterial, PhaseTransitionGpu},
    gpu_context::GpuContext,
    multicomponent::{MulticomponentGpu, State},
    thermal_diffusion::ThermalDiffusionGpu,
    thermal_enthalpy::ThermalEnthalpyGpu,
};
use crate::tables::{DiseaseTable, ElementTable};
use crate::world::PhysicsWorld;
#[path = "gpu_gas_cpu.rs"]
mod gas_cpu;

#[cfg(test)]
#[path = "gpu_bubble_tests.rs"]
mod bubble_tests;

#[cfg(test)]
#[path = "gpu_disease_tests.rs"]
mod disease_tests;

#[cfg(test)]
use onisim_gpu::disease_diffusion::{ActiveCell as DiffusionActive, DiseaseDiffusionGpu, SnapshotCell as DiffusionSnapshot};

#[path = "gpu_thermal_cpu.rs"]
mod thermal_cpu;
#[cfg(test)]
use onisim_gpu::radiation_region::{PendingRadiation, RadiationCell, RadiationParams, RadiationRegionGpu, RadiationResult};

pub(crate) struct GpuSimulation {
    pub(crate) context: Arc<GpuContext>,
    pub(crate) thermal: ThermalDiffusionGpu,
    region_map: Vec<u32>,
    thermal_cpu_groups: Vec<u32>,
    gas_cpu: Vec<gas_cpu::Workspace>,
    pub(crate) gas_cpu_regions_preferred: bool,
    gas_cpu_seen: bool,
    gas_gpu_seen: bool,
    #[cfg(test)]
    thermal_reference: bool,
    region_bounds: Vec<(i32,i32,i32,i32)>,
    thermal_properties: Vec<[u32; 2]>,
    thermal_flags: Vec<u32>,
    region_map_initialized: bool,
    pub(crate) total_steps: u64,
    gas: VecDeque<GasPipeline>,
    pub(crate) gas_batch_sources: Vec<crate::gas_region_snapshot::GasRegionSnapshot>,
    pub(crate) gas_batch_staging: Vec<crate::multicomponent_flux::GasStaging>,
    pub(crate) gas_staging: crate::multicomponent_flux::GasStaging,
    pub(crate) liquid_staging: crate::multicomponent_flux::LiquidStaging,
    pub(crate) diffusion_solid: Vec<u32>,
    pub(crate) liquid_solid: Vec<u32>,
    liquid_diffusion: VecDeque<MulticomponentGpu>,
    disease_growth: DiseaseGrowthGpu,
    disease_cells: Vec<DiseaseCell>,
    disease_growth_indices: Vec<usize>,
    disease_cpu_announced: bool,
    #[cfg(test)] disease_diffusion: DiseaseDiffusionGpu,
    #[cfg(test)] disease_old: Vec<DiffusionSnapshot>,
    #[cfg(test)] disease_active: Vec<DiffusionActive>,
    phase_transition: PhaseTransitionGpu,
    phase_cells: Vec<PhaseCell>,
    phase_decisions: Vec<PhaseDecision>,
    phase_indices: Vec<usize>,
    phase_materials: Vec<PhaseMaterial>,
    phase_metadata_keys: Vec<[u32; 15]>,
    phase_has_zero_id: bool,
    disease_metadata_ready: bool,
    disease_growth_meta: Vec<DiseaseMetadata>,
    disease_growth_elements: Vec<DiseaseElementMetadata>,
    disease_diffusion_meta: Vec<DiffusionDisease>,
    disease_diffusion_elements: Vec<DiffusionElement>,
    #[cfg(test)] radiation_region: RadiationRegionGpu,
    #[cfg(test)] radiation_cells: Vec<RadiationCell>,
    #[cfg(test)] radiation_result: Vec<RadiationResult>,
    gas_pressure_faces: GasPressureFacesGpu,
    pressure_cells: Vec<GasCell>,
    pressure_result: Vec<GasResult>,
    pub(crate) gas_displacement_unneeded: bool,
    bubble_settling: BubbleSettlingGpu,
    bubble_columns: Vec<usize>,
    bubble_active: Vec<bool>,
    bubble_cells: Vec<BubbleCell>,
    bubble_results: Vec<BubbleResult>,
    bubble_materials: Vec<BubbleMaterial>,
    bubble_y0: usize,
    bubble_height: usize,
    pub(crate) pathfinder: onisim_gpu::gpu_pathfinder::GpuPathfinder,
    pub(crate) resident: Option<onisim_gpu::resident_cells::ResidentCells>,
}

pub(crate) struct GasBatchJob<'a> {
    pub dimensions: (usize, usize, usize),
    pub state: State<'a>,
    pub temperature: &'a [f32],
    pub conductivity: &'a [f32],
    pub mass_output: &'a mut [f32],
    pub temperature_output: &'a mut [f32],
}

struct GasPipeline {
    transport: MulticomponentGpu,
    thermal: ThermalDiffusionGpu,
    enthalpy: ThermalEnthalpyGpu,
}

#[cfg(test)]
pub(crate) struct PendingRadiationRegion {
    pending: PendingRadiation,
    bounds: (usize, usize, usize, usize),
}

#[inline]
fn phase_cell(world: &PhysicsWorld, materials: &[PhaseMaterial], i: usize) -> Option<PhaseCell> {
            let cell = world.read.element[i]; let wall = world.backwall_element[i];
            let enabled = world.read.mass[i] > 0.0 && world.backwall_mass[i] > 0.0
                && wall != u16::MAX && wall != world.vacuum_element && cell != world.vacuum_element
                && wall != world.boundary_element && cell != world.boundary_element;
            let material = &materials[cell as usize];
            let temp = world.write.temperature[i];
            let transition_possible = !(world.write.mass[i] <= 0.0)
                && ((material.low_target != u16::MAX as u32 && !(material.low_temperature - 3.0 <= temp))
                    || (material.high_target != u16::MAX as u32 && !(temp <= material.high_temperature + 3.0)));
            if !enabled && !transition_possible { return None; }
            Some(PhaseCell { element: u32::from(cell), mass: world.write.mass[i],
                temperature: world.write.temperature[i], wall_element: u32::from(wall),
                old_temperature: world.read.temperature[i], old_mass: world.read.mass[i],
                wall_temperature: world.backwall_temperature[i], wall_mass: world.backwall_mass[i],
                insulation: u32::from(world.read.insulation[i]), enabled: u32::from(enabled), padding: [0; 2] })
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
        let disease_growth = DiseaseGrowthGpu::new(context.clone());
        #[cfg(test)]
        let disease_diffusion = DiseaseDiffusionGpu::new(context.clone());
        let phase_transition = PhaseTransitionGpu::new(context.clone());
        #[cfg(test)]
        let radiation_region = RadiationRegionGpu::new(context.clone());
        let gas_pressure_faces = GasPressureFacesGpu::new(context.clone());
        let bubble_settling = BubbleSettlingGpu::new(context.clone());
        let pathfinder = onisim_gpu::gpu_pathfinder::GpuPathfinder::new(context.clone());
        let resident = onisim_gpu::resident_cells::ResidentCells::new(context.clone(), cells).ok();
        Ok(Self {
            context,
            thermal,
            resident,
            region_map: vec![0; cells],
            thermal_cpu_groups: Vec::new(),
            gas_cpu: Vec::new(),
            gas_cpu_regions_preferred: false,
            gas_cpu_seen: false,
            gas_gpu_seen: false,
            #[cfg(test)]
            thermal_reference: false,
            region_bounds: Vec::new(),
            thermal_properties: Vec::new(),
            thermal_flags: Vec::new(),
            region_map_initialized: false,
            total_steps: 0,
            gas: VecDeque::new(),
            gas_staging: Default::default(),
            gas_batch_staging: Vec::new(),
            gas_batch_sources: Vec::new(),
            liquid_staging: Default::default(),
            diffusion_solid: Vec::new(),
            liquid_solid: Vec::new(),
            liquid_diffusion: VecDeque::new(),
            disease_growth,
            disease_cells: Vec::new(),
            disease_growth_indices: Vec::new(),
            disease_cpu_announced: false,
            #[cfg(test)] disease_diffusion,
            #[cfg(test)] disease_old: Vec::new(),
            #[cfg(test)] disease_active: Vec::new(),
            phase_transition,
            phase_cells: Vec::new(),
            phase_decisions: Vec::new(),
            phase_indices: Vec::new(),
            phase_materials: Vec::new(),
            phase_metadata_keys: Vec::new(),
            phase_has_zero_id: false,
            disease_metadata_ready: false,
            disease_growth_meta: Vec::new(),
            disease_growth_elements: Vec::new(),
            disease_diffusion_meta: Vec::new(),
            disease_diffusion_elements: Vec::new(),
            #[cfg(test)] radiation_region,
            #[cfg(test)] radiation_cells: Vec::new(),
            #[cfg(test)] radiation_result: Vec::new(),
            gas_pressure_faces,
            pressure_cells: Vec::new(),
            pressure_result: Vec::new(),
            gas_displacement_unneeded: false,
            bubble_settling,
            bubble_columns: Vec::new(),
            bubble_active: vec![false; width],
            bubble_cells: Vec::new(),
            bubble_results: Vec::new(),
            bubble_materials: Vec::new(),
            bubble_y0: 0,
            bubble_height: 0,
            pathfinder,
        })
    }

    pub(crate) fn begin_move_bubbles(&mut self, world: &PhysicsWorld, table: &ElementTable) -> Result<Option<PendingBubble>, String> {
        self.bubble_active.resize(world.width, false);
        self.bubble_active.fill(false);
        let mut lowest = world.height;
        let mut highest = 0usize;
        for y in 1..=world.height {
            let base = y * world.stride;
            let row = &world.mixture_mass[base + 1..base + world.width + 1];
            for (block, masses) in row.chunks(64).enumerate() {
                // Match the original unordered comparison: NaNs must not be
                // treated as empty, even though they are not positive.
                if masses.iter().all(|&mass| mass <= 0.0) { continue; }
                for (offset, &mass) in masses.iter().enumerate() {
                    if mass <= 0.0 { continue; }
                    let x = block * 64 + offset;
                    let cell = base + x + 1;
                    let secondary = table.elements.get(world.mixture_element[cell] as usize)
                        .ok_or("bubble cell has an invalid secondary element")?.record.state & 3;
                    let primary = table.elements.get(world.write.element[cell] as usize)
                        .ok_or("bubble cell has an invalid primary element")?.record.state & 3;
                    // Match the shader's two passes. Same-phase mixtures can never
                    // move here and must not trigger full-column GPU transfers.
                    let rises = primary == 2 && secondary == 1 && y < world.height;
                    let falls = primary == 1 && secondary == 2 && y > 1
                        && mass > 0.0 && mass.is_finite();
                    if rises || falls {
                        lowest = lowest.min(y); highest = highest.max(y);
                        self.bubble_active[x] = true;
                    }
                }
            }
        }
        self.bubble_columns.clear();
        self.bubble_columns.extend(self.bubble_active.iter().enumerate().filter_map(|(x, &active)| active.then_some(x + 1)));
        if self.bubble_columns.is_empty() { return Ok(None); }
        self.bubble_y0 = lowest.saturating_sub(1).max(1);
        self.bubble_height = highest.saturating_add(1).min(world.height) - self.bubble_y0 + 1;
        let stride = self.bubble_columns.len() + 2;
        let count = stride.checked_mul(self.bubble_height + 2).ok_or("bubble packed grid overflow")?;
        let empty = BubbleCell { element: u32::from(world.vacuum_element), mixture_element: u32::from(world.vacuum_element),
            properties: 0, flags: 0, mass: 0.0, mixture_mass: 0.0, temperature: 0.0, padding: 0 };
        self.bubble_cells.clear(); self.bubble_cells.resize(count, empty);
        for y in self.bubble_y0..self.bubble_y0 + self.bubble_height { for (column, &x) in self.bubble_columns.iter().enumerate() {
            let i = y * world.stride + x;
            let secondary = if world.mixture_mass[i] <= 0.0 { world.vacuum_element } else { world.mixture_element[i] };
            if secondary as usize >= table.len() { return Err("bubble cell has an invalid secondary element".into()); }
            self.bubble_cells[(y - self.bubble_y0 + 1) * stride + column + 1] = BubbleCell {
                element: u32::from(world.write.element[i]), mixture_element: u32::from(secondary),
                properties: u32::from(world.write.properties[i]), flags: 0, mass: world.write.mass[i],
                mixture_mass: world.mixture_mass[i], temperature: world.write.temperature[i], padding: 0,
            };
        }}
        self.bubble_materials.clear();
        self.bubble_materials.extend(table.elements.iter().map(|e| BubbleMaterial {
            phase: u32::from(e.record.state & 3), heat: e.record.specific_heat_capacity,
        }));
        self.bubble_settling.begin(self.bubble_columns.len() as u32, self.bubble_height as u32, u32::from(world.vacuum_element),
            &self.bubble_cells, &self.bubble_materials).map(Some)
    }

    pub(crate) fn finish_move_bubbles(&mut self, world: &mut PhysicsWorld, events: &mut crate::events::EventQueues,
        pending: PendingBubble) -> Result<(), String> {
        self.bubble_results.resize(self.bubble_columns.len() * self.bubble_height, BubbleResult {
            element: 0, mixture_element: 0, flags: 0, mass: 0.0, mixture_mass: 0.0, temperature: 0.0 });
        pending.finish(&mut self.bubble_results)?;
        for y in self.bubble_y0..self.bubble_y0 + self.bubble_height { for (column, &x) in self.bubble_columns.iter().enumerate() {
            let result = self.bubble_results[(y - self.bubble_y0) * self.bubble_columns.len() + column];
            if result.flags & 1 == 0 { continue; }
            let i = y * world.stride + x;
            world.write.element[i] = result.element as u16;
            world.write.mass[i] = result.mass;
            world.write.temperature[i] = result.temperature;
            world.mixture_element[i] = result.mixture_element as u16;
            world.mixture_mass[i] = result.mixture_mass;
            crate::fluid_helpers::notify(world, events, i);
        }}
        Ok(())
    }

    /// Reuse transition metadata across steps unless a relevant element field
    /// changes. Comparing actual values also handles tables replaced in place.
    pub(crate) fn begin_step(&mut self, table: &ElementTable) {
        if self.gas_cpu_seen || self.gas_gpu_seen {
            let prefer_cpu = self.gas_cpu_seen && !self.gas_gpu_seen;
            if prefer_cpu && !self.gas_cpu_regions_preferred {
                // Immediate host work needs no speculative source snapshots.
                self.gas_batch_sources.clear();
                self.gas_batch_staging.clear();
            }
            self.gas_cpu_regions_preferred = prefer_cpu;
        }
        self.gas_cpu_seen = false;
        self.gas_gpu_seen = false;
        self.disease_metadata_ready = false;
        let key = |r: &crate::tables::ElementRecord| [
            r.id as u32, u32::from(r.low_transition), u32::from(r.high_transition),
            r.low_ore_id as u32, r.high_ore_id as u32,
            r.low_temperature.to_bits(), r.high_temperature.to_bits(),
            r.low_ore_conversion.to_bits(), r.high_ore_conversion.to_bits(),
            r.thermal_conductivity.to_bits(), r.specific_heat_capacity.to_bits(), u32::from(r.state & 3),
            r.surface_area_solid.to_bits(), r.surface_area_liquid.to_bits(), r.surface_area_gas.to_bits(),
        ];
        if self.phase_has_zero_id == table.has_zero_id && self.phase_metadata_keys.len() == table.len()
            && self.phase_metadata_keys.iter().zip(&table.elements).all(|(old, element)| *old == key(&element.record)) {
            return;
        }
        self.phase_metadata_keys.clear();
        self.phase_metadata_keys.extend(table.elements.iter().map(|element| key(&element.record)));
        self.phase_has_zero_id = table.has_zero_id;
        let mut indices = std::collections::HashMap::with_capacity(table.len());
        for (i, element) in table.elements.iter().enumerate() { indices.entry(element.record.id).or_insert(i as u16); }
        let ore = |id: i32| -> u32 {
            if id as u32 == 0x2d39_bf75 || (id == 0 && !table.has_zero_id) { 65535 }
            else { u32::from(*indices.get(&id).unwrap_or(&u16::MAX)) }
        };
        self.phase_materials.clear();
        self.phase_materials.extend(table.elements.iter().map(|item| {
            let r = &item.record;
            PhaseMaterial { low_target: u32::from(r.low_transition), high_target: u32::from(r.high_transition),
                low_ore: ore(r.low_ore_id), high_ore: ore(r.high_ore_id), low_temperature: r.low_temperature,
                high_temperature: r.high_temperature, low_conversion: r.low_ore_conversion, high_conversion: r.high_ore_conversion,
                conductivity: r.thermal_conductivity, specific_heat: r.specific_heat_capacity, phase: u32::from(r.state & 3), padding0: 0,
                surface_solid: r.surface_area_solid, surface_liquid: r.surface_area_liquid, surface_gas: r.surface_area_gas, padding1: 0 }
        }));
    }

    fn prepare_disease_metadata(&mut self, table: &DiseaseTable) {
        if self.disease_metadata_ready { return; }
        let values = |range: &crate::tables::RangeInfo| [range.min_viable, range.min_growth, range.max_growth, range.max_viable];
        let same_float = |a: f32, b: f32| a.to_bits() == b.to_bits();
        let header_matches = self.disease_growth_meta.len() == table.diseases.len()
            && self.disease_diffusion_meta.len() == table.diseases.len()
            && table.diseases.iter().zip(self.disease_growth_meta.iter().zip(&self.disease_diffusion_meta))
                .all(|(d, (growth, diffusion))| {
                    growth.temperature_range.iter().zip(values(&d.temperature_range)).all(|(&a, b)| same_float(a, b))
                        && growth.temperature_half_lives.iter().zip(values(&d.temperature_half_lives)).all(|(&a, b)| same_float(a, b))
                        && same_float(growth.radiation_kill_rate, d.radiation_kill_rate)
                        && same_float(diffusion.strength, d.strength)
                });
        let element_count = table.diseases.iter().map(|d| d.elements.len()).sum::<usize>();
        let element_matches = self.disease_growth_elements.len() == element_count
            && self.disease_diffusion_elements.len() == element_count
            && table.diseases.iter().flat_map(|d| &d.elements)
                .zip(self.disease_growth_elements.iter().zip(&self.disease_diffusion_elements))
                .all(|(e, (growth, diffusion))| {
                    same_float(growth.min_count_per_kg, e.min_count_per_kg)
                        && same_float(growth.max_count_per_kg, e.max_count_per_kg)
                        && same_float(growth.under_population_death_rate, e.under_population_death_rate)
                        && same_float(growth.over_population_half_life, e.over_population_half_life)
                        && same_float(growth.population_half_life, e.population_half_life)
                        && same_float(diffusion.diffusion_scale, e.diffusion_scale)
                        && diffusion.min_diffusion_count == e.min_diffusion_count
                        && diffusion.min_infestation_tick == u32::from(e.min_infestation_tick)
                });
        if header_matches && element_matches {
            self.disease_metadata_ready = true;
            return;
        }
        self.disease_growth_meta.clear();
        self.disease_growth_meta.extend(table.diseases.iter().map(|d| DiseaseMetadata {
            temperature_range: values(&d.temperature_range), temperature_half_lives: values(&d.temperature_half_lives),
            radiation_kill_rate: d.radiation_kill_rate, padding: [0.0; 3],
        }));
        self.disease_diffusion_meta.clear();
        self.disease_diffusion_meta.extend(table.diseases.iter().map(|d| DiffusionDisease { strength: d.strength }));
        self.disease_growth_elements.clear(); self.disease_diffusion_elements.clear();
        for element in table.diseases.iter().flat_map(|d| d.elements.iter()) {
            self.disease_growth_elements.push(DiseaseElementMetadata {
                min_count_per_kg: element.min_count_per_kg, max_count_per_kg: element.max_count_per_kg,
                under_population_death_rate: element.under_population_death_rate,
                over_population_half_life: element.over_population_half_life, population_half_life: element.population_half_life,
                padding: [0.0; 3],
            });
            self.disease_diffusion_elements.push(DiffusionElement { diffusion_scale: element.diffusion_scale,
                min_diffusion_count: element.min_diffusion_count, min_infestation_tick: u32::from(element.min_infestation_tick), padding: 0 });
        }
        self.disease_metadata_ready = true;
    }

    /// Use face-based pressure for gas or homogeneous liquid regions.
    /// Other regions retain the ordered CPU sweep while migration continues.
    pub(crate) fn pressure_region(&mut self, world: &mut PhysicsWorld, table: &ElementTable,
        events: &mut crate::events::EventQueues, region: crate::world::Region, fluid_phase: u8) -> Result<bool, String> {
        self.gas_displacement_unneeded = false;
        if region.min_x < 1 || region.min_y < 1
            || region.max_x <= region.min_x || region.max_y <= region.min_y { return Ok(false); }
        let x0 = region.min_x as usize - 1; let y0 = region.min_y as usize - 1;
        let x1 = region.max_x as usize; let y1 = region.max_y as usize;
        if x1 >= world.stride || y1 >= world.padded_height { return Ok(false); }
        let width = x1 - x0 + 1; let height = y1 - y0 + 1;
        if width * height < 8192 && !cfg!(test) { return Ok(false); }
        self.pressure_cells.clear();
        let mut first_id = None;
        let mut multiple_species = false;
        let mut needs_head = false;
        let mut primary_gas = None;
        let mut multiple_primary_gases = false;
        for y in y0..=y1 { for x in x0..=x1 {
            let i = y * world.stride + x; let mut id = world.read.element[i];
            let mut phase = table.elements[id as usize].record.state & 3;
            if fluid_phase == 1 && phase == 1 {
                if primary_gas.is_some_and(|previous| previous != id) { multiple_primary_gases = true; }
                else if primary_gas.is_none() { primary_gas = Some(id); }
            }
            let blocking_bit = if fluid_phase == 2 { 2 } else { 1 };
            let blocked = world.read.properties[i] & blocking_bit != 0 || phase == 3;
            let mut mass = world.read.mass[i];
            // This pass writes absolute projected masses, not deltas. In a
            // halo the active state may already have changed since the read
            // snapshot, so using that snapshot would discard earlier work.
            if !blocked && (world.write.element[i] != id || world.write.mass[i] != mass) {
                return Ok(false);
            }
            let mut gas_element = world.vacuum_element;
            let mut gas_mass = 0.0;
            let mut gas_heat = 0.0;
            if !blocked && world.mixture_mass[i] != 0.0 {
                let mixed_id = world.mixture_element[i];
                let Some(mixed) = table.elements.get(mixed_id as usize) else { return Ok(false); };
                let mixed_phase = mixed.record.state & 3;
                if !world.mixture_mass[i].is_finite() || world.mixture_mass[i] <= 0.0
                    || !((phase == 1 && mixed_phase == 2) || (phase == 2 && mixed_phase == 1)) {
                    return Ok(false);
                }
                if phase != fluid_phase {
                    gas_element = id; gas_mass = mass;
                    gas_heat = table.elements[id as usize].record.specific_heat_capacity;
                    id = mixed_id; mass = world.mixture_mass[i]; phase = fluid_phase;
                } else {
                    gas_element = mixed_id; gas_mass = world.mixture_mass[i];
                    gas_heat = mixed.record.specific_heat_capacity;
                }
                if !gas_heat.is_finite() || gas_heat <= 0.0 || !gas_mass.is_finite() || gas_mass <= 0.0 { return Ok(false); }
            }
            // Represent the liquid in a gas-pressure pass as a stationary
            // component. Its unoccupied volume is available to incoming gas.
            if !blocked && fluid_phase == 1 && phase == 2 && world.mixture_mass[i] == 0.0 {
                if !mass.is_finite() || mass <= 0.0 {
                    return Ok(false);
                }
                gas_element = id; gas_mass = mass;
                gas_heat = table.elements[id as usize].record.specific_heat_capacity;
                if !gas_heat.is_finite() || gas_heat <= 0.0 { return Ok(false); }
                id = world.vacuum_element; phase = 0; mass = 0.0;
            }
            let mut available_volume = 1.0_f32;
            if !blocked && fluid_phase == 1 && gas_mass > 0.0 {
                let liquid = &table.elements[gas_element as usize].record;
                if !liquid.max_mass.is_finite() || liquid.max_mass <= 0.0 { return Ok(false); }
                available_volume = (1.0 - gas_mass / liquid.max_mass).clamp(0.0, 1.0);
            }
            let record = &table.elements[id as usize].record;
            if !blocked {
                if (phase != 0 && phase != fluid_phase && !(fluid_phase == 2 && phase == 1)) || id == world.sink_element
                    || world.read.disease_idx[i] != u8::MAX || world.write.disease_idx[i] != u8::MAX
                    || world.read.disease_count[i] != 0 || world.write.disease_count[i] != 0
                    || !mass.is_finite() || mass < 0.0
                    || !world.write.temperature[i].is_finite() || !record.flow.is_finite() || record.flow < 0.0 {
                    return Ok(false);
                }
                if phase == 2 && (!record.max_mass.is_finite() || record.max_mass <= 0.0
                    || !record.viscosity.is_finite() || record.viscosity < 0.0) { return Ok(false); }
                if phase != 0 && (!record.specific_heat_capacity.is_finite() || record.specific_heat_capacity <= 0.0) { return Ok(false); }
                if phase == 0 && (id != world.vacuum_element || mass != 0.0) { return Ok(false); }
                if phase == fluid_phase {
                    if first_id.is_some_and(|previous| previous != id) { multiple_species = true; }
                    else if first_id.is_none() { first_id = Some(id); }
                }
            }
            self.pressure_cells.push(GasCell { element: u32::from(id), phase: u32::from(phase), blocked: u32::from(blocked), padding: 0,
                mass, temperature: world.write.temperature[i],
                coefficient: if fluid_phase == 2 { record.specific_heat_capacity } else { record.flow }, padding1: 0.0,
                max_mass: if fluid_phase == 1 { available_volume } else { record.max_mass }, viscosity: record.viscosity,
                min_horizontal_flow: record.min_horizontal_flow, min_vertical_flow: record.min_vertical_flow,
                gas_element: u32::from(gas_element), gas_mass, gas_heat,
                phase_heat: if phase == 0 { 0.0 } else { record.specific_heat_capacity } });
        }}
        if first_id.is_none() { return Ok(false); }
        if fluid_phase == 2 {
            'head: for y in 1..height - 1 { for x in 1..width - 1 {
                let current = self.pressure_cells[y * width + x];
                if current.blocked != 0 || current.phase != 2 || current.mass <= 0.0 || current.viscosity <= 0.0 { continue; }
                let above = self.pressure_cells[(y + 1) * width + x];
                if above.blocked == 0 && (above.phase != 2 || above.element == current.element)
                    && (above.phase != 2 || above.mass < current.max_mass) {
                    needs_head = true;
                    break 'head;
                }
            }}
        }
        if fluid_phase == 2 && multiple_species {
            // Different liquids may share a region as long as no gas/vacuum
            // target can receive two distinct liquids in this pass. Such a
            // target would need a third component and cannot be projected to
            // the current two-slot cell representation without losing mass.
            for y in 0..height { for x in 0..width {
                let current = self.pressure_cells[y * width + x];
                if current.blocked != 0 || current.phase == 2 { continue; }
                let mut incoming = None;
                for (dx, dy) in [(-1isize, 0isize), (1, 0), (0, -1), (0, 1)] {
                    let nx = x as isize + dx; let ny = y as isize + dy;
                    if nx < 0 || ny < 0 || nx >= width as isize || ny >= height as isize { continue; }
                    let other = self.pressure_cells[ny as usize * width + nx as usize];
                    if other.blocked != 0 || other.phase != 2 || other.mass <= 0.0 { continue; }
                    if incoming.is_some_and(|id| id != other.element) { return Ok(false); }
                    incoming = Some(other.element);
                }
            }}
        }
        if fluid_phase == 1 && multiple_species {
            // A vacuum cell has one primary slot. When multiple species enter,
            // they are recorded and handled via multicomponent mixtures.
        }
        self.pressure_result.resize(self.pressure_cells.len(), GasResult { element: 0, changed: 0, mass: 0.0,
            temperature: 0.0, flow_left: 0.0, flow_right: 0.0, flow_up: 0.0, flow_down: 0.0,
            mixture_element: 0, mixture_mass: 0.0 });
        self.gas_pressure_faces.run(width as u32, height as u32, u32::from(world.vacuum_element), u32::from(fluid_phase), world.scan_direction, needs_head,
            &self.pressure_cells, &mut self.pressure_result)?;
        let mut n = 0;
        for y in y0..=y1 { for x in x0..=x1 {
            let i = y * world.stride + x; let r = self.pressure_result[n]; n += 1;
            if r.changed == 0 { continue; }
            let previous = world.write.element[i];
            world.write.element[i] = r.element as u16; world.write.mass[i] = r.mass; world.write.temperature[i] = r.temperature;
            world.mixture_element[i] = r.mixture_element as u16; world.mixture_mass[i] = r.mixture_mass;
            world.write.disease_infestation[i] = 0; world.write.disease_growth[i] = 0.0;
            world.flow[i][0] += r.flow_left; world.flow[i][1] += r.flow_right;
            world.flow[i][2] += r.flow_up; world.flow[i][3] += r.flow_down;
            if previous != world.write.element[i] || r.mixture_mass > 0.0 { crate::fluid_helpers::notify(world, events, i); }
        }}
        // The native displacement traversal may extend down to row 3 even
        // for higher regions; only use this region's species inventory when
        // the input rectangle covers that entire traversal and its halo.
        self.gas_displacement_unneeded = fluid_phase == 1 && region.min_y <= 3 && !multiple_primary_gases;
        Ok(true)
    }

    #[cfg(test)]
    pub(crate) fn begin_radiation_region(&mut self, world: &PhysicsWorld, elements: &ElementTable,
        diseases: &DiseaseTable, region: crate::world::Region) -> Result<Option<PendingRadiationRegion>, String> {
        let (min_x, max_x, min_y, max_y) = (region.min_x as usize, region.max_x as usize, region.min_y as usize, region.max_y as usize);
        if min_x >= max_x || min_y > max_y { return Ok(None); }
        if max_x >= world.stride || max_y >= world.padded_height { return Err("radiation region out of bounds".into()); }
        let width = max_x - min_x + 1; let height = max_y - min_y + 1;
        if width * height < 8192 && !cfg!(test) { return Ok(None); }
        self.radiation_cells.clear();
        for y in min_y..=max_y { for x in min_x..=max_x {
            let i = y * world.stride + x;
            let record = &elements.elements[world.write.element[i] as usize].record;
            self.radiation_cells.push(RadiationCell { mass: world.write.mass[i], radiation: world.write.radiation[i],
                absorption: record.radiation_absorption, emission: record.radiation_per_1000_mass,
                properties: u32::from(world.write.properties[i]), disease: u32::from(world.write.disease_idx[i]),
                disease_count: world.write.disease_count[i], padding: 0 });
        }}
        let p = world.radiation_params;
        let nuclear = diseases.diseases.iter().rposition(|d| d.hash as u32 == 0xd49f_77d6).map_or(255, |i| i as u32);
        let pending = self.radiation_region.begin(RadiationParams { width: width as u32, height: height as u32, nuclear, padding: 0,
            dose: p.dose_scale, cosmic: region.cosmic_radiation, absorption_solid: p.absorption_solid,
            absorption_mass_scale: p.absorption_mass_scale, mass_scale: p.mass_scale, absorption_base: p.absorption_base,
            padding0: 0.0, padding1: 0.0 }, &self.radiation_cells)?;
        Ok(Some(PendingRadiationRegion { pending, bounds: (min_x, max_x, min_y, max_y) }))
    }

    #[cfg(test)]
    pub(crate) fn finish_radiation_region(&mut self, world: &mut PhysicsWorld, work: PendingRadiationRegion) -> Result<(), String> {
        let (min_x, max_x, min_y, max_y) = work.bounds;
        let count = (max_x - min_x + 1) * (max_y - min_y + 1);
        self.radiation_result.resize(count, RadiationResult { radiation: 0.0, transmission: 0.0 });
        work.pending.finish(&mut self.radiation_result)?;
        let mut n = 0;
        for y in min_y..=max_y { for x in min_x..=max_x {
            let i = y * world.stride + x; let result = self.radiation_result[n]; n += 1;
            world.write.radiation[i] = result.radiation;
            if x < max_x { world.radiation_transmission[i] = result.transmission; }
        }}
        Ok(())
    }

    pub(crate) fn transition_region(&mut self, world: &mut PhysicsWorld, table: &ElementTable,
        events: &mut crate::events::EventQueues, region: crate::world::Region) -> Result<bool, String> {
        self.phase_cells.clear();
        self.phase_indices.clear();
        let work = ((region.max_x - region.min_x).max(0) as usize)
            .saturating_mul((region.max_y - region.min_y).max(0) as usize);
        if let Some(pool) = crate::parallel::pool(work) {
            let environment = crate::float_env::FloatEnv::capture();
            let materials = &self.phase_materials;
            let possible = pool.install(|| (region.min_y..region.max_y).into_par_iter()
                .with_min_len(8).any(|y| {
                    let _environment = environment.install();
                    (region.min_x..region.max_x).any(|x|
                        phase_cell(world, materials, y as usize * world.stride + x as usize).is_some())
                }));
            if !possible { return Ok(false); }
        }
        for y in region.min_y..region.max_y { for x in region.min_x..region.max_x {
            let i = y as usize * world.stride + x as usize;
            if let Some(cell) = phase_cell(world, &self.phase_materials, i) {
                self.phase_indices.push(i);
                self.phase_cells.push(cell);
            }
        }}
        if self.phase_cells.is_empty() { return Ok(false); }
        self.phase_decisions.resize(self.phase_cells.len(), PhaseDecision { target_id: 65535, ore: 65535, conversion: 0.0, temperature: 0.0,
            wall_temperature: 0.0, wall_changed: 0, wall_transition: 0, cell_temperature: 0.0 });
        self.phase_transition.select(&self.phase_cells, &self.phase_materials, &mut self.phase_decisions)?;
        for (&i, &result) in self.phase_indices.iter().zip(&self.phase_decisions) {
            if result.wall_changed != 0 {
                world.write.temperature[i] = result.cell_temperature;
                world.backwall_temperature[i] = result.wall_temperature;
                if result.wall_transition != 0 {
                    if let Some(game_cell) = crate::fluid_helpers::game_index(world, i) { events.backwall_should_transition(game_cell); }
                }
            }
        }
        for (&i, &result) in self.phase_indices.iter().zip(&self.phase_decisions) {
            if result.target_id != u16::MAX as u32 {
                crate::transition::apply_selected_transition(world, table, events, i, result.target_id as u16,
                    result.ore as u16, result.conversion, result.temperature);
            }
        }
        Ok(self.phase_decisions.iter().any(|result|
            result.wall_changed != 0 || result.target_id != u16::MAX as u32))
    }

    pub(crate) fn diffuse_diseases(&mut self, world: &mut PhysicsWorld, elements: &ElementTable,
        table: &DiseaseTable, region: crate::world::Region) -> Result<(), String> {
        let (min_x, max_x, min_y, max_y) = (region.min_x as usize, region.max_x as usize, region.min_y as usize, region.max_y as usize);
        if min_x >= max_x || min_y >= max_y { return Ok(()); }
        if max_x >= world.stride || max_y >= world.padded_height { return Err("disease diffusion region out of bounds".into()); }
        if !self.disease_cpu_announced {
            eprintln!("[CycleTrim OniSim] Disease diffusion uses resident CPU arrays (no GPU round trip)");
            self.disease_cpu_announced = true;
        }
        if (min_y..=max_y).all(|y| {
            let start = y * world.stride + min_x;
            world.read.disease_idx[start..=start + max_x - min_x].iter().all(|&id| id == u8::MAX)
        }) { return Ok(()); }
        let mut events = crate::events::EventQueues::default();
        crate::simulation_step::disease_diffusion(world, elements, table, &mut events, region)
            .map_err(|error| error.message)
    }

    #[cfg(test)]
    fn diffuse_diseases_gpu_reference(&mut self, world: &mut PhysicsWorld, elements: &ElementTable,
        table: &DiseaseTable, region: crate::world::Region) -> Result<(), String> {
        let (min_x, max_x, min_y, max_y) = (region.min_x as usize, region.max_x as usize, region.min_y as usize, region.max_y as usize);
        if min_x >= max_x || min_y >= max_y { return Ok(()); }
        if max_x >= world.stride || max_y >= world.padded_height { return Err("disease diffusion region out of bounds".into()); }
        let width = max_x - min_x + 1;
        let height = max_y - min_y + 1;
        // Most regions have no germs. Check the compact ID field before
        // packing two wider snapshots and touching element metadata.
        if (min_y..=max_y).all(|y| {
            let start = y * world.stride + min_x;
            world.read.disease_idx[start..start + width].iter().all(|&id| id == u8::MAX)
        }) { return Ok(()); }
        self.disease_old.clear();
        self.disease_active.clear();
        for y in min_y..=max_y { for x in min_x..=max_x {
            let i = y * world.stride + x;
            let element = world.read.element[i];
            self.disease_old.push(DiffusionSnapshot {
                element: u32::from(element), phase: u32::from(elements.elements[element as usize].record.state & 3),
                disease: u32::from(world.read.disease_idx[i]), infestation: u32::from(world.read.disease_infestation[i]),
                count: world.read.disease_count[i], padding: [0; 3],
            });
            self.disease_active.push(DiffusionActive {
                disease: u32::from(world.write.disease_idx[i]), count: world.write.disease_count[i],
                infestation: u32::from(world.write.disease_infestation[i]), growth: world.write.disease_growth[i],
            });
        }}
        self.prepare_disease_metadata(table);
        self.disease_diffusion.run(width, height, &self.disease_old, &mut self.disease_active,
            &self.disease_diffusion_meta, &self.disease_diffusion_elements, table.element_count)?;
        let mut local = 0;
        for y in min_y..=max_y { for x in min_x..=max_x {
            let i = y * world.stride + x;
            let cell = self.disease_active[local]; local += 1;
            world.write.disease_idx[i] = cell.disease as u8;
            world.write.disease_count[i] = cell.count;
            world.write.disease_infestation[i] = cell.infestation as u8;
            world.write.disease_growth[i] = cell.growth;
        }}
        Ok(())
    }

    pub(crate) fn grow_diseases(&mut self, world: &mut PhysicsWorld, table: &DiseaseTable,
        bounds: (usize, usize, usize, usize)) -> Result<(), String> {
        let (min_x, max_x, min_y, max_y) = bounds;
        if max_x > world.stride || max_y > world.padded_height || min_x > max_x || min_y > max_y {
            return Err("disease region is out of range".into());
        }
        self.disease_cells.clear();
        self.disease_growth_indices.clear();
        let work = (max_x - min_x).saturating_mul(max_y - min_y);
        if let Some(pool) = crate::parallel::pool(work) {
            let clean = pool.install(|| (min_y..max_y).into_par_iter().with_min_len(8).all(|y| {
                let range = y * world.stride + min_x..y * world.stride + max_x;
                world.write.disease_idx[range.clone()].iter().all(|&id| id == u8::MAX)
                    && world.write.disease_count[range.clone()].iter().all(|&count| count == 0)
                    && world.write.disease_infestation[range.clone()].iter().all(|&value| value == 1)
                    && world.write.disease_growth[range].iter().all(|value| value.to_bits() == 0)
            }));
            if clean { return Ok(()); }
        }
        // Gas projection can reset clean cells' infestation to zero. Their
        // growth result is still just cleanup, even with nonzero stale growth.
        // Avoid packing and dispatching the whole rectangle for that cleanup.
        let inactive_row = |y: usize| {
            let range = y * world.stride + min_x..y * world.stride + max_x;
            world.write.disease_idx[range.clone()].iter().all(|&id| id == u8::MAX)
                && world.write.disease_count[range].iter().all(|&count| count < 1)
        };
        let inactive = if let Some(pool) = crate::parallel::pool(work) {
            pool.install(|| (min_y..max_y).into_par_iter().with_min_len(8).all(inactive_row))
        } else {
            (min_y..max_y).all(inactive_row)
        };
        if inactive {
            return crate::disease::post_process_bounds(world, table, min_x, max_x, min_y, max_y)
                .map_err(|error| error.message);
        }
        for y in min_y..max_y {
            let range = y * world.stride + min_x..y * world.stride + max_x;
            if world.write.disease_idx[range.clone()].iter().all(|&id| id == u8::MAX)
                && world.write.disease_count[range.clone()].iter().all(|&count| count == 0)
                && world.write.disease_infestation[range.clone()].iter().all(|&value| value == 1)
                && world.write.disease_growth[range].iter().all(|value| value.to_bits() == 0) { continue; }
            for x in min_x..max_x {
            let i = y * world.stride + x;
            if world.write.disease_idx[i] == u8::MAX && world.write.disease_count[i] == 0
                && world.write.disease_infestation[i] == 1 && world.write.disease_growth[i].to_bits() == 0 { continue; }
            self.disease_growth_indices.push(i);
            self.disease_cells.push(DiseaseCell {
                element: u32::from(world.write.element[i]), disease: u32::from(world.write.disease_idx[i]),
                count: world.write.disease_count[i], infestation: u32::from(world.write.disease_infestation[i]),
                temperature: world.write.temperature[i], mass: world.write.mass[i], radiation: world.write.radiation[i],
                growth: world.write.disease_growth[i],
            });
        }}
        if self.disease_cells.is_empty() { return Ok(()); }
        if self.disease_cells.len() < 8192 && !cfg!(test) {
            return crate::disease::post_process_bounds(
                world, table, min_x, max_x, min_y, max_y,
            ).map_err(|e| e.message);
        }
        self.prepare_disease_metadata(table);
        self.disease_growth.run_cells(&mut self.disease_cells, &self.disease_growth_meta,
            &self.disease_growth_elements, table.element_count, world.radiation_enabled)?;
        for (&i, &cell) in self.disease_growth_indices.iter().zip(&self.disease_cells) {
            world.write.disease_idx[i] = cell.disease as u8;
            world.write.disease_count[i] = cell.count;
            world.write.disease_infestation[i] = cell.infestation as u8;
            world.write.disease_growth[i] = cell.growth;
        }
        Ok(())
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

    /// Independent worlds retain separate uniforms and ping-pong buffers even
    /// when their dimensions match. All kernels and copies share one submission.
    pub(crate) fn step_gas_batch(&mut self, jobs: &mut [GasBatchJob<'_>], diffuse: bool, dt: f32) -> Result<(), String> {
        if jobs.is_empty() { return Ok(()); }
        if self.try_cpu_gas(jobs, diffuse, dt)? { return Ok(()); }
        self.gas_gpu_seen = true;
        let mut pipelines = Vec::with_capacity(jobs.len());
        for job in jobs.iter() {
            let (width, height, channels) = job.dimensions;
            let pipeline = if let Some(index) = self.gas.iter().position(|p| p.transport.dimensions() == job.dimensions) {
                self.gas.remove(index).ok_or("GPU gas cache entry disappeared")?
            } else {
                GasPipeline {
                    transport: onisim_gpu::pollster::block_on(MulticomponentGpu::new_with_context(self.context.clone(), width, height, channels))?,
                    thermal: onisim_gpu::pollster::block_on(ThermalDiffusionGpu::new_with_context(self.context.clone(), width, height))?,
                    enthalpy: ThermalEnthalpyGpu::new(self.context.clone()),
                }
            };
            pipelines.push(pipeline);
        }
        let started = std::env::var_os("CYCLETRIM_GPU_PROFILE").is_some().then(std::time::Instant::now);
        let result = (|| {
            let context = &self.context;
            let environment = crate::float_env::FloatEnv::capture();
            let encode = |(job, pipeline): (&GasBatchJob<'_>, &mut GasPipeline)| {
                let _environment = environment.install();
                let mut encoder = context.command_encoder();
                let (width, height, channels) = job.dimensions;
                let cells = width * height;
                pipeline.transport.upload_for_temperature(&job.state, job.temperature)?;
                pipeline.thermal.upload_temperature(job.temperature)?;
                pipeline.enthalpy.encode(&mut encoder, cells, channels, pipeline.thermal.temperature_buffer(),
                    pipeline.transport.current_mass_buffer(), pipeline.transport.species_heat_buffer(),
                    pipeline.transport.current_enthalpy_buffer())?;
                pipeline.transport.encode_diffuse_and_generate_density_velocity(&mut encoder, diffuse && channels > 1)?;
                pipeline.enthalpy.encode_conductivity(&mut encoder, cells, channels, pipeline.transport.current_mass_buffer(),
                    job.conductivity, pipeline.thermal.conductivity_buffer())?;
                pipeline.transport.encode_bounded_resident(&mut encoder, dt)?;
                let (temperature, total_mass, heat_capacity) = pipeline.thermal.state_buffers();
                pipeline.enthalpy.encode_prepare(&mut encoder, cells, channels, pipeline.transport.current_mass_buffer(),
                    pipeline.transport.current_enthalpy_buffer(), pipeline.transport.species_heat_buffer(),
                    temperature, total_mass, heat_capacity)?;
                pipeline.thermal.encode_resident(&mut encoder, dt * 4.0, 0.25)?;
                Ok::<_, String>(encoder.finish())
            };
            let work = jobs.iter().map(|job| job.dimensions.0 * job.dimensions.1).sum();
            let commands = if let Some(pool) = crate::parallel::pool(work) {
                pool.install(|| jobs.par_iter().zip(pipelines.par_iter_mut()).map(encode).collect::<Result<Vec<_>, _>>())?
            } else {
                jobs.iter().zip(pipelines.iter_mut()).map(encode).collect::<Result<Vec<_>, _>>()?
            };
            let encoder = context.command_encoder();
            let encoded = started.map(|t| t.elapsed().as_secs_f64() * 1000.0);
            let mut readbacks: Vec<_> = jobs.iter_mut().zip(&mut pipelines).map(|(job, pipeline)|
                onisim_gpu::multicomponent::MassTemperatureReadback {
                    operator: &mut pipeline.transport,
                    temperature: pipeline.thermal.temperature_buffer(),
                    mass_output: job.mass_output,
                    temperature_output: job.temperature_output,
                }).collect();
            let result = MulticomponentGpu::readback_mass_and_temperature_commands_after(commands, encoder, &mut readbacks);
            if let (Some(started), Some(encoded)) = (started, encoded) {
                eprintln!("[CycleTrim OniSim] gas chain encode={encoded:.3}ms submit_wait_readback={:.3}ms",
                    started.elapsed().as_secs_f64() * 1000.0 - encoded);
            }
            result
        })();
        // Never retain an incomplete encoded chain after an upload/submit error.
        if result.is_ok() {
            for pipeline in pipelines.into_iter().rev() { self.gas.push_front(pipeline); }
            let estimate = |p: &GasPipeline| {
                let (w,h,c) = p.transport.dimensions();
                w.saturating_mul(h).saturating_mul(c.saturating_mul(24).saturating_add(128))
            };
            while self.gas.len() > 1 && (self.gas.len() > 8 || self.gas.iter().map(estimate).sum::<usize>() > 96 * 1024 * 1024) {
                self.gas.pop_back();
            }
        }
        result
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
        if sparse.is_none() && projected_output.is_none() {
            let mut jobs = [GasBatchJob {
                dimensions: (width, height, channels),
                state: State { mass: state.mass, enthalpy: state.enthalpy,
                    velocity_x: state.velocity_x, velocity_y: state.velocity_y,
                    solid: state.solid, species: state.species },
                temperature, conductivity: species_conductivity,
                mass_output, temperature_output,
            }];
            if self.try_cpu_gas(&mut jobs, diffuse, dt)? { return Ok(true); }
        }
        self.gas_gpu_seen = true;
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
            let thermal = onisim_gpu::pollster::block_on(ThermalDiffusionGpu::new_with_context(
                self.context.clone(), width, height,
            ))?;
            let enthalpy = ThermalEnthalpyGpu::new(self.context.clone());
            self.gas.push_front(GasPipeline { transport, thermal, enthalpy });
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
        // Gas density must be free to expand into vacuum. The CPU path uses
        // this same density-gradient velocity; an incompressible projection
        // cancels its divergence and adds a solver plus a host/GPU barrier.
        // Bounded face flux already guarantees nonnegative transport.
        pipeline.transport.encode_bounded_resident(&mut encoder, dt)?;
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
            eprintln!("[CycleTrim OniSim] GPU gas profile {width}x{height}x{channels}: upload/setup={:.3}ms bounded-transport-encode={:.3}ms submit+GPU-wait+readback={:.3}ms", uploaded.as_secs_f64()*1000.0, (projected-uploaded).as_secs_f64()*1000.0, (started.elapsed()-projected).as_secs_f64()*1000.0);
        }
        static LOG_FIRST_GAS_CHAIN: std::sync::Once = std::sync::Once::new();
        LOG_FIRST_GAS_CHAIN.call_once(|| {
            eprintln!("[CycleTrim OniSim] GPU gas density transport and heat completed in one submission (region {width}x{height}, {channels} channels)");
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

        let bounds_changed = self.region_bounds.len() != world.regions.len()
            || self.region_bounds.iter().zip(&world.regions).any(|(old, r)| *old != (r.min_x,r.min_y,r.max_x,r.max_y));
        if !self.region_map_initialized || bounds_changed {
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
        self.region_bounds.clear();
        self.region_bounds.extend(world.regions.iter().map(|r|(r.min_x,r.min_y,r.max_x,r.max_y)));
        self.region_map_initialized=true;
        }
        #[cfg(test)]
        let use_cpu_thermal = !self.thermal_reference;
        #[cfg(not(test))]
        let use_cpu_thermal = true;
        if use_cpu_thermal && thermal_cpu::try_step(world, elements, &self.region_map, &mut self.thermal_cpu_groups, dt) {
            if profiling {
                eprintln!("[CycleTrim OniSim] CPU sparse-face thermal profile cells={count} wall={:.3}ms",
                    started.elapsed().as_secs_f64() * 1000.0);
            }
            self.total_steps += 1;
            return Ok(());
        }
        self.thermal_properties.clear();
        self.thermal_properties.extend(elements.elements.iter().map(|element| {
            [element.record.thermal_conductivity.to_bits(), element.record.specific_heat_capacity.to_bits()]
        }));
        self.thermal.prepare_materials_by_element(&world.read.element, &self.thermal_properties)?;
        static LOG_MATERIAL:std::sync::Once=std::sync::Once::new();
        LOG_MATERIAL.call_once(||eprintln!("[CycleTrim OniSim] GPU thermal material lookup enabled ({count} cells)"));
        self.thermal.upload_state(&world.read.temperature, &world.read.mass)?;
        self.thermal_flags.clear();
        self.thermal_flags.extend((0..elements.len().max(world.gas_channel_of_element.len())).map(|id| {
            let gas = elements.elements.get(id).is_some_and(|e| e.record.state & 3 == 1);
            u32::from(gas) * 3
        }));
        self.thermal.prepare_deferred_groups_on_gpu(&world.mixture_element,&world.mixture_mass,&world.read.properties,&self.region_map,&self.thermal_flags)?;
        let prepared = started.elapsed();
        // Keep thermal dispatch and the CPU-required result copy in one
        // caller-owned submission. This is also the insertion point for other
        // GPU-resident operators that depend on the updated temperature.
        let mut encoder = self.context.command_encoder();
        self.thermal.encode_resident(&mut encoder, dt, 0.25)?;
        self.thermal.encode_readback(&mut encoder);
        let submission = self.context.submit(encoder);
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
        assert!(flux::finish_sparse_liquid_region(serial_pending,&mut serial,&mut a,&mut serial_mask,&mut gpu).unwrap());
        let pending=flux::begin_sparse_liquid_region(&overlap,&elements,region,&mut gpu).unwrap().unwrap();
        assert!(crate::simulation_step_fluid::gas_phase(&mut overlap,&elements,&diseases,&mut b,region));
        let mut overlap_mask=Vec::new();
        assert!(flux::finish_sparse_liquid_region(pending,&mut overlap,&mut b,&mut overlap_mask,&mut gpu).unwrap());
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
        assert!(multicomponent_flux::finish_sparse_liquid_region(pending,&mut overlap,&mut events,&mut overlap_solid,&mut gpu).unwrap());
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
        assert!(multicomponent_flux::finish_sparse_liquid_region(pending,&mut overlap,&mut events,&mut overlap_solid,&mut gpu).unwrap());
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
        assert!(multicomponent_flux::finish_sparse_liquid_region(pending,&mut world,&mut events,&mut solid,&mut gpu).unwrap());
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
        assert!(flux::finish_sparse_liquid_region(pending,&mut overlap,&mut overlap_events,&mut overlap_solid,&mut gpu).unwrap());
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
        gpu.thermal_reference = true;
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
                    let mut sum = 0.0f32;
                    world.for_each_cell_species(c, |elem_id, mass| {
                        if elem_id == id { sum += mass; }
                    });
                    sum
                }).sum()).collect()
        };
        let before = totals(&world);
        let mut solid = Vec::new();
        assert!(multicomponent_flux::step_region_with_mask(&mut world,&elements,&mut events,region,Some(&mut gpu),true,Some(&mut solid)).unwrap());
        assert_eq!(gpu.gas.front().unwrap().transport.dimensions(), (5, 3, 3));
        assert!(solid.iter().all(|&blocked| blocked == 0), "three species must not become artificial walls");
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
    fn gas_density_transport_and_heat_chain_conserves_mass_and_energy() {
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
    fn direct_gas_transport_spreads_into_vacuum_without_crossing_walls_or_oscillating() {
        use onisim_gpu::multicomponent::{SpeciesInfo, State};
        let (width, height, channels) = (12, 10, 3);
        let count = width * height;
        let mut gpu = super::GpuSimulation::new(width, height).unwrap();
        let species: Vec<_> = (0..channels).map(|c| SpeciesInfo {
            element_id: c as u16 + 1, phase: 1, specific_heat_capacity: 1.0 + c as f32,
            vertical_drift: 0.0,
        }).collect();
        let solid: Vec<_> = (0..count).map(|i| u32::from(i % width == 8)).collect();
        let source = 5 * width + 4;
        for amount in [0.001, 10000.0] {
            let mut mass = vec![0.0; count * channels];
            for c in 0..channels { mass[source * channels + c] = amount * (c + 1) as f32; }
            let mut temperature = vec![400.0; count];
            let mut next_mass = vec![0.0; mass.len()];
            let mut next_temperature = vec![0.0; count];
            let energy = |m: &[f32], t: &[f32]| m.iter().enumerate().map(|(i, &v)|
                f64::from(v) * f64::from(species[i % channels].specific_heat_capacity) * f64::from(t[i / channels])).sum::<f64>();
            let density_norm = |m: &[f32]| m.chunks_exact(channels).map(|v| v.iter().map(|&x| f64::from(x)).sum::<f64>().powi(2)).sum::<f64>();
            let heat = energy(&mass, &temperature);
            let mut previous_norm = density_norm(&mass);
            for _ in 0..20 {
                let state = State { mass: &mass, enthalpy: &[], velocity_x: &[], velocity_y: &[], solid: &solid, species: &species };
                gpu.step_gas(width, height, channels, &state, &temperature, &[0.0; 3], true, 0.05,
                    &mut next_mass, &mut next_temperature).unwrap();
                assert!(next_mass.iter().all(|&v| v.is_finite() && v >= 0.0));
                let norm = density_norm(&next_mass);
                assert!(norm <= previous_norm * 1.00001, "density oscillation {norm} > {previous_norm}");
                previous_norm = norm;
                for c in 0..channels {
                    let total: f64 = next_mass.iter().skip(c).step_by(channels).map(|&v| f64::from(v)).sum();
                    assert!((total - f64::from(amount) * (c + 1) as f64).abs() < f64::from(amount) * 1e-4);
                }
                assert!((energy(&next_mass, &next_temperature) - heat).abs() < heat * 1e-5);
                std::mem::swap(&mut mass, &mut next_mass);
                std::mem::swap(&mut temperature, &mut next_temperature);
            }
            assert!((0..channels).all(|c| mass[(source + 1) * channels + c] > 0.0));
            assert!((0..count).filter(|i| i % width >= 8).all(|i|
                mass[i * channels..(i + 1) * channels].iter().all(|&v| v == 0.0)));
        }
    }

    #[test]
    fn gas_pipeline_reuses_alternating_region_sizes() {
        use super::GpuSimulation;
        use onisim_gpu::multicomponent::{SpeciesInfo, State};
        let mut gpu = GpuSimulation::new(5, 4).unwrap();
        gpu.thermal_reference = true;
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
    #[ignore = "manual paired eight-world GPU timing"]
    fn profile_eight_world_gas_batch() {
        use super::{GpuSimulation, GasBatchJob};
        use onisim_gpu::multicomponent::{SpeciesInfo, State};
        struct Input {
            shape: (usize, usize, usize), mass: Vec<f32>, temperature: Vec<f32>,
            solid: Vec<u32>, species: Vec<SpeciesInfo>, conductivity: Vec<f32>,
        }
        let inputs: Vec<_> = [(32,32,1), (64,96,5), (80,174,2), (128,153,7),
            (160,274,6), (32,32,1), (64,127,2), (32,32,1)].into_iter().enumerate().map(|(index, shape)| {
            let (w,h,c) = shape;
            let mut mass = vec![0.0; w*h*c];
            for cell in 0..w*h {
                if cell % 97 != 0 { mass[cell*c + cell%c] = if cell%w < w/2 { 2.0 + index as f32 } else { 0.1 }; }
            }
            Input { shape, mass, temperature: vec![280.0 + index as f32 * 10.0; w*h],
                solid: (0..w*h).map(|cell| u32::from(cell % 97 == 0)).collect(),
                species: (0..c).map(|i| SpeciesInfo { element_id: i as u16 + 1, phase: 1,
                    specific_heat_capacity: 1.0 + i as f32 * 0.1, vertical_drift: 0.0 }).collect(),
                conductivity: vec![1.0; c] }
        }).collect();
        let mut gpu = GpuSimulation::new(160, 274).unwrap();
        let mut serial: Vec<_> = inputs.iter().map(|i| (vec![0.0; i.mass.len()], vec![0.0; i.temperature.len()])).collect();
        let mut batched = serial.clone();
        let mut serial_times = Vec::new();
        let mut batch_times = Vec::new();
        for iteration in 0..110 {
            for batch in if iteration % 2 == 0 { [false, true] } else { [true, false] } {
                let started = std::time::Instant::now();
                if batch {
                    let mut jobs: Vec<_> = inputs.iter().zip(&mut batched).map(|(input, (mass_output, temperature_output))| GasBatchJob {
                        dimensions: input.shape,
                        state: State { mass: &input.mass, enthalpy: &[], velocity_x: &[], velocity_y: &[], solid: &input.solid, species: &input.species },
                        temperature: &input.temperature, conductivity: &input.conductivity, mass_output, temperature_output,
                    }).collect();
                    gpu.step_gas_batch(&mut jobs, true, 0.05).unwrap();
                } else {
                    for (input, (mass, temperature)) in inputs.iter().zip(&mut serial) {
                        let (w,h,c) = input.shape;
                        let state = State { mass: &input.mass, enthalpy: &[], velocity_x: &[], velocity_y: &[], solid: &input.solid, species: &input.species };
                        gpu.step_gas(w,h,c,&state,&input.temperature,&input.conductivity,true,0.05,mass,temperature).unwrap();
                    }
                }
                if iteration >= 10 {
                    if batch { batch_times.push(started.elapsed().as_secs_f64()*1000.0); }
                    else { serial_times.push(started.elapsed().as_secs_f64()*1000.0); }
                }
            }
            for (index, (a,b)) in serial.iter().zip(&batched).enumerate() {
                assert!(a.0 == b.0 && a.1 == b.1, "batch output mismatch in world {index}");
            }
        }
        let wins = serial_times.iter().zip(&batch_times).filter(|(a,b)| b < a).count();
        println!("EIGHT_WORLD_GAS_PAIRS,batch_faster={wins}/100");
        for (label, mut times) in [("serial", serial_times), ("batch", batch_times)] {
            times.sort_by(f64::total_cmp);
            println!("EIGHT_WORLD_GAS,{label},median_ms={:.6},min_ms={:.6},max_ms={:.6}",
                (times[49]+times[50])*0.5,times[0],times[99]);
        }
    }

    #[test]
    #[ignore = "manual GPU timing on the game-sized gas region"]
    fn profile_game_sized_gas_chain() {
        use super::GpuSimulation;
        use onisim_gpu::multicomponent::{SpeciesInfo, State};
        let mut gpu = GpuSimulation::new(160, 274).unwrap();
        for (width, height, channels) in [(32,32,1), (64,96,5), (80,174,2), (128,153,7), (160,274,6)] {
        let cells = width * height;
        let mut mass = vec![0.0; cells * channels];
        let mut enthalpy = vec![0.0; cells * channels];
        for cell in 0..cells {
            let first = cell % channels;
            let amount = if cell % width < width / 2 { 2.0 } else { 0.1 };
            mass[cell * channels + first] = amount;
            enthalpy[cell * channels + first] = amount * 300.0;
        }
        let velocity = vec![0.0; cells];
        let solid = vec![0; cells];
        let species: Vec<_> = (0..channels).map(|i| SpeciesInfo {
            element_id: i as u16 + 1, phase: 1, specific_heat_capacity: 1.0, vertical_drift: 0.0,
        }).collect();
        let state = State { mass: &mass, enthalpy: &enthalpy, velocity_x: &velocity,
            velocity_y: &velocity, solid: &solid, species: &species };
        let mut out_mass = vec![0.0; mass.len()];
        let mut out_temperature = vec![0.0; cells];
        let conductivity = vec![1.0; channels];
        let temperatures = vec![300.0; cells];
        let mut times = Vec::new();
        for iteration in 0..25 {
            let start = std::time::Instant::now();
            gpu.step_gas(width, height, channels, &state, &temperatures, &conductivity, true, 0.05,
                &mut out_mass, &mut out_temperature).unwrap();
            if iteration >= 5 { times.push(start.elapsed().as_secs_f64() * 1000.0); }
        }
        times.sort_by(f64::total_cmp);
        println!("GAS_CHAIN_SAMPLE,{width},{height},{channels},median_ms={:.6},min_ms={:.6},max_ms={:.6}",
            (times[9] + times[10]) * 0.5, times[0], times[19]);
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
        gpu.thermal_reference = true;
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
