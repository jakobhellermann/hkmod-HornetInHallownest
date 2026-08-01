#!/usr/bin/env bash
set -euo pipefail

if [ "$(uname)" = Darwin ]; then
    macos="$HOME/Library/Application Support/Steam/steamapps/common/Hollow Knight/hollow_knight.app/Contents/MacOS"
    export SteamAppId=367520 SteamGameId=367520
    cd "$macos"
    exec "$macos/Hollow Knight"
else
    # cd so that steam_appid.txt is respected
    dir="$HOME/.local/share/Steam/steamapps/common/Hollow Knight"
    cd "$dir"
    if [ -x "$dir/run.sh" ]; then
        exec env DISPLAY= "$dir/run.sh"
    else
        exec env DISPLAY= "$dir/hollow_knight.x86_64"
    fi
fi
