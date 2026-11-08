use std::io::Cursor;

use rabex::UnityVersion;
use rabex::files::bundlefile::{BundleFileBuilder, BundleFileReader};
use rabex::files::serializedfile::CommonOffsetMap;
use rabex::typetree::TypeTreeProvider;
use rabex_convert::rewriter::SerializedFileRewriter;

use anyhow::{Context as _, Result};

pub fn map_bundle<T: AsRef<[u8]>, P: TypeTreeProvider>(
    bundle: BundleFileReader<Cursor<T>>,
    tpk: &P,
    common_strings: &CommonOffsetMap,
    from_version: &UnityVersion,
    to_version: Option<&UnityVersion>,
    mut f: impl FnMut(&str, &mut SerializedFileRewriter<'_, P>) -> Result<()>,
) -> Result<Option<BundleFileBuilder>> {
    let mut files = Vec::new();
    let mut changed = false;
    for entry in bundle.files() {
        let data = bundle.read_at_entry(entry)?;

        let rewritten = match entry.is_serialized() {
            true => {
                let mut rewriter = SerializedFileRewriter::new_in_bundle(
                    &data,
                    tpk,
                    common_strings,
                    Some(&from_version),
                )?;
                f(&entry.path, &mut rewriter).with_context(|| entry.path.clone())?;
                rewriter.finish()?
            }
            false => None,
        };
        changed |= rewritten.is_some();
        files.push((entry, rewritten.unwrap_or(data)));
    }

    if !changed {
        return Ok(None);
    }

    let header = bundle.header();
    let actual_to_version = to_version
        .cloned()
        .or_else(|| header.unity_revision.clone())
        .unwrap_or(from_version.clone());

    /*let container = match &written {
        Some(version) => container_version(version).min(header.version),
        None => header.version,
    };*/
    let mut out =
        BundleFileBuilder::unityfs(actual_to_version.unity_fs_version(), &actual_to_version);
    if to_version.is_none() {
        out = out.without_revision();
    }

    for (entry, data) in files {
        out.add_file_with_flags(&entry.path, &mut data.as_slice(), entry.flags)?;
    }

    Ok(Some(out))
}
