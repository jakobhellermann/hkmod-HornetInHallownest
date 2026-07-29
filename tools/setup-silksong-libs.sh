#!/usr/bin/env bash
# Generate Source/lib/Silksong.$assembly.dll for the set of assemblies we load from silksong.
set -euo pipefail

STEAM="$HOME/.local/share/Steam/steamapps/common"
SS_DEFAULT="$STEAM/Hollow Knight Silksong/Hollow Knight Silksong_Data/Managed"
HK_DEFAULT="$STEAM/Hollow Knight/hollow_knight_Data/Managed"
if [ "$(uname)" = "Darwin" ]; then
    STEAM="$HOME/Library/Application Support/Steam/steamapps/common"
    SS_DEFAULT="$STEAM/Hollow Knight Silksong/Hollow Knight Silksong.app/Contents/Resources/Data/Managed"
    HK_DEFAULT="$STEAM/Hollow Knight/hollow_knight.app/Contents/Resources/Data/Managed"
fi
SS="${SS_MANAGED:-$SS_DEFAULT}"
HK="${HK_MANAGED:-$HK_DEFAULT}"
HERE="$(cd "$(dirname "$0")" && pwd)"
LIB="$HERE/../Source/lib"
mkdir -p "$LIB"
rm -f "$LIB"/*.dll

echo "== prefixing Assembly-CSharp + firstpass + PlayMaker + TeamCherry action assemblies -> Silksong.* =="
# TeamCherry.NestedFadeGroup / TeamCherry.Localization / ConditionalExpression: contain PlayMaker actions
# TeamCherry.SharedUtils: has PlayMaker helpers
dotnet run -c Release --project "$HERE/SilksongPrefixer" -- \
    Silksong "$LIB" --managed "$SS" \
    "$SS/Assembly-CSharp.dll" "$SS/Assembly-CSharp-firstpass.dll" "$SS/PlayMaker.dll" \
    "$SS/TeamCherry.NestedFadeGroup.dll" "$SS/TeamCherry.Localization.dll" "$SS/ConditionalExpression.dll" \
    "$SS/TeamCherry.SharedUtils.dll"

echo "== copying Silksong-only / Unity-package deps HK lacks =="
n=0
for f in "$SS"/*.dll; do
    b="$(basename "$f")"
    case "$b" in
    System* | Mono.* | mscorlib* | netstandard* | UnityEngine* | Assembly-CSharp.dll | Assembly-CSharp-firstpass.dll) continue ;;
    esac
    [ -f "$HK/$b" ] && continue # HK already has it (shared) -> bind to HK's
    cp "$f" "$LIB/"
    n=$((n + 1))
done
echo "copied $n deps -> $LIB"
ls "$LIB"
