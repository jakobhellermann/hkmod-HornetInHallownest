#!/usr/bin/env bash
# Uninstall the persistent install (unity dlls, silksong prefixed dlls, ScriptingAssemblies.json)
set -euo pipefail

if [ "$(uname)" = Darwin ]; then
    DATA="$HOME/Library/Application Support/Steam/steamapps/common/Hollow Knight/hollow_knight.app/Contents/Resources/Data"
else
    DATA="$HOME/.local/share/Steam/steamapps/common/Hollow Knight/hollow_knight_Data"
fi
MGR="$DATA/Managed"
JSON="$DATA/ScriptingAssemblies.json"

# The Unity-package deps SilksongSetup copies from the Silksong install (mirror of SilksongSetup.deps).
deps=(
    Unity.Addressables Unity.ResourceManager Unity.Profiling.Core Unity.Mathematics Unity.Burst
    Newtonsoft.Json.UnityConverters TeamCherry.Splines Coffee.SoftMaskForUGUI
)

removed=0
for name in "${deps[@]}" ; do
    p="$MGR/$name.dll"
    [ -f "$p" ] && { rm -f "$p"; removed=$((removed + 1)); }
done
for p in "$MGR"/Silksong.*.dll ; do
    [ -e "$p" ] || continue
    rm -f "$p"; removed=$((removed + 1))
done
echo "removed $removed support assemblies from Managed"

# Restore ScriptingAssemblies.json from the backup SilksongSetup wrote before registering our assemblies.
if [ -f "$JSON.hornetbak" ]; then
    mv "$JSON.hornetbak" "$JSON"
    echo "restored $(basename "$JSON")"
else
    echo "no $(basename "$JSON").hornetbak to restore" >&2
fi

echo "done"
