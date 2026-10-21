//! Exact obstacle-free four-neighbor coverage, emitted as contiguous row spans.
//!
//! A search that accepts depths strictly below `max_depth` visits the clipped
//! Manhattan ball of radius `max_depth - 1`. Zero coordinates are excluded by
//! the native search. Its u16 coordinates also exclude any wrap through zero.
//! This iterator needs no grid-sized marks, queue, or result allocation.

use std::ops::Range;

/// One half-open linear-index range per nonempty row of the search diamond.
/// The owning world's allocated dimensions establish that index products fit.
#[derive(Clone)]
pub(crate) struct DiamondRows {
    stride: usize,
    center_x: i32,
    center_y: i32,
    radius: i32,
    upper_x: i32,
    next_y: i32,
    end_y: i32,
}

impl DiamondRows {
    pub(crate) fn y_bounds(&self) -> Range<usize> {
        self.next_y as usize..self.end_y as usize
    }

    pub(crate) fn span(&self, y: usize) -> Range<usize> {
        let reach = self.radius - (y as i32 - self.center_y).abs();
        let first_x = (self.center_x - reach).max(1) as usize;
        let end_x = (self.center_x + reach + 1).min(self.upper_x) as usize;
        let row = y * self.stride;
        row + first_x..row + end_x
    }
}

pub(crate) fn rows(
    stride: usize,
    padded_height: usize,
    start_x: i32,
    start_y: i32,
    max_depth: u8,
) -> DiamondRows {
    // Preserve the original cast before testing the start, including negative
    // and greater-than-u16 inputs. Valid nodes form a rectangle, not a torus.
    let center_x = i32::from(start_x as u16);
    let center_y = i32::from(start_y as u16);
    let upper_x = stride.min(65_536) as i32;
    let upper_y = padded_height.min(65_536) as i32;
    let radius = i32::from(max_depth) - 1;
    let valid =
        max_depth != 0 && center_x > 0 && center_y > 0 && center_x < upper_x && center_y < upper_y;
    let (next_y, end_y) = if valid {
        (
            (center_y - radius).max(1),
            (center_y + radius + 1).min(upper_y),
        )
    } else {
        (0, 0)
    };
    DiamondRows {
        stride,
        center_x,
        center_y,
        radius,
        upper_x,
        next_y,
        end_y,
    }
}

impl Iterator for DiamondRows {
    type Item = Range<usize>;

    #[inline]
    fn next(&mut self) -> Option<Self::Item> {
        if self.next_y >= self.end_y {
            return None;
        }
        let y = self.next_y;
        self.next_y += 1;
        Some(self.span(y as usize))
    }
}
