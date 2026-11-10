use std::{cell::Cell, collections::HashSet, sync::{mpsc, Arc}, time::Duration};
const SHADER: &str = include_str!("multicomponent_flux.wgsl");
const DIFFUSION_SHADER: &str = r#"
struct Params { width:u32, height:u32, channels:u32, unused:u32 };
@group(0) @binding(0) var<storage,read> mass:array<f32>;
@group(0) @binding(1) var<storage,read> enthalpy:array<f32>;
@group(0) @binding(2) var<storage,read> solid:array<u32>;
@group(0) @binding(3) var<storage,read_write> out_mass:array<f32>;
@group(0) @binding(4) var<storage,read_write> out_enthalpy:array<f32>;
@group(0) @binding(5) var<uniform> p:Params;
@group(0) @binding(6) var<storage,read> density:array<f32>;
fn total(i:u32)->f32 { return density[i]; }
fn exchange(a:u32,b:u32,c:u32,ta:f32)->vec2<f32> {
 if solid[b]!=0u {return vec2<f32>(0.0);}
 let tb=total(b); if ta<=0.0 || tb<=0.0 {return vec2<f32>(0.0);}
 let ai=a*p.channels+c; let bi=b*p.channels+c; let ma=mass[ai]; let mb=mass[bi];
 let flow=(ma/ta-mb/tb)*min(ta,tb)*0.05;
 var heat=0.0;
 if flow>0.0 && ma>0.0 {heat=flow*enthalpy[ai]/ma;}
 if flow<0.0 && mb>0.0 {heat=flow*enthalpy[bi]/mb;}
 return vec2<f32>(flow,heat);
}
@compute @workgroup_size(8,8)
fn main(@builtin(global_invocation_id) id:vec3<u32>) {
 if id.x>=p.width*p.channels || id.y>=p.height {return;}
 let x=id.x/p.channels; let c=id.x%p.channels;
 let a=id.y*p.width+x; let ta=total(a); let i=a*p.channels+c;
 var flux=vec2<f32>(0.0);
 if solid[a]==0u {
  if x>0u {flux+=exchange(a,a-1u,c,ta);}
  if x+1u<p.width {flux+=exchange(a,a+1u,c,ta);}
  if id.y>0u {flux+=exchange(a,a-p.width,c,ta);}
  if id.y+1u<p.height {flux+=exchange(a,a+p.width,c,ta);}
 }
 out_mass[i]=mass[i]-flux.x; out_enthalpy[i]=enthalpy[i]-flux.y;
}
"#;
const LIQUID_PROJECTION_SHADER: &str = r#"
struct Params { cells:u32, channels:u32, pad0:u32, pad1:u32 };
struct Cell { first:u32, second:u32, first_mass:f32, second_mass:f32, enthalpy:f32, overflow:u32 };
@group(0) @binding(0) var<storage,read> mass:array<f32>;
@group(0) @binding(1) var<storage,read> energy:array<f32>;
@group(0) @binding(2) var<storage,read_write> output:array<Cell>;
@group(0) @binding(3) var<uniform> p:Params;
@compute @workgroup_size(64)
fn main(@builtin(global_invocation_id) id:vec3<u32>) {
 let cell=id.x; if cell>=p.cells { return; }
 var a=0u; var b=0u; var am=0.0; var bm=0.0; var e=0.0; var present=0u;
 for(var c=0u;c<p.channels;c++) {
  let i=cell*p.channels+c; let m=max(mass[i],0.0); e+=energy[i];
  if m>0.0 {present+=1u;}
  if m>am {b=a; bm=am; a=c; am=m;} else if m>bm {b=c; bm=m;}
 }
 output[cell]=Cell(a,b,am,bm,e,select(0u,1u,present>2u));
}
"#;
const LIQUID_COMPACT_PROJECTION_SHADER:&str = r#"
struct Params { cells:u32, channels:u32, pad0:u32, pad1:u32 };
struct Cell { indices:u32, first_mass:f32, second_mass:f32, enthalpy:f32 };
@group(0) @binding(0) var<storage,read> mass:array<f32>;
@group(0) @binding(1) var<storage,read> energy:array<f32>;
@group(0) @binding(2) var<storage,read_write> output:array<Cell>;
@group(0) @binding(3) var<uniform> p:Params;
@compute @workgroup_size(64)
fn main(@builtin(global_invocation_id) id:vec3<u32>) {
 let cell=id.x; if cell>=p.cells {return;}
 var a=0u; var b=0u; var am=0.0; var bm=0.0; var e=0.0; var present=0u;
 for(var c=0u;c<p.channels;c++) {
  let i=cell*p.channels+c; let m=max(mass[i],0.0); e+=energy[i];
  if m>0.0 {present+=1u;}
  if m>am {b=a;bm=am;a=c;am=m;} else if m>bm {b=c;bm=m;}
 }
 output[cell]=Cell(a|(b<<16u)|select(0u,0x80000000u,present>2u),am,bm,e);
}
"#;
const GAS_PROJECTION_SHADER: &str = r#"
struct Params { cells:u32, channels:u32, pad0:u32, pad1:u32 };
struct Cell { first:u32, second:u32, first_mass:f32, second_mass:f32, temperature:f32, overflow:u32 };
@group(0) @binding(0) var<storage,read> mass:array<f32>;
@group(0) @binding(1) var<storage,read> temperature:array<f32>;
@group(0) @binding(2) var<storage,read_write> output:array<Cell>;
@group(0) @binding(3) var<uniform> p:Params;
@compute @workgroup_size(64)
fn main(@builtin(global_invocation_id) id:vec3<u32>) {
 let cell=id.x; if cell>=p.cells {return;}
 var a=0u; var b=0u; var am=0.0; var bm=0.0; var present=0u;
 for(var c=0u;c<p.channels;c++) {
  let m=max(mass[cell*p.channels+c],0.0); if m>0.0 {present+=1u;}
  if m>am {b=a;bm=am;a=c;am=m;} else if m>bm {b=c;bm=m;}
 }
 output[cell]=Cell(a,b,am,bm,temperature[cell],select(0u,1u,present>2u));
}
"#;
const GAS_COMPACT_PROJECTION_SHADER: &str = r#"
struct Params { cells:u32, channels:u32, pad0:u32, pad1:u32 };
struct Cell { indices:u32, first_mass:f32, second_mass:f32, temperature:f32 };
@group(0) @binding(0) var<storage,read> mass:array<f32>;
@group(0) @binding(1) var<storage,read> temperature:array<f32>;
@group(0) @binding(2) var<storage,read_write> output:array<Cell>;
@group(0) @binding(3) var<uniform> p:Params;
@compute @workgroup_size(64)
fn main(@builtin(global_invocation_id) id:vec3<u32>) {
 let cell=id.x; if cell>=p.cells {return;}
 var a=0u; var b=0u; var am=0.0; var bm=0.0; var present=0u;
 for(var c=0u;c<p.channels;c++) {
  let m=max(mass[cell*p.channels+c],0.0); if m>0.0 {present+=1u;}
  if m>am {b=a;bm=am;a=c;am=m;} else if m>bm {b=c;bm=m;}
 }
 output[cell]=Cell(a|(b<<16u)|select(0u,0x80000000u,present>2u),am,bm,temperature[cell]);
}
"#;
const LIQUID_ENTHALPY_SHADER: &str = r#"
struct Params { width:u32, height:u32, channels:u32, pad:u32 };
@group(0) @binding(0) var<storage,read> temperature:array<f32>;
@group(0) @binding(1) var<storage,read> mass:array<f32>;
@group(0) @binding(2) var<storage,read> heat:array<f32>;
@group(0) @binding(3) var<storage,read_write> enthalpy:array<f32>;
@group(0) @binding(4) var<uniform> p:Params;
@compute @workgroup_size(64)
fn main(@builtin(global_invocation_id) id:vec3<u32>) {
 let i=id.x; let values=p.width*p.height*p.channels; if i>=values {return;}
 enthalpy[i]=mass[i]*heat[i%p.channels]*temperature[i/p.channels];
}
"#;
const LIQUID_SPARSE_SHADER: &str = r#"
struct Params { width:u32, height:u32, channels:u32, pad:u32 };
struct Cell { first:u32, second:u32, first_mass:f32, second_mass:f32, temperature:f32 };
@group(0) @binding(0) var<storage,read> cells:array<Cell>;
@group(0) @binding(1) var<storage,read> solid:array<u32>;
@group(0) @binding(2) var<storage,read_write> mass:array<f32>;
@group(0) @binding(3) var<storage,read_write> enthalpy:array<f32>;
@group(0) @binding(4) var<storage,read> heat:array<f32>;
@group(0) @binding(5) var<uniform> p:Params;
@group(0) @binding(6) var<storage,read_write> temperatures:array<f32>;
@compute @workgroup_size(64)
fn main(@builtin(global_invocation_id) id:vec3<u32>) {
 let i=id.x; if i>=p.width*p.height*p.channels {return;}
 let c=i%p.channels; let cell=i/p.channels; let v=cells[cell]; var m=0.0;
 if c==0u {temperatures[cell]=v.temperature;}
 if solid[cell]==0u { if c==v.first {m+=v.first_mass;} if c==v.second {m+=v.second_mass;} }
 mass[i]=m; enthalpy[i]=m*v.temperature*heat[c];
}
"#;
const LIQUID_COMPACT_SPARSE_SHADER:&str = r#"
struct Params { width:u32, height:u32, channels:u32, pad:u32 };
struct Cell { indices:u32, first_mass:f32, second_mass:f32, temperature:f32 };
@group(0) @binding(0) var<storage,read> cells:array<Cell>;
@group(0) @binding(1) var<storage,read> solid:array<u32>;
@group(0) @binding(2) var<storage,read_write> mass:array<f32>;
@group(0) @binding(3) var<storage,read_write> enthalpy:array<f32>;
@group(0) @binding(4) var<storage,read> heat:array<f32>;
@group(0) @binding(5) var<uniform> p:Params;
@group(0) @binding(6) var<storage,read_write> temperatures:array<f32>;
@compute @workgroup_size(64)
fn main(@builtin(global_invocation_id) id:vec3<u32>) {
 let i=id.x; if i>=p.width*p.height*p.channels {return;}
 let c=i%p.channels; let cell=i/p.channels; let v=cells[cell]; var m=0.0;
 if c==0u {temperatures[cell]=v.temperature;}
 if solid[cell]==0u {if c==(v.indices&65535u) {m+=v.first_mass;} if c==(v.indices>>16u) {m+=v.second_mass;}}
 mass[i]=m; enthalpy[i]=m*v.temperature*heat[c];
}
"#;
const DENSITY_SUM_SHADER: &str = r#"
struct Params { width:u32, height:u32, channels:u32, unused:u32 };
@group(0) @binding(0) var<storage,read> mass:array<f32>;
@group(0) @binding(1) var<storage,read_write> density:array<f32>;
@group(0) @binding(2) var<uniform> params:Params;
@compute @workgroup_size(8,8)
fn main(@builtin(global_invocation_id) id:vec3<u32>) {
 if id.x>=params.width || id.y>=params.height {return;}
 let cell=id.y*params.width+id.x; var total=0.0;
 for(var c=0u;c<params.channels;c++){total+=mass[cell*params.channels+c];}
 density[cell]=total;
}
"#;
const DENSITY_VELOCITY_SHADER: &str = r#"
struct Params { width:u32, height:u32, channels:u32, unused:u32 };
@group(0) @binding(0) var<storage,read> mass:array<f32>;
@group(0) @binding(1) var<storage,read> solid:array<u32>;
@group(0) @binding(2) var<storage,read_write> vx:array<f32>;
@group(0) @binding(3) var<storage,read_write> vy:array<f32>;
@group(0) @binding(4) var<uniform> params:Params;
fn total(cell:u32)->f32 { return mass[cell]; }
@compute @workgroup_size(8,8)
fn main(@builtin(global_invocation_id) id:vec3<u32>) {
 if id.x>=params.width || id.y>=params.height {return;}
 let i=id.y*params.width+id.x;
 if solid[i]!=0u {vx[i]=0.0;vy[i]=0.0;return;}
 let center=total(i);
 var l=center; var r=center; var d=center; var u=center;
 if id.x>0u && solid[i-1u]==0u {l=total(i-1u);}
 if id.x+1u<params.width && solid[i+1u]==0u {r=total(i+1u);}
 if id.y>0u && solid[i-params.width]==0u {d=total(i-params.width);}
 if id.y+1u<params.height && solid[i+params.width]==0u {u=total(i+params.width);}
 vx[i]=0.04*(l-r); vy[i]=0.04*(d-u);
}
"#;
const CFL_SHADER: &str = r#"
struct Params { cells:u32, channels:u32, pad0:u32, pad1:u32 };
@group(0) @binding(0) var<storage,read> vx:array<f32>;
@group(0) @binding(1) var<storage,read> vy:array<f32>;
@group(0) @binding(2) var<storage,read> drift:array<f32>;
@group(0) @binding(3) var<storage,read_write> maximum:atomic<u32>;
@group(0) @binding(4) var<uniform> params:Params;
var<workgroup> rates:array<u32,64>;
@compute @workgroup_size(64)
fn main(@builtin(global_invocation_id) id:vec3<u32>, @builtin(local_invocation_index) lane:u32) {
 let i=id.x;
 var bits=0u;
 if i<params.cells {
   if (bitcast<u32>(vx[i]) & 0x7f800000u)==0x7f800000u || (bitcast<u32>(vy[i]) & 0x7f800000u)==0x7f800000u {
     bits=0x7f800000u;
   } else {
     let low=vy[i]+min(bitcast<f32>(params.pad0),drift[0]);
     let high=vy[i]+max(bitcast<f32>(params.pad1),drift[0]);
     if (bitcast<u32>(low) & 0x7f800000u)==0x7f800000u || (bitcast<u32>(high) & 0x7f800000u)==0x7f800000u {bits=0x7f800000u;}
     let vertical=max(abs(low),abs(high));
     let rate=abs(vx[i])+vertical;
     if (bitcast<u32>(rate) & 0x7f800000u)==0x7f800000u { bits=0x7f800000u; }
     else { bits=max(bits,bitcast<u32>(rate)); }
   }
 }
 rates[lane]=bits;
 workgroupBarrier();
 var stride=32u;
 loop {
   if lane<stride { rates[lane]=max(rates[lane],rates[lane+stride]); }
   workgroupBarrier();
   if stride==1u { break; }
   stride=stride/2u;
 }
 if lane==0u { atomicMax(&maximum,rates[0]); }
}
"#;

/// Canonical metadata for one tensor channel. Phase values match ONI Element.State:
/// 1 gas, 2 liquid, 3 solid. Enthalpy is stored per cell as mass * cp * T.
#[derive(Clone, Copy, Debug)]
pub struct SpeciesInfo {
    pub element_id: u16,
    pub phase: u8,
    pub specific_heat_capacity: f32,
    pub vertical_drift: f32,
}

#[derive(Clone, Copy)]
pub struct State<'a> {
    pub mass: &'a [f32],
    pub enthalpy: &'a [f32],
    pub velocity_x: &'a [f32],
    pub velocity_y: &'a [f32],
    pub solid: &'a [u32],
    pub species: &'a [SpeciesInfo],
}

#[repr(C)]
#[derive(Clone, Copy, Debug, Default, bytemuck::Pod, bytemuck::Zeroable)]
pub struct ProjectedLiquidCell {
    pub first: u32, pub second: u32, pub first_mass: f32, pub second_mass: f32,
    pub enthalpy: f32, pub overflow: u32,
}
#[repr(C)]
#[derive(Clone, Copy, bytemuck::Pod, bytemuck::Zeroable)]
struct CompactGasCell {indices:u32, first_mass:f32, second_mass:f32, temperature:f32}
#[repr(C)]
#[derive(Clone, Copy, Debug, Default, bytemuck::Pod, bytemuck::Zeroable)]
pub struct SparseLiquidCell {
    pub first:u32, pub second:u32, pub first_mass:f32, pub second_mass:f32, pub temperature:f32,
}

#[repr(C)]
#[derive(Clone, Copy, Debug, Default, bytemuck::Pod, bytemuck::Zeroable)]
pub struct PackedSparseCell {
    pub first:u16, pub second:u16, pub first_mass:f32, pub second_mass:f32, pub temperature:f32,
}

trait SparseInput:bytemuck::Pod {
    const PACKED:bool;
    fn unpack(&self)->SparseLiquidCell;
}
impl SparseInput for SparseLiquidCell {
    const PACKED:bool=false;
    fn unpack(&self)->SparseLiquidCell {*self}
}
impl SparseInput for PackedSparseCell {
    const PACKED:bool=true;
    fn unpack(&self)->SparseLiquidCell {SparseLiquidCell {first:self.first as u32,second:self.second as u32,
        first_mass:self.first_mass,second_mass:self.second_mass,temperature:self.temperature}}
}

/// A submitted liquid projection whose readback can be collected after other work.
pub struct PendingLiquidProjection {
    device: Arc<wgpu::Device>,
    readback: wgpu::Buffer,
    submission: wgpu::SubmissionIndex,
    result: mpsc::Receiver<Result<(), wgpu::BufferAsyncError>>,
    count: usize,
    busy: Arc<std::sync::atomic::AtomicBool>,
    compact: bool,
}

impl PendingLiquidProjection {
    pub fn finish(self, output: &mut [ProjectedLiquidCell]) -> Result<bool, String> {
        if output.len() != self.count { return Err("liquid projection output dimensions mismatch".into()); }
        self.device.poll(wgpu::PollType::Wait {submission_index:Some(self.submission.clone()),timeout:Some(Duration::from_secs(30))}).map_err(|e|e.to_string())?;
        self.result.recv_timeout(Duration::from_secs(2)).map_err(|e|e.to_string())?.map_err(|e|e.to_string())?;
        let view=self.readback.get_mapped_range(..).map_err(|e|e.to_string())?;
        let valid=if self.compact {
            let cells:&[CompactGasCell]=bytemuck::cast_slice(&view);
            let valid=cells.iter().all(|cell|cell.indices&0x8000_0000==0);
            if valid {for (dst,cell) in output.iter_mut().zip(cells) {
                *dst=ProjectedLiquidCell {first:cell.indices&0xffff,second:(cell.indices>>16)&0x7fff,
                    first_mass:cell.first_mass,second_mass:cell.second_mass,enthalpy:cell.temperature,overflow:0};
            }}
            valid
        } else {
            let cells:&[ProjectedLiquidCell]=bytemuck::cast_slice(&view);
            let valid=cells.iter().all(|cell|cell.overflow==0);
            if valid {output.copy_from_slice(cells);}
            valid
        };
        drop(view);
        Ok(valid)
    }
}

impl Drop for PendingLiquidProjection {
    fn drop(&mut self) {
        self.readback.unmap();
        self.busy.store(false, std::sync::atomic::Ordering::Release);
    }
}

struct LiquidProjection {
    pipeline: wgpu::ComputePipeline,
    params: wgpu::Buffer,
    output: wgpu::Buffer,
    readback: wgpu::Buffer,
    groups: Option<[wgpu::BindGroup; 2]>,
    temperature_groups: Vec<(wgpu::Buffer, [wgpu::BindGroup; 2])>,
}
struct LiquidPreparation { pipeline:wgpu::ComputePipeline, temperature:wgpu::Buffer, group:wgpu::BindGroup }
struct LiquidSparsePreparation { pipeline:wgpu::ComputePipeline, cells:wgpu::Buffer, _temperatures:wgpu::Buffer, group:wgpu::BindGroup, temperature_groups:Vec<(wgpu::Buffer,wgpu::BindGroup)> }

pub struct MulticomponentGpu {
    device: Arc<wgpu::Device>,
    queue: Arc<wgpu::Queue>,
    pipeline: wgpu::ComputePipeline,
    diffusion_pipeline: wgpu::ComputePipeline,
    diffusion_groups: [wgpu::BindGroup; 2],
    density_sum_pipeline: wgpu::ComputePipeline,
    density_sum_groups: [wgpu::BindGroup; 2],
    density_velocity_pipeline: wgpu::ComputePipeline,
    density_velocity_groups: [wgpu::BindGroup; 2],
    cfl_pipeline: wgpu::ComputePipeline,
    cfl_group: wgpu::BindGroup,
    cfl_maximum: wgpu::Buffer,
    cfl_params: wgpu::Buffer,
    cfl_readback: wgpu::Buffer,
    groups: [wgpu::BindGroup; 2],
    mass_a: wgpu::Buffer,
    mass_b: wgpu::Buffer,
    enthalpy_a: wgpu::Buffer,
    enthalpy_b: wgpu::Buffer,
    velocity_x: wgpu::Buffer,
    velocity_y: wgpu::Buffer,
    solid: wgpu::Buffer,
    drift_y: wgpu::Buffer,
    species_heat: wgpu::Buffer,
    params: wgpu::Buffer,
    readback: wgpu::Buffer,
    width: usize,
    height: usize,
    channels: usize,
    count: usize,
    uploaded: bool,
    current_is_a: bool,
    max_cfl_rate: f32,
    velocity_externally_exposed: Cell<bool>,
    pending_cfl_dt: Option<f32>,
    uploaded_drift: Vec<u32>,
    uploaded_heat: Vec<u32>,
    uploaded_solid: Vec<u32>,
    uploaded_sparse: Vec<u8>,
    liquid_projection: Option<LiquidProjection>,
    liquid_projection_busy: Arc<std::sync::atomic::AtomicBool>,
    gas_projection: Option<LiquidProjection>,
    liquid_preparation: Option<LiquidPreparation>,
    liquid_sparse_preparation: Option<LiquidSparsePreparation>,
    pub adapter: wgpu::AdapterInfo,
}

impl MulticomponentGpu {
    pub async fn new(width: usize, height: usize, channels: usize) -> Result<Self, String> {
        let context = Arc::new(crate::gpu_context::GpuContext::new().await?);
        Self::new_with_context(context, width, height, channels).await
    }

    pub async fn new_with_context(
        context: Arc<crate::gpu_context::GpuContext>,
        width: usize,
        height: usize,
        channels: usize,
    ) -> Result<Self, String> {
        let count = width.checked_mul(height).ok_or("grid overflow")?;
        let values = count.checked_mul(channels).ok_or("tensor overflow")?;
        if width == 0 || height == 0 || channels == 0 || values > u32::MAX as usize {
            return Err("invalid grid dimensions".into());
        }
        let device = &context.device;
        let max_groups = device.limits().max_compute_workgroups_per_dimension as usize;
        if (width * channels).div_ceil(8) > max_groups || height.div_ceil(8) > max_groups {
            return Err("transport grid exceeds device workgroup limits".into());
        }
        let info = context.adapter.clone();
        let max_bytes = (values.max(count).max(channels) * 4) as u64;
        let readback_bytes = values.checked_mul(8).and_then(|n| n.checked_add(4)).ok_or("readback size overflow")? as u64;
        if max_bytes > device.limits().max_storage_buffer_binding_size as u64 {
            return Err("tensor exceeds Vulkan storage-buffer limit".into());
        }
        if readback_bytes > device.limits().max_buffer_size {
            return Err("mass/enthalpy readback exceeds Vulkan buffer limit".into());
        }
        let scope = device.push_error_scope(wgpu::ErrorFilter::Validation);
        let shader = device.create_shader_module(wgpu::ShaderModuleDescriptor {
            label: Some("multicomponent conservative mass/enthalpy flux"),
            source: wgpu::ShaderSource::Wgsl(SHADER.into()),
        });
        let pipeline = device.create_compute_pipeline(&wgpu::ComputePipelineDescriptor {
            label: Some("multicomponent mass/enthalpy flux pipeline"),
            layout: None,
            module: &shader,
            entry_point: Some("main"),
            compilation_options: Default::default(),
            cache: None,
        });
        let diffusion_shader = device.create_shader_module(wgpu::ShaderModuleDescriptor {
            label: Some("conservative gas diffusion"), source: wgpu::ShaderSource::Wgsl(DIFFUSION_SHADER.into()) });
        let diffusion_pipeline = device.create_compute_pipeline(&wgpu::ComputePipelineDescriptor {
            label: Some("conservative gas diffusion"), layout: None, module: &diffusion_shader,
            entry_point: Some("main"), compilation_options: Default::default(), cache: None });
        let density_shader = device.create_shader_module(wgpu::ShaderModuleDescriptor {
            label: Some("gas density gradient"), source: wgpu::ShaderSource::Wgsl(DENSITY_VELOCITY_SHADER.into()),
        });
        let density_velocity_pipeline = device.create_compute_pipeline(&wgpu::ComputePipelineDescriptor {
            label: Some("gas density gradient"), layout: None, module: &density_shader, entry_point: Some("main"),
            compilation_options: Default::default(), cache: None,
        });
        let density_sum_shader = device.create_shader_module(wgpu::ShaderModuleDescriptor {
            label: Some("gas density sum"), source: wgpu::ShaderSource::Wgsl(DENSITY_SUM_SHADER.into()),
        });
        let density_sum_pipeline = device.create_compute_pipeline(&wgpu::ComputePipelineDescriptor {
            label: Some("gas density sum"), layout: None, module: &density_sum_shader, entry_point: Some("main"),
            compilation_options: Default::default(), cache: None,
        });
        let cfl_shader=device.create_shader_module(wgpu::ShaderModuleDescriptor { label:Some("resident CFL reduction"), source:wgpu::ShaderSource::Wgsl(CFL_SHADER.into()) });
        let cfl_pipeline=device.create_compute_pipeline(&wgpu::ComputePipelineDescriptor { label:Some("resident CFL reduction"),layout:None,module:&cfl_shader,entry_point:Some("main"),compilation_options:Default::default(),cache:None });
        let storage = wgpu::BufferUsages::STORAGE | wgpu::BufferUsages::COPY_DST;
        let make = |label: &str, bytes: usize, usage| {
            device.create_buffer(&wgpu::BufferDescriptor {
                label: Some(label),
                size: bytes.max(4) as u64,
                usage,
                mapped_at_creation: false,
            })
        };
        let cfl_maximum=make("CFL maximum",4,storage|wgpu::BufferUsages::COPY_SRC);
        let cfl_params=make("CFL parameters",16,wgpu::BufferUsages::UNIFORM|wgpu::BufferUsages::COPY_DST);
        let cfl_readback=make("CFL readback",4,wgpu::BufferUsages::MAP_READ|wgpu::BufferUsages::COPY_DST);
        let mass_a = make("mass A", values * 4, storage | wgpu::BufferUsages::COPY_SRC);
        let mass_b = make("mass B", values * 4, storage | wgpu::BufferUsages::COPY_SRC);
        let density = make("total gas density", count * 4, storage);
        let enthalpy_a = make("enthalpy A", values * 4, storage | wgpu::BufferUsages::COPY_SRC);
        let enthalpy_b = make("enthalpy B", values * 4, storage | wgpu::BufferUsages::COPY_SRC);
        let velocity_x = make("velocity x", count * 4, storage | wgpu::BufferUsages::COPY_SRC);
        let velocity_y = make("velocity y", count * 4, storage | wgpu::BufferUsages::COPY_SRC);
        let solid = make("solid mask", count * 4, storage);
        let drift_y = make("species vertical drift", channels * 4, storage);
        let species_heat = make("species heat capacities", channels * 4, storage);
        let params = make("uniform parameters", 16,
            wgpu::BufferUsages::UNIFORM | wgpu::BufferUsages::COPY_DST);
        let readback = make("mass and enthalpy readback", readback_bytes as usize,
            wgpu::BufferUsages::MAP_READ | wgpu::BufferUsages::COPY_DST);

        let create_group = |mass_in: &wgpu::Buffer, mass_out: &wgpu::Buffer,
                            enthalpy_in: &wgpu::Buffer, enthalpy_out: &wgpu::Buffer| {
            let entries = [
                bind(0, mass_in),
                bind(1, &velocity_x),
                bind(2, &velocity_y),
                bind(3, &solid),
                bind(4, &drift_y),
                bind(5, mass_out),
                bind(6, &params),
                bind(7, enthalpy_in),
                bind(8, enthalpy_out),
            ];
            device.create_bind_group(&wgpu::BindGroupDescriptor {
                label: Some("multicomponent mass/enthalpy bindings"),
                layout: &pipeline.get_bind_group_layout(0),
                entries: &entries,
            })
        };
        let groups = [
            create_group(&mass_a, &mass_b, &enthalpy_a, &enthalpy_b),
            create_group(&mass_b, &mass_a, &enthalpy_b, &enthalpy_a),
        ];
        let diffusion_group = |mi:&wgpu::Buffer, ei:&wgpu::Buffer, mo:&wgpu::Buffer, eo:&wgpu::Buffer| {
            let entries = [bind(0,mi),bind(1,ei),bind(2,&solid),bind(3,mo),bind(4,eo),bind(5,&params),bind(6,&density)];
            device.create_bind_group(&wgpu::BindGroupDescriptor { label:Some("gas diffusion bindings"),
                layout:&diffusion_pipeline.get_bind_group_layout(0), entries:&entries })
        };
        let diffusion_groups = [diffusion_group(&mass_a,&enthalpy_a,&mass_b,&enthalpy_b),
            diffusion_group(&mass_b,&enthalpy_b,&mass_a,&enthalpy_a)];
        let cfl_buffers = [&velocity_x, &velocity_y, &drift_y, &cfl_maximum, &cfl_params];
        let cfl_entries: Vec<_> = cfl_buffers.iter().enumerate().map(|(i,b)| wgpu::BindGroupEntry {
            binding:i as u32, resource:b.as_entire_binding() }).collect();
        let cfl_group = device.create_bind_group(&wgpu::BindGroupDescriptor {
            label: Some("resident CFL bindings"), layout: &cfl_pipeline.get_bind_group_layout(0), entries: &cfl_entries });
        let sum_group = |mass: &wgpu::Buffer| {
            let entries = [bind(0, mass), bind(1, &density), bind(2, &params)];
            device.create_bind_group(&wgpu::BindGroupDescriptor { label: Some("gas density sum bindings"),
                layout: &density_sum_pipeline.get_bind_group_layout(0), entries: &entries })
        };
        let density_sum_groups = [sum_group(&mass_a), sum_group(&mass_b)];
        let density_group = |mass: &wgpu::Buffer| {
            let input = if channels == 1 { mass } else { &density };
            let entries = [bind(0, input), bind(1, &solid), bind(2, &velocity_x), bind(3, &velocity_y), bind(4, &params)];
            device.create_bind_group(&wgpu::BindGroupDescriptor { label: Some("gas density gradient bindings"),
                layout: &density_velocity_pipeline.get_bind_group_layout(0), entries: &entries })
        };
        let density_velocity_groups = [density_group(&mass_a), density_group(&mass_b)];
        if let Some(error) = scope.pop().await {
            return Err(error.to_string());
        }
        Ok(Self {
            device: context.device.clone(),
            queue: context.queue.clone(),
            pipeline, diffusion_pipeline, diffusion_groups,
            density_sum_pipeline, density_sum_groups, density_velocity_pipeline, density_velocity_groups,
            cfl_pipeline, cfl_group, cfl_maximum, cfl_params, cfl_readback,
            groups,
            mass_a,
            mass_b,
            enthalpy_a,
            enthalpy_b,
            velocity_x,
            velocity_y,
            solid,
            drift_y,
            species_heat,
            params,
            readback,
            width,
            height,
            channels,
            count,
            uploaded: false,
            current_is_a: true,
            max_cfl_rate: 0.0,
            velocity_externally_exposed: Cell::new(false),
            pending_cfl_dt: None,
            uploaded_drift: Vec::new(),
            uploaded_heat: Vec::new(),
            uploaded_solid: Vec::new(),
            uploaded_sparse: Vec::new(),
            liquid_projection: None,
            liquid_projection_busy: Arc::new(std::sync::atomic::AtomicBool::new(false)),
            gas_projection: None,
            liquid_preparation: None,
            liquid_sparse_preparation: None,
            adapter: info,
        })
    }

    /// Current device-local NHWC mass tensor after the most recent dispatch.
    pub fn current_mass_buffer(&self) -> &wgpu::Buffer {
        if self.current_is_a { &self.mass_a } else { &self.mass_b }
    }

    pub fn current_enthalpy_buffer(&self) -> &wgpu::Buffer {
        if self.current_is_a { &self.enthalpy_a } else { &self.enthalpy_b }
    }

    pub fn species_heat_buffer(&self) -> &wgpu::Buffer { &self.species_heat }

    /// Shared cell-centred velocity buffers, suitable for another operator on the same GpuContext.
    pub fn velocity_buffers(&self) -> (&wgpu::Buffer, &wgpu::Buffer) {
        // Another operator can mutate these GPU buffers; the host-side CFL
        // bound is no longer evidence for a later transport dispatch.
        self.velocity_externally_exposed.set(true);
        (&self.velocity_x, &self.velocity_y)
    }

    /// Shared solid-cell mask for other operators on the same GpuContext.
    pub fn solid_buffer(&self) -> &wgpu::Buffer {
        &self.solid
    }

    pub fn dimensions(&self) -> (usize, usize, usize) {
        (self.width, self.height, self.channels)
    }

    /// Encode concentration diffusion into a caller-owned command buffer.
    ///
    /// The operation consumes the pipeline's current device-local mass and
    /// enthalpy tensors and leaves the updated tensors resident on the GPU.
    /// It neither submits commands nor maps buffers.
    pub fn encode_resident_diffusion(&mut self, encoder: &mut wgpu::CommandEncoder) -> Result<(), String> {
        self.encode_diffusion(encoder)
    }

    /// Diffuse concentration on device without reading back species tensors.
    pub fn diffuse_resident(&mut self) -> Result<(), String> {
        let mut encoder=self.device.create_command_encoder(&Default::default());
        self.encode_diffusion(&mut encoder)?;
        self.queue.submit([encoder.finish()]);
        Ok(())
    }

    fn encode_diffusion(&mut self, encoder: &mut wgpu::CommandEncoder) -> Result<(), String> {
        if !self.uploaded || self.pending_cfl_dt.is_some() { return Err("diffusion requires uploaded state".into()); }
        let groups_x = (self.width * self.channels).div_ceil(8);
        let groups_y = self.height.div_ceil(8);
        if groups_x > self.device.limits().max_compute_workgroups_per_dimension as usize || groups_y > self.device.limits().max_compute_workgroups_per_dimension as usize {
            return Err("diffusion grid exceeds device limits".into());
        }
        self.queue.write_buffer(&self.params, 0, bytemuck::cast_slice(&[self.width as u32,self.height as u32,self.channels as u32,0]));
        { let mut pass=encoder.begin_compute_pass(&Default::default()); pass.set_pipeline(&self.density_sum_pipeline);
          pass.set_bind_group(0,&self.density_sum_groups[if self.current_is_a {0} else {1}],&[]);
          pass.dispatch_workgroups((self.width as u32).div_ceil(8),groups_y as u32,1); }
        { let mut pass=encoder.begin_compute_pass(&Default::default()); pass.set_pipeline(&self.diffusion_pipeline);
          pass.set_bind_group(0,&self.diffusion_groups[if self.current_is_a {0} else {1}],&[]);
          pass.dispatch_workgroups(groups_x as u32,groups_y as u32,1); }
        self.current_is_a = !self.current_is_a;
        Ok(())
    }

    /// Compute the density-gradient velocity on the GPU from the resident mass tensor.
    pub fn generate_density_velocity(&mut self) -> Result<(), String> {
        self.diffuse_and_generate_density_velocity(false)
    }

    /// Optionally diffuse first, then generate velocity in the same GPU submission.
    pub fn diffuse_and_generate_density_velocity(&mut self, diffuse: bool) -> Result<(), String> {
        let mut encoder = self.device.create_command_encoder(&Default::default());
        self.encode_diffuse_and_generate_density_velocity(&mut encoder,diffuse)?;
        self.queue.submit([encoder.finish()]);
        Ok(())
    }

    pub fn encode_diffuse_and_generate_density_velocity(&mut self, encoder:&mut wgpu::CommandEncoder, diffuse:bool) -> Result<(), String> {
        if !self.uploaded { return Err("density gradient requires uploaded state".into()); }
        if diffuse { self.encode_diffusion(encoder)?; }
        self.queue.write_buffer(&self.params, 0, bytemuck::cast_slice(&[
            self.width as u32, self.height as u32, self.channels as u32, 0,
        ]));
        if self.channels > 1 {
            let mut pass = encoder.begin_compute_pass(&Default::default());
            pass.set_pipeline(&self.density_sum_pipeline);
            pass.set_bind_group(0, &self.density_sum_groups[if self.current_is_a {0} else {1}], &[]);
            pass.dispatch_workgroups((self.width as u32).div_ceil(8), (self.height as u32).div_ceil(8), 1);
        }
        {
            let mut pass = encoder.begin_compute_pass(&Default::default());
            pass.set_pipeline(&self.density_velocity_pipeline);
            pass.set_bind_group(0, &self.density_velocity_groups[if self.current_is_a {0} else {1}], &[]);
            pass.dispatch_workgroups((self.width as u32).div_ceil(8), (self.height as u32).div_ceil(8), 1);
        }
        self.velocity_externally_exposed.set(true);
        Ok(())
    }

    pub fn upload(&mut self, state: &State<'_>) -> Result<(), String> {
        self.upload_impl(state, true, None, 1)
    }

    /// Upload material state; velocities will be generated on the GPU.
    /// Host velocity slices are unused and may be empty. Device velocities must
    /// pass the existing CFL reduction before transport results are exposed.
    pub fn upload_for_density_velocity(&mut self, state: &State<'_>) -> Result<(), String> {
        self.upload_impl(state, false, None, 1)
    }

    /// Upload masses and material properties while enthalpy is generated from temperatures on the GPU.
    /// Host velocity slices are unused, as with upload_for_density_velocity.
    pub fn upload_for_temperature(&mut self, state: &State<'_>, temperature: &[f32]) -> Result<(), String> {
        self.upload_impl(state, false, Some(temperature), 1)
    }

    /// Concentration diffusion for liquid-only channels. This deliberately
    /// does not run the gas pressure or advection model.
    pub fn diffuse_liquids(&mut self, state: &State<'_>, mass: &mut [f32], enthalpy: &mut [f32]) -> Result<(), String> {
        if mass.len() != self.count * self.channels || enthalpy.len() != mass.len() {
            return Err("liquid diffusion output dimensions mismatch".into());
        }
        self.upload_impl(state, false, None, 2)?;
        let result = self.diffuse_resident().and_then(|()| self.readback_state(mass, enthalpy));
        self.uploaded = false;
        result
    }

    fn prepare_liquids_from_temperature(&mut self, state: &State<'_>, temperature: &[f32]) -> Result<(), String> {
        let mut encoder=self.device.create_command_encoder(&Default::default());
        self.encode_liquids_from_temperature(&mut encoder,state,temperature)?;
        self.queue.submit([encoder.finish()]);
        Ok(())
    }

    fn encode_liquids_from_temperature(&mut self, encoder:&mut wgpu::CommandEncoder, state: &State<'_>, temperature: &[f32]) -> Result<(), String> {
        let values = self.count * self.channels;
        if values.div_ceil(64) > self.device.limits().max_compute_workgroups_per_dimension as usize {
            return Err("liquid enthalpy grid exceeds device limits".into());
        }
        self.upload_impl(state, false, Some(temperature), 2)?;
        if self.liquid_preparation.is_none() {
            let shader=self.device.create_shader_module(wgpu::ShaderModuleDescriptor {
                label:Some("liquid enthalpy"), source:wgpu::ShaderSource::Wgsl(LIQUID_ENTHALPY_SHADER.into()) });
            let pipeline=self.device.create_compute_pipeline(&wgpu::ComputePipelineDescriptor {
                label:Some("liquid enthalpy"),layout:None,module:&shader,entry_point:Some("main"),
                compilation_options:Default::default(),cache:None });
            let temp=self.device.create_buffer(&wgpu::BufferDescriptor {label:Some("liquid temperatures"),
                size:(self.count*4) as u64,usage:wgpu::BufferUsages::STORAGE|wgpu::BufferUsages::COPY_DST,mapped_at_creation:false});
            let buffers=[&temp,&self.mass_a,&self.species_heat,&self.enthalpy_a,&self.params];
            let entries:Vec<_>=buffers.iter().enumerate().map(|(i,b)| wgpu::BindGroupEntry {binding:i as u32,resource:b.as_entire_binding()}).collect();
            let group=self.device.create_bind_group(&wgpu::BindGroupDescriptor {label:Some("liquid enthalpy bindings"),
                layout:&pipeline.get_bind_group_layout(0),entries:&entries});
            self.liquid_preparation=Some(LiquidPreparation {pipeline,temperature:temp,group});
        }
        let prep=self.liquid_preparation.as_ref().ok_or("missing liquid preparation")?;
        self.queue.write_buffer(&prep.temperature,0,bytemuck::cast_slice(temperature));
        self.queue.write_buffer(&self.params,0,bytemuck::cast_slice(&[self.width as u32,self.height as u32,self.channels as u32,0]));
        {let mut pass=encoder.begin_compute_pass(&Default::default()); pass.set_pipeline(&prep.pipeline);
         pass.set_bind_group(0,&prep.group,&[]); pass.dispatch_workgroups(values.div_ceil(64) as u32,1,1);}
        Ok(())
    }

    pub fn diffuse_liquids_from_temperature(&mut self, state: &State<'_>, temperature: &[f32], mass: &mut [f32], enthalpy: &mut [f32]) -> Result<(), String> {
        if mass.len()!=self.count*self.channels || enthalpy.len()!=mass.len() { return Err("liquid output dimensions mismatch".into()); }
        self.prepare_liquids_from_temperature(state,temperature)?;
        let result=self.diffuse_resident().and_then(|()| self.readback_state(mass,enthalpy));
        self.uploaded=false;
        result
    }

    /// Diffuse then project on the device, transferring only two component
    /// masses, their channel IDs, and total enthalpy for each cell.
    pub fn diffuse_liquids_projected(&mut self, state: &State<'_>, output: &mut [ProjectedLiquidCell]) -> Result<bool, String> {
        self.upload_impl(state, false, None, 2)?;
        let encoder=self.device.create_command_encoder(&Default::default());
        self.readback_projected_liquids_after(encoder,output)
    }

    pub fn diffuse_liquids_from_temperature_projected(&mut self, state: &State<'_>, temperature: &[f32], output: &mut [ProjectedLiquidCell]) -> Result<bool, String> {
        let mut encoder=self.device.create_command_encoder(&Default::default());
        self.encode_liquids_from_temperature(&mut encoder,state,temperature)?;
        self.readback_projected_liquids_after(encoder,output)
    }

    pub fn diffuse_sparse_liquids_projected(&mut self, cells:&[SparseLiquidCell], solid:&[u32], species:&[SpeciesInfo], output:&mut [ProjectedLiquidCell]) -> Result<bool, String> {
        if output.len()!=self.count { return Err("sparse liquid output dimensions mismatch".into()); }
        self.begin_sparse_liquids_projected(cells,solid,species)?.finish(output)
    }

    pub fn begin_sparse_liquids_projected(&mut self, cells:&[SparseLiquidCell], solid:&[u32], species:&[SpeciesInfo]) -> Result<PendingLiquidProjection, String> {
        let mut encoder=self.device.create_command_encoder(&Default::default());
        self.encode_sparse(&mut encoder,cells,solid,species,2,None)?;
        self.begin_projected_liquids_after(encoder)
    }

    pub fn begin_packed_sparse_liquids_projected(&mut self, cells:&[PackedSparseCell], solid:&[u32], species:&[SpeciesInfo]) -> Result<PendingLiquidProjection, String> {
        let mut encoder=self.device.create_command_encoder(&Default::default());
        self.encode_sparse(&mut encoder,cells,solid,species,2,None)?;
        self.begin_projected_liquids_after(encoder)
    }

    /// Expand two-component cells into resident gas mass and enthalpy tensors.
    pub fn upload_sparse_gas(&mut self, cells:&[SparseLiquidCell], solid:&[u32], species:&[SpeciesInfo]) -> Result<(), String> {
        self.prepare_sparse(cells, solid, species, 1, None)
    }

    pub fn upload_sparse_gas_into_temperature(&mut self, cells:&[SparseLiquidCell], solid:&[u32], species:&[SpeciesInfo], temperature:&wgpu::Buffer) -> Result<(), String> {
        self.prepare_sparse(cells, solid, species, 1, Some(temperature))
    }

    pub fn encode_sparse_gas_into_temperature(&mut self, encoder:&mut wgpu::CommandEncoder, cells:&[SparseLiquidCell], solid:&[u32], species:&[SpeciesInfo], temperature:&wgpu::Buffer) -> Result<(), String> {
        self.encode_sparse(encoder,cells,solid,species,1,Some(temperature))
    }

    pub fn encode_packed_sparse_gas_into_temperature(&mut self, encoder:&mut wgpu::CommandEncoder, cells:&[PackedSparseCell], solid:&[u32], species:&[SpeciesInfo], temperature:&wgpu::Buffer) -> Result<(), String> {
        self.encode_sparse(encoder,cells,solid,species,1,Some(temperature))
    }

    fn prepare_sparse(&mut self, cells:&[SparseLiquidCell], solid:&[u32], species:&[SpeciesInfo], phase:u8, temperature:Option<&wgpu::Buffer>) -> Result<(), String> {
        let mut encoder=self.device.create_command_encoder(&Default::default());
        self.encode_sparse(&mut encoder,cells,solid,species,phase,temperature)?;
        self.queue.submit([encoder.finish()]);
        Ok(())
    }

    fn encode_sparse<T:SparseInput>(&mut self, encoder:&mut wgpu::CommandEncoder, cells:&[T], solid:&[u32], species:&[SpeciesInfo], phase:u8, temperature:Option<&wgpu::Buffer>) -> Result<(), String> {
        if self.liquid_projection_busy.load(std::sync::atomic::Ordering::Acquire) { return Err("liquid projection is still pending".into()); }
        self.uploaded=false; self.pending_cfl_dt=None; self.current_is_a=true;
        if cells.len()!=self.count || solid.len()!=self.count || species.len()!=self.channels {
            return Err("sparse input dimensions mismatch".into());
        }
        if temperature.is_some_and(|buffer| buffer.size() < (self.count * 4) as u64 || buffer.size() > self.device.limits().max_storage_buffer_binding_size as u64 || !buffer.usage().contains(wgpu::BufferUsages::STORAGE)) {
            return Err("sparse temperature destination is too small, too large or lacks STORAGE".into());
        }
        let mut ids=HashSet::with_capacity(self.channels);
        if species.iter().any(|s| s.phase!=phase || !s.specific_heat_capacity.is_finite() || s.specific_heat_capacity<=0.0 || !s.vertical_drift.is_finite() || !ids.insert(s.element_id)) {
            return Err("invalid sparse species metadata".into());
        }
        for (cell,&blocked) in cells.iter().zip(solid) {
            let cell=cell.unpack();
            if !cell.temperature.is_finite() || cell.temperature<0.0 || (blocked==0 && (cell.first as usize>=self.channels || cell.second as usize>=self.channels ||
                !cell.first_mass.is_finite() || cell.first_mass<0.0 || !cell.second_mass.is_finite() || cell.second_mass<0.0 ||
                cell.temperature<=0.0)) {
                return Err("invalid sparse liquid cell".into());
            }
            if blocked==0 {
                let min_mass = 1e-6f32;
                let combined = if cell.first==cell.second {cell.first_mass+cell.second_mass} else {cell.first_mass};
                let a = if combined > min_mass {
                    (combined * cell.temperature * species[cell.first as usize].specific_heat_capacity).max(f32::MIN_POSITIVE)
                } else {
                    0.0
                };
                let b = if cell.first != cell.second && cell.second_mass > min_mass {
                    (cell.second_mass * cell.temperature * species[cell.second as usize].specific_heat_capacity).max(f32::MIN_POSITIVE)
                } else {
                    0.0
                };
                if !a.is_finite() || !b.is_finite() {
                    return Err(format!("invalid sparse enthalpy: mass={combined:?},{:?} temperature={:?} heat={:?},{:?} energy={a:?},{b:?}", cell.second_mass, cell.temperature, species[cell.first as usize].specific_heat_capacity, species[cell.second as usize].specific_heat_capacity));
                }
            }
        }
        let compact=self.channels<=65536;
        let cell_size=if compact {std::mem::size_of::<CompactGasCell>()} else {std::mem::size_of::<SparseLiquidCell>()};
        let bytes=self.count.checked_mul(cell_size).ok_or("sparse liquid buffer overflow")? as u64;
        if bytes>self.device.limits().max_storage_buffer_binding_size as u64 || bytes>self.device.limits().max_buffer_size ||
            (self.count*self.channels).div_ceil(64)>self.device.limits().max_compute_workgroups_per_dimension as usize {
            return Err("sparse liquid grid exceeds device limits".into());
        }
        if self.liquid_sparse_preparation.is_none() {
            let source=if compact {LIQUID_COMPACT_SPARSE_SHADER} else {LIQUID_SPARSE_SHADER};
            let shader=self.device.create_shader_module(wgpu::ShaderModuleDescriptor {label:Some("sparse liquid expansion"),source:wgpu::ShaderSource::Wgsl(source.into())});
            let pipeline=self.device.create_compute_pipeline(&wgpu::ComputePipelineDescriptor {label:Some("sparse liquid expansion"),layout:None,module:&shader,entry_point:Some("main"),compilation_options:Default::default(),cache:None});
            let input=self.device.create_buffer(&wgpu::BufferDescriptor {label:Some("sparse liquid cells"),size:bytes,usage:wgpu::BufferUsages::STORAGE|wgpu::BufferUsages::COPY_DST,mapped_at_creation:false});
            let temperatures=self.device.create_buffer(&wgpu::BufferDescriptor {label:Some("sparse temperatures"),size:(self.count*4) as u64,usage:wgpu::BufferUsages::STORAGE|wgpu::BufferUsages::COPY_SRC,mapped_at_creation:false});
            let buffers=[&input,&self.solid,&self.mass_a,&self.enthalpy_a,&self.species_heat,&self.params,&temperatures];
            let entries:Vec<_>=buffers.iter().enumerate().map(|(i,b)|wgpu::BindGroupEntry {binding:i as u32,resource:b.as_entire_binding()}).collect();
            let group=self.device.create_bind_group(&wgpu::BindGroupDescriptor {label:Some("sparse liquid bindings"),layout:&pipeline.get_bind_group_layout(0),entries:&entries});
            self.liquid_sparse_preparation=Some(LiquidSparsePreparation {pipeline,cells:input,_temperatures:temperatures,group,temperature_groups:Vec::new()});
        }
        if self.uploaded_heat.len()!=self.channels || self.uploaded_heat.iter().zip(species).any(|(&bits,s)|bits!=s.specific_heat_capacity.to_bits()) {
            self.uploaded_heat=species.iter().map(|s|s.specific_heat_capacity.to_bits()).collect();
            self.queue.write_buffer(&self.species_heat,0,bytemuck::cast_slice(&self.uploaded_heat));
        }
        if self.uploaded_drift.len()!=self.channels || self.uploaded_drift.iter().zip(species).any(|(&bits,s)|bits!=s.vertical_drift.to_bits()) {
            self.uploaded_drift=species.iter().map(|s|s.vertical_drift.to_bits()).collect();
            self.queue.write_buffer(&self.drift_y,0,bytemuck::cast_slice(&self.uploaded_drift));
        }
        self.upload_solid(solid);
        self.queue.write_buffer(&self.params,0,bytemuck::cast_slice(&[self.width as u32,self.height as u32,self.channels as u32,0]));
        let prep=self.liquid_sparse_preparation.as_mut().ok_or("missing sparse liquid preparation")?;
        let selected=if let Some(destination)=temperature {
            let index=if let Some(index)=prep.temperature_groups.iter().position(|(buffer,_)|buffer==destination) {index} else {
                let buffers=[&prep.cells,&self.solid,&self.mass_a,&self.enthalpy_a,&self.species_heat,&self.params,destination];
                let entries:Vec<_>=buffers.iter().enumerate().map(|(i,b)|wgpu::BindGroupEntry {binding:i as u32,resource:b.as_entire_binding()}).collect();
                let group=self.device.create_bind_group(&wgpu::BindGroupDescriptor {label:Some("sparse gas temperature bindings"),layout:&prep.pipeline.get_bind_group_layout(0),entries:&entries});
                if prep.temperature_groups.len()>=2 {prep.temperature_groups.remove(0);}
                prep.temperature_groups.push((destination.clone(),group)); prep.temperature_groups.len()-1
            };
            &prep.temperature_groups[index].1
        } else {&prep.group};
        const BLOCK:usize=1024;
        let host_size=std::mem::size_of::<T>();
        let input_bytes:&[u8]=bytemuck::cast_slice(cells);
        let mut ranges:Vec<(usize,usize)>=Vec::new();
        if self.uploaded_sparse.len()!=input_bytes.len() {ranges.push((0,cells.len()));}
        else {
            for start in (0..cells.len()).step_by(BLOCK) {
                let end=(start+BLOCK).min(cells.len());
                if self.uploaded_sparse[start*host_size..end*host_size] != input_bytes[start*host_size..end*host_size] {
                    if let Some((_,last))=ranges.last_mut() {if *last==start {*last=end;continue;}}
                    ranges.push((start,end));
                }
            }
            if ranges.len()>16 {ranges.clear();ranges.push((0,cells.len()));}
        }
        let mut uploaded_bytes=0usize;
        for &(start,end) in &ranges {
            if compact && T::PACKED && cfg!(target_endian="little") {
                self.queue.write_buffer(&prep.cells,(start*cell_size) as u64,&input_bytes[start*host_size..end*host_size]);
            } else if compact {
                let packed:Vec<_>=cells[start..end].iter().map(|cell|cell.unpack()).map(|cell|CompactGasCell {
                    indices:(cell.first&0xffff)|((cell.second&0xffff)<<16),first_mass:cell.first_mass,
                    second_mass:cell.second_mass,temperature:cell.temperature }).collect();
                self.queue.write_buffer(&prep.cells,(start*cell_size) as u64,bytemuck::cast_slice(&packed));
            } else if T::PACKED {
                let unpacked:Vec<_>=cells[start..end].iter().map(|cell|cell.unpack()).collect();
                self.queue.write_buffer(&prep.cells,(start*cell_size) as u64,bytemuck::cast_slice(&unpacked));
            } else {
                self.queue.write_buffer(&prep.cells,(start*cell_size) as u64,bytemuck::cast_slice(&cells[start..end]));
            }
            uploaded_bytes+=(end-start)*cell_size;
        }
        if self.uploaded_sparse.len()!=input_bytes.len() {self.uploaded_sparse=input_bytes.to_vec();}
        else {for &(start,end) in &ranges {self.uploaded_sparse[start*host_size..end*host_size].copy_from_slice(&input_bytes[start*host_size..end*host_size]);}}
        static PROFILE:std::sync::OnceLock<bool>=std::sync::OnceLock::new();
        static SAMPLE:std::sync::atomic::AtomicUsize=std::sync::atomic::AtomicUsize::new(0);
        if *PROFILE.get_or_init(||std::env::var_os("CYCLETRIM_GPU_PROFILE").is_some())
            && SAMPLE.fetch_add(1,std::sync::atomic::Ordering::Relaxed)%127==0 {
            eprintln!("[CycleTrim OniSim] sparse profile {}x{}x{} bytes={uploaded_bytes}",self.width,self.height,self.channels);
        }
        {let mut pass=encoder.begin_compute_pass(&Default::default()); pass.set_pipeline(&prep.pipeline);pass.set_bind_group(0,selected,&[]);
         pass.dispatch_workgroups((self.count*self.channels).div_ceil(64) as u32,1,1);}
        self.uploaded=true;
        self.velocity_externally_exposed.set(true);
        Ok(())
    }

    fn readback_projected_liquids_after(&mut self, encoder:wgpu::CommandEncoder, output: &mut [ProjectedLiquidCell]) -> Result<bool, String> {
        if output.len() != self.count { return Err("liquid projection output dimensions mismatch".into()); }
        self.begin_projected_liquids_after(encoder)?.finish(output)
    }

    fn begin_projected_liquids_after(&mut self, mut encoder:wgpu::CommandEncoder) -> Result<PendingLiquidProjection, String> {
        let compact=self.channels<=32768;
        let cell_size=if compact {std::mem::size_of::<CompactGasCell>()} else {std::mem::size_of::<ProjectedLiquidCell>()};
        let bytes = self.count.checked_mul(cell_size).ok_or("liquid projection overflow")? as u64;
        if bytes > self.device.limits().max_storage_buffer_binding_size as u64 || bytes > self.device.limits().max_buffer_size ||
            self.count.div_ceil(64) > self.device.limits().max_compute_workgroups_per_dimension as usize {
            return Err("liquid projection exceeds device limits".into());
        }
        if self.liquid_projection.is_none() {
            let shader = self.device.create_shader_module(wgpu::ShaderModuleDescriptor {
                label:Some("liquid projection"), source:wgpu::ShaderSource::Wgsl((if compact {LIQUID_COMPACT_PROJECTION_SHADER} else {LIQUID_PROJECTION_SHADER}).into()) });
            let pipeline = self.device.create_compute_pipeline(&wgpu::ComputePipelineDescriptor {
                label:Some("liquid projection"), layout:None, module:&shader, entry_point:Some("main"),
                compilation_options:Default::default(), cache:None });
            let buffer = |label, size, usage| self.device.create_buffer(&wgpu::BufferDescriptor {
                label:Some(label), size, usage, mapped_at_creation:false });
            let params=buffer("liquid projection params",16,wgpu::BufferUsages::UNIFORM|wgpu::BufferUsages::COPY_DST);
            let output=buffer("liquid projected cells",bytes,wgpu::BufferUsages::STORAGE|wgpu::BufferUsages::COPY_SRC);
            let readback=buffer("liquid projected readback",bytes,wgpu::BufferUsages::MAP_READ|wgpu::BufferUsages::COPY_DST);
            let group=|mass:&wgpu::Buffer, enthalpy:&wgpu::Buffer| {
                let buffers=[mass,enthalpy,&output,&params];
                let entries:Vec<_>=buffers.iter().enumerate().map(|(i,b)| wgpu::BindGroupEntry {binding:i as u32,resource:b.as_entire_binding()}).collect();
                self.device.create_bind_group(&wgpu::BindGroupDescriptor {label:Some("liquid projection bindings"),layout:&pipeline.get_bind_group_layout(0),entries:&entries})
            };
            let groups=[group(&self.mass_a,&self.enthalpy_a),group(&self.mass_b,&self.enthalpy_b)];
            self.liquid_projection = Some(LiquidProjection { pipeline,params,output,readback,groups:Some(groups),temperature_groups:Vec::new() });
        }
        self.encode_diffusion(&mut encoder)?;
        let projection = self.liquid_projection.as_ref().ok_or("missing liquid projection")?;
        self.queue.write_buffer(&projection.params,0,bytemuck::cast_slice(&[self.count as u32,self.channels as u32,0,0]));
        let group=&projection.groups.as_ref().ok_or("missing liquid projection bindings")?[if self.current_is_a {0} else {1}];
        { let mut pass=encoder.begin_compute_pass(&Default::default()); pass.set_pipeline(&projection.pipeline);
          pass.set_bind_group(0,group,&[]); pass.dispatch_workgroups(self.count.div_ceil(64) as u32,1,1); }
        encoder.copy_buffer_to_buffer(&projection.output,0,&projection.readback,0,bytes);
        let submission=self.queue.submit([encoder.finish()]);
        let (send,receive)=mpsc::sync_channel(1);
        projection.readback.map_async(wgpu::MapMode::Read,..,move |result| {let _=send.send(result);});
        self.uploaded=false;
        self.liquid_projection_busy.store(true, std::sync::atomic::Ordering::Release);
        Ok(PendingLiquidProjection {device:self.device.clone(), readback:projection.readback.clone(), submission, result:receive, count:self.count, busy:self.liquid_projection_busy.clone(),compact})
    }

    fn upload_solid(&mut self, solid:&[u32]) {
        const BLOCK:usize=4096;
        let mut ranges:Vec<(usize,usize)>=Vec::new();
        if self.uploaded_solid.len()!=solid.len() {
            ranges.push((0,solid.len()));
        } else {
            for start in (0..solid.len()).step_by(BLOCK) {
                let end=(start+BLOCK).min(solid.len());
                if self.uploaded_solid[start..end]!=solid[start..end] {
                    if let Some((_,last))=ranges.last_mut() {
                        if *last==start { *last=end; continue; }
                    }
                    ranges.push((start,end));
                }
            }
            if ranges.len()>16 {ranges.clear();ranges.push((0,solid.len()));}
        }
        let mut bytes=0usize;
        for &(start,end) in &ranges {
            self.queue.write_buffer(&self.solid,(start*4) as u64,bytemuck::cast_slice(&solid[start..end]));
            bytes+=(end-start)*4;
        }
        if self.uploaded_solid.len()!=solid.len() {
            self.uploaded_solid.clear();
            self.uploaded_solid.extend_from_slice(solid);
        } else {
            for &(start,end) in &ranges {
                self.uploaded_solid[start..end].copy_from_slice(&solid[start..end]);
            }
        }
        static PROFILE:std::sync::OnceLock<bool>=std::sync::OnceLock::new();
        static SAMPLE:std::sync::atomic::AtomicUsize=std::sync::atomic::AtomicUsize::new(0);
        if *PROFILE.get_or_init(||std::env::var_os("CYCLETRIM_GPU_PROFILE").is_some())
            && SAMPLE.fetch_add(1,std::sync::atomic::Ordering::Relaxed)%127==0 {
            eprintln!("[CycleTrim OniSim] solid profile {}x{}x{} uploaded={} bytes={bytes}",self.width,self.height,self.channels,!ranges.is_empty());
        }
    }

    fn upload_impl(&mut self, state: &State<'_>, upload_velocity: bool, temperature: Option<&[f32]>, phase: u8) -> Result<(), String> {
        if self.liquid_projection_busy.load(std::sync::atomic::Ordering::Acquire) { return Err("liquid projection is still pending".into()); }
        self.uploaded = false;
        self.pending_cfl_dt = None;
        self.current_is_a = true;
        let max_rate = if upload_velocity {
            validate_state_for_phase::<true>(state, self.count, self.channels, temperature, phase)?
        } else {
            validate_state_for_phase::<false>(state, self.count, self.channels, temperature, phase)?
        };
        self.queue.write_buffer(&self.mass_a, 0, bytemuck::cast_slice(state.mass));
        if temperature.is_none() {
            self.queue.write_buffer(&self.enthalpy_a, 0, bytemuck::cast_slice(state.enthalpy));
        }
        if upload_velocity {
            self.queue.write_buffer(&self.velocity_x, 0, bytemuck::cast_slice(state.velocity_x));
            self.queue.write_buffer(&self.velocity_y, 0, bytemuck::cast_slice(state.velocity_y));
        }
        self.upload_solid(state.solid);
        if self.uploaded_drift.len() != self.channels || self.uploaded_drift.iter().zip(state.species)
            .any(|(&bits, species)| bits != species.vertical_drift.to_bits()) {
            self.uploaded_drift = state.species.iter().map(|species| species.vertical_drift.to_bits()).collect();
            self.queue.write_buffer(&self.drift_y, 0, bytemuck::cast_slice(&self.uploaded_drift));
        }
        if self.uploaded_heat.len() != self.channels || self.uploaded_heat.iter().zip(state.species)
            .any(|(&bits, species)| bits != species.specific_heat_capacity.to_bits()) {
            self.uploaded_heat = state.species.iter().map(|species| species.specific_heat_capacity.to_bits()).collect();
            self.queue.write_buffer(&self.species_heat, 0, bytemuck::cast_slice(&self.uploaded_heat));
        }
        self.max_cfl_rate = max_rate;
        self.velocity_externally_exposed.set(!upload_velocity);
        self.uploaded = true;
        Ok(())
    }

    fn cfl_parameters(&self) -> [u32; 4] {
        let min = self.uploaded_drift.iter().map(|&v| f32::from_bits(v)).fold(f32::INFINITY, f32::min);
        let max = self.uploaded_drift.iter().map(|&v| f32::from_bits(v)).fold(f32::NEG_INFINITY, f32::max);
        [self.count as u32, self.channels as u32, min.to_bits(), max.to_bits()]
    }

    /// Recompute the CFL rate after another GPU operator modified velocity.
    /// Only one 32-bit scalar is copied to the host.
    pub fn revalidate_velocity(&mut self) -> Result<f32, String> {
        if !self.uploaded || self.pending_cfl_dt.is_some() || self.count.div_ceil(64) > self.device.limits().max_compute_workgroups_per_dimension as usize {
            return Err("CFL reduction requires uploaded state and a supported grid".into());
        }
        self.queue.write_buffer(&self.cfl_maximum,0,bytemuck::bytes_of(&0u32));
        self.queue.write_buffer(&self.cfl_params,0,bytemuck::cast_slice(&self.cfl_parameters()));
        let mut encoder=self.device.create_command_encoder(&Default::default());
        { let mut pass=encoder.begin_compute_pass(&Default::default()); pass.set_pipeline(&self.cfl_pipeline);pass.set_bind_group(0,&self.cfl_group,&[]);pass.dispatch_workgroups(self.count.div_ceil(64) as u32,1,1); }
        encoder.copy_buffer_to_buffer(&self.cfl_maximum,0,&self.cfl_readback,0,4);
        let submission=self.queue.submit([encoder.finish()]);
        let (send,receive)=mpsc::sync_channel(1);
        self.cfl_readback.map_async(wgpu::MapMode::Read,..,move |r|{let _=send.send(r);});
        self.device.poll(wgpu::PollType::Wait{submission_index:Some(submission),timeout:Some(Duration::from_secs(30))}).map_err(|e|e.to_string())?;
        receive.recv_timeout(Duration::from_secs(2)).map_err(|e|e.to_string())?.map_err(|e|e.to_string())?;
        let rate={let view=self.cfl_readback.get_mapped_range(..).map_err(|e|e.to_string())?;f32::from_bits(u32::from_le_bytes(view[..4].try_into().unwrap()))};
        self.cfl_readback.unmap();
        if !rate.is_finite() { return Err("non-finite projected velocity".into()); }
        self.max_cfl_rate=rate;
        self.velocity_externally_exposed.set(false);
        Ok(rate)
    }

    /// Advance the current device-local mass/enthalpy tensors without copying them to the CPU.
    pub fn dispatch_resident(&mut self, steps: usize, dt: f32) -> Result<(), String> {
        if !self.uploaded || self.pending_cfl_dt.is_some() || self.velocity_externally_exposed.get() || steps == 0 || !dt.is_finite() || dt <= 0.0 || dt * self.max_cfl_rate > 1.0 {
            return Err("resident dispatch requires uploaded state, positive steps/dt and the validated CFL bound".into());
        }
        self.dispatch_unchecked(steps, dt);
        Ok(())
    }

    /// Append validated substeps to a caller-owned command encoder.
    pub fn encode_resident(&mut self, encoder: &mut wgpu::CommandEncoder, steps: usize, dt: f32) -> Result<(), String> {
        if !self.uploaded || self.pending_cfl_dt.is_some() || self.velocity_externally_exposed.get()
            || steps == 0 || !dt.is_finite() || dt <= 0.0 || dt * self.max_cfl_rate > 1.0 {
            return Err("resident dispatch requires uploaded state, positive steps/dt and the validated CFL bound".into());
        }
        self.write_transport_params(dt);
        self.encode_transport(encoder, steps);
        Ok(())
    }

    /// Queue reduction and transport together; validate the CFL before exposing results to the host.
    pub fn dispatch_with_deferred_cfl(&mut self, dt: f32) -> Result<(), String> {
        let mut encoder = self.device.create_command_encoder(&Default::default());
        self.encode_with_deferred_cfl(&mut encoder, dt)?;
        self.queue.submit([encoder.finish()]);
        Ok(())
    }

    /// Append CFL reduction and transport after a velocity-producing GPU pass.
    pub fn encode_with_deferred_cfl(&mut self, encoder: &mut wgpu::CommandEncoder, dt: f32) -> Result<(), String> {
        if !self.uploaded || self.pending_cfl_dt.is_some() || !self.velocity_externally_exposed.get()
            || !dt.is_finite() || dt <= 0.0 || self.count.div_ceil(64) > self.device.limits().max_compute_workgroups_per_dimension as usize {
            return Err("deferred CFL requires uploaded state, changed velocity and positive timestep".into());
        }
        self.queue.write_buffer(&self.cfl_maximum, 0, bytemuck::bytes_of(&0u32));
        self.queue.write_buffer(&self.cfl_params, 0, bytemuck::cast_slice(&self.cfl_parameters()));
        self.write_transport_params(dt);
        { let mut pass=encoder.begin_compute_pass(&Default::default()); pass.set_pipeline(&self.cfl_pipeline);
          pass.set_bind_group(0,&self.cfl_group,&[]); pass.dispatch_workgroups(self.count.div_ceil(64) as u32,1,1); }
        self.encode_transport(encoder, 1);
        self.pending_cfl_dt = Some(dt);
        Ok(())
    }

    fn write_transport_params(&self, dt: f32) {
        self.queue.write_buffer(&self.params, 0, bytemuck::cast_slice(&[
            self.width as u32, self.height as u32, self.channels as u32, dt.to_bits()
        ]));
    }

    fn dispatch_unchecked(&mut self, steps: usize, dt: f32) {
        self.write_transport_params(dt);
        let mut encoder = self.device.create_command_encoder(&Default::default());
        self.encode_transport(&mut encoder, steps);
        self.queue.submit([encoder.finish()]);
    }

    fn encode_transport(&mut self, encoder: &mut wgpu::CommandEncoder, steps: usize) {
        for _ in 0..steps {
            let group = if self.current_is_a { &self.groups[0] } else { &self.groups[1] };
            let mut pass = encoder.begin_compute_pass(&Default::default());
            pass.set_pipeline(&self.pipeline);
            pass.set_bind_group(0, group, &[]);
            pass.dispatch_workgroups(((self.width * self.channels) as u32).div_ceil(8), (self.height as u32).div_ceil(8), 1);
            self.current_is_a = !self.current_is_a;
        }
    }

    /// Read the current mass tensor only when a CPU consumer actually needs it.
    pub fn readback_current(&mut self, output: &mut [f32]) -> Result<(), String> {
        let values = self.count * self.channels;
        if !self.uploaded || self.pending_cfl_dt.is_some() || output.len() != values {
            return Err("readback requires uploaded state and a matching output tensor".into());
        }
        let final_buffer = if self.current_is_a { &self.mass_a } else { &self.mass_b };
        let bytes = (values * 4) as u64;
        let mut encoder = self.device.create_command_encoder(&Default::default());
        encoder.copy_buffer_to_buffer(final_buffer, 0, &self.readback, 0, bytes);
        let submission = self.queue.submit([encoder.finish()]);
        let (send, receive) = mpsc::sync_channel(1);
        self.readback.map_async(wgpu::MapMode::Read, 0..bytes, move |r| { let _ = send.send(r); });
        self.device.poll(wgpu::PollType::Wait {
            submission_index: Some(submission),
            timeout: Some(Duration::from_secs(30)),
        }).map_err(|e| e.to_string())?;
        receive.recv_timeout(Duration::from_secs(2)).map_err(|e| e.to_string())?.map_err(|e| e.to_string())?;
        {
            let view = self.readback.get_mapped_range(0..bytes).map_err(|e| e.to_string())?;
            output.copy_from_slice(bytemuck::cast_slice(&view));
        }
        self.readback.unmap();
        Ok(())
    }

    pub fn run_steps_and_readback(
        &mut self,
        steps: usize,
        dt: f32,
        mass_output: &mut [f32],
        enthalpy_output: &mut [f32],
    ) -> Result<(), String> {
        let values = self.count * self.channels;
        if !self.uploaded || steps == 0 || mass_output.len() != values
            || enthalpy_output.len() != values || !dt.is_finite() || dt <= 0.0
            || dt * self.max_cfl_rate > 1.0 {
            return Err("resident run requires valid uploaded state, positive steps/dt and matching outputs".into());
        }
        self.dispatch_resident(steps, dt)?;
        self.readback_state(mass_output, enthalpy_output)
    }

    /// Read both current tensors together after any resident operator chain.
    pub fn readback_mass_and_temperature(&mut self, temperature: &wgpu::Buffer,
        mass_output: &mut [f32], temperature_output: &mut [f32]) -> Result<(), String> {
        let encoder=self.device.create_command_encoder(&Default::default());
        self.readback_mass_and_temperature_after(encoder,temperature,mass_output,temperature_output)
    }

    pub fn readback_mass_and_temperature_after(&mut self, mut encoder:wgpu::CommandEncoder, temperature:&wgpu::Buffer,
        mass_output:&mut [f32], temperature_output:&mut [f32]) -> Result<(),String> {
        let values = self.count * self.channels;
        let mass_bytes = (values * 4) as u64;
        let temperature_bytes = (self.count * 4) as u64;
        if !self.uploaded || mass_output.len() != values || temperature_output.len() != self.count
            || temperature.size() < temperature_bytes {
            return Err("readback requires uploaded state and matching mass/temperature buffers".into());
        }
        encoder.copy_buffer_to_buffer(self.current_mass_buffer(), 0, &self.readback, 0, mass_bytes);
        encoder.copy_buffer_to_buffer(temperature, 0, &self.readback, mass_bytes, temperature_bytes);
        if self.pending_cfl_dt.is_some() {
            encoder.copy_buffer_to_buffer(&self.cfl_maximum, 0, &self.readback, mass_bytes + temperature_bytes, 4);
        }
        let submission = self.queue.submit([encoder.finish()]);
        let (send, receive) = mpsc::sync_channel(1);
        self.readback.map_async(wgpu::MapMode::Read, .., move |r| { let _ = send.send(r); });
        self.device.poll(wgpu::PollType::Wait { submission_index: Some(submission),
            timeout: Some(Duration::from_secs(30)) }).map_err(|e| e.to_string())?;
        receive.recv_timeout(Duration::from_secs(2)).map_err(|e| e.to_string())?.map_err(|e| e.to_string())?;
        let mut invalid_cfl = false;
        let mut checked_rate = None;
        {
            let view = self.readback.get_mapped_range(..).map_err(|e| e.to_string())?;
            let result: &[f32] = bytemuck::cast_slice(&view);
            if let Some(dt) = self.pending_cfl_dt {
                let rate = result[values+self.count];
                checked_rate = Some(rate);
                invalid_cfl = !rate.is_finite() || !(dt * rate).is_finite() || dt * rate > 1.0;
            }
            if !invalid_cfl {
                mass_output.copy_from_slice(&result[..values]);
                temperature_output.copy_from_slice(&result[values..values+self.count]);
            }
        }
        self.readback.unmap();
        if invalid_cfl { self.uploaded = false; self.pending_cfl_dt = None; return Err("projected velocity exceeds CFL bound or is non-finite".into()); }
        if self.pending_cfl_dt.take().is_some() {
            self.max_cfl_rate = checked_rate.ok_or("missing CFL result")?;
            self.velocity_externally_exposed.set(false);
        }
        Ok(())
    }

    /// Project mass on device and include temperature and deferred CFL in the
    /// same readback. No caller output is modified on overflow or bad CFL.
    pub fn readback_projected_gas(&mut self, temperature:&wgpu::Buffer, output:&mut [ProjectedLiquidCell]) -> Result<bool,String> {
        let encoder=self.device.create_command_encoder(&Default::default());
        self.readback_projected_gas_after(encoder,temperature,output)
    }

    /// Append projection and readback after previously encoded device work.
    pub fn readback_projected_gas_after(&mut self, mut encoder:wgpu::CommandEncoder, temperature:&wgpu::Buffer, output:&mut [ProjectedLiquidCell]) -> Result<bool,String> {
        let compact=self.channels<=32768;
        let cell_size=if compact {std::mem::size_of::<CompactGasCell>()} else {std::mem::size_of::<ProjectedLiquidCell>()};
        let bytes=self.count.checked_mul(cell_size).ok_or("gas projection overflow")? as u64;
        if !self.uploaded || output.len()!=self.count || temperature.size()<(self.count*4) as u64 ||
            bytes>self.device.limits().max_storage_buffer_binding_size as u64 || bytes+4>self.device.limits().max_buffer_size ||
            self.count.div_ceil(64)>self.device.limits().max_compute_workgroups_per_dimension as usize {
            return Err("invalid gas projection buffers".into());
        }
        if self.gas_projection.is_none() {
            let source=if compact {GAS_COMPACT_PROJECTION_SHADER} else {GAS_PROJECTION_SHADER};
            let shader=self.device.create_shader_module(wgpu::ShaderModuleDescriptor {label:Some("gas projection"),source:wgpu::ShaderSource::Wgsl(source.into())});
            let pipeline=self.device.create_compute_pipeline(&wgpu::ComputePipelineDescriptor {label:Some("gas projection"),layout:None,module:&shader,entry_point:Some("main"),compilation_options:Default::default(),cache:None});
            let buffer=|label,size,usage|self.device.create_buffer(&wgpu::BufferDescriptor {label:Some(label),size,usage,mapped_at_creation:false});
            self.gas_projection=Some(LiquidProjection {pipeline, groups:None, temperature_groups:Vec::new(),
                params:buffer("gas projection params",16,wgpu::BufferUsages::UNIFORM|wgpu::BufferUsages::COPY_DST),
                output:buffer("gas projection cells",bytes,wgpu::BufferUsages::STORAGE|wgpu::BufferUsages::COPY_SRC),
                readback:buffer("gas projection readback",bytes+4,wgpu::BufferUsages::MAP_READ|wgpu::BufferUsages::COPY_DST)});
        }
        let projection=self.gas_projection.as_mut().ok_or("missing gas projection")?;
        self.queue.write_buffer(&projection.params,0,bytemuck::cast_slice(&[self.count as u32,self.channels as u32,0,0]));
        let index=if let Some(index)=projection.temperature_groups.iter().position(|(buffer,_)|buffer==temperature) {index} else {
            let make_group=|mass:&wgpu::Buffer| {
                let buffers=[mass,temperature,&projection.output,&projection.params];
                let entries:Vec<_>=buffers.iter().enumerate().map(|(i,b)|wgpu::BindGroupEntry {binding:i as u32,resource:b.as_entire_binding()}).collect();
                self.device.create_bind_group(&wgpu::BindGroupDescriptor {label:Some("gas projection bindings"),layout:&projection.pipeline.get_bind_group_layout(0),entries:&entries})
            };
            let groups=[make_group(&self.mass_a),make_group(&self.mass_b)];
            if projection.temperature_groups.len()>=2 {projection.temperature_groups.remove(0);}
            projection.temperature_groups.push((temperature.clone(),groups)); projection.temperature_groups.len()-1
        };
        let group=&projection.temperature_groups[index].1[if self.current_is_a {0} else {1}];
        {let mut pass=encoder.begin_compute_pass(&Default::default());pass.set_pipeline(&projection.pipeline);pass.set_bind_group(0,group,&[]);pass.dispatch_workgroups(self.count.div_ceil(64) as u32,1,1);}
        encoder.copy_buffer_to_buffer(&projection.output,0,&projection.readback,0,bytes);
        if self.pending_cfl_dt.is_some() {encoder.copy_buffer_to_buffer(&self.cfl_maximum,0,&projection.readback,bytes,4);}
        let submission=self.queue.submit([encoder.finish()]);
        let (send,receive)=mpsc::sync_channel(1);
        projection.readback.map_async(wgpu::MapMode::Read,..,move|result|{let _=send.send(result);});
        self.device.poll(wgpu::PollType::Wait {submission_index:Some(submission),timeout:Some(Duration::from_secs(30))}).map_err(|e|e.to_string())?;
        receive.recv_timeout(Duration::from_secs(2)).map_err(|e|e.to_string())?.map_err(|e|e.to_string())?;
        let mut invalid_cfl=false; let mut checked_rate=None; let valid;
        {let view=projection.readback.get_mapped_range(..).map_err(|e|e.to_string())?;
         if let Some(dt)=self.pending_cfl_dt {let rate=f32::from_bits(u32::from_le_bytes(view[bytes as usize..bytes as usize+4].try_into().unwrap()));
             checked_rate=Some(rate);invalid_cfl=!rate.is_finite() || rate*dt>1.0;}
         if compact {
             let cells:&[CompactGasCell]=bytemuck::cast_slice(&view[..bytes as usize]);
             valid=cells.iter().all(|cell|cell.indices&0x8000_0000==0);
             if valid && !invalid_cfl {for (dst,cell) in output.iter_mut().zip(cells) {
                 *dst=ProjectedLiquidCell {first:cell.indices&0xffff,second:(cell.indices>>16)&0x7fff,
                     first_mass:cell.first_mass,second_mass:cell.second_mass,enthalpy:cell.temperature,overflow:0};
             }}
         } else {
             let cells:&[ProjectedLiquidCell]=bytemuck::cast_slice(&view[..bytes as usize]);
             valid=cells.iter().all(|cell|cell.overflow==0);
             if valid && !invalid_cfl {output.copy_from_slice(cells);}
         }}
        projection.readback.unmap();
        if invalid_cfl {self.uploaded=false;self.pending_cfl_dt=None;return Err("projected velocity exceeds CFL bound or is non-finite".into());}
        if self.pending_cfl_dt.take().is_some() {self.max_cfl_rate=checked_rate.ok_or("missing CFL result")?;self.velocity_externally_exposed.set(false);}
        Ok(valid)
    }

    pub fn readback_state(&mut self, mass_output: &mut [f32], enthalpy_output: &mut [f32]) -> Result<(), String> {
        let values = self.count * self.channels;
        if !self.uploaded || self.pending_cfl_dt.is_some() || mass_output.len() != values || enthalpy_output.len() != values {
            return Err("readback requires uploaded state and matching output tensors".into());
        }
        let (mass_final, enthalpy_final) = if self.current_is_a {
            (&self.mass_a, &self.enthalpy_a)
        } else {
            (&self.mass_b, &self.enthalpy_b)
        };
        let bytes = (values * 4) as u64;
        let mut encoder = self.device.create_command_encoder(&Default::default());
        encoder.copy_buffer_to_buffer(mass_final, 0, &self.readback, 0, bytes);
        encoder.copy_buffer_to_buffer(enthalpy_final, 0, &self.readback, bytes, bytes);
        let submission = self.queue.submit([encoder.finish()]);
        let (send, receive) = mpsc::sync_channel(1);
        self.readback.map_async(wgpu::MapMode::Read, .., move |result| {
            let _ = send.send(result);
        });
        self.device.poll(wgpu::PollType::Wait {
            submission_index: Some(submission),
            timeout: Some(Duration::from_secs(30)),
        }).map_err(|e| e.to_string())?;
        receive.recv_timeout(Duration::from_secs(2)).map_err(|e| e.to_string())?
            .map_err(|e| e.to_string())?;
        {
            let view = self.readback.get_mapped_range(..).map_err(|e| e.to_string())?;
            let values_read: &[f32] = bytemuck::cast_slice(&view);
            mass_output.copy_from_slice(&values_read[..values]);
            enthalpy_output.copy_from_slice(&values_read[values..values * 2]);
        }
        self.readback.unmap();
        Ok(())
    }
}


/// Validates the CPU-side channel catalogue and conserved state before any GPU
/// buffer is mutated. This is also used by CPU-only regression tests.
fn validate_state(state: &State<'_>, count: usize, channels: usize) -> Result<f32, String> {
    validate_state_with_temperature(state, count, channels, None)
}

fn validate_state_with_temperature(state: &State<'_>, count: usize, channels: usize, temperature: Option<&[f32]>) -> Result<f32, String> {
    validate_state_for_phase::<true>(state, count, channels, temperature, 1)
}

fn validate_state_for_phase<const HOST_VELOCITY: bool>(state: &State<'_>, count: usize, channels: usize, temperature: Option<&[f32]>, phase: u8) -> Result<f32, String> {
    let values = count.checked_mul(channels).ok_or("tensor dimensions overflow")?;
    if state.mass.len() != values || (temperature.is_none() && state.enthalpy.len() != values)
        || temperature.is_some_and(|t| t.len() != count)
        || (HOST_VELOCITY && (state.velocity_x.len() != count || state.velocity_y.len() != count))
        || state.solid.len() != count || state.species.len() != channels {
        return Err("input tensor or species metadata dimensions mismatch".into());
    }
    let mut ids = HashSet::with_capacity(channels);
    for (channel, species) in state.species.iter().enumerate() {
        if species.phase != phase {
            return Err(format!("channel {channel} has the wrong phase for this operator"));
        }
        if !species.specific_heat_capacity.is_finite()
            || species.specific_heat_capacity <= 0.0
            || !species.vertical_drift.is_finite()
            || !ids.insert(species.element_id) {
            return Err(format!("invalid or duplicate gas species metadata at channel {channel}"));
        }
    }
    // |vy + drift| reaches its maximum at one of the drift extremes.
    // Compute those extremes once instead of scanning every species for
    // every cell in a potentially large region.
    let (min_drift, max_drift) = if HOST_VELOCITY {
        (state.species.iter().map(|s| s.vertical_drift).fold(f32::INFINITY, f32::min),
            state.species.iter().map(|s| s.vertical_drift).fold(f32::NEG_INFINITY, f32::max))
    } else { (0.0, 0.0) };
    if state.mass.iter().any(|v| !v.is_finite() || *v < 0.0)
        || state.enthalpy.iter().any(|v| !v.is_finite() || *v < 0.0)
        || (HOST_VELOCITY && state.velocity_x.iter().chain(state.velocity_y).any(|v| !v.is_finite())) {
        return Err("state contains negative mass/enthalpy or non-finite values".into());
    }
    let mut max_rate = 0.0f32;
    for cell in 0..count {
        let start = cell * channels;
        let end = start + channels;
        if state.solid[cell] != 0
            && (state.mass[start..end].iter().any(|v| *v != 0.0)
                || (temperature.is_none() && state.enthalpy[start..end].iter().any(|v| *v != 0.0))) {
            return Err("solid cells must contain zero mass and enthalpy".into());
        }
        if temperature.is_some_and(|t| !t[cell].is_finite()) {
            return Err("state contains negative mass/enthalpy or non-finite values".into());
        }
        for channel in 0..channels {
            let mass = state.mass[start + channel];
            // With finite temperature and heat capacity, an empty channel
            // always has zero derived enthalpy. Sparse mixtures usually have
            // only one or two populated channels per cell.
            if temperature.is_some() && mass == 0.0 { continue; }
            let enthalpy = if let Some(t) = temperature {
                mass * t[cell] * state.species[channel].specific_heat_capacity
            } else { state.enthalpy[start + channel] };
            if !enthalpy.is_finite() || enthalpy < 0.0 {
                return Err("state contains negative mass/enthalpy or non-finite values".into());
            }
            if mass == 0.0 && enthalpy != 0.0 {
                return Err("empty species channels must contain zero enthalpy".into());
            }
            if mass > 0.0 && enthalpy <= 0.0 {
                return Err("nonempty gas species must have positive sensible enthalpy".into());
            }
        }
        if HOST_VELOCITY {
            let max_vertical = (state.velocity_y[cell] + min_drift).abs()
                .max((state.velocity_y[cell] + max_drift).abs());
            max_rate = max_rate.max(state.velocity_x[cell].abs() + max_vertical);
        }
    }
    Ok(max_rate)
}

fn bind(binding: u32, buffer: &wgpu::Buffer) -> wgpu::BindGroupEntry<'_> {
    wgpu::BindGroupEntry { binding, resource: buffer.as_entire_binding() }
}

/// Checked CPU fallback for the gas transport operator.
///
/// Validation is completed before either output slice is modified. The CFL
/// bound matches the GPU dispatch guard, so callers cannot accidentally run
/// an unstable CPU-only step when the GPU path would reject the same input.
pub fn cpu_step(
    state: &State<'_>,
    width: usize,
    height: usize,
    channels: usize,
    dt: f32,
    mass_output: &mut [f32],
    enthalpy_output: &mut [f32],
) -> Result<(), String> {
    let count = width.checked_mul(height).ok_or("grid dimensions overflow")?;
    let values = count.checked_mul(channels).ok_or("tensor dimensions overflow")?;
    if width == 0 || height == 0 || channels == 0
        || mass_output.len() != values || enthalpy_output.len() != values {
        return Err("CPU step requires nonzero dimensions and matching output tensors".into());
    }
    if !dt.is_finite() || dt <= 0.0 {
        return Err("CPU step requires a finite positive timestep".into());
    }
    let max_rate = validate_state(state, count, channels)?;
    if !((dt * max_rate).is_finite()) || dt * max_rate > 1.0 {
        return Err("CPU step exceeds the validated CFL bound".into());
    }
    let flux = |a: f32, b: f32, speed: f32| {
        let cap = 1.0f32.min(0.125 / dt);
        let v = speed.clamp(-cap, cap);
        let delta = if (a-b).abs() <= 1e-7f32.max(a.max(b)*1e-5) { 0.0 } else { a-b };
        v * if v >= 0.0 { a } else { b } + 2.0f32.min(0.125/dt) * delta
    };
    let idx = |x: usize, y: usize, c: usize| (y * width + x) * channels + c;
    mass_output.copy_from_slice(state.mass);
    enthalpy_output.copy_from_slice(state.enthalpy);
    for y in 0..height {
        for x in 0..width {
            let cell = y * width + x;
            if state.solid[cell] != 0 {
                mass_output[cell * channels..(cell + 1) * channels].fill(0.0);
                enthalpy_output[cell * channels..(cell + 1) * channels].fill(0.0);
                continue;
            }
            for c in 0..channels {
                let mut mass_div = 0.0f32;
                let mut enthalpy_div = 0.0f32;
                let drift = state.species[c].vertical_drift;
                if x + 1 < width && state.solid[cell + 1] == 0 {
                    let v = 0.5 * (state.velocity_x[cell] + state.velocity_x[cell + 1]);
                    mass_div += flux(state.mass[idx(x, y, c)], state.mass[idx(x + 1, y, c)], v);
                    enthalpy_div += flux(state.enthalpy[idx(x, y, c)], state.enthalpy[idx(x + 1, y, c)], v);
                }
                if x > 0 && state.solid[cell - 1] == 0 {
                    let v = 0.5 * (state.velocity_x[cell - 1] + state.velocity_x[cell]);
                    mass_div -= flux(state.mass[idx(x - 1, y, c)], state.mass[idx(x, y, c)], v);
                    enthalpy_div -= flux(state.enthalpy[idx(x - 1, y, c)], state.enthalpy[idx(x, y, c)], v);
                }
                if y + 1 < height && state.solid[cell + width] == 0 {
                    let v = 0.5 * (state.velocity_y[cell] + state.velocity_y[cell + width]) + drift;
                    mass_div += flux(state.mass[idx(x, y, c)], state.mass[idx(x, y + 1, c)], v);
                    enthalpy_div += flux(state.enthalpy[idx(x, y, c)], state.enthalpy[idx(x, y + 1, c)], v);
                }
                if y > 0 && state.solid[cell - width] == 0 {
                    let v = 0.5 * (state.velocity_y[cell - width] + state.velocity_y[cell]) + drift;
                    mass_div -= flux(state.mass[idx(x, y - 1, c)], state.mass[idx(x, y, c)], v);
                    enthalpy_div -= flux(state.enthalpy[idx(x, y - 1, c)], state.enthalpy[idx(x, y, c)], v);
                }
                let slot = idx(x, y, c);
                mass_output[slot] = state.mass[slot] - dt * mass_div;
                enthalpy_output[slot] = state.enthalpy[slot] - dt * enthalpy_div;
            }
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn generated_velocity_upload_validates_material_without_host_velocity() {
        let species = [SpeciesInfo { element_id: 1, phase: 1,
            specific_heat_capacity: 2.0, vertical_drift: 0.25 }];
        let state = State { mass: &[1.0], enthalpy: &[], velocity_x: &[],
            velocity_y: &[], solid: &[0], species: &species };
        let check = |state: &State<'_>, temperature: &[f32]| {
            validate_state_for_phase::<false>(state, 1, 1, Some(temperature), 1)
        };
        assert_eq!(check(&state, &[300.0]).unwrap(), 0.0);
        // These buffers are neither uploaded nor read by the material path.
        let ignored_velocity = State { velocity_x: &[f32::NAN],
            velocity_y: &[f32::INFINITY], ..state };
        assert_eq!(check(&ignored_velocity, &[300.0]).unwrap(), 0.0);
        assert!(validate_state_with_temperature(&state, 1, 1, Some(&[300.0])).is_err());
        assert!(validate_state_with_temperature(&ignored_velocity, 1, 1, Some(&[300.0])).is_err());
        for mass in [-1.0, f32::NAN, f32::INFINITY] {
            assert!(check(&State { mass: &[mass], ..state }, &[300.0]).is_err());
        }
        for temperature in [f32::NAN, f32::INFINITY, f32::MAX, -1.0] {
            assert!(check(&state, &[temperature]).is_err());
        }
        assert!(check(&state, &[]).is_err());
        assert!(check(&State { mass: &[], ..state }, &[300.0]).is_err());
        assert!(check(&State { solid: &[1], ..state }, &[300.0]).is_err());
        assert!(check(&State { species: &[], ..state }, &[300.0]).is_err());
        assert!(validate_state_for_phase::<false>(&state, 1, 1, Some(&[300.0]), 2).is_err());
        let energy = State { enthalpy: &[600.0], ..state };
        assert_eq!(validate_state_for_phase::<false>(&energy, 1, 1, None, 1).unwrap(), 0.0);
        assert!(validate_state_for_phase::<false>(&State { enthalpy: &[0.0], ..energy }, 1, 1, None, 1).is_err());
    }

    #[test]
    fn temperature_input_rejects_invalid_or_overflowing_enthalpy() {
        let species = [SpeciesInfo { element_id: 1, phase: 1, specific_heat_capacity: 2.0, vertical_drift: 0.0 }];
        let state = State { mass: &[1.0], enthalpy: &[], velocity_x: &[0.0], velocity_y: &[0.0], solid: &[0], species: &species };
        assert!(validate_state_with_temperature(&state, 1, 1, Some(&[300.0])).is_ok());
        assert!(validate_state_with_temperature(&state, 1, 1, Some(&[])).is_err());
        assert!(validate_state_with_temperature(&state, 1, 1, Some(&[f32::NAN])).is_err());
        assert!(validate_state_with_temperature(&state, 1, 1, Some(&[f32::MAX])).is_err());
        let empty = State { mass: &[0.0], ..state };
        assert!(validate_state_with_temperature(&empty, 1, 1, Some(&[300.0])).is_ok());
        assert!(validate_state_with_temperature(&empty, 1, 1, Some(&[f32::NAN])).is_err());
        assert!(validate_state_with_temperature(&empty, 1, 1, Some(&[f32::INFINITY])).is_err());
    }

    #[test]
    fn gpu_density_velocity_matches_neighbour_gradients() {
        let mut gpu = pollster::block_on(MulticomponentGpu::new(3, 3, 2)).unwrap();
        let mass: Vec<f32> = (1..=18).map(|x| x as f32).collect();
        let enthalpy: Vec<f32> = mass.iter().map(|m| m * 300.0).collect();
        let velocity = [0.0; 9];
        let mut solid = [0; 9];
        solid[0] = 1;
        let mut mass = mass;
        let mut enthalpy = enthalpy;
        mass[..2].fill(0.0); enthalpy[..2].fill(0.0);
        let species = [SpeciesInfo { element_id: 1, phase: 1, specific_heat_capacity: 1.0, vertical_drift: 0.0 },
            SpeciesInfo { element_id: 2, phase: 1, specific_heat_capacity: 1.0, vertical_drift: 0.0 }];
        let state = State { mass: &mass, enthalpy: &enthalpy, velocity_x: &velocity,
            velocity_y: &velocity, solid: &solid, species: &species };
        gpu.upload_for_density_velocity(&state).unwrap();
        gpu.generate_density_velocity().unwrap();
        let readback = gpu.device.create_buffer(&wgpu::BufferDescriptor { label: None, size: 72,
            usage: wgpu::BufferUsages::MAP_READ | wgpu::BufferUsages::COPY_DST, mapped_at_creation: false });
        let mut encoder = gpu.device.create_command_encoder(&Default::default());
        encoder.copy_buffer_to_buffer(&gpu.velocity_x, 0, &readback, 0, 36);
        encoder.copy_buffer_to_buffer(&gpu.velocity_y, 0, &readback, 36, 36);
        let submission = gpu.queue.submit([encoder.finish()]);
        let (send, receive) = mpsc::sync_channel(1);
        readback.map_async(wgpu::MapMode::Read, .., move |r| { let _ = send.send(r); });
        gpu.device.poll(wgpu::PollType::Wait { submission_index: Some(submission), timeout: Some(Duration::from_secs(30)) }).unwrap();
        receive.recv_timeout(Duration::from_secs(2)).unwrap().unwrap();
        let view = readback.get_mapped_range(..).unwrap();
        let got: &[f32] = bytemuck::cast_slice(&view);
        for y in 0..3usize { for x in 0..3usize {
            let i = y * 3 + x;
            let total = |j: usize| mass[j*2] + mass[j*2+1];
            let center = total(i);
            let expected_x = if solid[i] != 0 { 0.0 } else { 0.04 * (if x>0 && solid[i-1]==0 {total(i-1)} else {center} - if x<2 && solid[i+1]==0 {total(i+1)} else {center}) };
            let expected_y = if solid[i] != 0 { 0.0 } else { 0.04 * (if y>0 && solid[i-3]==0 {total(i-3)} else {center} - if y<2 && solid[i+3]==0 {total(i+3)} else {center}) };
            assert!((got[i]-expected_x).abs() < 1e-6 && (got[9+i]-expected_y).abs() < 1e-6);
        }}
    }

    #[test]
    fn cfl_extreme_drifts_match_all_species_scan() {
        let species: Vec<_> = [-12.0, -0.25, 0.0, 1.5, 8.0].iter().enumerate()
            .map(|(i, &vertical_drift)| SpeciesInfo { element_id: i as u16, phase: 1,
                specific_heat_capacity: 1.0, vertical_drift }).collect();
        let vx = [0.2, -1.1, 3.0, -0.0];
        let vy = [-30.0, -2.0, 0.5, 40.0];
        let solid = [0; 4];
        let mass = [1.0; 20];
        let enthalpy = [300.0; 20];
        let state = State { mass: &mass, enthalpy: &enthalpy, velocity_x: &vx,
            velocity_y: &vy, solid: &solid, species: &species };
        let expected = vx.iter().zip(vy).map(|(&x, y)| x.abs() +
            species.iter().map(|s| (y + s.vertical_drift).abs()).fold(0.0, f32::max))
            .fold(0.0, f32::max);
        assert_eq!(validate_state(&state, 4, species.len()).unwrap(), expected);
    }

    #[test]
    fn wgsl_shader_validates() {
        for source in [SHADER, DIFFUSION_SHADER, CFL_SHADER, DENSITY_SUM_SHADER, DENSITY_VELOCITY_SHADER] {
            let module = naga::front::wgsl::parse_str(source).expect("WGSL parser");
            naga::valid::Validator::new(
                naga::valid::ValidationFlags::all(),
                naga::valid::Capabilities::all(),
            ).validate(&module).expect("WGSL validation");
        }
    }

    #[test]
    fn gpu_diffusion_preserves_species_and_energy_over_many_steps_with_wall() {
        let (width, height, channels) = (5, 2, 2);
        let cells = width * height;
        let mut gpu = pollster::block_on(MulticomponentGpu::new(width, height, channels)).unwrap();
        let species = [SpeciesInfo { element_id: 1, phase: 1, specific_heat_capacity: 1.0, vertical_drift: 0.0 },
            SpeciesInfo { element_id: 2, phase: 1, specific_heat_capacity: 1.0, vertical_drift: 0.0 }];
        let mut mass = vec![0.0f32; cells * channels];
        let mut enthalpy = vec![0.0f32; cells * channels];
        let mut solid = vec![0u32; cells];
        for y in 0..height { for x in 0..width {
            let cell=y*width+x;
            if x==2 { solid[cell]=1; continue; }
            let channel=if x==0 || x==4 {0} else {1};
            mass[cell*channels+channel]=1.0;
            enthalpy[cell*channels+channel]=if channel==0 {300.0} else {400.0};
        }}
        let velocity = vec![0.0; cells];
        let state = State { mass:&mass, enthalpy:&enthalpy, velocity_x:&velocity,
            velocity_y:&velocity, solid:&solid, species:&species };
        gpu.upload(&state).unwrap();
        for _ in 0..100 { gpu.diffuse_resident().unwrap(); }
        let mut output_mass=vec![0.0; mass.len()]; let mut output_heat=vec![0.0; mass.len()];
        gpu.readback_state(&mut output_mass,&mut output_heat).unwrap();
        for channel in 0..channels {
            let before:f64=mass.iter().skip(channel).step_by(channels).map(|&v|f64::from(v)).sum();
            let after:f64=output_mass.iter().skip(channel).step_by(channels).map(|&v|f64::from(v)).sum();
            assert!((before-after).abs()<1e-4);
        }
        let before:f64=enthalpy.iter().map(|&v|f64::from(v)).sum();
        let after:f64=output_heat.iter().map(|&v|f64::from(v)).sum();
        assert!((before-after).abs()/before<1e-5);
        assert!(output_mass.iter().all(|&v| v>=0.0));
        assert!(output_mass[1]>0.0 && output_mass[channels]>0.0);
        for y in 0..height { let wall=(y*width+2)*channels; assert_eq!(&output_mass[wall..wall+channels],&[0.0,0.0]); }
    }

    #[test]
    fn gpu_concentration_diffusion_mixes_and_conserves_mass_and_energy() {
        let mut gpu = pollster::block_on(MulticomponentGpu::new(2, 1, 2)).unwrap();
        let species = [SpeciesInfo { element_id: 1, phase: 1, specific_heat_capacity: 1.0, vertical_drift: 0.0 },
            SpeciesInfo { element_id: 2, phase: 1, specific_heat_capacity: 1.0, vertical_drift: 0.0 }];
        let state = State { mass: &[1.0, 0.0, 0.0, 1.0], enthalpy: &[300.0, 0.0, 0.0, 400.0],
            velocity_x: &[0.0; 2], velocity_y: &[0.0; 2], solid: &[0; 2], species: &species };
        gpu.upload(&state).unwrap();
        gpu.diffuse_resident().unwrap();
        let mut mass = [0.0; 4]; let mut heat = [0.0; 4];
        gpu.readback_state(&mut mass, &mut heat).unwrap();
        assert!((mass[0] - 0.95).abs() < 1e-6 && (mass[2] - 0.05).abs() < 1e-6);
        assert!((mass[1] - 0.05).abs() < 1e-6 && (mass[3] - 0.95).abs() < 1e-6);
        assert!((heat.iter().sum::<f32>() - 700.0).abs() < 1e-4);
        assert!((heat[2] - 15.0).abs() < 1e-5 && (heat[1] - 20.0).abs() < 1e-5);
    }

    #[test]
    #[ignore = "manual GPU kernel comparison"]
    fn benchmark_transport_thread_layouts() {
        use std::time::Instant;
        let (width,height,channels)=(160usize,274usize,8usize); let cells=width*height;
        let mut gpu=pollster::block_on(MulticomponentGpu::new(width,height,channels)).unwrap();
        let mass=vec![1.0f32;cells*channels]; let energy=vec![300.0f32;cells*channels];
        let velocity=vec![0.01f32;cells]; let solid=vec![0u32;cells];
        let species:Vec<_>=(0..channels).map(|i|SpeciesInfo {element_id:i as u16+1,phase:1,specific_heat_capacity:1.0,vertical_drift:0.0}).collect();
        gpu.upload(&State {mass:&mass,enthalpy:&energy,velocity_x:&velocity,velocity_y:&velocity,solid:&solid,species:&species}).unwrap();
        gpu.queue.write_buffer(&gpu.params,0,bytemuck::cast_slice(&[width as u32,height as u32,channels as u32,0.02f32.to_bits()]));
        let old=SHADER.replace("if gid.x >= params.width * params.channels || gid.y >= params.height { return; }", "if gid.x >= params.width || gid.y >= params.height { return; }")
            .replace("let x = gid.x / params.channels;\n    let c = gid.x % params.channels;", "let x = gid.x;")
            .replace("        let slot = cell * params.channels + c;", "    for (var c = 0u; c < params.channels; c += 1u) {\n        let slot = cell * params.channels + c;");
        let position=old.rfind('}').unwrap(); let mut old=old; old.insert_str(position,"    }\n");
        let shader=gpu.device.create_shader_module(wgpu::ShaderModuleDescriptor {label:Some("old transport layout"),source:wgpu::ShaderSource::Wgsl(old.into())});
        let pipeline=gpu.device.create_compute_pipeline(&wgpu::ComputePipelineDescriptor {label:None,layout:None,module:&shader,entry_point:Some("main"),compilation_options:Default::default(),cache:None});
        let buffers=[&gpu.mass_a,&gpu.velocity_x,&gpu.velocity_y,&gpu.solid,&gpu.drift_y,&gpu.mass_b,&gpu.params,&gpu.enthalpy_a,&gpu.enthalpy_b];
        let entries:Vec<_>=buffers.iter().enumerate().map(|(i,b)|bind(i as u32,b)).collect();
        let group=gpu.device.create_bind_group(&wgpu::BindGroupDescriptor {label:None,layout:&pipeline.get_bind_group_layout(0),entries:&entries});
        let measure=|old:bool| {let mut encoder=gpu.device.create_command_encoder(&Default::default());
            for _ in 0..32 {let mut pass=encoder.begin_compute_pass(&Default::default());
                if old {pass.set_pipeline(&pipeline);pass.set_bind_group(0,&group,&[]);pass.dispatch_workgroups((width as u32).div_ceil(8),(height as u32).div_ceil(8),1);}
                else {pass.set_pipeline(&gpu.pipeline);pass.set_bind_group(0,&gpu.groups[0],&[]);pass.dispatch_workgroups(((width*channels) as u32).div_ceil(8),(height as u32).div_ceil(8),1);}}
            let start=Instant::now(); let submission=gpu.queue.submit([encoder.finish()]);
            gpu.device.poll(wgpu::PollType::Wait {submission_index:Some(submission),timeout:Some(Duration::from_secs(30))}).unwrap(); start.elapsed().as_secs_f64()*1000.0/32.0};
        for _ in 0..4 {measure(true);measure(false);}
        let mut old=Vec::new();let mut new=Vec::new();for i in 0..21 {if i%2==0 {old.push(measure(true));new.push(measure(false));}else {new.push(measure(false));old.push(measure(true));}}
        old.sort_by(f64::total_cmp);new.sort_by(f64::total_cmp);
        eprintln!("transport 160x274x8 ms/step old median={:.4} new median={:.4}",old[10],new[10]);
    }

    #[test]
    #[ignore = "manual GPU kernel comparison"]
    fn benchmark_diffusion_thread_layouts() {
        use std::time::Instant;
        let (width,height,channels)=(160usize,274usize,8usize);
        let cells=width*height;
        let mut gpu=pollster::block_on(MulticomponentGpu::new(width,height,channels)).unwrap();
        let mass=vec![1.0f32;cells*channels]; let energy=vec![300.0f32;cells*channels];
        let velocity=vec![0.0f32;cells]; let solid=vec![0u32;cells];
        let species:Vec<_>=(0..channels).map(|i|SpeciesInfo {element_id:i as u16+1,phase:1,specific_heat_capacity:1.0,vertical_drift:0.0}).collect();
        let state=State {mass:&mass,enthalpy:&energy,velocity_x:&velocity,velocity_y:&velocity,solid:&solid,species:&species};
        gpu.upload(&state).unwrap(); gpu.diffuse_resident().unwrap();
        let prefix=DIFFUSION_SHADER.split("@compute @workgroup_size(8,8)").next().unwrap();
        let old=format!(r#"{prefix}
@compute @workgroup_size(8,8)
fn main(@builtin(global_invocation_id) id:vec3<u32>) {{
 if id.x>=p.width || id.y>=p.height {{return;}}
 let a=id.y*p.width+id.x; let ta=total(a);
 for(var c=0u;c<p.channels;c++) {{
  let i=a*p.channels+c; var flux=vec2<f32>(0.0);
  if solid[a]==0u {{
   if id.x>0u {{flux+=exchange(a,a-1u,c,ta);}}
   if id.x+1u<p.width {{flux+=exchange(a,a+1u,c,ta);}}
   if id.y>0u {{flux+=exchange(a,a-p.width,c,ta);}}
   if id.y+1u<p.height {{flux+=exchange(a,a+p.width,c,ta);}}
  }}
  out_mass[i]=mass[i]-flux.x; out_enthalpy[i]=enthalpy[i]-flux.y;
 }}
}}"#);
        let shader=gpu.device.create_shader_module(wgpu::ShaderModuleDescriptor {label:Some("old diffusion layout"),source:wgpu::ShaderSource::Wgsl(old.into())});
        let pipeline=gpu.device.create_compute_pipeline(&wgpu::ComputePipelineDescriptor {label:Some("old diffusion layout"),layout:None,module:&shader,entry_point:Some("main"),compilation_options:Default::default(),cache:None});
        let density=gpu.device.create_buffer(&wgpu::BufferDescriptor {label:None,size:(cells*4) as u64,usage:wgpu::BufferUsages::STORAGE|wgpu::BufferUsages::COPY_DST,mapped_at_creation:false});
        gpu.queue.write_buffer(&density,0,bytemuck::cast_slice(&vec![channels as f32;cells]));
        let buffers=[&gpu.mass_a,&gpu.enthalpy_a,&gpu.solid,&gpu.mass_b,&gpu.enthalpy_b,&gpu.params,&density];
        let entries:Vec<_>=buffers.iter().enumerate().map(|(i,b)|bind(i as u32,b)).collect();
        let old_group=gpu.device.create_bind_group(&wgpu::BindGroupDescriptor {label:None,layout:&pipeline.get_bind_group_layout(0),entries:&entries});
        let measure=|old:bool| {
            let mut encoder=gpu.device.create_command_encoder(&Default::default());
            for _ in 0..32 {let mut pass=encoder.begin_compute_pass(&Default::default());
                if old {pass.set_pipeline(&pipeline);pass.set_bind_group(0,&old_group,&[]);pass.dispatch_workgroups((width as u32).div_ceil(8),(height as u32).div_ceil(8),1);}
                else {pass.set_pipeline(&gpu.diffusion_pipeline);pass.set_bind_group(0,&gpu.diffusion_groups[0],&[]);pass.dispatch_workgroups(((width*channels) as u32).div_ceil(8),(height as u32).div_ceil(8),1);}}
            let start=Instant::now(); let submission=gpu.queue.submit([encoder.finish()]);
            gpu.device.poll(wgpu::PollType::Wait {submission_index:Some(submission),timeout:Some(Duration::from_secs(30))}).unwrap();
            start.elapsed().as_secs_f64()*1000.0/32.0
        };
        for _ in 0..4 {measure(true);measure(false);}
        let mut old_times=Vec::new(); let mut new_times=Vec::new();
        for i in 0..21 {if i%2==0 {old_times.push(measure(true));new_times.push(measure(false));}
            else {new_times.push(measure(false));old_times.push(measure(true));}}
        old_times.sort_by(f64::total_cmp);new_times.sort_by(f64::total_cmp);
        eprintln!("diffusion 160x274x8 ms/step old median={:.4} new median={:.4}",old_times[10],new_times[10]);
    }

    #[test]
    fn liquid_diffusion_mixes_without_crossing_walls_and_conserves_each_species() {
        let mut gpu = pollster::block_on(MulticomponentGpu::new(4, 1, 2)).unwrap();
        let species = [SpeciesInfo { element_id: 1, phase: 2, specific_heat_capacity: 1.0, vertical_drift: 0.0 },
            SpeciesInfo { element_id: 2, phase: 2, specific_heat_capacity: 1.0, vertical_drift: 0.0 }];
        let mass = [1.0, 0.0, 0.0, 1.0, 0.0, 0.0, 1.0, 0.0];
        let enthalpy = [300.0, 0.0, 0.0, 400.0, 0.0, 0.0, 500.0, 0.0];
        let velocity = [0.0; 4]; let solid = [0, 0, 1, 0];
        let state = State { mass: &mass, enthalpy: &enthalpy, velocity_x: &velocity, velocity_y: &velocity, solid: &solid, species: &species };
        let mut out_mass = [0.0; 8]; let mut out_enthalpy = [0.0; 8];
        gpu.diffuse_liquids(&state, &mut out_mass, &mut out_enthalpy).unwrap();
        assert!((out_mass[0]-0.95).abs()<1e-6 && (out_mass[2]-0.05).abs()<1e-6);
        assert!((out_mass[1]-0.05).abs()<1e-6 && (out_mass[3]-0.95).abs()<1e-6);
        assert_eq!(&out_mass[4..], &[0.0, 0.0, 1.0, 0.0]);
        assert!((out_enthalpy.iter().sum::<f32>()-1200.0).abs()<1e-4);
        let mixed = [species[0], SpeciesInfo { phase: 1, ..species[1] }];
        let mixed_state = State { species: &mixed, ..state };
        assert!(gpu.diffuse_liquids(&mixed_state, &mut out_mass, &mut out_enthalpy).is_err());
        assert!(gpu.upload(&state).is_err(), "gas transport must still reject liquids");
    }

    #[test]
    fn sparse_gas_upload_expands_mass_and_energy_and_checks_metadata() {
        let mut gpu = pollster::block_on(MulticomponentGpu::new(2, 1, 3)).unwrap();
        let species = [
            SpeciesInfo {element_id:1,phase:1,specific_heat_capacity:1.0,vertical_drift:-0.2},
            SpeciesInfo {element_id:2,phase:1,specific_heat_capacity:2.0,vertical_drift:0.1},
            SpeciesInfo {element_id:3,phase:1,specific_heat_capacity:3.0,vertical_drift:0.0},
        ];
        let cells = [
            SparseLiquidCell {first:0,second:2,first_mass:1.0,second_mass:0.5,temperature:300.0},
            SparseLiquidCell {first:1,second:1,first_mass:0.75,second_mass:0.25,temperature:400.0},
        ];
        gpu.upload_sparse_gas(&cells, &[0,0], &species).unwrap();
        let mut mass=[0.0;6]; let mut energy=[0.0;6];
        gpu.readback_state(&mut mass, &mut energy).unwrap();
        assert_eq!(mass, [1.0,0.0,0.5,0.0,1.0,0.0]);
        assert_eq!(energy, [300.0,0.0,450.0,0.0,800.0,0.0]);
        gpu.upload_sparse_gas(&cells, &[0,1], &species).unwrap();
        gpu.readback_state(&mut mass, &mut energy).unwrap();
        assert_eq!(&mass[3..], &[0.0,0.0,0.0]);
        gpu.upload_sparse_gas(&cells, &[0,0], &species).unwrap();
        gpu.readback_state(&mut mass, &mut energy).unwrap();
        assert_eq!(&mass[3..], &[0.0,1.0,0.0]);
        gpu.upload_sparse_gas(&cells, &[0,0], &species).unwrap();
        gpu.readback_state(&mut mass, &mut energy).unwrap();
        assert_eq!(&mass[3..], &[0.0,1.0,0.0]);
        let mut invalid=species; invalid[1].vertical_drift=f32::NAN;
        assert!(gpu.upload_sparse_gas(&cells, &[0,0], &invalid).is_err());

        // Multiplying mass by heat capacity first underflows here even though
        // mass * temperature * heat capacity is a representable normal value.
        let mut small_heat=species; small_heat[0].specific_heat_capacity=1e-20;
        let mut trace=cells; trace[0]=SparseLiquidCell {
            first:0, second:0, first_mass:1e-30, second_mass:0.0, temperature:1e20,
        };
        gpu.upload_sparse_gas(&trace, &[0,0], &small_heat).unwrap();
        gpu.readback_state(&mut mass, &mut energy).unwrap();
        assert!(energy[0] > 0.0 && (energy[0] / 1e-30 - 1.0).abs() < 1e-5);
        let destination=gpu.device.create_buffer(&wgpu::BufferDescriptor {label:None,size:8,usage:wgpu::BufferUsages::STORAGE|wgpu::BufferUsages::COPY_SRC,mapped_at_creation:false});
        let readback=gpu.device.create_buffer(&wgpu::BufferDescriptor {label:None,size:8,usage:wgpu::BufferUsages::COPY_DST|wgpu::BufferUsages::MAP_READ,mapped_at_creation:false});
        gpu.upload_sparse_gas_into_temperature(&cells, &[0,1], &species, &destination).unwrap();
        let second=gpu.device.create_buffer(&wgpu::BufferDescriptor {label:None,size:8,usage:wgpu::BufferUsages::STORAGE|wgpu::BufferUsages::COPY_SRC,mapped_at_creation:false});
        gpu.upload_sparse_gas_into_temperature(&cells, &[0,1], &species, &second).unwrap();
        gpu.upload_sparse_gas_into_temperature(&cells, &[0,1], &species, &destination).unwrap();
        assert_eq!(gpu.liquid_sparse_preparation.as_ref().unwrap().temperature_groups.len(),2);
        let mut encoder=gpu.device.create_command_encoder(&Default::default());
        encoder.copy_buffer_to_buffer(&destination,0,&readback,0,8);
        let submission=gpu.queue.submit([encoder.finish()]);
        let (send,receive)=mpsc::sync_channel(1);
        readback.map_async(wgpu::MapMode::Read,..,move |r| {let _=send.send(r);});
        gpu.device.poll(wgpu::PollType::Wait {submission_index:Some(submission),timeout:Some(Duration::from_secs(30))}).unwrap();
        receive.recv_timeout(Duration::from_secs(2)).unwrap().unwrap();
        {let view=readback.get_mapped_range(..).unwrap();assert_eq!(bytemuck::cast_slice::<u8,f32>(&view), &[300.0,400.0]);}
        readback.unmap();
    }

    #[test]
    fn pending_liquid_can_overlap_other_gpu_work_and_guards_reuse() {
        let context=Arc::new(pollster::block_on(crate::gpu_context::GpuContext::new()).unwrap());
        let mut liquid=pollster::block_on(MulticomponentGpu::new_with_context(context.clone(),2,1,1)).unwrap();
        let mut gas=pollster::block_on(MulticomponentGpu::new_with_context(context,2,1,1)).unwrap();
        let cells=[SparseLiquidCell {first:0,second:0,first_mass:1.0,second_mass:0.0,temperature:300.0};2];
        let liquid_species=[SpeciesInfo {element_id:1,phase:2,specific_heat_capacity:1.0,vertical_drift:0.0}];
        let gas_species=[SpeciesInfo {element_id:2,phase:1,specific_heat_capacity:1.0,vertical_drift:0.0}];
        let pending=liquid.begin_sparse_liquids_projected(&cells,&[0,0],&liquid_species).unwrap();
        assert!(liquid.begin_sparse_liquids_projected(&cells,&[0,0],&liquid_species).is_err());
        gas.upload_sparse_gas(&cells,&[0,0],&gas_species).unwrap();
        let mut mass=[0.0;2]; let mut energy=[0.0;2];
        gas.readback_state(&mut mass,&mut energy).unwrap();
        let mut result=[ProjectedLiquidCell::default();2];
        assert!(pending.finish(&mut result).unwrap());
        assert!((result[0].first_mass+result[1].first_mass-2.0).abs()<1e-6);
        assert!(liquid.begin_sparse_liquids_projected(&cells,&[0,0],&liquid_species).unwrap().finish(&mut result).unwrap());
        // A failed later stage may abandon the submitted readback. The buffer
        // must be unmapped and reusable on the next simulation step.
        drop(liquid.begin_sparse_liquids_projected(&cells,&[0,0],&liquid_species).unwrap());
        assert!(liquid.begin_sparse_liquids_projected(&cells,&[0,0],&liquid_species).unwrap().finish(&mut result).unwrap());
    }

    #[test]
    fn packed_and_wide_sparse_inputs_can_alternate_without_stale_gpu_state() {
        let mut gpu=pollster::block_on(MulticomponentGpu::new(2,1,3)).unwrap();
        let species=[SpeciesInfo {element_id:1,phase:1,specific_heat_capacity:1.0,vertical_drift:0.0},
            SpeciesInfo {element_id:2,phase:1,specific_heat_capacity:2.0,vertical_drift:0.0},
            SpeciesInfo {element_id:3,phase:1,specific_heat_capacity:3.0,vertical_drift:0.0}];
        let wide=[SparseLiquidCell {first:0,second:2,first_mass:1.0,second_mass:0.5,temperature:300.0};2];
        let packed=[PackedSparseCell {first:1,second:2,first_mass:2.0,second_mass:0.25,temperature:400.0};2];
        let temp=gpu.device.create_buffer(&wgpu::BufferDescriptor {label:None,size:8,usage:wgpu::BufferUsages::STORAGE,mapped_at_creation:false});
        let mut mass=[0.0;6];let mut energy=[0.0;6];
        gpu.upload_sparse_gas(&wide,&[0,0],&species).unwrap();
        gpu.readback_state(&mut mass,&mut energy).unwrap();assert_eq!(&mass[..3],&[1.0,0.0,0.5]);
        let mut encoder=gpu.device.create_command_encoder(&Default::default());
        gpu.encode_packed_sparse_gas_into_temperature(&mut encoder,&packed,&[0,0],&species,&temp).unwrap();
        gpu.queue.submit([encoder.finish()]);gpu.readback_state(&mut mass,&mut energy).unwrap();
        assert_eq!(&mass[..3],&[0.0,2.0,0.25]);assert_eq!(&energy[..3],&[0.0,1600.0,300.0]);
        gpu.upload_sparse_gas(&wide,&[0,0],&species).unwrap();
        gpu.readback_state(&mut mass,&mut energy).unwrap();assert_eq!(&mass[..3],&[1.0,0.0,0.5]);
    }

    #[test]
    fn sparse_input_partial_updates_preserve_other_blocks() {
        let count=4100usize;
        let mut gpu=pollster::block_on(MulticomponentGpu::new(count,1,1)).unwrap();
        let species=[SpeciesInfo {element_id:1,phase:1,specific_heat_capacity:1.0,vertical_drift:0.0}];
        let mut cells=vec![SparseLiquidCell {first:0,second:0,first_mass:1.0,second_mass:0.0,temperature:300.0};count];
        let solid=vec![0;count];
        let mut mass=vec![0.0;count]; let mut energy=vec![0.0;count];
        gpu.upload_sparse_gas(&cells,&solid,&species).unwrap();
        gpu.readback_state(&mut mass,&mut energy).unwrap();
        cells[17].first_mass=2.0;
        cells[4099].temperature=400.0;
        gpu.upload_sparse_gas(&cells,&solid,&species).unwrap();
        gpu.readback_state(&mut mass,&mut energy).unwrap();
        assert_eq!((mass[17],energy[17]),(2.0,600.0));
        assert_eq!((mass[2048],energy[2048]),(1.0,300.0));
        assert_eq!((mass[4099],energy[4099]),(1.0,400.0));
        gpu.upload_sparse_gas(&cells,&solid,&species).unwrap();
        gpu.readback_state(&mut mass,&mut energy).unwrap();
        assert_eq!((mass[17],energy[17]),(2.0,600.0));
        assert_eq!((mass[4099],energy[4099]),(1.0,400.0));
    }

    #[test]
    fn sparse_solid_partial_update_changes_only_target_cells() {
        let count=4100usize;
        let mut gpu=pollster::block_on(MulticomponentGpu::new(count,1,1)).unwrap();
        let species=[SpeciesInfo {element_id:1,phase:1,specific_heat_capacity:1.0,vertical_drift:0.0}];
        let cells=vec![SparseLiquidCell {first:0,second:0,first_mass:1.0,second_mass:0.0,temperature:300.0};count];
        let mut solid=vec![0;count];
        let mut mass=vec![0.0;count];let mut energy=vec![0.0;count];
        gpu.upload_sparse_gas(&cells,&solid,&species).unwrap();
        gpu.readback_state(&mut mass,&mut energy).unwrap();
        solid[count-1]=1;
        gpu.upload_sparse_gas(&cells,&solid,&species).unwrap();
        gpu.readback_state(&mut mass,&mut energy).unwrap();
        assert_eq!(mass[0],1.0);assert_eq!(mass[count-1],0.0);
        solid[count-1]=0;
        gpu.upload_sparse_gas(&cells,&solid,&species).unwrap();
        gpu.readback_state(&mut mass,&mut energy).unwrap();
        assert_eq!(mass[count-1],1.0);
    }

    #[test]
    fn liquid_projection_matches_full_result_and_rejects_three_components() {
        let mut gpu = pollster::block_on(MulticomponentGpu::new(2, 1, 4)).unwrap();
        let species: Vec<_> = (0..4).map(|i| SpeciesInfo {element_id:i+1,phase:2,specific_heat_capacity:1.0,vertical_drift:0.0}).collect();
        let mass = [1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0];
        let energy = [300.0, 0.0, 0.0, 0.0, 0.0, 400.0, 0.0, 0.0];
        let velocity = [0.0; 2]; let solid = [0, 0];
        let state = State {mass:&mass,enthalpy:&energy,velocity_x:&velocity,velocity_y:&velocity,solid:&solid,species:&species};
        let mut full_mass=[0.0;8]; let mut full_energy=[0.0;8];
        gpu.diffuse_liquids(&state,&mut full_mass,&mut full_energy).unwrap();
        let mut result=[ProjectedLiquidCell::default();2];
        assert!(gpu.diffuse_liquids_projected(&state,&mut result).unwrap());
        for (i, cell) in result.iter().enumerate() {
            assert!((cell.first_mass-full_mass[i*4+cell.first as usize]).abs()<1e-6);
            assert!((cell.second_mass-full_mass[i*4+cell.second as usize]).abs()<1e-6);
            assert!((cell.enthalpy-full_energy[i*4..i*4+4].iter().sum::<f32>()).abs()<1e-4);
        }
        let temperature=[300.0,400.0];
        let no_energy = State {enthalpy:&[],..state};
        result.fill(ProjectedLiquidCell::default());
        assert!(gpu.diffuse_liquids_from_temperature_projected(&no_energy,&temperature,&mut result).unwrap());
        for (i,cell) in result.iter().enumerate() {
            assert!((cell.enthalpy-full_energy[i*4..i*4+4].iter().sum::<f32>()).abs()<1e-4);
            assert!((cell.first_mass-full_mass[i*4+cell.first as usize]).abs()<1e-6);
        }
        let sparse=[SparseLiquidCell {first:0,second:0,first_mass:1.0,second_mass:0.0,temperature:300.0},
            SparseLiquidCell {first:1,second:1,first_mass:1.0,second_mass:0.0,temperature:400.0}];
        result.fill(ProjectedLiquidCell::default());
        assert!(gpu.diffuse_sparse_liquids_projected(&sparse,&solid,&species,&mut result).unwrap());
        for (i,cell) in result.iter().enumerate() {
            assert!((cell.enthalpy-full_energy[i*4..i*4+4].iter().sum::<f32>()).abs()<1e-4);
            assert!((cell.first_mass-full_mass[i*4+cell.first as usize]).abs()<1e-6);
            assert!((cell.second_mass-full_mass[i*4+cell.second as usize]).abs()<1e-6);
        }
        let mixed = [1.0,1.0,1.0,0.0, 0.0,1.0,0.0,0.0];
        let mixed_energy = [300.0,300.0,300.0,0.0, 0.0,300.0,0.0,0.0];
        let mixed_state = State {mass:&mixed,enthalpy:&mixed_energy,..state};
        let sentinel = ProjectedLiquidCell {first:99,..Default::default()};
        result.fill(sentinel);
        assert!(!gpu.diffuse_liquids_projected(&mixed_state,&mut result).unwrap());
        assert!(result.iter().all(|cell| cell.first==99));
    }

    #[test]
    fn gas_projection_matches_full_readback_and_checks_deferred_cfl() {
        let ctx=std::sync::Arc::new(pollster::block_on(crate::gpu_context::GpuContext::new()).unwrap());
        let mut gpu=pollster::block_on(MulticomponentGpu::new_with_context(ctx.clone(),2,1,5)).unwrap();
        let species:Vec<_>=(0..5).map(|i|SpeciesInfo {element_id:i+1,phase:1,specific_heat_capacity:1.0,vertical_drift:0.0}).collect();
        let mass=[1.0,0.0,0.0,0.0,0.0,0.0,1.0,0.0,0.0,0.0];
        let energy=[300.0,0.0,0.0,0.0,0.0,0.0,400.0,0.0,0.0,0.0];
        let velocity=[0.0;2];let solid=[0,0];
        let state=State {mass:&mass,enthalpy:&energy,velocity_x:&velocity,velocity_y:&velocity,solid:&solid,species:&species};
        let temp=ctx.device.create_buffer(&wgpu::BufferDescriptor {label:None,size:8,usage:wgpu::BufferUsages::STORAGE|wgpu::BufferUsages::COPY_DST|wgpu::BufferUsages::COPY_SRC,mapped_at_creation:false});
        ctx.queue.write_buffer(&temp,0,bytemuck::cast_slice(&[300.0f32,400.0]));
        gpu.upload(&state).unwrap();gpu.diffuse_resident().unwrap();
        let mut full=[0.0;10];let mut full_temp=[0.0;2];
        gpu.readback_mass_and_temperature(&temp,&mut full,&mut full_temp).unwrap();
        let mut projected=[ProjectedLiquidCell::default();2];
        assert!(gpu.readback_projected_gas(&temp,&mut projected).unwrap());
        for (i,cell) in projected.iter().enumerate() {
            assert!((cell.first_mass-full[i*5+cell.first as usize]).abs()<1e-6);
            assert!((cell.second_mass-full[i*5+cell.second as usize]).abs()<1e-6);
            assert_eq!(cell.enthalpy,full_temp[i]);
        }
        // Reuse the same pipeline with a different temperature buffer and
        // alternate tensor sides; cached bindings must follow both inputs.
        let temp2=ctx.device.create_buffer(&wgpu::BufferDescriptor {label:None,size:8,usage:wgpu::BufferUsages::STORAGE|wgpu::BufferUsages::COPY_DST,mapped_at_creation:false});
        ctx.queue.write_buffer(&temp2,0,bytemuck::cast_slice(&[510.0f32,620.0]));
        for (buffer, expected, diffuse) in [(&temp2,[510.0,620.0],false),(&temp,[300.0,400.0],true),(&temp2,[510.0,620.0],true)] {
            gpu.upload(&state).unwrap();
            if diffuse { gpu.diffuse_resident().unwrap(); }
            assert!(gpu.readback_projected_gas(buffer,&mut projected).unwrap());
            assert_eq!([projected[0].enthalpy,projected[1].enthalpy],expected);
        }
        let bad_velocity=[100.0;2];let bad=State {velocity_x:&bad_velocity,..state};
        gpu.upload(&bad).unwrap();let _=gpu.velocity_buffers();gpu.dispatch_with_deferred_cfl(1.0).unwrap();
        projected.fill(ProjectedLiquidCell {first:99,..Default::default()});
        assert!(gpu.readback_projected_gas(&temp,&mut projected).is_err());
        assert!(projected.iter().all(|cell|cell.first==99));
        let three=[1.0,1.0,1.0,0.0,0.0, 0.0,1.0,0.0,0.0,0.0];
        let three_energy=[300.0,300.0,300.0,0.0,0.0, 0.0,400.0,0.0,0.0,0.0];
        let three_state=State {mass:&three,enthalpy:&three_energy,..state};
        gpu.upload(&three_state).unwrap();
        assert!(!gpu.readback_projected_gas(&temp,&mut projected).unwrap());
        assert!(projected.iter().all(|cell|cell.first==99));
    }

    #[test]
    fn deferred_cfl_rejects_unstable_velocity_before_host_outputs_change() {
        let mut gpu = pollster::block_on(MulticomponentGpu::new(2, 2, 1)).unwrap();
        let species = [SpeciesInfo { element_id: 1, phase: 1, specific_heat_capacity: 1.0, vertical_drift: 0.0 }];
        let state = State { mass: &[1.0; 4], enthalpy: &[300.0; 4], velocity_x: &[0.0; 4],
            velocity_y: &[0.0; 4], solid: &[0; 4], species: &species };
        gpu.upload(&state).unwrap();
        let (vx, _) = gpu.velocity_buffers();
        gpu.queue.write_buffer(vx, 0, bytemuck::cast_slice(&[30.0f32; 4]));
        gpu.dispatch_with_deferred_cfl(0.1).unwrap();
        let temp = gpu.device.create_buffer(&wgpu::BufferDescriptor { label: None, size: 16,
            usage: wgpu::BufferUsages::COPY_SRC | wgpu::BufferUsages::COPY_DST, mapped_at_creation: false });
        gpu.queue.write_buffer(&temp, 0, bytemuck::cast_slice(&[300.0f32; 4]));
        let mut mass = [7.0; 4]; let mut temperature = [8.0; 4];
        assert!(gpu.readback_mass_and_temperature(&temp, &mut mass, &mut temperature).is_err());
        assert_eq!(mass, [7.0; 4]);
        assert_eq!(temperature, [8.0; 4]);
    }

    #[test]
    fn validated_substeps_transport_high_velocity_without_losing_mass() {
        let mut gpu = pollster::block_on(MulticomponentGpu::new(2, 2, 1)).unwrap();
        let species = [SpeciesInfo { element_id: 1, phase: 1, specific_heat_capacity: 1.0, vertical_drift: 0.0 }];
        let state = State { mass: &[1.0; 4], enthalpy: &[300.0; 4], velocity_x: &[0.0; 4],
            velocity_y: &[0.0; 4], solid: &[0; 4], species: &species };
        gpu.upload(&state).unwrap();
        let (vx, _) = gpu.velocity_buffers();
        gpu.queue.write_buffer(vx, 0, bytemuck::cast_slice(&[30.0f32; 4]));
        let rate = gpu.revalidate_velocity().unwrap();
        let mut encoder = gpu.device.create_command_encoder(&Default::default());
        assert!(gpu.encode_resident(&mut encoder, 1, 0.1).is_err());
        gpu.encode_resident(&mut encoder, 4, 0.025).unwrap();
        gpu.queue.submit([encoder.finish()]);
        let mut mass = [0.0; 4];
        let mut enthalpy = [0.0; 4];
        gpu.readback_state(&mut mass, &mut enthalpy).unwrap();
        assert!(rate >= 30.0);
        assert!(mass.iter().all(|m| *m >= 0.0));
        assert!((mass.iter().sum::<f32>() - 4.0).abs() < 1e-5);
        assert!((enthalpy.iter().sum::<f32>() - 1200.0).abs() < 1e-3);
    }

    #[test]
    fn gpu_cfl_reduction_handles_partial_workgroup_and_invalid_velocity() {
        let (width, height, channels) = (65, 2, 4);
        let count = width * height;
        let mass = vec![1.0; count * channels];
        let enthalpy = vec![300.0; count * channels];
        let vx = vec![0.0; count];
        let vy = vec![0.0; count];
        let solid = vec![0; count];
        let species = [
            SpeciesInfo { element_id: 1, phase: 1, specific_heat_capacity: 1.0, vertical_drift: 0.25 },
            SpeciesInfo { element_id: 2, phase: 1, specific_heat_capacity: 1.0, vertical_drift: 1.0 },
            SpeciesInfo { element_id: 3, phase: 1, specific_heat_capacity: 1.0, vertical_drift: -0.5 },
            SpeciesInfo { element_id: 4, phase: 1, specific_heat_capacity: 1.0, vertical_drift: 0.0 },
        ];
        let state = State { mass: &mass, enthalpy: &enthalpy, velocity_x: &vx,
            velocity_y: &vy, solid: &solid, species: &species };
        let mut gpu = pollster::block_on(MulticomponentGpu::new(width, height, channels)).unwrap();
        gpu.upload(&state).unwrap();
        let mut changed_x = vx.clone();
        let mut changed_y = vy.clone();
        changed_x[count - 1] = -3.0;
        changed_y[count - 1] = 2.0;
        let (x, y) = gpu.velocity_buffers();
        gpu.queue.write_buffer(x, 0, bytemuck::cast_slice(&changed_x));
        gpu.queue.write_buffer(y, 0, bytemuck::cast_slice(&changed_y));
        assert_eq!(gpu.revalidate_velocity().unwrap(), 6.0);
        changed_y[count - 1] = -2.0;
        let (_, y) = gpu.velocity_buffers();
        gpu.queue.write_buffer(y, 0, bytemuck::cast_slice(&changed_y));
        assert_eq!(gpu.revalidate_velocity().unwrap(), 5.5);
        changed_y[count - 1] = f32::NAN;
        let (_, y) = gpu.velocity_buffers();
        gpu.queue.write_buffer(y, 0, bytemuck::cast_slice(&changed_y));
        assert!(gpu.revalidate_velocity().is_err());
    }

    #[test]
    fn cpu_reference_conserves_species_mass_and_enthalpy_and_respects_walls() {
        let (width, height, channels) = (19usize, 13usize, 4usize);
        let count = width * height;
        let mut solid = vec![0u32; count];
        for x in 0..width { solid[x] = 1; solid[(height - 1) * width + x] = 1; }
        for y in 0..height { solid[y * width] = 1; solid[y * width + width - 1] = 1; }
        for y in 4..7 { for x in 8..11 { solid[y * width + x] = 1; } }
        let mut mass = vec![0.0f32; count * channels];
        let mut enthalpy = vec![0.0f32; count * channels];
        for cell in 0..count {
            if solid[cell] == 0 {
                for c in 0..channels {
                    let m = ((cell * 17 + c * 13) % 97) as f32 / 97.0;
                    mass[cell * channels + c] = m;
                    enthalpy[cell * channels + c] = m * (1.0 + c as f32 * 0.25) * (280.0 + c as f32 * 15.0);
                }
            }
        }
        let vx = vec![0.015f32; count];
        let vy = vec![-0.01f32; count];
        let species = [
            SpeciesInfo { element_id: 1, phase: 1, specific_heat_capacity: 1.0, vertical_drift: -0.002 },
            SpeciesInfo { element_id: 2, phase: 1, specific_heat_capacity: 1.25, vertical_drift: -0.001 },
            SpeciesInfo { element_id: 3, phase: 1, specific_heat_capacity: 1.5, vertical_drift: 0.001 },
            SpeciesInfo { element_id: 4, phase: 1, specific_heat_capacity: 1.75, vertical_drift: 0.002 },
        ];
        let state = State { mass: &mass, enthalpy: &enthalpy, velocity_x: &vx,
            velocity_y: &vy, solid: &solid, species: &species };
        validate_state(&state, count, channels).unwrap();
        let mut current_mass = mass.clone();
        let mut current_enthalpy = enthalpy.clone();
        let mut next_mass = vec![0.0f32; count * channels];
        let mut next_enthalpy = vec![0.0f32; count * channels];
        for _ in 0..100 {
            let step_state = State { mass: &current_mass, enthalpy: &current_enthalpy,
                velocity_x: &vx, velocity_y: &vy, solid: &solid, species: &species };
            cpu_step(&step_state, width, height, channels, 0.1, &mut next_mass, &mut next_enthalpy).unwrap();
            std::mem::swap(&mut current_mass, &mut next_mass);
            std::mem::swap(&mut current_enthalpy, &mut next_enthalpy);
        }
        for c in 0..channels {
            let before_mass: f64 = (0..count).map(|i| mass[i * channels + c] as f64).sum();
            let after_mass: f64 = (0..count).map(|i| current_mass[i * channels + c] as f64).sum();
            let before_energy: f64 = (0..count).map(|i| enthalpy[i * channels + c] as f64).sum();
            let after_energy: f64 = (0..count).map(|i| current_enthalpy[i * channels + c] as f64).sum();
            assert!((after_mass - before_mass).abs() / before_mass.max(1e-30) < 1e-5, "species mass {c}");
            assert!((after_energy - before_energy).abs() / before_energy.max(1e-30) < 1e-5, "species enthalpy {c}");
        }
        assert!(current_mass.iter().chain(current_enthalpy.iter()).all(|v| v.is_finite() && *v >= -1e-7));
        for cell in 0..count {
            if solid[cell] != 0 {
                assert!(current_mass[cell * channels..(cell + 1) * channels].iter().all(|v| *v == 0.0));
                assert!(current_enthalpy[cell * channels..(cell + 1) * channels].iter().all(|v| *v == 0.0));
            }
        }
    }

    #[test]
    fn cpu_step_rejects_invalid_timestep_cfl_and_shapes_without_mutating_outputs() {
        let mass = [1.0f32];
        let enthalpy = [300.0f32];
        let velocity_x = [2.0f32];
        let velocity_y = [0.0f32];
        let solid = [0u32];
        let species = [SpeciesInfo {
            element_id: 1, phase: 1, specific_heat_capacity: 1.0, vertical_drift: 0.0,
        }];
        let state = State {
            mass: &mass, enthalpy: &enthalpy, velocity_x: &velocity_x,
            velocity_y: &velocity_y, solid: &solid, species: &species,
        };
        let mut mass_out = [77.0f32];
        let mut enthalpy_out = [88.0f32];

        assert!(cpu_step(&state, 1, 1, 1, f32::NAN, &mut mass_out, &mut enthalpy_out).is_err());
        assert!(cpu_step(&state, 1, 1, 1, 1.0, &mut mass_out, &mut enthalpy_out).is_err());
        assert!(cpu_step(&state, 2, 1, 1, 0.1, &mut mass_out, &mut enthalpy_out).is_err());
        assert_eq!(mass_out, [77.0]);
        assert_eq!(enthalpy_out, [88.0]);
    }

    #[test]
    fn validates_gas_species_metadata_and_enthalpy_state() {
        let mass = [1.0f32, 1.0];
        let enthalpy = [300.0f32, 600.0];
        let velocity = [0.0f32];
        let solid = [0u32];
        let valid = [
            SpeciesInfo { element_id: 1, phase: 1, specific_heat_capacity: 1.0, vertical_drift: 0.0 },
            SpeciesInfo { element_id: 2, phase: 1, specific_heat_capacity: 2.0, vertical_drift: 0.0 },
        ];
        let state = State { mass: &mass, enthalpy: &enthalpy, velocity_x: &velocity,
            velocity_y: &velocity, solid: &solid, species: &valid };
        assert_eq!(validate_state(&state, 1, 2).unwrap(), 0.0);

        let duplicate = [valid[0], valid[0]];
        let duplicate_state = State { species: &duplicate, ..state };
        assert!(validate_state(&duplicate_state, 1, 2).is_err());

        let liquid = [
            valid[0],
            SpeciesInfo { phase: 2, ..valid[1] },
        ];
        let liquid_state = State { species: &liquid, ..state };
        assert!(validate_state(&liquid_state, 1, 2).is_err());

        let bad_energy = [300.0f32, f32::NAN];
        let energy_state = State { enthalpy: &bad_energy, ..state };
        assert!(validate_state(&energy_state, 1, 2).is_err());

        let empty_mass = [0.0f32, 1.0];
        let empty_energy = [1.0f32, 600.0];
        let empty_state = State { mass: &empty_mass, enthalpy: &empty_energy, ..state };
        assert!(validate_state(&empty_state, 1, 2).is_err());
    }
}
