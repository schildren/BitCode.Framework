using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using FluentAssertions;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Audit;

public class AuditHashCalculatorTests
{
    private static readonly Guid FixedId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime FixedTimestamp = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static string ComputeSample(
        string action = "pedidos.crear",
        AuditOutcome outcome = AuditOutcome.Success,
        IReadOnlyDictionary<string, string?>? metadata = null) =>
        AuditHashCalculator.Compute(
            FixedId,
            FixedTimestamp,
            new AuditActor("user-1", AuditActorType.User),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            action,
            new AuditResource("pedidos", "pedido-42"),
            outcome,
            reason: null,
            correlationId: "corr-1",
            traceId: "trace-1",
            ipAddress: "10.0.0.1",
            metadata: metadata ?? new Dictionary<string, string?>());

    [Fact]
    public void Compute_EsDeterministico_ParaElMismoContenido()
    {
        var hash1 = ComputeSample();
        var hash2 = ComputeSample();

        hash1.Should().Be(hash2);
        hash1.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(nameof(AuditOutcome.Denied))]
    [InlineData(nameof(AuditOutcome.Error))]
    public void Compute_Cambia_SiCambiaElOutcome(string outcomeName)
    {
        var outcome = Enum.Parse<AuditOutcome>(outcomeName);

        var original = ComputeSample();
        var modified = ComputeSample(outcome: outcome);

        modified.Should().NotBe(original);
    }

    [Fact]
    public void Compute_Cambia_SiCambiaLaAccion()
    {
        var original = ComputeSample();
        var modified = ComputeSample(action: "pedidos.eliminar");

        modified.Should().NotBe(original);
    }

    [Fact]
    public void Compute_Cambia_SiCambiaUnValorDeMetadata()
    {
        var original = ComputeSample(metadata: new Dictionary<string, string?> { ["motivo"] = "a" });
        var modified = ComputeSample(metadata: new Dictionary<string, string?> { ["motivo"] = "b" });

        modified.Should().NotBe(original);
    }

    [Fact]
    public void Compute_EsIndependienteDelOrdenDeInsercionDeMetadata()
    {
        var metadataA = new Dictionary<string, string?> { ["z"] = "1", ["a"] = "2" };
        var metadataB = new Dictionary<string, string?> { ["a"] = "2", ["z"] = "1" };

        var hashA = ComputeSample(metadata: metadataA);
        var hashB = ComputeSample(metadata: metadataB);

        hashA.Should().Be(hashB);
    }
}
