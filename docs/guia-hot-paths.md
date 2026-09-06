# Guía — Hot paths: queries y repositorios especializados (F1-18)

Amplía `docs/guia-queries-eficientes.md` (F1-17): cuándo el patrón genérico
(`ISpecification<T>`/`IReadRepository<TEntity, TId>`) **no alcanza**, y cómo escribir una consulta
especializada sin volver a exponer `IQueryable` ni abrir la puerta a SQL crudo sin control. No repite
las reglas duras de `docs/convenciones.md` — las asume vigentes, en particular la regla dura #5
("nunca exponer `IQueryable` desde un repositorio") y la regla dura #2 ("un `IQuery` nunca muta").

## Cuándo usar `ISpecification<T>` (F1-17) y cuándo un hot path (F1-18)

| Necesito... | Uso |
|---|---|
| Filtrar/ordenar/incluir/paginar una entidad | `ISpecification<T>` + `IReadRepository<,>.ListAsync`/`ListPagedAsync` (F1-17) — es el camino por defecto, cubre la enorme mayoría de los casos |
| Proyectar a un DTO sin traer la entidad completa | `IReadRepository<,>.ListAsync<TResult>`/`ListPagedAsync<TResult>` con `selector` (F1-17) |
| Un **agregado** (`SUM`, `AVG`, `COUNT` + `SUM` combinados, etc.) sobre una tabla grande sin traer las filas filtradas a memoria | `IHotPathQuery<TResult>` + `IHotPathQueryExecutor` (F1-18) — ver más abajo |
| Un `JOIN`/CTE/función de ventana que LINQ-to-Entities no traduce bien, o un hint específico del motor (`WITH (NOLOCK)`, `OPTION (RECOMPILE)`) | `IHotPathQuery<TResult>` + `IHotPathQueryExecutor` (F1-18), con SQL parametrizado |
| Cualquier otro caso | Empezar siempre por `ISpecification<T>`. Un hot path es la excepción documentada, no el default |

**Regla práctica:** si podés expresarlo con `ISpecification<T>` (aunque sea con un `selector` de
proyección), usá eso. Un hot path es para el caso en que el contrato genérico **no tiene** el método
necesario (por ejemplo, no existe un `SumAsync`/`AverageAsync` en `IReadRepository<,>` — a propósito,
para no convertir el repositorio genérico en un ORM completo) o en que expresar la consulta con LINQ
obliga a materializar más datos de los que hacen falta.

## El contrato de extensión

### 1. `IHotPathQuery<TResult>` (`Shared.Domain`, namespace `BitCode.Framework.Shared.Domain.HotPaths`)

```csharp
public interface IHotPathQuery<TResult>
    where TResult : class
{
    FormattableString ToSql();
}
```

- `TResult` es siempre un DTO plano materializado (una clase con propiedades públicas que EF Core
  mapea por nombre de columna), **nunca** una entidad de dominio ni un `IQueryable` — la interfaz vive
  en `Shared.Domain` (sin referencia a EF Core) precisamente para que un objeto de esta forma se pueda
  definir junto al resto del modelo de aplicación, no solo en la capa de infraestructura.
- `ToSql()` devuelve un `FormattableString`, no un `string` concatenado — cualquier valor interpolado
  (`{threshold}`, `{tenantId}`, etc.) se traduce a un parámetro real de SQL, exactamente igual que
  `FromSqlInterpolated`/`FromSqlRaw` con parámetros de EF Core. **Nunca** construir el SQL con
  `string.Concat`/`$"...{valorSinInterpolación}"` fuera de este mecanismo — sería inyección SQL.

### 2. `HotPathAttribute` (`Shared.Domain`, mismo namespace)

```csharp
[AttributeUsage(AttributeTargets.Class)]
public sealed class HotPathAttribute(string justification, string benchmarkRef) : Attribute
{
    public string Justification { get; }
    public string BenchmarkRef { get; }
}
```

Obligatorio sobre toda clase que implemente `IHotPathQuery<TResult>`. `Justification` explica en una
frase por qué `ISpecification<T>` no alcanzaba; `BenchmarkRef` apunta a un documento o test
reproducible con los números reales (por ejemplo, `"docs/guia-hot-paths.md#benchmark-resumen-de-agregados"`
o el nombre completo de un test de benchmark en el repo). Esto no es solo una convención documental:
**`HotPathQueryExecutor` lo exige en tiempo de ejecución** — ver más abajo.

### 3. `IHotPathQueryExecutor` (`Shared.Infrastructure.Persistence`, registrado por `AddSharedPersistence`)

```csharp
public interface IHotPathQueryExecutor
{
    Task<IReadOnlyList<TResult>> ListAsync<TResult>(IHotPathQuery<TResult> query, CancellationToken ct = default) where TResult : class;
    Task<TResult?> SingleOrDefaultAsync<TResult>(IHotPathQuery<TResult> query, CancellationToken ct = default) where TResult : class;
}
```

Único punto de entrada permitido para ejecutar un `IHotPathQuery<TResult>` — un handler de aplicación
lo inyecta igual que ya inyecta `IReadRepository<,>`/`IUnitOfWork`, **nunca** un `DbContext`
directamente. Internamente:

1. Valida (con reflexión, cacheada por tipo con `ConcurrentDictionary` para no pagar ese costo en cada
   ejecución de una consulta que, por definición, es de alta frecuencia) que la clase concreta de la
   consulta declare `[HotPath(Justification, BenchmarkRef)]` con ambos valores no vacíos. Si falta el
   atributo o está incompleto, lanza `InvalidOperationException` — el mecanismo simplemente no ejecuta
   una consulta especializada sin evidencia documentada de que hacía falta.
2. Traduce `ToSql()` a `dbContext.Database.SqlQuery<TResult>(...)` de EF Core (parametrizado) y
   materializa el resultado (`ToListAsync`/`SingleOrDefaultAsync`) antes de devolverlo — el
   `IQueryable` intermedio que EF Core construye nunca sale de `HotPathQueryExecutor`.

Un proyecto consumidor no registra nada adicional: `AddSharedPersistence<TContext>()` ya registra
`IHotPathQueryExecutor` como `Scoped` sobre el mismo `DbContext` de la unidad de trabajo actual (mismo
patrón que `IUnitOfWork`/`IRepository<,>`/`IReadRepository<,>`).

## Ejemplo completo (demostrativo, en el repo)

`tests/Shared.Infrastructure.Persistence.Tests/HotPaths/ResumenAmountHotPathQuery.cs`:

```csharp
[HotPath(
    justification: "El camino genérico (ISpecification/IReadRepository, F1-17) no tiene ningún " +
        "método de agregación: CountAsync cuenta filas, pero sumar Amount exige materializar todas " +
        "las filas filtradas con ListAsync(spec, selector) y sumarlas en memoria.",
    benchmarkRef: "docs/guia-hot-paths.md#benchmark-resumen-de-agregados")]
public sealed class ResumenAmountHotPathQuery(int threshold) : IHotPathQuery<ResumenAmountResult>
{
    public FormattableString ToSql() =>
        $"""
         SELECT COUNT(*) AS Total, COALESCE(SUM(Amount), 0) AS SumaAmount
         FROM TestEntities
         WHERE Amount > {threshold} AND IsDeleted = 0
         """;
}

public sealed class ResumenAmountResult
{
    public int Total { get; set; }
    public long SumaAmount { get; set; }
}
```

Uso desde un handler (`IQuery`, nunca muta):

```csharp
public class ObtenerResumenQueryHandler(IHotPathQueryExecutor hotPaths)
    : IRequestHandler<ObtenerResumenQuery, Result<ResumenResponse>>
{
    public async Task<Result<ResumenResponse>> Handle(ObtenerResumenQuery request, CancellationToken ct)
    {
        var resumen = await hotPaths.SingleOrDefaultAsync(new ResumenAmountHotPathQuery(request.Threshold), ct);
        return resumen is null
            ? Result.Failure<ResumenResponse>(Error.NotFound(...))
            : new ResumenResponse(resumen.Total, resumen.SumaAmount);
    }
}
```

## Benchmark — resumen de agregados (justifica el bypass del ejemplo)

**Tarea:** F1-18. **Fecha de medición:** 2026-09-06. **Entorno:** SQLite in-memory (misma conexión,
mismo dataset entre ambos caminos), mismo patrón de medición que `docs/benchmark-multitenancy.md`
(F1-11) — no representa el costo de red/E-S de SQL Server real, aísla el costo de EF Core/materialización.

**Test reproducible:** `tests/Shared.Infrastructure.Persistence.Tests/HotPaths/HotPathBenchmarkTests.cs`
(`Benchmark_GenericSpecificationPath_Vs_HotPath`), corrida también con `dotnet test --filter`.

**Diseño:** 5.000 filas (`TestEntity`, `Amount` aleatorio 0–4.999 con semilla fija), filtro
`Amount > 2.500` (~la mitad de las filas). Se compara, sobre la misma conexión y con warm-up previo
(para no medir la primera compilación de query/plan), 50 iteraciones de:

- **Genérico** (`IReadRepository<,>`, F1-17): `CountAsync(spec)` + `ListAsync(spec, e => e.Amount)`
  (proyección, ya optimizada según F1-17 — solo trae la columna `Amount`, no la fila completa) +
  `Sum()` en memoria del lado de la aplicación. Es el único camino posible con el contrato genérico
  actual: no existe `SumAsync` en `IReadRepository<,>`.
- **Hot path** (F1-18): `ResumenAmountHotPathQuery` — un único `SELECT COUNT(*), SUM(Amount)`.

**Resultados (3 corridas):**

| Corrida | Genérico (ms/iteración) | Hot path (ms/iteración) | Speedup |
|---|---|---|---|
| 1 | 2.9633 | 0.3526 | 8.4x |
| 2 | 2.8939 | 0.3651 | 7.9x |
| 3 | 3.2035 | 0.3253 | 9.8x |

**Lectura:** el hot path es consistentemente **8x–10x más rápido** en las 3 corridas, con resultados
idénticos (`GenericPathAndHotPath_ProduceTheSameResult` verifica que ambos caminos calculan el mismo
`Total`/`SumaAmount`). La causa es estructural, no una casualidad del entorno: el camino genérico
transfiere ~2.500 filas (la mitad de la tabla) desde SQLite al proceso de la aplicación para sumarlas
ahí, mientras que el hot path calcula el agregado dentro del motor y transfiere una sola fila. Esta
diferencia **escala con el tamaño de la tabla y con qué tan selectivo sea el filtro** — cuantas más
filas cumplan el filtro, mayor la ventaja del hot path; el caso contrario (un filtro muy selectivo que
deja pocas filas) reduciría la diferencia, porque ahí el costo de transferir filas ya es bajo en ambos
caminos.

**Honestidad sobre el alcance de esta medición:** SQLite in-memory no reproduce el costo de red/E-S de
SQL Server real (donde la ventaja de reducir el volumen transferido por la red suele ser aún mayor que
en un motor embebido). No se midió contra SQL Server ni Testcontainers en esta tarea — si un caso real
del proyecto necesita esta decisión con datos de SQL Server, repetir esta misma metodología (mismo
patrón de warm-up + 3 corridas) contra ese motor antes de decidir.

## Qué NO habilita este mecanismo

1. **No es una forma de esquivar el patrón genérico "porque sí"** — `HotPathQueryExecutor` exige el
   atributo `[HotPath]` con justificación y referencia a benchmark; sin eso, lanza
   `InvalidOperationException` en la primera ejecución.
2. **No expone `IQueryable`** — `ToSql()` devuelve un `FormattableString` (dato inerte, sin ejecutar
   nada), y el `IQueryable` que EF Core construye internamente en `HotPathQueryExecutor` nunca sale de
   esa clase: siempre se materializa (`ToListAsync`/`SingleOrDefaultAsync`) antes de devolver el
   resultado.
3. **No habilita SQL crudo sin parametrizar** — todo valor variable debe llegar por interpolación
   dentro del `FormattableString` (parametrizado automáticamente por EF Core), nunca por
   concatenación de strings.
4. **Sigue centralizado en infraestructura** — un handler de aplicación inyecta `IHotPathQueryExecutor`
   (una interfaz), nunca un `DbContext` ni `SqlConnection` directamente.

## Referencias

- `src/Shared.Domain/HotPaths/IHotPathQuery.cs`, `HotPathAttribute.cs`
- `src/Shared.Infrastructure.Persistence/HotPaths/IHotPathQueryExecutor.cs`, `HotPathQueryExecutor.cs`
- `tests/Shared.Infrastructure.Persistence.Tests/HotPaths/` — `ResumenAmountHotPathQuery.cs` (ejemplo),
  `HotPathQueryExecutorTests.cs` (validación del atributo obligatorio),
  `HotPathBenchmarkTests.cs` (benchmark reproducible)
- `docs/guia-queries-eficientes.md` (F1-17) — el patrón genérico que este documento amplía
- `docs/convenciones.md` — regla dura #5 (nunca exponer `IQueryable`) y regla dura #2 (`IQuery` nunca muta)
- `docs/benchmark-multitenancy.md` (F1-11) — metodología de microbenchmark con SQLite in-memory reutilizada aquí
