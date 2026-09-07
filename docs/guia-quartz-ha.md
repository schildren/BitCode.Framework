# Quartz HA — JobStore persistente, clustering, misfire e idempotencia (F4-11)

**Tarea:** F4-11 (Fase 4 — Runtime de alta disponibilidad) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Fecha:** 2026-09-07.
**Estado:** Completada — `AddSharedBackgroundJobs` soporta un `AdoJobStore` persistente clusterizado
sobre SQL Server, verificado con dos schedulers Quartz.NET reales compartiendo el mismo SQL Server
(Testcontainers). El pendiente de recovery con dos procesos de sistema operativo reales (dejado
explícitamente abierto por la versión original de esta tarea) se cerró el 2026-09-07 — ver sección
6.1.

---

## 1. Qué cambió

Antes de esta tarea, `AddSharedBackgroundJobs` (`Shared.Infrastructure.BackgroundJobs`) registraba
Quartz.NET sin llamar a `UsePersistentStore(...)` — Quartz usaba por tanto `RAMJobStore`: triggers,
estado de ejecución y misfires vivían únicamente en la memoria del proceso. Esto fue diagnosticado
como hallazgo real (no corregido en ese momento, alcance reservado a esta tarea) por la auditoría de
F4-03 (`docs/auditoria-estado-runtime-f4-03.md`, sección 4): con más de una réplica corriendo el
mismo `IJob`/trigger, cada instancia lo dispararía de forma independiente (duplicación de efectos),
porque `RAMJobStore` no coordina entre procesos; y si un pod moría a mitad de la ejecución de un job,
su estado se perdía sin ningún mecanismo de recuperación.

F4-11 agrega un overload de `AddSharedBackgroundJobs` que, cuando se invoca, cambia el `JobStore` de
Quartz de `RAMJobStore` a `AdoJobStore` persistente sobre SQL Server, con clustering habilitado por
defecto — el mecanismo que garantiza el criterio de aceptación de esta tarea: **un job lógico
programado una sola vez se dispara una única vez, sin importar cuántas réplicas del proceso estén
corriendo**.

## 2. Cómo habilitarlo

```csharp
services.AddSharedBackgroundJobs(
    quartz =>
    {
        var jobKey = new JobKey("recalcular-saldos");
        quartz.AddJob<RecalcularSaldosJob>(job => job
            .WithIdentity(jobKey)
            .RequestRecovery()      // ver sección 5 — recomendado para todo job con efectos en SQL Server
            .StoreDurably());
        quartz.AddTrigger(trigger => trigger
            .WithIdentity("recalcular-saldos-trigger")
            .ForJob(jobKey)
            .WithCronSchedule("0 0 * * * ?"));
    },
    ha =>
    {
        ha.ConnectionString = configuration.GetConnectionString("Default")!;
        // ha.Clustered = true (default) — no lo desactive con más de una réplica.
    });
```

Omitir el segundo parámetro (`configureHighAvailability`) conserva el comportamiento anterior
(`RAMJobStore`, apto solo para un único proceso o para pruebas) — no rompe ningún consumidor
existente. `samples/Sample.Api` no llama a `AddSharedBackgroundJobs` hoy (no registra ningún `IJob`,
confirmado en `InfrastructureModule.cs` — ver F4-03 sección 4), así que no hay ningún manifiesto de
`k8s/sample-api/` que actualizar; esta sección documenta cómo lo haría un consumidor real.

### 2.1. Opciones (`QuartzHighAvailabilityOptions`)

| Propiedad | Default | Qué controla |
|---|---|---|
| `ConnectionString` | obligatorio | Cadena de conexión a SQL Server donde viven las tablas `QRTZ_*`. |
| `TablePrefix` | `"QRTZ_"` | Prefijo de las tablas — debe coincidir con el esquema aplicado (sección 3). |
| `SchedulerName` | `null` (default de Quartz.NET, `"QuartzScheduler"`) | Debe ser IDÉNTICO en todas las réplicas que deban coordinarse — Quartz agrupa un cluster por este nombre + filas de `QRTZ_SCHEDULER_STATE`. |
| `Clustered` | `true` | `quartz.jobStore.clustered`. Ponerlo en `false` es válido solo para una única instancia con persistencia (sobrevive a un reinicio del proceso) sin coordinación — **no usar con más de una réplica activa**. |
| `MisfireThreshold` | 60s | Cuánto tiempo después de la hora programada un trigger que no pudo dispararse se considera "misfire". |
| `ClusterCheckinInterval` | 10s | Cada cuánto un nodo anuncia que sigue vivo en `QRTZ_SCHEDULER_STATE`. |
| `ClusterCheckinMisfireThreshold` | 20s | Margen adicional antes de que otro nodo considere a un nodo sin check-in reciente como caído (y dispare recovery). |

## 3. Esquema `QRTZ_*`

El `AdoJobStore` necesita que las tablas `QRTZ_*` existan de antemano en la base de datos — Quartz.NET
no las crea automáticamente. El script versionado está en
`src/Shared.Infrastructure.BackgroundJobs/Schema/quartz-sqlserver-schema.sql`, adaptado del script
oficial de Quartz.NET 3.13.1 (ver comentario de cabecera de ese archivo para el detalle de qué se
removió del script oficial y por qué: la sentencia `USE [enter_db_name_here]` y el bloque que por
defecto DROPea y recrea las tablas en cada ejecución — ambos incompatibles con un pipeline de
despliegue repetible contra un scheduler ya productivo).

**Cómo aplicarlo:**

- **Pruebas de integración:** `QuartzSqlServerSchemaInitializer.ApplySchemaIfMissingAsync(connectionString)`
  (`Shared.Infrastructure.BackgroundJobs`) — comprueba si `QRTZ_JOB_DETAILS` ya existe y, si no, aplica
  el script embebido. Seguro de invocar más de una vez (no falla ni hace nada si el esquema ya existe).
  Ver `tests/Shared.Infrastructure.BackgroundJobs.IntegrationTests/Integration/QuartzHighAvailabilityIntegrationTests.cs`.
- **Un consumidor real (producción):** aplicar el script (directamente, o llamando al mismo
  `QuartzSqlServerSchemaInitializer.ApplySchemaIfMissingAsync`) como un paso EXPLÍCITO del pipeline de
  despliegue, ANTES del rollout de los pods que corren el scheduler — nunca desde `Program.cs` de cada
  réplica. Igual que ya señala `docs/auditoria-estado-runtime-f4-03.md` (sección 5) para
  `EnsureCreatedAsync`: N pods arrancando en paralelo y aplicando el mismo DDL al mismo tiempo
  competirían por crear el mismo esquema. Un job de migración dedicado (`dotnet run` de una imagen
  mínima, o un `Job`/`initContainer` de Kubernetes que corre una sola vez antes del `Deployment`) es el
  lugar correcto.

## 4. Misfire

`MisfireThreshold`/`ClusterCheckinMisfireThreshold` (sección 2.1) definen CUÁNDO Quartz considera que
un trigger "se perdió" su hora de disparo. QUÉ hacer en ese caso lo define la política de misfire del
propio trigger — por ejemplo:

```csharp
quartz.AddTrigger(trigger => trigger
    .ForJob(jobKey)
    .WithCronSchedule("0 0 * * * ?", cron => cron
        .WithMisfireHandlingInstructionFireAndProceed())); // dispara una vez de inmediato, sigue el cron normal
```

Un job idempotente (sección 5) puede usar con seguridad
`WithMisfireHandlingInstructionFireAndProceed`. Un job NO idempotente (por ejemplo, uno que envía una
notificación externa sin deduplicar) debe evaluar `WithMisfireHandlingInstructionDoNothing` en su
lugar — disparar tarde puede seguir siendo incorrecto aunque no sea "duplicado" en sentido estricto.

## 5. Idempotencia de jobs — patrón recomendado, no un mecanismo nuevo

El clustering de Quartz (sección 2) resuelve la coordinación de EJECUCIÓN entre nodos: un job lógico
se dispara una sola vez. No resuelve, por sí solo, qué pasa si ESE disparo se ejecuta parcialmente y
Quartz decide reintentarlo (misfire tras una caída, o recovery — ver más abajo). Esa parte es
responsabilidad del propio `IJob`, con las mismas herramientas que el framework ya expone para
comandos HTTP:

- **`.RequestRecovery()`** al registrar el job (`AddJob<TJob>(j => j.RequestRecovery())`) — si el nodo
  que estaba ejecutando ese job muere sin completar (crash, no shutdown ordenado), Quartz reintenta la
  ejecución en el nodo que detecta la caída (ver `QRTZ_FIRED_TRIGGERS.REQUESTS_RECOVERY`), pasando
  `IJobExecutionContext.Recovering == true`. Un `Execute` que no tolera una segunda invocación con el
  mismo efecto de negocio NO debe marcarse `RequestRecovery` sin antes hacerse idempotente.
- **Reutilizar `IIdempotencyStore`/`AddSharedIdempotency`** (`Shared.Domain.Idempotency`/
  `Shared.Infrastructure.Persistence.Idempotency`, F1-22) dentro del `Execute` del job, con una clave
  determinística derivada del disparo lógico (por ejemplo, `JobKey.Name` + el intervalo de tiempo que
  el job procesa, NO `context.FireInstanceId`, que es distinto en cada intento) — mismo patrón que ya
  usa un `IRequestHandler` HTTP para deduplicar un reintento del cliente. Ver el comentario de
  `IIdempotencyStore` (`src/Shared.Domain/Idempotency/IIdempotencyStore.cs`) que ya señalaba este
  cableado como pendiente de F4-11.
- Preferir efectos naturalmente idempotentes cuando sea posible: un `UPDATE ... WHERE` que fija un
  estado final (no un `INSERT` puro), o un `INSERT` protegido por una clave única de negocio (mismo
  patrón que la Outbox/Inbox, F1-23/F3-04) en vez de una fila sin restricción.

**Gap identificado, no resuelto en esta tarea:** el framework no impone (ni puede imponer en tiempo
de compilación) que un `IJob` sea idempotente — es una convención documentada acá, verificada por
revisión de código, no un tipo/interfaz nueva. Introducir una abstracción `IIdempotentJob` o
equivalente sería una decisión de diseño mayor (cambia cómo se registran TODOS los jobs futuros) que
excede el alcance de F4-11 y no tiene todavía un caso de uso real en el repositorio (no hay ningún
`IJob` de negocio registrado hoy, solo el job de prueba usado para verificar el clustering). Queda
como trabajo futuro si/cuando un consumidor real necesite más de un job de negocio.

## 6. Verificación (evidencia del criterio de aceptación)

`tests/Shared.Infrastructure.BackgroundJobs.IntegrationTests/Integration/QuartzHighAvailabilityIntegrationTests.cs`
levanta DOS `ServiceProvider`/`IScheduler` de Quartz.NET reales, ambos con
`AddSharedBackgroundJobs(..., ha => ha.Clustered = true)` apuntando al MISMO SQL Server real
(Testcontainers), con el MISMO `JobKey`/`TriggerKey` registrado en ambos. El job escribe una fila en
una tabla de tracking por cada disparo real; el test confirma que, tras iniciar ambos schedulers casi
simultáneamente y esperar a que el trigger dispare, la tabla tiene EXACTAMENTE una fila — no una por
nodo.

Como control negativo (usado para validar el test durante el desarrollo de esta tarea, no forma parte
de la suite final): el mismo escenario con `configureHighAvailability` omitido (dos `RAMJobStore`
independientes, exactamente el comportamiento previo a esta tarea) produce DOS filas — confirma que
el test realmente distingue "coordinado" de "no coordinado", no que pasa por casualidad.

Este proyecto de test es un proyecto SEPARADO de
`tests/Shared.Infrastructure.BackgroundJobs.Tests` (no una carpeta `Integration/` dentro del mismo
proyecto, que es el patrón por defecto del resto del repositorio) por una limitación real y ya
documentada de Quartz.Extensions.Hosting: `Quartz.Logging.LogProvider` cachea de forma estática el
`ILoggerFactory` del primer `Host`/`ServiceProvider` con Quartz creado en el proceso, y no lo libera
al disponerlo — un segundo scheduler creado en el MISMO proceso de test después de que el primero se
disponga lanza `ObjectDisposedException`. Como el test de clustering necesita levantar dos schedulers
Quartz reales dentro de un mismo test (para probarlos simultáneamente, no en secuencia), y
`dotnet test` levanta un proceso (`testhost`) por proyecto, separarlo en su propio proyecto evita la
colisión sin modificar el test unitario existente (`BackgroundJobsServiceCollectionExtensionsTests`).

### 6.1. Recovery ante caída de un nodo — verificado empíricamente con dos procesos de SO reales

**Cerrado el 2026-09-07**, como seguimiento explícito del pendiente que dejó F4-11 (texto original de
esta sección conservado más abajo para trazabilidad). El escenario pedido — un job con
`RequestRecovery()` ejecutándose en el nodo A es retomado por el nodo B si el PROCESO de sistema
operativo del nodo A muere a mitad de la ejecución, sin duplicar su efecto de negocio — se verificó
con el mismo patrón de dos procesos reales que `docs/auditoria-estado-runtime-f4-03.md` (sección 2.2)
usó para `samples/Sample.Api`: dos binarios publicados (`dotnet publish -c Release`) ejecutados como
dos procesos de SO independientes, contra un SQL Server 2022 real (contenedor Docker, no
Testcontainers — esta prueba es manual, no forma parte de `dotnet test`), y `taskkill /F` para matar
uno de los dos procesos mientras el job seguía en ejecución.

**Herramienta:** `tools/QuartzHaRecoveryHarness` (nuevo, `dotnet run`/binario publicado independiente
del resto del repositorio — no se agregó a ningún proyecto de test porque no es parte de la suite
automatizada, es un binario de diagnóstico manual conservado como herramienta reutilizable). Expone
tres comandos:

```
QuartzHaRecoveryHarness init   <connectionString>
QuartzHaRecoveryHarness run    <connectionString> <nodeLabel> [workSeconds=30]
QuartzHaRecoveryHarness status <connectionString>
```

`init` crea la base de datos, aplica el esquema `QRTZ_*` (mismo `QuartzSqlServerSchemaInitializer` de
producción) y crea una tabla propia `RecoveryEvidence` (una fila por `JobKey`, con `Status`,
`AttemptCount`, quién la inició/completó y si fue una ejecución de recovery). `run` arranca UN
scheduler Quartz.NET real, clusterizado (`AddSharedBackgroundJobs(..., ha => ha.Clustered = true)`,
mismos parámetros que expone `QuartzHighAvailabilityOptions`, con `ClusterCheckinInterval`/
`ClusterCheckinMisfireThreshold` reducidos a 3s/6s solo para que la prueba manual no tarde minutos —
mismo criterio que ya aplica `QuartzHighAvailabilityIntegrationTests`), registra un único
`RecoveryProbeJob` (`.RequestRecovery().StoreDurably()`, `JobKey`/`TriggerKey` fijos e idénticos en
ambos procesos) y queda corriendo indefinidamente hasta que el proceso se termine. El job simula
trabajo real con `Task.Delay(workSeconds)` entre dos escrituras a SQL Server real: al empezar,
un `MERGE` que dedica `RecoveryEvidence.Status = 'Started'` con el `InstanceId`/PID del nodo y si
`context.Recovering == true`; al terminar, un `UPDATE ... WHERE Status <> 'Completed'` que marca
`Status = 'Completed'` — idempotente por diseño (patrón de la sección 5 de este documento / regla
dura 28 de `docs/convenciones.md`): si el job ya está `Completed` al arrancar `Execute`, no repite el
efecto.

**Procedimiento ejecutado:**

1. SQL Server 2022 real en un contenedor Docker dedicado (puerto `15533`), una única base de datos.
2. `QuartzHaRecoveryHarness.dll init` — crea el esquema `QRTZ_*` y `RecoveryEvidence`.
3. Dos procesos de SO reales arrancados por separado desde el mismo binario publicado:
   `QuartzHaRecoveryHarness.dll run <connStr> NodeA 30` (PID `30140`) y
   `QuartzHaRecoveryHarness.dll run <connStr> NodeB 30` (PID `14384`), ambos apuntando al mismo SQL
   Server, mismo `JobKey`/`TriggerKey`, arrancados casi simultáneamente.
4. El trigger disparó a los 5s de ambos arranques; por coordinación de clustering (`QRTZ_LOCKS`), solo
   **NodeB** lo ejecutó (`Recovering=False`, log `18:33:52.110`).
5. A los ~3.7s de iniciado el trabajo simulado (30s), se mató el proceso de NodeB con
   `taskkill /F /PID 14384` (`18:34:06`) — equivalente exacto a `SIGKILL`/eliminación forzada de un
   pod, no un shutdown ordenado.
6. NodeA, el nodo superviviente, detectó la caída vía su `ClusterManager` (log real):
   ```
   18:34:14.017 ClusterManager: detected 1 failed or restarted instances.
   18:34:14.017 ClusterManager: Scanning for instance "Victus...270108743"'s failed in-progress jobs.
   18:34:14.147 ClusterManager: ......Deleted 1 complete triggers(s).
   18:34:14.147 ClusterManager: ......Scheduled 1 recoverable job(s) for recovery.
   18:34:19.867 Handling 1 trigger(s) that missed their scheduled fire-time.
   18:34:20.053 [NodeA] Job recovery-probe-job INICIADO (pid=30140, Recovering=True) — 30s de trabajo
   18:34:50.072 [NodeA] Job recovery-probe-job COMPLETADO (pid=30140)
   ```
   La detección tardó ~8s desde la muerte del proceso (`ClusterCheckinMisfireThreshold=6s` + margen del
   ciclo de `ClusterManager`), consistente con la configuración de la prueba — en producción, con los
   defaults de `QuartzHighAvailabilityOptions` (10s/20s), la detección tardaría más, por diseño (evita
   falsos positivos por GC/latencia de red transitoria).
7. `QuartzHaRecoveryHarness status` contra SQL Server real, después de que NodeA completó:
   ```
   JobKey=recovery-probe-job Status=Completed AttemptCount=2
   StartedBy=Victus...269973712/pid=30140 StartedAtUtc=2026-09-07 22:34:20 Recovering=True
   CompletedBy=Victus...269973712/pid=30140 CompletedAtUtc=2026-09-07 22:34:50
   ```

**Resultado:** exactamente **una** fila en `RecoveryEvidence` para el job lógico, con `Status =
'Completed'` una única vez y `CompletedBy` apuntando al nodo superviviente (NodeA, no el nodo muerto).
`AttemptCount = 2` refleja los dos `Execute` reales que sí ocurrieron (el intento de NodeB que murió a
mitad de camino sin completar, y el intento de recovery de NodeA que sí completó) — no una duplicación
del efecto de negocio: el efecto observable (`Status = 'Completed'`, una sola vez) es idéntico al que
produciría una ejecución sin caídas. Esto confirma, con procesos de SO reales (no dos schedulers en el
mismo proceso .NET, que es lo que ya cubre `QuartzHighAvailabilityIntegrationTests`), tanto que Quartz
detecta la caída del nodo (`ClusterCheckinMisfireThreshold`) como que `RequestRecovery()` re-dispara
el job en el nodo superviviente (`IJobExecutionContext.Recovering == true`) sin que el patrón de
idempotencia documentado en la sección 5 permita que el efecto de negocio final quede duplicado.

**Limpieza:** ambos procesos de SO (`taskkill /F`) y el contenedor SQL Server (`docker rm -f`) se
destruyeron al finalizar — no queda infraestructura de prueba residual. `tools/QuartzHaRecoveryHarness`
se conserva en el repositorio como herramienta de diagnóstico reutilizable (no se agregó a ningún
pipeline de CI ni a `dotnet test` — se invoca manualmente, igual que este procedimiento, contra
cualquier SQL Server real cuando haga falta repetir la verificación).

<details>
<summary>Texto original de esta sección (pendiente antes del 2026-09-07), conservado para trazabilidad</summary>

El enunciado de la tarea pedía, "si es viable", verificar también que un job con `RequestsRecovery()`
sea recuperado por el nodo superviviente si el nodo que lo ejecutaba muere a mitad de la ejecución.
Se evaluó y se decidió NO incluir un test automatizado de ese escenario en esta tarea: simular una
caída "dura" (equivalente a `SIGKILL`/eliminación forzada de un pod, como sí se hizo con procesos de
sistema operativo reales en `docs/auditoria-estado-runtime-f4-03.md` sección 2.2) requiere dos
PROCESOS de sistema operativo separados — dentro de un único proceso de test .NET no hay forma
confiable de "matar" un `IScheduler` a mitad de una ejecución sin que eso termine siendo, en la
práctica, un `Shutdown()` ordenado (que limpia el estado de `QRTZ_SCHEDULER_STATE` correctamente y no
ejercita el camino de recovery real). El mecanismo en sí (`REQUESTS_RECOVERY`, detección de nodo
caído vía `ClusterCheckinMisfireThreshold`, re-disparo con `IJobExecutionContext.Recovering == true`)
es funcionalidad estándar y ampliamente probada de Quartz.NET/`AdoJobStore`, no código nuevo de este
framework — lo que SÍ es responsabilidad de esta tarea (y queda verificado) es que el framework
exponga `RequestRecovery()` correctamente cableado y que el `AdoJobStore` esté configurado con
clustering real. Verificar el comportamiento de recovery de punta a punta con un escenario de dos
procesos reales queda como pendiente explícito, análogo en espíritu a la prueba de dos instancias
reales de `samples/Sample.Api` de F4-03, pero fuera del alcance mínimo de esta tarea.

</details>

## Referencias

- [`plan-maestro-bitcode-ia.md`](plan-maestro-bitcode-ia.md) — Fase 4, fila F4-11.
- [`auditoria-estado-runtime-f4-03.md`](auditoria-estado-runtime-f4-03.md) — sección 4, diagnóstico original de `RAMJobStore` sin persistencia.
- [`politica-dependencias.md`](politica-dependencias.md) sección 5.4 — evaluación de `Quartz.Serialization.SystemTextJson`/`Microsoft.Data.SqlClient`.
- `src/Shared.Infrastructure.BackgroundJobs/BackgroundJobsServiceCollectionExtensions.cs` — implementación.
- `src/Shared.Infrastructure.BackgroundJobs/QuartzHighAvailabilityOptions.cs` — opciones.
- `src/Shared.Infrastructure.BackgroundJobs/QuartzSqlServerSchemaInitializer.cs` / `Schema/quartz-sqlserver-schema.sql` — esquema `QRTZ_*`.
- `tests/Shared.Infrastructure.BackgroundJobs.IntegrationTests/Integration/QuartzHighAvailabilityIntegrationTests.cs` — evidencia del criterio de aceptación (clustering, dos schedulers en el mismo proceso .NET).
- `tools/QuartzHaRecoveryHarness/Program.cs` — herramienta de diagnóstico manual usada para la verificación de recovery con dos procesos de SO reales (sección 6.1).
