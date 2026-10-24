#!/usr/bin/env bash
# Builds mod/1_RegionFileFix/RegionFileFix.dll with Mono's mcs compiler (macOS / Linux).
# Usage: ./build.sh "/path/to/7 Days To Die"
set -euo pipefail

GAME_DIR="${1:?Usage: ./build.sh \"/path/to/7 Days To Die\"}"
MANAGED="$GAME_DIR/7DaysToDie_Data/Managed"
HARMONY="$GAME_DIR/Mods/0_TFP_Harmony/0Harmony.dll"
ROOT="$(cd "$(dirname "$0")" && pwd)"
OUT="$ROOT/mod/1_RegionFileFix/RegionFileFix.dll"

for f in "$MANAGED/Assembly-CSharp.dll" "$HARMONY"; do
  [ -f "$f" ] || { echo "Not found: $f" >&2; exit 1; }
done
command -v mcs >/dev/null || { echo "mcs not found. Install Mono: brew install mono" >&2; exit 1; }

mcs -target:library -nostdlib -out:"$OUT" \
  -r:"$MANAGED/mscorlib.dll" \
  -r:"$MANAGED/System.dll" \
  -r:"$MANAGED/System.Core.dll" \
  -r:"$MANAGED/netstandard.dll" \
  -r:"$MANAGED/Assembly-CSharp.dll" \
  -r:"$MANAGED/LogLibrary.dll" \
  -r:"$MANAGED/UnityEngine.CoreModule.dll" \
  -r:"$HARMONY" \
  "$ROOT/src/RegionFileFix.cs"

echo "Built: $OUT"
