//! Steamworks SDK 1.53a layouts for the installed game's versioned flat API.
//! Source: ValveSoftware/Proton, lsteamclient/steamworks_sdk_153a.

use std::ffi::{c_char, c_void};

pub(super) const CREATOR_APP: u32 = 636750;
pub(super) const CONSUMER_APP: u32 = 457140;

#[repr(C)]
pub(super) struct Strings {
    pub strings: *mut *const c_char,
    pub count: i32,
}

macro_rules! record {
    ($name:ident { $($field:ident: $ty:ty),* $(,)? }) => {
        #[repr(C)]
        #[cfg_attr(not(target_os = "windows"), repr(packed(4)))]
        #[derive(Clone, Copy)]
        pub(super) struct $name { $(pub $field: $ty),* }
    };
}

record!(Share {
    result: i32,
    handle: u64,
    filename: [u8; 260]
});
record!(Published {
    result: i32,
    id: u64,
    needs_agreement: u8
});
record!(Owned {
    result: i32,
    returned: i32,
    total: i32,
    ids: [u64; 50]
});
record!(Downloaded {
    result: i32,
    handle: u64,
    app: u32,
    bytes: i32,
    filename: [u8; 260],
    owner: u64
});
record!(Details {
    result: i32,
    id: u64,
    creator: u32,
    consumer: u32,
    title: [u8; 129],
    description: [u8; 8000],
    file: u64,
    preview: u64,
    owner: u64,
    created: u32,
    updated: u32,
    visibility: i32,
    banned: u8,
    tags: [u8; 1025],
    tags_truncated: u8,
    filename: [u8; 260],
    file_bytes: i32,
    preview_bytes: i32,
    url: [u8; 256],
    kind: i32,
    accepted: u8,
});
record!(UgcDetails {
    id: u64,
    result: i32,
    kind: i32,
    creator: u32,
    consumer: u32,
    title: [u8; 129],
    description: [u8; 8000],
    owner: u64,
    created: u32,
    updated: u32,
    added: u32,
    visibility: i32,
    banned: u8,
    accepted: u8,
    tags_truncated: u8,
    tags: [u8; 1025],
    file: u64,
    preview: u64,
    filename: [u8; 260],
    file_bytes: i32,
    preview_bytes: i32,
    url: [u8; 256],
    up: u32,
    down: u32,
    score: f32,
    children: u32,
});
record!(Queried {
    handle: u64,
    result: i32,
    returned: u32,
    total: u32,
    cached: u8,
    cursor: [u8; 256]
});
record!(Submitted {
    result: i32,
    needs_agreement: u8,
    id: u64
});
record!(Dependency {
    result: i32,
    parent: u64,
    child: u64
});

pub(super) type Interface = *mut c_void;

#[cfg(test)]
mod tests {
    use super::*;
    use std::mem::{align_of, offset_of, size_of};

    #[test]
    #[cfg(not(target_os = "windows"))]
    fn callback_layouts_match_the_valve_header_compiler_probe() {
        assert_eq!(size_of::<Share>(), 272);
        assert_eq!(size_of::<Published>(), 16);
        assert_eq!(size_of::<Owned>(), 412);
        assert_eq!(size_of::<Downloaded>(), 288);
        assert_eq!(size_of::<Queried>(), 280);
        assert_eq!(size_of::<Submitted>(), 16);
        assert_eq!(size_of::<Dependency>(), 20);
        assert_eq!(size_of::<Details>(), 9748);
        assert_eq!(size_of::<UgcDetails>(), 9764);
        assert_eq!(align_of::<Details>(), 4);
        assert_eq!(offset_of!(Details, file), 8152);
        assert_eq!(offset_of!(UgcDetails, children), 9760);
        assert_eq!(offset_of!(Published, id), 4);
        assert_eq!(offset_of!(Submitted, id), 8);
    }
}
