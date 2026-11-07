struct Params {
 width:u32, height:u32, dt:f32, dx:f32, dy:f32, omega:f32, color:u32, gauge_cell:u32,
 channels:u32, cell_volume:f32, mass_scale:f32, _padding0:u32,
 _padding1:u32, _padding2:u32, _padding3:u32,
};
@group(0) @binding(0) var<storage,read_write> rho:array<f32>;
@group(0) @binding(1) var<storage,read_write> velocity_x:array<f32>;
@group(0) @binding(2) var<storage,read_write> velocity_y:array<f32>;
@group(0) @binding(3) var<storage,read> solid:array<u32>;
@group(0) @binding(4) var<storage,read_write> u_faces:array<f32>;
@group(0) @binding(5) var<storage,read_write> v_faces:array<f32>;
@group(0) @binding(6) var<storage,read_write> beta_u:array<f32>;
@group(0) @binding(7) var<storage,read_write> beta_v:array<f32>;
@group(0) @binding(8) var<storage,read_write> rhs:array<f32>;
@group(0) @binding(9) var<storage,read_write> pressure:array<f32>;
@group(0) @binding(10) var<uniform> params:Params;
@group(0) @binding(11) var<storage,read> mass:array<f32>;

@compute @workgroup_size(8,8,1)
fn prepare_u_faces(@builtin(global_invocation_id) gid:vec3<u32>) {
 let x=gid.x; let y=gid.y; let w=params.width;
 if x>w || y>=params.height { return; }
 let f=y*(w+1u)+x;
 if x==0u || x==w { u_faces[f]=0.0; beta_u[f]=0.0; return; }
 let a=y*w+x-1u; let b=a+1u;
 if solid[a]!=0u || solid[b]!=0u { u_faces[f]=0.0; beta_u[f]=0.0; return; }
 if rho[a]<=0.0 || rho[b]<=0.0 { u_faces[f]=0.0; beta_u[f]=0.0; return; }
 u_faces[f]=0.5*(velocity_x[a]+velocity_x[b]);
 beta_u[f]=2.0/(rho[a]+rho[b]);
}

@compute @workgroup_size(8,8,1)
fn prepare_v_faces(@builtin(global_invocation_id) gid:vec3<u32>) {
 let x=gid.x; let y=gid.y; let w=params.width;
 if x>=w || y>params.height { return; }
 let f=y*w+x;
 if y==0u || y==params.height { v_faces[f]=0.0; beta_v[f]=0.0; return; }
 let a=(y-1u)*w+x; let b=a+w;
 if solid[a]!=0u || solid[b]!=0u { v_faces[f]=0.0; beta_v[f]=0.0; return; }
 if rho[a]<=0.0 || rho[b]<=0.0 { v_faces[f]=0.0; beta_v[f]=0.0; return; }
 v_faces[f]=0.5*(velocity_y[a]+velocity_y[b]);
 beta_v[f]=2.0/(rho[a]+rho[b]);
}

@compute @workgroup_size(8,8,1)
fn compute_divergence(@builtin(global_invocation_id) gid:vec3<u32>) {
 let x=gid.x; let y=gid.y; let w=params.width;
 if x>=w || y>=params.height { return; }
 let i=y*w+x;
 if solid[i]!=0u { rhs[i]=0.0; pressure[i]=0.0; return; }
 let ul=y*(w+1u)+x; let ur=ul+1u; let vt=y*w+x; let vb=vt+w;
 let div=(u_faces[ur]-u_faces[ul])/params.dx+(v_faces[vb]-v_faces[vt])/params.dy;
 rhs[i]=div/params.dt; pressure[i]=0.0;
}

@compute @workgroup_size(8,8,1)
fn rbgs_pressure(@builtin(global_invocation_id) gid:vec3<u32>) {
 let x=gid.x; let y=gid.y; let w=params.width;
 if x>=w || y>=params.height || ((x+y)&1u)!=params.color { return; }
 let i=y*w+x;
 if solid[i]!=0u || i==params.gauge_cell { pressure[i]=0.0; return; }
 let idx=1.0/(params.dx*params.dx); let idy=1.0/(params.dy*params.dy);
 let ul=y*(w+1u)+x; let ur=ul+1u; let vt=y*w+x; let vb=vt+w;
 var diag=0.0; var sum=0.0;
 if x>0u { let q=beta_u[ul]*idx; diag+=q; sum+=q*pressure[i-1u]; }
 if x+1u<w { let q=beta_u[ur]*idx; diag+=q; sum+=q*pressure[i+1u]; }
 if y>0u { let q=beta_v[vt]*idy; diag+=q; sum+=q*pressure[i-w]; }
 if y+1u<params.height { let q=beta_v[vb]*idy; diag+=q; sum+=q*pressure[i+w]; }
 if diag<=0.0 { pressure[i]=0.0; return; }
 let gs=(sum-rhs[i])/diag;
 pressure[i]+=params.omega*(gs-pressure[i]);
}

@compute @workgroup_size(8,8,1)
fn project_u_faces(@builtin(global_invocation_id) gid:vec3<u32>) {
 let x=gid.x; let y=gid.y; let w=params.width;
 if x>w || y>=params.height { return; }
 let f=y*(w+1u)+x;
 if x==0u || x==w || beta_u[f]==0.0 { u_faces[f]=0.0; return; }
 let a=y*w+x-1u; let b=a+1u;
 u_faces[f]-=params.dt*beta_u[f]*(pressure[b]-pressure[a])/params.dx;
}

@compute @workgroup_size(8,8,1)
fn project_v_faces(@builtin(global_invocation_id) gid:vec3<u32>) {
 let x=gid.x; let y=gid.y; let w=params.width;
 if x>=w || y>params.height { return; }
 let f=y*w+x;
 if y==0u || y==params.height || beta_v[f]==0.0 { v_faces[f]=0.0; return; }
 let a=(y-1u)*w+x; let b=a+w;
 v_faces[f]-=params.dt*beta_v[f]*(pressure[b]-pressure[a])/params.dy;
}

@compute @workgroup_size(8, 8, 1)
fn compute_density_from_species_mass(@builtin(global_invocation_id) gid: vec3<u32>) {
    let x = gid.x;
    let y = gid.y;
    if x >= params.width || y >= params.height { return; }
    let cell = y * params.width + x;
    if solid[cell] != 0u {
        rho[cell] = 0.0;
        return;
    }
    var total_mass = 0.0;
    for (var c = 0u; c < params.channels; c += 1u) {
        total_mass += mass[cell * params.channels + c];
    }
    rho[cell] = total_mass * params.mass_scale / params.cell_volume;
}

@compute @workgroup_size(8, 8, 1)
fn reconstruct_cell_velocity(@builtin(global_invocation_id) gid: vec3<u32>) {
    let x = gid.x;
    let y = gid.y;
    if x >= params.width || y >= params.height { return; }
    let cell = y * params.width + x;
    if solid[cell] != 0u {
        velocity_x[cell] = 0.0;
        velocity_y[cell] = 0.0;
        return;
    }
    let u_left = y * (params.width + 1u) + x;
    let u_right = u_left + 1u;
    let v_top = y * params.width + x;
    let v_bottom = v_top + params.width;
    velocity_x[cell] = 0.5 * (u_faces[u_left] + u_faces[u_right]);
    velocity_y[cell] = 0.5 * (v_faces[v_top] + v_faces[v_bottom]);
}
