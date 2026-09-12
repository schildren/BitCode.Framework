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
        // F2-16: el primer registro escrito para un tenant es el "génesis" de esa cadena -- ningún
        // registro anterior con el que encadenar todavía.
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

    [Fact]
    public async Task WriteAsync_DosEscriturasDelMismoTenant_EncadenanCorrectamente()
    {
        var sut = new InMemoryAuditWriter();
        var request = CreateRequest();

        var first = await sut.WriteAsync(request);
        var second = await sut.WriteAsync(request);

        first.Value.PreviousAuditHash.Should().BeNull();
        second.Value.PreviousAuditHash.Should().Be(first.Value.AuditHash);
    }

    [Fact]
    public async Task WriteAsync_TenantsDistintos_NoComparteCadena()
    {
        var sut = new InMemoryAuditWriter();
        var requestTenantA = CreateRequest();
        var requestTenantB = CreateRequest();

        var firstA = await sut.WriteAsync(requestTenantA);
        var firstB = await sut.WriteAsync(requestTenantB);
        var secondA = await sut.WriteAsync(requestTenantA);

        // requestTenantA/requestTenantB tienen cada uno su propio TenantId (Guid.NewGuid() en CreateRequest)
        // -- cada uno es el génesis de su propia cadena, independiente de la escritura del otro tenant en
        // el medio.
        firstA.Value.PreviousAuditHash.Should().BeNull();
        firstB.Value.PreviousAuditHash.Should().BeNull();
        secondA.Value.PreviousAuditHash.Should().Be(firstA.Value.AuditHash);
    }

    [Fact]
    public async Task WriteAsync_TenantNull_TieneSuPropiaCadenaIndependienteDeTenantsConcretos()
    {
        var sut = new InMemoryAuditWriter();
        var baseRequest = CreateRequest();
        var platformRequest = new AuditEntryRequest(
            baseRequest.Actor, tenantId: null, baseRequest.Action, baseRequest.Resource, baseRequest.Outcome,
            baseRequest.Reason, baseRequest.CorrelationId, baseRequest.TraceId, baseRequest.IpAddress,
            baseRequest.Metadata);

        var tenantEntry = await sut.WriteAsync(baseRequest);
        var firstPlatform = await sut.WriteAsync(platformRequest);
        var secondPlatform = await sut.WriteAsync(platformRequest);

        firstPlatform.Value.PreviousAuditHash.Should().BeNull();
        secondPlatform.Value.PreviousAuditHash.Should().Be(firstPlatform.Value.AuditHash);
        tenantEntry.Value.PreviousAuditHash.Should().BeNull();
    }

    [Fact]
    public async Task WriteAsync_EscriturasConcurrentesDelMismoTenant_MantienenLaCadenaConsistente()
    {
        // Regresión: el enlace PreviousAuditHash (determinado leyendo el último hash de la cadena) y el
        // Enqueue que hace visible la entrada en Entries deben ejecutarse como una única sección atómica
        // por cadena -- si no lo fueran, dos escrituras concurrentes del mismo tenant podrían completar
        // esas dos operaciones en órdenes relativos distintos entre sí y dejar Entries en un orden que no
        // coincide con el orden lógico del enlace, lo que IAuditIntegrityVerifier.Verify reportaría como un
        // falso positivo de PreviousHashLinkMismatch sobre escrituras completamente legítimas.
        var sut = new InMemoryAuditWriter();
        var verifier = new AuditIntegrityVerifier();
        var request = CreateRequest();
        const int concurrentWrites = 50;

        var tasks = Enumerable.Range(0, concurrentWrites)
            .Select(_ => sut.WriteAsync(request))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());
        sut.Entries.Should().HaveCount(concurrentWrites);

        var verification = verifier.Verify(sut.Entries);

        verification.IsValid.Should().BeTrue(
            "el orden de aparición en Entries debe coincidir siempre con el orden lógico del enlace " +
            "PreviousAuditHash, incluso bajo escrituras concurrentes del mismo tenant");
    }
}
