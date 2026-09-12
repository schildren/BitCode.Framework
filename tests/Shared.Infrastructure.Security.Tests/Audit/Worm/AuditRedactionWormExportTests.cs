using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Infrastructure.Security.Audit.Worm;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Audit.Worm;

/// <summary>
/// F2-19 + F2-18: un lote escrito a través de <see cref="RedactingAuditWriter"/> y luego exportado a WORM
/// (<see cref="AuditWormExportPipeline"/>) debe contener, al leerlo de vuelta, los datos YA redactados --
/// nunca los originales. Prueba de componente de extremo a extremo (writer real + pipeline real + storage
/// real), no de mocks, para cerrar el criterio de aceptación "Logs sin PII no autorizada" también en el
/// destino de retención de largo plazo, no solo en el almacenamiento operacional.
/// </summary>
public class AuditRedactionWormExportTests
{
    [Fact]
    public async Task LoteExportadoAWorm_ContieneLosDatosRedactados_NoLosOriginales()
    {
        var innerWriter = new InMemoryAuditWriter();
        var redactionPolicy = new AuditRedactionPolicy(Options.Create(new AuditRedactionOptions()));
        var redactingWriter = new RedactingAuditWriter(innerWriter, redactionPolicy);

        await redactingWriter.WriteAsync(new AuditEntryRequest(
            actor: new AuditActor("user-1", AuditActorType.User),
            tenantId: Guid.NewGuid(),
            action: "pedidos.crear",
            resource: new AuditResource("pedidos", "pedido-1"),
            outcome: AuditOutcome.Success,
            metadata: new Dictionary<string, string?>
            {
                ["password"] = "hunter2",
                ["canal"] = "web",
                ["contacto"] = "usuario@ejemplo.com",
            }));

        var pipeline = new AuditWormExportPipeline(new InMemoryWormStorage(), Options.Create(new AuditWormExportOptions()));
        var exportResult = await pipeline.ExportAsync(new AuditWormExportRequest("lote-redactado", innerWriter.Entries));
        exportResult.IsSuccess.Should().BeTrue();

        var readResult = await pipeline.ReadAsync("lote-redactado");

        readResult.IsSuccess.Should().BeTrue();
        var exportedEntry = readResult.Value.Batch.Should().ContainSingle().Subject;
        exportedEntry.Metadata["password"].Should().Be("[REDACTED]");
        exportedEntry.Metadata["contacto"].Should().Be("[REDACTED]");
        exportedEntry.Metadata["canal"].Should().Be("web");

        // El hash leído de vuelta desde WORM tiene que seguir correspondiendo al contenido redactado --
        // confirma que la redacción ocurrió antes del cálculo de AuditHash (F2-16), no después de exportar.
        var recomputedHash = AuditHashCalculator.Compute(
            exportedEntry.Id, exportedEntry.OccurredAtUtc, exportedEntry.Actor, exportedEntry.TenantId,
            exportedEntry.Action, exportedEntry.Resource, exportedEntry.Outcome, exportedEntry.Reason,
            exportedEntry.CorrelationId, exportedEntry.TraceId, exportedEntry.IpAddress, exportedEntry.Metadata);
        exportedEntry.AuditHash.Should().Be(recomputedHash);
    }
}
