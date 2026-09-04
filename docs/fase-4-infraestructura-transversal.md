# Fase 4 — Infraestructura transversal

**Estado:** Completa
**Commits:** `8d3b8b7` … `deea3b6` (4 commits)
**Tests:** 82 nuevos (78 unitarios/con dependencias reales + 4 de integración contra contenedores reales — Redis y SQL Server)

## Objetivo

Cubrir las piezas transversales que todo proyecto necesita y que normalmente se reimplementan por separado en cada uno: manejo uniforme de errores HTTP, observabilidad (logs estructurados + trazas/métricas), ejecución de tareas en background, y caché compartida entre instancias.

## Decisiones de diseño

| Decisión | Elegido | Motivo |
|---|---|---|
| Background jobs | Quartz.NET | 100% Microsoft/OSS-friendly, sin dashboard propietario ni licenciamiento distinto según uso (a diferencia de Hangfire) |
| Caching | `HybridCache` (L1 memoria + Redis L2 opcional) | API estándar de .NET 8/9; un proyecto de una sola instancia no necesita levantar Redis, uno con varias réplicas lo activa configurando una connection string |
| Logging | Serilog + OpenTelemetry | Combo más usado en proyectos .NET reales; Serilog para logs estructurados con sinks flexibles, OpenTelemetry para trazas/métricas exportables a cualquier collector OTLP |

## Componentes por tarea

### Tarea 4.1 — Exception handling con `ProblemDetails` (`Shared.Infrastructure.Web`, nuevo proyecto)

- `GlobalExceptionHandler` (`IExceptionHandler` de .NET 8): captura cualquier excepción no manejada y responde `ProblemDetails` RFC 7807 con `TraceId`.
- `ResultExtensions.ToProblemDetails()`/`ToOkOrProblem()`: cierra el ciclo del patrón `Result<T>` (Fase 2) en el borde HTTP — mapea cada `ErrorType` a su status code (`Validation`→400 vía `ValidationProblem`, `NotFound`→404, `Conflict`→409, `Unauthorized`→401, `Forbidden`→403, `Failure`→500).

**Corrección durante el desarrollo:** `Microsoft.AspNetCore.Http.Results.ValidationProblem()` (la API "no tipada") no devuelve el tipo concreto `HttpResults.ValidationProblem` — solo `TypedResults` sí. Se migró todo el mapeo a `TypedResults` para consistencia y para que el tipo de retorno sea verificable en tests.

### Tarea 4.2 — Serilog + OpenTelemetry (`Shared.Infrastructure.Observability`, nuevo proyecto)

- `UseSharedSerilog()`: configura Serilog leyendo la sección `"Serilog"` de la configuración, enriquecido con `LogContext` y el nombre de la aplicación.
- `AddSharedObservability()`: tracing y métricas de OpenTelemetry (instrumentación ASP.NET Core, HttpClient, runtime), exportando a OTLP solo si se configura `OpenTelemetry:OtlpEndpoint` — en desarrollo local sin collector el registro no falla, simplemente no exporta.

**Vulnerabilidad corregida durante el desarrollo:** los paquetes `OpenTelemetry.Exporter.OpenTelemetryProtocol`/`Extensions.Hosting` en su versión inicial (1.9.0) traían una vulnerabilidad moderada conocida (`GHSA-4625-4j76-fww9`) que ni siquiera 1.10.0 corregía todavía. Se subieron a 1.18.0.

### Tarea 4.3 — Quartz.NET (`Shared.Infrastructure.BackgroundJobs`, nuevo proyecto)

`AddSharedBackgroundJobs()`: registra Quartz.NET + el hosted service que ejecuta el scheduler, con `WaitForJobsToComplete = true` para no cortar un job a mitad de ejecución al apagar el proceso. El proyecto consumidor define sus propios `IJob` y triggers dentro del delegado de configuración.

**Limitación de testing detectada (no bug de código):** `Quartz.Logging.LogProvider` cachea el `ILoggerFactory` del primer `Host` creado en el proceso de forma estática, y no lo libera al disponer ese `Host`. Un segundo `Host` de Quartz en el mismo proceso de test lanza `ObjectDisposedException` al referenciar el logger ya liberado del primero. Los tests de este módulo se consolidaron en un único `Host` por esta razón — documentado para quien agregue más tests aquí en el futuro.

### Tarea 4.4 — `HybridCache` con Redis L2 opcional (`Shared.Infrastructure.Caching`, nuevo proyecto)

`AddSharedCaching()`: registra `HybridCache` (L1 en memoria, siempre activo). Si la sección `"Caching"` trae `RedisConnectionString`, registra Redis como `IDistributedCache` **antes** de `AddHybridCache()` para que HybridCache lo detecte automáticamente como L2 — sin Redis configurado, un proyecto de una sola instancia sigue funcionando solo con L1.

**Hallazgo real durante el desarrollo:** el primer test de integración contra Redis real falló — un valor escrito por una instancia no era visible de inmediato para otra instancia distinta apuntando al mismo Redis. Se aisló la causa con un test de diagnóstico usando `IDistributedCache` directo (sin `HybridCache` de por medio), que sí compartió el valor correctamente entre instancias, descartando un problema de conectividad o configuración de Redis. La causa real: `HybridCache` escribe a la capa L2 **en segundo plano** (fire-and-forget) para no bloquear `GetOrCreateAsync`, no de forma síncrona. Un delay corto antes de leer desde la otra instancia confirmó el comportamiento. Queda documentado como característica esperada de `HybridCache`, no como defecto del framework — un consumidor que necesite lectura fuertemente consistente inmediatamente después de escribir debe tenerlo en cuenta.

## Cómo usarlo desde un proyecto consumidor

```csharp
// Program.cs
builder.Host.UseSharedSerilog();

builder.Services.AddSharedExceptionHandling();
builder.Services.AddSharedObservability(builder.Configuration);
builder.Services.AddSharedCaching(builder.Configuration);
builder.Services.AddSharedBackgroundJobs(quartz =>
{
    var jobKey = new JobKey("sincronizar-catalogo");
    quartz.AddJob<SincronizarCatalogoJob>(j => j.WithIdentity(jobKey));
    quartz.AddTrigger(t => t.ForJob(jobKey).WithCronSchedule("0 0 * * * ?"));
});

var app = builder.Build();
app.UseExceptionHandler();

// Un Endpoint que ya usa Result<T> de la Fase 2 solo necesita esto:
app.MapPost("/productos", async (CrearProductoCommand command, ISender mediator) =>
{
    var result = await mediator.Send(command);
    return result.ToOkOrProblem();
});
```

```json
// appsettings.json — secciones que las Tareas 4.2 y 4.4 requieren/aceptan
{
  "Serilog": { "MinimumLevel": "Information" },
  "OpenTelemetry": { "ServiceName": "MiApp", "OtlpEndpoint": "http://localhost:4317" },
  "Caching": { "RedisConnectionString": "localhost:6379" }
}
```

## Cobertura de tests

| Área | Unitarios/con dependencias reales | Integración contra contenedor real |
|---|---|---|
| Exception handling + `Result→ProblemDetails` | 11 | — |
| Serilog + OpenTelemetry | 4 | — |
| Quartz.NET | 1 (job real ejecutado por el scheduler) | — |
| `HybridCache`/Redis | 2 | 2 (Redis real) |
| **Total Fase 4** | **18** | **2** |

(La tabla cuenta solo los tests nuevos de esta fase; el total del repo en este punto es 82 unitarios + 9 de integración sumando las Fases 1, 3 y 4.)

## Pendiente / fuera de alcance de esta fase

- Un `AddSharedInfrastructure()` que agrupe las cuatro extensiones en una sola llamada — se mantuvieron separadas deliberadamente porque son concerns independientes que un consumidor puede necesitar parcialmente (p.ej. observabilidad sin background jobs).
- Localización (`IStringLocalizer`) — mencionada en el plan original de Fase 4 pero no desarrollada; queda pendiente si surge una necesidad real de multi-idioma.
- Health checks (`AspNetCore.HealthChecks.*`, visto en BC-SFE-MID para Postgres/Redis/RabbitMQ) — no se incluyó en esta fase, candidato natural para cuando se arme el proyecto de ejemplo end-to-end (Fase 8).
- Fase 5 del plan general (sistema de módulos: `IFrameworkModule`, auto-registro por convención) — siguiente fase a desarrollar.
