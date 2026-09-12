# bitcode-feature

`dotnet new bitcode-feature` (BitCode.Framework) genera un **vertical slice CQRS completo** (Command o
Query + Validator + Handler + endpoint HTTP) para agregarlo **dentro de un módulo ya existente** -- a
diferencia de `dotnet new bitcode-app`/`dotnet new bitcode-module` (que generan un proyecto nuevo), este
es un template de tipo `item`: no crea ningún `.csproj`, solo agrega archivos `.cs` sueltos al directorio
donde lo corrés.

## Cuándo usarlo

Cuando un módulo generado con `dotnet new bitcode-module` (o cualquier módulo real del framework, ver
`src/Platform/BitCode.Platform.*/`) necesita un caso de uso nuevo además de los que ya tiene. Corré el
comando **parado dentro del proyecto del módulo destino**, en la subcarpeta donde vive el agregado al que
pertenece el feature (la misma convención que `Elementos/` en `dotnet new bitcode-module`: una subcarpeta
por agregado/bounded concept, con todos sus casos de uso adentro).

```bash
cd MiModulo/Pedidos
dotnet new bitcode-feature -n CrearPedido --Namespace MiEmpresa.Modules.Inventario.Pedidos --Kind Command --ResponseType Guid
dotnet new bitcode-feature -n ObtenerPedido --Namespace MiEmpresa.Modules.Inventario.Pedidos --Kind Query
```

- `-n` / `--name`: nombre del feature (verbo + sustantivo, ej. `CrearPedido`, `ObtenerPedido`,
  `ListarPedidos`) -- se usa como prefijo de todos los tipos generados (`CrearPedidoCommand`,
  `CrearPedidoCommandHandler`, `CrearPedidoEndpoints`, etc.).
- `--Namespace`: namespace completo del feature generado. Tiene que coincidir con el namespace real de la
  carpeta donde lo estás generando (el mismo namespace que ya usan los demás archivos de esa carpeta).
- `--Kind`: `Command` (default) o `Query`.
  - `Command` genera `<Nombre>Command.cs` (record + `AbstractValidator` + `IRequestHandler`, `ICommand<ResponseType>`)
    y `<Nombre>CommandEndpoints.cs` (`MapPost`).
  - `Query` genera `<Nombre>Query.cs` (record + `<Nombre>Response` + `IRequestHandler`, `IQuery<Response>`)
    y `<Nombre>QueryEndpoints.cs` (`MapGet`).
- `--ResponseType`: tipo de dato que retorna el Command (`ICommand<ResponseType>`, default `Guid`). Se
  ignora en `Kind=Query` -- la Query siempre retorna el record `<Nombre>Response` generado junto a ella.

## Qué generan los archivos

- El **Handler** viene con `throw new NotImplementedException(...)` a propósito -- este template scaffoldea
  la forma del caso de uso (los cuatro archivos/tipos que un vertical slice CQRS necesita, ya conectados
  entre sí y a MediatR/FluentValidation), no inventa lógica de negocio. Reemplazá el cuerpo por la
  implementación real (normalmente `IRepository<TEntity, TId>`/`IReadRepository<TEntity, TId>`, ver
  `Elementos/CrearElementoCommand.cs` y `Elementos/ObtenerElementoQuery.cs` en
  `dotnet new bitcode-module` para un ejemplo completo con entidad y repositorio).
- El **Command** NO llama `IUnitOfWork.SaveChangesAsync` explícitamente -- `TransactionBehavior` ya lo hace
  después de que el handler retorna un `Result` exitoso (ver
  `Shared.Application/Behaviors/TransactionBehavior.cs`).
- El **Endpoint** (`Map<Nombre>Endpoint`) es una extensión sobre `IEndpointRouteBuilder` pensada para
  encadenarse sobre el `MapGroup` ya versionado y autorizado del módulo -- no define su propio
  `MapGroup`, versión de API ni policy de autorización. Hay que wirearlo manualmente desde el
  `*EndpointRouteBuilderExtensions.cs` del módulo (el mismo archivo que genera
  `dotnet new bitcode-module`), por ejemplo:

  ```csharp
  var pedidos = endpoints.MapGroup("/api/v{version:apiVersion}/inventario/pedidos")
      .WithApiVersionSet(apiVersionSet)
      .HasApiVersion(new ApiVersion(1));

  pedidos.MapCrearPedidoEndpoint().RequireAuthorization(InventarioPermissions.PedidosAdministrar);
  pedidos.MapObtenerPedidoEndpoint().RequireAuthorization(InventarioPermissions.PedidosVer);
  ```

  Esto es intencional: cada módulo mapea todos sus endpoints en un único archivo agregador (ver
  `ModuleNameEndpointRouteBuilderExtensions.cs`), así que el template no puede -- ni debe -- generar ese
  archivo por sí solo cada vez que se agrega un feature.

## Qué NO genera este template

- Ningún `.csproj` ni carpeta de pruebas -- se asume que ya existen (son del módulo destino).
- Ninguna entidad de dominio -- para eso está `dotnet new bitcode-entity` (o escribila a mano).
- Ninguna prueba unitaria del handler -- copiá el patrón de
  `ModuleName.Tests/CrearElementoCommandHandlerTests.cs` (generado por `dotnet new bitcode-module`) contra
  el handler real una vez que reemplaces el `NotImplementedException`.

Ver `docs/convenciones.md` en la raíz del framework para las reglas duras (`Result`/`Error`,
`ToOkOrProblem`, `TransactionBehavior`, `IReadRepository` de solo lectura, etc.) que este scaffold ya sigue.
