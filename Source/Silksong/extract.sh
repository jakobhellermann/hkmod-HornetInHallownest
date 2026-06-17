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
# Full metadata names (namespace-qualified where needed; ilspycmd -t needs the exact name). Everything is wrapped
# into `namespace Silksong;` regardless of its original namespace — HeroController (also in Silksong) resolves these
# unqualified names in its own namespace first, so the original `using GlobalEnums;` etc. don't matter.
TYPES=(
  HeroControllerStates
  HeroControllerConfig
  HeroController
  # hero-internal helpers + enums referenced by HeroController
  GlobalEnums.HeroLockStates
  GlobalEnums.EnvironmentTypes
  GlobalEnums.DamagePropertyFlags
  CurrencyType
  AttackToolBinding
  SilkSpool
  Downspike
  WallTouchCache
  RunEffects
  TouchGroundResult
  CharacterBumpCheck
  # shared-but-divergent types HeroController uses pervasively and that we'll want for real later (flash FX, RNG).
  # HK has same-named versions but they diverge (e.g. missing nested SpriteFlash.FlashHandle), so we shadow with
  # Silksong's real ones.
  SpriteFlash
  Probability
)
# NOTE: extracting HeroController's combat/tools/quest dependencies (ToolItem, DeliveryQuestItem, NailAttackBase,
# DamageTag, NoiseMaker, …) does NOT converge — they cascade into the tools/quest/inventory/addressables subsystems
# (ToolItemManager, QuestCompletionData, CollectableItem, AsyncOperationHandle<>, …), i.e. half the game. Those are
# unnecessary for a playable Hornet, so they are STUBBED in Adapt/ rather than extracted.

mkdir -p "$OUT"
for t in "${TYPES[@]}"; do
  file="$OUT/${t##*.}.cs" # basename after the last dot
  if [ -f "$file" ] && [ "${FORCE:-0}" != "1" ]; then
    echo "skip $t (exists; FORCE=1 to re-extract)"
    continue
  fi

  src="$(ilspycmd -t "$t" "$DLL" 2>/dev/null)" || { echo "  WARN: failed $t" >&2; continue; }
  [ -n "$src" ] || { echo "  WARN: empty $t" >&2; continue; }
  echo "extracting $t -> ${t##*.}.cs"

  # Types that HK also defines (HeroController, HeroControllerStates, …) decompile WITHOUT a namespace and must be
  # wrapped in `namespace Silksong;` to avoid colliding. Silksong-only types in their own namespace (GlobalEnums.*)
  # decompile WITH it — leave those pristine; HeroController's `using GlobalEnums;` resolves them.
  if printf '%s\n' "$src" | grep -qE '^namespace '; then
    printf '%s\n' "$src" > "$file"
  else
    printf '%s\n' "$src" \
      | awk 'BEGIN{d=0}
             /^(public|internal|sealed|abstract|static|partial|\[)/ && !d {print "namespace Silksong;"; print ""; d=1}
             {print}' > "$file"
  fi
done
echo "done -> $OUT"
