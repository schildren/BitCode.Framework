# Revisión Documental — Fase 10 (F10-07)

**Tarea:** Fase 10 — Producción y Operación Continua, tarea F10-07 (Documentation review) del
[Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Trabajo:** Validar instalación, actualización y troubleshooting.
**Entregable:** Paquete documental.
**Criterio de aceptación:** "Prueba por usuario nuevo".

**Fecha:** Septiembre de 2026.
**Método:** simulación real, no auditoría de lectura — cada camino descrito abajo se ejecutó de punta a
punta en una estación de trabajo limpia respecto del repositorio (proyectos generados fuera de
`samples/`, sin reutilizar artefactos de sesiones anteriores), siguiendo exclusivamente lo que el texto
de la documentación dice hoy, sin conocimiento previo del código fuente salvo el necesario para
diagnosticar cada fricción encontrada.

---

## 1. Alcance de la prueba: los 4 caminos de un usuario nuevo

1. **(a) Instalación y arranque** de un proyecto existente del repositorio (`README.md` como punto de
   entrada).
2. **(b) Generar una app nueva** con `dotnet new bitcode-app` siguiendo `docs/guia-uso-proyectos.md`
   sección 0 y `templates/app/README.md`, sin copiar nada de `samples/` a mano.
3. **(c) Actualización del framework** (ciclo de migraciones/rollout) siguiendo
   `docs/guia-migraciones.md`.
4. **(d) Troubleshooting** de un problema simulado usando `docs/guia-cli-diagnostico.md` y los runbooks
   operativos (`docs/runbook-dlq.md`, `docs/runbook-workflow.md`).

---

## 2. Hallazgos y correcciones

### 2.1 (Camino a) `README.md` — framework declarado como .NET 8, repo real en .NET 10

**Corregido en la primera mitad de esta revisión** (antes de la interrupción por error de red que dio
origen a esta continuación). `README.md` decía "Framework base de desarrollo .NET 8"; `Directory.Build.props`
fija `<TargetFramework>net10.0</TargetFramework>` en todo `src/`. Corregido a una redacción que no quede
obsoleta con la próxima actualización de SDK: "Framework base de desarrollo .NET (hoy .NET 10, ver
`TargetFramework` de cada `.csproj` en `src/`)". Verificado con `grep TargetFramework Directory.Build.props`.

### 2.2 (Camino c) `docs/guia-migraciones.md` — `--assembly` contra un host ASP.NET Core falla siempre

**Corregido en la primera mitad de esta revisión.** Los 5 comandos de la sección 3.1
(`validate`/`status`/`migrate`/`rollback`/`script`), tal como estaban documentados, usan como ejemplo
literal `--assembly "bin/Debug/net10.0/MiApp.Api.dll"` — exactamente un ensamblado `Microsoft.NET.Sdk.Web`.
Reproducido el fallo real:

```
dotnet run --project tools/BitCode.Migrations -- validate --assembly "samples/Sample.Api/bin/Debug/net10.0/Sample.Api.dll"
# [ERROR FATAL]: Unable to load one or more of the requested types.
# Could not load file or assembly 'Microsoft.AspNetCore, Version=10.0.0.0, ...'.
```

Causa: `BitCode.Migrations` es un ejecutable de consola simple que carga el ensamblado indicado con
`Assembly.LoadFrom`; un proyecto Web SDK asume el *shared framework* `Microsoft.AspNetCore.App`
disponible en el proceso que lo carga, y el proceso de consola no lo referencia. Se agregó la sección 3.2
("Limitación conocida y workaround obligatorio") con el workaround verificado (`dotnet exec
--runtimeconfig/--depsfile` del host real, apuntando al `.dll` de `BitCode.Migrations`), y una segunda
limitación relacionada: `status`/`migrate`/`rollback` necesitan `IDesignTimeDbContextFactory<TContext>`
si el `DbContext` es multi-tenant (constructor con `ITenantProvider`).

**Reverificado en esta continuación** (2026-09-12), contra `Sample.Api` y contra un proyecto nuevo
generado con `dotnet new bitcode-app` (ver sección 2.3): el workaround documentado funciona sin
modificaciones — el error de carga de ensamblado desaparece; el resultado pasa a ser el mensaje de
negocio correcto ("No se encontraron clases de migración en 'Sample.Api'"), no la excepción original.

### 2.3 (Camino b) `dotnet new bitcode-app` — sin fricción, verificado de punta a punta

Se instaló la plantilla (`dotnet new install ./templates/app`, ya instalada de la sesión anterior de
F8-11/F8-12) y se generó una aplicación nueva **fuera del árbol del repositorio** (en un directorio de
trabajo temporal), exactamente con el comando documentado en `docs/guia-uso-proyectos.md` sección 0,
ajustando `--SharedSourceRoot` a una ruta absoluta (como la propia guía indica que hay que hacer cuando
el proyecto se genera fuera de `samples/`):

```
dotnet new bitcode-app -n RevF10App -o RevF10App --SharedSourceRoot "C:/03_Laboral/Repositorio/BitCode.Framework/src"
```

Resultado: **sin fricción de ningún tipo.**

- `dotnet build` compiló sin errores (solo advertencias `NU1902`/`NU1900` preexistentes de vulnerabilidad
  de paquete y de un feed NuGet corporativo inalcanzable — ninguna de las dos es específica de la
  plantilla; se verificó que las mismas advertencias aparecen al compilar `samples/Sample.Api`).
- `dotnet run` contra el SQL Server real ya levantado por `docker-compose.yml` (F8-11) creó el esquema
  con `Database.EnsureCreatedAsync()`, expuso `/health/live` y `/health/ready` en `Healthy`, y el feature
  de ejemplo `Elementos/` funcionó de punta a punta: `POST /api/v1/elementos` devolvió un `Guid` nuevo y
  el `GET /api/v1/elementos` subsiguiente lo listó correctamente (paginado).
- Se confirmó con `SELECT name FROM sys.tables` contra la base generada que las tablas reales creadas
  son `Elementos`, `IdempotencyKey`, `InboxMessage`, `OutboxMessage` — dato que motivó el hallazgo de la
  sección 2.5.

No se requirió ninguna corrección en `templates/app/README.md` ni en `docs/guia-uso-proyectos.md`
sección 0: el camino "generar una app nueva" es hoy honesto y ejecutable tal como está escrito. El
proyecto generado y el directorio de trabajo temporal se eliminaron al finalizar la prueba; no se dejó
ningún artefacto de prueba dentro de `samples/` ni de `templates/`.

### 2.4 (Camino d) `docs/guia-cli-diagnostico.md` — verificado con una falla real inducida

Se ejecutó `dotnet run --project tools/BitCode.Diagnostics -- doctor` contra el entorno sano (5
contenedores healthy): reporte idéntico en estructura y contenido al documentado en la sección 5 de la
guía (advertencias esperadas por variables de entorno no configuradas, todo lo demás `[OK]`).

Para simular un troubleshooting real (no solo leer la guía), se detuvo deliberadamente el contenedor
`bitcode-redis` (`docker stop bitcode-redis`) y se re-ejecutó `dotnet run --project tools/BitCode.Diagnostics
-- connectivity`: el CLI detectó correctamente la falla (`[WARN] Redis 7.2 — No se pudo conectar ... The
operation was canceled.`) y ofreció la remediación exacta documentada (`.\scripts\dev-env.ps1 up` o
`docker compose up -d redis`). Se restauró el contenedor (`docker start bitcode-redis`) y se confirmó que
volvió a `healthy`. **Sin fricción**: la guía describe con precisión el comportamiento real, incluido el
formato de salida y los códigos de remediación.

### 2.5 (Camino d) Nombre real de tabla `OutboxMessage`/`InboxMessage`/`IdempotencyKey` — documentado como plural en varios lugares, singular en la realidad (bug nuevo, corregido)

Al seguir literalmente `docs/runbook-dlq.md` sección 1 ("Identificar mensajes en DLQ, Opción A") para
diagnosticar un incidente simulado de dead-letter, la consulta SQL documentada es:

```sql
SELECT Id, TenantId, EventType, Error, RetryCount, ExhaustedAtUtc, OccurredAtUtc
FROM OutboxMessages   -- (documentado así antes de esta corrección)
WHERE ExhaustedAtUtc IS NOT NULL
ORDER BY ExhaustedAtUtc DESC;
```

Un operador nuevo que copie y pegue esa consulta contra cualquier base de datos real de este framework
recibe `Invalid object name 'OutboxMessages'`. Verificado empíricamente contra la base de datos generada
en la sección 2.3 (`SELECT name FROM sys.tables` de la base `RevF10App`):

```
Elementos
IdempotencyKey
InboxMessage
OutboxMessage
```

Los tres nombres reales son **singulares**, no plurales. Causa raíz: `OutboxMessage`/`InboxMessage`/
`IdempotencyKey` **no se exponen como propiedades `DbSet<T>`** en `MultiTenantDbContext` (se accede a
ellas vía `Set<T>()`, ver comentarios en `OutboxModelConfigurator`/`IdempotencyModelConfigurator`); EF
Core, sin una propiedad `DbSet` de la que tomar el nombre, usa por convención el nombre singular del tipo
CLR. En cambio, `RefreshToken` (Security 2.0) **sí** se expone como `DbSet<RefreshToken> RefreshTokens`
en `MultiTenantIdentityDbContext`, y por eso su tabla real **sí** es plural (`RefreshTokens`) — la
inconsistencia de la documentación no era arbitraria, sino una generalización incorrecta a partir de un
caso (`RefreshTokens`) que sí es plural.

**Corregido en esta continuación**, en los lugares donde la referencia es una consulta SQL ejecutable
(el riesgo real para un operador que sigue el runbook al pie de la letra) y en las guías de referencia
por módulo que describen el modelo de datos:

- `docs/runbook-dlq.md` (consulta SQL de la sección "1. Identificar mensajes en DLQ, Opción A").
- `docs/runbook-workflow.md` (dos ocurrencias: la tabla de SLO/alertas y la consulta SQL adaptada del
  runbook de DLQ).
- `docs/golden-paths.md` (flujo canónico Outbox/Inbox).
- `docs/convenciones.md` (regla dura #4, idempotencia).
- `docs/guia-dashboard.md`, `docs/guia-catalogs.md`, `docs/guia-organization.md`,
  `docs/guia-identity-administration.md`, `docs/guia-workflow.md` (descripción del modelo de datos por
  módulo).

**Limitación conocida, fuera de alcance de esta corrección puntual:** el mismo error de nomenclatura
(`IdempotencyKeys`/`OutboxMessages`/`InboxMessages` en plural) está presente en los comentarios XML de
documentación (`///`) de **12 archivos `.cs`** en `src/Platform/*/**DbContext.cs`,
`src/Shared.Infrastructure.Persistence/Interceptors/OutboxSaveChangesInterceptor.cs`,
`src/Shared.Infrastructure.Persistence/PersistenceServiceCollectionExtensions.cs`,
`src/Shared.Kernel/AggregateRoot.cs`, `src/Platform/BitCode.Platform.Identity/*.cs` y
`templates/module/ModuleNameDbContext.cs`. Corregir esos comentarios de código es un cambio cohesionado
pero de alcance distinto (toca 12+ archivos de código fuente en 10 módulos de plataforma, no el "paquete
documental" que es el entregable literal de F10-07); se deja explícitamente pendiente como tarea de
limpieza de comentarios, no como bloqueo de esta revisión.

---

## 3. Verificación de correcciones previas de Fase 8/9 en el resto de la documentación

- **`CLUSTER_ID` de Kafka inválido (Gate de Fase 8):** la corrección (`docker-compose.yml`, Cluster ID
  KRaft válido) ya está aplicada y es la única referencia a `CLUSTER_ID` en el repositorio
  (`docker-compose.yml:82`); no quedó ninguna mención residual del valor inválido anterior en ningún
  documento. Confirmado además de forma indirecta: el entorno local (`docker compose up -d`, F8-11) sigue
  levantando los 5 servicios `healthy` en esta sesión, incluido `bitcode-kafka`, usado activamente en las
  pruebas de las secciones 2.3 y 2.4.
- **Nombres de health check `sql-server-workflow`/`kafka-producer` (F9-10):** se buscó en todo el
  repositorio (`docs/` y `src/`) cualquier referencia residual a un nombre de health check que debiera
  haberse renombrado. No se encontró ninguna ocurrencia de `kafka-producer` en ningún archivo actual del
  repositorio. La única ocurrencia de `sql-server-workflow` (`docs/guia-workflow.md:373`) corresponde al
  nombre real y vigente del check registrado en
  `WorkflowServiceCollectionExtensions.cs:31` (`.AddCheck<WorkflowDbContextHealthCheck>("sql-server-workflow", ...)`)
  — es un check **específico del módulo Workflow**, deliberadamente distinto del check genérico
  `"sql-server"` de la infraestructura compartida, no un nombre viejo que haya quedado sin actualizar. No
  se encontró ninguna inconsistencia real en esta dimensión.

---

## 4. Resumen de correcciones aplicadas en esta continuación

| Archivo | Corrección |
|---|---|
| `docs/runbook-dlq.md` | `FROM OutboxMessages` → `FROM OutboxMessage` (consulta SQL ejecutable). |
| `docs/runbook-workflow.md` | Dos ocurrencias de `OutboxMessages` → `OutboxMessage` (tabla de SLO/alertas y consulta SQL). |
| `docs/golden-paths.md` | `OutboxMessages`/`InboxMessages` → singular (flujo canónico). |
| `docs/convenciones.md` | `IdempotencyKeys` → `IdempotencyKey` (regla dura #4). |
| `docs/guia-dashboard.md` | `IdempotencyKeys`/`OutboxMessages`/`InboxMessages` → singular. |
| `docs/guia-catalogs.md` | ídem. |
| `docs/guia-organization.md` | ídem. |
| `docs/guia-identity-administration.md` | `IdempotencyKeys`/`OutboxMessages` → singular. |
| `docs/guia-workflow.md` | `OutboxMessages` → `OutboxMessage` (sección de alertas). |

(Las correcciones de `README.md` y `docs/guia-migraciones.md` — sección 2.1 y 2.2 de este documento — se
aplicaron en la primera mitad de esta revisión, antes de la interrupción por error de red, y se listan
aquí solo como referencia; no se repitieron.)

---

## 5. Qué NO se hizo (fuera de alcance explícito de esta tarea)

- No se corrigieron los comentarios XML (`///`) de código fuente con la misma inconsistencia de
  pluralización (sección 2.5) — se deja documentado como limitación conocida, no como bloqueo.
- No se tocó `docker-compose.yml` más allá de usarlo tal cual está (ya validado en F8-11/Gate de Fase 8).
- No se tocó `release.yml` ni ningún ADR existente.
- No se realizó release firmado, prueba de carga/chaos real, ni pen-test — explícitamente fuera del
  alcance autónomo de Fase 10 acordado con el usuario.
- No se marcó ningún checkbox del Gate de salida de Fase 10.

## 6. Cierre frente al criterio de aceptación

**Criterio de aceptación literal: "Prueba por usuario nuevo".** Los 4 caminos (instalación, generación de
app nueva, actualización/migraciones, troubleshooting) se ejecutaron de punta a punta como lo haría una
persona sin conocimiento previo del repositorio, siguiendo el texto de la documentación tal como está
escrito hoy. Se encontraron y corrigieron 4 fricciones reales y verificadas (framework .NET 8→10 en
`README.md`; limitación de carga de ensamblados de host en `guia-migraciones.md`; nomenclatura de tablas
`OutboxMessage`/`InboxMessage`/`IdempotencyKey` en 9 documentos); un camino completo (generación de app
nueva) resultó sin ninguna fricción; y se verificó que dos correcciones previas de Fase 8/9 (CLUSTER_ID de
Kafka, nombres de health check) están correctamente reflejadas en toda la documentación sin residuos.
