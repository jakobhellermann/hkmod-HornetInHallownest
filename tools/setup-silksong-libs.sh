#!/usr/bin/env bash
# Populate Source/lib/ for the B2 approach: Silksong's hero-bearing managed code, made loadable inside Hollow Knight.
#  - Assembly-CSharp + Assembly-CSharp-firstpass: IL-prefixed to `Silksong.*` (they collide with HK's same-named ones).
#  - Every other managed assembly Silksong has but HK lacks (Unity packages: Addressables/ResourceManager/Burst/
#    Collections/Mathematics/Timeline/… + Coffee.SoftMaskForUGUI, TeamCherry.Splines, Newtonsoft.Json.UnityConverters):
#    copied verbatim (HK lacks them, so no collision; they're engine-agnostic and load fine).
# Shared assemblies (UnityEngine*, TeamCherry.TK2D/PlayMaker/Newtonsoft, System/Mono) are NOT copied — Silksong.* binds
# to HK's at runtime (and tk2d MUST stay shared so the ported HeroController and the asset-bundle agree on tk2d types).
#
# Re-run after a Silksong update to regenerate. Output (Source/lib/*.dll) is gitignored.
set -euo pipefail

SS="${SS_MANAGED:-$HOME/.local/share/Steam/steamapps/common/Hollow Knight Silksong/Hollow Knight Silksong_Data/Managed}"
HK="${HK_MANAGED:-$HOME/.local/share/Steam/steamapps/common/Hollow Knight/hollow_knight_Data/Managed}"
HERE="$(cd "$(dirname "$0")" && pwd)"
LIB="$HERE/../Source/lib"
mkdir -p "$LIB"
rm -f "$LIB"/*.dll

echo "== prefixing Assembly-CSharp + firstpass -> Silksong.* =="
dotnet run -c Release --project "$HERE/SilksongPrefixer" -- \
  Silksong "$LIB" --managed "$SS" \
  "$SS/Assembly-CSharp.dll" "$SS/Assembly-CSharp-firstpass.dll"

echo "== copying Silksong-only / Unity-package deps HK lacks =="
n=0
for f in "$SS"/*.dll; do
  b="$(basename "$f")"
  case "$b" in
    System*|Mono.*|mscorlib*|netstandard*|UnityEngine*|Assembly-CSharp.dll|Assembly-CSharp-firstpass.dll) continue;;
  esac
  [ -f "$HK/$b" ] && continue   # HK already has it (shared) -> bind to HK's
  cp "$f" "$LIB/"; n=$((n+1))
done
echo "copied $n deps -> $LIB"
ls "$LIB"
