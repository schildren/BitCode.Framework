#!/usr/bin/env bash
# ==============================================================================
# BitCode Framework — Release Automation Runner (F8-13)
# ==============================================================================
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

VERSION=""
DRY_RUN=false
SKIP_TESTS=false
FORCE=false

for arg in "$@"; do
    case $arg in
        --dry-run)
            DRY_RUN=true
            shift
            ;;
        --skip-tests)
            SKIP_TESTS=true
            shift
            ;;
        --force)
            FORCE=true
            shift
            ;;
        v*|[0-9]*)
            VERSION="${arg#v}"
            shift
            ;;
    esac
done

echo "========================================================================"
echo " BitCode Framework — Herramienta de Automatización de Release (F8-13)   "
echo "========================================================================"

# 1. Comprobar working tree
echo -e "\n1. Verificando estado del working tree de Git..."
if [ -n "$(git status --porcelain)" ] && [ "$FORCE" = false ]; then
    echo "Advertencia: El working tree contiene cambios no guardados."
    if [ "$DRY_RUN" = false ]; then
        echo "Error: El release requiere un working tree limpio. Aplique commit o use --force / --dry-run."
        exit 1
    fi
fi
echo "   [OK] Working tree verificado."

# 2. Pre-flight check
echo -e "\n2. Ejecutando pre-flight check de herramientas..."
"${REPO_ROOT}/scripts/doctor.sh" tools

# 3. Tests
if [ "$SKIP_TESTS" = false ]; then
    echo -e "\n3. Ejecutando pruebas de arquitectura..."
    dotnet test "${REPO_ROOT}/tests/BitCode.Architecture.Tests/BitCode.Architecture.Tests.csproj" -c Release --nologo
    echo "   [OK] Pruebas de arquitectura superadas."
else
    echo -e "\n3. Omitiendo ejecución de pruebas (--skip-tests)."
fi

# 4. Tags
LATEST_TAG=$(git tag --sort=-creatordate | head -n 1 || echo "")
echo -e "\n4. Último tag Git registrado: ${LATEST_TAG:-Ninguno}"

# 5. Generar notas
echo -e "\n5. Generando notas de release..."
node "${REPO_ROOT}/scripts/generate-changelog.mjs" --notes-only

# 6. Dry run
if [ "$DRY_RUN" = true ]; then
    echo -e "\n------------------------------------------------------------------------"
    echo " [SIMULACIÓN / DRY-RUN FINALIZADA] No se crearon tags ni se realizaron commits."
    echo -e " Para aplicar el release ejecute:\n   ./scripts/release.sh <X.Y.Z>\n"
    exit 0
fi

if [ -z "$VERSION" ]; then
    echo "Error: Debe especificar una versión válida (ej. 0.2.0)."
    exit 1
fi

TAG_NAME="v${VERSION}"
echo -e "\n6. Actualizando CHANGELOG.md..."
node "${REPO_ROOT}/scripts/generate-changelog.mjs"

echo -e "\n7. Creando tag Git local: ${TAG_NAME}..."
git tag -a "${TAG_NAME}" -m "Release ${TAG_NAME}"

echo -e "\n========================================================================"
echo " Release ${TAG_NAME} preparado localmente con éxito!"
echo "========================================================================"
echo -e "Para disparar el pipeline de release:\n   git push origin ${TAG_NAME}\n"
