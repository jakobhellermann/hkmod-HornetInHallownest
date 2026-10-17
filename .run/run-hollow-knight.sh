#!/usr/bin/env bash
set -euo pipefail

if [ "$(uname)" = Darwin ]; then
    macos="$HOME/Library/Application Support/Steam/steamapps/common/Hollow Knight/hollow_knight.app/Contents/MacOS"
    export SteamAppId=367520 SteamGameId=367520
    cd "$macos"
    exec "$macos/Hollow Knight"
else
    # cd so that steam_appid.txt is respected
    dir="${HK_GAME_DIR:-$HOME/.local/share/Steam/steamapps/common/Hollow Knight}"
    cd "$dir"
    # An empty DISPLAY makes Unity run natively on Wayland; players older than Unity 2022.2 can't and need X11.
    if grep -q -a wl_display_connect "$dir/UnityPlayer.so"; then
        export DISPLAY=
    fi
    if [ -x "$dir/run.sh" ]; then
        exec "$dir/run.sh"
    else
        exec "$dir/hollow_knight.x86_64"
    fi
fi
