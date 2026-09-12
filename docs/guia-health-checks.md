# Guía — Health checks: liveness vs. readiness (F1-25)

## Por qué dos endpoints, no uno

Un único endpoint `/health` que mezcla "¿el proceso está vivo?" con "¿puedo servir tráfico ahora
mismo?" produce el peor resultado posible cuando una dependencia externa (SQL Server, Redis) está
caída: un orquestador (Kubernetes) que interpreta ese único endpoint como *liveness* reinicia el
proceso una y otra vez, aunque el proceso .NET esté perfectamente sano — un `restart` no arregla que
SQL Server no responda, así que el resultado es un `CrashLoopBackOff` que empeora la disponibilidad en
vez de mejorarla.

El framework separa la semántica en dos endpoints estándar, con criterios de contenido
deliberadamente distintos:

| Endpoint | Pregunta que responde | Depende de SQL Server/Redis | Quién lo consume | Acción del consumidor ante una falla |
|---|---|---|---|---|
| `/health/live` (**liveness**) | ¿El proceso .NET está vivo y puede responder requests? | **Nunca** | Orquestador (Kubernetes `livenessProbe`) | Reiniciar el proceso |
| `/health/ready` (**readiness**) | ¿Puedo servir tráfico de negocio *ahora mismo*? | **Sí**, siempre que la dependencia sea crítica para el flujo principal | Balanceador/orquestador (Kubernetes `readinessProbe`) | Sacar la instancia del pool de tráfico hasta que se recupere — **nunca** reiniciarla por esto |

## Cómo registrarlo en un proyecto consumidor

No hace falta ningún paso adicional si el proyecto ya sigue el patrón estándar del framework:

```csharp
// InfrastructureModule.cs
public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
{
    services.AddSharedPersistence<MiDbContext>(connectionString); // registra el check "sql-server" (tag "ready")
    services.AddSharedCaching(configuration);                     // si Caching:RedisConnectionString está
                                                                    // configurado, registra el check "redis"
                                                                    // (tag "ready") — si no, no registra nada
    // ...
}

public void ConfigureApplication(WebApplication app)
{
    app.MapSharedHealthChecks(); // mapea /health/live y /health/ready
}
```

`AddSharedPersistence<TContext>` (Shared.Infrastructure.Persistence) y `AddSharedCaching`
(Shared.Infrastructure.Caching) llaman internamente a `services.AddHealthChecks()` — el método es
idempotente entre sí, así que un proyecto que usa ambos no necesita coordinarlos. Un proyecto sin
persistencia ni cache (poco común) que igual quiera exponer `/health/live` debe llamar
`services.AddSharedHealthChecks()` (Shared.Infrastructure.Web) para que `HealthCheckService` exista en
el contenedor — sin al menos un `AddHealthChecks()` en algún punto del registro, `MapHealthChecks` no
puede resolver el servicio.

`MapSharedHealthChecks()` (Shared.Infrastructure.Web, `HealthCheckEndpointRouteBuilderExtensions`)
mapea ambos endpoints con la semántica descrita arriba:

```csharp
endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false, // descarta TODOS los checks registrados — nunca se invoca ningún delegado
});

endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
});
```

El `Predicate = _ => false` de `/health/live` es la pieza clave: `HealthCheckService` **nunca
ejecuta ningún delegado de chequeo** para ese endpoint, así que responde 200 mientras el proceso
pueda atender el request HTTP, sin abrir una conexión a SQL Server, sin llamar a Redis, sin ninguna
dependencia externa en absoluto — verificado en
`tests/Shared.Infrastructure.Web.Tests/HealthChecks/HealthCheckEndpointRouteBuilderExtensionsTests.cs`
(`Live_SiempreRespondeOk_SinInvocarNingunHealthCheckRegistrado`, que cuenta invocaciones de un check
falso para demostrar que nunca se llama).

`/health/ready` responde el código de estado por defecto de `HealthCheckOptions` sin
`ResponseWriter` propio: 200 si todos los checks etiquetados `"ready"` son `Healthy`/`Degraded`, 503
(`Service Unavailable`) si al menos uno es `Unhealthy`.

## Cómo registrar el check de un módulo de plataforma nuevo (Fase 6 y en adelante)

Un módulo de plataforma nuevo que dependa de una infraestructura crítica propia (un broker de
mensajería, un proveedor de secretos, otro servicio HTTP del que depende para operar) debe:

1. Implementar `IHealthCheck` (`Microsoft.Extensions.Diagnostics.HealthChecks`, paquete de Microsoft
   sin dependencia de ASP.NET Core — usable incluso en un proyecto sin `FrameworkReference` a
   `Microsoft.AspNetCore.App`) haciendo la verificación **más barata posible** que demuestre
   disponibilidad real (una conexión, un `PING`, nunca una operación de negocio completa) y
   capturando cualquier excepción de conexión como `HealthCheckResult.Unhealthy(mensaje, excepción)`
   — nunca dejarla propagar sin controlar hacia `HealthCheckService`.
2. Registrarlo con `services.AddHealthChecks().AddCheck<MiCheck>("mi-dependencia", tags: ["ready"])`
   dentro del propio método `AddSharedMiModulo(...)` del módulo — no en `Program.cs`/
   `InfrastructureModule` del proyecto consumidor. `AddHealthChecks()` es idempotente entre módulos.
3. **Nunca** agregar el tag `"ready"` a un check si la dependencia no es estrictamente necesaria
   para atender el flujo principal del proyecto (por ejemplo, una integración opcional/best-effort) —
   eso sacaría instancias sanas del pool de tráfico por una dependencia no crítica. Si hace falta un
   tercer nivel de severidad ("degradado pero operable"), usar `HealthCheckResult.Degraded` en el
   check en vez de `Unhealthy` — `/health/ready` sigue respondiendo 200 para `Degraded`.
4. **Nunca** agregar el check sin tag (o con cualquier tag distinto de `"ready"`) esperando que
   `/health/live` lo recoja — `/health/live` no evalúa ningún tag, su `Predicate` descarta todo.

Ver `src/Shared.Infrastructure.Persistence/HealthChecks/DbContextHealthCheck.cs` (SQL Server,
`Database.CanConnectAsync`) y
`src/Shared.Infrastructure.Caching/HealthChecks/RedisDistributedCacheHealthCheck.cs` (Redis, un
`GetAsync` trivial sin escribir nada) como referencia de implementación — ambos registrados con tag
`"ready"` y ambos capturan cualquier excepción sin dejarla propagar.

## Qué NO hacer

- **No** poner un check de SQL Server/Redis en `/health/live` "para estar seguros" — invalida
  completamente la separación liveness/readiness y reintroduce el `CrashLoopBackOff` que esta tarea
  resuelve.
- **No** hacer que un check de readiness ejecute una consulta de negocio real (`SELECT COUNT(*) FROM
  TablaGrande`) — el costo del check debe ser mínimo y constante; usar
  `Database.CanConnectAsync()`/un `GET` trivial, nunca una query que compita por recursos con el
  tráfico real.
- **No** cachear el resultado de un check de readiness con `HybridCache`/cualquier cache — el cache
  no puede ser la fuente de verdad de si una dependencia crítica está disponible ahora mismo (mismo
  principio que "nunca usar cache como fuente de verdad para saldos/ledger/auditoría/transacciones",
  sección 3.2 del Plan Maestro).

## Pruebas de referencia

- `tests/Shared.Infrastructure.Web.Tests/HealthChecks/HealthCheckEndpointRouteBuilderExtensionsTests.cs`
  — verifica la semántica de los dos endpoints con checks falsos controlables (sin Docker): liveness
  siempre 200 y nunca invoca ningún delegado; readiness refleja 200/503 según el estado del check
  etiquetado `"ready"`.
- `tests/Shared.Infrastructure.Persistence.Tests/Integration/DbContextHealthCheckIntegrationTests.cs`
  — verifica contra SQL Server real (Testcontainers) que el check "sql-server" reporta `Healthy` con
  la base disponible y `Unhealthy` (sin lanzar excepción sin controlar) con una connection string
  inalcanzable.
- `samples/Sample.Api.Tests/Integration/HealthCheckEndpointsIntegrationTests.cs` — camino feliz de
  punta a punta contra el proyecto piloto real: ambos endpoints responden 200 con SQL Server
  disponible, cableados exactamente como lo haría un consumidor real
  (`AddSharedPersistence` + `MapSharedHealthChecks`).
