//! GPU-resident variable-density finite-volume pressure projection.
use std::{sync::{mpsc, Arc}, time::Duration};
const SHADER:&str=include_str!("pressure_projection.wgsl");

#[derive(Clone,Copy)]
pub struct ProjectionInput<'a>{
 pub density:&'a[f32],pub velocity_x:&'a[f32],pub velocity_y:&'a[f32],pub solid:&'a[u32],
}
pub struct ProjectionOutput{
 pub pressure:Vec<f32>,pub u_faces:Vec<f32>,pub v_faces:Vec<f32>,
 pub iterations:usize,pub adapter:wgpu::AdapterInfo,
}
struct SharedBindings {
 buffers: [wgpu::Buffer; 4],
 groups: [wgpu::BindGroup; 9],
}
pub struct PressureProjectionGpu{
 device:Arc<wgpu::Device>,queue:Arc<wgpu::Queue>,
 density_from_mass:wgpu::ComputePipeline,reconstruct_velocity:wgpu::ComputePipeline,
 prepare_u:wgpu::ComputePipeline,prepare_v:wgpu::ComputePipeline,
 divergence:wgpu::ComputePipeline,rbgs:wgpu::ComputePipeline,
 project_u:wgpu::ComputePipeline,project_v:wgpu::ComputePipeline,
 groups:[wgpu::BindGroup;8],
 shared_bindings:Vec<SharedBindings>,
 density:wgpu::Buffer,velocity_x:wgpu::Buffer,velocity_y:wgpu::Buffer,solid:wgpu::Buffer,
 u_faces:wgpu::Buffer,v_faces:wgpu::Buffer,beta_u:wgpu::Buffer,beta_v:wgpu::Buffer,rhs:wgpu::Buffer,pressure:wgpu::Buffer,params_red:wgpu::Buffer,params_black:wgpu::Buffer,
 readback:wgpu::Buffer,width:usize,height:usize,cells:usize,u_count:usize,v_count:usize,
 pub adapter:wgpu::AdapterInfo,
}
impl PressureProjectionGpu{
 pub async fn new(width:usize,height:usize)->Result<Self,String>{
  let context=Arc::new(crate::gpu_context::GpuContext::new().await?);
  Self::new_with_context(context,width,height).await
 }
 pub async fn new_with_context(context:Arc<crate::gpu_context::GpuContext>,width:usize,height:usize)->Result<Self,String>{
  if width<2||height<2{return Err("pressure grid must be at least 2x2".into());}
  let cells=width.checked_mul(height).ok_or("cell count overflow")?;
  let u_count=height.checked_mul(width+1).ok_or("u-face count overflow")?;
  let v_count=(height+1).checked_mul(width).ok_or("v-face count overflow")?;
  if [cells,u_count,v_count].iter().any(|&n|n>u32::MAX as usize){return Err("pressure grid exceeds u32 shader indexing".into());}
  let device=&context.device;let info=context.adapter.clone();let limits=device.limits();
  let largest=[cells,u_count,v_count].into_iter().max().unwrap()*4;
  if largest as u64>limits.max_storage_buffer_binding_size as u64
   ||(width+1).div_ceil(8)>limits.max_compute_workgroups_per_dimension as usize
   ||(height+1).div_ceil(8)>limits.max_compute_workgroups_per_dimension as usize{
   return Err("pressure grid exceeds Vulkan storage or dispatch limits".into());
  }
  let scope=device.push_error_scope(wgpu::ErrorFilter::Validation);
  let shader=device.create_shader_module(wgpu::ShaderModuleDescriptor{label:Some("OniSim pressure projection WGSL"),source:wgpu::ShaderSource::Wgsl(SHADER.into())});
  let make_pipeline=|label:&'static str,entry_point:&'static str|{
   device.create_compute_pipeline(&wgpu::ComputePipelineDescriptor{label:Some(label),layout:None,module:&shader,entry_point:Some(entry_point),compilation_options:Default::default(),cache:None})
  };
  let density_from_mass=make_pipeline("density from species mass","compute_density_from_species_mass");
  let reconstruct_velocity=make_pipeline("reconstruct cell velocity","reconstruct_cell_velocity");
  let prepare_u=make_pipeline("prepare u faces","prepare_u_faces");
  let prepare_v=make_pipeline("prepare v faces","prepare_v_faces");
  let divergence=make_pipeline("compute divergence","compute_divergence");
  let rbgs=make_pipeline("red-black gauss-seidel","rbgs_pressure");
  let project_u=make_pipeline("project u faces","project_u_faces");
  let project_v=make_pipeline("project v faces","project_v_faces");
  let storage=wgpu::BufferUsages::STORAGE|wgpu::BufferUsages::COPY_DST;
  let make_buffer=|label:&'static str,bytes:usize,usage:wgpu::BufferUsages|{
   device.create_buffer(&wgpu::BufferDescriptor{label:Some(label),size:bytes.max(4)as u64,usage,mapped_at_creation:false})
  };
  let density=make_buffer("cell density",cells*4,storage);
  let velocity_x=make_buffer("cell velocity x",cells*4,storage);
  let velocity_y=make_buffer("cell velocity y",cells*4,storage);
  let solid=make_buffer("solid mask",cells*4,storage);
  let u_faces=make_buffer("u face velocity",u_count*4,storage|wgpu::BufferUsages::COPY_SRC);
  let v_faces=make_buffer("v face velocity",v_count*4,storage|wgpu::BufferUsages::COPY_SRC);
  let beta_u=make_buffer("u inverse face density",u_count*4,storage);
  let beta_v=make_buffer("v inverse face density",v_count*4,storage);
  let rhs=make_buffer("pressure poisson rhs",cells*4,storage);
  let pressure=make_buffer("pressure solution",cells*4,storage|wgpu::BufferUsages::COPY_SRC);
  let params_red=make_buffer("red parameters",64,wgpu::BufferUsages::UNIFORM|wgpu::BufferUsages::COPY_DST);
  let params_black=make_buffer("black parameters",64,wgpu::BufferUsages::UNIFORM|wgpu::BufferUsages::COPY_DST);
  let readback=make_buffer("projection readback",(cells+u_count+v_count)*4,wgpu::BufferUsages::MAP_READ|wgpu::BufferUsages::COPY_DST);
  let buffers:[&wgpu::Buffer;10]=[&density,&velocity_x,&velocity_y,&solid,&u_faces,&v_faces,&beta_u,&beta_v,&rhs,&pressure];
  let bind=|pipeline:&wgpu::ComputePipeline,bindings:&[u32],params:&wgpu::Buffer|{
   let entries:Vec<_>=bindings.iter().map(|&binding|{
    let resource=if binding==10{params}else{buffers[binding as usize]};
    wgpu::BindGroupEntry{binding,resource:resource.as_entire_binding()}
   }).collect();
   device.create_bind_group(&wgpu::BindGroupDescriptor{label:Some("pressure projection bind group"),layout:&pipeline.get_bind_group_layout(0),entries:&entries})
  };
  let groups=[
   bind(&prepare_u,&[0,1,3,4,6,10],&params_red),
   bind(&prepare_v,&[0,2,3,5,7,10],&params_red),
   bind(&divergence,&[3,4,5,8,9,10],&params_red),
   bind(&rbgs,&[3,6,7,8,9,10],&params_red),
   bind(&rbgs,&[3,6,7,8,9,10],&params_black),
   bind(&project_u,&[4,6,9,10],&params_red),
   bind(&project_v,&[5,7,9,10],&params_red),
   bind(&reconstruct_velocity,&[1,2,3,4,5,10],&params_red),
  ];
  if let Some(error)=scope.pop().await{return Err(error.to_string());}
  Ok(Self{device:context.device.clone(),queue:context.queue.clone(),density_from_mass,reconstruct_velocity,prepare_u,prepare_v,divergence,rbgs,project_u,project_v,groups,shared_bindings:Vec::new(),
   density,velocity_x,velocity_y,solid,u_faces,v_faces,beta_u,beta_v,rhs,pressure,
   params_red,params_black,readback,width,height,cells,u_count,v_count,adapter:info})
 }
 pub fn run(&mut self,input:ProjectionInput<'_>,dt:f32,dx:f32,dy:f32,iterations:usize,omega:f32)->Result<ProjectionOutput,String>{
  if input.density.len()!=self.cells||input.velocity_x.len()!=self.cells||input.velocity_y.len()!=self.cells||input.solid.len()!=self.cells{return Err("pressure input arrays do not match grid dimensions".into());}
  if !dt.is_finite()||dt<=0.0||!dx.is_finite()||dx<=0.0||!dy.is_finite()||dy<=0.0||iterations==0||!omega.is_finite()||!(0.0<omega&&omega<2.0){return Err("invalid pressure timestep, spacing, iteration count or relaxation".into());}
  if input.density.iter().any(|v|!v.is_finite()||*v<=0.0)||input.velocity_x.iter().chain(input.velocity_y).any(|v|!v.is_finite()){return Err("pressure input contains invalid density or non-finite velocity".into());}
  let gauge=input.solid.iter().position(|&wall|wall==0).ok_or("pressure domain contains no fluid cells")?;
  let params=[self.width as u32,self.height as u32,dt.to_bits(),dx.to_bits(),dy.to_bits(),omega.to_bits(),0,gauge as u32,1,1.0f32.to_bits(),1.0f32.to_bits(),0,0,0,0,0];
  self.queue.write_buffer(&self.params_red,0,bytemuck::cast_slice(&params));
  let mut black=params;black[6]=1;
  self.queue.write_buffer(&self.params_black,0,bytemuck::cast_slice(&black));
  self.queue.write_buffer(&self.density,0,bytemuck::cast_slice(input.density));
  self.queue.write_buffer(&self.velocity_x,0,bytemuck::cast_slice(input.velocity_x));
  self.queue.write_buffer(&self.velocity_y,0,bytemuck::cast_slice(input.velocity_y));
  self.queue.write_buffer(&self.solid,0,bytemuck::cast_slice(input.solid));
  let mut encoder=self.device.create_command_encoder(&Default::default());
  {
   let mut pass=encoder.begin_compute_pass(&Default::default());pass.set_pipeline(&self.prepare_u);pass.set_bind_group(0,&self.groups[0],&[]);
   pass.dispatch_workgroups((self.width as u32+1).div_ceil(8),(self.height as u32).div_ceil(8),1);
  }
  {
   let mut pass=encoder.begin_compute_pass(&Default::default());pass.set_pipeline(&self.prepare_v);pass.set_bind_group(0,&self.groups[1],&[]);
   pass.dispatch_workgroups((self.width as u32).div_ceil(8),(self.height as u32+1).div_ceil(8),1);
  }
  {
   let mut pass=encoder.begin_compute_pass(&Default::default());pass.set_pipeline(&self.divergence);pass.set_bind_group(0,&self.groups[2],&[]);
   pass.dispatch_workgroups((self.width as u32).div_ceil(8),(self.height as u32).div_ceil(8),1);
  }
  for _ in 0..iterations{for color in 0..2{
   let mut pass=encoder.begin_compute_pass(&Default::default());pass.set_pipeline(&self.rbgs);pass.set_bind_group(0,&self.groups[3+color],&[]);
   pass.dispatch_workgroups((self.width as u32).div_ceil(8),(self.height as u32).div_ceil(8),1);
  }}
  {
   let mut pass=encoder.begin_compute_pass(&Default::default());pass.set_pipeline(&self.project_u);pass.set_bind_group(0,&self.groups[5],&[]);
   pass.dispatch_workgroups((self.width as u32+1).div_ceil(8),(self.height as u32).div_ceil(8),1);
  }
  {
   let mut pass=encoder.begin_compute_pass(&Default::default());pass.set_pipeline(&self.project_v);pass.set_bind_group(0,&self.groups[6],&[]);
   pass.dispatch_workgroups((self.width as u32).div_ceil(8),(self.height as u32+1).div_ceil(8),1);
  }
  {
   let mut pass=encoder.begin_compute_pass(&Default::default());pass.set_pipeline(&self.reconstruct_velocity);pass.set_bind_group(0,&self.groups[7],&[]);
   pass.dispatch_workgroups((self.width as u32).div_ceil(8),(self.height as u32).div_ceil(8),1);
  }
  let u_offset=(self.cells*4)as u64;let v_offset=((self.cells+self.u_count)*4)as u64;
  encoder.copy_buffer_to_buffer(&self.pressure,0,&self.readback,0,(self.cells*4)as u64);
  encoder.copy_buffer_to_buffer(&self.u_faces,0,&self.readback,u_offset,(self.u_count*4)as u64);
  encoder.copy_buffer_to_buffer(&self.v_faces,0,&self.readback,v_offset,(self.v_count*4)as u64);
  let submission=self.queue.submit([encoder.finish()]);
  let(send,receive)=mpsc::sync_channel(1);
  self.readback.map_async(wgpu::MapMode::Read,..,move|result|{let _=send.send(result);});
  self.device.poll(wgpu::PollType::Wait{submission_index:Some(submission),timeout:Some(Duration::from_secs(30))}).map_err(|e|e.to_string())?;
  receive.recv_timeout(Duration::from_secs(2)).map_err(|e|e.to_string())?.map_err(|e|e.to_string())?;
  let(pressure,u_faces,v_faces)={
   let view=self.readback.get_mapped_range(..).map_err(|e|e.to_string())?;
   let values:&[f32]=bytemuck::cast_slice(&view);
   (values[..self.cells].to_vec(),values[self.cells..self.cells+self.u_count].to_vec(),values[self.cells+self.u_count..self.cells+self.u_count+self.v_count].to_vec())
  };
  self.readback.unmap();
  Ok(ProjectionOutput{pressure,u_faces,v_faces,iterations,adapter:self.adapter.clone()})
 }

 /// Run the projection directly against another operator's device-local state.
 /// The mass tensor is NHWC; density is computed on the GPU as total mass / cell_volume.
 /// The updated cell-centred velocity is written back into the supplied velocity buffers.
 /// Empty cells produce zero density and block adjacent faces; open-vacuum flow is not modelled.
 pub fn run_with_shared_state(
  &mut self,
  mass:&wgpu::Buffer,
  velocity_x:&wgpu::Buffer,
  velocity_y:&wgpu::Buffer,
  solid:&wgpu::Buffer,
  channels:usize,
  cell_volume:f32,
  mass_scale_kg_per_game_unit:f32,
  gauge_cell:usize,
  dt:f32,
  dx:f32,
  dy:f32,
  iterations:usize,
  omega:f32,
 )->Result<(),String>{
  let mut encoder=self.device.create_command_encoder(&Default::default());
  self.encode_with_shared_state(&mut encoder,mass,velocity_x,velocity_y,solid,channels,cell_volume,
   mass_scale_kg_per_game_unit,gauge_cell,dt,dx,dy,iterations,omega)?;
  self.queue.submit([encoder.finish()]);
  Ok(())
 }

 /// Encode projection into a caller-owned command buffer, allowing dependent GPU stages to share a submission.
 pub fn encode_with_shared_state(
  &mut self, encoder:&mut wgpu::CommandEncoder, mass:&wgpu::Buffer, velocity_x:&wgpu::Buffer,
  velocity_y:&wgpu::Buffer, solid:&wgpu::Buffer, channels:usize, cell_volume:f32,
  mass_scale_kg_per_game_unit:f32, gauge_cell:usize, dt:f32, dx:f32, dy:f32,
  iterations:usize, omega:f32,
 )->Result<(),String>{
  if channels==0||channels>u32::MAX as usize||!cell_volume.is_finite()||cell_volume<=0.0||!mass_scale_kg_per_game_unit.is_finite()||mass_scale_kg_per_game_unit<=0.0{
   return Err("shared state requires positive channel count and cell volume".into());
  }
  if gauge_cell>=self.cells||!dt.is_finite()||dt<=0.0||!dx.is_finite()||dx<=0.0
   ||!dy.is_finite()||dy<=0.0||iterations==0||!omega.is_finite()||!(0.0<omega&&omega<2.0){
   return Err("invalid shared pressure projection parameters".into());
  }
  let params=[self.width as u32,self.height as u32,dt.to_bits(),dx.to_bits(),dy.to_bits(),
   omega.to_bits(),0,gauge_cell as u32,channels as u32,cell_volume.to_bits(),mass_scale_kg_per_game_unit.to_bits(),0,0,0,0,0];
  self.queue.write_buffer(&self.params_red,0,bytemuck::cast_slice(&params));
  let mut black=params;black[6]=1;
  self.queue.write_buffer(&self.params_black,0,bytemuck::cast_slice(&black));

  let cached = self.shared_bindings.iter().position(|entry|
   entry.buffers[0] == *mass && entry.buffers[1] == *velocity_x &&
   entry.buffers[2] == *velocity_y && entry.buffers[3] == *solid);
  let index = if let Some(index)=cached { index } else {
  let bind_shared=|pipeline:&wgpu::ComputePipeline,bindings:&[u32],params_buffer:&wgpu::Buffer|{
   let entries:Vec<_>=bindings.iter().map(|&binding|{
    let resource=match binding{
     0=>self.density.as_entire_binding(),
     1=>velocity_x.as_entire_binding(),
     2=>velocity_y.as_entire_binding(),
     3=>solid.as_entire_binding(),
     4=>self.u_faces.as_entire_binding(),
     5=>self.v_faces.as_entire_binding(),
     6=>self.beta_u.as_entire_binding(),
     7=>self.beta_v.as_entire_binding(),
     8=>self.rhs.as_entire_binding(),
     9=>self.pressure.as_entire_binding(),
     10=>params_buffer.as_entire_binding(),
     11=>mass.as_entire_binding(),
     _=>unreachable!("unknown pressure projection binding"),
    };
    wgpu::BindGroupEntry{binding,resource}
   }).collect();
   self.device.create_bind_group(&wgpu::BindGroupDescriptor{
    label:Some("shared OniSim pressure projection bindings"),
    layout:&pipeline.get_bind_group_layout(0),entries:&entries,
   })
  };
  let groups=[
   bind_shared(&self.density_from_mass,&[0,3,10,11],&self.params_red),
   bind_shared(&self.prepare_u,&[0,1,3,4,6,10],&self.params_red),
   bind_shared(&self.prepare_v,&[0,2,3,5,7,10],&self.params_red),
   bind_shared(&self.divergence,&[3,4,5,8,9,10],&self.params_red),
   bind_shared(&self.rbgs,&[3,6,7,8,9,10],&self.params_red),
   bind_shared(&self.rbgs,&[3,6,7,8,9,10],&self.params_black),
   bind_shared(&self.project_u,&[4,6,9,10],&self.params_red),
   bind_shared(&self.project_v,&[5,7,9,10],&self.params_red),
   bind_shared(&self.reconstruct_velocity,&[1,2,3,4,5,10],&self.params_red),
  ];
  if self.shared_bindings.len() == 2 { self.shared_bindings.remove(0); }
  self.shared_bindings.push(SharedBindings { buffers:[mass.clone(),velocity_x.clone(),velocity_y.clone(),solid.clone()], groups });
  self.shared_bindings.len()-1
  };
  let groups=&self.shared_bindings[index].groups;
  let dispatch=|encoder:&mut wgpu::CommandEncoder,pipeline:&wgpu::ComputePipeline,group:&wgpu::BindGroup,x:u32,y:u32|{
   let mut pass=encoder.begin_compute_pass(&Default::default());
   pass.set_pipeline(pipeline);pass.set_bind_group(0,group,&[]);pass.dispatch_workgroups(x,y,1);
  };
  dispatch(encoder,&self.density_from_mass,&groups[0],(self.width as u32).div_ceil(8),(self.height as u32).div_ceil(8));
  dispatch(encoder,&self.prepare_u,&groups[1],(self.width as u32+1).div_ceil(8),(self.height as u32).div_ceil(8));
  dispatch(encoder,&self.prepare_v,&groups[2],(self.width as u32).div_ceil(8),(self.height as u32+1).div_ceil(8));
  dispatch(encoder,&self.divergence,&groups[3],(self.width as u32).div_ceil(8),(self.height as u32).div_ceil(8));
  for _ in 0..iterations{
   dispatch(encoder,&self.rbgs,&groups[4],(self.width as u32).div_ceil(8),(self.height as u32).div_ceil(8));
   dispatch(encoder,&self.rbgs,&groups[5],(self.width as u32).div_ceil(8),(self.height as u32).div_ceil(8));
  }
  dispatch(encoder,&self.project_u,&groups[6],(self.width as u32+1).div_ceil(8),(self.height as u32).div_ceil(8));
  dispatch(encoder,&self.project_v,&groups[7],(self.width as u32).div_ceil(8),(self.height as u32+1).div_ceil(8));
  dispatch(encoder,&self.reconstruct_velocity,&groups[8],(self.width as u32).div_ceil(8),(self.height as u32).div_ceil(8));
  Ok(())
 }

}
#[cfg(test)]
mod tests{
 #[test]
 fn pressure_projection_wgsl_validates(){
  let module=naga::front::wgsl::parse_str(super::SHADER).expect("pressure WGSL parser");
  naga::valid::Validator::new(naga::valid::ValidationFlags::all(),naga::valid::Capabilities::all()).validate(&module).expect("pressure WGSL validation");
 }
}
