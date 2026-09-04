using BitCode.Framework.Shared.Modularity.Tests.HappyPathModules;
using BitCode.Framework.Shared.Modularity.Tests.NegativeCases;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Modularity.Tests;

public class ModularityServiceCollectionExtensionsTests
{
    [Fact]
    public void AddModules_DiscoversAndConfiguresAllModulesInAssembly()
    {
        ExecutionLog.Order.Clear();
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        services.AddModules(configuration, typeof(CoreModule).Assembly);

        ExecutionLog.Order.Should().BeEquivalentTo(
            [typeof(CoreModule), typeof(ProductosModule), typeof(PagosModule)]);
    }

    [Fact]
    public void AddModules_RespectsDependsOnOrder()
    {
        ExecutionLog.Order.Clear();
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        services.AddModules(configuration, typeof(CoreModule).Assembly);

        var coreIndex = ExecutionLog.Order.IndexOf(typeof(CoreModule));
        var productosIndex = ExecutionLog.Order.IndexOf(typeof(ProductosModule));
        var pagosIndex = ExecutionLog.Order.IndexOf(typeof(PagosModule));

        coreIndex.Should().BeLessThan(productosIndex, "ProductosModule depende de CoreModule");
        productosIndex.Should().BeLessThan(pagosIndex, "PagosModule depende de ProductosModule");
    }

    [Fact]
    public void AddModules_ActuallyRegistersServicesFromEachModule()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        services.AddModules(configuration, typeof(CoreModule).Assembly);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<string>().Should().Be("core-marker");
    }

    [Fact]
    public void AddModules_WithModuleLackingParameterlessConstructor_ThrowsClearError()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        var act = () => services.AddModules(
            configuration, typeof(ModuleWithoutParameterlessConstructor).Assembly);

        act.Should().Throw<InvalidOperationException>().WithMessage("*constructor público sin parámetros*");
    }
}
