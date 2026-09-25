#!/usr/bin/env bash
# Build Source/lib/ for HOLLOW KNIGHT 1.5.78.11833 (Unity 2020.2.2f1) instead of the Unity 6 HK the mod targets.
#
# The stock pipeline (tools/setup-silksong-libs.sh + the rabex bins) assumes host and Silksong share a Unity
# generation. 1.5.78 is four majors behind Silksong's 6000.0.50f1, which shows up in three places:
#
#   1. MANAGED API   — Silksong's IL calls Unity 6 members 2020.2 lacks. UnityDowngrader rewrites them: pure
#                      renames in place (Rigidbody2D.linearVelocity -> velocity), everything genuinely absent
#                      retargeted onto HornetInHallownest.UnityCompat.
#   2. BUNDLE FORMAT — Silksong ships UnityFS v8; 2020.2 reads at most v7 ("Unable to read header from archive
#                      file"). Emit v7.
#   3. VERSION CHECK — a v7 container still declaring 6000.0.50f1 is refused ("not built with the right version
#                      or build target"). Declare the host's version instead.
#
# All three were confirmed empirically against HK 1.5.78.11833; with them applied a real Hornet collections
# bundle loads and deserializes (GameObject + Material + Texture2D).
#
# Addressables content is NOT handled here — it is 7.5 GB and slow. Run tools/convert-aa separately:
#   cargo run --release --bin convert-aa -- <silksong>/StreamingAssets/aa <out>/StreamingAssets/aa 2020.2.2f1
#
# env: HK_MANAGED, SS_MANAGED (required — no Steam autodetect), TARGET_UNITY_VERSION (default 2020.2.2f1)
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$HERE/.."
LIB="$ROOT/Source/lib"
export TARGET_UNITY_VERSION="${TARGET_UNITY_VERSION:-2020.2.2f1}"

: "${HK_MANAGED:?set HK_MANAGED to Hollow Knight's Managed dir}"
: "${SS_MANAGED:?set SS_MANAGED to Silksong's Managed dir}"
SS_INSTALL="$(cd "$SS_MANAGED/.." && pwd)"          # <...>_Data
SS_INSTALL="$(cd "$SS_INSTALL/.." && pwd)"          # install root, for rabex's GameFiles::probe

# A previous install leaves support assemblies in HK's Managed/. setup-silksong-libs.sh skips copying anything HK
# "already has", so those leftovers make it silently omit real dependencies (Unity.Addressables, Unity.ResourceManager,
# …) from Source/lib — and the mod then ships without them. Refuse to run against a dirty install.
# `|| true`: with `set -o pipefail`, ls exiting non-zero because nothing matched (the GOOD case) would otherwise
# abort the whole script.
leftovers=$( { ls "$HK_MANAGED"/Silksong.*.dll "$HK_MANAGED"/Unity.Addressables.dll "$HK_MANAGED"/Unity.ResourceManager.dll \
                  "$HK_MANAGED"/HornetInHallownest.UnityCompat.dll "$HK_MANAGED"/TeamCherry.TK2D.dll 2>/dev/null || true; } | wc -l | tr -d ' ')
if [ "$leftovers" != "0" ]; then
    echo "ERROR: $leftovers previously-installed support assemblies are still in $HK_MANAGED." >&2
    echo "       Run: HK_DATA=\"\$(dirname \"$HK_MANAGED\")\" bash $HERE/uninstall.sh" >&2
    exit 1
fi

echo "== 1/4 prefix Silksong assemblies -> Silksong.* =="
bash "$HERE/setup-silksong-libs.sh"

# setup-silksong-libs.sh copies every Silksong assembly the host lacks, which includes TeamCherry.TK2D. Shipping it
# would give the process a SECOND tk2d type identity; UnityDowngrader instead rewrites tk2d references to the host's
# Assembly-CSharp (where 1.5.78 compiles tk2d into). Drop the copy so it can never be installed.
rm -f "$LIB/TeamCherry.TK2D.dll"

echo "== 2/4 build + install the Unity 2020.2 compat shim =="
dotnet build -c Release -v q --nologo "$HERE/UnityCompat" -p:HKRefs="$HK_MANAGED"
# TFM-agnostic: the shim targets whatever framework lets it bind to the host's own BCL (net472 + FrameworkPathOverride
# for 1.5.78, since Span<T> lives in Unity's mscorlib and not in the netstandard reference surface).
shim=$(find "$HERE/UnityCompat/bin/Release" -name 'HornetInHallownest.UnityCompat.dll' -print -quit)
[ -n "$shim" ] || { echo "ERROR: compat shim was not produced" >&2; exit 1; }
cp "$shim" "$LIB/"

# NOTE: no tk2d facade. An earlier revision generated a TeamCherry.TK2D type-forwarder here; Unity 2020.2's Mono
# crashes following the first forward (verified in-game), so UnityDowngrader rewrites tk2d type references straight
# to the host's Assembly-CSharp instead. See the hostProviders comment in tools/UnityDowngrader.

echo "== 3/4 downgrade Unity 6 IL -> Unity 2020.2 =="
dotnet build -c Release -v q --nologo "$HERE/UnityDowngrader"
# bash 3.2 (macOS default) has no mapfile; xargs keeps it portable and handles the spaces in these paths.
find "$LIB" -maxdepth 1 -name '*.dll' ! -name 'HornetInHallownest.UnityCompat.dll' -print0 | sort -z \
  | xargs -0 dotnet "$HERE/UnityDowngrader/bin/Release/net8.0/UnityDowngrader.dll" \
      --hk "$HK_MANAGED" --shim "$LIB/HornetInHallownest.UnityCompat.dll"

echo "== 3b/4 validate rewritten metadata =="
# A bad rewrite shows up in-game only as a native Mono assertion at engine level, with no managed stack and no
# indication of which assembly is at fault. Catch it here instead.
dotnet build -c Release -v q --nologo "$HERE/MetadataCheck"
find "$LIB" -maxdepth 1 -name '*.dll' -print0 \
  | xargs -0 dotnet "$HERE/MetadataCheck/bin/Release/net8.0/mdcheck.dll"

echo "== 3c/4 check for API the host cannot provide =="
# Only covers the assemblies actually installed onto the probe path; the rest of Source/lib is shipped but never
# loaded, and several of those legitimately need Unity 6 engine modules that do not exist here.
dotnet build -c Release -v q --nologo "$HERE/ApiGapCheck"
INSTALLED="Unity.Addressables Unity.ResourceManager Unity.Profiling.Core Unity.Mathematics Unity.Burst
           Unity.Burst.Unsafe Newtonsoft.Json.UnityConverters TeamCherry.Splines Coffee.SoftMaskForUGUI
           HornetInHallownest.UnityCompat Silksong.AssemblyCSharp Silksong.AssemblyCSharpfirstpass
           Silksong.PlayMaker Silksong.TeamCherryNestedFadeGroup Silksong.TeamCherryLocalization
           Silksong.TeamCherrySharedUtils Silksong.ConditionalExpression"
set -- ; for n in $INSTALLED; do [ -f "$LIB/$n.dll" ] && set -- "$@" "$LIB/$n.dll"; done
# BurstCompilerService.RuntimeLog: Unity 6's CoreModule has it, 2020.2's does not. Cannot be shimmed (the class
# is internal in the host), and cannot be reached either — it is only called from Burst-compiled code, and this
# build uses the Mono scripting backend where nothing is Burst-compiled.
dotnet "$HERE/ApiGapCheck/bin/Release/net8.0/apigapcheck.dll" --host "$HK_MANAGED" --probe "$LIB" \
  --allow "BurstCompilerService::RuntimeLog" "$@"

echo "== 4/4 rebuild bundles declaring $TARGET_UNITY_VERSION =="
export SILKSONG_PATH="$SS_INSTALL"
( cd "$HERE/asset-pipeline" && cargo run --release --quiet --bin remap-monoscripts )
( cd "$HERE/asset-pipeline" && cargo run --release --quiet --bin repack-resources )

echo "done. Source/lib is now Hollow Knight 1.5.78 compatible."
