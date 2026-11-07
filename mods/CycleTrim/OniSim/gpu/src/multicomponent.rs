use std::{sync::{mpsc, Arc}, time::Duration};
const SHADER: &str = include_str!("multicomponent_flux.wgsl");

pub struct State<'a> {
    pub mass: &'a [f32], pub velocity_x: &'a [f32], pub velocity_y: &'a [f32],
    pub solid: &'a [u32], pub drift_y: &'a [f32],
}
pub struct MulticomponentGpu {
    device: Arc<wgpu::Device>, queue: Arc<wgpu::Queue>, pipeline: wgpu::ComputePipeline,
    groups: [wgpu::BindGroup; 2], mass_a: wgpu::Buffer, mass_b: wgpu::Buffer,
    velocity_x: wgpu::Buffer, velocity_y: wgpu::Buffer, solid: wgpu::Buffer,
    drift_y: wgpu::Buffer, params: wgpu::Buffer, readback: wgpu::Buffer,
    width: usize, height: usize, channels: usize, count: usize, uploaded: bool, max_cfl_rate: f32,
    pub adapter: wgpu::AdapterInfo,
}
impl MulticomponentGpu {
    pub async fn new(width: usize, height: usize, channels: usize) -> Result<Self,String> {
        let context = Arc::new(crate::gpu_context::GpuContext::new().await?);
        Self::new_with_context(context, width, height, channels).await
    }

    pub async fn new_with_context(context: Arc<crate::gpu_context::GpuContext>, width: usize, height: usize, channels: usize) -> Result<Self,String> {
        let count=width.checked_mul(height).ok_or("grid overflow")?;
        let values=count.checked_mul(channels).ok_or("tensor overflow")?;
        if width==0 || height==0 || channels==0 || values>u32::MAX as usize { return Err("invalid grid dimensions".into()); }
        let device=&context.device;
        let info=context.adapter.clone();
        let max_bytes=(values.max(count).max(channels)*4) as u64;
        if max_bytes>device.limits().max_storage_buffer_binding_size as u64 { return Err("tensor exceeds Vulkan storage-buffer limit".into()); }
        let scope=device.push_error_scope(wgpu::ErrorFilter::Validation);
        let shader=device.create_shader_module(wgpu::ShaderModuleDescriptor{label:Some("multicomponent finite-volume flux"),source:wgpu::ShaderSource::Wgsl(SHADER.into())});
        let pipeline=device.create_compute_pipeline(&wgpu::ComputePipelineDescriptor{label:Some("multicomponent flux pipeline"),layout:None,module:&shader,entry_point:Some("main"),compilation_options:Default::default(),cache:None});
        let storage=wgpu::BufferUsages::STORAGE|wgpu::BufferUsages::COPY_DST;
        let make=|label:&str,bytes:usize,usage|device.create_buffer(&wgpu::BufferDescriptor{label:Some(label),size:bytes.max(4) as u64,usage,mapped_at_creation:false});
        let mass_a=make("mass A",values*4,storage|wgpu::BufferUsages::COPY_SRC);
        let mass_b=make("mass B",values*4,storage|wgpu::BufferUsages::COPY_SRC);
        let velocity_x=make("velocity x",count*4,storage);
        let velocity_y=make("velocity y",count*4,storage);
        let solid=make("solid mask",count*4,storage);
        let drift_y=make("species drift",channels*4,storage);
        let params=make("uniform parameters",16,wgpu::BufferUsages::UNIFORM|wgpu::BufferUsages::COPY_DST);
        let readback=make("mass readback",values*4,wgpu::BufferUsages::MAP_READ|wgpu::BufferUsages::COPY_DST);
        let create_group=|input:&wgpu::Buffer,output:&wgpu::Buffer|{
            let entries=[
                bind(0,input),bind(1,&velocity_x),bind(2,&velocity_y),bind(3,&solid),
                bind(4,&drift_y),bind(5,output),bind(6,&params),
            ];
            device.create_bind_group(&wgpu::BindGroupDescriptor{label:Some("multicomponent bindings"),layout:&pipeline.get_bind_group_layout(0),entries:&entries})
        };
        let groups=[create_group(&mass_a,&mass_b),create_group(&mass_b,&mass_a)];
        if let Some(e)=scope.pop().await{return Err(e.to_string());}
        Ok(Self{device:context.device.clone(),queue:context.queue.clone(),pipeline,groups,mass_a,mass_b,velocity_x,velocity_y,solid,drift_y,params,readback,width,height,channels,count,uploaded:false,max_cfl_rate:0.0,adapter:info})
    }
    pub fn upload(&mut self,state:&State<'_>)->Result<(),String>{
        self.uploaded=false;
        if state.mass.len()!=self.count*self.channels||state.velocity_x.len()!=self.count||state.velocity_y.len()!=self.count||state.solid.len()!=self.count||state.drift_y.len()!=self.channels{return Err("input tensor dimensions mismatch".into());}
        if state.mass.iter().any(|v| !v.is_finite() || *v < 0.0)
            || state.velocity_x.iter().chain(state.velocity_y).chain(state.drift_y).any(|v| !v.is_finite()) {
            return Err("state contains negative mass or non-finite values".into());
        }
        let mut max_rate=0.0f32;
        for cell in 0..self.count {
            if state.solid[cell] != 0 && state.mass[cell*self.channels..(cell+1)*self.channels].iter().any(|v| *v != 0.0) {
                return Err("solid cells must contain zero mass".into());
            }
            let max_vertical=state.drift_y.iter().map(|d|(state.velocity_y[cell]+*d).abs()).fold(0.0f32,f32::max);
            max_rate=max_rate.max(state.velocity_x[cell].abs()+max_vertical);
        }
        self.max_cfl_rate=max_rate;
        self.queue.write_buffer(&self.mass_a,0,bytemuck::cast_slice(state.mass));
        self.queue.write_buffer(&self.velocity_x,0,bytemuck::cast_slice(state.velocity_x));
        self.queue.write_buffer(&self.velocity_y,0,bytemuck::cast_slice(state.velocity_y));
        self.queue.write_buffer(&self.solid,0,bytemuck::cast_slice(state.solid));
        self.queue.write_buffer(&self.drift_y,0,bytemuck::cast_slice(state.drift_y));
        self.uploaded=true;Ok(())
    }
    pub fn run_steps_and_readback(&mut self,steps:usize,dt:f32,output:&mut[f32])->Result<(),String>{
        if !self.uploaded||steps==0||output.len()!=self.count*self.channels||!dt.is_finite()||dt<=0.0||dt*self.max_cfl_rate>1.0{return Err("resident run requires valid uploaded state, positive steps/dt and matching output".into());}
        self.queue.write_buffer(&self.params,0,bytemuck::cast_slice(&[self.width as u32,self.height as u32,self.channels as u32,dt.to_bits()]));
        let mut encoder=self.device.create_command_encoder(&Default::default());
        for step in 0..steps{
            let mut pass=encoder.begin_compute_pass(&Default::default());
            pass.set_pipeline(&self.pipeline);pass.set_bind_group(0,&self.groups[step&1],&[]);
            pass.dispatch_workgroups((self.width as u32).div_ceil(8),(self.height as u32).div_ceil(8),1);
        }
        let final_buffer=if steps&1==1{&self.mass_b}else{&self.mass_a};
        encoder.copy_buffer_to_buffer(final_buffer,0,&self.readback,0,(output.len()*4) as u64);
        let submission=self.queue.submit([encoder.finish()]);
        let(send,receive)=mpsc::sync_channel(1);
        self.readback.map_async(wgpu::MapMode::Read,..,move|r|{let _=send.send(r);});
        self.device.poll(wgpu::PollType::Wait{submission_index:Some(submission),timeout:Some(Duration::from_secs(30))}).map_err(|e|e.to_string())?;
        receive.recv_timeout(Duration::from_secs(2)).map_err(|e|e.to_string())?.map_err(|e|e.to_string())?;
        {let view=self.readback.get_mapped_range(..).map_err(|e|e.to_string())?;output.copy_from_slice(bytemuck::cast_slice(&view));}
        self.readback.unmap();Ok(())
    }
}
fn bind(binding:u32,buffer:&wgpu::Buffer)->wgpu::BindGroupEntry<'_>{wgpu::BindGroupEntry{binding,resource:buffer.as_entire_binding()}}

pub fn cpu_step(state:&State<'_>,width:usize,height:usize,channels:usize,dt:f32,output:&mut[f32]){
    let idx=|x:usize,y:usize,c:usize|(y*width+x)*channels+c;
    output.copy_from_slice(state.mass);
    for y in 0..height{for x in 0..width{
        let cell=y*width+x;
        if state.solid[cell]!=0{output[cell*channels..(cell+1)*channels].fill(0.0);continue;}
        for c in 0..channels{
            let mut div=0.0f32;
            if x+1<width&&state.solid[cell+1]==0{let v=0.5*(state.velocity_x[cell]+state.velocity_x[cell+1]);let d=if v>=0.0{state.mass[idx(x,y,c)]}else{state.mass[idx(x+1,y,c)]};div+=v*d;}
            if x>0&&state.solid[cell-1]==0{let v=0.5*(state.velocity_x[cell-1]+state.velocity_x[cell]);let d=if v>=0.0{state.mass[idx(x-1,y,c)]}else{state.mass[idx(x,y,c)]};div-=v*d;}
            if y+1<height&&state.solid[cell+width]==0{let v=0.5*(state.velocity_y[cell]+state.velocity_y[cell+width])+state.drift_y[c];let d=if v>=0.0{state.mass[idx(x,y,c)]}else{state.mass[idx(x,y+1,c)]};div+=v*d;}
            if y>0&&state.solid[cell-width]==0{let v=0.5*(state.velocity_y[cell-width]+state.velocity_y[cell])+state.drift_y[c];let d=if v>=0.0{state.mass[idx(x,y-1,c)]}else{state.mass[idx(x,y,c)]};div-=v*d;}
            output[idx(x,y,c)]=state.mass[idx(x,y,c)]-dt*div;
        }
    }}
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn wgsl_shader_validates() {
        let module = naga::front::wgsl::parse_str(SHADER).expect("WGSL parser");
        naga::valid::Validator::new(
            naga::valid::ValidationFlags::all(),
            naga::valid::Capabilities::all(),
        ).validate(&module).expect("WGSL validation");
    }

    #[test]
    fn cpu_reference_conserves_each_species_and_respects_walls() {
        let (width, height, channels) = (19usize, 13usize, 4usize);
        let count = width * height;
        let mut solid = vec![0u32; count];
        for x in 0..width { solid[x] = 1; solid[(height - 1) * width + x] = 1; }
        for y in 0..height { solid[y * width] = 1; solid[y * width + width - 1] = 1; }
        for y in 4..7 { for x in 8..11 { solid[y * width + x] = 1; } }
        let mut mass = vec![0.0f32; count * channels];
        for cell in 0..count {
            if solid[cell] == 0 {
                for c in 0..channels { mass[cell * channels + c] = ((cell * 17 + c * 13) % 97) as f32 / 97.0; }
            }
        }
        let vx = vec![0.015f32; count];
        let vy = vec![-0.01f32; count];
        let drift = [-0.002f32, -0.001, 0.001, 0.002];
        let state = State { mass: &mass, velocity_x: &vx, velocity_y: &vy, solid: &solid, drift_y: &drift };
        let mut output = vec![0.0f32; count * channels];
        cpu_step(&state, width, height, channels, 0.1, &mut output);
        for c in 0..channels {
            let before: f64 = (0..count).map(|i| mass[i * channels + c] as f64).sum();
            let after: f64 = (0..count).map(|i| output[i * channels + c] as f64).sum();
            assert!((after - before).abs() / before.max(1e-30) < 1e-6, "species {c}");
        }
        assert!(output.iter().all(|v| v.is_finite() && *v >= -1e-7));
        for cell in 0..count {
            if solid[cell] != 0 { assert!(output[cell * channels..(cell + 1) * channels].iter().all(|v| *v == 0.0)); }
        }
    }
}
