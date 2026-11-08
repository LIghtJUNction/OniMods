// One invocation owns a whole column. This preserves the existing ordered
// bubble and droplet passes without concurrent writes to the same cell.
struct Params { width: u32, height: u32, stride: u32, vacuum: u32 }
struct Cell {
    element: u32, mixture_element: u32, properties: u32, flags: u32,
    mass: f32, mixture_mass: f32, temperature: f32, _pad: u32,
}
struct Material { phase: u32, heat: f32 }
struct ResultCell { element: u32, mixture_element: u32, flags: u32, mass: f32, mixture_mass: f32, temperature: f32 }
@group(0) @binding(0) var<storage, read_write> cells: array<Cell>;
@group(0) @binding(1) var<storage, read> materials: array<Material>;
@group(0) @binding(2) var<uniform> params: Params;
@group(0) @binding(3) var<storage, read_write> result: array<ResultCell>;

@compute @workgroup_size(64)
fn main(@builtin(global_invocation_id) gid: vec3<u32>) {
    let x = gid.x + 1u;
    if (x > params.width) { return; }
    for (var y = params.height; y > 0u; y -= 1u) {
        let i = y * params.stride + x;
        var source = cells[i];
        let primary_phase = materials[source.element].phase;
        let secondary_phase = materials[source.mixture_element].phase;
        if (y > 1u && source.mixture_mass > 0.0 && source.mixture_mass <= 3.402823466e+38
            && primary_phase == 1u && secondary_phase == 2u) {
            source.flags |= 2u; cells[i] = source; continue;
        }
        if (y == params.height || source.mixture_mass <= 0.0 || primary_phase != 2u || secondary_phase != 1u) { continue; }
        let j = i + params.stride;
        var dest = cells[j];
        if ((dest.properties & 1u) != 0u) { continue; }
        let dest_phase = materials[dest.element].phase;
        let amount = source.mixture_mass;
        if (dest.element == params.vacuum) {
            dest.element = source.mixture_element; dest.mass = amount; dest.temperature = source.temperature;
        } else if (dest.element == source.mixture_element && dest_phase == 1u) {
            dest.temperature = (dest.temperature * dest.mass + source.temperature * amount) / (dest.mass + amount);
            dest.mass += amount;
        } else if ((dest_phase == 1u || dest_phase == 2u)
            && (dest.mixture_mass <= 0.0 || dest.mixture_element == source.mixture_element)) {
            dest.mixture_element = source.mixture_element; dest.mixture_mass += amount;
        } else { continue; }
        source.mixture_element = params.vacuum; source.mixture_mass = 0.0;
        source.flags |= 1u; dest.flags |= 1u; cells[i] = source; cells[j] = dest;
    }
    for (var y = 2u; y <= params.height; y += 1u) {
        let i = y * params.stride + x;
        var source = cells[i];
        if ((source.flags & 2u) == 0u || !(source.mixture_mass > 0.0)
            || !(source.mixture_mass <= 3.402823466e+38)
            || materials[source.element].phase != 1u || materials[source.mixture_element].phase != 2u) { continue; }
        let j = i - params.stride;
        var dest = cells[j];
        if ((dest.properties & 1u) != 0u) { continue; }
        let amount = source.mixture_mass;
        let dest_phase = materials[dest.element].phase;
        if (dest.element == params.vacuum && dest.mixture_mass <= 0.0) {
            dest.element = source.mixture_element; dest.mass = amount; dest.temperature = source.temperature;
        } else if (dest.element == source.mixture_element || (dest_phase == 1u
            && (dest.mixture_mass <= 0.0 || dest.mixture_element == source.mixture_element))) {
            let old_capacity = dest.mass * max(materials[dest.element].heat, 0.0)
                + max(dest.mixture_mass, 0.0) * max(materials[dest.mixture_element].heat, 0.0);
            let incoming_capacity = amount * max(materials[source.mixture_element].heat, 0.0);
            if (old_capacity + incoming_capacity > 0.0) {
                dest.temperature = (dest.temperature * old_capacity + source.temperature * incoming_capacity) / (old_capacity + incoming_capacity);
            }
            if (dest.element == source.mixture_element) { dest.mass += amount; }
            else { dest.mixture_element = source.mixture_element; dest.mixture_mass += amount; }
        } else { continue; }
        source.mixture_element = params.vacuum; source.mixture_mass = 0.0;
        source.flags |= 1u; dest.flags |= 1u; cells[i] = source; cells[j] = dest;
    }
    for (var y = 1u; y <= params.height; y += 1u) {
        let c = cells[y * params.stride + x];
        result[(y - 1u) * params.width + x - 1u] = ResultCell(c.element, c.mixture_element, c.flags, c.mass, c.mixture_mass, c.temperature);
    }
}
