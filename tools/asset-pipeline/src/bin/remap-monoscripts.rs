//! Rewrite the Silksong monoscripts CAB so its MonoScripts' m_AssemblyName points at the IL-prefixed Silksong.*
//! assemblies, so the Hero_Hornet prefab's components bind to our prefixed types (serialized values intact) instead of
//! HK's same-named classes. Re-adds the AssetBundle manifest + all MonoScripts; writes Source/lib/monoscripts.silksong.bundle.

use std::io::Cursor;

use anyhow::{Context, Result};
use hornet_asset_pipeline::find_game;
use rabex::files::bundlefile::{BundleFileBuilder, CompressionType};
use rabex::files::serializedfile::build_common_offset_map;
use rabex::files::serializedfile::builder::SerializedFileBuilder;
use rabex::objects::ClassId;
use rabex_env::unity::types::{AssetBundle, MonoScript};

const CAB: &str = "CAB-283454ff0b75a987406e2e403b4dec2b";
const OUT: &str = concat!(
    env!("CARGO_MANIFEST_DIR"),
    "/../../Source/lib/monoscripts.silksong.bundle"
);

// Prefix an assembly name in our set to "Silksong.<base>" (mirrors AssemblyPrefixer.PrefixedName)
fn remap_assembly(a: &str) -> Option<String> {
    let base = a.strip_suffix(".dll").unwrap_or(a); // PlayMaker.dll is suffixed
    matches!(
        base,
        "Assembly-CSharp"
            | "Assembly-CSharp-firstpass"
            | "PlayMaker"
            | "TeamCherry.NestedFadeGroup"
            | "TeamCherry.Localization"
            | "ConditionalExpression"
    )
    .then(|| format!("Silksong.{base}"))
}

fn main() -> Result<()> {
    let env = find_game("silksong")?.context("silksong not found")?;
    let files = env.load_all_serialized_files()?;
    let key = files
        .keys()
        .find(|k| k.contains("283454ff"))
        .context("monoscripts CAB not found")?
        .clone();
    let file = &files[&key];
    let version = env.unity_version()?.clone();

    let offset_map = build_common_offset_map(&env.tpk.inner, &version);
    let mut b = SerializedFileBuilder::new(&version, &env.tpk, &offset_map, true);

    let mut remapped = 0usize;
    let mut total = 0usize;
    for obj in file.objects::<()>() {
        let pid = obj.path_id();
        match obj.class_id() {
            ClassId::AssetBundle => {
                let ab = obj.cast::<AssetBundle>().read()?;
                b.add_object_at(pid, &ab)?;
            }
            ClassId::MonoScript => {
                total += 1;
                let mut ms = obj.cast::<MonoScript>().read()?;
                if let Some(new_asm) = remap_assembly(&ms.m_AssemblyName) {
                    // Assembly-only rename (matches SilksongPrefixer): m_Namespace/m_ClassName stay intact so name-based
                    // resolution keeps working.
                    ms.m_AssemblyName = new_asm;
                    remapped += 1;
                }
                b.add_object_at(pid, &ms)?;
            }
            _ => {}
        }
    }

    let mut serialized = Vec::new();
    b.write(Cursor::new(&mut serialized))?;

    let mut bundle = BundleFileBuilder::unityfs(7, &version);
    bundle.add_file(CAB, Cursor::new(serialized))?;
    let mut out = std::fs::File::create(OUT)?;
    bundle.write(&mut out, CompressionType::None)?; // uncompressed: loads fine, avoids needing the lz4hc feature

    println!("remapped {remapped}/{total} MonoScripts -> Silksong.*; wrote {OUT}");
    std::mem::forget(env);
    Ok(())
}
