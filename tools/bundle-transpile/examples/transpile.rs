use std::path::Path;

use rabex::UnityVersion;
use rabex::typetree::typetree_cache::sync::TypeTreeCache;
use rabex_env::Environment;

use anyhow::Result;

fn main() -> Result<()> {
    let tpk = TypeTreeCache::embedded();
    let env = Environment::new_in("/home/jakob/.steamapps/Hollow Knight Silksong", tpk)?;
    let version = env.unity_version()?.clone();
    let to_version: UnityVersion = "2020.2.2f1".parse().unwrap();

    let path = Path::new(
        "/home/jakob/.local/share/Steam/steamapps/common/Hollow Knight Silksong/Hollow Knight Silksong_Data/StreamingAssets/aa/StandaloneLinux64/herodynamic_assets_all.bundle",
    );

    let out = bundle_transpile::transpile::transpile(&path, &version, &to_version)?;

    std::fs::write("/tmp/out.bundle", out)?;

    Ok(())
}
