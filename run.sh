#!/usr/bin/env bash
set -euo pipefail

export __NV_DISABLE_EXPLICIT_SYNC=1

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
dotnet run --project "$SCRIPT_DIR/src/EditorApp/EditorApp.csproj"
