//! Shared helpers for the asset-pipeline binaries. Vendored from rabex-env's `examples/utils` (which isn't part of
//! rabex-env's published lib), so both bins can locate the installed Silksong via Steam.
#![allow(dead_code)]

use anyhow::Result;
use rabex::tpk::TpkTypeTreeBlob;
use rabex::typetree::typetree_cache::sync::TypeTreeCache;
use rabex_env::Environment;
use rabex_env::resolver::GameFiles;

pub fn for_each_steam_game(mut f: impl FnMut(Environment) -> Result<()>) -> Result<()> {
    let steam = steamlocate::SteamDir::locate()?;
    for lib in steam.libraries()? {
        let lib = lib?;
        for app in lib.apps() {
            let app = app?;
            let path = lib.resolve_app_dir(&app);

            let Ok(game_files) = GameFiles::probe(path) else {
                continue;
            };

            // PERF: reuse
            let tpk = TypeTreeCache::new(TpkTypeTreeBlob::embedded());
            let env = Environment::new(game_files, tpk);
            f(env)?;
        }
    }
    Ok(())
}

fn search_transform(input: &str) -> String {
    input.to_ascii_lowercase().replace(char::is_whitespace, "")
}

pub fn find_game(name_filter: &str) -> Result<Option<Environment>> {
    // Non-Steam installs (GOG/Wine/itch) aren't discoverable via steamlocate. Allow an explicit override:
    //   SILKSONG_PATH=/path/to/'Hollow Knight Silksong'  (the dir containing *_Data), or HK_PATH for hollow knight.
    let override_var = if search_transform(name_filter).contains("silksong") {
        "SILKSONG_PATH"
    } else {
        "HK_PATH"
    };
    if let Ok(dir) = std::env::var(override_var) {
        let game_files = GameFiles::probe(std::path::Path::new(&dir))?;
        let tpk = TypeTreeCache::new(TpkTypeTreeBlob::embedded());
        return Ok(Some(Environment::new(game_files, tpk)));
    }

    let name_filter = search_transform(name_filter);

    let steam = steamlocate::SteamDir::locate()?;

    let mut candidates = Vec::new();

    for lib in steam.libraries()? {
        let lib = lib?;
        for app in lib.apps() {
            let app = app?;

            let name = app.name.as_ref().unwrap_or(&app.install_dir);
            let name = search_transform(name);

            let matches = name.contains(&name_filter);

            if matches {
                let score = name.len() - name_filter.len();
                let path = lib.resolve_app_dir(&app);
                candidates.push((path, score));
            }
        }
    }

    candidates.sort_by_key(|&(_, score)| score);
    let path = match candidates.as_slice() {
        [(best, _), ..] => best,
        _ => return Ok(None),
    };

    let game_files = GameFiles::probe(path)?;

    let tpk = TypeTreeCache::new(TpkTypeTreeBlob::embedded());
    let env = Environment::new(game_files, tpk);

    Ok(Some(env))
}

/// Engine version the bundles we emit should DECLARE in their UnityFS header.
///
/// Unity applies two independent gates when loading a bundle, in this order:
///   1. archive/container version - Silksong ships UnityFS v8; HK 1.5.78 (Unity 2020.2.2f1) reads at most v7
///      ("Unable to read header from archive file").
///   2. an exact engine-version check - a v7 container that still declares `6000.0.50f1` is refused with
///      "can't be loaded because it was not built with the right version or build target".
///
/// Both were confirmed empirically against HK 1.5.78.11833. Declaring the HOST's version clears gate 2, and the
/// Unity 6 payload behind it deserializes fine (verified: GameObject/Material/Texture2D out of a real Hornet
/// collections bundle). Unset -> keep the source version, which is what the Unity 6 HK build wants.
pub fn target_unity_version(source: &rabex::UnityVersion) -> rabex::UnityVersion {
    match std::env::var("TARGET_UNITY_VERSION") {
        Ok(v) if !v.trim().is_empty() => v.trim().parse().expect("invalid TARGET_UNITY_VERSION"),
        _ => source.clone(),
    }
}
