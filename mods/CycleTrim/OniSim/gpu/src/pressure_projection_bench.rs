use onisim_gpu::pressure_projection::{PressureProjectionGpu,ProjectionInput};
use serde::Serialize;
use std::{hint::black_box,time::Instant};

struct Fixture{rho:Vec<f32>,ux:Vec<f32>,uy:Vec<f32>,solid:Vec<u32>}
impl Fixture{
 fn new(w:usize,h:usize)->Self{
  let n=w*h;let mut seed=0x6d2b79f5u32;
  let mut next=||{seed^=seed<<13;seed^=seed>>17;seed^=seed<<5;seed};
  let mut solid=vec![0;n];
  for x in 0..w{solid[x]=1;solid[(h-1)*w+x]=1;}
  for y in 0..h{solid[y*w]=1;solid[y*w+w-1]=1;}
  if w>20&&h>20{for y in h/3..h/3+9{for x in w/3..w/3+13{solid[y*w+x]=1;}}}
  let rho=(0..n).map(|i|1.0+(i%17)as f32*0.025).collect();
  let ux=(0..n).map(|_|((next()%2001)as i32-1000)as f32*0.0005).collect();
  let uy=(0..n).map(|_|((next()%2001)as i32-1000)as f32*0.0005).collect();
  Self{rho,ux,uy,solid}
 }
 fn input(&self)->ProjectionInput<'_>{ProjectionInput{density:&self.rho,velocity_x:&self.ux,velocity_y:&self.uy,solid:&self.solid}}
}
#[derive(Serialize)]
struct Row{width:usize,height:usize,iterations:usize,median_us:f64,p95_us:f64,divergence_before_l2:f64,divergence_after_l2:f64,divergence_ratio:f64}
#[derive(Serialize)]
struct Report{stage:&'static str,adapter:String,vendor:u32,device_type:String,backend:String,warmup:usize,samples:usize,rows:Vec<Row>}
fn median(v:&mut[f64])->f64{v.sort_by(f64::total_cmp);v[v.len()/2]}
fn divergence(w:usize,h:usize,u:&[f32],v:&[f32],solid:&[u32])->f64{
 let mut sum=0.0f64;
 for y in 0..h{for x in 0..w{let i=y*w+x;if solid[i]!=0{continue;}let d=(u[y*(w+1)+x+1]-u[y*(w+1)+x])+(v[(y+1)*w+x]-v[y*w+x]);sum+=(d as f64)*(d as f64);}}
 sum.sqrt()
}
fn initial_faces(f:&Fixture,w:usize,h:usize)->(Vec<f32>,Vec<f32>){
 let mut u=vec![0.0;h*(w+1)];let mut v=vec![0.0;(h+1)*w];
 for y in 0..h{for x in 1..w{let a=y*w+x-1;let b=a+1;if f.solid[a]==0&&f.solid[b]==0{u[y*(w+1)+x]=0.5*(f.ux[a]+f.ux[b]);}}}
 for y in 1..h{for x in 0..w{let a=(y-1)*w+x;let b=a+w;if f.solid[a]==0&&f.solid[b]==0{v[y*w+x]=0.5*(f.uy[a]+f.uy[b]);}}}
 (u,v)
}
fn main()->Result<(),String>{
 let iterations=std::env::var("ONISIM_PRESSURE_ITERATIONS").ok().and_then(|v|v.parse().ok()).unwrap_or(256usize);
 let mut rows=Vec::new();let(mut adapter_name,mut vendor,mut device_type,mut backend)=(String::new(),0,String::new(),String::new());
 for(w,h)in[(96,96),(256,384)]{
  let fixture=Fixture::new(w,h);let(u0,v0)=initial_faces(&fixture,w,h);
  let before=divergence(w,h,&u0,&v0,&fixture.solid);
  let mut gpu=pollster::block_on(PressureProjectionGpu::new(w,h))?;
  adapter_name=gpu.adapter.name.clone();vendor=gpu.adapter.vendor;device_type=format!("{:?}",gpu.adapter.device_type);backend=format!("{:?}",gpu.adapter.backend);
  let mut output=gpu.run(fixture.input(),0.2,1.0,1.0,iterations,1.0)?;
  let after=divergence(w,h,&output.u_faces,&output.v_faces,&fixture.solid);
  let mut samples=Vec::new();
  for _ in 0..5{output=gpu.run(fixture.input(),0.2,1.0,1.0,iterations,1.0)?;}
  for _ in 0..31{let t=Instant::now();output=black_box(gpu.run(fixture.input(),0.2,1.0,1.0,iterations,1.0)?);samples.push(t.elapsed().as_secs_f64()*1e6);}
  let p95_index=(samples.len()*95).div_ceil(100)-1;
  let mut sorted=samples.clone();sorted.sort_by(f64::total_cmp);let p95=sorted[p95_index];let med=median(&mut samples);
  let after=divergence(w,h,&output.u_faces,&output.v_faces,&fixture.solid);
  rows.push(Row{width:w,height:h,iterations,median_us:med,p95_us:p95,divergence_before_l2:before,divergence_after_l2:after,divergence_ratio:after/before.max(1e-30)});
 }
 println!("{}",serde_json::to_string_pretty(&Report{stage:"Stage 2 Vulkan Poisson pressure projection",adapter:adapter_name,vendor,device_type,backend,warmup:5,samples:31,rows}).map_err(|e|e.to_string())?);
 Ok(())
}
