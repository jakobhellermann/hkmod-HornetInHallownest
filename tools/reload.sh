#!/usr/bin/env bash
# Hot-reload the mod and checkpoint the reload's log burst with logsnap.
#
#   1. Commit any pending log lines so the reload burst is isolated ("nothing to commit" is fine).
#   2. dotnet build -> the csproj copies the DLL into Managed/Mods/HornetPlayer/ -> the modding API hot-reloads
#      (Unload -> Initialize -> auto-respawn Hornet).
#   3. Wait for the reload to finish (the [SpawnReal] instantiated marker) + settle, then checkpoint.
#
# Assumes a logsnap session is already open on the logs (run tools/clean-startup.sh first). Iterate with this
# between code edits instead of restarting.
set -u

SRC="/home/jakob/dev/hk/mods/HornetPlayer/Source"
BUILDLOG=/tmp/reload-build.log

# 1. Isolate the reload: checkpoint everything logged so far (no-op if nothing new).
logsnap commit -m "pre-reload"

# 2. Build + deploy. Tee to a file (don't pipe a long build straight to grep); only surface errors on failure.
echo "building..."
if ! dotnet build "$SRC/HornetPlayer.csproj" >"$BUILDLOG" 2>&1; then
    echo "build FAILED:"
    grep -E "error|Error\(s\)" "$BUILDLOG" | head
    exit 1
fi
echo "build ok — waiting for hot-reload + respawn"

# 3. Block until the hot-reload re-spawns Hornet, then until the follow-up burst goes quiet, then checkpoint.
#    --at-most bounds it (e.g. reloading at the menu won't auto-respawn -> the wait gives up and the script ends).
logsnap commit -m "reload" --wait-for "[SpawnReal] instantiated" --settle 200ms --at-most 60s
