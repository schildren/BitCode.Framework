# Checklist — Índices y consultas SQL (F1-20)

Política práctica de revisión de índices y consultas para cualquier desarrollador que agregue una
entidad nueva, una consulta nueva, o modifique una existente. Amplía `docs/guia-queries-eficientes.md`
(F1-17) y `docs/guia-hot-paths.md` (F1-18) — no repite las reglas duras de `docs/convenciones.md`, las
asume vigentes.

## Estado relevado antes de esta tarea

- Ninguna entidad del framework ni de `samples/Sample.Api` declaraba un índice explícito (`HasIndex`)
  más allá de la clave primaria — confirmado por búsqueda (`HasIndex`) sobre todo el repositorio.
- `MultiTenancyModelConfigurator.ApplyGlobalFilters` (F1-11/F1-12) agrega, a **toda** entidad
  `ITenantEntity`, un filtro global `WHERE TenantId = @tenantId` (más `IsDeleted = 0` si la entidad
  también implementa `ISoftDelete`) — un predicado que EF Core aplica a **cada** consulta contra esa
  entidad, sin excepción, sin que el desarrollador de un `IQuery`/`ISpecification` lo escriba a mano.
- Sin un índice que arranque por `TenantId`, ese filtro se resolvía con un table/clustered index scan
  completo — un costo que crece con el volumen **total** de la tabla (todos los tenants juntos), no
  con el volumen del tenant consultado. Es el peor patrón de escalado posible para una tabla
  multi-tenant que crece con el tiempo.
- El repo no tiene ningún flujo de migraciones EF Core generadas todavía (no existe ninguna carpeta
  `Migrations/` en ningún proyecto) — los cambios de modelo (como el índice de esta tarea) se aplican
  hoy vía `OnModelCreating`/`EnsureCreatedAsync`, no vía migración manual.

## Qué cambió

`TenantIndexModelConfigurator` (`src/Shared.Infrastructure.Persistence/MultiTenancy/TenantIndexModelConfigurator.cs`)
agrega automáticamente, por reflexión (mismo patrón que `MultiTenancyModelConfigurator` y
`ConcurrencyModelConfigurator`), un índice a toda entidad `ITenantEntity` detectada en el modelo:

- `HasIndex(TenantId)` si la entidad solo implementa `ITenantEntity`.
- `HasIndex(TenantId, IsDeleted)` (compuesto, en ese orden) si la entidad también implementa
  `ISoftDelete` — porque el filtro global real que EF Core ejecuta combina ambos predicados con AND.

Se invoca desde `MultiTenantDbContext.OnModelCreating` y `MultiTenantIdentityDbContext.OnModelCreating`
(las dos bases de `DbContext` que aplican el filtro global) — un proyecto consumidor no configura nada
adicional, el índice aparece la primera vez que se crea/migra la base de datos.

**No se generó ninguna migración manual** — el repo no tiene el flujo de migraciones EF Core
establecido (ver estado relevado arriba); el índice se refleja en la próxima migración que un proyecto
consumidor genere con `dotnet ef migrations add`, o directamente en el esquema si el proyecto usa
`EnsureCreatedAsync` (como `samples/Sample.Api` y todos los tests de integración del repo hoy).

**Ninguna entidad real de `samples/Sample.Api` implementa `ITenantEntity` hoy** (`Producto` es
`ISoftDelete` pero no `ITenantEntity`) — el efecto práctico inmediato de esta tarea es sobre el
framework para cualquier proyecto consumidor que sí use entidades tenant-aware, y sobre las entidades
de prueba (`TestEntity`) que ya lo son.

## Checklist — al agregar una entidad nueva

1. ¿La entidad implementa `ITenantEntity`? Si sí, no hace falta declarar el índice a mano —
   `TenantIndexModelConfigurator` lo agrega automáticamente. Verificar igual, tras la primera
   migración/`EnsureCreatedAsync`, que el índice exista (`sys.indexes` o el snapshot del modelo de EF
   Core) — es una convención automática, pero conviene confirmarla la primera vez que se usa en un
   proyecto nuevo.
2. ¿La entidad tiene alguna otra columna de filtrado muy frecuente además de `TenantId`/`IsDeleted`
   (por ejemplo, un `Estado`/`Status` que casi toda consulta de listado filtra)? Si sí, **no** agregar
   un índice especulativo sin evidencia — medir primero con un plan de ejecución real (sección
   "Cómo capturar un plan real" más abajo) contra un volumen de datos representativo, y documentar el
   resultado (ver `docs/evidencia-indices-sql.md` como ejemplo del formato).
3. ¿La entidad va a tener consultas de alto volumen por una columna que no es `TenantId` (por ejemplo,
   una búsqueda por email, código externo, o similar)? Mismo criterio: medir antes de indexar.

## Checklist — al agregar una query nueva (`ISpecification`, hot path, o SQL directo)

1. **¿La query filtra por una columna sin índice?** Si la tabla ya tiene varios miles de filas (o se
   espera que las tenga), un `WHERE` sobre una columna sin índice de soporte es candidato a table/
   clustered index scan. No es necesariamente un problema (una tabla pequeña, o un filtro que no es de
   alta frecuencia, puede no justificar un índice) — pero hay que poder responder conscientemente, no
   por default.
2. **Si la entidad es `ITenantEntity`, ¿el filtro adicional de la query puede aprovechar el índice
   `(TenantId, IsDeleted)` (o `(TenantId)`) que ya existe?** Un índice compuesto sirve mejor cuando las
   columnas de la query aparecen en el mismo orden que el índice, empezando por la primera. Si la
   query filtra por `TenantId` + una tercera columna de alta selectividad, evaluar si conviene ampliar
   el índice compuesto (con evidencia de plan, no especulativamente) en vez de agregar un índice nuevo
   y separado.
3. **¿Hay un `ORDER BY` sin índice que lo soporte, en una query paginada (`ListPagedAsync`,
   F1-17)?** Sin un índice que cubra el orden, SQL Server necesita un `Sort` explícito tras el filtro
   — aceptable para volúmenes chicos/medianos, pero a vigilar si el volumen crece y el `ORDER BY` es de
   alta frecuencia (listados con paginación que el usuario dispara constantemente).
4. **¿Se validó el plan de ejecución real para una query de alta frecuencia (hot path, endpoint muy
   usado, job recurrente)?** No hace falta para toda query — es la misma barra que ya exige
   `docs/guia-hot-paths.md` para justificar un `IHotPathQuery<TResult>`: si la consulta es
   suficientemente importante como para preocuparse por su plan, hay que poder mostrar el plan real
   (no solo asumir que "debería" ser eficiente).
5. **Un `IQuery` sigue usando `IReadRepository<,>`/`ISpecification<T>` (F1-17) como camino por
   defecto** — un índice nuevo no es sustituto de evitar `SELECT *`, evitar tracking innecesario
   (`AsNoTracking`, ya garantizado por F1-17), o evitar traer todas las filas para contarlas
   (`ListPagedAsync`). Revisar esa guía antes de asumir que el problema de rendimiento es "falta de
   índice".

## Cómo capturar un plan de ejecución real (mecanismo usado en esta tarea)

Se usó `SET STATISTICS XML ON` (plan de ejecución **real**, no estimado) leído como el segundo
resultset de un `SqlDataReader` sobre una conexión ADO.NET directa (`Microsoft.Data.SqlClient`), en vez
de `sys.dm_exec_query_plan` — no depende de que el plan siga en el caché de planes de SQL Server en el
momento de la consulta, y refleja exactamente la ejecución que se está midiendo:

```csharp
await using var command = connection.CreateCommand();
command.CommandText = "SET STATISTICS XML ON;";
await command.ExecuteNonQueryAsync();
// ... ejecutar la query real, leer el primer resultset (los datos) y luego reader.NextResultAsync()
// para leer el segundo resultset: una sola fila con una sola columna XML, el plan real.
```

Ver la implementación completa y reproducible en
`tests/Shared.Infrastructure.Persistence.Tests/Integration/TenantIndexIntegrationTests.cs`
(`CapturePlanAsync`). Contra el plan capturado, se buscan los operadores físicos relevantes como texto
plano dentro del XML (`"Clustered Index Scan"`, `"Index Seek"`, `"Table Scan"`, `"Key Lookup"`) — no
hace falta deserializar el XML completo para una verificación de "¿esto es un scan o un seek?".

Como evidencia complementaria (no sustituta del plan), medir tiempos reales con `Stopwatch` sobre
varias iteraciones con warm-up previo (mismo patrón que `docs/guia-hot-paths.md` y
`docs/benchmark-multitenancy.md`) — el plan explica **por qué** algo es lento; el tiempo real confirma
que el efecto es medible, no solo teórico.

## Evidencia real de esta tarea

Ver `docs/evidencia-indices-sql.md` para el resultado completo (antes/después, 3 corridas) de aplicar
`TenantIndexModelConfigurator` sobre `TestEntity` (20.000 filas, 20 tenants, filtro que deja el 5% de
la tabla) contra SQL Server real (Testcontainers).

## Referencias

- `src/Shared.Infrastructure.Persistence/MultiTenancy/TenantIndexModelConfigurator.cs`
- `src/Shared.Infrastructure.Persistence/MultiTenancy/MultiTenancyModelConfigurator.cs` (filtro global
  que este índice sirve)
- `tests/Shared.Infrastructure.Persistence.Tests/Integration/TenantIndexIntegrationTests.cs`
- `docs/evidencia-indices-sql.md`
- `docs/guia-queries-eficientes.md` (F1-17), `docs/guia-hot-paths.md` (F1-18)
- `docs/convenciones.md`
