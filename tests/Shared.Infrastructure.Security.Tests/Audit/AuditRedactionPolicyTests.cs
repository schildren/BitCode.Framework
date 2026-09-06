using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Audit;

/// <summary>
/// F2-19: pruebas de <see cref="AuditRedactionPolicy"/> aisladas de <see cref="IAuditWriter"/> --
/// clasificación por nombre de clave (principal) y detección de patrones de contenido (red de seguridad
/// adicional, best-effort). Ver <see cref="RedactingAuditWriterTests"/> para el efecto de esta política
/// combinado con la escritura real (orden de operaciones respecto de <c>AuditHash</c>).
/// </summary>
public class AuditRedactionPolicyTests
{
    private static AuditEntryRequest CreateRequest(
        IReadOnlyDictionary<string, string?>? metadata = null,
        string? reason = null) =>
        new(
            actor: new AuditActor("user-1", AuditActorType.User),
            tenantId: Guid.NewGuid(),
            action: "pedidos.crear",
            resource: new AuditResource("pedidos", "pedido-1"),
            outcome: AuditOutcome.Success,
            reason: reason,
            correlationId: "corr-1",
            traceId: "trace-1",
            ipAddress: "10.0.0.1",
            metadata: metadata ?? new Dictionary<string, string?>());

    private static AuditRedactionPolicy CreatePolicy(Action<AuditRedactionOptions>? configure = null)
    {
        var options = new AuditRedactionOptions();
        configure?.Invoke(options);
        return new AuditRedactionPolicy(Options.Create(options));
    }

    [Fact]
    public void Redact_ClaveDeclaradaSensible_RedactaElValorSinImportarSuContenido()
    {
        var sut = CreatePolicy();
        var request = CreateRequest(metadata: new Dictionary<string, string?> { ["password"] = "hunter2" });

        var redacted = sut.Redact(request);

        redacted.Metadata["password"].Should().Be("[REDACTED]");
    }

    [Fact]
    public void Redact_ClaveDeclaradaSensible_ComparacionEsCaseInsensitive()
    {
        var sut = CreatePolicy();
        var request = CreateRequest(metadata: new Dictionary<string, string?> { ["PassWord"] = "hunter2" });

        var redacted = sut.Redact(request);

        redacted.Metadata["PassWord"].Should().Be("[REDACTED]");
    }

    [Fact]
    public void Redact_ClaveNoSensibleConValorNormal_NoSeToca()
    {
        var sut = CreatePolicy();
        var request = CreateRequest(metadata: new Dictionary<string, string?> { ["canal"] = "web" });

        var redacted = sut.Redact(request);

        redacted.Metadata["canal"].Should().Be("web");
    }

    [Fact]
    public void Redact_ClaveNoDeclarada_PeroValorMatcheaPatronDeEmail_SeRedactaPorLaRedDeSeguridad()
    {
        var sut = CreatePolicy();
        var request = CreateRequest(
            metadata: new Dictionary<string, string?> { ["contacto"] = "usuario@ejemplo.com" });

        var redacted = sut.Redact(request);

        redacted.Metadata["contacto"].Should().Be("[REDACTED]");
    }

    [Fact]
    public void Redact_ClaveNoDeclarada_PeroValorMatcheaPatronDeTarjetaConSeparadores_SeRedacta()
    {
        var sut = CreatePolicy();
        var request = CreateRequest(
            metadata: new Dictionary<string, string?> { ["nota"] = "4111-1111-1111-1111" });

        var redacted = sut.Redact(request);

        redacted.Metadata["nota"].Should().Be("[REDACTED]");
    }

    [Fact]
    public void Redact_ClaveNoDeclarada_PeroValorMatcheaSecuenciaLargaDeDigitosSinSeparadores_SeRedacta()
    {
        var sut = CreatePolicy();
        var request = CreateRequest(
            metadata: new Dictionary<string, string?> { ["nota"] = "4111111111111111" });

        var redacted = sut.Redact(request);

        redacted.Metadata["nota"].Should().Be("[REDACTED]");
    }

    [Fact]
    public void Redact_ValorNormalSinNingunPatron_NoSeToca()
    {
        var sut = CreatePolicy();
        var request = CreateRequest(
            metadata: new Dictionary<string, string?> { ["nota"] = "pedido-42" });

        var redacted = sut.Redact(request);

        redacted.Metadata["nota"].Should().Be("pedido-42");
    }

    [Fact]
    public void Redact_ReasonConPatronDeEmail_SeRedacta()
    {
        var sut = CreatePolicy();
        var request = CreateRequest(reason: "rechazado: contactar a fulano@dominio.com para más detalle");

        var redacted = sut.Redact(request);

        redacted.Reason.Should().Be("[REDACTED]");
    }

    [Fact]
    public void Redact_ReasonSinNingunPatron_NoSeToca()
    {
        var sut = CreatePolicy();
        var request = CreateRequest(reason: "rbac:permiso-insuficiente");

        var redacted = sut.Redact(request);

        redacted.Reason.Should().Be("rbac:permiso-insuficiente");
    }

    [Fact]
    public void Redact_ConDeteccionDePatronesDeshabilitada_NoRedactaValoresDeClavesNoDeclaradas()
    {
        var sut = CreatePolicy(o => o.EnableContentPatternDetection = false);
        var request = CreateRequest(
            metadata: new Dictionary<string, string?> { ["contacto"] = "usuario@ejemplo.com" },
            reason: "contactar a otro@dominio.com");

        var redacted = sut.Redact(request);

        redacted.Metadata["contacto"].Should().Be("usuario@ejemplo.com");
        redacted.Reason.Should().Be("contactar a otro@dominio.com");
    }

    [Fact]
    public void Redact_ConDeteccionDePatronesDeshabilitada_SigueRedactandoClavesDeclaradasExplicitamente()
    {
        var sut = CreatePolicy(o => o.EnableContentPatternDetection = false);
        var request = CreateRequest(metadata: new Dictionary<string, string?> { ["password"] = "hunter2" });

        var redacted = sut.Redact(request);

        redacted.Metadata["password"].Should().Be("[REDACTED]");
    }

    [Fact]
    public void Redact_PlaceholderConfigurable_UsaElValorConfigurado()
    {
        var sut = CreatePolicy(o => o.RedactionPlaceholder = "***");
        var request = CreateRequest(metadata: new Dictionary<string, string?> { ["password"] = "hunter2" });

        var redacted = sut.Redact(request);

        redacted.Metadata["password"].Should().Be("***");
    }

    [Fact]
    public void Redact_ClaveSensibleConValorNull_NoLanzaYQuedaRedactado()
    {
        var sut = CreatePolicy();
        var request = CreateRequest(metadata: new Dictionary<string, string?> { ["password"] = null });

        var redacted = sut.Redact(request);

        redacted.Metadata["password"].Should().Be("[REDACTED]");
    }

    [Fact]
    public void Redact_ValorNullEnClaveNoSensible_SePreservaComoNull()
    {
        var sut = CreatePolicy();
        var request = CreateRequest(metadata: new Dictionary<string, string?> { ["opcional"] = null });

        var redacted = sut.Redact(request);

        redacted.Metadata["opcional"].Should().BeNull();
    }

    [Fact]
    public void Redact_NoMutaElRequestOriginal()
    {
        var sut = CreatePolicy();
        var originalMetadata = new Dictionary<string, string?> { ["password"] = "hunter2" };
        var request = CreateRequest(metadata: originalMetadata);

        sut.Redact(request);

        request.Metadata["password"].Should().Be("hunter2");
    }

    [Fact]
    public void Redact_PreservaLosDemasCamposDelRequestSinCambios()
    {
        var sut = CreatePolicy();
        var request = CreateRequest(metadata: new Dictionary<string, string?> { ["password"] = "hunter2" });

        var redacted = sut.Redact(request);

        redacted.Actor.Should().Be(request.Actor);
        redacted.TenantId.Should().Be(request.TenantId);
        redacted.Action.Should().Be(request.Action);
        redacted.Resource.Should().Be(request.Resource);
        redacted.Outcome.Should().Be(request.Outcome);
        redacted.CorrelationId.Should().Be(request.CorrelationId);
        redacted.TraceId.Should().Be(request.TraceId);
        redacted.IpAddress.Should().Be(request.IpAddress);
    }
}
