# HornetPlayer build/asset pipeline.
# Regenerates the prefixed Silksong libs + remapped bundles the mod loads. See CLAUDE.md "build pipeline".

unity := env_var('HOME') / "dev/unity"
ss := "silksong"
lib := justfile_directory() / "Source/lib"

# Prefix Silksong's Assembly-CSharp/firstpass/PlayMaker -> Silksong.* into Source/lib/.
setup-libs:
    bash {{justfile_directory()}}/tools/setup-silksong-libs.sh

# Rebuild the hero MonoScripts bundle, rewriting m_AssemblyName -> Silksong.* (incl. PlayMaker.dll -> Silksong.PlayMaker).
remap-monoscripts:
    cd {{unity}}/rabex-env && cargo run --release --example remap_monoscripts

# Regenerate the Hero_Hornet addressable dependency closure (505 bundles) that reload-all-deps loads before spawn-real.
# Committed so it survives reboots (was a fragile /tmp file before).
hero-deps:
    cd {{unity}}/rabex-env && cargo run --release --example addressable_asset_deps -- Hero_Hornet > {{lib}}/hornet-deps.txt
    @echo "wrote {{lib}}/hornet-deps.txt ($(wc -l < {{lib}}/hornet-deps.txt) bundles)"

# Repack Silksong's _GameCameras rig (camera + HUD) from Menu_Title into a loadable bundle for HK.
# NOTE: Menu_Title is an ADDRESSABLE scene; stock unity-scene-repacker only reads classic levelN files, so it needs
# the addressable-scene path added (see CLAUDE.md). MonoScripts in the output then need remapping to Silksong.* too.
repack-gamecameras:
    cargo run --manifest-path {{unity}}/unity-scene-repacker/Cargo.toml -- \
        --steam-game {{ss}} \
        --scene-objects {{justfile_directory()}}/tools/gamecameras.objects.json \
        --mode asset \
        --bundle-name gamecameras \
        --output {{lib}}/gamecameras.silksong.bundle \
        --compression none
