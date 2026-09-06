# Guía — Queries eficientes (F1-17)

Cómo escribir una consulta contra `IReadRepository<TEntity, TId>` sin traer una entidad completa cuando no hace falta, y cómo paginar sin traer todas las filas para contarlas. No repite las reglas duras de `docs/convenciones.md` — las asume vigentes (en particular la regla dura #2: **un `IQuery` nunca modifica datos**).

## Estado relevado antes de esta tarea

- `ISpecification<T>`/`Specification<T>` (F1 inicial) ya soportaban `AsNoTracking` — pero como una bandera **opt-in** por especificación (`ApplyAsNoTracking()`), no como comportamiento por defecto. Nada obligaba a que quien escribiera una especificación de solo lectura la llamara.
- `RepositoryBase<TEntity, TId>.GetByIdAsync` y `ListAsync()` (sin especificación) no aplicaban `AsNoTracking` en ningún caso — ni siquiera opcionalmente.
- `IReadRepository<,>` e `IRepository<,>` resolvían, ambos, a la **misma** clase concreta (`RepositoryBase<,>`) vía `AddSharedPersistence`. Como consecuencia, un `IQuery` (que nunca debería dejar entradas trackeadas) terminaba con el `ChangeTracker` del `DbContext` del request cargado igual que un `ICommand`.
- No existía ningún mecanismo para proyectar a un DTO sin materializar la entidad completa: la única forma de "proyectar" era traer la entidad entera con `ListAsync(spec)` y hacer `.Select()` en memoria — lo que sí trae todas las columnas de la fila.
- No existía ninguna primitiva de paginación con metadatos (total de filas, página actual, etc.) — `ISpecification` soportaba `Skip`/`Take`, pero calcular el total exigía una segunda consulta manual repitiendo el `Where`.

## Qué cambió

### 1. `IReadRepository<,>` ahora resuelve a una implementación de solo lectura dedicada

`ReadOnlyRepositoryBase<TEntity, TId>` (`Shared.Infrastructure.Persistence`) es la que registra `AddSharedPersistence<TContext>()` detrás de `IReadRepository<,>`. Fuerza `AsNoTracking()` en **todas** sus lecturas (`GetByIdAsync`, `ListAsync`, `ListAsync<TResult>`, `ListPagedAsync`, `ListPagedAsync<TResult>`), sin depender de que cada `Specification` recuerde declarar `ApplyAsNoTracking()`.

`RepositoryBase<TEntity, TId>` (detrás de `IRepository<,>`, el contrato de escritura) sigue trackeando por defecto: un handler de `ICommand`/`ITransactionalCommand` necesita `GetByIdAsync` trackeado para poder mutar la misma instancia y llamar `Update`.

No hay ninguna acción a tomar en un proyecto consumidor: un handler de `IQuery` que ya inyectaba `IReadRepository<Producto, Guid>` (patrón obligatorio desde F1, ver `ObtenerProductoQueryHandler` en `samples/Sample.Api`) empieza a beneficiarse de esto automáticamente.

### Antes / después — lectura simple

```csharp
// Antes (funcionaba, pero el ChangeTracker terminaba con la entidad trackeada
// aunque el handler nunca fuera a llamar SaveChangesAsync)
public class ObtenerProductoQueryHandler(IReadRepository<Producto, Guid> repository)
    : IQueryHandler<ObtenerProductoQuery, ProductoResponse>
{
    public async Task<Result<ProductoResponse>> Handle(ObtenerProductoQuery request, CancellationToken ct)
    {
        var producto = await repository.GetByIdAsync(request.Id, ct); // ahora ya viene AsNoTracking
        ...
    }
}
```

No hace falta cambiar el código del handler — el cambio está en la implementación que resuelve `IReadRepository<,>`.

### 2. Proyección directa a DTO sin traer la entidad completa

`IReadRepository<TEntity, TId>` agrega:

```csharp
Task<IReadOnlyList<TResult>> ListAsync<TResult>(
    ISpecification<TEntity> specification,
    Expression<Func<TEntity, TResult>> selector,
    CancellationToken cancellationToken = default);
```

El `Select(selector)` se aplica **antes** de materializar (`ToListAsync`), sobre el `IQueryable` que ya tiene el filtro/orden/includes de la especificación — EF Core traduce esto a un `SELECT` con solo las columnas usadas por `selector`, no `SELECT *`.

```csharp
// Antes: trae Producto completo (todas las columnas) para quedarse solo con Nombre y Precio
var productos = await repository.ListAsync(new ProductosActivosSpecification());
var nombres = productos.Select(p => p.Nombre).ToList(); // proyección en memoria, tarde

// Después: el SELECT generado por EF Core solo trae Nombre y Precio
var nombres = await repository.ListAsync(
    new ProductosActivosSpecification(),
    p => new { p.Nombre, p.Precio });
```

Usar este overload en cualquier query de "listado para mostrar en pantalla" (grilla, combo, export) donde no se necesite la entidad completa — que es el caso más común de un `IQuery`.

### 3. Paginación con metadatos, sin traer todas las filas para contarlas

`PagedResult<T>` (`Shared.Kernel`) es la primitiva común: `Items`, `Page`, `PageSize`, `TotalCount`, `TotalPages`, `HasNextPage`, `HasPreviousPage`.

`IReadRepository<TEntity, TId>` agrega:

```csharp
Task<PagedResult<TEntity>> ListPagedAsync(
    ISpecification<TEntity> specification, int page, int pageSize, CancellationToken cancellationToken = default);

Task<PagedResult<TResult>> ListPagedAsync<TResult>(
    ISpecification<TEntity> specification, Expression<Func<TEntity, TResult>> selector,
    int page, int pageSize, CancellationToken cancellationToken = default);
```

Internamente ejecuta dos consultas contra el **mismo** filtro (`Criteria`/`Includes`/`OrderBy` de la especificación, sin su propio `Skip`/`Take`): un `CountAsync` para el total y un `Skip/Take` (más `Select` en el overload proyectado) para la página pedida — sin traer nunca todas las filas a memoria solo para contarlas.

```csharp
// Antes: sin primitiva común; cada feature inventaba su propio (Skip, Take, Count) a mano
var total = await repository.CountAsync(spec);
var items = await repository.ListAsync(specConSkipTake);

// Después
var pagina = await repository.ListPagedAsync(spec, page: 2, pageSize: 20);
// o, proyectado (recomendado para un listado):
var pagina = await repository.ListPagedAsync(spec, p => new ProductoListItemResponse(p.Id, p.Nombre, p.Precio), page: 2, pageSize: 20);
```

**Fuera de alcance de esta tarea (F1-17):** ningún límite máximo de `pageSize` ni paginación por cursor todavía — eso es responsabilidad de F1-21 ("Paginación: incorporar límites máximos y cursores donde aplique"), que se apoya en esta misma primitiva `PagedResult<T>`.

## Regla práctica

1. Un `IQuery` inyecta `IReadRepository<TEntity, TId>` (nunca `IRepository<,>`) — regla ya vigente desde F1, ahora además garantiza `AsNoTracking`.
2. Si el resultado del `IQuery` es un DTO/Response y no la entidad de dominio, usar el overload de `ListAsync`/`ListPagedAsync` con `selector` — no traer la entidad completa para descartar columnas después.
3. Si el listado es potencialmente grande, usar `ListPagedAsync`/`ListPagedAsync<TResult>` en vez de `ListAsync`/`CountAsync` por separado.
4. Un `ICommand` que necesita cargar una entidad para mutarla sigue usando `IRepository<TEntity, TId>.GetByIdAsync` (tracking normal) — no cambia.

**Fuera de alcance de esta tarea (F1-17), cubierto por F1-18:** si ninguno de los métodos de
`IReadRepository<,>` de arriba alcanza (por ejemplo, un agregado — `SUM`/`AVG`/`COUNT` combinados — que
exige un único `SELECT` en la base de datos), ver `docs/guia-hot-paths.md` para el contrato de
extensión controlada (`IHotPathQuery<TResult>`/`IHotPathQueryExecutor`), que exige un benchmark real
documentado antes de bypasear el patrón genérico.

## Referencias

- `src/Shared.Infrastructure.Persistence/Repositories/ReadOnlyRepositoryBase.cs`
- `src/Shared.Infrastructure.Persistence/Repositories/RepositoryBase.cs`
- `src/Shared.Domain/Persistence/IReadRepository.cs`
- `src/Shared.Kernel/PagedResult.cs`
- `tests/Shared.Infrastructure.Persistence.Tests/ReadOnlyRepositoryBaseTests.cs` — pruebas de comportamiento (`ChangeTracker.Entries()` vacío tras una lectura vía `IReadRepository`, proyección, paginación).
- `docs/convenciones.md` — regla dura #2 (`IQuery` nunca muta) y regla dura #5 (nunca exponer `IQueryable`).
