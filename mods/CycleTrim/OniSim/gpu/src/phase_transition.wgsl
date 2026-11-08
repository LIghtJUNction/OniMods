// Select phase changes independently for a region. The host consumes these
// records in row order to preserve ore, falling-liquid and dirty-cell events.
struct Params { count: u32, element_count: u32, _pad0: u32, _pad1: u32 }
struct Cell {
    element: u32, mass: f32, temperature: f32, wall_element: u32,
    old_temperature: f32, old_mass: f32, wall_temperature: f32, wall_mass: f32,
    insulation: u32, enabled: u32, _pad0: u32, _pad1: u32,
}
struct Material {
    low_target: u32, high_target: u32,
    low_ore: u32, high_ore: u32,
    low_temperature: f32, high_temperature: f32,
    low_conversion: f32, high_conversion: f32,
    conductivity: f32, specific_heat: f32, phase: u32, _pad0: u32,
    surface_solid: f32, surface_liquid: f32, surface_gas: f32, _pad1: u32,
}
struct Decision {
    target_id: u32, ore: u32, conversion: f32, temperature: f32,
    wall_temperature: f32, wall_changed: u32, wall_transition: u32, cell_temperature: f32,
}

@group(0) @binding(0) var<storage, read> cells: array<Cell>;
@group(0) @binding(1) var<storage, read> materials: array<Material>;
@group(0) @binding(2) var<storage, read_write> decisions: array<Decision>;
@group(0) @binding(3) var<uniform> params: Params;

fn surface(material: Material, other_phase: u32) -> f32 {
    if (other_phase == 1u) { return material.surface_gas; }
    if (other_phase == 2u) { return material.surface_liquid; }
    if (other_phase == 3u) { return material.surface_solid; }
    return 1.0;
}
fn ordered_min(a: f32, b: f32) -> f32 { if (a < b) { return a; } return b; }
fn ordered_max(a: f32, b: f32) -> f32 { if (a > b) { return a; } return b; }

@compute @workgroup_size(64)
fn main(@builtin(global_invocation_id) gid: vec3<u32>) {
    let i = gid.x;
    if (i >= params.count) { return; }
    let c = cells[i];
    var temperature = c.temperature;
    var wall_temperature = c.wall_temperature;
    var wall_changed = 0u;
    var wall_transition = 0u;
    if (c.enabled != 0u && c.element < params.element_count && c.wall_element < params.element_count) {
        let m = materials[c.element];
        let wall = materials[c.wall_element];
        let insulation = f32(c.insulation);
        let insulation2 = insulation * insulation;
        let cell_k = insulation2 * bitcast<f32>(0x37810183u) * m.conductivity;
        var conductance = ordered_min(cell_k, wall.conductivity);
        if (insulation2 >= bitcast<f32>(0x477e00feu)) {
            conductance = exp((log(wall.conductivity) + log(cell_k)) * 0.5);
        }
        let contact = conductance * surface(m, wall.phase) * surface(wall, m.phase);
        let tc = c.old_temperature;
        let tw = c.wall_temperature;
        let cc = c.old_mass * m.specific_heat;
        let cw = c.wall_mass * wall.specific_heat;
        let equilibrium = (cw * tw + cc * tc) / (cw + cc);
        var next_cell = tc;
        if (tc <= tw) {
            let delta = tw - tc;
            let step = 0.01 * delta;
            var energy = ordered_min(delta * 0.2 * contact, step * cc);
            energy = ordered_min(energy, step * cw);
            next_cell = ordered_min(tc + energy / cc, equilibrium);
            wall_temperature = ordered_max(tw - energy / cw, equilibrium);
        } else {
            let delta = tc - tw;
            let step = 0.01 * delta;
            var energy = ordered_min(delta * 0.2 * contact, step * cw);
            energy = ordered_min(step * cc, energy);
            next_cell = ordered_max(tc - energy / cc, equilibrium);
            wall_temperature = ordered_min(tw + energy / cw, equilibrium);
        }
        temperature = next_cell + (c.temperature - tc);
        wall_changed = 1u;
        if (wall_temperature < wall.low_temperature - 3.0 || wall_temperature > wall.high_temperature + 3.0) { wall_transition = 1u; }
    }
    var result = Decision(65535u, 65535u, 0.0, temperature, wall_temperature, wall_changed, wall_transition, temperature);
    if (!(c.mass <= 0.0) && c.element < params.element_count) {
        let m = materials[c.element];
        if (!(m.low_temperature - 3.0 <= temperature || m.low_target == 65535u)) {
            var shifted = 0.0;
            if (temperature > -1.5) { shifted = temperature + 1.5; }
            result.target_id = m.low_target; result.ore = m.low_ore; result.conversion = m.low_conversion; result.temperature = shifted;
        } else if (!(temperature <= 3.0 + m.high_temperature || m.high_target == 65535u)) {
            var shifted = 0.0;
            if (temperature > 1.5) { shifted = temperature - 1.5; }
            result.target_id = m.high_target; result.ore = m.high_ore; result.conversion = m.high_conversion; result.temperature = shifted;
        }
    }
    decisions[i] = result;
}
