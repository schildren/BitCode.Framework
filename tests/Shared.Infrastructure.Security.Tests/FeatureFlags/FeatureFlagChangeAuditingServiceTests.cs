using System.Collections.Concurrent;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Infrastructure.Security.FeatureFlags;
using BitCode.Framework.Shared.Kernel;
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

    /// <summary>
    /// Captura cada <see cref="AuditEntryRequest"/> escrito, exponiendo un mecanismo de espera asíncrono
    /// (en vez de <c>Task.Delay</c> fijo) para el escenario "fire-and-forget" de
    /// <see cref="FeatureFlagChangeAuditingService"/> (el callback <c>OnChange</c> de
    /// <see cref="IOptionsMonitor{TOptions}"/> es síncrono, así que la escritura de auditoría corre en un
    /// <see cref="Task"/> desatendido).
    /// </summary>
    private sealed class CapturingAuditWriter : IAuditWriter
    {
        private readonly ConcurrentQueue<AuditEntryRequest> _pending = new();
        private TaskCompletionSource<AuditEntryRequest>? _waiter;

        public ConcurrentQueue<AuditEntryRequest> WrittenRequests { get; } = new();

        public Task<Result<AuditEntry>> WriteAsync(AuditEntryRequest request, CancellationToken cancellationToken = default)
        {
            WrittenRequests.Enqueue(request);

            var waiter = Interlocked.Exchange(ref _waiter, null);
            if (waiter is not null)
            {
                waiter.TrySetResult(request);
            }
            else
            {
                _pending.Enqueue(request);
            }

            var entry = new AuditEntry(
                Guid.NewGuid(),
                DateTime.UtcNow,
                request.Actor,
                request.TenantId,
                request.Action,
                request.Resource,
                request.Outcome,
                request.Reason,
                request.CorrelationId,
                request.TraceId,
                request.IpAddress,
                request.Metadata,
                auditHash: "TEST-HASH");

            return Task.FromResult(Result<AuditEntry>.Success(entry));
        }

        public async Task<AuditEntryRequest> WaitForNextAsync(TimeSpan? timeout = null)
        {
            if (_pending.TryDequeue(out var already))
            {
                return already;
            }

            var tcs = new TaskCompletionSource<AuditEntryRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
            Interlocked.Exchange(ref _waiter, tcs);

            // Chequeo de última hora por si la escritura llegó entre el TryDequeue y el Exchange de arriba.
            if (_pending.TryDequeue(out var raceWinner))
            {
                tcs.TrySetResult(raceWinner);
            }

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeout ?? TimeSpan.FromSeconds(5)));
            if (completed != tcs.Task)
            {
                throw new TimeoutException("No se recibió ninguna escritura de auditoría dentro del tiempo esperado.");
            }

            return await tcs.Task;
        }
    }
}
