using BitCode.Framework.Shared.Infrastructure.Security.Audit.Worm;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Audit.Worm;

public class AuditWormExportServiceCollectionExtensionsTests
{
    private static IConfiguration EmptyConfiguration() => new ConfigurationBuilder().Build();

    [Fact]
    public void AddSharedAuditWormExport_RegistraInMemoryWormStoragePorDefecto()
    {
        var services = new ServiceCollection();

        services.AddSharedAuditWormExport(EmptyConfiguration());
        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IWormStorage>().Should().BeOfType<InMemoryWormStorage>();
    }

    [Fact]
    public void AddSharedAuditWormExport_RegistraAuditWormExportPipeline()
    {
        var services = new ServiceCollection();

        services.AddSharedAuditWormExport(EmptyConfiguration());
        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IAuditWormExportPipeline>().Should().BeOfType<AuditWormExportPipeline>();
    }

    [Fact]
    public void AddSharedAuditWormExport_EsSingleton_MismaInstanciaEntreScopes()
    {
        var services = new ServiceCollection();
        services.AddSharedAuditWormExport(EmptyConfiguration());
        var provider = services.BuildServiceProvider();

        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();

        scope1.ServiceProvider.GetRequiredService<IWormStorage>()
            .Should().BeSameAs(scope2.ServiceProvider.GetRequiredService<IWormStorage>());
    }

    [Fact]
    public void AddSharedAuditWormExport_UnaImplementacionPropiaRegistradaDespues_GanaLaResolucion()
    {
        var services = new ServiceCollection();

        services.AddSharedAuditWormExport(EmptyConfiguration());
        services.AddSingleton<IWormStorage, FakeWormStorage>();

        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IWormStorage>().Should().BeOfType<FakeWormStorage>();
    }

    [Fact]
    public void AddSharedAuditWormExport_LeeElRetentionPeriodConfigurado()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AuditWormExport:RetentionPeriod"] = "10.00:00:00",
            })
            .Build();

        services.AddSharedAuditWormExport(configuration);
        var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AuditWormExportOptions>>();
        options.Value.RetentionPeriod.Should().Be(TimeSpan.FromDays(10));
    }

    private sealed class FakeWormStorage : IWormStorage
    {
        public Task<BitCode.Framework.Shared.Kernel.Result<WormObjectMetadata>> WriteAsync(
            WormWriteRequest request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<BitCode.Framework.Shared.Kernel.Result<WormObject>> ReadAsync(
            string key, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<BitCode.Framework.Shared.Kernel.Result> DeleteAsync(
            string key, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }
}
