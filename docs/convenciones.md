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
6. **Un error de negocio esperado es un `Result.Failure`, nunca una excepción.** Las excepciones son para lo verdaderamente inesperado; `GlobalExceptionHandler` las trata como error 500 sin distinción.
7. **Toda entidad que necesite auditoría/soft-delete/multi-tenancy implementa la interfaz correspondiente (`IAuditedEntity`/`ISoftDelete`/`ITenantEntity`) y nada más** — el framework detecta las interfaces por reflexión, no requiere configuración adicional en `OnModelCreating`.
8. **Un endpoint siempre termina en `.ToOkOrProblem()` o `.ToProblemDetails()` sobre el `Result` que devuelve el `Sender`**, nunca inspeccionando manualmente `IsSuccess`/`Error` para construir la respuesta HTTP a mano.
9. **Una entidad con concurrencia optimista implementa `IHasConcurrencyToken` (propiedad `byte[] RowVersion`) y nada más** — el framework la detecta por reflexión (mismo patrón que `IAuditedEntity`/`ISoftDelete`/`ITenantEntity`) y configura `RowVersion` como token de concurrencia de EF Core (`IsRowVersion()`) sin configuración adicional en `OnModelCreating`. `TransactionBehavior` captura de forma uniforme el conflicto resultante (`DbUpdateConcurrencyException` de EF Core, traducido primero a `ConcurrencyConflictException` en `Shared.Domain` para no filtrar el tipo de EF Core hasta la capa de aplicación) y lo convierte siempre en el mismo `Result.Failure` con `ConcurrencyError.Conflict` (código `"Concurrency.Conflict"`, `ErrorType.Conflict`) — nunca en una excepción sin controlar que llegue a `GlobalExceptionHandler`. `ResultExtensions.ToProblemDetails` ya mapea `ErrorType.Conflict` a `409 Conflict` (F1-08).

## Cuándo usar qué

| Necesito... | Uso |
|---|---|
| Una entidad nueva | `dotnet new bitcode-entity -n MiEntidad --MultiTenant true\|false` |
| Un feature CQRS nuevo | `dotnet new bitcode-feature -n MiAccion` |
| Agrupar servicios de una feature | `IFrameworkModule` con `[DependsOn(typeof(InfrastructureModule))]` |
| Que el módulo también mapee endpoints | `IWebFrameworkModule` en vez de `IFrameworkModule` |
| Cachear el resultado de una query costosa | `HybridCache.GetOrCreateAsync` (Fase 4) — recordar que la escritura a Redis L2 es asíncrona, no asumir consistencia inmediata entre instancias |
| Un job recurrente | `IJob` de Quartz.NET registrado en `AddSharedBackgroundJobs` |
| Proteger un endpoint por permiso | `[RequirePermission("entidad.accion")]` o `.RequireAuthorization("entidad.accion")` |

## Testing

- Un test que depende de Docker (SQL Server, Redis) va en una carpeta `Integration/` y su nombre de clase/namespace debe contener `Integration` — el filtro `FullyQualifiedName!~Integration` de CI y de uso diario depende de esa convención.
- Reutilizar `SqlServerContainerFixture`/`RedisContainerFixture` de `Shared.Testing`, no crear una copia local — ver Fase 7 para el porqué.
- Un test de integración usa `fixture.BuildIsolatedConnectionString(prefijo, nombreDeTest)` para que cada test tenga su propia base de datos dentro del mismo contenedor.

## Referencias

- [`docs/README.md`](README.md) — índice de fases con el detalle de cada decisión de diseño.
- [`samples/Sample.Api`](../samples/Sample.Api) — implementación de referencia siguiendo todas estas convenciones.
