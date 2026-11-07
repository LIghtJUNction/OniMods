// Each invocation owns one output cell. Inputs retain native padded CellSOA
// layout; u16 element pairs share a u32 so no CPU repacking pass is needed.
struct Shape { width: u32, height: u32, stride: u32, count: u32 }
@group(0) @binding(0) var<storage, read> flow: array<vec4<f32>>;
@group(0) @binding(1) var<storage, read> mass: array<f32>;
@group(0) @binding(2) var<storage, read> old_element: array<u32>;
@group(0) @binding(3) var<storage, read> element: array<u32>;
@group(0) @binding(4) var<storage, read_write> output: array<vec2<f32>>;
@group(0) @binding(5) var<uniform> shape: Shape;

// Exact round-to-nearest-even reciprocal for positive normal binary32 inputs.
// Vulkan FDiv permits an error bound, so plain 1.0 / mass did not match CPU
// words. A bounded integer long division avoids vendor-specific approximations.
fn reciprocal_rte(value: f32) -> f32 {
    let bits = bitcast<u32>(value);
    let exponent = (bits >> 23u) & 255u;
    let mantissa = (bits & 0x7fffffu) | 0x800000u;
    if exponent == 255u { return 0.0; } // positive infinity
    if mantissa == 0x800000u {
        if exponent == 254u { return bitcast<f32>(0x400000u); }
        return bitcast<f32>((254u - exponent) << 23u);
    }
    var remainder = 0x800000u;
    var quotient = 0u;
    let steps = min(24u, 276u - exponent);
    for (var i = 0u; i < steps; i += 1u) {
        remainder <<= 1u;
        quotient <<= 1u;
        if remainder >= mantissa {
            remainder -= mantissa;
            quotient |= 1u;
        }
    }
    let twice = remainder << 1u;
    if twice > mantissa || (twice == mantissa && (quotient & 1u) != 0u) {
        quotient += 1u;
    }
    if exponent >= 253u { return bitcast<f32>(quotient); }
    // Addition also handles a rounded significand carry into the next exponent.
    return bitcast<f32>(((253u - exponent) << 23u) + quotient - 0x800000u);
}

@compute @workgroup_size(32, 8)
fn main(@builtin(global_invocation_id) id: vec3<u32>) {
    if id.x >= shape.width || id.y >= shape.height { return; }
    let index = id.y * shape.width + id.x;
    let cell = (id.y + 1u) * shape.stride + id.x + 1u;
    let shift = (cell & 1u) * 16u;
    let old = (old_element[cell / 2u] >> shift) & 65535u;
    let current = (element[cell / 2u] >> shift) & 65535u;
    var scale = 1.0;
    if old != current {
        scale = 0.0;
    } else if mass[cell] > 1.0 {
        scale = reciprocal_rte(mass[cell]);
    }
    let f = flow[cell];
    output[index] = vec2<f32>((f.x - f.y) * scale, (f.w - f.z) * scale);
}
