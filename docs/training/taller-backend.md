# Taller de Backend — De cero a un vertical slice CQRS real

**Audiencia:** desarrolladores backend nuevos en BitCode.Framework.
**Objetivo de aprendizaje:** al terminar este taller vas a poder generar un módulo nuevo, agregarle un
caso de uso (vertical slice CQRS: Command o Query + Validator + Handler + endpoint HTTP) siguiendo las
convenciones duras del framework, y probarlo con una prueba unitaria real — sin copiar y pegar código a
ciegas.

**Duración estimada:** 60-90 minutos.

**Cómo se verificó este taller:** los pasos de las secciones 2 a 5 se ejecutaron de punta a punta durante
la preparación de esta tarea (F10-09), en un directorio de trabajo temporal fuera del repositorio, contra
`dotnet --version` `10.0.302`, sin backend de base de datos corriendo (las pruebas del módulo son
unitarias, contra un repositorio en memoria — ver sección 5). Se encontró una fricción real de compilación
al wirear el endpoint (sección 4.3), corregida y documentada en la sección 6 — este taller la incluye tal
como se resolvió, no como un problema oculto.

## 0. Prerrequisitos concretos

- **.NET SDK 10** instalado (`dotnet --version` debe imprimir `10.x`). El framework fija
  `<TargetFramework>net10.0</TargetFramework>` en `Directory.Build.props` de todo `src/`.
- Un checkout del repositorio `BitCode.Framework` en tu máquina (para `--SharedSourceRoot`, ver sección
  1 — el framework todavía no publica paquetes NuGet, se consume por `ProjectReference` directa, ver
  `docs/guia-uso-proyectos.md` sección 1).
- **No hace falta** Docker ni SQL Server corriendo para este taller: las pruebas que vas a escribir son
  unitarias, contra un repositorio de prueba en memoria (`FakeElementoRepository`, ver sección 5) — el
  módulo generado por `dotnet new bitcode-module` ya viene con ese patrón. Si más adelante querés levantar
  el módulo contra un backend real, ver `docs/guia-entorno-local.md` y `docs/guia-cli-diagnostico.md`
  (cubierto en el taller de operación).
- Haber leído (no memorizado) `docs/convenciones.md`, sección "Reglas duras" — este taller te hace
  aplicarlas, no te las vuelve a explicar en abstracto.

### Checkpoint 0

```bash
dotnet --version
```
Debe imprimir `10.x`. Si no, instalá el SDK correspondiente antes de seguir.

## 1. Generar un módulo nuevo (`dotnet new bitcode-module`)

Un **módulo** en BitCode es un bounded context propio: dueño exclusivo de su esquema de base de datos, su
catálogo de permisos y su superficie pública (`ServiceCollectionExtensions`/
`EndpointRouteBuilderExtensions`) — nunca referencia a otro módulo de negocio por `ProjectReference`
directo (ver `templates/module/README.md`).

Parados en la raíz del checkout del framework:

```bash
dotnet new install ./templates/module   # si no lo tenés instalado todavía
dotnet new bitcode-module -n Inventario -o modules/Inventario --Namespace MiEmpresa.Modules.Inventario
```

**Sobre `-o modules/Inventario` (dos niveles bajo la raíz del repo):** el parámetro `--SharedSourceRoot`
del template tiene como default `../../src` (relativo al directorio de salida), asumiendo esa profundidad
— generar directamente en la raíz (`-o Inventario`) rompe esa referencia relativa. Si preferís generar el
módulo en otro lugar (por ejemplo, fuera del árbol del repositorio, como se hizo en la verificación de
este taller), pasá `--SharedSourceRoot` con una ruta absoluta a la carpeta `src/` del framework:

```bash
dotnet new bitcode-module -n Inventario -o /ruta/fuera/del/repo/Inventario \
  --Namespace MiEmpresa.Modules.Inventario \
  --SharedSourceRoot "/ruta/absoluta/a/BitCode.Framework/src"
```

**Nota sobre `-n`:** a diferencia de `dotnet new bitcode-app`, acá el nombre pasado con `-n` se usa
directamente como prefijo de identificadores C# (`InventarioDbContext`, `InventarioPermissions`,
`AddSharedInventario()`, etc.) — **no puede contener un punto**. Si querés que el namespace refleje tu
organización, usá `--Namespace` por separado (como en el ejemplo de arriba).

### Checkpoint 1

```bash
cd modules/Inventario
dotnet build
```

Debe terminar con `Compilación correcta` (0 errores; vas a ver warnings preexistentes de vulnerabilidades
de paquetes de terceros y de un feed NuGet corporativo inalcanzable — no son de tu módulo, son los mismos
que aparecen al compilar `samples/Sample.Api`, ya documentado en `docs/revision-documental-fase10.md`).

El módulo ya viene con un agregado de ejemplo completo (`Elementos/`: entidad `Elemento`, un comando
`CrearElementoCommand` y dos queries `ObtenerElementoQuery`/`ListarElementosQuery`) y sus propias pruebas
(`Inventario.Tests/`) — correlas ahora para confirmar que el punto de partida funciona:

```bash
dotnet test Inventario.Tests/Inventario.Tests.csproj
```

Debe imprimir `Superado: 4` (0 fallos).

## 2. Agregar un caso de uso nuevo (`dotnet new bitcode-feature`)

Supongamos que necesitás un caso de uso nuevo dentro del mismo agregado `Elementos`: **listar solo los
elementos activos** (una Query). `dotnet new bitcode-feature` genera el vertical slice completo (record +
`AbstractValidator` si es Command, o `record` + `Response` si es Query, más el `IRequestHandler` y el
endpoint) — **corrido parado dentro de la carpeta del agregado** (`Elementos/`), no en la raíz del módulo:

```bash
cd Elementos
dotnet new install ../../../templates/feature   # ruta relativa al checkout del framework; ajustar según dónde generaste el módulo
dotnet new bitcode-feature -n ListarElementosActivos --Namespace MiEmpresa.Modules.Inventario.Elementos --Kind Query
```

(La plantilla `bitcode-feature`, igual que `bitcode-module`, solo hace falta instalarla una vez por
máquina — `dotnet new install` es idempotente; si ya la instalaste antes, este paso no hace nada nuevo.)

Esto genera dos archivos nuevos:

- `ListarElementosActivosQuery.cs` — el `record` de la Query, el `record` del Response, y el
  `IRequestHandler` con el cuerpo `throw new NotImplementedException(...)` **a propósito** — el template
  scaffoldea la forma del caso de uso, no inventa lógica de negocio (ver `templates/feature/README.md`).
- `ListarElementosActivosQueryEndpoints.cs` — la extensión `MapListarElementosActivosEndpoint`, todavía
  sin wirear a ningún grupo de rutas real.

### Checkpoint 2

```bash
dotnet build
```

Debe compilar igual que antes (el `NotImplementedException` está dentro de un método, no se ejecuta al
compilar) — si falla acá, revisá que `--Namespace` coincida exactamente con el namespace real de los
archivos vecinos (`Elemento.cs`, `CrearElementoCommand.cs`) en la misma carpeta.

## 3. Implementar el handler siguiendo el golden path real

Reemplazá el `throw new NotImplementedException(...)` por la lógica real, **siguiendo el mismo patrón que
ya usa `ListarElementosQuery.cs`** en el mismo agregado (generado por `dotnet new bitcode-module`, es tu
referencia viva, no un ejemplo aparte del Plan Maestro):

```csharp
using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace MiEmpresa.Modules.Inventario.Elementos;

public record ListarElementosActivosQuery : IQuery<IReadOnlyList<ElementoResponse>>;

public class ListarElementosActivosQueryHandler(IReadRepository<Elemento, Guid> repository)
    : IRequestHandler<ListarElementosActivosQuery, Result<IReadOnlyList<ElementoResponse>>>
{
    public async Task<Result<IReadOnlyList<ElementoResponse>>> Handle(
        ListarElementosActivosQuery request, CancellationToken cancellationToken)
    {
        var elementos = await repository.ListAsync(
            new TodosLosElementosOrdenadosPorNombreSpecification(),
            e => new ElementoResponse(e.Id, e.Nombre),
            cancellationToken);

        return Result.Success<IReadOnlyList<ElementoResponse>>(elementos);
    }
}
```

Tres decisiones de convención que este código ya respeta (repasalas contra `docs/convenciones.md` mientras
las escribís, no después):

1. **`IReadRepository<Elemento, Guid>`, nunca `IRepository<Elemento, Guid>`** — regla dura #15: un handler
   de `IQuery` inyecta el repositorio de solo lectura (fuerza `AsNoTracking()`), nunca el de escritura.
2. **`ElementoResponse` vía `selector`, no la entidad completa** — el `SELECT` generado por EF Core solo
   trae las columnas usadas por el `selector` (`docs/guia-queries-eficientes.md`). Reutilizás el mismo
   `ElementoResponse`/`TodosLosElementosOrdenadosPorNombreSpecification` que ya existen en
   `ListarElementosQuery.cs` — no dupliques un DTO ni una Specification que ya cubre lo que necesitás.
3. **El handler nunca llama `SaveChangesAsync`** — no aplica acá porque es una Query (regla dura #2: un
   `IQuery` nunca modifica datos), pero es el mismo criterio que vas a aplicar cuando el próximo caso de
   uso sea un Command.

**Nota de diseño, no fricción:** este ejemplo usa `ListAsync` (colección completa) en vez de
`ListPagedAsync` a propósito, para simplificar el taller — en un caso de uso real, la regla dura #16
(`docs/convenciones.md`) exige paginar cualquier listado que pueda crecer sin límite con la actividad del
negocio. `ListarElementosQuery.cs` (el ejemplo que ya trae el módulo) sí pagina — usalo como referencia
cuando el listado real de tu dominio no esté "estructuralmente acotado" (catálogo pequeño y fijo).

### Checkpoint 3

```bash
dotnet build
```

Debe compilar sin errores.

## 4. Wirear el endpoint al grupo de rutas del módulo

`dotnet new bitcode-feature` **nunca** wirea el endpoint por vos (ver `templates/feature/README.md`, "Qué
NO genera este template") — cada módulo mapea todos sus endpoints desde un único archivo agregador
(`InventarioEndpointRouteBuilderExtensions.cs`, generado por `dotnet new bitcode-module`).

### 4.1 Elegir una ruta que no colisione con una ya existente

El módulo ya tiene un `GET /` mapeado a `ListarElementosQuery` (paginado). Montá el nuevo endpoint en un
sub-path distinto, por ejemplo `/activos`:

```csharp
// Dentro de InventarioEndpointRouteBuilderExtensions.MapInventarioEndpoints, después de los
// endpoints ya existentes de "elementos":
elementos.MapGroup("/activos")
    .MapListarElementosActivosEndpoint()
    .RequireAuthorization(InventarioPermissions.ElementosVer);
```

### 4.2 Autorización

Igual que los endpoints de lectura ya existentes en el módulo (`ObtenerElementoQuery`,
`ListarElementosQuery`), encadená `.RequireAuthorization(InventarioPermissions.ElementosVer)` — nunca un
endpoint sin política de autorización explícita (Zero Trust por defecto, `docs/convenciones.md`).

### 4.3 Anotar OpenAPI en el lugar correcto (fricción real, ver sección 6)

Si intentás encadenar `.Produces<T>(...)` DESPUÉS de `MapListarElementosActivosEndpoint()` en el
agregador (paso 4.1), el build **falla** con `CS1929` — la extensión `MapListarElementosActivosEndpoint`
devuelve `IEndpointConventionBuilder` (regla dura #21 de `docs/convenciones.md`: el generador nativo de
OpenAPI necesita anotaciones explícitas), y `.Produces<T>()` es una extensión de `RouteHandlerBuilder`, no
de esa interfaz más genérica. La anotación va DENTRO de `ListarElementosActivosQueryEndpoints.cs`, sobre el
`RouteHandlerBuilder` concreto que devuelve `MapGet` directamente:

```csharp
// Dentro de MapListarElementosActivosEndpoint, en ListarElementosActivosQueryEndpoints.cs:
return group.MapGet("/", async (ISender sender, CancellationToken ct) =>
{
    var result = await sender.Send(new ListarElementosActivosQuery(), ct);
    return result.ToOkOrProblem();
})
    .Produces<IReadOnlyList<ElementoResponse>>(StatusCodes.Status200OK);
```

(Necesitás agregar `using Microsoft.AspNetCore.Http;` para `StatusCodes` si el archivo generado todavía no
lo tiene.)

### Checkpoint 4

```bash
dotnet build
```

Debe compilar sin errores (`Compilación correcta`) — si ves `CS1929` acá, revisá que moviste el
`.Produces<T>()` al archivo correcto (sección 4.3), no al agregador.

## 5. Probar el handler (prueba unitaria, sin infraestructura real)

Seguí el mismo patrón que `Inventario.Tests/CrearElementoCommandHandlerTests.cs` (generado junto con el
módulo): un repositorio en memoria (`FakeElementoRepository`, ya generado, implementa
`IRepository<Elemento, Guid>`, que a su vez extiende `IReadRepository<Elemento, Guid>` — te sirve tal cual
para probar un handler de Query sin escribir un doble nuevo).

```csharp
// Inventario.Tests/ListarElementosActivosQueryHandlerTests.cs
using MiEmpresa.Modules.Inventario.Elementos;

namespace MiEmpresa.Modules.Inventario.Tests;

public sealed class ListarElementosActivosQueryHandlerTests
{
    [Fact]
    public async Task Handle_ConElementosCargados_DevuelveTodosOrdenadosPorNombre()
    {
        var repository = new FakeElementoRepository();
        await repository.AddAsync(new Elemento(Guid.NewGuid(), "Zapallo"), CancellationToken.None);
        await repository.AddAsync(new Elemento(Guid.NewGuid(), "Acelga"), CancellationToken.None);
        var handler = new ListarElementosActivosQueryHandler(repository);

        var result = await handler.Handle(new ListarElementosActivosQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
    }

    [Fact]
    public async Task Handle_SinElementos_DevuelveListaVacia()
    {
        var repository = new FakeElementoRepository();
        var handler = new ListarElementosActivosQueryHandler(repository);

        var result = await handler.Handle(new ListarElementosActivosQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }
}
```

### Checkpoint 5 (final del taller)

```bash
dotnet test Inventario.Tests/Inventario.Tests.csproj
```

Debe imprimir `Superado: 6` (los 4 originales + tus 2 nuevos), `Con error: 0`. Si llegaste hasta acá con
el checkpoint en verde, completaste un vertical slice CQRS real siguiendo las reglas duras del framework,
de punta a punta.

## 6. Problemas comunes (hallazgos reales, no hipotéticos)

| Problema | Causa | Solución |
|---|---|---|
| `CS1929` al encadenar `.Produces<T>()` después de `MapXxxEndpoint()` en el agregador del módulo | `MapXxxEndpoint` (generado por `dotnet new bitcode-feature`) devuelve `IEndpointConventionBuilder`, no `RouteHandlerBuilder` — es intencional (regla dura #21, `docs/convenciones.md`), no un bug del template. | Mové la anotación `.Produces<T>()`/`.ProducesProblem()` DENTRO del archivo `*Endpoints.cs` generado, encadenada directamente sobre el `MapGet`/`MapPost` (ver sección 4.3). |
| Dos `MapGet("/")` en el mismo grupo de rutas | Copiaste el patrón de otro endpoint de listado sin revisar que la ruta raíz del grupo (`/`) ya estaba tomada. | Montá el endpoint nuevo bajo un sub-path propio (`elementos.MapGroup("/activos")...`, ver sección 4.1) o cambiá la ruta del endpoint ya existente si el nuevo es el "canónico". |
| `dotnet new bitcode-feature` falla con un error de namespace/tipo no encontrado | `--Namespace` no coincide EXACTAMENTE con el namespace real de los archivos vecinos en la carpeta donde corriste el comando. | Verificá el namespace de `Elemento.cs`/`CrearElementoCommand.cs` en la misma carpeta antes de generar (`--Namespace` debe ser idéntico, no solo "parecido"). |
| `dotnet run --project tools/BitCode.Migrations -- validate --assembly ...` falla con `Could not load file or assembly 'Microsoft.AspNetCore...'` | Limitación conocida y documentada: `--assembly` apuntando a un ensamblado `Microsoft.NET.Sdk.Web` (cualquier host real de este framework) falla al cargarse por reflexión desde el proceso de consola de `BitCode.Migrations` — no es específico de este taller, aplica a cualquier host. | Ver `docs/guia-migraciones.md` sección 3.2 (workaround verificado con `dotnet exec --runtimeconfig/--depsfile`) — cubierto en detalle en `taller-operacion.md`. |
| `status`/`migrate`/`rollback` de `BitCode.Migrations` fallan con "No se pudo instanciar el DbContext" | El `DbContext` hereda de `MultiTenantDbContext`/`MultiTenantIdentityDbContext<,>` (constructor con `ITenantProvider` además de `DbContextOptions<T>`) y el proyecto no implementa `IDesignTimeDbContextFactory<TContext>`. | Implementar `IDesignTimeDbContextFactory<TContext>` en el proyecto host (ver `docs/guia-migraciones.md` sección 3.2, segunda limitación) — no aplica a este taller (no llegamos a correr migraciones reales), documentado acá para cuando avances a un módulo con base de datos real. |

## 7. Qué sigue después de este taller

- Si tu caso de uso real necesita escribir (no solo leer), repetí este taller con `--Kind Command` y leé
  la regla dura #1/#3/#4 de `docs/convenciones.md` (`TransactionBehavior`, `ITransactionalCommand`,
  `IIdempotentCommand`) antes de escribir el handler.
- Para instalar tu módulo en una app host real (no solo sus propias pruebas unitarias), ver
  `templates/module/README.md`, sección "Cómo instalar este módulo en una app host".
- Para conectar tu módulo a un frontend real, ver `taller-frontend.md` de esta misma carpeta.
- Para operar tu módulo una vez desplegado (diagnóstico, migraciones, incidentes), ver
  `taller-operacion.md`.
