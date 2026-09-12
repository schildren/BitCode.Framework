using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Infrastructure.Security.Audit.Query;
using BitCode.Framework.Shared.Kernel;
using FluentAssertions;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Audit.Query;

/// <summary>
/// F2-20: <see cref="InMemoryAuditWriter.SearchAsync"/> -- implementación por defecto de <see
/// cref="IAuditReader"/>. Cubre filtros combinados y paginación en crudo, sin RBAC/auditoría de acceso
/// (eso es <see cref="AuditQueryServiceTests"/>).
/// </summary>
public class InMemoryAuditWriterSearchTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private static async Task<InMemoryAuditWriter> CreateWriterWithEntriesAsync()
    {
        var writer = new InMemoryAuditWriter();

        // TenantA, actor-1, pedidos.crear, Success
        await writer.WriteAsync(new AuditEntryRequest(
            new AuditActor("actor-1", AuditActorType.User), TenantA, "pedidos.crear",
            new AuditResource("pedidos", "1"), AuditOutcome.Success));

        // TenantA, actor-2, pedidos.eliminar, Denied
        await writer.WriteAsync(new AuditEntryRequest(
            new AuditActor("actor-2", AuditActorType.User), TenantA, "pedidos.eliminar",
            new AuditResource("pedidos", "2"), AuditOutcome.Denied, reason: "rbac:denied"));

        // TenantB, actor-1, pagos.aprobar, Success -- otro tenant, no debe filtrar junto con TenantA
        await writer.WriteAsync(new AuditEntryRequest(
            new AuditActor("actor-1", AuditActorType.User), TenantB, "pagos.aprobar",
            new AuditResource("pagos", "9"), AuditOutcome.Success));

        return writer;
    }

    private static PageRequest Page(int page = 1, int pageSize = 10) => PageRequest.Create(page, pageSize).Value;

    [Fact]
    public async Task SearchAsync_SinFiltros_DevuelveTodasLasEntradasOrdenadasPorMasRecientePrimero()
    {
        var writer = await CreateWriterWithEntriesAsync();

        var result = await writer.SearchAsync(new AuditSearchFilter(Page()));

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(3);
        result.Value.Items.Should().HaveCount(3);
        result.Value.Items.Select(e => e.OccurredAtUtc).Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task SearchAsync_FiltroPorTenant_DevuelveSoloEseTenant()
    {
        var writer = await CreateWriterWithEntriesAsync();

        var result = await writer.SearchAsync(new AuditSearchFilter(Page(), tenantId: TenantA));

        result.Value.TotalCount.Should().Be(2);
        result.Value.Items.Should().OnlyContain(e => e.TenantId == TenantA);
    }

    [Fact]
    public async Task SearchAsync_FiltrosCombinados_TenantYActorYOutcome_DevuelveSoloElSubconjuntoQueMatcheaTodos()
    {
        var writer = await CreateWriterWithEntriesAsync();

        var result = await writer.SearchAsync(new AuditSearchFilter(
            Page(), tenantId: TenantA, actorId: "actor-2", outcome: AuditOutcome.Denied));

        result.Value.TotalCount.Should().Be(1);
        result.Value.Items.Should().ContainSingle().Which.Action.Should().Be("pedidos.eliminar");
    }

    [Fact]
    public async Task SearchAsync_FiltroPorAccionYRecurso_DevuelveSoloElSubconjuntoCorrecto()
    {
        var writer = await CreateWriterWithEntriesAsync();

        var result = await writer.SearchAsync(new AuditSearchFilter(
            Page(), action: "pagos.aprobar", resourceType: "pagos"));

        result.Value.TotalCount.Should().Be(1);
        result.Value.Items.Should().ContainSingle().Which.TenantId.Should().Be(TenantB);
    }

    [Fact]
    public async Task SearchAsync_FiltroPorRangoDeFechasFueraDeRango_NoDevuelveNada()
    {
        var writer = await CreateWriterWithEntriesAsync();

        var result = await writer.SearchAsync(new AuditSearchFilter(
            Page(), fromUtc: DateTime.UtcNow.AddDays(1)));

        result.Value.TotalCount.Should().Be(0);
        result.Value.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_Paginacion_Pagina1_DevuelveLaCantidadPedidaYTotalCorrecto()
    {
        var writer = new InMemoryAuditWriter();
        for (var i = 0; i < 5; i++)
        {
            await writer.WriteAsync(new AuditEntryRequest(
                new AuditActor($"actor-{i}", AuditActorType.User), TenantA, "pedidos.consultar",
                new AuditResource("pedidos"), AuditOutcome.Success));
        }

        var result = await writer.SearchAsync(new AuditSearchFilter(Page(1, 2)));

        result.Value.TotalCount.Should().Be(5);
        result.Value.Items.Should().HaveCount(2);
        result.Value.Page.Should().Be(1);
        result.Value.PageSize.Should().Be(2);
        result.Value.TotalPages.Should().Be(3);
        result.Value.HasNextPage.Should().BeTrue();
        result.Value.HasPreviousPage.Should().BeFalse();
    }

    [Fact]
    public async Task SearchAsync_Paginacion_Pagina2_DevuelveElRestoSinSolaparConLaPagina1()
    {
        var writer = new InMemoryAuditWriter();
        for (var i = 0; i < 5; i++)
        {
            await writer.WriteAsync(new AuditEntryRequest(
                new AuditActor($"actor-{i}", AuditActorType.User), TenantA, "pedidos.consultar",
                new AuditResource("pedidos", i.ToString()), AuditOutcome.Success));
        }

        var page1 = await writer.SearchAsync(new AuditSearchFilter(Page(1, 2)));
        var page2 = await writer.SearchAsync(new AuditSearchFilter(Page(2, 2)));

        page2.Value.Items.Should().HaveCount(2);
        page2.Value.HasPreviousPage.Should().BeTrue();
        var page1Ids = page1.Value.Items.Select(e => e.Resource.Id).ToArray();
        var page2Ids = page2.Value.Items.Select(e => e.Resource.Id).ToArray();
        page2Ids.Should().NotIntersectWith(page1Ids);
    }

    [Fact]
    public async Task SearchAsync_NuncaDevuelveMasDeUnaPagina_SinImportarElTotalDeCoincidencias()
    {
        var writer = new InMemoryAuditWriter();
        for (var i = 0; i < 25; i++)
        {
            await writer.WriteAsync(new AuditEntryRequest(
                new AuditActor("actor-x", AuditActorType.User), TenantA, "pedidos.consultar",
                new AuditResource("pedidos"), AuditOutcome.Success));
        }

        var result = await writer.SearchAsync(new AuditSearchFilter(Page(1, 5)));

        result.Value.Items.Should().HaveCount(5, "una búsqueda controlada nunca debe devolver todo el histórico sin paginar");
        result.Value.TotalCount.Should().Be(25);
    }
}
