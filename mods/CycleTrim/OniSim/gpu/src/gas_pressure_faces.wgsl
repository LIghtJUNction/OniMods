// Conservative pressure transfer for isolated, single-species gas cells.
// The caller must mark cells with unsupported phases, disease, secondary
// components, or conflicting neighbouring species as blocked.
struct Params { width: u32, height: u32, vacuum: u32, phase: u32, direction: i32, head_enabled: u32, _pad1: u32, _pad2: u32 }
struct Cell {
    element: u32, phase: u32, blocked: u32, _pad: u32,
    mass: f32, temperature: f32, coefficient: f32, _pad1: f32,
    max_mass: f32, viscosity: f32, min_horizontal_flow: f32, min_vertical_flow: f32,
    gas_element: u32, gas_mass: f32, gas_heat: f32, phase_heat: f32,
}
struct Face { right: f32, up: f32, diagonal: f32 }
struct ResultCell {
    element: u32, changed: u32, mass: f32, temperature: f32,
    flow_left: f32, flow_right: f32, flow_up: f32, flow_down: f32,
    mixture_element: u32, mixture_mass: f32,
}
@group(0) @binding(0) var<storage, read> cells: array<Cell>;
@group(0) @binding(1) var<storage, read_write> faces: array<Face>;
@group(0) @binding(2) var<storage, read_write> result: array<ResultCell>;
@group(0) @binding(3) var<uniform> params: Params;
@group(0) @binding(4) var<storage, read> hydraulic_head: array<f32>;

// Positive transfer flows from a to b. At most one eighth of each donor's
// mass can cross an edge, so the sum across six edges cannot exceed its mass.
fn flux(a: Cell, b: Cell, vertical: bool, a_can_donate: bool, b_can_donate: bool, head: f32, upper_y: f32) -> f32 {
    if (a.blocked != 0u || b.blocked != 0u) { return 0.0; }
    if (params.phase == 2u) {
        if (a.phase > 2u || b.phase > 2u || (a.phase != 2u && b.phase != 2u)) { return 0.0; }
        if (a.phase == 2u && b.phase == 2u && a.element != b.element) { return 0.0; }
        let am = select(0.0, a.mass, a.phase == 2u);
        let bm = select(0.0, b.mass, b.phase == 2u);
        // a is below b. A downward transfer is negative, upward is positive.
        if (vertical) {
            if (b.phase == 2u && b_can_donate) {
                let capacity = max(b.max_mass, bm * 1.01);
                let down = min(max(capacity - am, 0.0) * 0.5, min(b.viscosity, bm * 0.25));
                if (down > 0.0 && (!(b.mass > b.min_vertical_flow) || down >= b.min_vertical_flow)) { return -down; }
            }
            if (a.phase == 2u && a_can_donate && b.phase == 2u) {
                let threshold = max(a.max_mass, bm * 1.01);
                let up = min(max(am - threshold, 0.0) * 0.5, min(a.viscosity, am * 0.25));
                if (up > 0.01) { return up; }
            }
            return 0.0;
        }
        var viscosity = a.viscosity;
        if (a.phase != 2u) { viscosity = b.viscosity; }
        let from_a = select(0.0, min(viscosity, am * 0.25), a_can_donate);
        let from_b = select(0.0, min(viscosity, bm * 0.25), b_can_donate);
        let amount = clamp((am - bm) * 0.25, -from_b, from_a);
        if (amount > 0.0 && amount < a.min_horizontal_flow) { return 0.0; }
        if (amount < 0.0 && -amount < b.min_horizontal_flow) { return 0.0; }
        return amount;
    }
    if ((a.phase != 1u && a.phase != 0u) || (b.phase != 1u && b.phase != 0u)) { return 0.0; }
    if (a.phase == 0u && b.phase == 0u) { return 0.0; }
    if (a.phase == 1u && b.phase == 1u && a.element != b.element) { return 0.0; }
    // Gas pressure is mass per available cell volume. Liquid volume is
    // stationary during this pass and limits both pressure and conductance.
    let volume_a = a.max_mass;
    let volume_b = b.max_mass;
    if (volume_a <= 0.0 || volume_b <= 0.0) { return 0.0; }
    var coefficient = a.coefficient;
    if (a.phase == 0u) { coefficient = b.coefficient; }
    let difference = a.mass / volume_a - b.mass / volume_b;
    return clamp(coefficient * difference * min(volume_a, volume_b), -0.125 * b.mass, 0.125 * a.mass);
}

@compute @workgroup_size(64)
fn compute_faces(@builtin(global_invocation_id) gid: vec3<u32>) {
    let i = gid.x;
    if (i >= params.width * params.height) { return; }
    let x = i % params.width; let y = i / params.width;
    var f = Face(0.0, 0.0, 0.0);
    let gas_row = y > 0u && y + 1u < params.height;
    let gas_horizontal = gas_row && x > 0u && x + 1u < params.width;
    let forward_vertical = x > 0u && x + 1u < params.width;
    let reverse_vertical = x > 1u && x < params.width;
    let gas_vertical = gas_row && select(reverse_vertical, forward_vertical, params.direction > 0);
    if (x + 1u < params.width && (params.phase != 1u || gas_horizontal)) {
        // Liquid source cells are inside the rectangle; halo cells may receive
        // mass, but their own updates belong to a neighbouring region.
        if (params.phase != 2u || gas_row) {
            let a_inside = x > 0u;
            let b_inside = x + 2u < params.width;
            f.right = flux(cells[i], cells[i + 1u], false, a_inside, b_inside, 0.0, 0.0);
        }
    }
    if (y + 1u < params.height && (params.phase != 1u || gas_vertical)) {
        if (params.phase != 2u || (x > 0u && x + 1u < params.width)) {
            let a_inside = y > 0u;
            let b_inside = y + 2u < params.height;
            var head = 0.0;
            if (params.head_enabled != 0u) { head = hydraulic_head[i]; }
            f.up = flux(cells[i], cells[i + params.width], true, a_inside, b_inside, head, f32(y + 1u));
        }
    }
    if (params.phase == 1u && gas_vertical) {
        var side = i - 1u;
        var diagonal = i + params.width - 1u;
        if (params.direction > 0) { side = i + 1u; diagonal = i + params.width + 1u; }
        if ((cells[side].phase | cells[i + params.width].phase) != 3u
            && cells[side].blocked == 0u && cells[i + params.width].blocked == 0u
            && cells[side].max_mass > 0.0 && cells[i + params.width].max_mass > 0.0) {
            f.diagonal = flux(cells[i], cells[diagonal], false, true, true, 0.0, 0.0);
        }
    }
    faces[i] = f;
}

@compute @workgroup_size(64)
fn apply_faces(@builtin(global_invocation_id) gid: vec3<u32>) {
    let i = gid.x;
    if (i >= params.width * params.height) { return; }
    let x = i % params.width; let y = i / params.width;
    let c = cells[i];
    var r = ResultCell(c.element, 0u, c.mass, c.temperature, 0.0, 0.0, 0.0, 0.0, params.vacuum, 0.0);
    if (c.blocked != 0u) { result[i] = r; return; }
    var left = 0.0; var right = 0.0; var down = 0.0; var up = 0.0;
    var diagonal_in = 0.0; var diagonal_out = 0.0;
    var below_diagonal = i; var above_diagonal = i;
    if (x > 0u) { left = faces[i - 1u].right; }
    if (x + 1u < params.width) { right = faces[i].right; }
    if (y > 0u) { down = faces[i - params.width].up; }
    if (y + 1u < params.height) { up = faces[i].up; }
    if (params.phase == 1u) {
        if (params.direction > 0) {
            if (x > 0u && y > 0u) { below_diagonal = i - params.width - 1u; diagonal_in = faces[below_diagonal].diagonal; }
            if (x + 1u < params.width && y + 1u < params.height) { above_diagonal = i + params.width + 1u; diagonal_out = faces[i].diagonal; }
        } else {
            if (x + 1u < params.width && y > 0u) { below_diagonal = i - params.width + 1u; diagonal_in = faces[below_diagonal].diagonal; }
            if (x > 0u && y + 1u < params.height) { above_diagonal = i + params.width - 1u; diagonal_out = faces[i].diagonal; }
        }
    }
    let liquid_mode = params.phase == 2u;
    var liquid_heat = c.phase_heat;
    var energy = (c.mass * liquid_heat + c.gas_mass * c.gas_heat) * c.temperature;
    var mass = c.mass;
    if (liquid_mode && c.phase != 2u) { mass = 0.0; }
    if (x > 0u) {
        mass += left;
        if (left > 0.0) { let source = cells[i - 1u]; liquid_heat = source.phase_heat; energy += left * source.temperature * liquid_heat; r.element = source.element; }
        else { energy += left * c.temperature * liquid_heat; }
    }
    if (x + 1u < params.width) {
        mass -= right;
        if (right < 0.0) { let source = cells[i + 1u]; liquid_heat = source.phase_heat; energy -= right * source.temperature * liquid_heat; r.element = source.element; }
        else { energy -= right * c.temperature * liquid_heat; }
    }
    if (y > 0u) {
        mass += down;
        if (down > 0.0) { let source = cells[i - params.width]; liquid_heat = source.phase_heat; energy += down * source.temperature * liquid_heat; r.element = source.element; }
        else { energy += down * c.temperature * liquid_heat; }
    }
    if (y + 1u < params.height) {
        mass -= up;
        if (up < 0.0) { let source = cells[i + params.width]; liquid_heat = source.phase_heat; energy -= up * source.temperature * liquid_heat; r.element = source.element; }
        else { energy -= up * c.temperature * liquid_heat; }
    }
    if (params.phase == 1u) {
        mass += diagonal_in;
        if (diagonal_in > 0.0) { let source = cells[below_diagonal]; liquid_heat = source.phase_heat; energy += diagonal_in * source.temperature * liquid_heat; r.element = source.element; }
        else { energy += diagonal_in * c.temperature * c.phase_heat; }
        mass -= diagonal_out;
        if (diagonal_out < 0.0) { let source = cells[above_diagonal]; liquid_heat = source.phase_heat; energy -= diagonal_out * source.temperature * liquid_heat; r.element = source.element; }
        else { energy -= diagonal_out * c.temperature * c.phase_heat; }
    }
    r.mass = max(mass, 0.0);
    if ((liquid_mode && c.phase == 1u) || c.gas_mass > 0.0) {
        let liquid_mass = r.mass;
        var gas_mass = c.gas_mass;
        var gas_element = c.gas_element;
        var gas_heat = c.gas_heat;
        if (liquid_mode && c.phase == 1u) { gas_mass = c.mass; gas_element = c.element; gas_heat = c.phase_heat; }
        let heat_capacity = gas_mass * gas_heat + liquid_mass * liquid_heat;
        if (heat_capacity > 0.0) { r.temperature = energy / heat_capacity; }
        if (liquid_mass > 0.0) {
            if (liquid_mass >= gas_mass) { r.mixture_element = gas_element; r.mixture_mass = gas_mass; }
            else { r.mixture_element = r.element; r.mixture_mass = liquid_mass; r.element = gas_element; r.mass = gas_mass; }
        } else { r.element = gas_element; r.mass = gas_mass; }
    } else if (r.mass > 0.0) {
        let heat_capacity = r.mass * liquid_heat;
        if (heat_capacity > 0.0) { r.temperature = energy / heat_capacity; }
    } else { r.element = params.vacuum; r.temperature = 0.0; }
    if (left != 0.0 || right != 0.0 || down != 0.0 || up != 0.0 || diagonal_in != 0.0 || diagonal_out != 0.0) { r.changed = 1u; }
    if (params.phase == 2u) {
        r.flow_left = max(-left, 0.0); r.flow_right = max(right, 0.0);
        r.flow_up = max(up, 0.0); r.flow_down = max(-down, 0.0);
    } else {
        if (params.direction > 0) { r.flow_left = -left; r.flow_right = right; }
        else { r.flow_left = -right; r.flow_right = left; }
        r.flow_up = up; r.flow_down = -down;
    }
    result[i] = r;
}
