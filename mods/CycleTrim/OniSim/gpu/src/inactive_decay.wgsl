struct Cell { mass: f32, phase: u32 }
struct Params { count: u32, _pad0: u32, _pad1: u32, _pad2: u32 }
@group(0) @binding(0) var<storage, read> input: array<Cell>;
@group(0) @binding(1) var<storage, read_write> output: array<f32>;
@group(0) @binding(2) var<uniform> params: Params;

@compute @workgroup_size(64)
fn main(@builtin(global_invocation_id) gid: vec3<u32>) {
    let i = gid.x;
    if (i >= params.count) { return; }
    let c = input[i];
    var decay = 0.0;
    if (c.phase == 1u) { decay = bitcast<f32>(0x3ca3d70au); }
    if (c.phase == 2u) { decay = 20.0; }
    let mass = c.mass - decay;
    output[i] = select(mass, 0.0, mass <= 0.0);
}
