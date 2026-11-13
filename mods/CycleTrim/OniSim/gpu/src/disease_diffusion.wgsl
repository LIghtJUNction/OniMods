// Gather the four disease edges touching each destination. Eligibility and edge
// deltas use the immutable old snapshot; mutations to the active cell follow
// the same bottom, left, right, top order as the row-major CPU traversal.
struct Params {
    stride: u32,
    height: u32,
    min_x: u32,
    max_x: u32,
    min_y: u32,
    max_y: u32,
    element_count: u32,
    disease_count: u32,
}
struct SnapshotCell {
    element: u32,
    phase: u32,
    disease: u32,
    infestation: u32,
    count: i32,
    _pad0: u32,
    _pad1: u32,
    _pad2: u32,
}
struct ActiveCell {
    disease: u32,
    count: i32,
    infestation: u32,
    growth: f32,
}
struct Disease { strength: f32 }
struct DiseaseElement {
    diffusion_scale: f32,
    min_diffusion_count: i32,
    min_infestation_tick: u32,
    _pad: u32,
}
struct Delta { disease: u32, amount: i32 }

@group(0) @binding(0) var<storage, read> old_cells: array<SnapshotCell>;
@group(0) @binding(1) var<storage, read> active_cells: array<ActiveCell>;
@group(0) @binding(2) var<storage, read> diseases: array<Disease>;
@group(0) @binding(3) var<storage, read> disease_elements: array<DiseaseElement>;
@group(0) @binding(4) var<storage, read_write> results: array<ActiveCell>;
@group(0) @binding(5) var<uniform> params: Params;

fn wrap_add(a: i32, b: i32) -> i32 { return bitcast<i32>(bitcast<u32>(a) + bitcast<u32>(b)); }
fn wrap_sub(a: i32, b: i32) -> i32 { return bitcast<i32>(bitcast<u32>(a) - bitcast<u32>(b)); }
fn negate(a: i32) -> i32 { return bitcast<i32>(0u - bitcast<u32>(a)); }
fn cvtt(value: f32) -> i32 {
    if (!(value >= -2147483648.0 && value < 2147483648.0)) { return bitcast<i32>(0x80000000u); }
    return i32(value);
}
fn scale(cell: SnapshotCell) -> f32 {
    if (cell.disease >= params.disease_count || cell.element >= params.element_count) { return 0.0; }
    let info = disease_elements[cell.disease * params.element_count + cell.element];
    if (cell.count < info.min_diffusion_count || cell.infestation < info.min_infestation_tick) { return 0.0; }
    return info.diffusion_scale;
}

// `for_first` selects which side's change to return.
fn edge(first: SnapshotCell, second: SnapshotCell, for_first: bool) -> Delta {
    if (first.phase != second.phase || (first.disease == 255u && second.disease == 255u)) {
        return Delta(255u, 0);
    }
    if (first.disease == second.disease) {
        let difference = wrap_sub(second.count, first.count);
        var factor = scale(first);
        if (difference > 0) { factor = scale(second); }
        let amount = cvtt((f32(difference) * 0.125) * factor);
        if (for_first) { return Delta(first.disease, amount); }
        return Delta(second.disease, negate(amount));
    }
    var first_stronger = false;
    if (first.disease != 255u) {
        if (second.disease == 255u) { first_stronger = true; }
        else if (first.disease < params.disease_count && second.disease < params.disease_count) {
            first_stronger = f32(first.count) * diseases[first.disease].strength > f32(second.count) * diseases[second.disease].strength;
        }
    }
    var donor = second;
    if (first_stronger) { donor = first; }
    let amount = cvtt((f32(donor.count) * 0.125) * scale(donor));
    if (for_first == first_stronger) { return Delta(donor.disease, negate(amount)); }
    return Delta(donor.disease, amount);
}

fn add_disease(current: ActiveCell, delta: Delta) -> ActiveCell {
    if (delta.amount == 0) { return current; }
    var cell = current;
    let old = cell.disease;
    if (old == delta.disease) {
        cell.count = wrap_add(cell.count, delta.amount);
    } else if (old == 255u) {
        cell.disease = delta.disease; cell.count = delta.amount;
    } else if (delta.disease != 255u && old < params.disease_count && delta.disease < params.disease_count) {
        let old_strength = diseases[old].strength * f32(cell.count);
        let new_strength = diseases[delta.disease].strength * f32(delta.amount);
        if (old_strength > new_strength) {
            let remaining = cvtt(f32(delta.amount) - (old_strength / new_strength) * f32(cell.count));
            if (remaining < 0) { cell.count = negate(remaining); }
            else { cell.disease = delta.disease; cell.count = remaining; }
        } else if (cell.count < 0) {
            cell.disease = delta.disease; cell.count = negate(cell.count);
        }
    }
    if (cell.count <= 0) {
        cell.disease = 255u; cell.count = 0; cell.infestation = 0u; cell.growth = 0.0;
    } else if (cell.disease != old) { cell.infestation = 0u; }
    return cell;
}

@compute @workgroup_size(64)
fn main(@builtin(global_invocation_id) gid: vec3<u32>) {
    let x = params.min_x + gid.x % (params.max_x - params.min_x + 1u);
    let y = params.min_y + gid.x / (params.max_x - params.min_x + 1u);
    if (x > params.max_x || y > params.max_y || x >= params.stride || y >= params.height) { return; }
    let i = y * params.stride + x;
    var cell = active_cells[i];
    let here = old_cells[i];
    if (y > params.min_y && x < params.max_x) { cell = add_disease(cell, edge(old_cells[i - params.stride], here, false)); }
    if (x > params.min_x && y < params.max_y) { cell = add_disease(cell, edge(old_cells[i - 1u], here, false)); }
    if (x < params.max_x && y < params.max_y) {
        cell = add_disease(cell, edge(here, old_cells[i + 1u], true));
        cell = add_disease(cell, edge(here, old_cells[i + params.stride], true));
    }
    results[i] = cell;
}
