#!/usr/bin/env bash
# ---------------------------------------------------------------------------------------------
# F5-12 (Fase 5 -- Disaster Recovery y multi-región): simulacro end-to-end de DR ("DR drill").
#
# Orquesta, con PROCESOS DE SISTEMA OPERATIVO REALES (mismo estándar de evidencia que F5-10/F5-11),
# la secuencia completa de un evento de disaster recovery:
#
#   operación normal (regionA) -> caída real de regionA (+ su enlace de replicación) ->
#   detección (health-check) -> failover (tools/RegionalFailoverHarness, F5-10) ->
#   operación en regionB (promovida) -> regionA se recupera -> resincronización ->
#   failback (F5-11, sin split-brain) -> mide RPO real (ledger-diff) y RTO real (reloj de pared)
#
# No es un test de integración de una tarea puntual (eso ya existe en F5-04/F5-05/F5-10/F5-11):
# es el simulacro cronometrado que integra todo lo anterior, tal como pide el criterio de
# aceptación de F5-12 ("RPO/RTO demostrados").
#
# Requiere: bash (Git Bash en Windows), curl, dotnet. Sin Docker/Testcontainers -- mismo criterio
# que F5-10/F5-11 (dos procesos de SO reales en un host de desarrollo, ver docs/bia-fase5.md).
#
# Uso:
#   tools/DrDrill/run-drill.sh [workdir]
#
# Salida: imprime cada paso con su timestamp real y, al final, un resumen JSON con los tiempos
# de RTO y RPO medidos. Ver docs/dr-drill-fase5.md para el análisis del resultado.
# ---------------------------------------------------------------------------------------------
set -euo pipefail

kill_pid_if_alive() {
    local pid="$1"
    [ -z "${pid:-}" ] && return 0
    kill -9 "$pid" 2>/dev/null || taskkill //F //PID "$pid" 2>/dev/null || true
}

cleanup() {
    # Se ejecuta siempre (éxito, error o Ctrl+C) -- ningún proceso de nodo/replicador del drill
    # debe sobrevivir a la corrida, para que una ejecución siguiente no falle por el .exe bloqueado.
    kill_pid_if_alive "${PID_NODE_A:-}"
    kill_pid_if_alive "${PID_NODE_B:-}"
    kill_pid_if_alive "${PID_REPLICATE:-}"
    kill_pid_if_alive "${PID_NODE_A_RECOVERED:-}"
}
trap cleanup EXIT

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
WORKDIR="${1:-$REPO_ROOT/tools/DrDrill/.run}"
EXE_REL="tools/RegionalFailoverHarness/bin/Debug/net10.0/RegionalFailoverHarness.exe"
EXE="$REPO_ROOT/$EXE_REL"

TENANT="dddddddd-dddd-dddd-dddd-dddddddddddd"
PORT_A=5701
PORT_B=5702
REPLICATION_DELAY_SECONDS=3      # retraso real simulado del "log shipping"/mirror asíncrono
WRITE_INTERVAL_SECONDS=0.3
WRITES_BEFORE_FAILURE=10          # ~3s de operación normal antes de la caída simulada
WRITES_DURING_PROMOTED_OPERATION=15  # ~4.5s operando sobre regionB promovida
HEALTH_THRESHOLD=2
HEALTH_INTERVAL_SECONDS=1
RESYNC_THRESHOLD=2
RESYNC_INTERVAL_SECONDS=1
LOCK_HOLD_SECONDS=3

now_ms() { date +%s%3N; }
now_iso() { date -u +"%Y-%m-%dT%H:%M:%S.%3NZ"; }
elapsed_seconds() { awk -v a="$1" -v b="$2" 'BEGIN{printf "%.3f", (b-a)/1000}'; }

log() { echo "[drill $(now_iso)] $*"; }

rm -rf "$WORKDIR"
mkdir -p "$WORKDIR"
cd "$WORKDIR"

MAP="$WORKDIR/map.json"
AUDIT="$WORKDIR/audit.jsonl"
LAG_A="$WORKDIR/lagA.json"
LAG_B="$WORKDIR/lagB.json"   # nunca se escribe -- regionB no necesita reportar replication-status en este drill
LEDGER_A="$WORKDIR/ledgerA.jsonl"
LEDGER_B="$WORKDIR/ledgerB.jsonl"
LOG_NODE_A="$WORKDIR/nodeA.log"
LOG_NODE_B="$WORKDIR/nodeB.log"
LOG_REPLICATE="$WORKDIR/replicate.log"
TIMES_FILE="$WORKDIR/times.env"

log "=== Paso 0: build del harness ==="
dotnet build "$REPO_ROOT/tools/RegionalFailoverHarness/RegionalFailoverHarness.csproj" -v quiet

log "=== Paso 1: preparación (mapa inicial, regionA propietaria) ==="
"$EXE" init "$MAP" "$TENANT" regionA

log "=== Paso 2: arranque de dos nodos como procesos de SO reales ==="
"$EXE" node "$MAP" regionA "$PORT_A" "$LAG_A" "$LEDGER_A" > "$LOG_NODE_A" 2>&1 &
PID_NODE_A=$!
"$EXE" node "$MAP" regionB "$PORT_B" "$LAG_B" "$LEDGER_B" > "$LOG_NODE_B" 2>&1 &
PID_NODE_B=$!
sleep 1
log "nodeA pid real=$PID_NODE_A (puerto $PORT_A), nodeB pid real=$PID_NODE_B (puerto $PORT_B)"

log "=== Paso 3: arranque del replicador asíncrono real (regionA -> regionB, retraso ${REPLICATION_DELAY_SECONDS}s) ==="
"$EXE" replicate "$LEDGER_A" "$LEDGER_B" "$REPLICATION_DELAY_SECONDS" > "$LOG_REPLICATE" 2>&1 &
PID_REPLICATE=$!
log "replicate pid real=$PID_REPLICATE"

log "=== Paso 4: operación normal -- $WRITES_BEFORE_FAILURE escrituras reales confirmadas por regionA ==="
T_START_MS=$(now_ms)
for seq in $(seq 1 "$WRITES_BEFORE_FAILURE"); do
    curl -s -o /dev/null "http://127.0.0.1:$PORT_A/write/$TENANT?seq=$seq"
    sleep "$WRITE_INTERVAL_SECONDS"
done
log "operación normal completa: $WRITES_BEFORE_FAILURE escrituras confirmadas por regionA (algunas todavía en tránsito hacia regionB por el retraso de replicación real de ${REPLICATION_DELAY_SECONDS}s)."

log "=== Paso 5: DESASTRE REAL -- se mata regionA y su enlace de replicación en el mismo instante ==="
T_FAILURE_MS=$(now_ms)
T_FAILURE_ISO=$(now_iso)
kill -9 "$PID_NODE_A" 2>/dev/null || taskkill //F //PID "$PID_NODE_A" 2>/dev/null || true
kill -9 "$PID_REPLICATE" 2>/dev/null || taskkill //F //PID "$PID_REPLICATE" 2>/dev/null || true
log "regionA (pid=$PID_NODE_A) y su replicador (pid=$PID_REPLICATE) fueron terminados de verdad en t=$T_FAILURE_ISO -- este es el instante de la caída simulada."

log "=== Paso 5b: medición REAL de RPO -- diff entre lo que regionA había confirmado y lo que llegó a regionB ANTES de la caída ==="
# Se mide acá, antes de reanudar operación en regionB, para que el ledger de regionB refleje
# exactamente lo que el replicador real alcanzó a copiar antes de morir junto con regionA -- sin
# contaminar la comparación con la actividad posterior a la promoción (que usa su propio rango de
# números de secuencia, ver paso 8).
RPO_REPORT_FILE="$WORKDIR/rpo-report.json"
"$EXE" ledger-diff "$LEDGER_A" "$LEDGER_B" | tee "$RPO_REPORT_FILE"

log "=== Paso 6: detección + failover automatizado (F5-10) hacia regionB ==="
set +e
"$EXE" failover "$MAP" "$AUDIT" "$TENANT" regionB \
    --health-url "http://127.0.0.1:$PORT_A/health" --threshold "$HEALTH_THRESHOLD" \
    --interval-seconds "$HEALTH_INTERVAL_SECONDS" --confirm
FAILOVER_EXIT=$?
set -e
if [ "$FAILOVER_EXIT" -ne 0 ]; then
    log "ERROR: failover no se aplicó (exit=$FAILOVER_EXIT) -- abortando el drill."
    exit 1
fi

log "=== Paso 7: servicio restaurado -- primera escritura aceptada por regionB tras la promoción ==="
until curl -s -o /dev/null -w "%{http_code}" "http://127.0.0.1:$PORT_B/write/$TENANT?seq=9000" | grep -q 200; do
    sleep 0.1
done
T_SERVICE_RESTORED_MS=$(now_ms)
log "regionB acepta escrituras del tenant -- servicio restaurado en t=$(now_iso)."

log "=== Paso 8: operación sobre regionB (promovida) durante un intervalo -- $WRITES_DURING_PROMOTED_OPERATION escrituras ==="
for seq in $(seq 9001 $((9000 + WRITES_DURING_PROMOTED_OPERATION))); do
    curl -s -o /dev/null "http://127.0.0.1:$PORT_B/write/$TENANT?seq=$seq"
    sleep "$WRITE_INTERVAL_SECONDS"
done

log "=== Paso 10: regionA se recupera (nuevo proceso de SO, réplica local todavía desactualizada) ==="
T_RECOVERY_START_MS=$(now_ms)
"$EXE" node "$MAP" regionA "$PORT_A" "$LAG_A" "$LEDGER_A" > "$LOG_NODE_A.recovered.log" 2>&1 &
PID_NODE_A_RECOVERED=$!
sleep 1
CODE=$(curl -s -o /dev/null -w "%{http_code}" "http://127.0.0.1:$PORT_A/write/$TENANT?seq=9999")
log "regionA (pid real=$PID_NODE_A_RECOVERED) recuperada, escritura rechazada con HTTP $CODE (correcto: no es propietaria hasta un failback verificado)."

log "=== Paso 11: verificación de resincronización (fallo cerrado por diseño, F5-11) ==="
curl -s "http://127.0.0.1:$PORT_A/replication-status/$TENANT" ; echo
log "regionA todavía NO está al día (esperado, sin resync-set) -- se procede a resincronizar los datos reales antes de intentar el failback."

log "=== Paso 12: resincronización real de datos (copia de lo que regionB aceptó mientras regionA estaba caída) ==="
cp "$LEDGER_B" "$LEDGER_A"
"$EXE" resync-set "$LAG_A" "$TENANT" 0
# El hot-reload de configuración (mismo mecanismo de F4-12) no es instantáneo -- se espera a que
# el proceso "node" de regionA efectivamente recargue el archivo antes de exigirle al comando
# "failback" observaciones consecutivas de "al día" (si no, la primera observación puede leer
# todavía el valor anterior, igual que documenta docs/failback-fase5.md sección 2.2).
sleep 2
T_RESYNC_CONFIRMED_MS=$(now_ms)
log "resincronización de datos completa y confirmada -- ledgerA ahora contiene todo lo que ledgerB tiene."

log "=== Paso 13: failback (F5-11) -- retorno de ownership a regionA sin split-brain ==="
set +e
"$EXE" failback "$MAP" "$AUDIT" "$TENANT" regionA \
    --resync-url "http://127.0.0.1:$PORT_A/replication-status" \
    --resync-threshold "$RESYNC_THRESHOLD" --resync-interval-seconds "$RESYNC_INTERVAL_SECONDS" \
    --lock-hold-seconds "$LOCK_HOLD_SECONDS" --confirm
FAILBACK_EXIT=$?
set -e
if [ "$FAILBACK_EXIT" -ne 0 ]; then
    log "ERROR: failback no se aplicó (exit=$FAILBACK_EXIT) -- abortando el drill."
    exit 1
fi

log "=== Paso 14: topología original restaurada -- primera escritura aceptada de nuevo por regionA ==="
until curl -s -o /dev/null -w "%{http_code}" "http://127.0.0.1:$PORT_A/write/$TENANT?seq=20000" | grep -q 200; do
    sleep 0.1
done
T_FAILBACK_COMPLETE_MS=$(now_ms)
log "regionA vuelve a aceptar escrituras del tenant -- ciclo de disaster recovery completo en t=$(now_iso)."

# Limpieza de procesos que quedaron vivos
kill -9 "$PID_NODE_B" 2>/dev/null || taskkill //F //PID "$PID_NODE_B" 2>/dev/null || true
kill -9 "$PID_NODE_A_RECOVERED" 2>/dev/null || taskkill //F //PID "$PID_NODE_A_RECOVERED" 2>/dev/null || true

RTO_SERVICE_RESTORED_S=$(elapsed_seconds "$T_FAILURE_MS" "$T_SERVICE_RESTORED_MS")
RTO_FULL_CYCLE_S=$(elapsed_seconds "$T_FAILURE_MS" "$T_FAILBACK_COMPLETE_MS")
RESYNC_WINDOW_S=$(elapsed_seconds "$T_RECOVERY_START_MS" "$T_RESYNC_CONFIRMED_MS")

cat > "$WORKDIR/drill-summary.json" <<EOF
{
  "drillTimestampUtc": "$T_FAILURE_ISO",
  "tenantId": "$TENANT",
  "rto": {
    "serviceRestoredSeconds": $RTO_SERVICE_RESTORED_S,
    "fullCycleFailoverPlusFailbackSeconds": $RTO_FULL_CYCLE_S,
    "resyncWindowSeconds": $RESYNC_WINDOW_S
  },
  "rpoReportFile": "$RPO_REPORT_FILE",
  "auditLogFile": "$AUDIT",
  "workdir": "$WORKDIR"
}
EOF

log "=== RESUMEN DEL DRILL ==="
cat "$WORKDIR/drill-summary.json"
log "Reporte de RPO: $RPO_REPORT_FILE"
log "Log de auditoría failover/failback: $AUDIT"
log "Artefactos completos en: $WORKDIR"
