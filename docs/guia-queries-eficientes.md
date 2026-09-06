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

### 4. Límites máximos de paginación (F1-21)

`PagedResult<T>`/`ListPagedAsync` (arriba) no tenían, hasta esta tarea, ningún límite máximo de
`pageSize`: un handler que pasara `page`/`pageSize` crudos del cliente HTTP directamente al
repositorio dejaba, de hecho, un endpoint "ilimitado" — nada impedía pedir `pageSize=1000000` y traer
la tabla completa en una sola respuesta.

`PageRequest` (`Shared.Kernel`) es la primitiva que cierra ese hueco:

```csharp
public static Result<PageRequest> Create(int page, int pageSize, int maxPageSize = PageRequest.DefaultMaxPageSize);
```

- `DefaultMaxPageSize` es `100` — un valor conservador para un listado servido a una grilla/tabla de
  UI. Un feature con una necesidad distinta pasa su propio `maxPageSize` explícito a `Create` (no hay
  ningún valor global oculto que cambiar en el framework).
- Un `page < 1`, un `pageSize < 1` o un `pageSize > maxPageSize` **nunca se trunca en silencio**:
  `Create` devuelve un `Result<PageRequest>` fallido con `ErrorType.Validation`
  (`"Paginacion.PaginaInvalida"` / `"Paginacion.TamanioInvalido"` / `"Paginacion.TamanioExcedeLimite"`)
  que el handler propaga como el `Error` de la propia query — el endpoint lo traduce a
  `400 Bad Request` vía `ToProblemDetails()`, igual que cualquier otro error de validación.
- `IReadRepository<TEntity, TId>` agrega overloads de `ListPagedAsync`/`ListPagedAsync<TResult>` que
  reciben un `PageRequest` ya validado en vez de `page`/`pageSize` crudos — es la forma recomendada de
  llamar al repositorio desde un handler de `IQuery`.

```csharp
// Patrón obligatorio para un IQuery de listado paginado expuesto a un endpoint HTTP
var pageRequestResult = PageRequest.Create(request.Page, request.PageSize);
if (pageRequestResult.IsFailure)
{
    return Result.Failure<PagedResult<ProductoResponse>>(pageRequestResult.Error);
}

return await repository.ListPagedAsync(
    new TodosLosProductosOrdenadosPorNombreSpecification(),
    p => new ProductoResponse(p.Id, p.Nombre, p.Precio),
    pageRequestResult.Value,
    cancellationToken);
```

Ver `ListarProductosQuery`/`ListarProductosQueryHandler` en `samples/Sample.Api/Productos/` para el
patrón de referencia completo (incluye el endpoint `GET /productos?page=&pageSize=`) y
`ListarProductos_ConPageSizeSuperiorAlMaximo_Retorna400ConErrorDeValidacion_SinTruncarEnSilencio` en
`samples/Sample.Api.Tests/Integration/ProductosEndpointsIntegrationTests.cs` para la verificación de
punta a punta.

**Recorte defensivo de última línea, no el mecanismo principal:** `RepositoryBase.NormalizePaging`
(el método interno que usan los overloads `ListPagedAsync(spec, page, pageSize, ct)` con `page`/
`pageSize` crudos) también aplica un límite máximo (`PageRequest.DefaultMaxPageSize`) como red de
seguridad de infraestructura, por si algo llega a invocar el repositorio sin pasar por
`PageRequest.Create` — pero ese recorte sí trunca en silencio a propósito (protege la base de datos,
no reemplaza la respuesta de error clara al cliente). Todo endpoint de listado debe construir su
`PageRequest` en el handler, nunca depender de este recorte de infraestructura como mecanismo de
validación.

**Cursores (paginación por keyset): decisión consciente de no implementarlos todavía.**
`PagedResult<T>` sigue siendo offset-based (`Skip`/`Take` + `CountAsync`). Una paginación por cursor
(keyset) evita el "page drift" que ocurre cuando se insertan/eliminan filas entre el pedido de una
página y la siguiente, y es más eficiente que `Skip`/`Take` en tablas muy grandes (evita que SQL
Server tenga que recorrer y descartar todas las filas anteriores al offset). No se justifica agregarla
ahora: el volumen real de los consumidores actuales del framework (`samples/Sample.Api`) es bajo y no
hay reporte de un problema de page drift ni de degradación de performance por `Skip`/`Take` en un
listado real. Si un consumidor real reporta cualquiera de los dos síntomas, la primitiva a agregar es
nueva (`CursorPagedResult<T>`/`ICursorPagination`, aditiva) — no un reemplazo de `PagedResult<T>`, que
sigue siendo la primitiva correcta para el caso común de una UI con "página 1, página 2, ...".

## Regla práctica

1. Un `IQuery` inyecta `IReadRepository<TEntity, TId>` (nunca `IRepository<,>`) — regla ya vigente desde F1, ahora además garantiza `AsNoTracking`.
2. Si el resultado del `IQuery` es un DTO/Response y no la entidad de dominio, usar el overload de `ListAsync`/`ListPagedAsync` con `selector` — no traer la entidad completa para descartar columnas después.
3. Si el listado es potencialmente grande, usar `ListPagedAsync`/`ListPagedAsync<TResult>` en vez de `ListAsync`/`CountAsync` por separado. Un `IQuery` de listado expuesto a un endpoint HTTP **siempre** construye un `PageRequest` con `PageRequest.Create` (F1-21) a partir de los parámetros crudos del cliente antes de llamar al repositorio — nunca pasa `page`/`pageSize` sin validar. `ListAsync()`/`ListAsync(spec)` sin paginar (que devuelven la colección completa) solo son aceptables cuando el volumen está estructuralmente acotado por diseño (por ejemplo, un catálogo de configuración pequeño y fijo, o una relación 1:1 con el tenant) — nunca para una tabla que crece sin límite con la actividad del negocio. Ver la regla dura correspondiente en `docs/convenciones.md`.
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
- `src/Shared.Kernel/PageRequest.cs` (F1-21)
- `samples/Sample.Api/Productos/ListarProductosQuery.cs` (F1-21) — patrón de referencia end-to-end
- `tests/Shared.Kernel.Tests/PageRequestTests.cs` (F1-21) — validación de límites (min/max, sin truncar en silencio)
- `tests/Shared.Infrastructure.Persistence.Tests/ReadOnlyRepositoryBaseTests.cs` — pruebas de comportamiento (`ChangeTracker.Entries()` vacío tras una lectura vía `IReadRepository`, proyección, paginación, overloads de `PageRequest` y recorte defensivo de `NormalizePaging`).
- `samples/Sample.Api.Tests/Integration/ProductosEndpointsIntegrationTests.cs` — verificación de punta a punta de "ningún endpoint ilimitado" contra el endpoint real `GET /productos`.
- `docs/convenciones.md` — regla dura #2 (`IQuery` nunca muta), regla dura #5 (nunca exponer `IQueryable`) y regla dura (F1-21) sobre límites de paginación.
