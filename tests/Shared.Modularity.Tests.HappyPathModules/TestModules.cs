using BitCode.Framework.Shared.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Modularity.Tests.HappyPathModules;

public static class ExecutionLog
{
    public static readonly List<Type> Order = [];
}

public class CoreModule : IFrameworkModule
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        ExecutionLog.Order.Add(GetType());
        services.AddSingleton("core-marker");
    }
}

[DependsOn(typeof(CoreModule))]
public class ProductosModule : IFrameworkModule
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        ExecutionLog.Order.Add(GetType());
    }
}

[DependsOn(typeof(ProductosModule))]
public class PagosModule : IFrameworkModule
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        ExecutionLog.Order.Add(GetType());
    }
}
