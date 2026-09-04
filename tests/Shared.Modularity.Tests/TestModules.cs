using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Modularity.Tests;

// Usados solo directamente con ModuleDependencyResolver.OrderByDependencies (no vía AddModules/
// escaneo de ensamblado), para no contaminar los tests de "happy path" que sí escanean un
// ensamblado completo (Shared.Modularity.Tests.HappyPathModules).
[DependsOn(typeof(ModuleB))]
public class ModuleA : IFrameworkModule
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
    }
}

[DependsOn(typeof(ModuleA))]
public class ModuleB : IFrameworkModule
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
    }
}

[DependsOn(typeof(ModuleNotInSet))]
public class ModuleWithMissingDependency : IFrameworkModule
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
    }
}

public class ModuleNotInSet : IFrameworkModule
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
    }
}
