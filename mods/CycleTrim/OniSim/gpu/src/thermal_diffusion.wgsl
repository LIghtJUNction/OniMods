// Conservative finite-volume thermal diffusion operator.
// The production gather kernel computes shared faces on demand. The original
// two-pass kernels remain as a GPU reference for parity tests and benchmarks.

struct ThermalParams {
    width: u32,
    height: u32,
    dt: f32,
    max_transfer_fraction: f32,
}

@group(0) @binding(0) var<storage, read> temperature: array<f32>;
@group(0) @binding(1) var<storage, read> mass: array<f32>;
@group(0) @binding(2) var<storage, read> conductivity: array<f32>;
@group(0) @binding(3) var<storage, read> specific_heat: array<f32>;
@group(0) @binding(4) var<storage, read_write> flux_u: array<f32>;
@group(0) @binding(5) var<storage, read_write> flux_v: array<f32>;
@group(0) @binding(6) var<storage, read_write> next_temperature: array<f32>;
@group(0) @binding(7) var<uniform> params: ThermalParams;
// A nonzero group identifies cells whose mutual faces are handled by another
// thermal pass. Different groups and zero still exchange here.
@group(0) @binding(8) var<storage, read> deferred_group: array<u32>;

fn compute_u_flux(gid: vec3<u32>) {
    let x = gid.x;
    let y = gid.y;
    let w = params.width;
    if (x >= w - 1u || y >= params.height) { return; }

    let cell_a = y * w + x;
    let cell_b = cell_a + 1u;
    let face_idx = y * (w - 1u) + x;

    if (deferred_group[cell_a] != 0u && deferred_group[cell_a] == deferred_group[cell_b]) {
        flux_u[face_idx] = 0.0;
        return;
    }

    let ma = mass[cell_a];
    let mb = mass[cell_b];
    if (ma <= 0.0 || mb <= 0.0) {
        flux_u[face_idx] = 0.0;
        return;
    }

    let ta = temperature[cell_a];
    let tb = temperature[cell_b];
    let delta_t = ta - tb; // Heat flows from A to B if Ta > Tb
    if (abs(delta_t) < 1e-6) {
        flux_u[face_idx] = 0.0;
        return;
    }

    let ca = ma * specific_heat[cell_a];
    let cb = mb * specific_heat[cell_b];
    let ka = conductivity[cell_a];
    let kb = conductivity[cell_b];
    let k_eff = 2.0 * (ka * kb) / (ka + kb + 1e-6);

    let max_heat_transfer = min(ca, cb) * abs(delta_t) * params.max_transfer_fraction;
    let heat_flux = clamp(k_eff * delta_t * params.dt, -max_heat_transfer, max_heat_transfer);
    flux_u[face_idx] = heat_flux;
}

fn compute_v_flux(gid: vec3<u32>) {
    let x = gid.x;
    let y = gid.y;
    let w = params.width;
    if (x >= w || y >= params.height - 1u) { return; }

    let cell_a = y * w + x;
    let cell_b = cell_a + w;
    let face_idx = y * w + x;

    if (deferred_group[cell_a] != 0u && deferred_group[cell_a] == deferred_group[cell_b]) {
        flux_v[face_idx] = 0.0;
        return;
    }

    let ma = mass[cell_a];
    let mb = mass[cell_b];
    if (ma <= 0.0 || mb <= 0.0) {
        flux_v[face_idx] = 0.0;
        return;
    }

    let ta = temperature[cell_a];
    let tb = temperature[cell_b];
    let delta_t = ta - tb; // Heat flows from A to B if Ta > Tb
    if (abs(delta_t) < 1e-6) {
        flux_v[face_idx] = 0.0;
        return;
    }

    let ca = ma * specific_heat[cell_a];
    let cb = mb * specific_heat[cell_b];
    let ka = conductivity[cell_a];
    let kb = conductivity[cell_b];
    let k_eff = 2.0 * (ka * kb) / (ka + kb + 1e-6);

    let max_heat_transfer = min(ca, cb) * abs(delta_t) * params.max_transfer_fraction;
    let heat_flux = clamp(k_eff * delta_t * params.dt, -max_heat_transfer, max_heat_transfer);
    flux_v[face_idx] = heat_flux;
}

@compute @workgroup_size(8, 8, 1)
fn compute_flux(@builtin(global_invocation_id) gid: vec3<u32>) {
    compute_u_flux(gid);
    compute_v_flux(gid);
}

@compute @workgroup_size(8, 8, 1)
fn apply_thermal_flux(@builtin(global_invocation_id) gid: vec3<u32>) {
    let x = gid.x;
    let y = gid.y;
    let w = params.width;
    let h = params.height;
    if (x >= w || y >= h) { return; }

    let cell = y * w + x;
    let m = mass[cell];
    if (m <= 0.0) {
        next_temperature[cell] = temperature[cell];
        return;
    }

    let cap = m * specific_heat[cell];
    if (cap <= 1e-6) {
        next_temperature[cell] = temperature[cell];
        return;
    }

    var total_energy_delta = 0.0;

    // Left face
    if (x > 0u) {
        let face_l = y * (w - 1u) + (x - 1u);
        total_energy_delta += flux_u[face_l];
    }
    // Right face
    if (x + 1u < w) {
        let face_r = y * (w - 1u) + x;
        total_energy_delta -= flux_u[face_r];
    }
    // Bottom/up face
    if (y > 0u) {
        let face_d = (y - 1u) * w + x;
        total_energy_delta += flux_v[face_d];
    }
    // Top/down face
    if (y + 1u < h) {
        let face_u = y * w + x;
        total_energy_delta -= flux_v[face_u];
    }

    let new_temp = temperature[cell] + (total_energy_delta / cap);
    next_temperature[cell] = clamp(new_temp, 1.0, 10000.0);
}

// Compute each shared face in the same orientation for both adjacent cells.
// This trades duplicate arithmetic for eliminating the face buffers and an
// entire dispatch, while retaining the previous accumulation order.
fn face_flux(a: u32, b: u32) -> f32 {
    if (deferred_group[a] != 0u && deferred_group[a] == deferred_group[b]) { return 0.0; }
    let ma = mass[a];
    let mb = mass[b];
    if (ma <= 0.0 || mb <= 0.0) { return 0.0; }
    let delta_t = temperature[a] - temperature[b];
    if (abs(delta_t) < 1e-6) { return 0.0; }
    let ca = ma * specific_heat[a];
    let cb = mb * specific_heat[b];
    let ka = conductivity[a];
    let kb = conductivity[b];
    let k_eff = 2.0 * (ka * kb) / (ka + kb + 1e-6);
    let limit = min(ca, cb) * abs(delta_t) * params.max_transfer_fraction;
    return clamp(k_eff * delta_t * params.dt, -limit, limit);
}

@compute @workgroup_size(8, 8, 1)
fn gather_thermal_flux(@builtin(global_invocation_id) gid: vec3<u32>) {
    let x = gid.x;
    let y = gid.y;
    let w = params.width;
    let h = params.height;
    if (x >= w || y >= h) { return; }
    let cell = y * w + x;
    let m = mass[cell];
    let cap = m * specific_heat[cell];
    if (m <= 0.0 || cap <= 1e-6) {
        next_temperature[cell] = temperature[cell];
        return;
    }
    var energy = 0.0;
    if (x > 0u) { energy += face_flux(cell - 1u, cell); }
    if (x + 1u < w) { energy -= face_flux(cell, cell + 1u); }
    if (y > 0u) { energy += face_flux(cell - w, cell); }
    if (y + 1u < h) { energy -= face_flux(cell, cell + w); }
    next_temperature[cell] = clamp(temperature[cell] + energy / cap, 1.0, 10000.0);
}
