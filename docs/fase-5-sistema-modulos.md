# Fase 5 — Sistema de módulos

**Estado:** Completa
**Commits:** `8f4464c`, `d130ba4` (2 commits)
**Tests:** 8 nuevos (7 en `Shared.Modularity.Tests` repartidos en tres ensamblados + 1 en `Shared.Infrastructure.Web.Tests`)

## Objetivo

Formalizar, al estilo `IModule` de ASP.NET Boilerplate, la forma en que un **proyecto consumidor** organiza el registro de sus propias features — en vez de acumular todo en `Program.cs` a mano.

## Decisión de alcance (la más importante de esta fase)

El plan original preveía "un sistema de módulos formal con auto-registro de servicios", inspirado en que ABP envuelve *todo* (incluida su propia infraestructura) en módulos. Se evaluó envolver los `AddSharedX<T>()` ya construidos en las Fases 1-4 (`AddSharedPersistence<TContext>`, `AddSharedSecurity<TUser,TRole,TContext>`, etc.) en módulos autodescubribles, pero **se descartó**: esos métodos requieren parámetros genéricos específicos del proyecto consumidor (`TContext`, `TUser`, `TRole`) que no existen hasta que ese proyecto los define — no hay forma de autodescubrir "regístrame `AddSharedPersistence<MiAppDbContext>`" por reflexión sin que el consumidor ya lo haya escrito explícitamente en alguna parte.

El valor real de `IFrameworkModule` es distinto: darle al **consumidor** una forma de agrupar sus propias features (`ProductosModule`, `PagosModule`, `NotificacionesModule`...) en unidades cohesivas y autodescubiertas, cada una llamando internamente a los `AddSharedX<T>()` del framework con sus propios tipos concretos. Esto es exactamente lo que hace ABP con sus módulos de aplicación, aunque las piezas de infraestructura del framework mismo se sigan registrando con extension methods explícitos (patrón ya establecido y funcionando desde la Fase 1).

## Componentes por tarea

### Tarea 5.1 — `IFrameworkModule` + `[DependsOn]` (`Shared.Modularity`, nuevo proyecto)

```csharp
public interface IFrameworkModule
{
    void ConfigureServices(IServiceCollection services, IConfiguration configuration);
}
```

- `AddModules(configuration, params Assembly[] assemblies)`: descubre por reflexión toda clase concreta que implemente `IFrameworkModule`, la instancia (requiere constructor sin parámetros — error claro y temprano si no lo tiene) y llama a `ConfigureServices`.
- `[DependsOn(typeof(OtroModulo))]`: equivalente al `DependsOn` de ASP.NET Boilerplate. `ModuleDependencyResolver` ordena topológicamente (DFS) antes de invocar cualquier módulo, y lanza si detecta un ciclo o una dependencia fuera del conjunto de assemblies escaneados.

**Decisión de testing:** el escaneo por reflexión de `AddModules` opera sobre *todo* el assembly indicado — cualquier clase que implemente `IFrameworkModule` en ese assembly se recoge, sin distinguir "módulos de prueba buenos" de "módulos de prueba malos". Para poder probar tanto el camino feliz (cadena `Core→Productos→Pagos`) como los casos negativos (ciclo, constructor faltante, dependencia inexistente) sin que se contaminen entre sí, los tests se repartieron en tres ensamblados: `Shared.Modularity.Tests` (el resolver en aislamiento, sin pasar por `AddModules`), `Shared.Modularity.Tests.HappyPathModules` (la cadena real, escaneada de verdad) y `Shared.Modularity.Tests.NegativeCases` (el módulo sin constructor sin parámetros, escaneado por separado).

### Tarea 5.2 — Módulos que configuran el pipeline HTTP (`Shared.Infrastructure.Web`)

```csharp
public interface IWebFrameworkModule : IFrameworkModule
{
    void ConfigureApplication(WebApplication app);
}
```

Para que un módulo pueda tocar el pipeline HTTP (`app.Use...`, `app.Map...`) además de registrar servicios, `AddModules` ahora registra cada **instancia** de módulo en el contenedor (no solo invoca `ConfigureServices`), preservando el orden ya resuelto por `[DependsOn]`. `UseModules()` (llamado después de `Build()`, antes de `Run()`) resuelve esas instancias y llama `ConfigureApplication` sobre las que implementan `IWebFrameworkModule`, en el mismo orden.

Verificado con un test de extremo a extremo real (`WebApplication` + `UseTestServer()` + `AddModules` + `UseModules` + petición HTTP real a un endpoint mapeado por el módulo de prueba).

## Cómo usarlo desde un proyecto consumidor

```csharp
// ProductosModule.cs — el consumidor agrupa su propia feature
[DependsOn(typeof(InfraestructuraModule))]
public class ProductosModule : IWebFrameworkModule
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IProductoRepository, ProductoRepository>();
        // MediatR/FluentValidation de este feature ya los trae AddSharedApplication desde
        // InfraestructuraModule; aquí solo lo específico de Productos.
    }

    public void ConfigureApplication(WebApplication app)
    {
        app.MapProductosEndpoints(); // extension method con los Minimal API de este feature
    }
}

// InfraestructuraModule.cs — envuelve los AddSharedX<T>() del framework con los tipos del proyecto
public class InfraestructuraModule : IFrameworkModule
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSharedPersistence<MiAppDbContext>(configuration.GetConnectionString("Default")!);
        services.AddSharedApplication(typeof(InfraestructuraModule).Assembly);
        services.AddSharedExceptionHandling();
    }
}

// Program.cs
builder.Services.AddModules(builder.Configuration, typeof(Program).Assembly);
var app = builder.Build();
app.UseExceptionHandler();
app.UseModules();
app.Run();
```

## Cobertura de tests

| Área | Tests |
|---|---|
| `ModuleDependencyResolver` (ciclo, dependencia faltante, caso simple) | 3 |
| `AddModules` (descubrimiento, orden real, registro de servicios, error por constructor faltante) | 4 |
| `UseModules` (módulo web real ejecutado end-to-end) | 1 |
| **Total Fase 5** | **8** |

## Pendiente / fuera de alcance de esta fase

- Módulos "raíz" que el framework mismo provea (p.ej. un `PersistenceModule` genérico) — descartado explícitamente por el problema de los parámetros genéricos, ver decisión de alcance arriba.
- Habilitar/deshabilitar módulos condicionalmente por configuración (feature flags a nivel de módulo) — no forma parte del plan original, candidato a evaluar si surge la necesidad.
- Fase 6 del plan general (scaffolding: plantillas `dotnet new` o source generators para generar Command/Handler/Validator/Endpoint) — siguiente fase a desarrollar.
