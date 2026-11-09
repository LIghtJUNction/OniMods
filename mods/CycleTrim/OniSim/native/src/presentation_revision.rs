//! A publication identity for the five CPU texture images, independent of the
//! two alternating GameData addresses. Zero always means "upload normally".

#[repr(C)]
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct PresentationUpdate {
    pub version: u32,
    pub cell_count: u32,
    pub generation: u64,
    pub revision: u64,
    pub pointers: [usize; 5],
}

const _: [(); 64] = [(); std::mem::size_of::<PresentationUpdate>()];

impl PresentationUpdate {
    pub(crate) fn new(generation: u64, revision: u64, count: usize, pointers: [usize; 5]) -> Self {
        Self { version: 1, cell_count: count as u32, generation, revision, pointers }
    }
}

pub(crate) struct PresentationRevision(Option<u64>);

impl Default for PresentationRevision {
    fn default() -> Self { Self(Some(0)) }
}

impl PresentationRevision {
    pub(crate) fn generated(&mut self) {
        // Exhaustion permanently disables reuse instead of wrapping back to a
        // previously uploaded identity. This does not affect texture generation.
        self.0 = self.0.and_then(|value| value.checked_add(1));
    }

    pub(crate) fn current(&self) -> u64 { self.0.unwrap_or(0) }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn revision_never_reuses_an_identity_after_exhaustion() {
        let mut revision = PresentationRevision(Some(u64::MAX - 1));
        revision.generated();
        assert_eq!(revision.current(), u64::MAX);
        revision.generated();
        assert_eq!(revision.current(), 0);
        revision.generated();
        assert_eq!(revision.current(), 0);
    }
}
