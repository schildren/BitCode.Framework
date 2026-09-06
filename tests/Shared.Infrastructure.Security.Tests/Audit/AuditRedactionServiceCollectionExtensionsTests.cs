using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Audit;

public class AuditRedactionServiceCollectionExtensionsTests
{
    private static IConfiguration EmptyConfiguration() => new ConfigurationBuilder().Build();

    [Fact]
    public void AddSharedAuditRedaction_DecoraElIAuditWriterYaRegistrado()
    {
        var services = new ServiceCollection();
        services.AddSharedAuditing();

        services.AddSharedAuditRedaction(EmptyConfiguration());
        var provider = services.BuildServiceProvider();

        var writer = provider.GetRequiredService<IAuditWriter>();
        writer.Should().BeOfType<RedactingAuditWriter>();
    }

    [Fact]
    public void AddSharedAuditRedaction_SinIAuditWriterRegistradoAntes_Lanza()
    {
        var services = new ServiceCollection();

        var act = () => services.AddSharedAuditRedaction(EmptyConfiguration());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task AddSharedAuditRedaction_LlamadaDosVeces_NoVuelveADecorar()
    {
        // Regresión del Hallazgo 2 (revisión de arquitectura de F2-19): la comprobación de idempotencia
        // anterior (`d.ImplementationType == typeof(RedactingAuditWriter)`) nunca era verdadera, porque el
        // decorador se registra vía factory (ImplementationType siempre null para ese descriptor) -- una
        // segunda llamada SÍ volvía a decorar (RedactingAuditWriter sobre RedactingAuditWriter). El test
        // anterior no lo detectaba porque solo contaba descriptores de IAuditWriter después de `Replace`
        // (que siempre deja count=1, sin importar cuántas veces se llame). Este test verifica el
        // comportamiento REAL: con doble envoltorio, un valor sensible se redactaría dos veces, lo cual es
        // observable contando cuántas veces se invoca la política de redacción por escritura.
        var services = new ServiceCollection();
        services.AddSharedAuditing();

        services.AddSharedAuditRedaction(EmptyConfiguration());
        services.AddSharedAuditRedaction(EmptyConfiguration());

        services.RemoveAll<IAuditRedactionPolicy>();
        var countingPolicy = new CountingRedactionPolicy();
        services.AddSingleton<IAuditRedactionPolicy>(countingPolicy);

        var provider = services.BuildServiceProvider();
        var writer = provider.GetRequiredService<IAuditWriter>();

        await writer.WriteAsync(new AuditEntryRequest(
            actor: new AuditActor("user-1", AuditActorType.User),
            tenantId: Guid.NewGuid(),
            action: "pedidos.crear",
            resource: new AuditResource("pedidos", "pedido-1"),
            outcome: AuditOutcome.Success));

        countingPolicy.InvocationCount.Should().Be(1);
    }

    private sealed class CountingRedactionPolicy : IAuditRedactionPolicy
    {
        public int InvocationCount { get; private set; }

        public AuditEntryRequest Redact(AuditEntryRequest request)
        {
            InvocationCount++;
            return request;
        }
    }

    [Fact]
    public async Task AddAuditWriter_RegistradoDespuesDeAddSharedAuditRedaction_SigueRedactando()
    {
        // Regresión del Hallazgo 1 (revisión de arquitectura de F2-19, CRÍTICO): reproduce exactamente el
        // escenario de falla descrito -- AddSharedAuditing() -> AddSharedAuditRedaction(...) -> registrar
        // un IAuditWriter propio DESPUÉS. Con un `services.AddScoped<IAuditWriter, TWriter>()` manual, ese
        // registro ganaría la resolución y el RedactingAuditWriter quedaría huérfano sin ningún error
        // visible -- el writer real recibiría el AuditEntryRequest SIN REDACTAR. Usando AddAuditWriter<T>
        // (fix elegido), la redacción debe seguir aplicándose sin importar el orden.
        var services = new ServiceCollection();
        services.AddSharedAuditing();
        services.AddSharedAuditRedaction(EmptyConfiguration());

        // Registro del IAuditWriter "real" del proyecto consumidor, DESPUÉS de AddSharedAuditRedaction.
        services.AddAuditWriter<RecordingAuditWriter>();

        var provider = services.BuildServiceProvider();
        var writer = provider.GetRequiredService<IAuditWriter>();
        var recordingWriter = provider.GetRequiredService<RecordingAuditWriter>();

        var result = await writer.WriteAsync(new AuditEntryRequest(
            actor: new AuditActor("user-1", AuditActorType.User),
            tenantId: Guid.NewGuid(),
            action: "pedidos.crear",
            resource: new AuditResource("pedidos", "pedido-1"),
            outcome: AuditOutcome.Success,
            metadata: new Dictionary<string, string?> { ["password"] = "hunter2" }));

        result.IsSuccess.Should().BeTrue();
        result.Value.Metadata["password"].Should().Be("[REDACTED]");
        recordingWriter.LastReceivedRequest.Should().NotBeNull();
        recordingWriter.LastReceivedRequest!.Metadata["password"].Should().Be("[REDACTED]");
    }

    private sealed class RecordingAuditWriter : IAuditWriter
    {
        public AuditEntryRequest? LastReceivedRequest { get; private set; }

        public Task<Result<AuditEntry>> WriteAsync(AuditEntryRequest request, CancellationToken cancellationToken = default)
        {
            LastReceivedRequest = request;
            var id = Guid.NewGuid();
            var occurredAtUtc = DateTime.UtcNow;
            var auditHash = AuditHashCalculator.Compute(
                id, occurredAtUtc, request.Actor, request.TenantId, request.Action, request.Resource,
                request.Outcome, request.Reason, request.CorrelationId, request.TraceId, request.IpAddress,
                request.Metadata);

            var entry = new AuditEntry(
                id, occurredAtUtc, request.Actor, request.TenantId, request.Action, request.Resource,
                request.Outcome, request.Reason, request.CorrelationId, request.TraceId, request.IpAddress,
                request.Metadata, auditHash);

            return Task.FromResult(Result.Success(entry));
        }
    }

    [Fact]
    public async Task AddSharedAuditRedaction_ElWriterDecoradoRedactaAntesDeLlegarAlWriterOriginal()
    {
        var services = new ServiceCollection();
        services.AddSharedAuditing();
        services.AddSharedAuditRedaction(EmptyConfiguration());

        var provider = services.BuildServiceProvider();
        var writer = provider.GetRequiredService<IAuditWriter>();

        var result = await writer.WriteAsync(new AuditEntryRequest(
            actor: new AuditActor("user-1", AuditActorType.User),
            tenantId: Guid.NewGuid(),
            action: "pedidos.crear",
            resource: new AuditResource("pedidos", "pedido-1"),
            outcome: AuditOutcome.Success,
            metadata: new Dictionary<string, string?> { ["password"] = "hunter2" }));

        result.IsSuccess.Should().BeTrue();
        result.Value.Metadata["password"].Should().Be("[REDACTED]");
    }

    [Fact]
    public void AddSharedAuditRedaction_LeeLaConfiguracionDeClavesSensiblesAdicionales()
    {
        var services = new ServiceCollection();
        services.AddSharedAuditing();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AuditRedaction:SensitiveMetadataKeys:0"] = "codigoInterno",
            })
            .Build();
        services.AddSharedAuditRedaction(configuration);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AuditRedactionOptions>>().Value;

        options.SensitiveMetadataKeys.Should().Contain("codigoInterno");
    }

    [Fact]
    public void AddSharedAuditRedaction_UnaImplementacionPropiaDeIAuditRedactionPolicy_GanaLaResolucion()
    {
        var services = new ServiceCollection();
        services.AddSharedAuditing();
        services.AddSharedAuditRedaction(EmptyConfiguration());
        services.AddSingleton<IAuditRedactionPolicy, PassthroughRedactionPolicy>();

        var provider = services.BuildServiceProvider();
        var policy = provider.GetRequiredService<IAuditRedactionPolicy>();

        policy.Should().BeOfType<PassthroughRedactionPolicy>();
    }

    private sealed class PassthroughRedactionPolicy : IAuditRedactionPolicy
    {
        public AuditEntryRequest Redact(AuditEntryRequest request) => request;
    }
}
