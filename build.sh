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
#   - Node.js and npm
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
FRONTEND_DIR="$SCRIPT_DIR/frontend"
CSPROJ_DIR="$SCRIPT_DIR/src/EditorApp"
WWWROOT_DIR="$CSPROJ_DIR/wwwroot"
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

# ── Step 1: Build the React frontend ────────────────────────────────────────
echo "==> Step 1: Building React frontend..."
(
  cd "$FRONTEND_DIR"
  npm ci --silent
  npm run build
)
echo "    React build complete: $FRONTEND_DIR/dist/"
echo ""

# ── Step 2: Copy React bundle into C# wwwroot ──────────────────────────────
echo "==> Step 2: Copying React bundle to wwwroot..."
mkdir -p "$WWWROOT_DIR/assets"
cp -r "$FRONTEND_DIR/dist/assets/"* "$WWWROOT_DIR/assets/"
# Copy the Vite-generated index.html as a reference (App.razor is the actual host page)
cp "$FRONTEND_DIR/dist/index.html" "$WWWROOT_DIR/index.html"
echo "    Copied to $WWWROOT_DIR"
echo ""

# ── Step 3: Publish C# application for each target platform ────────────────
echo "==> Step 3: Publishing .NET application..."
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

# ── Step 4: Package executables ─────────────────────────────────────────────
echo "==> Step 4: Packaging executables..."
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
