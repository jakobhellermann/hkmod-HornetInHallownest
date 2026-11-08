use std::ffi::OsStr;
use std::os::unix::ffi::OsStrExt;
use std::path::Path;

use rabex::UnityVersion;

#[unsafe(no_mangle)]
pub unsafe extern "C" fn transpile(
    path_ptr: *const u8,
    path_len: usize,
    from_version_ptr: *const u8,
    from_version_len: usize,
    to_version_ptr: *const u8,
    to_version_len: usize,
    out_ptr: *mut *mut u8,
    out_len: *mut usize,
) -> i8 {
    let path = unsafe { bytes(path_ptr, path_len) };
    let from_version = unsafe { bytes(from_version_ptr, from_version_len) };
    let to_version = unsafe { bytes(to_version_ptr, to_version_len) };

    match transpile_inner(path, from_version, to_version) {
        Ok(bytes) => {
            let data = Box::into_raw(bytes.into_boxed_slice());
            unsafe {
                out_ptr.write(data.cast::<u8>());
                out_len.write(data.len());
            }
            0
        }
        Err(_) => 1,
    }
}

/// # Safety
/// `ptr`/`len` must come from a successful `transpile` call and be freed exactly once.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn transpile_free(ptr: *mut u8, len: usize) {
    drop(unsafe { Box::from_raw(std::ptr::slice_from_raw_parts_mut(ptr, len)) });
}

// C# `fixed` on an empty array yields a null pointer, which `from_raw_parts` forbids.
unsafe fn bytes<'a>(ptr: *const u8, len: usize) -> &'a [u8] {
    if len == 0 {
        &[]
    } else {
        unsafe { std::slice::from_raw_parts(ptr, len) }
    }
}

fn transpile_inner(path: &[u8], from_version: &[u8], to_version: &[u8]) -> Result<Vec<u8>, ()> {
    let path: &Path = OsStr::from_bytes(path).as_ref();
    let from_version: UnityVersion = std::str::from_utf8(from_version)
        .map_err(drop)?
        .parse()
        .map_err(drop)?;
    let to_version: UnityVersion = std::str::from_utf8(to_version)
        .map_err(drop)?
        .parse()
        .map_err(drop)?;

    crate::transpile::transpile(path, &from_version, &to_version).map_err(drop)
}
