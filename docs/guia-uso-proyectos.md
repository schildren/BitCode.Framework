# Guía de uso — cómo consumir BitCode.Framework en un proyecto nuevo

Guía práctica, paso a paso, para arrancar un proyecto **consumidor** del framework desde cero. Para nomenclatura y reglas duras ya establecidas, ver [`convenciones.md`](convenciones.md); para el detalle de diseño de cada pieza, ver los documentos de fase en [`README.md`](README.md). El proyecto [`samples/Sample.Api`](../samples/Sample.Api) es la referencia viva de todo lo descrito aquí.

## 1. Referenciar el framework

Hoy se consume por `ProjectReference` directa (aún no hay paquetes NuGet publicados — ver [`fase-8-documentacion-adopcion.md`](fase-8-documentacion-adopcion.md)). En el `.csproj` del proyecto Web, referenciar solo los proyectos que el consumidor necesita:

```xml
<ItemGroup>
  <ProjectReference Include="..\BitCode.Framework\src\Shared.Infrastructure.Persistence\Shared.Infrastructure.Persistence.csproj" />
  <ProjectReference Include="..\BitCode.Framework\src\Shared.Application\Shared.Application.csproj" />
  <ProjectReference Include="..\BitCode.Framework\src\Shared.Infrastructure.Web\Shared.Infrastructure.Web.csproj" />
  <ProjectReference Include="..\BitCode.Framework\src\Shared.Modularity\Shared.Modularity.csproj" />
</ItemGroup>
```

Agregar además `Shared.Infrastructure.Security`, `.Observability`, `.BackgroundJobs` o `.Caching` según lo que el proyecto necesite (sección 5).

## 2. Crear el `DbContext`

Heredar de `MultiTenantDbContext` (o `MultiTenantIdentityDbContext<TUser, TRole>` si también se usa `Shared.Infrastructure.Security`). No hace falta configurar auditoría/soft-delete/tenant en `OnModelCreating`: el framework las detecta por reflexión si la entidad implementa la interfaz correspondiente.

```csharp
public class MiAppDbContext(DbContextOptions<MiAppDbContext> options, ITenantProvider tenantProvider)
    : MultiTenantDbContext(options, tenantProvider)
{
    public DbSet<MiEntidad> MiEntidades => Set<MiEntidad>();
}
```

## 3. Crear el `InfrastructureModule`

Agrupa todos los `AddSharedX<T>()` del framework para que el resto de los módulos de feature solo dependan de él (`[DependsOn(typeof(InfrastructureModule))]`) sin repetir configuración.

```csharp
public class InfrastructureModule : IWebFrameworkModule
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:Default en la configuración.");

        services.AddSharedPersistence<MiAppDbContext>(connectionString);
        services.AddSharedApplication(typeof(InfrastructureModule).Assembly);
        services.AddSharedExceptionHandling();
    }

    public void ConfigureApplication(WebApplication app)
    {
        app.UseExceptionHandler();
    }
}
```

`AddSharedPersistence<TContext>` registra `ITenantProvider`/`ICurrentUserProvider` con implementaciones no-op por defecto (`TryAdd`). Si el proyecto es multi-tenant o necesita el usuario autenticado en auditoría, registrar las implementaciones propias **antes** de llamar a `AddSharedPersistence`.

## 4. `Program.cs`

Con módulos, `Program.cs` no crece por feature — solo compone:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddModules(builder.Configuration, typeof(Program).Assembly);

var app = builder.Build();

app.UseModules();

app.Run();

public partial class Program;
```

`AddModules`/`UseModules` (Fase 5) descubren por reflexión todas las clases `IFrameworkModule`/`IWebFrameworkModule` del assembly, resuelven el orden por `[DependsOn]` y llaman `ConfigureServices`/`ConfigureApplication` en ese orden.

**Migraciones, no `EnsureCreated`:** `Sample.Api` usa `EnsureCreatedAsync()` porque es un piloto/demo. Un proyecto real debe usar EF Core Migrations:

```bash
dotnet ef migrations add InicialMiApp --project MiApp.Api
dotnet ef database update --project MiApp.Api
```

## 5. Agregar un feature (CQRS)

Un feature = una carpeta = un `IWebFrameworkModule` con `[DependsOn(typeof(InfrastructureModule))]`. Generar el scaffolding con las plantillas (Fase 6) en vez de copiar a mano:

```bash
dotnet new bitcode-entity -n MiEntidad --MultiTenant true
dotnet new bitcode-feature -n MiAccion
```

Estructura resultante (ver `samples/Sample.Api/Productos/` como referencia completa):

```
MiFeature/
  MiEntidad.cs              (Entity<Guid> + IAuditedEntity/ISoftDelete/ITenantEntity según aplique)
  Crear MiEntidadCommand.cs (Command + Validator + Handler)
  ObtenerMiEntidadQuery.cs  (Query + Handler)
  MiFeatureModule.cs        (IWebFrameworkModule: MapGroup + endpoints)
```

Reglas a respetar (detalle completo en [`convenciones.md`](convenciones.md)):

- Un `ICommand` handler nunca llama `SaveChangesAsync` — `TransactionBehavior` ya lo hace.
- Un `IQuery` handler inyecta `IReadRepository<,>`, nunca `IRepository<,>`, y nunca escribe.
- Un endpoint siempre resuelve el `Result` con `.ToOkOrProblem()`/`.ToProblemDetails()`.
- Un error de negocio esperado es `Result.Failure(Error.X(...))`, nunca una excepción.

## 6. Piezas opcionales (agregar solo si el proyecto las necesita)

| Necesito... | Referenciar | Registrar en `InfrastructureModule` | Configuración requerida |
|---|---|---|---|
| Login, JWT, permisos por rol | `Shared.Infrastructure.Security` | `services.AddSharedSecurity<MiUser, MiRole, MiAppDbContext>(configuration)` (requiere `MiAppDbContext : MultiTenantIdentityDbContext<MiUser, MiRole>`) | Sección `Jwt` con `SecretKey`/`Issuer`/`Audience` |
| Trazas y métricas | `Shared.Infrastructure.Observability` | `services.AddSharedObservability(configuration)` | Sección `OpenTelemetry` con `ServiceName` (`OtlpEndpoint` opcional) |
| Cachear resultados de queries | `Shared.Infrastructure.Caching` | `services.AddSharedCaching(configuration)` | Sección `Caching` con `RedisConnectionString` opcional (sin ella, solo L1 en memoria) |
| Jobs recurrentes | `Shared.Infrastructure.BackgroundJobs` | `services.AddSharedBackgroundJobs(q => q.AddJob<MiJob>(j => j.WithIdentity("mi-job")).AddTrigger(t => t.ForJob("mi-job").WithCronSchedule("0 0 * * * ?")))` | Ninguna adicional |
| Proteger un endpoint por permiso | (parte de `Shared.Infrastructure.Security`) | — | `.RequireAuthorization("entidad.accion")` en el endpoint |

Cachear con `HybridCache.GetOrCreateAsync`: recordar que la escritura a Redis L2 es asíncrona — no asumir consistencia inmediata entre instancias (Fase 4).

## 7. `appsettings.json` mínimo

```json
{
  "ConnectionStrings": {
    "Default": "Server=localhost,1433;Database=MiApp;User Id=sa;Password=<tu-password>;TrustServerCertificate=True;"
  },
  "Jwt": {
    "SecretKey": "<clave-larga-y-secreta>",
    "Issuer": "MiApp",
    "Audience": "MiApp"
  },
  "OpenTelemetry": {
    "ServiceName": "MiApp.Api",
    "OtlpEndpoint": ""
  },
  "Caching": {
    "RedisConnectionString": ""
  }
}
```

Incluir solo las secciones correspondientes a los `AddSharedX` que el proyecto registre — cada extension method valida la suya y lanza `InvalidOperationException` si falta.

## 8. Testing del proyecto consumidor

- Tests de integración (los que usan Docker: SQL Server, Redis) van en una carpeta `Integration/` y su namespace/clase debe contener `Integration` — de eso depende el filtro `FullyQualifiedName!~Integration` usado en CI y en desarrollo diario.
- Reutilizar `SqlServerContainerFixture`/`RedisContainerFixture` de `Shared.Testing` en vez de crear fixtures propios.
- Para probar endpoints end-to-end, seguir el patrón de `Sample.Api.Tests`: `WebApplicationFactory<Program>` + Testcontainers, inyectando el connection string de prueba vía variable de entorno (`ConnectionStrings__Default`) — `ConfigureAppConfiguration` no llega a tiempo porque `AddModules` lee la configuración de forma síncrona antes (ver [`fase-8-documentacion-adopcion.md`](fase-8-documentacion-adopcion.md)).

```bash
dotnet test --filter "FullyQualifiedName!~Integration"   # rápidos, sin Docker
dotnet test --filter "FullyQualifiedName~Integration"    # requieren Docker
```

## 9. Checklist de arranque

- [ ] Referenciar solo los proyectos `Shared.*` que se van a usar.
- [ ] `DbContext` hereda de `MultiTenantDbContext` (o `MultiTenantIdentityDbContext<,>`).
- [ ] `InfrastructureModule` agrupa los `AddSharedX<T>()`, con `ITenantProvider`/`ICurrentUserProvider` propios registrados antes si aplica.
- [ ] `Program.cs` solo tiene `AddModules`/`UseModules`.
- [ ] Migraciones EF Core creadas y aplicadas (no `EnsureCreated` en un proyecto real).
- [ ] Cada feature es una carpeta con su `IWebFrameworkModule` y `[DependsOn(typeof(InfrastructureModule))]`.
- [ ] `appsettings.json` tiene solo las secciones de los `AddSharedX` registrados.
- [ ] Tests de integración marcados y corriendo contra `Shared.Testing`.
