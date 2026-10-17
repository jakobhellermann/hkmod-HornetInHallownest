#!/usr/bin/env bash
# Uninstall the persistent install (unity dlls, silksong prefixed dlls, ScriptingAssemblies.json)
set -euo pipefail

# Non-Steam installs (GOG/Wine/itch) are not discoverable, so allow an explicit override.
if [ -n "${HK_DATA:-}" ]; then
    DATA="$HK_DATA"
elif [ "$(uname)" = Darwin ]; then
    DATA="$HOME/Library/Application Support/Steam/steamapps/common/Hollow Knight/hollow_knight.app/Contents/Resources/Data"
else
    DATA="$HOME/.local/share/Steam/steamapps/common/Hollow Knight/hollow_knight_Data"
fi
MGR="$DATA/Managed"
JSON="$DATA/ScriptingAssemblies.json"

# The deps SilksongSetup copies from the Silksong install (mirror of SilksongAssemblies).
deps=(
    Unity.Addressables Unity.ResourceManager Unity.Profiling.Core Unity.Mathematics Unity.Burst Unity.Burst.Unsafe
    Newtonsoft.Json.UnityConverters TeamCherry.Splines Coffee.SoftMaskForUGUI
)
# HK 1.5.12620 ships these itself; only HK 1.5.78 (no TeamCherry.Localization of its own) got ours.
if [ ! -f "$MGR/TeamCherry.Localization.dll" ]; then
    deps+=(
        TeamCherry.SharedUtils TeamCherry.Cinematics TeamCherry.BuildBot Unity.InputSystem com.rlabrecque.steamworks.net
        SharpDX SharpDX.DirectInput XGamingRuntime # only from Windows Silksong builds
        HornetInHallownest.UnityCompat
    )
fi

removed=0
for name in "${deps[@]}" ; do
    p="$MGR/$name.dll"
    [ -f "$p" ] && { rm -f "$p"; removed=$((removed + 1)); }
done
for p in "$MGR"/Silksong.*.dll ; do
    [ -e "$p" ] || continue
    rm -f "$p"; removed=$((removed + 1))
done
rm -f "$MGR/Silksong.support.fingerprint"
echo "removed $removed support assemblies from Managed"

# Restore ScriptingAssemblies.json from the backup SilksongSetup wrote before registering our assemblies.
if [ -f "$JSON.hornetbak" ]; then
    mv "$JSON.hornetbak" "$JSON"
    echo "restored $(basename "$JSON")"
else
    echo "no $(basename "$JSON").hornetbak to restore" >&2
fi

echo "done"
