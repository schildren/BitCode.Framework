using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using FluentAssertions;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Audit;

public class InMemoryAuditWriterTests
{
    private static AuditEntryRequest CreateRequest(AuditOutcome outcome = AuditOutcome.Success, string? reason = null) =>
        new(
            actor: new AuditActor("user-1", AuditActorType.User),
            tenantId: Guid.NewGuid(),
            action: "pedidos.crear",
            resource: new AuditResource("pedidos", "pedido-42"),
            outcome: outcome,
            reason: reason,
            correlationId: "corr-1",
            traceId: "trace-1",
            ipAddress: "10.0.0.1",
            metadata: new Dictionary<string, string?> { ["canal"] = "web" });

    [Fact]
    public async Task WriteAsync_PersisteUnRegistroConTodosLosCamposCriticosCompletos()
    {
        var sut = new InMemoryAuditWriter();
        var request = CreateRequest();

        var result = await sut.WriteAsync(request);

        result.IsSuccess.Should().BeTrue();
        var entry = result.Value;

        entry.Id.Should().NotBeEmpty();
        entry.OccurredAtUtc.Should().NotBe(default);
        entry.OccurredAtUtc.Kind.Should().Be(DateTimeKind.Utc);
        entry.Actor.Id.Should().Be("user-1");
        entry.Actor.Type.Should().Be(AuditActorType.User);
        entry.TenantId.Should().Be(request.TenantId);
        entry.Action.Should().Be("pedidos.crear");
        entry.Resource.Type.Should().Be("pedidos");
        entry.Resource.Id.Should().Be("pedido-42");
        entry.Outcome.Should().Be(AuditOutcome.Success);
        entry.CorrelationId.Should().Be("corr-1");
        entry.TraceId.Should().Be("trace-1");
        entry.IpAddress.Should().Be("10.0.0.1");
        entry.Metadata.Should().ContainKey("canal");
        entry.AuditHash.Should().NotBeNullOrWhiteSpace();
        // F2-16 (todavía no implementada): la cadena de integridad no existe en F2-15, la columna queda
        // reservada en null hasta que el servicio de integridad de F2-16 la complete.
        entry.PreviousAuditHash.Should().BeNull();
    }

    [Fact]
    public async Task WriteAsync_ConDenegado_PersisteElMotivo()
    {
        var sut = new InMemoryAuditWriter();
        var request = CreateRequest(AuditOutcome.Denied, reason: "rbac:permiso-insuficiente");

        var result = await sut.WriteAsync(request);

        result.Value.Outcome.Should().Be(AuditOutcome.Denied);
        result.Value.Reason.Should().Be("rbac:permiso-insuficiente");
    }

    [Fact]
    public async Task WriteAsync_DosLlamadas_GeneranIdYHashDistintos()
    {
        var sut = new InMemoryAuditWriter();
        var request = CreateRequest();

        var first = await sut.WriteAsync(request);
        var second = await sut.WriteAsync(request);

        first.Value.Id.Should().NotBe(second.Value.Id);
        first.Value.AuditHash.Should().NotBe(second.Value.AuditHash);
    }

    [Fact]
    public async Task WriteAsync_AgregaLaEntradaALaColeccionDeSoloLectura()
    {
        var sut = new InMemoryAuditWriter();
        var result = await sut.WriteAsync(CreateRequest());

        sut.Entries.Should().ContainSingle().Which.Id.Should().Be(result.Value.Id);
    }

    [Fact]
    public async Task Entries_DevuelveUnaCopia_QueNoAfectaElEstadoInterno()
    {
        var sut = new InMemoryAuditWriter();
        await sut.WriteAsync(CreateRequest());

        var snapshot = (AuditEntry[])sut.Entries;
        Array.Clear(snapshot);

        // Mutar el array devuelto no debería afectar una segunda lectura -- Entries siempre devuelve una
        // copia nueva, nunca la colección interna del writer.
        sut.Entries.Should().ContainSingle();
    }
}
