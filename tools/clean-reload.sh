#!/usr/bin/env bash
# Clean startup + staged log snapshots for HornetInHallownest debugging.
#
# Launches Hollow Knight (with the mod) via .run/run-hollow-knight.sh (which handles the OS/Rosetta launch),
# then checkpoints the logs with logsnap at three stages:
#   "loadgame"  — after boot + mod init
#   "loadlevel" — after loading a save slot (HK GameManager.LoadGameFromUI via the mod's /load-save route)
#   "switch"    — after switching control to Hornet (the mod's /switch?who=hornet route)
#
# Opens a fresh logsnap session on the log files at the start (cursors at EOF); logsnap is
# rotation-aware, so it follows Unity truncating Player.log on relaunch.
#
# Usage:  tools/clean-reload.sh [-f <tfm>] [slot]   # slot defaults to 1 ("level 1"); -f picks the game version
#                                                  # (e.g. hk1578) and launches its GamePath from HollowKnightPath.props
set -eu

HERE="$(cd "$(dirname "$0")" && pwd)"
LAUNCHER="$HERE/../.run/run-hollow-knight.sh"
TFM=""
if [ "${1:-}" = "-f" ]; then
    TFM="$2"
    shift 2
fi
SLOT="${1:-1}"
DEV="localhost:8201"

if [ "$(uname)" = Darwin ]; then
    # macOS splits the two logs across different directories.
    PLAYER_LOG="$HOME/Library/Logs/Team Cherry/Hollow Knight/Player.log"
    MODLOG="$HOME/Library/Application Support/unity.Team Cherry.Hollow Knight/ModLog.txt"
else
    LOGDIR="$HOME/.config/unity3d/Team Cherry/Hollow Knight"
    PLAYER_LOG="$LOGDIR/Player.log"
    MODLOG="$LOGDIR/ModLog.txt"
fi

# pidof matches the executable, unlike pkill -f, which also hits shells or editors that mention it.
game_pids() {
    if [ "$(uname)" = Darwin ]; then
        pgrep -f "hollow_knight.app/Contents/MacOS/Hollow Knight"
    else
        pidof hollow_knight.x86_64
    fi
}

# Returns once the game has exited: a relaunch while it is still shutting down fails to start.
kill_game() {
    pids=$(game_pids) || return 1
    kill $pids
    for _ in $(seq 1 100); do
        game_pids >/dev/null || return 0
        sleep 0.1
    done
    echo "Hollow Knight (pid $pids) did not exit" >&2
    exit 1
}

launch_game() {
    # nohup: detached, so the game survives this script exiting (portable across macOS/Linux).
    nohup "$LAUNCHER" >/dev/null 2>&1 < /dev/null &
    echo "launched Hollow Knight (pid $!)"
}

# 0. Open a fresh logsnap session (cursors at EOF) and kill any running instance before a fresh launch.
rm -f "$PLAYER_LOG" "$MODLOG"
logsnap open "$PLAYER_LOG" "$MODLOG"
if kill_game; then
    echo "killed running Hollow Knight"
fi

if [ -n "$TFM" ]; then
    dotnet build "$HERE/../Source" -f "$TFM"
    HK_GAME_DIR="$(dotnet msbuild "$HERE/../Source" -p:TargetFramework="$TFM" -getProperty:GamePath)"
    export HK_GAME_DIR
else
    dotnet build
fi

# 1. Launch.
launch_game

# 2. Wait for boot + mod init. The mod's debug server only starts listening at the END of Initialize, so
#    "server up" IS the boot+init-done signal (replaces a fixed sleep). A (re)install of the Silksong assemblies
#    stops init and asks for a restart instead; relaunch once then. Afterwards checkpoint the game-load logs,
#    settling briefly to catch any straggler init lines.
echo -n "waiting for debug server"
relaunched=false
for _ in $(seq 1 90); do
    if curl -sf --max-time 1 "$DEV/routes" >/dev/null 2>&1; then echo " up"; break; fi
    if ! $relaunched && grep -q "Please restart Hollow Knight" "$MODLOG" 2>/dev/null; then
        echo " installed Silksong assemblies, restarting"
        kill_game
        launch_game
        relaunched=true
    fi
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
logsnap commit -m "loadlevel" --wait-for "[HornetSpawner] instantiated" --settle 200ms --at-most 60s

# 5. Switch control to Hornet, then checkpoint the switch's log burst (HeroSwitch + HUD retarget + adapter
#    taking over Hornet) once it settles.
echo "switching to Hornet ..."
curl -s -X POST "$DEV/switch?who=hornet" -d '' >/dev/null && echo
logsnap commit -m "switch" --settle 200ms

echo "done — checkpoints: loadgame, loadlevel, switch"
