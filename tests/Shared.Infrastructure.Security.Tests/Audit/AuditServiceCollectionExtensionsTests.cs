using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Audit;

public class AuditServiceCollectionExtensionsTests
{
    [Fact]
    public void AddSharedAuditing_RegistraInMemoryAuditWriterPorDefecto()
    {
        var services = new ServiceCollection();

        services.AddSharedAuditing();
        var provider = services.BuildServiceProvider();

        var writer = provider.GetRequiredService<IAuditWriter>();
        writer.Should().BeOfType<InMemoryAuditWriter>();
    }

    [Fact]
    public void AddSharedAuditing_EsSingleton_MismaInstanciaEntreScopes()
    {
        var services = new ServiceCollection();
        services.AddSharedAuditing();
        var provider = services.BuildServiceProvider();

        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();

        var writer1 = scope1.ServiceProvider.GetRequiredService<IAuditWriter>();
        var writer2 = scope2.ServiceProvider.GetRequiredService<IAuditWriter>();

        writer1.Should().BeSameAs(writer2);
    }

    [Fact]
    public void AddSharedAuditing_RegistraAuditIntegrityVerifierPorDefecto()
    {
        var services = new ServiceCollection();

        services.AddSharedAuditing();
        var provider = services.BuildServiceProvider();

        var verifier = provider.GetRequiredService<IAuditIntegrityVerifier>();
        verifier.Should().BeOfType<AuditIntegrityVerifier>();
    }

    [Fact]
    public void AddSharedAuditing_UnaImplementacionPropiaRegistradaDespues_GanaLaResolucion()
    {
        var services = new ServiceCollection();

        services.AddSharedAuditing();
        services.AddScoped<IAuditWriter, FakeAuditWriter>();

        var provider = services.BuildServiceProvider();
        var writer = provider.GetRequiredService<IAuditWriter>();

        writer.Should().BeOfType<FakeAuditWriter>();
    }

    private sealed class FakeAuditWriter : IAuditWriter
    {
        public Task<BitCode.Framework.Shared.Kernel.Result<AuditEntry>> WriteAsync(
            AuditEntryRequest request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }
}
