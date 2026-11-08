struct Params { cells:u32, flags:u32, unused0:u32, unused1:u32 }
@group(0) @binding(0) var<storage,read> element_pairs:array<u32>;
@group(0) @binding(1) var<storage,read> mixture_pairs:array<u32>;
@group(0) @binding(2) var<storage,read> mixture_mass:array<f32>;
@group(0) @binding(3) var<storage,read> property_words:array<u32>;
@group(0) @binding(4) var<storage,read> region:array<u32>;
@group(0) @binding(5) var<storage,read> gas_flags:array<u32>;
@group(0) @binding(6) var<storage,read_write> groups:array<u32>;
@group(0) @binding(7) var<uniform> params:Params;
@group(0) @binding(8) var<storage,read> dirty_workgroups:array<u32>;

@compute @workgroup_size(64)
fn main(@builtin(workgroup_id) workgroup:vec3<u32>, @builtin(local_invocation_index) local:u32) {
    let i=dirty_workgroups[workgroup.x]*64u+local;
    if i>=params.cells {return;}
    let shift=(i%2u)*16u;
    let id=(element_pairs[i/2u]>>shift)&65535u;
    let mix=(mixture_pairs[i/2u]>>shift)&65535u;
    let property=(property_words[i/4u]>>((i%4u)*8u))&255u;
    var eligible=id<params.flags && (property&1u)==0u && region[i]!=0u;
    if eligible { eligible=(gas_flags[id]&1u)!=0u; }
    if eligible && !(mixture_mass[i]<=0.0) {
        eligible=mix<params.flags;
        if eligible {eligible=(gas_flags[mix]&2u)!=0u;}
    }
    groups[i]=select(0u,region[i],eligible);
}
