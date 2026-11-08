// Radiation propagation for a region plus its top/right halo. Column
// transmission is independent across x. Radiation emission is gathered in
// source row order so each cell keeps the CPU traversal's clamp position.
struct Params {
    width: u32, height: u32, nuclear: u32, _pad: u32,
    dose: f32, cosmic: f32, absorption_solid: f32, absorption_mass_scale: f32,
    mass_scale: f32, absorption_base: f32, _pad0: f32, _pad1: f32,
}
struct Cell {
    mass: f32, radiation: f32, absorption: f32, emission: f32,
    properties: u32, disease: u32, disease_count: i32, _pad: u32,
}
struct ResultCell { radiation: f32, transmission: f32 }
@group(0) @binding(0) var<storage, read> cells: array<Cell>;
@group(0) @binding(1) var<storage, read_write> transmission: array<f32>;
@group(0) @binding(2) var<storage, read_write> result: array<ResultCell>;
@group(0) @binding(3) var<uniform> params: Params;

fn omin(a: f32, b: f32) -> f32 { if (a < b) { return a; } return b; }
fn omax(a: f32, b: f32) -> f32 { if (a > b) { return a; } return b; }
fn attenuate(c: Cell) -> f32 {
    var amount = (c.mass / params.mass_scale) * params.absorption_mass_scale + params.absorption_base;
    if ((c.properties & 128u) != 0u) { amount = params.absorption_solid; }
    return omax(omin(c.absorption * amount, 1.0), 0.0);
}

@compute @workgroup_size(64)
fn columns(@builtin(global_invocation_id) gid: vec3<u32>) {
    let x = gid.x;
    if (x >= params.width - 1u) { return; }
    var incoming = 1.0;
    for (var yy = params.height; yy > 0u; yy -= 1u) {
        let y = yy - 1u;
        let i = y * params.width + x;
        // The original resets transmission for the two highest rows.
        if (y + 2u >= params.height) { incoming = 1.0; }
        let value = (1.0 - attenuate(cells[i])) * incoming;
        if (0.01 <= value) { transmission[i] = omax(value, 0.0); }
        else { transmission[i] = 0.0; }
        incoming = transmission[i];
    }
}

fn weight(dx: i32, dy: i32) -> f32 {
    let x = abs(dx); let y = abs(dy);
    if (x == 0 && y == 0) { return 1.0; }
    if ((x == 1 && y == 0) || (x == 0 && y == 1)) { return 0.75; }
    if (x == 1 && y == 1) { return 0.5; }
    if ((x == 2 && y == 0) || (x == 0 && y == 2)) { return 0.25; }
    if ((x == 2 && y == 1) || (x == 1 && y == 2)) { return 0.15; }
    return 0.1;
}

@compute @workgroup_size(64)
fn gather(@builtin(global_invocation_id) gid: vec3<u32>) {
    let i = gid.x;
    if (i >= params.width * params.height) { return; }
    let x = i % params.width; let y = i / params.width;
    let c = cells[i];
    let inside = x + 1u < params.width && y + 1u < params.height;
    var value = c.radiation;
    if (x + 1u < params.width) {
        let loss = value / params.dose;
        if (loss == 0.0) { if (value > 1.0) { value -= 1.0; } else { value = 0.0; } }
        else { value -= loss; }
    }
    // Emissions from earlier source cells arrive before this cell's clamp.
    for (var sy = max(i32(y) - 2, 0); sy <= min(i32(y) + 2, i32(params.height) - 2); sy += 1) {
        for (var sx = max(i32(x) - 2, 0); sx <= min(i32(x) + 2, i32(params.width) - 2); sx += 1) {
            let source_index = u32(sy) * params.width + u32(sx);
            if (!inside || source_index <= i) {
                let source = cells[source_index];
                if (source.emission > 0.0) { value = source.emission * (source.mass * 0.001) * weight(i32(x) - sx, i32(y) - sy) + value; }
            }
        }
    }
    if (inside) {
        if (c.disease == params.nuclear) { value += f32(c.disease_count) * 0.001; }
        if (transmission[i] > 0.0) { value += transmission[i] * (params.cosmic / params.dose); }
        if (!(value >= 0.0) || value <= 0.01) { value = 0.0; }
        else if (value > 9000000.0) { value = 9000000.0; }
        // Later emitters write after this cell's clamp in the original order.
        for (var sy = max(i32(y) - 2, 0); sy <= min(i32(y) + 2, i32(params.height) - 2); sy += 1) {
            for (var sx = max(i32(x) - 2, 0); sx <= min(i32(x) + 2, i32(params.width) - 2); sx += 1) {
                let source_index = u32(sy) * params.width + u32(sx);
                if (source_index > i) {
                    let source = cells[source_index];
                    if (source.emission > 0.0) { value = source.emission * (source.mass * 0.001) * weight(i32(x) - sx, i32(y) - sy) + value; }
                }
            }
        }
    }
    var t = 0.0;
    if (x + 1u < params.width) { t = transmission[i]; }
    result[i] = ResultCell(value, t);
}
