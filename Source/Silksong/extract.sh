#!/usr/bin/env bash
# Reproducibly extract Silksong hero classes from the game assembly via ilspycmd, as near-pristine source.
#
# Strategy for easy re-porting to new game versions: the .cs files here are decompiler output with exactly ONE
# deterministic transform applied — wrapped in `namespace Silksong;` so they don't collide with Hollow Knight's own
# same-named types (HeroController, HeroControllerStates, PlayerData, …). Any behavioural adaptation lives in
# SEPARATE files (partial classes / extension methods under Silksong/Adapt/), never by editing these — so re-running
# this script on a new Assembly-CSharp.dll regenerates clean files and the diff stays tiny.
#
# Usage: ./extract.sh   (run from Source/Silksong/)
set -euo pipefail

DLL="${SS_DLL:-$HOME/.local/share/Steam/steamapps/common/Hollow Knight Silksong/Hollow Knight Silksong_Data/Managed/Assembly-CSharp.dll}"
OUT="$(cd "$(dirname "$0")" && pwd)/Decompiled"

# Types to extract pristine. Grow this list as the locomotion port expands.
TYPES=(
  HeroControllerStates
  HeroControllerConfig
)

mkdir -p "$OUT"
for t in "${TYPES[@]}"; do
  echo "extracting $t"
  # Decompile to stdout, then insert a file-scoped `namespace Silksong;` right after the using block.
  ilspycmd -t "$t" "$DLL" \
    | awk 'BEGIN{done=0}
           /^(public|internal|sealed|abstract|static|partial|\[|class|enum|struct|namespace)/ && !done {print "namespace Silksong;"; print ""; done=1}
           {print}' \
    > "$OUT/$t.cs"
done
echo "done -> $OUT"
