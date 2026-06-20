#!/usr/bin/env bash
# Measure the HUD bring-up's contribution to Player.log across a hot-reload.
#
# Why a rebuild (not just /despawn-real + /spawn-real): the transient "missing script" warnings fire only when the HUD
# rig is FIRST activated. DespawnReal destroys only the hero, not the rig/HUD (Cleanup runs in Unload). A `dotnet build`
# hot-reload (Unload -> GameCamerasBootstrap.Cleanup destroys the rig -> Initialize -> auto-spawn -> SpawnReal ->
# BringUpHud re-activates a FRESH HUD) reliably reproduces them. So: record the log offset, rebuild, wait for the
# auto-spawn, then print only the new error/warning lines, grouped + address-normalized.
#
# Usage: tools/check-hud-errors.sh   (run from anywhere; must be in a gameplay scene so auto-spawn fires)
set -u
PL="$HOME/.config/unity3d/Team Cherry/Hollow Knight/Player.log"
CSPROJ="/home/jakob/dev/hk/mods/HornetPlayer/Source/HornetPlayer.csproj"
WAIT="${1:-6}"   # seconds to wait for hot-reload + auto-spawn (override: check-hud-errors.sh 8)

off=$(wc -l < "$PL")
echo "[check] Player.log offset = $off; building (hot-reload)…"
dotnet build "$CSPROJ" 2>&1 | grep -E 'Build succeeded|: error' | tail -1
echo "[check] waiting ${WAIT}s for auto-spawn + BringUpHud…"
sleep "$WAIT"

new=$(wc -l < "$PL")
echo "[check] new lines: $((new - off))"
echo "=== error/warning delta (grouped, 0xADDR/<GUID> normalized) ==="
tail -n +$((off + 1)) "$PL" \
  | grep -iE 'missing!|NullReference|Invalid layer|button skins|Exception|tilemap| at [A-Z]' \
  | sed -E 's/0x[0-9a-f]+/0xADDR/g; s/<[0-9a-f]{32}>/<GUID>/g' \
  | sort | uniq -c | sort -rn
echo "=== raw missing-script lines (with any preceding context) ==="
tail -n +$((off + 1)) "$PL" | grep -nE 'referenced script' | head
