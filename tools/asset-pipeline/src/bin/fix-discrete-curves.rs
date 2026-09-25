//! Make Unity 6 AnimationClips playable on Unity 2020.2 (Hollow Knight 1.5.78) by folding "discrete" curves back into
//! the streamed curve count.
//!
//! Unity 2022+ stores sprite swaps / PPtr curves as *discrete* curves: their keys live in the StreamedClip data right
//! after the continuous streamed curves (same key layout, value = PPtr mapping index as float), but are counted in a new
//! `discreteCurveCount` field instead of `curveCount`. Unity 2020 has no such field, drops it when reading the Unity 6
//! typetree, and then hits keys whose curve index is >= curveCount -> native crash as soon as an Animator binds the clip
//! (e.g. Silksong's "Tool Flea Egg HUD" / "Lava Bell HUD"). Unity 2020 encodes PPtr curves exactly like that (float index,
//! streamed), so setting `curveCount += discreteCurveCount; discreteCurveCount = 0` is a faithful conversion.
//!
//! In Unity 6 both fields are u16s packed directly after the StreamedClip data array (Unity 2020 reads the u16
//! curveCount into its u32 via the typetree), so the patch is size-preserving and done in place
//! on uncompressed bundles (convert-aa writes them uncompressed). Every patch is appended to `<dir>/discrete-curves.patchlog`
//! (file, offset, old values) so it can be reverted. Idempotent: patched clips report discreteCurveCount 0.
//!
//! usage: fix-discrete-curves [--dry-run] <bundle-or-dir>...

use std::io::Cursor;
use std::path::{Path, PathBuf};
use std::sync::Mutex;
use std::sync::atomic::{AtomicUsize, Ordering};

use anyhow::{Context, Result};
use rabex::UnityVersion;
use rabex::files::SerializedFile;
use rabex::files::bundlefile::ExtractionConfig;
use rabex::tpk::TpkTypeTreeBlob;
use rabex::typetree::typetree_cache::sync::TypeTreeCache;

const ANIMATION_CLIP: i32 = 74;

struct Patch {
    offset: usize,
    curve_count: u32,
    discrete: u32,
}

fn find_all(hay: &[u8], needle: &[u8]) -> Vec<usize> {
    let mut out = Vec::new();
    if needle.is_empty() || hay.len() < needle.len() {
        return out;
    }
    let first = needle[0];
    let mut i = 0;
    while i + needle.len() <= hay.len() {
        match memchr(first, &hay[i..=hay.len() - needle.len()]) {
            Some(p) => {
                let at = i + p;
                if &hay[at..at + needle.len()] == needle {
                    out.push(at);
                }
                i = at + 1;
            }
            None => break,
        }
    }
    out
}

fn memchr(b: u8, s: &[u8]) -> Option<usize> {
    s.iter().position(|&x| x == b)
}

fn patches_for(bundle: &[u8], tpk: &TypeTreeCache<TpkTypeTreeBlob>) -> Result<(usize, Vec<Patch>)> {
    let ss: UnityVersion = "6000.0.50f1".parse().unwrap();
    let config = ExtractionConfig::default().with_fallback_unity_version(ss);
    let reader = rabex::files::bundlefile::reader::BundleFileReader::from_reader(
        Cursor::new(bundle.to_vec()),
        &config,
    )?;
    let mut clips = 0;
    let mut patches = Vec::new();
    for entry in reader.files().to_vec() {
        if entry.path.ends_with(".resS") || entry.path.ends_with(".resource") {
            continue;
        }
        let data = reader.read_at_entry(&entry)?;
        let mut cur = Cursor::new(&data);
        let Ok(file) = SerializedFile::from_reader(&mut cur) else {
            continue;
        };
        for info in file.objects() {
            if info.m_ClassID.0 != ANIMATION_CLIP {
                continue;
            }
            clips += 1;
            let v: serde_json::Value = file.read(info, tpk, &mut cur)?;
            let sc = &v["m_MuscleClip"]["m_Clip"]["data"]["m_StreamedClip"];
            let discrete = sc["discreteCurveCount"].as_u64().unwrap_or(0) as u32;
            if discrete == 0 {
                continue;
            }
            let curve_count = sc["curveCount"].as_u64().context("curveCount")? as u32;
            let words: Vec<u32> = sc["data"]
                .as_array()
                .context("streamed data")?
                .iter()
                .map(|w| w.as_u64().unwrap_or(0) as u32)
                .collect();
            // Serialized layout: i32 len, len * u32, u16 curveCount, u16 discreteCurveCount.
            anyhow::ensure!(
                curve_count + discrete <= u16::MAX as u32,
                "curve count overflows u16"
            );
            let mut needle = Vec::with_capacity(12 + words.len() * 4);
            needle.extend_from_slice(&(words.len() as u32).to_le_bytes());
            for w in &words {
                needle.extend_from_slice(&w.to_le_bytes());
            }
            needle.extend_from_slice(&(curve_count as u16).to_le_bytes());
            needle.extend_from_slice(&(discrete as u16).to_le_bytes());

            let hits = find_all(bundle, &needle);
            anyhow::ensure!(
                !hits.is_empty(),
                "clip '{}' streamed data not found verbatim (bundle compressed?)",
                v["m_Name"].as_str().unwrap_or("?")
            );
            for h in hits {
                patches.push(Patch {
                    offset: h + needle.len() - 4,
                    curve_count,
                    discrete,
                });
            }
        }
    }
    Ok((clips, patches))
}

fn collect(paths: &[String]) -> Vec<PathBuf> {
    let mut out = Vec::new();
    let mut stack: Vec<PathBuf> = paths.iter().map(PathBuf::from).collect();
    while let Some(p) = stack.pop() {
        if p.is_dir() {
            if let Ok(rd) = std::fs::read_dir(&p) {
                stack.extend(rd.flatten().map(|e| e.path()));
            }
        } else if p.extension().is_some_and(|e| e == "bundle") {
            out.push(p);
        }
    }
    out.sort();
    out
}

fn main() -> Result<()> {
    let mut args: Vec<String> = std::env::args().skip(1).collect();
    let dry = args.first().is_some_and(|a| a == "--dry-run");
    if dry {
        args.remove(0);
    }
    anyhow::ensure!(
        !args.is_empty(),
        "usage: fix-discrete-curves [--dry-run] <bundle-or-dir>..."
    );
    let files = collect(&args);
    let log_path = {
        let first = Path::new(&args[0]);
        let dir = if first.is_dir() {
            first.to_path_buf()
        } else {
            first.parent().unwrap_or(Path::new(".")).to_path_buf()
        };
        dir.join("discrete-curves.patchlog")
    };
    println!(
        "{} bundle(s){}",
        files.len(),
        if dry { " (dry run)" } else { "" }
    );

    let tpk = TypeTreeCache::new(TpkTypeTreeBlob::embedded());
    let (done, clips_total, patched_total, bundles_patched) = (
        AtomicUsize::new(0),
        AtomicUsize::new(0),
        AtomicUsize::new(0),
        AtomicUsize::new(0),
    );
    let log = Mutex::new(Vec::<String>::new());
    let errors = Mutex::new(Vec::<String>::new());
    let threads = 4; // bundles are read whole; bounded to keep memory in check
    let total = files.len();
    let chunks: Vec<Vec<PathBuf>> = files
        .chunks(files.len().div_ceil(threads).max(1))
        .map(<[_]>::to_vec)
        .collect();

    std::thread::scope(|s| {
        for chunk in &chunks {
            let (tpk, done, clips_total, patched_total, bundles_patched, log, errors) = (
                &tpk,
                &done,
                &clips_total,
                &patched_total,
                &bundles_patched,
                &log,
                &errors,
            );
            s.spawn(move || {
                for path in chunk {
                    let result = (|| -> Result<()> {
                        let mut bytes = std::fs::read(path)?;
                        let (clips, patches) = patches_for(&bytes, tpk)?;
                        clips_total.fetch_add(clips, Ordering::Relaxed);
                        if patches.is_empty() {
                            return Ok(());
                        }
                        let mut lines = Vec::new();
                        for p in &patches {
                            lines.push(format!(
                                "{}\t{}\t{}\t{}",
                                path.display(),
                                p.offset,
                                p.curve_count,
                                p.discrete
                            ));
                            bytes[p.offset..p.offset + 2].copy_from_slice(
                                &((p.curve_count + p.discrete) as u16).to_le_bytes(),
                            );
                            bytes[p.offset + 2..p.offset + 4].copy_from_slice(&0u16.to_le_bytes());
                        }
                        if !dry {
                            let tmp = path.with_extension("bundle.tmp");
                            std::fs::write(&tmp, &bytes)?;
                            std::fs::rename(&tmp, path)?;
                            log.lock().unwrap().extend(lines);
                        }
                        patched_total.fetch_add(patches.len(), Ordering::Relaxed);
                        bundles_patched.fetch_add(1, Ordering::Relaxed);
                        Ok(())
                    })();
                    if let Err(e) = result {
                        errors
                            .lock()
                            .unwrap()
                            .push(format!("{}: {e:#}", path.display()));
                    }
                    let n = done.fetch_add(1, Ordering::Relaxed) + 1;
                    if n % 100 == 0 {
                        println!("  {n}/{total}");
                    }
                }
            });
        }
    });

    if !dry {
        use std::io::Write;
        let mut f = std::fs::OpenOptions::new()
            .create(true)
            .append(true)
            .open(&log_path)?;
        for l in log.lock().unwrap().iter() {
            writeln!(f, "{l}")?;
        }
    }
    let errs = errors.lock().unwrap();
    println!(
        "clips scanned: {}, discrete clips patched: {} in {} bundle(s), errors: {}",
        clips_total.load(Ordering::Relaxed),
        patched_total.load(Ordering::Relaxed),
        bundles_patched.load(Ordering::Relaxed),
        errs.len()
    );
    for e in errs.iter().take(20) {
        println!("  ERR {e}");
    }
    if !dry {
        println!("patch log: {}", log_path.display());
    }
    Ok(())
}
