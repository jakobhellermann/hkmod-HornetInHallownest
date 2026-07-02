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
