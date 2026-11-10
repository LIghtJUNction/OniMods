//! Readers pin an immutable publication, not the mutable simulation session.
//! The worker can reuse an unleased buffer; a slow reader triggers copy-on-write.
use std::sync::{Arc, RwLock};
use std::sync::atomic::{AtomicU64, Ordering};
use crate::multicomponent_publication::{MulticomponentBuffers, OniSimMulticomponentUpdate};
use crate::status::{Error, Result, Status};

pub(crate) struct Publication {
    pub(crate) descriptor: OniSimMulticomponentUpdate,
    _buffers: Arc<MulticomponentBuffers>,
}

static CURRENT: RwLock<Option<Arc<Publication>>> = RwLock::new(None);
static NEXT: AtomicU64 = AtomicU64::new(1);
static REVISION: AtomicU64 = AtomicU64::new(0);

pub(crate) fn publish(buffers: &Arc<MulticomponentBuffers>) -> Result<()> {
    let revision = NEXT.try_update(Ordering::Relaxed, Ordering::Relaxed, |value|
        value.checked_add(1)).map_err(|_| Error::new(Status::ResourceLimit, "publication revision exhausted"))?;
    let publication = Arc::new(Publication {
        descriptor: buffers.descriptor(revision)?,
        _buffers: Arc::clone(buffers),
    });
    let mut current = CURRENT.write().map_err(|_| poisoned())?;
    *current = Some(publication);
    REVISION.store(revision, Ordering::Release);
    Ok(())
}

pub(crate) fn acquire() -> Result<Arc<Publication>> {
    CURRENT.read().map_err(|_| poisoned())?.as_ref().cloned().ok_or_else(||
        Error::new(Status::InvalidState, "Start is required before reading multicomponent publication"))
}

pub(crate) fn revision() -> u64 { REVISION.load(Ordering::Acquire) }

pub(crate) fn clear() -> Result<()> {
    let mut current = CURRENT.write().map_err(|_| poisoned())?;
    *current = None;
    REVISION.store(0, Ordering::Release);
    Ok(())
}

fn poisoned() -> Error {
    Error::new(Status::InternalFailure, "publication registry lock poisoned")
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn leases_survive_buffer_reuse_without_blocking_other_readers() {
        // Isolated ownership test: no global registry shared with native session tests.
        let mut buffers = Arc::new(MulticomponentBuffers::new(4).unwrap());
        let published = Arc::new(Publication {
            descriptor: buffers.descriptor(1).unwrap(), _buffers: Arc::clone(&buffers),
        });
        let old = published.descriptor.cells;
        let reader = Arc::clone(&published);
        let second = std::thread::spawn(move || {
            assert_eq!(reader.descriptor.generation, 1);
            reader
        }).join().unwrap();
        let new = Arc::make_mut(&mut buffers).descriptor(2).unwrap();
        assert_ne!(old, new.cells, "leased memory cannot be overwritten by the worker");
        assert_eq!(second.descriptor.cells, old);
        drop(published);
        assert_eq!(second.descriptor.generation, 1, "reader outlives publisher");
        let reusable = new.cells;
        assert_eq!(Arc::make_mut(&mut buffers).descriptor(3).unwrap().cells, reusable);
    }
}
