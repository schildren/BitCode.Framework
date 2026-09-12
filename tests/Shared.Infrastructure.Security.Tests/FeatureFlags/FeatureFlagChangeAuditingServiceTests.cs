using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Infrastructure.Security.FeatureFlags;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.FeatureFlags;

public class FeatureFlagChangeAuditingServiceTests
{
    [Fact]
    public async Task RecargaDeConfiguracion_FlagCambiaDeValor_AuditaLaTransicionOldNew()
    {
        var (source, sut, auditWriter) = BuildSut(new Dictionary<string, string?>
        {
            ["FeatureFlags:NuevoFlujoDePagos"] = "false",
        });

        await sut.StartAsync(CancellationToken.None);
        try
        {
            source.Provider!.SetAndReload(new Dictionary<string, string?>
            {
                ["FeatureFlags:NuevoFlujoDePagos"] = "true",
            });

            var written = await auditWriter.WaitForNextAsync();

            written.Action.Should().Be(FeatureFlagChangeAuditingService.AuditAction);
            written.Resource.Type.Should().Be(FeatureFlagChangeAuditingService.AuditResourceType);
            written.Resource.Id.Should().Be("NuevoFlujoDePagos");
            written.Actor.Id.Should().Be(FeatureFlagChangeAuditingService.SystemActorId);
            written.Actor.Type.Should().Be(AuditActorType.System);
            written.Outcome.Should().Be(AuditOutcome.Success);
            written.Metadata["oldValue"].Should().Be("False");
            written.Metadata["newValue"].Should().Be("True");
        }
        finally
        {
            await sut.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RecargaDeConfiguracion_FlagNuevoAgregado_AuditaConOldValueNulo()
    {
        var (source, sut, auditWriter) = BuildSut(new Dictionary<string, string?>());

        await sut.StartAsync(CancellationToken.None);
        try
        {
            source.Provider!.SetAndReload(new Dictionary<string, string?>
            {
                ["FeatureFlags:NuevoFlujoDePagos"] = "true",
            });

            var written = await auditWriter.WaitForNextAsync();

            written.Metadata["oldValue"].Should().BeNull();
            written.Metadata["newValue"].Should().Be("True");
        }
        finally
        {
            await sut.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RecargaDeConfiguracion_SinCambiosEnLosFlags_NoEmiteAuditoria()
    {
        var (source, sut, auditWriter) = BuildSut(new Dictionary<string, string?>
        {
            ["FeatureFlags:NuevoFlujoDePagos"] = "true",
            ["Logging:LogLevel:Default"] = "Information",
        });

        await sut.StartAsync(CancellationToken.None);
        try
        {
            // Recarga la sección FeatureFlags con EL MISMO valor (solo cambia una clave fuera de esa
            // sección) -- no debe generar ninguna entrada de auditoría.
            source.Provider!.SetAndReload(new Dictionary<string, string?>
            {
                ["FeatureFlags:NuevoFlujoDePagos"] = "true",
                ["Logging:LogLevel:Default"] = "Warning",
            });

            // Da tiempo a que, si (incorrectamente) se emitiera una auditoría, el fire-and-forget la escriba.
            await Task.Delay(TimeSpan.FromMilliseconds(200));

            auditWriter.WrittenRequests.Should().BeEmpty();
        }
        finally
        {
            await sut.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public void ComputeChanges_MismoValorEnAmbosSnapshots_NoReportaCambio()
    {
        var previous = new Dictionary<string, bool> { ["A"] = true };
        var current = new Dictionary<string, bool> { ["A"] = true };

        var changes = FeatureFlagChangeAuditingService.ComputeChanges(previous, current);

        changes.Should().BeEmpty();
    }

    [Fact]
    public void ComputeChanges_FlagRemovido_ReportaCambioConNewValueNulo()
    {
        var previous = new Dictionary<string, bool> { ["A"] = true };
        var current = new Dictionary<string, bool>();

        var changes = FeatureFlagChangeAuditingService.ComputeChanges(previous, current);

        changes.Should().ContainSingle();
        var change = changes[0];
        change.FlagName.Should().Be("A");
        change.OldValue.Should().Be("True");
        change.NewValue.Should().BeNull();
    }

    private static (ReloadableConfigurationSource Source, FeatureFlagChangeAuditingService Sut, CapturingAuditWriter AuditWriter) BuildSut(
        Dictionary<string, string?> initialValues)
    {
        var source = new ReloadableConfigurationSource(initialValues);
        var configurationRoot = new ConfigurationBuilder().Add(source).Build();

        var services = new ServiceCollection();
        services.Configure<FeatureFlagsOptions>(configurationRoot.GetSection(FeatureFlagsOptions.SectionName));
        var serviceProvider = services.BuildServiceProvider();

        var auditWriter = new CapturingAuditWriter();
        var sut = new FeatureFlagChangeAuditingService(
            serviceProvider.GetRequiredService<IOptionsMonitor<FeatureFlagsOptions>>(),
            auditWriter,
            NullLogger<FeatureFlagChangeAuditingService>.Instance);

        return (source, sut, auditWriter);
    }
}
