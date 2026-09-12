using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Infrastructure.Security.Audit.Worm;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Audit.Worm;

/// <summary>
/// F2-18: <see cref="AuditWormExportPipeline"/> es la pieza que convierte la primitiva genérica <see
/// cref="IWormStorage"/> en el entregable "Pipeline de retención" -- serializa un lote de <see
/// cref="AuditEntry"/> (opcionalmente con su <see cref="AuditBatchSignature"/> de F2-17) y aplica la
/// retención configurada por defecto (o una explícita por lote). Estas pruebas cubren los 4 escenarios de
/// "Escritura y lectura probadas" a nivel del pipeline completo (serialización incluida), no solo de <see
/// cref="IWormStorage"/> (ver <see cref="InMemoryWormStorageTests"/> para la semántica WORM en crudo).
/// </summary>
public class AuditWormExportPipelineTests
{
    private static AuditWormExportPipeline CreatePipeline(IWormStorage? storage = null, TimeSpan? defaultRetention = null)
    {
        var options = new AuditWormExportOptions();
        if (defaultRetention.HasValue)
        {
            options.RetentionPeriod = defaultRetention.Value;
        }

        return new AuditWormExportPipeline(storage ?? new InMemoryWormStorage(), Options.Create(options));
    }

    private static IReadOnlyList<AuditEntry> CreateBatch(int count, Guid? tenantId = null)
    {
        var writer = new InMemoryAuditWriter();
        for (var i = 0; i < count; i++)
        {
            writer.WriteAsync(new AuditEntryRequest(
                    actor: new AuditActor($"user-{i}", AuditActorType.User),
                    tenantId: tenantId,
                    action: "pedidos.eliminar",
                    resource: new AuditResource("pedidos", $"pedido-{i}"),
                    outcome: AuditOutcome.Success,
                    reason: "motivo-de-prueba",
                    correlationId: $"corr-{i}",
                    traceId: $"trace-{i}",
                    ipAddress: "127.0.0.1",
                    metadata: new Dictionary<string, string?> { ["campo"] = $"valor-{i}" }))
                .GetAwaiter().GetResult();
        }

        return writer.Entries;
    }

    [Fact]
    public async Task ExportAsync_ThenReadAsync_ElLoteLeidoCoincideExactamenteConElExportado()
    {
        var tenantId = Guid.NewGuid();
        var pipeline = CreatePipeline();
        var batch = CreateBatch(3, tenantId);

        var exportResult = await pipeline.ExportAsync(new AuditWormExportRequest("tenant/lote-1", batch));
        exportResult.IsSuccess.Should().BeTrue();

        var readResult = await pipeline.ReadAsync("tenant/lote-1");

        readResult.IsSuccess.Should().BeTrue();
        readResult.Value.Signature.Should().BeNull();
        readResult.Value.Batch.Should().HaveCount(3);
        for (var i = 0; i < batch.Count; i++)
        {
            readResult.Value.Batch[i].Id.Should().Be(batch[i].Id);
            readResult.Value.Batch[i].AuditHash.Should().Be(batch[i].AuditHash);
            readResult.Value.Batch[i].PreviousAuditHash.Should().Be(batch[i].PreviousAuditHash);
            readResult.Value.Batch[i].TenantId.Should().Be(batch[i].TenantId);
            readResult.Value.Batch[i].Action.Should().Be(batch[i].Action);
            readResult.Value.Batch[i].Resource.Type.Should().Be(batch[i].Resource.Type);
            readResult.Value.Batch[i].Resource.Id.Should().Be(batch[i].Resource.Id);
            readResult.Value.Batch[i].Metadata.Should().BeEquivalentTo(batch[i].Metadata);
        }
    }

    [Fact]
    public async Task ExportAsync_ConFirmaDeF2_17_LaFirmaViajaJuntoConElLoteYSeLeeDeVuelta()
    {
        var pipeline = CreatePipeline();
        var batch = CreateBatch(2);
        var signature = new AuditBatchSignature("1", DateTime.UtcNow, "ZmlybWEtZGUtcHJ1ZWJh");

        await pipeline.ExportAsync(new AuditWormExportRequest("lote-firmado", batch, signature));
        var readResult = await pipeline.ReadAsync("lote-firmado");

        readResult.IsSuccess.Should().BeTrue();
        readResult.Value.Signature.Should().NotBeNull();
        readResult.Value.Signature!.ToString().Should().Be(signature.ToString());
    }

    [Fact]
    public async Task ExportAsync_ConLoteVacio_Falla_SinLlegarAlAlmacenamiento()
    {
        var pipeline = CreatePipeline();

        var result = await pipeline.ExportAsync(new AuditWormExportRequest("lote-vacio", Array.Empty<AuditEntry>()));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AuditWormExport.EmptyBatch");
    }

    [Fact]
    public async Task ExportAsync_SinRetencionExplicita_UsaElDefaultConfigurado()
    {
        var storage = new InMemoryWormStorage();
        var pipeline = CreatePipeline(storage, defaultRetention: TimeSpan.FromDays(400));
        var batch = CreateBatch(1);

        var result = await pipeline.ExportAsync(new AuditWormExportRequest("lote-default", batch));

        result.IsSuccess.Should().BeTrue();
        (result.Value.RetentionExpiresAtUtc - result.Value.WrittenAtUtc).Should().Be(TimeSpan.FromDays(400));
    }

    [Fact]
    public async Task ExportAsync_ConRetencionExplicitaPorLote_SobreescribeElDefault()
    {
        var storage = new InMemoryWormStorage();
        var pipeline = CreatePipeline(storage, defaultRetention: TimeSpan.FromDays(400));
        var batch = CreateBatch(1);

        var result = await pipeline.ExportAsync(
            new AuditWormExportRequest("lote-explicito", batch, retentionPeriod: TimeSpan.FromDays(10)));

        result.IsSuccess.Should().BeTrue();
        (result.Value.RetentionExpiresAtUtc - result.Value.WrittenAtUtc).Should().Be(TimeSpan.FromDays(10));
    }

    [Fact]
    public async Task ExportAsync_ConClaveYaExportada_PropagaElConflictoDeIWormStorage()
    {
        var pipeline = CreatePipeline();
        var batch = CreateBatch(1);
        await pipeline.ExportAsync(new AuditWormExportRequest("misma-clave", batch));

        var second = await pipeline.ExportAsync(new AuditWormExportRequest("misma-clave", CreateBatch(1)));

        second.IsFailure.Should().BeTrue();
        second.Error.Code.Should().Be("Worm.ObjectAlreadyExists");
    }

    [Fact]
    public async Task ReadAsync_ConClaveInexistente_Falla()
    {
        var pipeline = CreatePipeline();

        var result = await pipeline.ReadAsync("no-existe");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Worm.ObjectNotFound");
    }
}
