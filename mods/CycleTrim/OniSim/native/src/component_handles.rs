//! Dense component storage with the official CompactedVector handle encoding.
use crate::status::{Error, Result, Status};

#[derive(Clone)]
pub(crate) struct ComponentHandles<T> {
    pub(crate) data: Vec<T>,
    handles: Vec<i32>,
    slots: Vec<usize>,
    generations: Vec<u8>,
    free: Vec<i32>,
}

impl<T> Default for ComponentHandles<T> {
    fn default() -> Self {
        Self {
            data: Vec::new(),
            handles: Vec::new(),
            slots: Vec::new(),
            generations: Vec::new(),
            free: Vec::new(),
        }
    }
}

impl<T> ComponentHandles<T> {
    // Register 0x5c970/0x62160, Unregister 0x5c760/0x62020. The native
    // generation wraps after 256 removals; handles are not permanent IDs.
    pub(crate) fn insert(&mut self, value: T) -> Result<i32> {
        let dense = self.data.len();
        self.data.try_reserve(1).map_err(allocation)?;
        self.handles.try_reserve(1).map_err(allocation)?;
        let handle = if let Some(handle) = self.free.pop() {
            self.slots[Self::slot(handle)] = dense;
            handle
        } else {
            // Native accessors reject bits 20..23 even though they extract 24
            // index bits. Bound the safe replacement to its accepted slots.
            if self.slots.len() >= 0x10_0000 {
                return Err(Error::new(
                    Status::ResourceLimit,
                    "component handle slots exhausted",
                ));
            }
            self.slots.try_reserve(1).map_err(allocation)?;
            self.generations.try_reserve(1).map_err(allocation)?;
            let handle = self.slots.len() as i32;
            self.slots.push(dense);
            self.generations.push(0);
            handle
        };
        self.handles.push(handle);
        self.data.push(value);
        Ok(handle)
    }

    pub(crate) fn slot(handle: i32) -> usize {
        (handle as u32 & 0x00ff_ffff) as usize
    }
    pub(crate) fn slot_count(&self) -> usize {
        self.slots.len()
    }
    pub(crate) fn handle_at(&self, dense: usize) -> i32 {
        self.handles[dense]
    }

    pub(crate) fn contains(&self, handle: i32) -> bool {
        self.index(handle).is_some()
    }

    pub(crate) fn index(&self, handle: i32) -> Option<usize> {
        let bits = handle as u32;
        let slot = Self::slot(handle);
        if bits & 0x00f0_0000 != 0
            || self.generations.get(slot).copied() != Some((bits >> 24) as u8)
        {
            return None;
        }
        let dense = self.slots[slot];
        // Invalid FFI handles get an error instead of the official assert and
        // unchecked access. Valid handles preserve the native dense order.
        (self.handles.get(dense).copied() == Some(handle)).then_some(dense)
    }

    pub(crate) fn get(&self, handle: i32) -> Result<&T> {
        self.index(handle)
            .map(|index| &self.data[index])
            .ok_or_else(invalid_handle)
    }

    pub(crate) fn get_mut(&mut self, handle: i32) -> Result<&mut T> {
        let index = self.index(handle).ok_or_else(invalid_handle)?;
        Ok(&mut self.data[index])
    }

    pub(crate) fn remove(&mut self, handle: i32) -> Result<T> {
        let dense = self.index(handle).ok_or_else(invalid_handle)?;
        self.free.try_reserve(1).map_err(allocation)?;
        let slot = Self::slot(handle);
        let generation = ((handle as u32 >> 24) as u8).wrapping_add(1);
        self.generations[slot] = generation;
        self.free
            .push(((generation as u32) << 24 | slot as u32) as i32);
        self.slots[slot] = 0;
        let result = self.data.swap_remove(dense);
        self.handles.swap_remove(dense);
        if let Some(&moved) = self.handles.get(dense) {
            self.slots[Self::slot(moved)] = dense;
        }
        Ok(result)
    }
}

fn allocation(_: std::collections::TryReserveError) -> Error {
    Error::new(
        Status::ResourceLimit,
        "could not allocate component storage",
    )
}

fn invalid_handle() -> Error {
    Error::new(Status::InvalidArgument, "invalid or stale component handle")
}
