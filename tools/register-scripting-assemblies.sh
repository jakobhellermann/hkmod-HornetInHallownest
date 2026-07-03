#!/usr/bin/env bash
# One-time-per-install: register the IL-prefixed Silksong.* assemblies in HK's ScriptingAssemblies.json.
#
# Unity only resolves nested custom-[Serializable] / MonoBehaviour types from assemblies listed here (loaded from the
# Managed root at startup). Without this, nested fields on the Hero_Hornet prefab (HeroController.configs, ToolItem's
# alternateUnlockedTest, cState, …) deserialize to NULL -> NullReferenceExceptions during spawn. The prefixed DLLs are
# already deployed to the Managed root by the build (CopyMod's B2Lib copy); this only edits the JSON. See
# docs/experiment-scriptingassemblies.md. Idempotent; keeps a one-time .bak of the original.
set -euo pipefail

if [ "$(uname)" = Darwin ]; then
    DATA="$HOME/Library/Application Support/Steam/steamapps/common/Hollow Knight/hollow_knight.app/Contents/Resources/Data"
else
    DATA="$HOME/.local/share/Steam/steamapps/common/Hollow Knight/hollow_knight_Data"
fi
JSON="$DATA/ScriptingAssemblies.json"
MGR="$DATA/Managed"
[ -f "$JSON" ] || { echo "not found: $JSON" >&2; exit 1; }

names=()
for f in "$MGR"/Silksong.*.dll; do names+=("$(basename "$f")"); done
[ "${#names[@]}" -gt 0 ] || { echo "no Silksong.*.dll in $MGR (build first)" >&2; exit 1; }

[ -f "$JSON.bak" ] || { cp "$JSON" "$JSON.bak"; echo "backed up -> $JSON.bak"; }

tmp="$(mktemp)"
jq --args '
  reduce $ARGS.positional[] as $n (.;
    if (.names | index($n)) then . else .names += [$n] | .types += [16] end)
' "${names[@]}" < "$JSON" > "$tmp"
mv "$tmp" "$JSON"

echo "registered (type flag 16):"
jq -r '.names[] | select(startswith("Silksong."))' "$JSON" | sed 's/^/  /'
