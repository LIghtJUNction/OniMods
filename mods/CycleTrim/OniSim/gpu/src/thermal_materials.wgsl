struct Params { cells: u32, materials: u32, unused0: u32, unused1: u32 }
@group(0) @binding(0) var<storage, read> element_pairs: array<u32>;
@group(0) @binding(1) var<storage, read> material: array<vec2<f32>>;
@group(0) @binding(2) var<storage, read_write> conductivity: array<f32>;
@group(0) @binding(3) var<storage, read_write> specific_heat: array<f32>;
@group(0) @binding(4) var<uniform> params: Params;
@group(0) @binding(5) var<storage, read> dirty_workgroups: array<u32>;

@compute @workgroup_size(64)
fn main(@builtin(workgroup_id) workgroup: vec3<u32>, @builtin(local_invocation_index) local: u32) {
    let cell = dirty_workgroups[workgroup.x] * 64u + local;
    if cell >= params.cells { return; }
    let pair = element_pairs[cell / 2u];
    let id = (pair >> ((cell % 2u) * 16u)) & 65535u;
    if id < params.materials {
        conductivity[cell] = material[id].x;
        specific_heat[cell] = material[id].y;
    } else {
        conductivity[cell] = 0.0;
        specific_heat[cell] = 1.0;
    }
}
