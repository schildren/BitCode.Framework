#!/usr/bin/env bash
# ==============================================================================
# BitCode Diagnostics Runner (F8-12)
# ==============================================================================
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
PROJECT_PATH="${REPO_ROOT}/tools/BitCode.Diagnostics/BitCode.Diagnostics.csproj"

if [ ! -f "${PROJECT_PATH}" ]; then
    echo "Error: No se encontró el proyecto BitCode.Diagnostics en ${PROJECT_PATH}"
    exit 1
fi

exec dotnet run --project "${PROJECT_PATH}" -- "$@"
