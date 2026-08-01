//! Hacky, just to get something working in the beginning.
//! In need of a refactor, or maybe removal alltogether if possible.

//! Repack Silksong's `resources.assets` into one AssetBundle bound to the IL-prefixed `Silksong.*` assemblies.
//! Used by `SilksongResources`. Mostly used for localization/playmaker globals.
//!
//!   * AssetBundle at path id 1, otherwise it won't load
//!   * MonoBehaviour `m_Script` points at a MonoScript in globalgamemanagers.assets (external, HK lacks it) -> embed it
//!     locally, remap `m_AssemblyName` to `Silksong.*`, repoint `m_Script`, add a SerializedType with `m_ScriptTypeIndex`.
//!   * Typetrees are required (a typetree-less Silksong bundle fails LoadFromFile due to monoscript-hash checks).
//!     resources.assets is typetree-stripped, so per MonoBehaviour we generate the script tree from the original assembly
//!     (env.tpk only knows engine types); baked via `generate_typetree`, validated by rabex-env's validate_typetrees.

use std::borrow::Cow;
use std::io::Cursor;

use anyhow::{Context, Result};
use hornet_asset_pipeline::find_game;
use rabex::files::bundlefile::{BundleFileBuilder, CompressionType};
use rabex::files::serializedfile::builder::SerializedFileBuilder;
use rabex::files::serializedfile::{LocalSerializedObjectIdentifier, build_common_offset_map};
use rabex::objects::ClassId;
use rabex::objects::pptr::{FileId, PPtr};
use rabex::typetree::TypeTreeNode;
use rabex_env::trace_pptr::replace_pptrs_inplace_endianed;
use rabex_env::unity::types::{AssetBundle, AssetInfo, MonoBehaviour, MonoScript, ResourceManager};
use rustc_hash::FxHashMap;

const OUT: &str = concat!(
    env!("CARGO_MANIFEST_DIR"),
    "/../../Assets/bundles/silksong-resources.bundle"
);

// Toggle for experiments; must stay true (see module doc).
const BAKE_TYPETREE: bool = true;

// Prefix an assembly name in our set to "Silksong.<base>" (same rule as SilksongPrefixer / remap-monoscripts).
fn remap_assembly(a: &str) -> Option<String> {
    let base = a.strip_suffix(".dll").unwrap_or(a);
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
    let version = env.unity_version()?.clone();

    // 1. ResourceManager: lowercase path -> PPtr (all point into resources.assets, file id 4).
    let ggm = env.load_serialized("globalgamemanagers")?;
    let rm = ggm
        .find_object_of::<ResourceManager>()?
        .context("no ResourceManager in globalgamemanagers")?;

    // ONLY_PATH=<lowercase container path> builds a minimal 1-object bundle for fast iteration. Unset = pack everything.
    let only_path = std::env::var("ONLY_PATH").ok();
    let only_pid: Option<i64> = match &only_path {
        Some(p) => Some(
            rm.m_Container
                .get(p.as_str())
                .with_context(|| format!("no '{p}' in ResourceManager"))?
                .m_PathID,
        ),
        None => None,
    };
    let keep = |pid: i64| only_pid.is_none_or(|only| pid == only);

    // 2. resources.assets — owned file + raw data for the raw copy.
    let (res_file, res_data) = env.load_serialized_uncached("resources.assets")?;
    let res_data = res_data.as_ref();
    let endianness = res_file.m_Header.m_Endianess;
    // 3. handle for cross-file reads (MonoBehaviour.m_Script -> globalgamemanagers.assets MonoScript).
    let res_handle = env.load_serialized("resources.assets")?;

    let offset_map = build_common_offset_map(&env.tpk.inner, &version);
    let mut b = SerializedFileBuilder::new(&version, &env.tpk, &offset_map, BAKE_TYPETREE);
    b.copy_externals(&res_file);
    b.next_path_id = 2; // reserve pid 1 for the AssetBundle

    // path id remap for ALL resources.assets objects (orig -> >=2). Empty file-id remap = identity externals.
    let mut remap: FxHashMap<i64, i64> = FxHashMap::default();
    for obj in res_file.objects() {
        let new_pid = b.next_path_id;
        b.next_path_id += 1;
        remap.insert(obj.m_PathID, new_pid);
    }
    let file_id_remap: FxHashMap<FileId, FileId> = FxHashMap::default();

    // 2b. Embed the MonoScripts referenced by MonoBehaviours, remapped to Silksong.*, and generate each script's typetree
    // from the original assembly (see module doc).
    let mut ms_embed: FxHashMap<i64, i64> = FxHashMap::default(); // ggma monoscript pid -> local embedded pid
    let mut ms_typetree: FxHashMap<i64, TypeTreeNode> = FxHashMap::default(); // ggma monoscript pid -> generated tree
    let mut mb_to_ggma: FxHashMap<i64, i64> = FxHashMap::default(); // mb NEW pid -> ggma monoscript pid
    let mut embed_pid: i64 = 1_000_000;
    for obj in res_handle.objects_of::<MonoBehaviour>() {
        let mb_orig = obj.path_id();
        if !keep(mb_orig) {
            continue;
        }
        let mb = obj.read()?;
        let ggma_pid = mb.m_Script.m_PathID;
        if !ms_embed.contains_key(&ggma_pid) {
            let mut ms: MonoScript = res_handle.deref_read(mb.m_Script)?;
            let tt = env
                .generate_typetree(&ms.assembly_name(), &ms.full_name())?
                .with_context(|| {
                    format!(
                        "no typetree for {} ({})",
                        ms.full_name(),
                        ms.assembly_name()
                    )
                })?
                .clone();
            ms_typetree.insert(ggma_pid, tt);
            let p = embed_pid;
            embed_pid += 1;
            if let Some(n) = remap_assembly(ms.assembly_name_base()) {
                ms.m_AssemblyName = n;
            }
            b.add_object_at(p, &ms)?;
            ms_embed.insert(ggma_pid, p);
        }
        mb_to_ggma.insert(remap[&mb_orig], ggma_pid);
    }

    // 3. Copy every object raw, with internal PPtr remap; MBs get m_Script repointed + a script-typed SerializedType.
    let mut ms_scripttype: FxHashMap<i64, i16> = FxHashMap::default();
    let mut n_mb = 0usize;
    for obj in res_file.objects() {
        if !keep(obj.m_PathID) {
            continue;
        }
        let new_pid = remap[&obj.m_PathID];
        let off = obj.m_Offset as usize;
        let size = obj.m_Size as usize;
        let mut raw = res_data[off..off + size].to_vec();

        // MonoBehaviours use the generated script tree; everything else uses env.tpk (engine types).
        let is_mb = obj.m_ClassID == ClassId::MonoBehaviour;
        let tt: Cow<TypeTreeNode> = if is_mb {
            Cow::Borrowed(&ms_typetree[&mb_to_ggma[&new_pid]])
        } else {
            res_file.get_typetree_for(obj, &env.tpk)?
        };
        // Remap internal local PPtrs on a copy; tolerate a walker failure ([SerializeReference] nodes it can't traverse)
        // rather than abort — m_Script is repointed manually below, and the objects we load have no such fields.
        let mut remapped = raw.clone();
        match replace_pptrs_inplace_endianed(&mut remapped, &tt, &remap, &file_id_remap, endianness)
        {
            Ok(()) => raw = remapped,
            Err(e) => eprintln!(
                "[skip-pptr-remap] {:?} pid {} ({}): {e}",
                obj.m_ClassID, obj.m_PathID, tt.m_Type
            ),
        }

        let mut ty = res_file.m_Types[obj.m_TypeID as usize].clone();
        if BAKE_TYPETREE {
            ty.m_Type = Some(tt.into_owned());
        }
        if is_mb {
            let local_ms = ms_embed[&mb_to_ggma[&new_pid]];
            // Repoint m_Script (PPtr: file id i32 @ 16, path id i64 @ 20) to the local embedded monoscript.
            #[allow(clippy::identity_op)]
            raw[16..20].copy_from_slice(&0i32.to_le_bytes());
            raw[20..28].copy_from_slice(&local_ms.to_le_bytes());
            let st = *ms_scripttype.entry(local_ms).or_insert_with(|| {
                let sts = b.serialized.m_ScriptTypes.get_or_insert_with(Vec::new);
                let i = sts.len() as i16;
                sts.push(LocalSerializedObjectIdentifier {
                    m_LocalSerializedFileIndex: FileId::LOCAL,
                    m_LocalIdentifierInFile: local_ms,
                });
                i
            });
            ty.m_ScriptTypeIndex = st;
            n_mb += 1;
        }
        let tid = b.add_type_uncached(ty);
        b.add_object_untyped_with(new_pid, obj.m_ClassID, tid, Cow::Owned(raw))?;
    }

    // 4. AssetBundle container (keep the original lowercase Resources paths) + per-entry preload table.
    let mut ab = AssetBundle::asset_base("silksongresources");
    let mut preload: Vec<PPtr> = Vec::new();
    let mut n_entries = 0usize;
    for (path, pptr) in rm.m_Container.iter() {
        if !keep(pptr.m_PathID) {
            continue;
        }
        let Some(&new_pid) = remap.get(&pptr.m_PathID) else {
            continue;
        }; // skip entries not in resources.assets
        let pre_start = preload.len() as i32;
        preload.push(PPtr::local(new_pid));
        if let Some(&ggma) = mb_to_ggma.get(&new_pid) {
            preload.push(PPtr::local(ms_embed[&ggma]));
        }
        let pre_size = preload.len() as i32 - pre_start;
        ab.m_Container.insert(
            path.clone(),
            AssetInfo {
                preloadIndex: pre_start,
                preloadSize: pre_size,
                asset: PPtr::local(new_pid),
            },
        );
        n_entries += 1;
    }
    ab.m_PreloadTable = preload;
    b.add_object_at(1, &ab)?;

    let mut serialized = Vec::new();
    b.write(Cursor::new(&mut serialized))?;
    let mut bundle = BundleFileBuilder::unityfs(7, &version);
    bundle.add_file("CAB-silksongresources", Cursor::new(serialized))?;
    let mut out = std::fs::File::create(OUT)?;
    bundle.write(&mut out, CompressionType::None)?;

    println!(
        "wrote {OUT}: {} container entries, {} MonoBehaviours, {} embedded monoscripts",
        n_entries,
        n_mb,
        ms_embed.len()
    );
    std::mem::forget(env);
    Ok(())
}
