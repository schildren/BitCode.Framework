using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using FluentAssertions;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Audit;

public class AuditIntegrityVerifierTests
{
    private readonly AuditIntegrityVerifier _sut = new();

    private static AuditEntryRequest CreateRequest(Guid tenantId, string action = "pedidos.crear") =>
        new(
            actor: new AuditActor("user-1", AuditActorType.User),
            tenantId: tenantId,
            action: action,
            resource: new AuditResource("pedidos", "pedido-42"),
            outcome: AuditOutcome.Success,
            metadata: new Dictionary<string, string?> { ["canal"] = "web" });

    private static AuditEntry Reconstruct(AuditEntry source, string? action = null, string? auditHash = null, string? previousAuditHash = null) =>
        new(
            source.Id,
            source.OccurredAtUtc,
            source.Actor,
            source.TenantId,
            action ?? source.Action,
            source.Resource,
            source.Outcome,
            source.Reason,
            source.CorrelationId,
            source.TraceId,
            source.IpAddress,
            source.Metadata,
            auditHash ?? source.AuditHash,
            previousAuditHash ?? source.PreviousAuditHash);

    [Fact]
    public void Verify_CadenaVacia_EsValida()
    {
        var result = _sut.Verify(Array.Empty<AuditEntry>());

        result.IsValid.Should().BeTrue();
        result.BrokenAtIndex.Should().BeNull();
    }

    [Fact]
    public async Task Verify_UnSoloRegistroGenesis_EsValido()
    {
        var writer = new InMemoryAuditWriter();
        var tenantId = Guid.NewGuid();
        var entry = (await writer.WriteAsync(CreateRequest(tenantId))).Value;

        var result = _sut.Verify([entry]);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Verify_CadenaEncadenadaSinAlterar_EsValida()
    {
        var writer = new InMemoryAuditWriter();
        var tenantId = Guid.NewGuid();
        var request = CreateRequest(tenantId);

        await writer.WriteAsync(request);
        await writer.WriteAsync(request);
        await writer.WriteAsync(request);

        var result = _sut.Verify(writer.Entries);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Verify_RegistroAlteradoDespuesDeEscrito_DetectaHashMismatch()
    {
        var writer = new InMemoryAuditWriter();
        var tenantId = Guid.NewGuid();
        var request = CreateRequest(tenantId);

        await writer.WriteAsync(request);
        await writer.WriteAsync(request);

        var entries = writer.Entries;
        // Simula manipulación: el segundo registro cambia de acción pero conserva su AuditHash original
        // (no recalculado) -- exactamente lo que un atacante que edite directamente el almacenamiento
        // subyacente, sin pasar por IAuditWriter, produciría.
        var tampered = new[] { entries[0], Reconstruct(entries[1], action: "pedidos.eliminar") };

        var result = _sut.Verify(tampered);

        result.IsValid.Should().BeFalse();
        result.BrokenAtIndex.Should().Be(1);
        result.BrokenEntryId.Should().Be(entries[1].Id);
        result.Reason.Should().Be(AuditIntegrityBreakReason.HashMismatch);
    }

    [Fact]
    public async Task Verify_RegistroEliminadoDeLaSecuencia_DetectaPreviousHashLinkMismatch()
    {
        var writer = new InMemoryAuditWriter();
        var tenantId = Guid.NewGuid();
        var request = CreateRequest(tenantId);

        await writer.WriteAsync(request);
        await writer.WriteAsync(request);
        await writer.WriteAsync(request);

        var entries = writer.Entries;
        // Simula la eliminación completa del segundo registro: la secuencia provista salta directamente
        // del primero al tercero, cuyo PreviousAuditHash sigue apuntando al (ahora ausente) segundo.
        var withGap = new[] { entries[0], entries[2] };

        var result = _sut.Verify(withGap);

        result.IsValid.Should().BeFalse();
        result.BrokenAtIndex.Should().Be(1);
        result.BrokenEntryId.Should().Be(entries[2].Id);
        result.Reason.Should().Be(AuditIntegrityBreakReason.PreviousHashLinkMismatch);
    }

    [Fact]
    public async Task Verify_SecuenciaReordenada_DetectaPreviousHashLinkMismatch()
    {
        var writer = new InMemoryAuditWriter();
        var tenantId = Guid.NewGuid();
        var request = CreateRequest(tenantId);

        await writer.WriteAsync(request);
        await writer.WriteAsync(request);

        var entries = writer.Entries;
        var reordered = new[] { entries[1], entries[0] };

        var result = _sut.Verify(reordered);

        result.IsValid.Should().BeFalse();
        result.BrokenAtIndex.Should().Be(0);
        result.Reason.Should().Be(AuditIntegrityBreakReason.PreviousHashLinkMismatch);
    }

    [Fact]
    public void Verify_ChainNull_LanzaArgumentNullException()
    {
        var act = () => _sut.Verify(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
