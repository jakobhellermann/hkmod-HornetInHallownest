//! b2 validation: rewrite the Silksong monoscripts bundle so its MonoScripts point at the IL-prefixed Silksong.*
//! assemblies. Then the original Hero_Hornet prefab's components bind to our prefixed types (with their serialized
//! field values intact) instead of HK's same-named classes.
//!
//! Rebuilds the (externals-free) monoscripts CAB: re-adds the AssetBundle manifest + all 1262 MonoScripts, rewriting
//! m_AssemblyName/m_Namespace for the Assembly-CSharp / Assembly-CSharp-firstpass ones to match SilksongPrefixer's
//! naming. Writes Source/lib/monoscripts.silksong.bundle.

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

fn remap_assembly(a: &str) -> Option<&'static str> {
    match a {
        "Assembly-CSharp" => Some("Silksong.AssemblyCSharp"),
        "Assembly-CSharp-firstpass" => Some("Silksong.AssemblyCSharpfirstpass"),
        // PlayMaker is prefixed too (SilksongPrefixer): the bundle's PlayMakerFSM components must bind to
        // Silksong.PlayMaker so Hornet's FSMs use Silksong's isolated PlayMaker runtime, not HK's shared one.
        // NOTE: PlayMaker MonoScripts store m_AssemblyName as "PlayMaker.dll" (WITH the .dll suffix), unlike the
        // Assembly-CSharp ones — match that exact string or they're silently skipped.
        "PlayMaker" | "PlayMaker.dll" => Some("Silksong.PlayMaker"),
        // Shared TeamCherry assemblies that define PlayMaker actions (FadeNestedFadeGroup, GetLocalisedString,
        // ConditionalExpression) — prefixed too, so their actions derive from Silksong.PlayMaker AND their components
        // bind to the same prefixed assembly the actions reference (else action field type vs real component mismatch).
        "TeamCherry.NestedFadeGroup" => Some("Silksong.TeamCherryNestedFadeGroup"),
        "TeamCherry.Localization" => Some("Silksong.TeamCherryLocalization"),
        "ConditionalExpression" => Some("Silksong.ConditionalExpression"),
        _ => None,
    }
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
                    // ASSEMBLY-ONLY rename: change only m_AssemblyName so the bundle's components bind to our renamed
                    // copy. m_Namespace / m_ClassName stay intact (the prefixer no longer prefixes namespaces), so
                    // name-based resolution (PlayMaker actions, Unity nested [Serializable] classes) keeps working.
                    ms.m_AssemblyName = new_asm.to_string();
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
