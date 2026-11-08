// Connected-component hydraulic head, using decreasing atomic parent labels.
struct Params { width: u32, height: u32, vacuum: u32, phase: u32, direction: i32, _pad0: u32, _pad1: u32, _pad2: u32 }
struct Cell {
    element: u32, phase: u32, blocked: u32, _pad: u32,
    mass: f32, temperature: f32, coefficient: f32, _pad1: f32,
    max_mass: f32, viscosity: f32, min_horizontal_flow: f32, min_vertical_flow: f32,
    gas_element: u32, gas_mass: f32, gas_heat: f32, phase_heat: f32,
}
@group(0) @binding(0) var<storage, read> cells: array<Cell>;
@group(0) @binding(1) var<storage, read_write> labels: array<atomic<u32>>;
@group(0) @binding(2) var<storage, read_write> maxima: array<atomic<u32>>;
@group(0) @binding(3) var<uniform> params: Params;
@group(0) @binding(4) var<storage, read_write> head: array<f32>;

fn liquid(i: u32) -> bool {
    let c = cells[i];
    return c.blocked == 0u && c.phase == 2u && c.mass > 0.0 && c.max_mass > 0.0;
}

@compute @workgroup_size(64)
fn seed(@builtin(global_invocation_id) gid: vec3<u32>) {
    let i = gid.x;
    if (i >= params.width * params.height) { return; }
    var label = 0xffffffffu;
    if (liquid(i)) { label = i; }
    atomicStore(&labels[i], label);
    atomicStore(&maxima[i], 0u);
}

// Parents strictly decrease, so path halving cannot create a cycle.
fn root(start: u32) -> u32 {
    var current = start;
    for (var iteration = 0u; iteration < 64u; iteration += 1u) {
        let parent = atomicLoad(&labels[current]);
        if (parent == current) { return current; }
        let grandparent = atomicLoad(&labels[parent]);
        atomicMin(&labels[current], grandparent);
        current = parent;
    }
    return current;
}

fn join(a: u32, b: u32) {
    var left = root(a);
    var right = root(b);
    for (var iteration = 0u; iteration < 64u && left != right; iteration += 1u) {
        let high = max(left, right); let low = min(left, right);
        let old = atomicMin(&labels[high], low);
        if (old == high) { return; }
        left = root(old); right = root(low);
    }
}

@compute @workgroup_size(64)
fn unite(@builtin(global_invocation_id) gid: vec3<u32>) {
    let i = gid.x;
    if (i >= params.width * params.height || !liquid(i)) { return; }
    let x = i % params.width; let y = i / params.width;
    if (x + 1u < params.width && liquid(i + 1u) && cells[i].element == cells[i + 1u].element) { join(i, i + 1u); }
    if (y + 1u < params.height && liquid(i + params.width) && cells[i].element == cells[i + params.width].element) { join(i, i + params.width); }
}

@compute @workgroup_size(64)
fn accumulate(@builtin(global_invocation_id) gid: vec3<u32>) {
    let i = gid.x;
    if (i >= params.width * params.height || !liquid(i)) { return; }
    let c = cells[i];
    let height = f32(i / params.width) + clamp(c.mass / c.max_mass, 0.0, 1.0);
    atomicMax(&maxima[root(i)], bitcast<u32>(height));
}

@compute @workgroup_size(64)
fn finalize(@builtin(global_invocation_id) gid: vec3<u32>) {
    let i = gid.x;
    if (i >= params.width * params.height) { return; }
    if (!liquid(i)) { head[i] = -1.0; return; }
    head[i] = bitcast<f32>(atomicLoad(&maxima[root(i)]));
}
