//! Retarget MonoScripts in an existing monoscripts bundle from one assembly to another, without needing a Silksong
//! install (remap-monoscripts regenerates from the game; this patches its output in place).
//!
//! Needed for Hollow Knight 1.5.78: there tk2d is compiled into Assembly-CSharp, not a separate TeamCherry.TK2D
//! assembly. UnityDowngrader already points the IL's tk2d references at Assembly-CSharp, but the bundle's tk2d
//! MonoScripts still name TeamCherry.TK2D, so every tk2dSprite/tk2dSpriteAnimator on Hornet loads as a missing script.
//!
//! usage: retarget-monoscripts <in.bundle> <out.bundle> <from-asm>=<to-asm>...
//!   e.g. retarget-monoscripts monoscripts.silksong.bundle out.bundle TeamCherry.TK2D=Assembly-CSharp

use std::io::Cursor;

use anyhow::{Context, Result};
use rabex::UnityVersion;
use rabex::files::SerializedFile;
use rabex::files::bundlefile::builder::BundleFileBuilder;
use rabex::files::bundlefile::{CompressionType, ExtractionConfig};
use rabex::files::serializedfile::build_common_offset_map;
use rabex::files::serializedfile::builder::SerializedFileBuilder;
use rabex::objects::ClassId;
use rabex::tpk::TpkTypeTreeBlob;
use rabex::typetree::typetree_cache::sync::TypeTreeCache;
use rabex_env::unity::types::{AssetBundle, MonoScript};

// Serialized payload version inside the bundle (the container header declares the host version instead).
const SS_VERSION: &str = "6000.0.50f1";

fn main() -> Result<()> {
    let args: Vec<String> = std::env::args().collect();
    anyhow::ensure!(
        args.len() >= 4,
        "usage: retarget-monoscripts <in.bundle> <out.bundle> <from>=<to>..."
    );
    let (input, output) = (&args[1], &args[2]);
    let maps: Vec<(String, String)> = args[3..]
        .iter()
        .map(|m| {
            m.split_once('=')
                .map(|(a, b)| (a.to_string(), b.to_string()))
                .context("mapping must be from=to")
        })
        .collect::<Result<_>>()?;

    let ss_version: UnityVersion = SS_VERSION.parse().unwrap();
    let config = ExtractionConfig::default().with_fallback_unity_version(ss_version.clone());
    let bytes = std::fs::read(input).with_context(|| format!("open {input}"))?;
    let reader = rabex::files::bundlefile::reader::BundleFileReader::from_reader(
        Cursor::new(bytes),
        &config,
    )?;
    let declared = reader
        .header()
        .unity_revision
        .clone()
        .unwrap_or(ss_version.clone());
    let entries = reader.files().to_vec();
    anyhow::ensure!(
        entries.len() == 1,
        "expected a single CAB, found {}",
        entries.len()
    );
    let entry = &entries[0];
    let data = reader.read_at_entry(entry)?;

    let mut cur = Cursor::new(&data);
    let file = SerializedFile::from_reader(&mut cur)?;
    let version = file.m_UnityVersion.clone().unwrap_or(ss_version);

    let tpk = TypeTreeCache::new(TpkTypeTreeBlob::embedded());
    let offset_map = build_common_offset_map(&tpk.inner, &version);
    let mut b = SerializedFileBuilder::new(&version, &tpk, &offset_map, true);

    let (mut total, mut changed) = (0usize, 0usize);
    for info in file.objects() {
        let pid = info.m_PathID;
        match info.m_ClassID {
            ClassId::AssetBundle => {
                let ab: AssetBundle = file.read(info, &tpk, &mut cur)?;
                b.add_object_at(pid, &ab)?;
            }
            ClassId::MonoScript => {
                total += 1;
                let mut ms: MonoScript = file.read(info, &tpk, &mut cur)?;
                if let Some((_, to)) = maps.iter().find(|(from, _)| *from == ms.m_AssemblyName) {
                    println!(
                        "  {}.{}: {} -> {to}",
                        ms.m_Namespace, ms.m_ClassName, ms.m_AssemblyName
                    );
                    ms.m_AssemblyName = to.clone();
                    changed += 1;
                }
                b.add_object_at(pid, &ms)?;
            }
            other => anyhow::bail!("unexpected object class {other:?} in monoscripts bundle"),
        }
    }

    let mut serialized = Vec::new();
    b.write(Cursor::new(&mut serialized))?;
    let mut bundle = BundleFileBuilder::unityfs(7, &declared);
    bundle.add_file(&entry.path, Cursor::new(serialized))?;
    bundle.write_to_file(output, CompressionType::None)?;
    println!("retargeted {changed}/{total} MonoScripts; wrote {output} (declares {declared})");
    Ok(())
}
