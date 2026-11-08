use std::io::Cursor;
use std::path::Path;

use rabex::UnityVersion;
use rabex::files::bundlefile::{BundleFileReader, CompressionType, ExtractionConfig};
use rabex::typetree::typetree_cache::sync::TypeTreeCache;

use anyhow::Result;
use rabex::objects::ClassId;

#[derive(serde::Deserialize)]
#[allow(non_snake_case)]
struct ShaderName {
    m_ParsedForm: ParsedFormName,
}

#[derive(serde::Deserialize)]
#[allow(non_snake_case)]
struct ParsedFormName {
    m_Name: String,
}

pub fn transpile(
    bundle: &Path,
    from_version: &UnityVersion,
    to_version: &UnityVersion,
) -> Result<Vec<u8>> {
    let bundle_name = bundle.file_name().unwrap_or_default().display().to_string();
    let bundle = Cursor::new(std::fs::read(bundle)?);
    let bundle = BundleFileReader::from_reader(
        bundle,
        &ExtractionConfig::default().with_fallback_unity_version(from_version.clone()),
    )?;

    let tpk = TypeTreeCache::embedded();

    let common_strings =
        rabex::files::serializedfile::build_common_offset_map(&tpk.inner, &to_version);

    let out = crate::utils::map_bundle(
        bundle,
        &tpk,
        &common_strings,
        &from_version,
        Some(&to_version),
        |name, rewrite| {
            dbg!(name);
            for info in rewrite.source.objects() {
                if info.m_ClassID == ClassId::Shader {
                    let shader: ShaderName = rewrite.source.read(info)?;
                    eprintln!("[shader] {bundle_name} {name} {} {}", info.m_PathID, shader.m_ParsedForm.m_Name);
                }
            }

            let retarget = rabex_convert::retarget::Retarget {
                to: to_version.clone(),
                typetrees: &tpk,
                shader_chunk_size: 8 << 20,
                common_strings: common_strings.clone(),
                source_version: Some(from_version.clone()),
                scripts: None,
            };
            let report = retarget.apply(rewrite)?;
            dbg!(report);

            Ok(())
        },
    )?
    .unwrap();

    let mut buf = Vec::new();
    out.write(Cursor::new(&mut buf), CompressionType::None)?;

    Ok(buf)
}
