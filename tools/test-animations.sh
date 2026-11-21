#!/bin/sh
# Usage: [SPEED=2] tools/test-animations.sh [wf-recorder args, e.g. -o DP-1]   # SPEED divides the waits
speed="${SPEED:-1}"
recording="$HOME/Videos/test-animations-$(date +%Y-%m-%d_%H-%M-%S).mp4"
# On niri, record the output showing the game.
if [ $# -eq 0 ] && command -v niri >/dev/null; then
	set -- -o "$(niri msg --json windows | jq -r --argjson ws "$(niri msg --json workspaces)" \
		'.[] | select(.app_id // "" | startswith("hollow_knight")) | .workspace_id as $id | $ws[] | select(.id == $id) | .output')"
fi

record() {
	# Tagged limited-range BT.709: wf-recorder's untagged full-range default plays back washed out.
	wf-recorder -c libx264 -x yuv420p -p crf=30 -p preset=fast -r 30 \
		-p colorspace=bt709 -p color_primaries=bt709 -p color_trc=bt709 -p color_range=tv \
		-F 'scale=1280:-2:out_color_matrix=bt709:out_range=tv' "$@" -f "$recording" >/dev/null 2>&1 &
	recorder=$!
	sleep 1
}

play() {
	clip=$(printf %s "$1" | sed 's/ /%20/g')
	curl -s -d '' "localhost:8201/popup?text=$clip" >/dev/null
	curl -s -d '' "localhost:8201/play-clip?name=$clip" >/dev/null
	sleep "$(awk "BEGIN { print $2 / $speed }")"
}

play 'Idle' 1
record "$@"
play 'Idle' 1
play 'LookUp' 1
play 'Look Up Half' 1
play 'LookDown Slight' 1
play 'LookDown' 1
play 'Idle' 1

play 'Look Up Talk' 1
play 'Look Up Half Talk' 1
play 'Talking Standard' 1
play 'Look Down Talk' 1
play 'Idle' 1
play 'Idle Slap' 1
play 'Idle' 1

curl -s -d '' 'localhost:8201/popup-clear' >/dev/null
kill -INT "$recorder"
wait "$recorder"
echo "recorded to $recording"
