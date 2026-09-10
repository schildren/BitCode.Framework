# ModuleName

> **El nombre del módulo (`-n`) se usa directamente como prefijo de identificador C#**
> (`ModuleNameDbContext`, `ModuleNamePermissions`, `AddSharedModuleName()`, etc.) -- a diferencia de
> `dotnet new bitcode-app` (donde `AppName` solo nombra el proyecto), acá `-n` **no puede contener un
> punto** (`MiEmpresa.Inventario` generaría un identificador C# inválido). Usá `MiEmpresaInventario` o
> `Inventario` y, si querés que el namespace refleje tu organización, pasá `--Namespace
> MiEmpresa.Modules.Inventario` por separado.

Módulo (bounded context) generado por `dotnet new bitcode-module` (BitCode.Framework) -- mismo patrón que
`src/Platform/BitCode.Platform.*/` (ver `docs/convenciones.md`): un proyecto de biblioteca standalone,
dueño exclusivo de su propio esquema (`ModuleNameDbContext`), su propio catálogo de permisos
(`ModuleNamePermissions`) y su propia superficie pública (`ModuleNameServiceCollectionExtensions` /
`ModuleNameEndpointRouteBuilderExtensions`), instalable dentro de cualquier app host vía `ProjectReference`.

A diferencia de `dotnet new bitcode-app` (genera una aplicación completa ejecutable) o
`dotnet new bitcode-feature`/`dotnet new bitcode-entity` (generan una pieza suelta para pegar DENTRO de un
proyecto ya existente), este template genera un proyecto propio con límites explícitos: solo referencia
`Shared.*` (nunca otro módulo de negocio, ni `Platform.*` ni un módulo de otro bounded context -- ver el
comentario en `ModuleName.csproj`). Si dos módulos necesitan comunicarse, es a través de un evento de
integración (Outbox/Inbox, F1-24) o de la API HTTP pública del otro módulo, nunca por `ProjectReference`
directo.

## Contenido generado

- `ModuleNameDbContext.cs` -- `MultiTenantDbContext` propio del módulo (una tabla, `Elementos`).
- `Elementos/` -- agregado de ejemplo (`Elemento`, multi-tenant) + un comando (`CrearElementoCommand`) y
  dos queries (`ObtenerElementoQuery`, `ListarElementosQuery`) -- reemplazalo por tu dominio real, o usalo
  como plantilla copiando su estructura para el próximo agregado del módulo (`dotnet new bitcode-entity`/
  `dotnet new bitcode-feature` generan piezas sueltas equivalentes dentro de este mismo proyecto).
- `ModuleNamePermissions.cs` -- catálogo de permisos RBAC propio del módulo.
- `ModuleNameServiceCollectionExtensions.cs` / `ModuleNameEndpointRouteBuilderExtensions.cs` -- superficie
  pública que un host consume (`AddSharedModuleName()` / `MapModuleNameEndpoints()`).
- `HealthChecks/ModuleNameDbContextHealthCheck.cs` -- readiness check de `ModuleNameDbContext`.
- `ModuleName.Tests/` -- pruebas del propio módulo (ver más abajo).

## Cómo instalar este módulo en una app host

En el `InfrastructureModule`/`Program.cs` de la app host (ver `samples/Sample.Dashboard.Api/InfrastructureModule.cs`
como referencia real de este mismo patrón):

```csharp
services.AddSharedPersistence<ModuleNameDbContext>(connectionString);
services.AddSharedModuleName();

// Handlers/validators de este módulo viven en SU PROPIO ensamblado -- hay que agregarlo explícitamente,
// además del ensamblado del host.
services.AddSharedApplication(
    typeof(InfrastructureModule).Assembly,
    typeof(ModuleNameDbContext).Assembly);
```

Y en el pipeline HTTP:

```csharp
app.MapModuleNameEndpoints();
```

### RBAC

Los endpoints de escritura llaman `.RequireAuthorization(ModuleNamePermissions.ElementosAdministrar)`
(y los de lectura, `ModuleNamePermissions.ElementosVer`). Para que esos nombres de policy resuelvan en
runtime, el host debe registrarlos como policies de ASP.NET Core Authorization (ver
`docs/convenciones.md`, "Zero Trust por defecto" -- default deny) o integrarlos con el módulo de
`Shared.Infrastructure.Security` (RBAC/ABAC) que ya usa el resto del framework.

## Pruebas de este módulo ("módulo con límites y pruebas")

`ModuleName.Tests/` contiene:

- `CrearElementoCommandHandlerTests.cs` -- prueba unitaria del caso de uso (comando) contra un repositorio
  en memoria (`FakeElementoRepository`), sin infraestructura real.
- `ModuleBoundaryTests.cs` -- prueba de arquitectura/límites: verifica por reflexión que el ensamblado de
  este módulo NO referencia ningún otro módulo de negocio (`BitCode.Framework.Platform.*` ni ningún
  ensamblado fuera de la lista explícita de dependencias permitidas: `System.*`, `Microsoft.*`,
  `MediatR`, `FluentValidation`, `BitCode.Framework.Shared.*`). Si en algún momento alguien agrega, por
  accidente o por comodidad, un `ProjectReference` a otro módulo de negocio, esta prueba falla.

```bash
dotnet test ModuleName.Tests/ModuleName.Tests.csproj
```

Ver `docs/convenciones.md` en la raíz del framework para las reglas duras que este módulo ya sigue.
