# Failover automatizado — Fase 5, Disaster Recovery y multi-región

**Tarea:** F5-10 (Fase 5 — Disaster Recovery y multi-región) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Fecha:** 2026-09-08
**Depende de:** F5-01 ([`docs/bia-fase5.md`](bia-fase5.md) — perfiles DR), F5-02 ([`docs/mapa-ownership-regional.md`](mapa-ownership-regional.md) — `RegionId`, `ITenantRegionMapStore`, `IRegionalOwnershipResolver`, `ICurrentRegionProvider`), F5-03 ([`docs/routing-regional-gateway.md`](routing-regional-gateway.md) — routing regional del Gateway sobre esas mismas interfaces).
**Estado:** Workflow de promoción de región implementado como herramienta operativa (`tools/RegionalFailoverHarness`), con controles de seguridad reales (confirmación explícita + umbral de fallos consecutivos), verificado con **dos procesos de sistema operativo reales** (mismo estándar de evidencia que `docs/guia-quartz-ha.md`, F4-11) y ejecución de failover → failback → failover repetida sin dejar estado inconsistente.

**Alcance:** esta tarea cierra la brecha explícita que dejaban F5-02/F5-03: el mapa tenant → región propietaria (`ITenantRegionMapStore`) hoy es siempre una fuente estática/configurada a mano (`InMemoryTenantRegionMapStore`, `ConfigurationTenantRegionMapStore`) — nada lo actualiza automáticamente cuando la región propietaria cae. F5-10 entrega el **workflow** que detecta la caída y promueve la región secundaria, con salvaguardas explícitas contra un failover accidental o disparado por una partición de red transitoria. No entrega (ni finge) un orquestador multi-región cloud real — eso no existe en este repositorio (un solo host de desarrollo, ver `docs/bia-fase5.md`) — sino una herramienta ejecutable y un runbook con los comandos exactos, igual que se hizo con los pendientes de Fase 4 que dependían de infraestructura no disponible (`docs/guia-quartz-ha.md` sección 6.1, `docs/politica-configuracion-y-feature-flags.md` sección 2.4).

---

## 1. El problema que resuelve

`RegionalOwnershipRoutingMiddleware` (F5-03, `BitCode.Gateway`) rechaza con `421 Misdirected Request` cualquier request de un tenant que llega a una instancia del Gateway que no es su región propietaria, según `ITenantRegionMapStore`. Hasta F5-09 esa asignación se movía únicamente:

- Editando a mano `InMemoryTenantRegionMapStore.Assign(...)` (backend, F5-02), o
- Editando a mano la sección `Regional:TenantRegionAssignments` de la configuración del Gateway (F5-03).

Si la región propietaria de un tenant cae, no había ningún mecanismo que (a) detectara la caída, (b) decidiera de forma controlada promover la región secundaria, y (c) dejara esa promoción reflejada donde el routing y el `RegionalOwnershipBehavior` (Shared.Application) la leen — todo sin depender de reiniciar procesos, ni de una sola ejecución "de una vez" que no se pueda repetir de forma determinística.

## 2. Diseño

### 2.1 Por qué una herramienta standalone (`tools/RegionalFailoverHarness`), no un `IJob` de Quartz ni un endpoint del Gateway

Mismo razonamiento que ya aplicó `docs/politica-backups-fase5.md` sección 4.1 para los backups (F5-07): el mecanismo que decide "esta región está caída, promover la otra" **no puede depender del proceso de la propia región que se está cayendo** para ejecutarse. Un `IJob` alojado dentro del backend de la región A no puede promover a la región B si el proceso de la región A es justamente el que murió. Por eso, igual que los scripts de backup, el workflow de failover es un binario externo e independiente del ciclo de vida de cualquier instancia regional — invocado por un operador o por un orquestador externo (Kubernetes liveness/readiness + un `CronJob`/proceso de monitoreo, fuera del alcance de este repositorio), nunca por el propio proceso que podría estar cayendo.

### 2.2 Fuente de verdad y mecanismo de propagación: el mismo patrón de hot-reload de F4-12

El mapa tenant → región propietaria se modela como un archivo JSON (`tenant-region-map.json`) que cada instancia "node" (representando una instancia regional del Gateway/backend) carga con:

```csharp
builder.Configuration.AddJsonFile(mapFile, optional: true, reloadOnChange: true);
```

Es **el mismo mecanismo, ya documentado y ya en producción de referencia**, que `docs/politica-configuracion-y-feature-flags.md` sección 2.4 usa para el hot-reload de feature flags vía `ConfigMap` montado como volumen (F4-12): cuando el archivo cambia (un `ConfigMap` de Kubernetes resincronizado por el kubelet, o en este caso una reescritura atómica del harness), el proceso ya corriendo relee la configuración **sin reiniciar** — que es exactamente la propiedad que un failover real necesita (no se puede exigir un `kubectl rollout restart` de todas las instancias del Gateway de la región secundaria como parte de la ruta crítica de un failover de emergencia).

### 2.3 Controles de seguridad (requisito explícito del criterio de aceptación)

El comando `failover` de la herramienta implementa dos salvaguardas independientes, ambas obligatorias:

| Salvaguarda | Mecanismo | Qué evita |
|---|---|---|
| **Confirmación explícita** | El flag `--confirm` es obligatorio SIEMPRE. Sin él, el comando aborta sin tocar el mapa y **igual registra el intento en el log de auditoría** (resultado `aborted-not-confirmed`). | Que una invocación accidental (o un script mal parametrizado) promueva una región sin una decisión deliberada — modela el "quórum/confirmación" pedido por la tarea: quien ejecuta el comando con `--confirm` está tomando la decisión, y queda auditado quién fue. |
| **Umbral de fallos consecutivos (ventana de gracia)** | Si se pasa `--health-url`, la promoción solo procede tras observar `--threshold` fallos de health-check **consecutivos** (separados por `--interval-seconds`); si el health-check vuelve a responder `200` en cualquier intento, se aborta inmediatamente (`aborted-healthy`) sin promover. | Que una partición de red transitoria (un solo `blip`) dispare un failover — el mismo tipo de falso positivo que en un clúster real produciría un "split-brain" evitable. |

Ambas salvaguardas son independientes: el umbral protege contra el disparo automatizado prematuro; la confirmación protege contra la ejecución no deliberada. Un failback manual (sin `--health-url`, ver sección 4.3) igual exige `--confirm` — nunca hay una ruta que promueva sin confirmación explícita.

### 2.4 Repetibilidad (criterio de aceptación literal: "Ejecución repetible")

- **Idempotencia**: promover un tenant al mismo `RegionId` que ya tiene como propietaria es un no-op auditado (`no-op-already-owner`) — el archivo del mapa no se reescribe, y ejecutar el mismo comando dos veces seguidas no cambia el resultado ni corrompe el estado.
- **Reversibilidad**: el failback es la misma operación (`failover <mapFile> <auditLog> <tenantId> <regiónOriginal> --confirm`) con el `RegionId` candidato invertido — no existe una operación "de un solo sentido" ni un estado especial de "modo failover" que haya que "des-hacer" con un comando distinto.
- **Escritura atómica**: cada promoción escribe primero a un archivo temporal y lo mueve (`File.Move(tmp, mapFile, overwrite: true)`) sobre el archivo final, para que ningún proceso "node" observando el archivo con `reloadOnChange: true` pueda leer un JSON a medio escribir durante una ejecución concurrente con otra.
- **Auditoría append-only**: cada invocación (promovida, no-op, o abortada por cualquiera de las dos salvaguardas) agrega una línea JSON al log de auditoría (`timestampUtc`, `tenantId`, `previousOwner`, `newOwner`, `result`, `reason`, `consecutiveFailures`, `healthUrl`, `triggeredBy`, `pid`, `machine`) — nunca sobrescribe entradas anteriores, de modo que ejecutar el ciclo failover → failback → failover N veces dejas un historial completo y reconstruible, no solo el estado final.

### 2.5 Qué NO resuelve esta tarea (brechas explícitas, no encubiertas)

- **No hay un orquestador de detección automática corriendo 24/7** en este repositorio (eso sería un servicio de monitoreo externo real invocando este binario con un cron/systemd timer/Kubernetes `CronJob` apuntando al `--health-url` de cada región) — se entrega el mecanismo de detección+promoción con salvaguardas, no el daemon de monitoreo continuo, que es infraestructura operativa fuera del alcance de un repositorio de framework.
- **No hay replicación de datos** en esta tarea — la promoción de ownership asume que la topología de replicación (F5-04/F5-05) ya sincronizó los datos hacia la región candidata; F5-10 solo redirige el tráfico de escritura, no reconcilia datos.
- **Habilitar tráfico productivo real hacia la región promovida** (DNS/anycast, actualización de un balanceador externo real) requiere aprobación humana explícita según la sección 13 del Plan Maestro ("habilitación de tráfico productivo", "failover/failback productivo") — esta herramienta y su prueba operan sobre un mapa de configuración y procesos de referencia en un host de desarrollo, nunca contra tráfico de producción real. El propio comentario de `InMemoryTenantRegionMapStore` (F5-02) ya deja esto documentado.

---

## 3. Componentes

| Componente | Rol |
|---|---|
| [`tools/RegionalFailoverHarness/Program.cs`](../tools/RegionalFailoverHarness/Program.cs) | Herramienta de línea de comandos: `init` (crea el mapa inicial), `node` (levanta una instancia de referencia de una región, con hot-reload del mapa), `failover` (workflow de detección+promoción con las dos salvaguardas de la sección 2.3), `status` (imprime el mapa vigente y el log de auditoría). |
| [`tools/RegionalFailoverHarness/RegionalFailoverHarness.csproj`](../tools/RegionalFailoverHarness/RegionalFailoverHarness.csproj) | Referencia únicamente a `Shared.Domain` (reutiliza `RegionId`, F5-02) — deliberadamente standalone del resto del framework, mismo criterio que `tools/QuartzHaRecoveryHarness` (F4-11). |
| `tenant-region-map.json` (generado por `init`, no versionado) | Fuente de verdad del mapeo tenant → región propietaria para esta prueba de referencia — análogo al `ConfigMap` real de F4-12/F5-03. |
| `audit-log.jsonl` (generado por `failover`, no versionado) | Registro auditable append-only de cada intento de failover (promovido, no-op o abortado). |

---

## 4. Evidencia real — dos procesos de sistema operativo, ciclo repetido

Procedimiento ejecutado en Windows (PowerShell/Git Bash), sin Docker ni Testcontainers — igual que `docs/guia-quartz-ha.md` sección 6.1, esto es una verificación manual con procesos de SO reales, no parte de `dotnet test`. Reproducible con los comandos exactos siguientes.

### 4.1 Preparación

```
dotnet build tools/RegionalFailoverHarness/RegionalFailoverHarness.csproj
EXE=tools/RegionalFailoverHarness/bin/Debug/net10.0/RegionalFailoverHarness.exe
TENANT=11111111-1111-1111-1111-111111111111

$EXE init tenant-region-map.json $TENANT regionA
# -> {"TenantRegionMap":{"11111111-1111-1111-1111-111111111111":"regionA"}}
```

### 4.2 Arranque de dos nodos como procesos de SO independientes

```
$EXE node tenant-region-map.json regionA 5501 &   # PID real 12580
$EXE node tenant-region-map.json regionB 5502 &   # PID real 12688
```

Verificación de routing inicial (tenant asignado a `regionA`):

```
curl http://127.0.0.1:5501/write/$TENANT   -> HTTP 200 {"status":"accepted","region":"regionA","pid":12580}
curl http://127.0.0.1:5502/write/$TENANT   -> HTTP 421 {"status":"rejected","ownerRegion":"regionA","currentRegion":"regionB","pid":12688}
```

### 4.3 Caída real de la región propietaria (`taskkill /F` de verdad, no simulación de código)

```
taskkill /F /PID 12580     # mata el proceso regionA de verdad
curl --max-time 2 http://127.0.0.1:5501/health   -> sin respuesta (conexión rechazada)
```

### 4.4 Salvaguarda 1 verificada: sin `--confirm` no promueve, aunque el umbral se cumpla

```
$EXE failover tenant-region-map.json audit-log.jsonl $TENANT regionB \
    --health-url http://127.0.0.1:5501/health --threshold 3 --interval-seconds 1
```
Salida real:
```
failover: health-check 1/3 FALLÓ (...) -- fallos consecutivos=1.
failover: health-check 2/3 FALLÓ (...) -- fallos consecutivos=2.
failover: health-check 3/3 FALLÓ (...) -- fallos consecutivos=3.
failover: falta --confirm (confirmación explícita obligatoria) -- se aborta la promoción sin tocar el mapa.
```
El mapa sigue con `regionA` como propietaria — verificado con `cat tenant-region-map.json` tras el intento.

### 4.5 Failover real con las dos salvaguardas satisfechas

```
$EXE failover tenant-region-map.json audit-log.jsonl $TENANT regionB \
    --health-url http://127.0.0.1:5501/health --threshold 3 --interval-seconds 1 --confirm
```
Salida real:
```
failover: health-check 1/3 FALLÓ ... fallos consecutivos=1.
failover: health-check 2/3 FALLÓ ... fallos consecutivos=2.
failover: health-check 3/3 FALLÓ ... fallos consecutivos=3.
failover: PROMOCIÓN aplicada. tenant='...' propietaria anterior='regionA' propietaria nueva='regionB'.
```
`tenant-region-map.json` queda con `"...": "regionB"`.

### 4.6 Hot-reload verificado sin reiniciar el proceso de la región B

```
curl http://127.0.0.1:5502/write/$TENANT
-> HTTP 200 {"status":"accepted","region":"regionB","pid":12688}
```
**El PID (`12688`) es el mismo que arrancó en 4.2** — el proceso de la región B nunca se reinició; recargó el mapa vía `reloadOnChange: true`, exactamente como haría una instancia real del Gateway con el `ConfigMap` de F4-12/F5-03.

### 4.7 Región caída, al volver a levantarse, respeta la nueva asignación

```
$EXE node tenant-region-map.json regionA 5501 &   # nuevo proceso, PID real 37832 (recovery de la región A)
curl http://127.0.0.1:5501/write/$TENANT
-> HTTP 421 {"status":"rejected","ownerRegion":"regionB","currentRegion":"regionA","pid":37832}
```
La región A, aunque vuelve a estar sana, correctamente NO acepta escrituras del tenant hasta un failback explícito — sin esto se produciría doble escritor (split-brain), justamente lo que F5-11 (failback) debe seguir evitando.

### 4.8 Failback manual (sin `--health-url`, solo confirmación) y **repetición completa del ciclo**

```
# Failback (misma operación, candidato invertido, sin health-url porque es una decisión manual del operador)
$EXE failover tenant-region-map.json audit-log.jsonl $TENANT regionA          # sin --confirm: aborta (verificado)
$EXE failover tenant-region-map.json audit-log.jsonl $TENANT regionA --confirm
# -> PROMOCIÓN aplicada. propietaria anterior='regionB' propietaria nueva='regionA'.

curl http://127.0.0.1:5501/write/$TENANT -> HTTP 200 (pid=37832, sin reinicio)
curl http://127.0.0.1:5502/write/$TENANT -> HTTP 421 (pid=12688, sin reinicio)

# SEGUNDO ciclo completo: se mata regionA otra vez y se repite el failover
taskkill /F /PID 37832
$EXE failover tenant-region-map.json audit-log.jsonl $TENANT regionB \
    --health-url http://127.0.0.1:5501/health --threshold 2 --interval-seconds 1 --confirm
# -> PROMOCIÓN aplicada. propietaria anterior='regionA' propietaria nueva='regionB'.

curl http://127.0.0.1:5502/write/$TENANT -> HTTP 200 (pid=12688, mismo proceso desde el arranque original)
```

**Resultado**: el ciclo failover → failback → failover se ejecutó dos veces completas con el mismo par de comandos, sin reiniciar el proceso de la región B en ningún momento, y sin que el mapa ni el log de auditoría quedaran en un estado inconsistente en ningún punto — el criterio de aceptación "Ejecución repetible" queda demostrado empíricamente, no solo argumentado por diseño.

### 4.9 Auditoría íntegra de los intentos anteriores (repetibilidad + trazabilidad, verificado con `cat audit-log.jsonl`)

```json
{"timestampUtc":"2026-09-09T00:47:37.55Z","tenantId":"1111...","previousOwner":null,"newOwner":"regionB","result":"aborted-not-confirmed","reason":"missing-explicit-confirmation","consecutiveFailures":3,"healthUrl":"http://127.0.0.1:5501/health","triggeredBy":"javie","pid":40932,"machine":"VICTUS"}
{"timestampUtc":"2026-09-09T00:47:51.35Z","tenantId":"1111...","previousOwner":"regionA","newOwner":"regionB","result":"promoted","reason":"consecutive-health-check-failures","consecutiveFailures":3,"healthUrl":"http://127.0.0.1:5501/health","triggeredBy":"javie","pid":34232,"machine":"VICTUS"}
{"timestampUtc":"2026-09-09T00:48:18.05Z","tenantId":"1111...","previousOwner":null,"newOwner":"regionA","result":"aborted-not-confirmed","reason":"missing-explicit-confirmation","consecutiveFailures":0,"healthUrl":null,"triggeredBy":"javie","pid":25872,"machine":"VICTUS"}
{"timestampUtc":"2026-09-09T00:48:18.30Z","tenantId":"1111...","previousOwner":"regionB","newOwner":"regionA","result":"promoted","reason":"manual-confirmed","consecutiveFailures":0,"healthUrl":null,"triggeredBy":"javie","pid":31380,"machine":"VICTUS"}
{"timestampUtc":"2026-09-09T00:48:33.53Z","tenantId":"1111...","previousOwner":"regionA","newOwner":"regionB","result":"promoted","reason":"consecutive-health-check-failures","consecutiveFailures":2,"healthUrl":"http://127.0.0.1:5501/health","triggeredBy":"javie","pid":40216,"machine":"VICTUS"}
{"timestampUtc":"2026-09-09T00:48:41.37Z","tenantId":"1111...","previousOwner":"regionB","newOwner":"regionB","result":"no-op-already-owner","reason":"manual-confirmed","consecutiveFailures":0,"healthUrl":null,"triggeredBy":"javie","pid":38860,"machine":"VICTUS"}
```

Cada línea, incluyendo los intentos abortados por falta de `--confirm`, quedó registrada — quien intentó forzar el failover sin confirmación queda igual de trazado que quien lo ejecutó exitosamente (`triggeredBy`, `pid`, `machine`, `timestampUtc`), cumpliendo el requisito de "registro auditable de quién/qué disparó el failover".

---

## 5. Runbook operativo (referencia, no un compromiso de infraestructura productiva)

Para un despliegue real, el orden de comandos es el mismo que en la sección 4, sustituyendo `tenant-region-map.json` por el archivo respaldado por el `ConfigMap`/volumen real que monta cada instancia del Gateway (`Regional:TenantRegionAssignments`, ver `docs/routing-regional-gateway.md` sección 2.2), y `--health-url` por el endpoint de salud real de la región (p. ej. el `/health` del Gateway o del balanceador de la región):

```bash
# 1. Un monitor externo (fuera de este repositorio: Kubernetes liveness externo, Prometheus Alertmanager,
#    o un cron/systemd timer) detecta que la región primaria no responde y decide iniciar el workflow.
# 2. Un operador humano (o un runbook de incident response con aprobación) ejecuta:
RegionalFailoverHarness failover /config/tenant-region-map.json /var/log/bitcode/regional-failover-audit.jsonl \
    <tenantId> <regionSecundaria> \
    --health-url https://gateway.<region-primaria>.bitcode.internal/health \
    --threshold 5 --interval-seconds 10 --confirm

# 3. Verificar que el tráfico ya fluye hacia la región secundaria (curl/monitoreo real).
# 4. Cuando la región primaria se recupera y la replicación de datos (F5-04/F5-05) confirma
#    consistencia, F5-11 (Failback) ejecuta la reconciliación y SOLO ENTONCES se corre el
#    mismo comando failover en sentido inverso -- nunca antes de confirmar que no hay pérdida de datos.
```

**Aprobación humana obligatoria (sección 13 del Plan Maestro)**: la ejecución de este comando contra el mapa real que gobierna tráfico de producción constituye "habilitación de tráfico productivo"/"failover productivo" y requiere el mismo circuito de aprobación que cualquier otro cambio de esa categoría — esta herramienta automatiza el *mecanismo* con sus salvaguardas técnicas, no elimina la necesidad de una decisión humana explícita para ejecutarlo contra un entorno productivo real.

---

## 6. Verificación del criterio de aceptación ("Ejecución repetible")

- **Workflow entregado**: `tools/RegionalFailoverHarness` implementa detección (health-check con umbral de fallos consecutivos) + promoción (reescritura atómica del mapa de ownership) + controles de seguridad (confirmación explícita, ventana de gracia) + auditoría (log append-only).
- **Evidencia real, no simulada**: dos procesos de sistema operativo reales (PIDs reales, `taskkill /F` real, HTTP real sobre `localhost`), sección 4.
- **Repetibilidad demostrada empíricamente**: el ciclo failover → failback → failover se ejecutó dos veces completas sin reiniciar el proceso de la región B y sin dejar el mapa ni la auditoría en un estado inconsistente (secciones 4.5 a 4.8).
- **Brechas explícitas documentadas, no encubiertas**: sección 2.5.

**Conclusión:** criterio de aceptación de F5-10 ("Ejecución repetible") cumplido con evidencia real para el mecanismo entregado por esta tarea.

## Referencias

- [`plan-maestro-bitcode-ia.md`](plan-maestro-bitcode-ia.md) — backlog F5-10, sección 13 (aprobaciones humanas: failover/failback productivo).
- [`mapa-ownership-regional.md`](mapa-ownership-regional.md) — F5-02, `RegionId`/`ITenantRegionMapStore`/`IRegionalOwnershipResolver`/`ICurrentRegionProvider`.
- [`routing-regional-gateway.md`](routing-regional-gateway.md) — F5-03, `RegionalOwnershipRoutingMiddleware`, `ConfigurationTenantRegionMapStore`.
- [`politica-configuracion-y-feature-flags.md`](politica-configuracion-y-feature-flags.md) sección 2.4 — F4-12, mismo mecanismo de hot-reload (`AddJsonFile(reloadOnChange: true)`) reutilizado acá.
- [`politica-backups-fase5.md`](politica-backups-fase5.md) sección 4.1 — F5-07, mismo razonamiento de "automatización desacoplada del proceso que protege".
- [`guia-quartz-ha.md`](guia-quartz-ha.md) sección 6.1 — F4-11, mismo estándar de evidencia con dos procesos de SO reales.
- `tools/RegionalFailoverHarness/Program.cs` — implementación del workflow verificado en la sección 4.
