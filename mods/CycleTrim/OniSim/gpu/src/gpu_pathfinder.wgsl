struct Params {
    width: u32,
    height: u32,
    stride: u32,
    chore_count: u32,
    step_size: u32,
    origin_cell: u32,
    padding0: u32,
    padding1: u32,
};

struct ChoreCandidate {
    chore_id: u32,
    target_cell: u32,
    priority: i32,
    cost: i32,
};

@group(0) @binding(0) var<storage, read> nav_grid: array<u32>;
@group(0) @binding(1) var<storage, read_write> seed_field: array<i32>;
@group(0) @binding(2) var<storage, read_write> next_seed_field: array<i32>;
@group(0) @binding(3) var<storage, read_write> chores: array<ChoreCandidate>;
@group(0) @binding(4) var<uniform> params: Params;

@compute @workgroup_size(64)
fn init_cost(@builtin(global_invocation_id) gid: vec3<u32>) {
    let cell = gid.x;
    if (cell >= params.width * params.height) { return; }
    if (cell == params.origin_cell) {
        seed_field[cell] = i32(cell);
        next_seed_field[cell] = i32(cell);
    } else {
        seed_field[cell] = -1;
        next_seed_field[cell] = -1;
    }
}

@compute @workgroup_size(64)
fn step_cost(@builtin(global_invocation_id) gid: vec3<u32>) {
    let cell = gid.x;
    let total = params.width * params.height;
    if (cell >= total) { return; }
    let traversable = nav_grid[cell];
    if (traversable == 0u) {
        next_seed_field[cell] = -1;
        return;
    }

    let x = i32(cell % params.width);
    let y = i32(cell / params.width);
    let step = i32(params.step_size);

    var best_seed = seed_field[cell];
    var best_dist = 1000000;
    if (best_seed >= 0) {
        let sx = best_seed % i32(params.width);
        let sy = best_seed / i32(params.width);
        best_dist = abs(x - sx) + abs(y - sy);
    }

    for (var dy = -1; dy <= 1; dy += 1) {
        for (var dx = -1; dx <= 1; dx += 1) {
            if (dx == 0 && dy == 0) { continue; }
            let nx = x + dx * step;
            let ny = y + dy * step;
            if (nx >= 0 && nx < i32(params.width) && ny >= 0 && ny < i32(params.height)) {
                let ncell = u32(ny * i32(params.width) + nx);
                let nseed = seed_field[ncell];
                if (nseed >= 0) {
                    let sx = nseed % i32(params.width);
                    let sy = nseed / i32(params.width);
                    let dist = abs(x - sx) + abs(y - sy);
                    if (dist < best_dist) {
                        best_dist = dist;
                        best_seed = nseed;
                    }
                }
            }
        }
    }

    next_seed_field[cell] = best_seed;
}

@compute @workgroup_size(64)
fn evaluate_chores(@builtin(global_invocation_id) gid: vec3<u32>) {
    let idx = gid.x;
    if (idx >= params.chore_count) { return; }
    let cell = chores[idx].target_cell;
    let total = params.width * params.height;
    if (cell >= total) {
        chores[idx].cost = -1;
        return;
    }
    let seed = seed_field[cell];
    if (seed < 0) {
        chores[idx].cost = -1;
    } else {
        let x = i32(cell % params.width);
        let y = i32(cell / params.width);
        let sx = seed % i32(params.width);
        let sy = seed / i32(params.width);
        chores[idx].cost = abs(x - sx) + abs(y - sy);
    }
}
