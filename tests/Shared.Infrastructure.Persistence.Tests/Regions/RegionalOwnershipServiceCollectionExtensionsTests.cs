using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy.Regions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.Tests.Regions;

/// <summary>F5-02: registro de servicios de ownership regional.</summary>
public class RegionalOwnershipServiceCollectionExtensionsTests
{
    [Fact]
    public void AddRegionalOwnership_RegistersDefaultInMemoryImplementations()
    {
        var services = new ServiceCollection();

        services.AddRegionalOwnership(RegionId.Primary);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ITenantRegionMapStore>().Should().BeOfType<InMemoryTenantRegionMapStore>();
        provider.GetRequiredService<IRegionalOwnershipResolver>().Should().BeOfType<TenantRegionOwnershipResolver>();
        provider.GetRequiredService<ICurrentRegionProvider>().CurrentRegion.Should().Be(RegionId.Primary);
    }

    [Fact]
    public void AddRegionalOwnership_DoesNotOverrideAlreadyRegisteredStoreOrResolver()
    {
        var services = new ServiceCollection();
        var customStore = new InMemoryTenantRegionMapStore();
        services.AddSingleton<ITenantRegionMapStore>(customStore);

        services.AddRegionalOwnership(new RegionId("eu-west"));
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ITenantRegionMapStore>().Should().BeSameAs(customStore);
    }

    [Fact]
    public void AddRegionalOwnership_CurrentRegionProvider_AlwaysReflectsLastCallArgument()
    {
        var services = new ServiceCollection();

        services.AddRegionalOwnership(new RegionId("eu-west"));
        services.AddRegionalOwnership(new RegionId("us-east"));
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ICurrentRegionProvider>().CurrentRegion.Should().Be(new RegionId("us-east"));
    }
}
