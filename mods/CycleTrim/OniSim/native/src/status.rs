use std::cell::RefCell;
use std::ffi::{CString, c_char};
use std::panic::{AssertUnwindSafe, catch_unwind};

#[derive(Clone, Copy)]
#[repr(i32)]
pub(crate) enum Status {
    InvalidArgument = 1,
    Unsupported = 2,
    InvalidState = 3,
    ResourceLimit = 4,
    InternalFailure = 5,
    BufferTooSmall = 6,
}

pub(crate) struct Error {
    pub(crate) status: Status,
    pub(crate) message: String,
}

impl Error {
    pub(crate) fn new(status: Status, message: impl Into<String>) -> Self {
        Self {
            status,
            message: message.into(),
        }
    }
}

pub(crate) type Result<T> = std::result::Result<T, Error>;

struct LastStatus {
    code: i32,
    message: CString,
}

thread_local! {
    static LAST: RefCell<LastStatus> = RefCell::new(LastStatus {
        code: 0,
        message: CString::default(),
    });
}

fn set(error: Option<Error>) -> bool {
    catch_unwind(AssertUnwindSafe(|| {
        LAST.try_with(|last| {
            let Ok(mut last) = last.try_borrow_mut() else {
                return false;
            };
            match error {
                None => {
                    last.code = 0;
                    last.message = CString::default();
                }
                Some(error) => {
                    last.code = error.status as i32;
                    let safe = error.message.replace('\0', "?");
                    last.message = CString::new(safe).unwrap_or_default();
                }
            }
            true
        })
        .unwrap_or(false)
    }))
    .unwrap_or(false)
}

pub(crate) fn boundary<T>(failure: T, f: impl FnOnce() -> Result<T>) -> T {
    // Foreign TLS destructors may call us after this thread's LAST was destroyed.
    // Refuse the operation before mutation when its status cannot be reported.
    if !set(None) {
        return failure;
    }
    match catch_unwind(AssertUnwindSafe(f)) {
        Ok(Ok(value)) => value,
        Ok(Err(error)) => {
            set(Some(error));
            failure
        }
        Err(_) => {
            set(Some(Error::new(
                Status::InternalFailure,
                "Rust panic contained at FFI boundary",
            )));
            failure
        }
    }
}

pub(crate) fn last_status() -> i32 {
    catch_unwind(|| {
        LAST.try_with(|last| {
            last.try_borrow()
                .map_or(Status::InternalFailure as i32, |last| last.code)
        })
        .unwrap_or(Status::InternalFailure as i32)
    })
    .unwrap_or(Status::InternalFailure as i32)
}
pub(crate) fn last_error() -> *const c_char {
    let unavailable = c"status unavailable".as_ptr();
    catch_unwind(|| {
        LAST.try_with(|last| {
            last.try_borrow()
                .map_or(unavailable, |last| last.message.as_ptr())
        })
        .unwrap_or(unavailable)
    })
    .unwrap_or(unavailable)
}
