# Convenciones — BitCode.Framework

Guía de convenciones para un proyecto consumidor del framework. Extraída de los patrones ya validados en `samples/Sample.Api` y en la suite de tests de cada fase — no son reglas nuevas, son las que el propio framework ya sigue.

## Estructura de un proyecto consumidor

```
MiApp/
  MiApp.Api/                      (o el nombre del proyecto Web)
    Program.cs                    (AddModules + UseModules, nada más)
    InfrastructureModule.cs       (envuelve los AddSharedX<T>() del framework)
    MiAppDbContext.cs             (: MultiTenantDbContext o MultiTenantIdentityDbContext)
    appsettings.json
    <Feature>/
      <Feature>.cs                 (entidad de dominio)
      Crear<Feature>Command.cs     (Command + Validator + Handler, un archivo o tres)
      Obtener<Feature>Query.cs     (Query + Handler)
      <Feature>Module.cs           (IWebFrameworkModule: MapGroup + endpoints)
```

Un feature = una carpeta = un `IWebFrameworkModule` con `[DependsOn(typeof(InfrastructureModule))]`. Ver `samples/Sample.Api/Productos/` como referencia completa.

## Nomenclatura

| Elemento | Convención | Ejemplo |
|---|---|---|
| Command | `{Verbo}{Entidad}Command` | `CrearProductoCommand`, `EliminarProductoCommand` |
| Query | `Obtener{Entidad}Query` / `Listar{Entidad}sQuery` | `ObtenerProductoQuery` |
| Validator | `{Command}Validator` | `CrearProductoCommandValidator` |
| Handler | `{Command}Handler` | `CrearProductoCommandHandler` |
| Response de Query | `{Entidad}Response` | `ProductoResponse` |
| Módulo de feature | `{Feature}Module` | `ProductosModule` |
| Error de dominio | `{Entidad}.{Motivo}` (código) | `"Producto.NoEncontrado"` |
| Permiso | `{entidad}.{accion}` (minúsculas, punto) | `"productos.crear"` |

## Reglas duras (no opcionales)

Derivadas de decisiones de diseño ya tomadas en fases anteriores — apartarse de ellas rompe garantías que el resto del framework asume:

1. **Un handler de un `ICommand` simple (sin `ITransactionalCommand`) nunca llama `IUnitOfWork.SaveChangesAsync` explícitamente.** `TransactionBehavior` ya lo hace al finalizar el pipeline con un `Result` exitoso. Ver el comentario en `CrearProductoCommandHandler` del piloto. Excepción explícita: un handler de `ITransactionalCommand` **sí** puede llamar `SaveChangesAsync` a mitad de su ejecución cuando necesita coordinar varias operaciones de escritura que dependen entre sí (por ejemplo, necesitar el resultado ya persistido de la primera antes de ejecutar la segunda) — todas esas llamadas intermedias quedan dentro de la misma transacción de base de datos abierta por `TransactionBehavior`, que revierte **todo** (incluidas las ya guardadas) ante un fallo posterior. `TransactionBehavior` mismo nunca llama `SaveChangesAsync` directamente: en el camino transaccional delega toda la persistencia final a un único `IUnitOfWork.CommitAsync` (F1-07).
2. **Un `IQuery` nunca modifica datos.** `TransactionBehavior` está restringido a `IBaseCommand` — una query que escribe no tiene la protección transaccional y puede dejar cambios a medias sin que el framework lo detecte.
3. **Solo un comando que implementa explícitamente `ITransactionalCommand` abre una transacción real de base de datos con rollback coordinado.** Un `ICommand`/`ICommand<T>` simple (sin `ITransactionalCommand`) persiste sus cambios automáticamente vía `SaveChangesAsync` al finalizar con éxito, pero **no** abre una transacción explícita — usalo para el caso común de un único agregado por comando. Reservá `ITransactionalCommand` para comandos que coordinan más de una operación de escritura (varios agregados, varios `SaveChanges`, efectos que deben confirmarse o revertirse en conjunto). Ver ADR `docs/adr/0009-contratos-comando-transaccion-explicita.md`. **(F1-07)** Mientras la transacción de un `ITransactionalCommand` está abierta, el handler no debe realizar llamadas HTTP salientes, a cache distribuido (Redis) ni a un futuro broker de eventos/mensajería — son fuente clásica de timeouts y deadlocks al mantener locks de base de datos abiertos durante una llamada de red lenta. Esas llamadas deben diferirse hasta después de que el `Result` exitoso confirme el commit (por ejemplo, publicando un evento de dominio vía Outbox — F1-23 — en lugar de llamar directamente a un broker dentro del handler).
4. **`IIdempotentCommand` es el contrato marcador para comandos que deben tolerar reintentos sin duplicar efectos.** La implementación del middleware de detección de duplicados es una tarea aparte (F1-22); declarar la interfaz hoy no activa ningún comportamiento adicional todavía.
5. **`IUnitOfWork`/`DbContext` son propiedad exclusiva del pipeline (`TransactionBehavior`/`UnitOfWork`); nada más llama `Database.BeginTransactionAsync`/`IDbContextTransaction.CommitAsync`/`RollbackAsync` directamente**, y ningún handler ni repositorio recibe un `DbContext` inyectado (solo `IRepository<T, TId>`/`IUnitOfWork`). **(F1-09)** Si un handler de `ITransactionalCommand` despacha otro `ITransactionalCommand` vía `ISender` dentro del mismo scope (mismo `IUnitOfWork`/`DbContext`), ambos comparten la misma transacción física: solo el `BeginTransactionAsync`/`CommitAsync` del nivel más externo abre/confirma la transacción; los niveles internos reutilizan la transacción existente y su `CommitAsync` solo hace `flush` (`SaveChangesAsync`) sin cerrarla. Un `RollbackAsync`, sea del nivel interno o externo, siempre revierte la transacción física completa — no existe "rollback parcial" de un nivel anidado, porque ambos comandos comparten el mismo `ChangeTracker`. Ver addendum F1-09 en `docs/adr/0009-contratos-comando-transaccion-explicita.md`.
5. **Nunca exponer `IQueryable` desde un repositorio.** Toda consulta pasa por `ISpecification<T>` o por los métodos tipados de `IRepository`/`IReadRepository`.
6. **Un error de negocio esperado es un `Result.Failure`, nunca una excepción.** Las excepciones son para lo verdaderamente inesperado; `GlobalExceptionHandler` las trata como error 500 sin distinción, salvo `OperationCanceledException` con el request abortado (regla 10).
10. **(F1-10) Todo método async en la cadena de un comando/query acepta y propaga un `CancellationToken`; nunca `CancellationToken.None` salvo justificación explícita en un comentario.** Esto incluye el endpoint (`CancellationToken ct` inyectado por el binding de minimal API, tomado de `HttpContext.RequestAborted`), `ISender.Send(request, ct)`, cada `IPipelineBehavior` del pipeline de MediatR, el handler, `IRepository`/`IReadRepository` y `IUnitOfWork`. La única excepción legítima es cuando el propio contrato de una API de terceros no acepta `CancellationToken` (por ejemplo, `UserManager`/`RoleManager` de ASP.NET Core Identity en `PermissionService`) — ahí no hay nada que propagar. Una cancelación (`OperationCanceledException`/`TaskCanceledException`) **nunca** se traduce a un `Result.Failure`/ProblemDetails como si fuera un error de negocio o una excepción inesperada:
    - `TransactionBehavior` revierte la transacción de un `ITransactionalCommand` ante una cancelación igual que ante cualquier otro fallo (para no dejar bloqueos de fila huérfanos en SQL Server), pero relanza la excepción tal cual — nunca la atrapa como el `catch (Exception)` genérico ni la convierte en un `Result.Failure`. El `RollbackAsync` de ese camino se invoca con `CancellationToken.None`, no con el token ya cancelado: pasarle el token cancelado haría que EF Core lance `OperationCanceledException` al iniciar el propio rollback sin llegar a emitir el `ROLLBACK` real contra SQL Server.
    - `GlobalExceptionHandler` no trata una `OperationCanceledException` con `httpContext.RequestAborted.IsCancellationRequested == true` como error 500: la loguea en un nivel bajo y no intenta escribir una respuesta sobre una conexión que el cliente ya cerró.
    - Ver `TransactionBehaviorTests` (unitarios) y `CancellationIntegrationTests` (SQL Server real) en `Shared.Application.Tests`/`Shared.Infrastructure.Persistence.Tests` para el criterio de aceptación "requests cancelados liberan recursos": una escritura posterior sobre la misma fila, desde una conexión nueva, tiene éxito de inmediato después de la cancelación (no queda ningún bloqueo colgado).
11. **(F1-10) `AddSharedPersistence<TContext>` acepta opcionalmente un `Action<PersistenceOptions>` con `CommandTimeoutSeconds`** para acotar cuánto puede tardar un comando SQL individual en el servidor, independiente del `CancellationToken` del request (protege contra, por ejemplo, un bloqueo inesperado en SQL Server que el cliente nunca cancela). Sin configurar, se mantiene el default del proveedor de EF Core para SQL Server (equivalente al de `SqlCommand`, 30 segundos) — no es un comportamiento nuevo, es la posibilidad de ajustarlo por proyecto.
7. **Toda entidad que necesite auditoría/soft-delete/multi-tenancy implementa la interfaz correspondiente (`IAuditedEntity`/`ISoftDelete`/`ITenantEntity`) y nada más** — el framework detecta las interfaces por reflexión, no requiere configuración adicional en `OnModelCreating`.
8. **Un endpoint siempre termina en `.ToOkOrProblem()` o `.ToProblemDetails()` sobre el `Result` que devuelve el `Sender`**, nunca inspeccionando manualmente `IsSuccess`/`Error` para construir la respuesta HTTP a mano.
12. **(F1-12) El `TenantId` de un `ITenantProvider` productivo nunca se resuelve desde un header HTTP, query string o cualquier dato que el cliente controle directamente sin pasar por autenticación validada por el servidor.** Un proyecto multi-tenant real registra `services.AddHttpContextTenantProvider()` (Shared.Infrastructure.Web) ANTES de `AddSharedPersistence<TContext>()` en su `InfrastructureModule` — resuelve el tenant desde el claim `TenantClaimTypes.TenantId` del JWT ya validado (`HttpContext.User`), emitido automáticamente por `JwtTokenGenerator` a partir de `ApplicationUser.TenantId`. Si un usuario autenticado no trae ese claim, `HttpContextTenantProvider` lanza `TenantResolutionException` en vez de asumir un tenant por defecto o deshabilitar el filtro — fallar de forma segura es preferible a arriesgar una fuga de datos entre tenants (ver `docs/threat-model.md`, hallazgo S2, y ADR `docs/adr/0003-tenancy-multi-tenant-por-filtro-global.md`). `NullTenantProvider` (registrado por defecto vía `TryAddScoped`) sigue siendo la opción correcta solo para proyectos de un único tenant o para código sin `HttpContext` (jobs en background, seeders) — nunca para un proyecto multi-tenant real con pipeline HTTP.
9. **Una entidad con concurrencia optimista implementa `IHasConcurrencyToken` (propiedad `byte[] RowVersion`) y nada más** — el framework la detecta por reflexión (mismo patrón que `IAuditedEntity`/`ISoftDelete`/`ITenantEntity`) y configura `RowVersion` como token de concurrencia de EF Core (`IsRowVersion()`) sin configuración adicional en `OnModelCreating`. `TransactionBehavior` captura de forma uniforme el conflicto resultante (`DbUpdateConcurrencyException` de EF Core, traducido primero a `ConcurrencyConflictException` en `Shared.Domain` para no filtrar el tipo de EF Core hasta la capa de aplicación) y lo convierte siempre en el mismo `Result.Failure` con `ConcurrencyError.Conflict` (código `"Concurrency.Conflict"`, `ErrorType.Conflict`) — nunca en una excepción sin controlar que llegue a `GlobalExceptionHandler`. `ResultExtensions.ToProblemDetails` ya mapea `ErrorType.Conflict` a `409 Conflict` (F1-08).
13. **(F1-15) `ITenantContext` (Shared.Domain) es el contrato que debe consumir código de aplicación/logging para leer el `TenantId` del request actual — no `ITenantProvider` directamente.** `ITenantContext` envuelve el `ITenantProvider` ya registrado y memoiza el `TenantId` la primera vez que se accede dentro del scope: una vez resuelto, ningún handler, middleware o dependencia puede sobrescribirlo (la interfaz no expone ningún setter). `ITenantProvider` sigue siendo el contrato de bajo nivel que consumen `MultiTenantDbContext`/`TenantSaveChangesInterceptor` para el filtro global de EF Core (F1-12) — no cambia. `AddSharedPersistence<TContext>` registra `TenantContext` (implementación por defecto) con `TryAddScoped`, igual que el resto de los contratos de tenancy. Un proyecto con pipeline HTTP que quiera que el `TenantId` aparezca automáticamente en todos los logs estructurados del request (sin agregarlo a mano en cada `ILogger.LogInformation(...)`) llama `app.UseTenantContextLogging()` (Shared.Infrastructure.Web, `TenantContextApplicationBuilderExtensions`) después de `UseAuthentication()`/`UseAuthorization()` — el middleware empuja el `TenantId` al `LogContext` de Serilog (`Enrich.FromLogContext()`, ya configurado por `UseSharedSerilog`) para todo el resto del pipeline, y lo retira automáticamente al finalizar el request (no se filtra entre requests concurrentes ni a código fuera del pipeline).
14. **(F1-16) Cachear datos sensibles a tenant con `HybridCache` directamente (sin componer el `TenantId` en la clave) es una fuga de información entre tenants, no un detalle de implementación.** `HybridCache` no tiene ningún concepto de tenant propio: dos tenants que pidan el mismo recurso lógico ("producto con slug X") con la misma clave literal reciben el mismo valor cacheado. Un handler que cachee el resultado de una query de negocio debe usar `ITenantAwareCache` (Shared.Infrastructure.Caching, registrado por `AddSharedCaching` vía `TryAddScoped`), que compone `"tenant:{TenantId}:"` en la clave efectiva usando `ITenantContext` (F1-15) y lanza `TenantResolutionException` (fallar de forma segura, mismo principio que `HttpContextTenantProvider`) si el proyecto opera en modo multi-tenant sin un `TenantId` resuelto para el scope actual. `HybridCache` directamente sigue siendo correcto solo para datos que **no** son sensibles a tenant (metadata/configuración compartida entre todos los tenants). Ver `docs/threat-model.md` (hallazgo I5) y `docs/adr/0006-cache-hybridcache-valkey-redis.md`.

## Cuándo usar qué

| Necesito... | Uso |
|---|---|
| Una entidad nueva | `dotnet new bitcode-entity -n MiEntidad --MultiTenant true\|false` |
| Un feature CQRS nuevo | `dotnet new bitcode-feature -n MiAccion` |
| Agrupar servicios de una feature | `IFrameworkModule` con `[DependsOn(typeof(InfrastructureModule))]` |
| Que el módulo también mapee endpoints | `IWebFrameworkModule` en vez de `IFrameworkModule` |
| Cachear el resultado de una query costosa **sensible a tenant** (datos de negocio) | `ITenantAwareCache.GetOrCreateAsync` (F1-16, Shared.Infrastructure.Caching) — nunca `HybridCache` directamente: compone el `TenantId` en la clave para que dos tenants nunca compartan una entrada por usar la misma clave lógica. Recordar que la escritura a Redis L2 es asíncrona, no asumir consistencia inmediata entre instancias |
| Cachear datos que **no** son sensibles a tenant (metadata/configuración global compartida) | `HybridCache.GetOrCreateAsync` directo (Fase 4) — acá sí es correcto que todos los tenants compartan la misma entrada |
| Un job recurrente | `IJob` de Quartz.NET registrado en `AddSharedBackgroundJobs` |
| Proteger un endpoint por permiso | `[RequirePermission("entidad.accion")]` o `.RequireAuthorization("entidad.accion")` |
| Resolver el tenant en un proyecto multi-tenant real | `services.AddHttpContextTenantProvider()` (Shared.Infrastructure.Web) antes de `AddSharedPersistence<TContext>()` — nunca implementar `ITenantProvider` leyendo un header/query string |
| Leer el `TenantId` del request actual desde un handler/servicio de aplicación | Inyectar `ITenantContext` (Shared.Domain), no `ITenantProvider` — ya viene memoizado e inmutable para el scope |
| Que el `TenantId` aparezca automáticamente en todos los logs de un request | `app.UseTenantContextLogging()` (Shared.Infrastructure.Web) después de `UseAuthentication()`/`UseAuthorization()` |

## Testing

- Un test que depende de Docker (SQL Server, Redis) va en una carpeta `Integration/` y su nombre de clase/namespace debe contener `Integration` — el filtro `FullyQualifiedName!~Integration` de CI y de uso diario depende de esa convención.
- Reutilizar `SqlServerContainerFixture`/`RedisContainerFixture` de `Shared.Testing`, no crear una copia local — ver Fase 7 para el porqué.
- Un test de integración usa `fixture.BuildIsolatedConnectionString(prefijo, nombreDeTest)` para que cada test tenga su propia base de datos dentro del mismo contenedor.

## Referencias

- [`docs/README.md`](README.md) — índice de fases con el detalle de cada decisión de diseño.
- [`samples/Sample.Api`](../samples/Sample.Api) — implementación de referencia siguiendo todas estas convenciones.
