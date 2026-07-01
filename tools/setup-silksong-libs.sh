#!/usr/bin/env bash
# Populate Source/lib/ for the B2 approach: Silksong's hero-bearing managed code, made loadable inside Hollow Knight.
#  - Assembly-CSharp + Assembly-CSharp-firstpass: IL-prefixed to `Silksong.*` (they collide with HK's same-named ones).
#  - Every other managed assembly Silksong has but HK lacks (Unity packages: Addressables/ResourceManager/Burst/
#    Collections/Mathematics/Timeline/… + Coffee.SoftMaskForUGUI, TeamCherry.Splines, Newtonsoft.Json.UnityConverters):
#    copied verbatim (HK lacks them, so no collision; they're engine-agnostic and load fine).
# Shared assemblies (UnityEngine*, TeamCherry.TK2D/Newtonsoft, System/Mono) are NOT copied — Silksong.* binds to HK's
# at runtime (and tk2d MUST stay shared so the ported HeroController and the asset-bundle agree on tk2d types).
#  - PlayMaker is the exception: it is PREFIXED (Silksong.PlayMaker), NOT shared. HK and Silksong ship different
#    PlayMaker versions and a shared one means a single global action-type lookup serving both games -> name
#    collisions break each other's FSMs. Prefixing gives Silksong its own isolated PlayMaker runtime.
#
# Re-run after a Silksong update to regenerate. Output (Source/lib/*.dll) is gitignored.
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
# TeamCherry.NestedFadeGroup / TeamCherry.Localization / ConditionalExpression define PlayMaker actions
# (FadeNestedFadeGroup, GetLocalisedString, ConditionalExpression). They must be prefixed so their actions derive
# from Silksong.PlayMaker (else Hornet's isolated FSMs can't instantiate them -> "Could Not Create Action").
# TeamCherry.SharedUtils exposes PlayMaker-typed helpers (Extensions.GetSafe(FsmOwnerDefault, FsmStateAction), …) that
# Hornet's actions call. Bound to HK's PlayMaker (HK ships it too) the overload's param types don't match Silksong's
# PlayMaker -> MissingMethodException at the call site. Prefixing rewrites its PlayMaker refs to Silksong.PlayMaker so
# the Silksong-typed overload exists (and the call sites resolve to Silksong.TeamCherrySharedUtils).
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
    System*|Mono.*|mscorlib*|netstandard*|UnityEngine*|Assembly-CSharp.dll|Assembly-CSharp-firstpass.dll) continue;;
  esac
  [ -f "$HK/$b" ] && continue   # HK already has it (shared) -> bind to HK's
  cp "$f" "$LIB/"; n=$((n+1))
done
echo "copied $n deps -> $LIB"
ls "$LIB"
