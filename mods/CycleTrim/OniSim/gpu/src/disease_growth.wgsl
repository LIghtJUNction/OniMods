// Per-cell disease growth and cleanup. Inputs are snapshots for one region;
// no invocation writes another cell. Metadata is indexed by disease and element.
struct Params {
    count: u32,
    element_count: u32,
    disease_count: u32,
    radiation_enabled: u32,
}

struct Cell {
    element: u32,
    disease: u32,
    count: i32,
    infestation: u32,
    temperature: f32,
    mass: f32,
    radiation: f32,
    growth: f32,
}
struct ResultCell { disease: u32, count: i32, infestation: u32, growth: f32 }

struct Disease {
    temperature_range: vec4<f32>,
    temperature_half_lives: vec4<f32>,
    radiation_kill_rate: f32,
    _padding0: f32,
    _padding1: f32,
    _padding2: f32,
}

struct DiseaseElement {
    min_count_per_kg: f32,
    max_count_per_kg: f32,
    under_population_death_rate: f32,
    over_population_half_life: f32,
    population_half_life: f32,
    _padding0: f32,
    _padding1: f32,
    _padding2: f32,
}

@group(0) @binding(0) var<storage, read> cells_in: array<Cell>;
@group(0) @binding(1) var<storage, read> diseases: array<Disease>;
@group(0) @binding(2) var<storage, read> disease_elements: array<DiseaseElement>;
@group(0) @binding(3) var<storage, read_write> cells_out: array<ResultCell>;
@group(0) @binding(4) var<uniform> params: Params;

fn half_life(value: f32, points: vec4<f32>, lives: vec4<f32>) -> f32 {
    var lo: u32 = 0u;
    var hi: u32 = 0u;
    if (!(value > points.x)) {
        lo = 0u; hi = 0u;
    } else if (!(value > points.y)) {
        lo = 0u; hi = 1u;
    } else if (!(value > points.z)) {
        return bitcast<f32>(0x7f800000u);
    } else if (!(value > points.w)) {
        lo = 2u; hi = 3u;
    } else {
        lo = 3u; hi = 3u;
    }
    let first = lives[lo];
    let second = lives[hi];
    if (first > 3.402823466e+38 || second > 3.402823466e+38) {
        return bitcast<f32>(0x7f800000u);
    }
    let span = points[hi] - points[lo];
    if (!(span > 0.0)) { return first; }
    let fraction = (value - points[lo]) / span;
    return first * (1.0 - fraction) + fraction * second;
}

fn rate(life: f32) -> f32 {
    if (life == 0.0 || life != life) { return life; }
    if (life > 3.402823466e+38) { return 1.0; }
    return exp2(-0.2 / life);
}

// Match the native CVTT invalid/out-of-range result instead of saturating.
fn trunc_i32(value: f32) -> i32 {
    if (!(value >= -2147483648.0 && value < 2147483648.0)) {
        return bitcast<i32>(0x80000000u);
    }
    return i32(value);
}

@compute @workgroup_size(64)
fn main(@builtin(global_invocation_id) gid: vec3<u32>) {
    let i = gid.x;
    if (i >= params.count) { return; }
    var cell = cells_in[i];
    if (cell.disease != 255u && cell.disease < params.disease_count && cell.element < params.element_count) {
        let disease = diseases[cell.disease];
        let material = disease_elements[cell.disease * params.element_count + cell.element];
        let n = f32(cell.count);
        let temp_growth = rate(half_life(cell.temperature, disease.temperature_range, disease.temperature_half_lives));
        var change = (cell.growth - n) + temp_growth * n;
        let min_count = material.min_count_per_kg * cell.mass;
        let max_count = material.max_count_per_kg * cell.mass;
        if (!(max_count >= n) || !(n >= min_count)) {
            if (min_count > n) {
                change -= 0.2 * material.under_population_death_rate;
            } else {
                change += (rate(material.over_population_half_life) - 1.0) * n;
            }
        } else {
            change += (rate(material.population_half_life) - 1.0) * n;
        }
        if (params.radiation_enabled != 0u) { change -= cell.radiation * disease.radiation_kill_rate; }
        let whole = trunc_i32(change);
        cell.growth = change - f32(whole);
        cell.count = bitcast<i32>(bitcast<u32>(cell.count) + bitcast<u32>(whole));
    }
    if (cell.count < 1) {
        cell.disease = 255u;
        cell.count = 0;
        cell.infestation = 0u;
        cell.growth = 0.0;
    }
    cell.infestation = min(cell.infestation + 1u, 254u);
    cells_out[i] = ResultCell(cell.disease, cell.count, cell.infestation, cell.growth);
}
