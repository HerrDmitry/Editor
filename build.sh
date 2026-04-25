#!/usr/bin/env bash
#
# build.sh — Build and package EditorApp for Windows, macOS, and Linux.
#
# Usage:
#   ./build.sh              Build for all platforms
#   ./build.sh win-x64      Build for Windows only
#   ./build.sh osx-x64      Build for macOS only
#   ./build.sh linux-x64    Build for Linux only
#
# Prerequisites:
#   - .NET 10 SDK
#   - Node.js (for TypeScript compilation via tsc.js)
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CSPROJ_DIR="$SCRIPT_DIR/src/EditorApp"
OUTPUT_DIR="$SCRIPT_DIR/publish"

ALL_RIDS=("win-x64" "osx-x64" "linux-x64")

# If specific RIDs were passed as arguments, use those; otherwise build all.
if [ $# -gt 0 ]; then
  RIDS=("$@")
else
  RIDS=("${ALL_RIDS[@]}")
fi

echo "==> EditorApp build script"
echo "    Targets: ${RIDS[*]}"
echo ""

# ── Step 1: Publish .NET application for each target platform ───────────────
# TypeScript compilation runs automatically via the CompileTypeScript MSBuild
# target in EditorApp.csproj (node scripts/tsc.js -p tsconfig.json).
echo "==> Step 1: Publishing .NET application..."
mkdir -p "$OUTPUT_DIR"

for rid in "${RIDS[@]}"; do
  echo "    Publishing for $rid..."
  dotnet publish "$CSPROJ_DIR/EditorApp.csproj" \
    --configuration Release \
    --runtime "$rid" \
    --output "$OUTPUT_DIR/$rid" \
    --self-contained true \
    /p:PublishSingleFile=true \
    /p:IncludeNativeLibrariesForSelfExtract=true
  echo "    ✓ $rid → $OUTPUT_DIR/$rid/"
done
echo ""

# ── Step 2: Package executables ─────────────────────────────────────────────
echo "==> Step 2: Packaging executables..."
PACKAGE_DIR="$OUTPUT_DIR/packages"
mkdir -p "$PACKAGE_DIR"

for rid in "${RIDS[@]}"; do
  case "$rid" in
    win-*)
      EXECUTABLE="EditorApp.exe"
      ARCHIVE="EditorApp-$rid.zip"
      (cd "$OUTPUT_DIR/$rid" && zip -q "$PACKAGE_DIR/$ARCHIVE" "$EXECUTABLE")
      ;;
    *)
      EXECUTABLE="EditorApp"
      ARCHIVE="EditorApp-$rid.tar.gz"
      (cd "$OUTPUT_DIR/$rid" && tar -czf "$PACKAGE_DIR/$ARCHIVE" "$EXECUTABLE")
      ;;
  esac
  echo "    ✓ $ARCHIVE"
done
echo ""

echo "==> Build complete!"
echo "    Executables: $OUTPUT_DIR/<rid>/"
echo "    Packages:    $PACKAGE_DIR/"
