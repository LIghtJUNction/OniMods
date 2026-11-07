// Conservative multi-component finite-volume transport prototype.
// Mass and sensible enthalpy use the same face flux, preserving each species'
// mass and energy under the caller's CFL bound. Layout is NHWC.
struct Params {
    width: u32,
    height: u32,
    channels: u32,
    dt: f32,
}
@group(0) @binding(0) var<storage, read> mass: array<f32>;
@group(0) @binding(1) var<storage, read> velocity_x: array<f32>;
@group(0) @binding(2) var<storage, read> velocity_y: array<f32>;
@group(0) @binding(3) var<storage, read> solid: array<u32>;
@group(0) @binding(4) var<storage, read> drift_y: array<f32>;
@group(0) @binding(5) var<storage, read_write> next_mass: array<f32>;
@group(0) @binding(6) var<uniform> params: Params;
@group(0) @binding(7) var<storage, read> enthalpy: array<f32>;
@group(0) @binding(8) var<storage, read_write> next_enthalpy: array<f32>;

fn face_flux(a: f32, b: f32, speed: f32, open_face: bool) -> f32 {
    let donor = select(b, a, speed >= 0.0);
    return select(0.0, speed * donor, open_face);
}

@compute @workgroup_size(8, 8, 1)
fn main(@builtin(global_invocation_id) gid: vec3<u32>) {
    if gid.x >= params.width || gid.y >= params.height { return; }
    let x = gid.x;
    let y = gid.y;
    let w = params.width;
    let h = params.height;
    let cell = y * w + x;
    let is_solid = solid[cell] != 0u;

    let left_cell = y * w + select(x, x - 1u, x > 0u);
    let right_cell = y * w + select(x, x + 1u, x + 1u < w);
    let up_cell = select(y, y - 1u, y > 0u) * w + x;
    let down_cell = select(y, y + 1u, y + 1u < h) * w + x;

    let has_left = x > 0u && solid[left_cell] == 0u && !is_solid;
    let has_right = x + 1u < w && solid[right_cell] == 0u && !is_solid;
    let has_up = y > 0u && solid[up_cell] == 0u && !is_solid;
    let has_down = y + 1u < h && solid[down_cell] == 0u && !is_solid;

    for (var c = 0u; c < params.channels; c += 1u) {
        let slot = cell * params.channels + c;
        let m = mass[slot];
        let ml = mass[left_cell * params.channels + c];
        let mr = mass[right_cell * params.channels + c];
        let mu = mass[up_cell * params.channels + c];
        let md = mass[down_cell * params.channels + c];

        let e = enthalpy[slot];
        let el = enthalpy[left_cell * params.channels + c];
        let er = enthalpy[right_cell * params.channels + c];
        let eu = enthalpy[up_cell * params.channels + c];
        let ed = enthalpy[down_cell * params.channels + c];

        let vx_r = 0.5 * (velocity_x[cell] + velocity_x[right_cell]);
        let vx_l = 0.5 * (velocity_x[left_cell] + velocity_x[cell]);
        let vy_d = 0.5 * (velocity_y[cell] + velocity_y[down_cell]) + drift_y[c];
        let vy_u = 0.5 * (velocity_y[up_cell] + velocity_y[cell]) + drift_y[c];

        let fr = face_flux(m, mr, vx_r, has_right);
        let fl = face_flux(ml, m, vx_l, has_left);
        let fd = face_flux(m, md, vy_d, has_down);
        let fu = face_flux(mu, m, vy_u, has_up);
        let mass_next = m - params.dt * ((fr - fl) + (fd - fu));

        let er_flux = face_flux(e, er, vx_r, has_right);
        let el_flux = face_flux(el, e, vx_l, has_left);
        let ed_flux = face_flux(e, ed, vy_d, has_down);
        let eu_flux = face_flux(eu, e, vy_u, has_up);
        let enthalpy_next = e - params.dt * ((er_flux - el_flux) + (ed_flux - eu_flux));

        next_mass[slot] = select(mass_next, 0.0, is_solid);
        next_enthalpy[slot] = select(enthalpy_next, 0.0, is_solid);
    }
}
