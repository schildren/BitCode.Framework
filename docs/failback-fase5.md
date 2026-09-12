# Failback — Fase 5, Disaster Recovery y multi-región

**Tarea:** F5-11 (Fase 5 — Disaster Recovery y multi-región) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Fecha:** 2026-09-08
**Depende de:** F5-10 ([`docs/failover-automatizado-fase5.md`](failover-automatizado-fase5.md) — `tools/RegionalFailoverHarness`, comando `failover`), F5-04 ([`docs/replicacion-sql-fase5.md`](replicacion-sql-fase5.md) — RPO real de log shipping/Always On), F5-05 ([`docs/replicacion-kafka-fase5.md`](replicacion-kafka-fase5.md) — replicación cross-cluster), F5-02 ([`docs/mapa-ownership-regional.md`](mapa-ownership-regional.md) — `RegionId`, `ITenantRegionMapStore`).
**Estado:** Comando `failback` implementado en `tools/RegionalFailoverHarness` (distinto de `failover`, no reutilizado a ciegas), con dos garantías obligatorias — resincronización verificada antes de revertir y ventana explícita de no-doble-escritor — verificadas con **dos procesos de sistema operativo reales** más un test de integración automatizado (`RegionFailbackNoSplitBrainIntegrationTests`) que fuerza el escenario adversarial de doble escritura concurrente.

---

## 1. El problema que resuelve (distinto del de F5-10)

F5-10 entrega el mecanismo de **promoción** (detección de caída + failover) con dos salvaguardas (confirmación explícita, umbral de fallos consecutivos). Su propio reporte deja explícito el pendiente que F5-11 cierra:

> "F5-11 (Failback formal con resincronización, para evitar split-brain de forma más completa que el simple 'no-op si ya es propietario' de esta tarea)".

El riesgo específico de un failback **no** es el mismo que el de un failover:

- Un **failover** promueve la región secundaria porque la primaria dejó de responder — no hay ambigüedad de datos, porque la región que se promueve es, por definición, la única que sigue viva y aceptando escrituras desde ese momento.
- Un **failback** trae de vuelta una región que **estuvo caída** mientras la otra región seguía aceptando escrituras del tenant. Si esa región vuelve y se le reasigna el rol de propietaria sin verificar que su réplica de datos está al día, el resultado es:
  - **Pérdida de datos silenciosa**: la región que vuelve empieza a servir lecturas/escrituras sobre una copia desactualizada, sin que nada lo indique.
  - **Split-brain real**: si el mapa se actualiza en un proceso pero otro nodo todavía tiene en caché la asignación anterior (ventana de propagación del hot-reload, F4-12), hay un instante en el que dos instancias podrían, en teoría, aceptar escrituras del mismo tenant creyendo cada una que es la única propietaria.

Un failback naive = "correr el mismo comando `failover` con los argumentos invertidos" (que es válido para un **failback manual, decidido por un operador, sin evidencia automatizable** — ver F5-10 sección 2.4 y 4.3) **no** es aceptable como único mecanismo cuando lo que se busca es evitar split-brain de forma sistemática: no hay ninguna verificación de que los datos de la región que vuelve estén al día, y no hay ninguna ventana explícita que impida que ambas regiones acepten tráfico durante la transición.

---

## 2. Diseño

### 2.1 Por qué `failback` es un comando nuevo, no `failover` con los parámetros invertidos

| | `failover` (F5-10) | `failback` (F5-11) |
|---|---|---|
| Verificación previa | `--health-url` **opcional** (umbral de fallos consecutivos de la región que se está cayendo) | `--resync-url` **obligatorio** (umbral de éxitos consecutivos de "al día" de la región que vuelve) — nunca se puede omitir |
| Qué pasa si la verificación previa no se cumple | Se aborta sin tocar el mapa (`aborted-threshold-not-met` / `aborted-healthy`) | Se aborta sin tocar el mapa (`aborted-not-resynced`) — **fallo cerrado**: si no hay dato de replicación, se asume "NO al día", nunca lo contrario |
| Ventana de transición | Ninguna — la promoción es una única escritura atómica | **Ventana de lock explícita** (`TenantRegionLock`): se escribe el lock, se espera `--lock-hold-seconds`, y solo entonces se promueve y libera el lock, en una segunda escritura atómica |
| Confirmación explícita | `--confirm` obligatorio | `--confirm` obligatorio (misma salvaguarda) |
| Auditoría | `promoted` / `no-op-already-owner` / `aborted-*` | Los mismos resultados, más `lock-applied` como evento propio (la activación del lock queda auditada independientemente de si la promoción luego se completa) |

La diferencia central es el **orden de las garantías y su carácter obligatorio, no opcional**: en `failover` la verificación de salud es una salvaguarda contra un falso positivo de caída (partición transitoria); en `failback` la verificación de resincronización es la única fuente de evidencia de que **no hay pérdida de datos** al revertir, así que no puede ser opcional ni "mejor esfuerzo".

### 2.2 Garantía 1 — Resincronización verificada antes de revertir, nunca al revés

El comando `failback` exige `--resync-url`, apuntando al endpoint `/replication-status/{tenantId}` que expone el nodo de la región candidata (el mismo binario `RegionalFailoverHarness node`, extendido en F5-11). Ese endpoint reporta:

```json
{ "region": "regionA", "pid": 12345, "tenantId": "...", "lagSeconds": 0, "caughtUp": true }
```

- **Fallo cerrado por diseño**: si no hay un valor de lag explícito para el tenant (por ejemplo, la región nunca recibió una actualización de resincronización), `lagSeconds` se reporta como el máximo valor posible y `caughtUp` es `false`. Una región recién recuperada, sin evidencia positiva de estar al día, **nunca** puede reclamar automáticamente estar lista para el failback — el silencio nunca se interpreta como "listo".
- **Umbral de éxitos consecutivos** (`--resync-threshold`, `--resync-interval-seconds`): igual patrón que el umbral de fallos consecutivos de `failover`, pero invertido — se exige observar `caughtUp: true` N veces seguidas antes de proceder. Una sola observación "al día" aislada (que podría ser una lectura obsoleta de un archivo a medio actualizar) no es suficiente.
- **En un despliegue real**, `/replication-status/{tenantId}` no sería un archivo de simulación (`resync-set`, ver sección 2.4) sino la métrica real de RPO ya construida en F5-04 (`SqlLogShippingRpoIntegrationTests`, lag de log shipping/Always On) y F5-05 (lag de consumidor del mirror Kafka cross-cluster) — F5-11 define el **contrato** (`caughtUp: bool`, fallo cerrado) que cualquiera de esos dos mecanismos reales puede alimentar sin cambiar el comando `failback`.

### 2.3 Garantía 2 — Ventana explícita de no-doble-escritor

Antes de tocar el propietario, `failback`:

1. Escribe `TenantRegionLock:{tenantId} = true` en el mismo archivo que las instancias `node` observan vía hot-reload (F4-12) — esta es una escritura atómica independiente (write-to-temp + `File.Move`).
2. Espera `--lock-hold-seconds` (tiempo suficiente para que **todas** las instancias `node`, incluida la propietaria vigente, recarguen la configuración y empiecen a rechazar escrituras).
3. Solo entonces reescribe, en una **única** escritura atómica, el propietario nuevo y `TenantRegionLock:{tenantId} = false`.

El handler `/write/{tenantId}` de cada nodo comprueba el lock **antes** que el propietario: si el lock está activo, la respuesta es `503 Service Unavailable` (`status: "locked"`) sin importar qué región es. El resultado: en todo momento hay **exactamente una** de estas tres situaciones, nunca una cuarta ambigua:

- Exactamente una región es propietaria (estado normal, antes y después del failback).
- Ninguna región acepta escrituras del tenant (ventana de lock, corta y explícita).
- (Lo que nunca puede ocurrir): dos regiones aceptando escrituras del mismo tenant al mismo tiempo.

### 2.4 Simulación del estado de replicación (`resync-set`)

Este repositorio no tiene una topología de réplicas SQL/Kafka multi-región corriendo en el entorno de pruebas (ver limitación explícita en la sección 5). Para poder ejercer el comando `failback` con **procesos de sistema operativo reales** (no solo argumentado por diseño), se añadió:

- `RegionalFailoverHarness resync-set <replicationStateFile> <tenantId> <lagSeconds>`: escritura atómica de un archivo JSON independiente (`ReplicationLagSeconds`) que el nodo de la región candidata carga con el mismo mecanismo de hot-reload.
- El comando `node` acepta un cuarto argumento opcional (`replicationStateFile`) para asociar ese archivo a una instancia concreta.

Esto reemplaza, solo a efectos de la prueba, la fuente real de la métrica (F5-04/F5-05) por un valor controlable manualmente — el contrato que consume `failback` (`GET <resync-url>/<tenantId>` → `{ "caughtUp": bool }`) es el mismo que consumiría un endpoint real de monitoreo de RPO.

---

## 3. Comandos del harness (extensión de F5-10)

```
RegionalFailoverHarness node       <mapFile> <regionId> <port> [replicationStateFile]
RegionalFailoverHarness resync-set <replicationStateFile> <tenantId> <lagSeconds>
RegionalFailoverHarness failback   <mapFile> <auditLog> <tenantId> <candidateRegion>
                                   --resync-url <url> [--resync-threshold <n>]
                                   [--resync-interval-seconds <s>] [--lock-hold-seconds <s>]
                                   [--confirm]
```

Los comandos `init`, `failover` y `status` de F5-10 no cambian (ver `docs/failover-automatizado-fase5.md`).

---

## 4. Evidencia real

### 4.1 Manual — dos procesos de sistema operativo, ciclo completo failover → failback

Procedimiento ejecutado en Windows, mismo criterio que F5-10 sección 4 (sin Docker/Testcontainers, procesos reales).

```bash
EXE=tools/RegionalFailoverHarness/bin/Debug/net10.0/RegionalFailoverHarness.exe
TENANT=22222222-2222-2222-2222-222222222222
MAP=map.json ; AUDIT=audit.jsonl ; LAGA=lagA.json

$EXE init $MAP $TENANT regionA
$EXE node $MAP regionA 5601 $LAGA &   # PID real 35620
$EXE node $MAP regionB 5602 &         # PID real 13220

curl http://127.0.0.1:5601/write/$TENANT   # -> 200 {"status":"accepted","region":"regionA",...}
curl http://127.0.0.1:5602/write/$TENANT   # -> 421 {"status":"rejected","ownerRegion":"regionA",...}

taskkill /F /PID 35620   # cae regionA de verdad

$EXE failover $MAP $AUDIT $TENANT regionB --health-url http://127.0.0.1:5601/health --threshold 2 --interval-seconds 1 --confirm
# -> PROMOCIÓN aplicada. propietaria anterior='regionA' propietaria nueva='regionB'.

$EXE node $MAP regionA 5601 $LAGA &   # regionA "vuelve", PID real 42680 (recovery, SIN resincronizar)
curl http://127.0.0.1:5601/write/$TENANT   # -> 421 (correcto: aunque está sana, NO es propietaria)

curl http://127.0.0.1:5601/replication-status/$TENANT
# -> {"lagSeconds":1.7976931348623157E+308,"caughtUp":false}  <- fallo cerrado, sin resync-set previo

$EXE failback $MAP $AUDIT $TENANT regionA --resync-url http://127.0.0.1:5601/replication-status --resync-threshold 2 --resync-interval-seconds 1 --confirm
# -> resync-check 1/2 NO al día ... resync-check 2/2 NO al día ...
# -> la región candidata NO demostró estar resincronizada -- se aborta la reversión sin tocar el mapa.
curl http://127.0.0.1:5602/write/$TENANT   # -> 200 (regionB sigue siendo la única propietaria)

$EXE resync-set $LAGA $TENANT 0
# -> resync-set: ... lagSeconds=0 (caughtUp=True).

$EXE failback $MAP $AUDIT $TENANT regionA --resync-url http://127.0.0.1:5601/replication-status --resync-threshold 2 --resync-interval-seconds 1 --lock-hold-seconds 4 --confirm &
```

Mientras el comando anterior corría (ventana de `--lock-hold-seconds 4`), se golpearon **ambos** endpoints de escritura cada 0.3s desde otra terminal:

```
t=1  A=421 B=200
t=2  A=421 B=200
t=3  A=421 B=200
t=4  A=503 B=503     <- ventana de lock: NINGUNA región acepta escrituras
t=5  A=503 B=503
...
t=9  A=503 B=503
t=10 A=200 B=421     <- failback completo: regionA es la nueva (y única) propietaria
...
t=30 A=200 B=421
```

Salida del comando `failback`:

```
failback: resync-check 1/2 AL DÍA (...) -- consecutivos al día=1.
failback: resync-check 2/2 AL DÍA (...) -- consecutivos al día=2.
failback: LOCK activado para tenant '...' -- ninguna región acepta escrituras durante 4s (ventana de no-doble-escritor).
failback: PROMOCIÓN aplicada y lock liberado. tenant='...' propietaria anterior='regionB' propietaria nueva='regionA'.
```

**Ninguna observación registró `A=200` y `B=200` simultáneamente** — en las 30 observaciones tomadas cada 0.3s durante todo el ciclo (single-owner regionB → lock total → single-owner regionA), el sistema nunca presentó ambigüedad de propietario.

### 4.2 Automatizada — test de integración real (`dotnet test`)

[`tests/Shared.Infrastructure.Persistence.Tests/Integration/RegionFailbackNoSplitBrainIntegrationTests.cs`](../tests/Shared.Infrastructure.Persistence.Tests/Integration/RegionFailbackNoSplitBrainIntegrationTests.cs) reproduce el escenario adversarial completo de la sección 4.1, pero como un test ejecutable y repetible por CI/desarrollador:

1. Compila el harness (`dotnet build tools/RegionalFailoverHarness/...`).
2. Arranca `nodeA` y `nodeB` como **procesos de sistema operativo reales** (`Process.Start("dotnet", ...)`, no `TestServer`/`WebApplicationFactory` in-process).
3. Verifica el estado inicial (regionA propietaria).
4. Mata `nodeA` de verdad (`Process.Kill(entireProcessTree: true)`) y corre `failover` real hacia regionB.
5. Levanta un nuevo proceso `nodeA` (recovery) y verifica que, aunque está sano, sigue rechazando escrituras (`421`).
6. Corre `failback` **sin** resincronización confirmada y verifica que se aborta (`exit code 2`, `aborted-not-resynced`, el mapa no cambia).
7. Confirma la resincronización (`resync-set ... 0`) y espera a que el propio endpoint de la región lo refleje.
8. Dispara `failback` con `--lock-hold-seconds 4` **en background** y, en paralelo, ejecuta un bucle de ~80 observaciones (cada 150ms durante 12s) golpeando **ambos** endpoints de escritura con HTTP real.
9. Asserts:
   - Ninguna observación tiene `A=200 && B=200` simultáneamente (criterio de aceptación "Evita split-brain").
   - Al menos una observación real tiene `A=503 && B=503` (evidencia de que el lock se materializó en ambos nodos, no solo en el archivo).
   - Estado final: `A=200`, `B=421` (exactamente una propietaria).
   - El log de auditoría contiene `aborted-not-resynced`, `lock-applied` y `promoted`.

Ejecución real (3 corridas consecutivas, sin flakiness observada):

```
dotnet test tests/Shared.Infrastructure.Persistence.Tests/Shared.Infrastructure.Persistence.Tests.csproj \
    --filter "FullyQualifiedName~RegionFailbackNoSplitBrainIntegrationTests"

Correctas! - Con error: 0, Superado: 1, Omitido: 0, Total: 1, Duración: 14 s
```

(repetido tres veces con el mismo resultado; el tiempo de ejecución —14-19s— corresponde a los `Task.Delay` reales de los umbrales de resincronización y de la ventana de lock, deliberadamente cortos pero reales, no simulados con reloj virtual).

---

## 5. Qué NO resuelve esta tarea (brechas explícitas)

- **No hay una topología de replicación SQL/Kafka multi-región corriendo en este repositorio** (un solo host de desarrollo, ver `docs/bia-fase5.md`). El endpoint `/replication-status/{tenantId}` de esta tarea es un contrato simulado (`resync-set`) — en producción, esa métrica la alimentaría el lag real de F5-04 (log shipping/Always On) o F5-05 (consumidor Kafka), sin cambiar el comando `failback` ni su semántica de fallo cerrado.
- **El `--lock-hold-seconds` es un tiempo fijo elegido por el operador**, no un mecanismo de sincronización distribuida con ACK de todas las instancias de una región (p. ej. un quórum tipo Raft/consenso). Es proporcional al intervalo de recarga del `ConfigMap`/volumen real (F4-12) — un despliegue real debe calibrar este valor con el peor caso de propagación de su infraestructura de configuración, no asumir el valor de referencia usado en esta prueba.
- **Habilitar tráfico productivo real** (DNS/anycast, balanceador externo) durante un failback sigue requiriendo aprobación humana explícita según la sección 13 del Plan Maestro ("failover/failback productivo") — igual que se documentó en F5-10 sección 2.5 y 5.

---

## 6. Runbook operativo

```bash
# 1. La región original (que cayó y fue reemplazada por un failover, F5-10) vuelve a estar disponible.
#    NO se ejecuta ningún comando de reversión todavía -- "responde a health-check" no es "está lista".

# 2. Se confirma resincronización mediante la métrica REAL de RPO (F5-04/F5-05), no el comando
#    "resync-set" (que es solo para la prueba de referencia de este repositorio):
#    - SQL: verificar que el log shipping/Always On de la región candidata no tiene lag pendiente.
#    - Kafka: verificar que el consumidor del mirror cross-cluster está al día (lag = 0).
#    Esa métrica debe exponerse en un endpoint HTTP con el contrato { "caughtUp": bool } para poder
#    usarse como --resync-url.

# 3. Un operador humano (con aprobación, ver sección 5) ejecuta el failback:
RegionalFailoverHarness failback /config/tenant-region-map.json /var/log/bitcode/regional-failover-audit.jsonl \
    <tenantId> <regionOriginal> \
    --resync-url https://monitoring.<region-original>.bitcode.internal/replication-status \
    --resync-threshold 5 --resync-interval-seconds 10 \
    --lock-hold-seconds 15 \
    --confirm

# 4. Durante la ventana de --lock-hold-seconds, TODAS las instancias de ambas regiones rechazan
#    escrituras del tenant (503) -- esto es esperado y corto, no un incidente.

# 5. Verificar que el tráfico fluye hacia la región original y que la región temporal (la promovida
#    en el failover) ya no acepta escrituras del tenant (421).

# 6. Auditar el ciclo completo con 'status' -- debe verse la secuencia completa:
#    promoted (failover) -> aborted-not-resynced (si hubo un intento prematuro) -> lock-applied -> promoted (failback).
RegionalFailoverHarness status /config/tenant-region-map.json /var/log/bitcode/regional-failover-audit.jsonl
```

**Aprobación humana obligatoria (sección 13 del Plan Maestro)**: igual que el failover (F5-10 sección 5), ejecutar este comando contra el mapa real que gobierna tráfico de producción constituye "failover/failback productivo" y "habilitación de tráfico productivo" — requiere el mismo circuito de aprobación explícita.

---

## 7. Verificación del criterio de aceptación ("Evita split-brain")

- **Runbook entregado**: sección 6, con los comandos exactos del harness extendido.
- **Diseño con dos garantías obligatorias, no opcionales**: resincronización verificada con fallo cerrado (sección 2.2) + ventana explícita de no-doble-escritor (sección 2.3) — nunca se revierte "a ciegas", nunca hay un instante de propietario ambiguo.
- **Evidencia real manual**: dos procesos de sistema operativo reales, `taskkill /F` real, HTTP real, sección 4.1.
- **Evidencia real automatizada**: test de integración (`RegionFailbackNoSplitBrainIntegrationTests`) que fuerza el escenario adversarial de doble escritura concurrente contra dos procesos reales y confirma, empíricamente y de forma repetible, que el sistema nunca acepta ambas regiones como válidas al mismo tiempo — sección 4.2.
- **Brechas explícitas documentadas, no encubiertas**: sección 5.

**Conclusión:** criterio de aceptación de F5-11 ("Evita split-brain") cumplido con evidencia real, tanto manual como automatizada, para el mecanismo entregado por esta tarea.

## Referencias

- [`plan-maestro-bitcode-ia.md`](plan-maestro-bitcode-ia.md) — backlog F5-11, sección 13 (aprobaciones humanas).
- [`failover-automatizado-fase5.md`](failover-automatizado-fase5.md) — F5-10, comando `failover`, salvaguardas de confirmación y umbral de fallos consecutivos.
- [`replicacion-sql-fase5.md`](replicacion-sql-fase5.md) — F5-04, RPO real de log shipping/Always On (fuente real de `/replication-status` en producción).
- [`replicacion-kafka-fase5.md`](replicacion-kafka-fase5.md) — F5-05, replicación cross-cluster (fuente real alternativa de `/replication-status`).
- [`mapa-ownership-regional.md`](mapa-ownership-regional.md) — F5-02, `RegionId`/`ITenantRegionMapStore`.
- `tools/RegionalFailoverHarness/Program.cs` — implementación de `failback`, `resync-set`, y el endpoint `/replication-status`.
- `tests/Shared.Infrastructure.Persistence.Tests/Integration/RegionFailbackNoSplitBrainIntegrationTests.cs` — test de integración real verificado en la sección 4.2.
