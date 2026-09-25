//! Debug helper: dump objects of the given class ids from a bundle as JSON, optionally filtered by m_Name substring.
//! usage: dump-objects <bundle> <class-id>[,<class-id>...] [name-filter]
use std::io::Cursor;

use anyhow::Result;
use rabex::UnityVersion;
use rabex::files::SerializedFile;
use rabex::files::bundlefile::ExtractionConfig;
use rabex::tpk::TpkTypeTreeBlob;
use rabex::typetree::typetree_cache::sync::TypeTreeCache;

fn main() -> Result<()> {
    let args: Vec<String> = std::env::args().collect();
    anyhow::ensure!(
        args.len() >= 3,
        "usage: dump-objects <bundle> <class-id,...> [name-filter]"
    );
    let classes: Vec<i32> = args[2]
        .split(',')
        .map(|c| c.parse())
        .collect::<Result<_, _>>()?;
    let filter = args.get(3).map(|s| s.to_lowercase());
    let ss: UnityVersion = "6000.0.50f1".parse().unwrap();
    let config = ExtractionConfig::default().with_fallback_unity_version(ss);
    let bytes = std::fs::read(&args[1])?;
    let reader = rabex::files::bundlefile::reader::BundleFileReader::from_reader(
        Cursor::new(bytes),
        &config,
    )?;
    let tpk = TypeTreeCache::new(TpkTypeTreeBlob::embedded());
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
            if !classes.contains(&info.m_ClassID.0) {
                continue;
            }
            let v: serde_json::Value = match file.read(info, &tpk, &mut cur) {
                Ok(v) => v,
                Err(e) => {
                    eprintln!("{e:?}");
                    continue;
                }
            };
            let name = v
                .get("m_Name")
                .and_then(|n| n.as_str())
                .unwrap_or("")
                .to_lowercase();
            if let Some(f) = &filter {
                if !name.contains(f.as_str()) {
                    continue;
                }
            }
            println!(
                "=== {} class {} pid {}",
                entry.path, info.m_ClassID.0, info.m_PathID
            );
            println!("{}", serde_json::to_string(&v)?);
        }
    }
    Ok(())
}
