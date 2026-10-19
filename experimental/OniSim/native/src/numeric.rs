// SSE scalar MINSS/MAXSS select the second operand for unordered/equal values.
// Rust f32::min/max choose differently for NaNs and some signed-zero cases.
pub(crate) fn smin(first: f32, second: f32) -> f32 {
    if first < second {
        first
    } else {
        second
    }
}

pub(crate) fn smax(first: f32, second: f32) -> f32 {
    if first > second {
        first
    } else {
        second
    }
}

// CVTTSS2SI returns integer-indefinite, rather than Rust's saturating conversion.
pub(crate) fn cvtt_f32_i32(value: f32) -> i32 {
    if !value.is_finite() || !(-2_147_483_648.0..2_147_483_648.0).contains(&value) {
        i32::MIN
    } else {
        value as i32
    }
}
