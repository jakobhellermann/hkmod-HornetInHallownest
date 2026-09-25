//! Re-emit a Unity 6 AssetBundle inside a UnityFS **v7** container, byte-identical payload.
//!
//! Silksong ships UnityFS **v8** archives; Hollow Knight 1.5.78.11833 (Unity 2020.2.2f1) only understands up to v7 and
//! fails at the archive layer ("Unable to read header from archive file") before it ever looks at the objects inside.
//! This copies each inner file out verbatim and rewrites only the container around it, so a load attempt on the result
//! isolates one variable: is the container version the whole barrier, or is there also a class-version barrier behind
//! it? Deliberately does NOT rebake typetrees — repack-resources already covers that path.
//!
//! usage: recontainer <in.bundle> <out.bundle> [declared-unity-version]
//!
//! Silksong strips the engine version from addressables headers ("0.0.0"), so the source version is assumed to be
//! SS_VERSION. The optional third arg overrides what the OUTPUT header claims, which lets us probe Unity's
//! exact-version check separately from the container-version check.

use std::io::Cursor;

use anyhow::{Context, Result};
use rabex::UnityVersion;
use rabex::files::bundlefile::builder::BundleFileBuilder;
use rabex::files::bundlefile::{CompressionType, ExtractionConfig};

const SS_VERSION: &str = "6000.0.50f1";

fn main() -> Result<()> {
    let args: Vec<String> = std::env::args().collect();
    let (input, output) = match args.as_slice() {
        [_, i, o] | [_, i, o, _] => (i, o),
        _ => anyhow::bail!("usage: recontainer <in.bundle> <out.bundle> [declared-unity-version]"),
    };

    // Silksong's addressables headers carry "0.0.0"; supply the real version so archive-flag parsing picks the
    // modern layout.
    let ss_version: UnityVersion = SS_VERSION.parse().expect("valid unity version");
    let config = ExtractionConfig::default().with_fallback_unity_version(ss_version.clone());
    // read_at_entry is only implemented for Cursor-backed readers, so slurp the archive.
    let bytes = std::fs::read(input).with_context(|| format!("open {input}"))?;
    let reader = rabex::files::bundlefile::reader::BundleFileReader::from_reader(
        Cursor::new(bytes),
        &config,
    )?;

    let header_version = reader.header().version;
    let unity_version = reader.header().unity_revision.clone().unwrap_or(ss_version);
    let declared: UnityVersion = match args.get(3) {
        Some(v) => v.parse().expect("valid unity version"),
        None => unity_version.clone(),
    };
    let entries = reader.files().to_vec();
    println!(
        "in : UnityFS v{header_version}, built with {unity_version}, {} file(s)",
        entries.len()
    );

    println!("out declares: {declared}");
    let mut builder = BundleFileBuilder::unityfs(7, &declared);
    for entry in &entries {
        let data = reader.read_at_entry(entry)?;
        println!(
            "     {} ({} bytes, flags {})",
            entry.path,
            data.len(),
            entry.flags
        );
        builder.add_file_with_flags(&entry.path, Cursor::new(data), entry.flags)?;
    }

    // Uncompressed: keeps the rewrite to purely the container version, and 2020.2 reads LZ4 and none alike.
    builder.write_to_file(output, CompressionType::None)?;
    println!("out: UnityFS v7 -> {output}");
    Ok(())
}
