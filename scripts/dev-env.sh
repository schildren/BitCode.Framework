#!/usr/bin/env bash
# Scripts de gestión para entorno local de desarrollo — BitCode.Framework (F8-11)
set -euo pipefail

ACTION="${1:-up}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

cd "${REPO_ROOT}"

case "${ACTION}" in
    up)
        echo "Iniciando infraestructura local de BitCode.Framework..."
        docker compose up -d
        echo ""
        echo "Servicios iniciados en segundo plano."
        echo "Endpoints locales disponibles:"
        echo " - SQL Server: localhost:1433 (sa / Password123!)"
        echo " - Redis:      localhost:6379"
        echo " - Kafka:      localhost:9092"
        echo " - OTel gRPC:  localhost:4317"
        echo " - OTel HTTP:  localhost:4318"
        echo " - Jaeger UI:  http://localhost:16686"
        echo ""
        docker compose ps
        ;;
    down)
        echo "Deteniendo contenedores de desarrollo..."
        docker compose down
        echo "Entorno detenido."
        ;;
    status)
        docker compose ps
        ;;
    logs)
        docker compose logs -f
        ;;
    clean)
        echo "Limpiando contenedores y volúmenes de datos (reset completo)..."
        docker compose down -v
        echo "Volúmenes eliminados. El entorno arrancará desde cero en el próximo 'up'."
        ;;
    *)
        echo "Uso: $0 [up|down|status|logs|clean]"
        exit 1
        ;;
esac
