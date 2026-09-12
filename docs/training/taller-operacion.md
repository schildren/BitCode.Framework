# Taller de Operación — Diagnóstico, migraciones y respuesta a incidentes

**Audiencia:** personas que operan BitCode.Framework en el día a día (no necesariamente quienes escriben
el código de negocio).
**Objetivo de aprendizaje:** al terminar este taller vas a poder diagnosticar el estado del entorno con
`BitCode.Diagnostics`, aplicar/consultar migraciones con `BitCode.Migrations`, seguir un runbook real ante
un incidente de mensajería simulado, y entender qué dice — y qué NO dice todavía — el ORR de la
plataforma sobre qué tan lista está para operar en un entorno real.

**Duración estimada:** 60-90 minutos.

**Cómo se construyó este taller:** los comandos y salidas de las secciones 1 a 3 están tomados de
verificaciones reales ya documentadas y con evidencia citada textualmente: `docs/guia-cli-diagnostico.md`
(F8-12, incluida una falla real inducida — contenedor de Redis detenido a propósito y CLI detectándolo),
`docs/guia-migraciones.md` (F8-07/F9-08, con las dos limitaciones reales de `--assembly`/
`IDesignTimeDbContextFactory` verificadas el 2026-09-12 en `docs/revision-documental-fase10.md`), y
`docs/runbook-dlq.md` (F3-08/F3-09, procedimiento con pruebas de integración reales contra Kafka).

## 0. Prerrequisitos concretos

- **Docker Desktop** (o Docker Engine + Docker Compose v2) corriendo — el entorno local reproducible de
  F8-11 (`docker-compose.yml`) levanta SQL Server, Redis, Kafka (KRaft), el OTel Collector y Jaeger.
- **.NET SDK 10** (mismo prerrequisito que el taller de backend).
- Un checkout del repositorio `BitCode.Framework`.
- Acceso de lectura a `docs/orr-plataforma.md` — vas a necesitarlo en la sección 4, no hace falta
  memorizarlo antes.

### Checkpoint 0

```powershell
docker --version
docker compose version
dotnet --version
```

## 1. Levantar el entorno local y diagnosticarlo (`BitCode.Diagnostics`, F8-12)

```powershell
.\scripts\dev-env.ps1 up
```

(En Linux/macOS: `docker compose up -d`.) Esto levanta los 5 contenedores del entorno local (SQL Server,
Redis, Kafka, OTel Collector, Jaeger).

### Checkpoint 1.1 — diagnóstico completo

```powershell
.\scripts\doctor.ps1
```

(Equivalente directo: `dotnet run --project tools/BitCode.Diagnostics -- doctor`.) Con el entorno sano
deberías ver únicamente `[OK]` en las tres dimensiones (`tools`/`config`/`connectivity`), salvo
advertencias esperadas de variables de entorno no configuradas (`[WARN] ConnectionStrings__DefaultConnection
... Fallback: localhost,1433` es normal si no seteaste esa variable explícitamente — el fallback sigue
siendo válido para desarrollo local).

### Checkpoint 1.2 — inducir una falla real y ver la remediación sugerida

Este es el checkpoint que de verdad enseña algo (no solo confirma que "todo está verde"):

```powershell
docker stop bitcode-redis
dotnet run --project tools/BitCode.Diagnostics -- connectivity
```

Deberías ver algo como:

```
[WARN] Redis 7.2    No se pudo conectar a Redis 7.2 en 127.0.0.1:6379. ...
💡 Acciones de Remediación Sugeridas:
  1. [Redis 7.2]: Inicie el contenedor con: .\scripts\dev-env.ps1 up (o docker compose up -d redis).
```

Restaurá el contenedor y confirmá que vuelve a `[OK]`:

```powershell
docker start bitcode-redis
dotnet run --project tools/BitCode.Diagnostics -- connectivity
```

**Por qué este ejercicio importa:** en un incidente real, el primer paso no es adivinar qué está caído —
es correr `doctor`/`connectivity` y leer la remediación sugerida antes de escalar.

## 2. Migraciones con `BitCode.Migrations` (F8-07) — y su limitación real

### 2.1 El error que vas a encontrar si seguís el ejemplo "obvio"

Si compilás un host real (por ejemplo `samples/Sample.Api`) y corrés `validate` apuntando directo al
`.dll` del host, **falla siempre** — no es un error tuyo:

```powershell
dotnet build samples/Sample.Api
dotnet run --project tools/BitCode.Migrations -- validate --assembly "samples/Sample.Api/bin/Debug/net10.0/Sample.Api.dll"

# [ERROR FATAL]: Unable to load one or more of the requested types.
# Could not load file or assembly 'Microsoft.AspNetCore, Version=10.0.0.0, ...'.
```

**Causa (no la ignores, entendela):** `BitCode.Migrations` es un ejecutable de consola simple que carga el
ensamblado con `Assembly.LoadFrom`. Un proyecto `Microsoft.NET.Sdk.Web` (cualquier host real de este
framework) asume el *shared framework* `Microsoft.AspNetCore.App` disponible en el proceso que lo carga —
el proceso de consola de `BitCode.Migrations` no lo referencia.

### Checkpoint 2.1 — aplicar el workaround verificado

```powershell
dotnet build tools/BitCode.Migrations
dotnet build samples/Sample.Api

dotnet exec `
  --runtimeconfig samples/Sample.Api/bin/Debug/net10.0/Sample.Api.runtimeconfig.json `
  --depsfile samples/Sample.Api/bin/Debug/net10.0/Sample.Api.deps.json `
  tools/BitCode.Migrations/bin/Debug/net10.0/BitCode.Migrations.dll `
  validate --assembly "samples/Sample.Api/bin/Debug/net10.0/Sample.Api.dll"
```

Debe pasar de la excepción de carga de ensamblado al mensaje de negocio correcto ("No se encontraron
clases de migración en 'Sample.Api'" si el host no tiene migraciones generadas todavía, o el resultado real
de la validación si las tiene). Este mismo patrón (`--runtimeconfig`/`--depsfile` del host real) aplica a
`status`/`migrate`/`rollback`/`script` — reemplazá `--assembly`/`validate` según el comando que necesites.

### 2.2 Segunda limitación: `DbContext` multi-tenant

Si tu host usa `MultiTenantDbContext`/`MultiTenantIdentityDbContext<,>` (el patrón estándar, constructor
con `ITenantProvider` además de `DbContextOptions<T>`), `status`/`migrate`/`rollback` (no `validate`, que
no instancia el `DbContext`) fallan con `No se pudo instanciar el DbContext '<Nombre>'` si el proyecto no
implementa `IDesignTimeDbContextFactory<TContext>` — mismo criterio que `dotnet ef` (ver
`docs/guia-migraciones.md` sección 3.2).

### Checkpoint 2.2

Confirmá que entendés la diferencia: `validate` (análisis estático de las clases de migración compiladas,
sin conectarse a la base) es el único comando que NO necesita el `DbContext` instanciado — es el que
podés correr en un pipeline de CI sin acceso a una base de datos real (ver `docs/guia-migraciones.md`
sección 5, "Rollout en CI/CD y Kubernetes").

## 3. Runbook real: Dead-Letter Queue de eventos de integración

Seguí `docs/runbook-dlq.md` completo, sección "Procedimiento operativo", contra un módulo que publique
eventos de integración vía Outbox/Kafka. Resumen del ejercicio (no reemplaza leer el runbook completo):

### Checkpoint 3.1 — identificar mensajes agotados

```sql
SELECT Id, TenantId, EventType, Error, RetryCount, ExhaustedAtUtc, OccurredAtUtc
FROM OutboxMessage
WHERE ExhaustedAtUtc IS NOT NULL
ORDER BY ExhaustedAtUtc DESC;
```

**Nota de nomenclatura (fricción real, ya corregida en la documentación, pero repasala):** el nombre de
tabla real es `OutboxMessage` (singular), no `OutboxMessages` — `OutboxMessage`/`InboxMessage`/
`IdempotencyKey` no se exponen como `DbSet<T>` con nombre propio en `MultiTenantDbContext`, así que EF Core
usa el nombre singular del tipo CLR por convención (a diferencia de `RefreshTokens`, que sí es plural
porque sí tiene su propio `DbSet`). Si copiás una consulta SQL de memoria o de una fuente vieja y te da
`Invalid object name 'OutboxMessages'`, esta es la causa — ver
`docs/revision-documental-fase10.md` sección 2.5 para el detalle completo.

### Checkpoint 3.2 — decidir y reprocesar

```csharp
var reprocessor = serviceProvider.GetRequiredService<IDeadLetterReprocessor>();
var result = await reprocessor.ReprocessAsync(new DeadLetterReprocessRequest(
    outboxMessageId: outboxMessageId,
    actor: new AuditActor(operatorId, AuditActorType.User),
    reason: "Causa raíz corregida: <describí la causa real, INC-XXXX si aplica>."));
```

Confirmá el resultado (`result.IsSuccess`/`result.Error.Code`) y que la fila vuelve a ser candidata de
publicación (`ExhaustedAtUtc` en `null` otra vez).

### Checkpoint 3.3 — verificar la auditoría

Consultá la auditoría por `Resource.Type = "OutboxMessage"` y `Resource.Id = <outboxMessageId>` y confirmá
que existe una entrada `Action = "OutboxMessage.DeadLetterReprocess"` con el actor y el motivo (`Reason`)
que usaste — **nunca** des un reprocesamiento por hecho sin este paso: `docs/runbook-dlq.md` documenta
explícitamente que la fila NUNCA se reabre si la auditoría falla primero (orden "auditar primero, mutar
después").

## 4. Qué dice el ORR de la plataforma sobre qué tan lista está para operar (lectura obligatoria, no ejercicio)

Antes de considerarte "habilitado" para operar un entorno real (no solo local), leé
`docs/orr-plataforma.md` completo — es un checklist honesto, no una lista para tranquilizarte. Puntos
clave que este taller no puede resolver por vos:

- **No hay dashboards de métricas ni alerting conectado a un canal real** (Slack/PagerDuty/Opsgenie) — hoy
  la detección de una degradación depende de que una persona esté consultando activamente `/health/ready`
  o las trazas de Jaeger/Tempo.
- **No hay on-call real ni `CODEOWNERS`** — este taller te habilita técnicamente en las herramientas, pero
  no resuelve la ausencia de una organización operativa real (fuera del alcance de cualquier material de
  entrenamiento).
- **Solo Workflow tiene runbooks de operación propios** (4 runbooks A-D, `docs/runbook-workflow.md`) — los
  otros 11 módulos de plataforma dependen de los runbooks genéricos (`runbook-dlq.md`,
  `runbook-pitr-fase5.md`) y del patrón compartido de health checks. Si operás un módulo sin runbook
  propio, generalizá el patrón de `runbook-workflow.md` en vez de empezar de cero.

### Checkpoint 4 (final del taller)

Después de leer `docs/orr-plataforma.md`, deberías poder responder sin volver a mirar el documento:

1. ¿Qué tres categorías del checklist están completamente "Listo" hoy? (Pista: health checks, y el
   tooling de diagnóstico/migraciones que ya usaste en este taller.)
2. ¿Qué decisión de la sección 2.7 (secretos/KMS) requiere aprobación humana explícita antes de avanzar?
3. ¿Por qué el documento dice explícitamente que NO otorga la aprobación de Operaciones, aunque se llame
   "checklist ORR"?

Si podés responder las tres, completaste el taller de operación con una lectura crítica del estado real
de la plataforma, no solo con la ejecución mecánica de comandos.

## 5. Problemas comunes (consolidado de las tres secciones)

| Problema | Causa | Solución |
|---|---|---|
| `[WARN]` de conectividad para un servicio recién levantado | El contenedor puede tardar unos segundos en pasar a `healthy` después de `docker compose up -d`. | Reintentar `doctor`/`connectivity` después de unos segundos, o `docker compose ps` para confirmar el estado real del contenedor. |
| `BitCode.Migrations` falla con `Could not load file or assembly 'Microsoft.AspNetCore...'` | `--assembly` apunta a un host `Microsoft.NET.Sdk.Web`, cargado por reflexión desde un proceso de consola que no referencia el *shared framework*. | Usar `dotnet exec --runtimeconfig/--depsfile` del host real (sección 2.1). |
| `BitCode.Migrations status/migrate/rollback` falla con "No se pudo instanciar el DbContext" | `DbContext` multi-tenant sin `IDesignTimeDbContextFactory<TContext>`. | Implementar la factory (sección 2.2, `docs/guia-migraciones.md`). |
| Una consulta SQL de un runbook/guía vieja da `Invalid object name 'OutboxMessages'` (o `InboxMessages`/`IdempotencyKeys`) | Nombre de tabla real es singular (`OutboxMessage`), no plural — inconsistencia de documentación ya corregida en los documentos vigentes, pero puede seguir en comentarios de código fuente (`///`, ver `docs/revision-documental-fase10.md` sección 2.5, 12 archivos pendientes de limpieza, fuera de alcance de F10-07/F10-09). | Usar siempre el nombre singular al escribir una consulta SQL manual; si encontrás una referencia en plural en un documento vigente, es un hallazgo a reportar (no debería quedar ninguna después de F10-07). |
| Un reprocesamiento de DLQ "no aparece" reflejado en la base | Se saltó el paso de confirmar la auditoría (sección 3.3) antes de asumir éxito, o la auditoría falló y por diseño la fila nunca se reabrió. | Revisar el `Result` devuelto por `ReprocessAsync` (`IsFailure`/`Error.Code`) antes de asumir que la operación tuvo efecto. |

## 6. Qué sigue después de este taller

- Para el ciclo completo de despliegue de migraciones en Kubernetes (Job pre-deploy, `RollingUpdate`), ver
  `docs/guia-migraciones.md` sección 5.
- Para el runbook completo del módulo piloto de extracción de microservicio (Workflow), ver
  `docs/runbook-workflow.md` — es el modelo a seguir si tenés que escribir un runbook nuevo para otro
  módulo.
- Para el procedimiento de restore a un instante exacto (PITR) de SQL Server, ver
  `docs/runbook-pitr-fase5.md`.
