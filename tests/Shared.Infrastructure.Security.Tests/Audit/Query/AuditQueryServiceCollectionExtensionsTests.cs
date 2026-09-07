using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Infrastructure.Security.Audit.Query;
using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using BitCode.Framework.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Audit.Query;

public class AuditQueryServiceCollectionExtensionsTests
{
    [Fact]
    public void AddSharedAuditQuery_SinAddSharedAuditingPrevio_Lanza()
    {
        var services = new ServiceCollection();

        var act = () => services.AddSharedAuditQuery();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddSharedAuditQuery_RegistraIAuditReader_SobreLaMismaInstanciaSingletonQueIAuditWriter()
    {
        var services = new ServiceCollection();
        services.AddSharedAuditing();
        services.AddSharedAuditQuery();
        var provider = services.BuildServiceProvider();

        var writer = provider.GetRequiredService<IAuditWriter>();
        var reader = provider.GetRequiredService<IAuditReader>();

        reader.Should().BeSameAs(writer, "F2-20 debe buscar sobre el mismo almacenamiento en el que F2-15 escribe");
    }

    [Fact]
    public void AddSharedAuditQuery_RegistraIAuditQueryService()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ITenantContext>());
        services.AddSharedAuditing();
        services.AddSharedPermissionEvaluation();
        services.AddSharedAuditQuery();
        var provider = services.BuildServiceProvider();

        var service = provider.GetRequiredService<IAuditQueryService>();

        service.Should().BeOfType<AuditQueryService>();
    }

    [Fact]
    public void AddSharedAuditQuery_UnaImplementacionPropiaDeIAuditReaderRegistradaAntes_GanaLaResolucion()
    {
        var services = new ServiceCollection();
        services.AddSharedAuditing();
        var fakeReader = Substitute.For<IAuditReader>();
        services.AddSingleton(fakeReader);
        services.AddSharedAuditQuery();
        var provider = services.BuildServiceProvider();

        var reader = provider.GetRequiredService<IAuditReader>();

        reader.Should().BeSameAs(fakeReader);
    }

    [Fact]
    public void AddSharedAuditQuery_TrasAddAuditWriterConWriterRealSinIAuditReaderPropio_Lanza()
    {
        // Regresión del Hallazgo 1 (revisión de arquitectura de F2-20, CRÍTICO): reproduce exactamente el
        // escenario de falla descrito -- AddSharedAuditing() -> AddAuditWriter<TFakeSqlWriter>() (F2-19,
        // reemplaza IAuditWriter por un writer productivo distinto de InMemoryAuditWriter) ->
        // AddSharedAuditQuery() SIN que el proyecto registre su propio IAuditReader. Antes del fix, el guard
        // de arranque solo comprobaba que InMemoryAuditWriter siguiera registrado como TIPO CONCRETO (lo
        // cual sigue siendo cierto -- AddAuditWriter<T> no lo remueve), así que NUNCA lanzaba, y
        // TryAddSingleton resolvía IAuditReader en silencio sobre InMemoryAuditWriter, que en este escenario
        // nunca recibe ninguna escritura real (todo va a TFakeSqlWriter) -- toda búsqueda devolvería
        // siempre resultados vacíos. Con el fix, debe fallar explícitamente en el arranque.
        var services = new ServiceCollection();
        services.AddSharedAuditing();
        services.AddAuditWriter<FakeSqlAuditWriter>();

        var act = () => services.AddSharedAuditQuery();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task AddSharedAuditQuery_TrasAddAuditWriterConWriterRealYConIAuditReaderPropio_VeLasEscriturasRealesDelWriterReal()
    {
        // Contraparte del test anterior: si el proyecto SÍ registra su propio IAuditReader antes de
        // AddSharedAuditQuery, el arranque no debe fallar y el reader efectivamente ve las escrituras
        // reales del writer real (FakeSqlAuditWriter) -- nunca las de InMemoryAuditWriter (que en este
        // escenario nunca recibe ninguna escritura real, precisamente el hueco que motiva el fix).
        var services = new ServiceCollection();
        services.AddSharedAuditing();
        services.AddAuditWriter<FakeSqlAuditWriter>(ServiceLifetime.Singleton);
        services.AddSingleton<IAuditReader>(sp => new FakeSqlAuditReader(sp.GetRequiredService<FakeSqlAuditWriter>()));

        services.AddSharedAuditQuery();
        var provider = services.BuildServiceProvider();

        var writer = provider.GetRequiredService<IAuditWriter>();
        await writer.WriteAsync(new AuditEntryRequest(
            new AuditActor("actor-1", AuditActorType.User), null, "pedidos.crear", new AuditResource("pedidos"), AuditOutcome.Success));

        var reader = provider.GetRequiredService<IAuditReader>();
        var searchResult = await reader.SearchAsync(new AuditSearchFilter(PageRequest.Create(1, 10).Value));

        searchResult.IsSuccess.Should().BeTrue();
        searchResult.Value.Items.Should().ContainSingle().Which.Action.Should().Be("pedidos.crear");

        // El InMemoryAuditWriter subyacente (que AddSharedAuditQuery habría usado por defecto si no
        // hubiera un IAuditReader propio) nunca recibió esta escritura -- confirma que todo pasó por el
        // writer real, no por el placeholder en memoria.
        var inMemoryWriter = provider.GetRequiredService<InMemoryAuditWriter>();
        inMemoryWriter.Entries.Should().BeEmpty();
    }

    [Fact]
    public void AddAuditWriter_TrasAddSharedAuditQuerySinIAuditReaderPropio_ConWriterReal_Lanza()
    {
        // Mismo hueco de raíz que el test anterior, pero en el orden INVERSO: AddSharedAuditing() ->
        // AddSharedAuditQuery() (sin IAuditReader propio -- resuelve por defecto sobre InMemoryAuditWriter)
        // -> AddAuditWriter<TFakeSqlWriter>() DESPUÉS. El IAuditReader por defecto ya resuelto (TryAddSingleton
        // no se puede deshacer) quedaría apuntando a un almacenamiento que ya no recibe escrituras reales.
        var services = new ServiceCollection();
        services.AddSharedAuditing();
        services.AddSharedAuditQuery();

        var act = () => services.AddAuditWriter<FakeSqlAuditWriter>();

        act.Should().Throw<InvalidOperationException>();
    }

    private sealed class FakeSqlAuditWriter : IAuditWriter
    {
        public List<AuditEntry> Entries { get; } = [];

        public Task<Result<AuditEntry>> WriteAsync(AuditEntryRequest request, CancellationToken cancellationToken = default)
        {
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

            Entries.Add(entry);
            return Task.FromResult(Result.Success(entry));
        }
    }

    private sealed class FakeSqlAuditReader(FakeSqlAuditWriter writer) : IAuditReader
    {
        public Task<Result<PagedResult<AuditEntry>>> SearchAsync(AuditSearchFilter filter, CancellationToken cancellationToken = default)
        {
            var items = writer.Entries.ToArray();
            return Task.FromResult(Result.Success(new PagedResult<AuditEntry>(items, filter.Page.Page, filter.Page.PageSize, items.Length)));
        }
    }
}
