using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Audit;

/// <summary>
/// F2-19: <see cref="RedactingAuditWriter"/> con un <see cref="IAuditWriter"/> real (<see
/// cref="InMemoryAuditWriter"/>) y una <see cref="AuditRedactionPolicy"/> real -- prueba de componente, no
/// de mocks, mismo criterio que el resto de la suite de la Épica F2-D. El foco de esta clase es el ORDEN DE
/// OPERACIONES respecto de F2-16 (<c>AuditHash</c>)/F2-17 (firma de lotes): la redacción debe ocurrir ANTES
/// de que <see cref="InMemoryAuditWriter"/> calcule <see cref="AuditEntry.AuditHash"/>, para que el hash
/// persistido corresponda siempre a los datos YA redactados y <see cref="IAuditIntegrityVerifier"/> nunca
/// reporte un falso <c>HashMismatch</c> sobre un registro cuyo único "cambio" fue la propia redacción.
/// </summary>
public class RedactingAuditWriterTests
{
    private static AuditEntryRequest CreateRequest(
        IReadOnlyDictionary<string, string?> metadata, string? reason = null, Guid? tenantId = null) =>
        new(
            actor: new AuditActor("user-1", AuditActorType.User),
            tenantId: tenantId ?? Guid.NewGuid(),
            action: "pedidos.crear",
            resource: new AuditResource("pedidos", "pedido-1"),
            outcome: AuditOutcome.Success,
            reason: reason,
            correlationId: "corr-1",
            traceId: "trace-1",
            ipAddress: "10.0.0.1",
            metadata: metadata);

    private static RedactingAuditWriter CreateSut(out InMemoryAuditWriter inner)
    {
        inner = new InMemoryAuditWriter();
        var policy = new AuditRedactionPolicy(Options.Create(new AuditRedactionOptions()));
        return new RedactingAuditWriter(inner, policy);
    }

    [Fact]
    public async Task WriteAsync_ClaveSensible_ElRegistroPersistidoContieneElValorRedactado_NoElOriginal()
    {
        var sut = CreateSut(out var inner);
        var request = CreateRequest(new Dictionary<string, string?> { ["password"] = "hunter2" });

        var result = await sut.WriteAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Value.Metadata["password"].Should().Be("[REDACTED]");
        inner.Entries.Should().ContainSingle().Which.Metadata["password"].Should().Be("[REDACTED]");
    }

    [Fact]
    public async Task WriteAsync_ValorNormal_SePersisteSinCambios()
    {
        var sut = CreateSut(out _);
        var request = CreateRequest(new Dictionary<string, string?> { ["canal"] = "web" });

        var result = await sut.WriteAsync(request);

        result.Value.Metadata["canal"].Should().Be("web");
    }

    [Fact]
    public async Task WriteAsync_AuditHashPersistido_CorrespondeAlContenidoYaRedactado_NoAlOriginal()
    {
        // Regresión del orden de operaciones: si InMemoryAuditWriter calculara el hash ANTES de la
        // redacción (o si la redacción se aplicara sobre un AuditEntry ya construido sin recalcular), el
        // AuditHash almacenado no coincidiría con AuditHashCalculator.Compute sobre el contenido
        // efectivamente persistido (ya redactado) -- exactamente la condición que
        // IAuditIntegrityVerifier.Verify reportaría como HashMismatch sobre un registro que nunca fue
        // manipulado por un tercero.
        var sut = CreateSut(out var inner);
        var request = CreateRequest(new Dictionary<string, string?> { ["password"] = "hunter2" });

        var result = await sut.WriteAsync(request);

        var persisted = result.Value;
        var recomputedHashOverPersistedContent = AuditHashCalculator.Compute(
            persisted.Id, persisted.OccurredAtUtc, persisted.Actor, persisted.TenantId, persisted.Action,
            persisted.Resource, persisted.Outcome, persisted.Reason, persisted.CorrelationId, persisted.TraceId,
            persisted.IpAddress, persisted.Metadata);

        persisted.AuditHash.Should().Be(recomputedHashOverPersistedContent);

        var recomputedHashOverOriginalContent = AuditHashCalculator.Compute(
            persisted.Id, persisted.OccurredAtUtc, persisted.Actor, persisted.TenantId, persisted.Action,
            persisted.Resource, persisted.Outcome, persisted.Reason, persisted.CorrelationId, persisted.TraceId,
            persisted.IpAddress, request.Metadata);

        persisted.AuditHash.Should().NotBe(recomputedHashOverOriginalContent,
            "el hash debe corresponder al contenido YA redactado, nunca al valor original sin redactar");
    }

    [Fact]
    public async Task WriteAsync_CadenaDeIntegridad_SigueSiendoValidaSobreRegistrosRedactados()
    {
        var sut = CreateSut(out var inner);
        var verifier = new AuditIntegrityVerifier();
        var tenantId = Guid.NewGuid();

        await sut.WriteAsync(CreateRequest(new Dictionary<string, string?> { ["password"] = "hunter2" }, tenantId: tenantId));
        await sut.WriteAsync(CreateRequest(new Dictionary<string, string?> { ["password"] = "otra-clave" }, tenantId: tenantId));

        var verification = verifier.Verify(inner.Entries);

        verification.IsValid.Should().BeTrue(
            "F2-16 debe seguir funcionando sin cambios sobre registros cuyo contenido ya fue redactado " +
            "antes de calcular el hash");
    }

    [Fact]
    public async Task WriteAsync_UnFalloDelWriterInterno_SePropagaSinAlterar()
    {
        var failingInner = new FailingAuditWriter();
        var policy = new AuditRedactionPolicy(Options.Create(new AuditRedactionOptions()));
        var sut = new RedactingAuditWriter(failingInner, policy);

        var result = await sut.WriteAsync(CreateRequest(new Dictionary<string, string?>()));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Fake.Error");
    }

    private sealed class FailingAuditWriter : IAuditWriter
    {
        public Task<BitCode.Framework.Shared.Kernel.Result<AuditEntry>> WriteAsync(
            AuditEntryRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(BitCode.Framework.Shared.Kernel.Result.Failure<AuditEntry>(
                BitCode.Framework.Shared.Kernel.Error.Failure("Fake.Error", "Fallo simulado")));
    }
}
