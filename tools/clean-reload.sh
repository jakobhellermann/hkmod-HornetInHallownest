#!/usr/bin/env bash
# Clean startup + staged log snapshots for HornetPlayer debugging.
#
# Launches Hollow Knight (with the mod), then checkpoints the logs with logsnap at three stages:
#   "loadgame"  — after boot + mod init
#   "loadlevel" — after loading a save slot (HK GameManager.LoadGameFromUI via the mod's /load-save route)
#   "switch"    — after switching control to Hornet (the mod's /switch?who=hornet route)
#
# Opens a fresh logsnap session on the log files at the start (cursors at EOF); logsnap is
# rotation-aware, so it follows Unity truncating Player.log on relaunch.
#
# Usage:  tools/clean-reload.sh [slot]      # slot defaults to 1 ("level 1")
set -eu

GAME_DIR="/home/jakob/.steamapps/Hollow Knight"   # symlink -> ~/.local/share/Steam/steamapps/common/Hollow Knight
GAME_BIN="$GAME_DIR/hollow_knight.x86_64"
LOGDIR="$HOME/.config/unity3d/Team Cherry/Hollow Knight"
SLOT="${1:-1}"
DEV="localhost:8201"

# 0. Open a fresh logsnap session (cursors at EOF) and kill any running instance before a fresh launch.
rm "$LOGDIR/Player.log" "$LOGDIR/ModLog.txt"
logsnap open "$LOGDIR/Player.log" "$LOGDIR/ModLog.txt"
if pkill -f hollow_knight.x86_64 2>/dev/null; then
    echo "killed running Hollow Knight"
    sleep 1   # let it release the window / log file before relaunch
fi

dotnet build

# 1. Launch detached so the game survives this script exiting.
cd "$GAME_DIR" || { echo "game dir not found: $GAME_DIR" >&2; exit 1; }
setsid "$GAME_BIN" >/dev/null 2>&1 < /dev/null &
echo "launched Hollow Knight (pid $!)"

# 2. Wait for boot + mod init. The mod's debug server only starts listening at the END of Initialize, so
#    "server up" IS the boot+init-done signal (replaces a fixed sleep). Then checkpoint the game-load logs,
#    settling briefly to catch any straggler init lines.
echo -n "waiting for debug server"
for _ in $(seq 1 60); do
    if curl -sf --max-time 1 "$DEV/routes" >/dev/null 2>&1; then echo " up"; break; fi
    echo -n "."
    sleep 1
done
logsnap commit -m "loadgame" --settle 200ms

# 3. Load the save.
echo "loading save slot $SLOT ..."
curl -s -X POST "$DEV/load-save?slot=$SLOT" -d '' >/dev/null && echo

# 4. Checkpoint once the scene has loaded AND Hornet has spawned: wait for the spawn marker, then for the
#    follow-up burst (HUD bring-up etc.) to go quiet. Replaces a fixed sleep; --at-most bounds the wait if
#    auto-spawn doesn't fire (e.g. already spawned) so the script still finishes.
logsnap commit -m "loadlevel" --wait-for "[SpawnReal] instantiated" --settle 200ms --at-most 10s

# 5. Switch control to Hornet, then checkpoint the switch's log burst (HeroSwitch + HUD retarget + adapter
#    taking over Hornet) once it settles.
echo "switching to Hornet ..."
curl -s -X POST "$DEV/switch?who=hornet" -d '' >/dev/null && echo
logsnap commit -m "switch" --settle 200ms

echo "done — checkpoints: loadgame, loadlevel, switch"
