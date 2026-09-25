//! Convert Silksong's whole Addressables tree into bundles Hollow Knight 1.5.78.11833 (Unity 2020.2.2f1) can load.
//!
//! Unity gates bundle loading twice, and both gates were confirmed empirically against HK 1.5.78:
//!   1. container version — Silksong ships UnityFS v8, HK 1.5.78 reads at most v7
//!      -> "Unable to read header from archive file"
//!   2. exact engine version — a v7 container still declaring `6000.0.50f1` is refused with
//!      "can't be loaded because it was not built with the right version or build target"
//!
//! Rewriting only the container (payload copied byte-for-byte) and declaring the HOST engine version clears both.
//! The Unity 6 objects behind them deserialize correctly on 2020.2 — verified on a real Hornet collections bundle
//! (GameObject + Material + Texture2D all loaded).
//!
//! The non-bundle catalog files (catalog.bin/.hash, settings.json, AddressablesLink) are copied verbatim; they are
//! version-independent and the mod's `SilksongAddressables` points Addressables at them.
//!
//! usage: convert-aa <silksong-aa-dir> <out-aa-dir> [declared-unity-version]

use std::io::Cursor;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicUsize, Ordering};

use anyhow::{Context, Result};
use rabex::UnityVersion;
use rabex::files::bundlefile::builder::BundleFileBuilder;
use rabex::files::bundlefile::{CompressionType, ExtractionConfig};

const SS_VERSION: &str = "6000.0.50f1";
const DEFAULT_TARGET: &str = "2020.2.2f1";

fn convert(
    input: &Path,
    output: &Path,
    source: &UnityVersion,
    declared: &UnityVersion,
) -> Result<()> {
    let bytes = std::fs::read(input)?;
    let config = ExtractionConfig::default().with_fallback_unity_version(source.clone());
    let reader = rabex::files::bundlefile::reader::BundleFileReader::from_reader(
        Cursor::new(bytes),
        &config,
    )?;

    let entries = reader.files().to_vec();
    let mut builder = BundleFileBuilder::unityfs(7, declared);
    for entry in &entries {
        let data = reader.read_at_entry(entry)?;
        builder.add_file_with_flags(&entry.path, Cursor::new(data), entry.flags)?;
    }
    // UNCOMPRESSED, deliberately. Recompressing the blocks with rabex's LZ4 writer produces archives Hollow Knight
    // 1.5.78 rejects at load time with "Failed to decompress data for the AssetBundle '<name>'" — every bundle, and
    // Addressables then crashes in AssetBundleProvider.Release while tearing the failed load down. Writing the blocks
    // uncompressed is the variant that was verified to load. It costs little: these payloads are already-compressed
    // texture/audio data, so an uncompressed archive comes out about the same size as Silksong's LZ4HC original.
    builder.write_to_file(output, CompressionType::None)?;
    Ok(())
}

fn main() -> Result<()> {
    let args: Vec<String> = std::env::args().collect();
    let (in_dir, out_dir) = match args.as_slice() {
        [_, i, o] | [_, i, o, _] => (PathBuf::from(i), PathBuf::from(o)),
        _ => anyhow::bail!(
            "usage: convert-aa <silksong-aa-dir> <out-aa-dir> [declared-unity-version]"
        ),
    };
    let source: UnityVersion = SS_VERSION.parse().unwrap();
    let declared: UnityVersion = args
        .get(3)
        .map(String::as_str)
        .unwrap_or(DEFAULT_TARGET)
        .parse()
        .context("invalid declared unity version")?;
    println!(
        "converting {} -> {} (declaring {declared})",
        in_dir.display(),
        out_dir.display()
    );

    // Collect work: every file under in_dir, bundles get converted, everything else copied.
    let mut jobs: Vec<(PathBuf, PathBuf)> = Vec::new();
    let mut stack = vec![in_dir.clone()];
    while let Some(dir) = stack.pop() {
        for entry in std::fs::read_dir(&dir)? {
            let path = entry?.path();
            if path.is_dir() {
                stack.push(path);
            } else {
                let rel = path.strip_prefix(&in_dir)?.to_path_buf();
                jobs.push((path, out_dir.join(rel)));
            }
        }
    }
    jobs.sort();
    let total = jobs.len();
    println!("{total} files");

    let done = AtomicUsize::new(0);
    let converted = AtomicUsize::new(0);
    let copied = AtomicUsize::new(0);
    let failed = AtomicUsize::new(0);
    let errors = std::sync::Mutex::new(Vec::<String>::new());

    let threads = std::thread::available_parallelism()
        .map(|n| n.get())
        .unwrap_or(4);
    let chunks: Vec<_> = jobs
        .chunks(total.div_ceil(threads).max(1))
        .map(<[_]>::to_vec)
        .collect();

    // Bind shared state as references so each `move` closure captures the reference, not the value.
    let (source, declared) = (&source, &declared);
    let (done, converted, copied, failed, errors) = (&done, &converted, &copied, &failed, &errors);

    std::thread::scope(|scope| {
        for chunk in &chunks {
            let chunk: &[(PathBuf, PathBuf)] = chunk;
            scope.spawn(move || {
                for (src, dst) in chunk {
                    if let Some(parent) = dst.parent() {
                        let _ = std::fs::create_dir_all(parent);
                    }
                    let is_bundle = src.extension().is_some_and(|e| e == "bundle");
                    let result = if is_bundle {
                        convert(src, dst, source, declared).map(|()| {
                            converted.fetch_add(1, Ordering::Relaxed);
                        })
                    } else {
                        std::fs::copy(src, dst)
                            .map(|_| {
                                copied.fetch_add(1, Ordering::Relaxed);
                            })
                            .map_err(Into::into)
                    };
                    if let Err(e) = result {
                        failed.fetch_add(1, Ordering::Relaxed);
                        errors.lock().unwrap().push(format!(
                            "{}: {e}",
                            src.file_name().unwrap().to_string_lossy()
                        ));
                    }
                    let n = done.fetch_add(1, Ordering::Relaxed) + 1;
                    if n % 100 == 0 || n == total {
                        println!("  {n}/{total}");
                    }
                }
            });
        }
    });

    let errs = errors.lock().unwrap();
    println!(
        "done: {} converted, {} copied, {} failed",
        converted.load(Ordering::Relaxed),
        copied.load(Ordering::Relaxed),
        failed.load(Ordering::Relaxed)
    );
    for e in errs.iter().take(20) {
        println!("  FAIL {e}");
    }
    Ok(())
}
