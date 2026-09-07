using System.Security.Claims;
using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Infrastructure.Security.Audit.Query;
using BitCode.Framework.Shared.Infrastructure.Security.Audit.Worm;
using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using BitCode.Framework.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Audit.Query;

/// <summary>
/// Prueba de componente (F2-20): ejercita <see cref="IAuditQueryService"/> resuelto vía contenedor de DI
/// real -- <see cref="PermissionEvaluator"/> (F2-07), <see cref="InMemoryAuditWriter"/> (F2-15) e
/// <see cref="AuditWormExportPipeline"/> (F2-18) reales, sin mockear ninguna de esas piezas. Solo se
/// sustituyen <see cref="IPermissionService"/> y <see cref="ITenantContext"/> (mismo criterio que
/// <c>PrivilegedOperationsEndToEndTests</c>). Cubre el criterio de aceptación literal de F2-20 ("Acceso
/// auditado y paginado"): filtros combinados, paginación, denegación fail-closed sin el permiso RBAC
/// dedicado, cada acceso (concedido o denegado) genera su propia entrada de auditoría, y la exportación
/// reutiliza el pipeline WORM ya existente.
/// </summary>
public class AuditQueryServiceTests
{
    private static IServiceProvider BuildProvider(
        string[] userPermissions, bool multiTenancyEnabled = false, Guid? tenantId = null, bool withWormExport = true)
    {
        var services = new ServiceCollection();

        var permissionService = Substitute.For<IPermissionService>();
        permissionService.GetPermissionsForUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(userPermissions);
        services.AddSingleton(permissionService);

        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.IsMultiTenancyEnabled.Returns(multiTenancyEnabled);
        tenantContext.TenantId.Returns(tenantId);
        services.AddSingleton(tenantContext);

        services.AddSharedAuditing();
        services.AddSharedPermissionEvaluation();
        services.AddSharedAuditQuery();

        if (withWormExport)
        {
            services.AddSharedAuditWormExport(new ConfigurationBuilder().Build());
        }

        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal CreateUser(string userId) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "Test"));

    private static PageRequest Page(int page = 1, int pageSize = 10) => PageRequest.Create(page, pageSize).Value;

    private static async Task SeedAsync(IAuditWriter writer, Guid? tenantId, string actorId, string action, AuditOutcome outcome)
    {
        await writer.WriteAsync(new AuditEntryRequest(
            new AuditActor(actorId, AuditActorType.User), tenantId, action, new AuditResource("pedidos"), outcome));
    }

    [Fact]
    public async Task SearchAsync_ConPermiso_DevuelveElSubconjuntoQueMatcheaLosFiltrosCombinados()
    {
        var provider = BuildProvider(["auditoria.consultar"]);
        var auditWriter = provider.GetRequiredService<IAuditWriter>();
        await SeedAsync(auditWriter, null, "actor-1", "pedidos.crear", AuditOutcome.Success);
        await SeedAsync(auditWriter, null, "actor-2", "pedidos.eliminar", AuditOutcome.Denied);
        var sut = provider.GetRequiredService<IAuditQueryService>();
        var user = CreateUser(Guid.NewGuid().ToString());

        var result = await sut.SearchAsync(user, new AuditSearchFilter(Page(), actorId: "actor-2", outcome: AuditOutcome.Denied));

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(1);
        result.Value.Items.Should().ContainSingle().Which.Action.Should().Be("pedidos.eliminar");
    }

    [Fact]
    public async Task SearchAsync_ConPermiso_Paginacion_Pagina1YPagina2_NoSeSolapan()
    {
        var provider = BuildProvider(["auditoria.consultar"]);
        var auditWriter = provider.GetRequiredService<IAuditWriter>();
        for (var i = 0; i < 5; i++)
        {
            await SeedAsync(auditWriter, null, $"actor-{i}", "pedidos.consultar", AuditOutcome.Success);
        }

        var sut = provider.GetRequiredService<IAuditQueryService>();
        var user = CreateUser(Guid.NewGuid().ToString());

        // resourceType: "pedidos" excluye deliberadamente las propias entradas de acceso ("auditoria",
        // ver AuditQueryService) que cada llamada a SearchAsync genera -- sin este filtro, la entrada de
        // acceso escrita por la búsqueda de la página 1 se colaría en el conjunto que ordena/pagina la
        // búsqueda de la página 2, desplazando el corte de página 2 en uno.
        var page1 = await sut.SearchAsync(user, new AuditSearchFilter(Page(1, 2), resourceType: "pedidos"));
        var page2 = await sut.SearchAsync(user, new AuditSearchFilter(Page(2, 2), resourceType: "pedidos"));

        page1.Value.Items.Should().HaveCount(2);
        page2.Value.Items.Should().HaveCount(2);
        page1.Value.Items.Select(e => e.Actor.Id).Should().NotIntersectWith(page2.Value.Items.Select(e => e.Actor.Id));
    }

    [Fact]
    public async Task SearchAsync_SinElPermisoRequerido_EsDenegada_FailClosed()
    {
        var provider = BuildProvider([]);
        var sut = provider.GetRequiredService<IAuditQueryService>();
        var user = CreateUser(Guid.NewGuid().ToString());

        var result = await sut.SearchAsync(user, new AuditSearchFilter(Page()));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
    }

    [Fact]
    public async Task SearchAsync_Exitosa_GeneraSuPropiaEntradaDeAuditoriaDeAcceso()
    {
        var provider = BuildProvider(["auditoria.consultar"]);
        var auditWriter = (InMemoryAuditWriter)provider.GetRequiredService<IAuditWriter>();
        var sut = provider.GetRequiredService<IAuditQueryService>();
        var userId = Guid.NewGuid().ToString();
        var user = CreateUser(userId);

        await sut.SearchAsync(user, new AuditSearchFilter(Page()));

        var accessEntries = auditWriter.Entries.Where(e => e.Resource.Type == "auditoria").ToArray();
        accessEntries.Should().ContainSingle();
        accessEntries[0].Actor.Id.Should().Be(userId);
        accessEntries[0].Action.Should().Be("auditoria.consultar");
        accessEntries[0].Outcome.Should().Be(AuditOutcome.Success);
    }

    [Fact]
    public async Task SearchAsync_Denegada_GeneraSuPropiaEntradaDeAuditoriaDeAccesoDenegado()
    {
        var provider = BuildProvider([]);
        var auditWriter = (InMemoryAuditWriter)provider.GetRequiredService<IAuditWriter>();
        var sut = provider.GetRequiredService<IAuditQueryService>();
        var userId = Guid.NewGuid().ToString();
        var user = CreateUser(userId);

        await sut.SearchAsync(user, new AuditSearchFilter(Page()));

        var accessEntries = auditWriter.Entries.Where(e => e.Resource.Type == "auditoria").ToArray();
        accessEntries.Should().ContainSingle();
        accessEntries[0].Outcome.Should().Be(AuditOutcome.Denied);
        accessEntries[0].Reason.Should().Contain("auditoria.consultar");
    }

    [Fact]
    public async Task SearchAsync_LaPropiaEntradaDeAuditoriaDeAcceso_NoDisparaUnaNuevaConsulta()
    {
        // Regresión de recursión (documentado en AuditQueryService): la entrada de acceso se escribe
        // directamente vía IAuditWriter, nunca a través de SearchAsync -- por cada llamada del test debe
        // haber EXACTAMENTE una entrada de acceso, nunca una cascada.
        var provider = BuildProvider(["auditoria.consultar"]);
        var auditWriter = (InMemoryAuditWriter)provider.GetRequiredService<IAuditWriter>();
        var sut = provider.GetRequiredService<IAuditQueryService>();
        var user = CreateUser(Guid.NewGuid().ToString());

        await sut.SearchAsync(user, new AuditSearchFilter(Page()));
        await sut.SearchAsync(user, new AuditSearchFilter(Page()));

        auditWriter.Entries.Count(e => e.Resource.Type == "auditoria").Should().Be(2);
    }

    [Fact]
    public async Task SearchAsync_MultiTenancyHabilitada_FiltroConTenantDistintoDelLlamador_EsDenegada()
    {
        var tenantDelLlamador = Guid.NewGuid();
        var otroTenant = Guid.NewGuid();
        var provider = BuildProvider(["auditoria.consultar"], multiTenancyEnabled: true, tenantId: tenantDelLlamador);
        var sut = provider.GetRequiredService<IAuditQueryService>();
        var user = CreateUser(Guid.NewGuid().ToString());

        var result = await sut.SearchAsync(user, new AuditSearchFilter(Page(), tenantId: otroTenant));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
    }

    [Fact]
    public async Task SearchAsync_MultiTenancyHabilitada_FiltroConElMismoTenantDelLlamador_EsPermitida()
    {
        var tenantDelLlamador = Guid.NewGuid();
        var provider = BuildProvider(["auditoria.consultar"], multiTenancyEnabled: true, tenantId: tenantDelLlamador);
        var sut = provider.GetRequiredService<IAuditQueryService>();
        var user = CreateUser(Guid.NewGuid().ToString());

        var result = await sut.SearchAsync(user, new AuditSearchFilter(Page(), tenantId: tenantDelLlamador));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task SearchAsync_MultiTenancyHabilitada_LlamadorSinTenantResuelto_FiltroSinTenant_EsDenegada_FailClosed()
    {
        // Regresión del Hallazgo 2 (revisión de arquitectura de F2-20, Alto): tenantContext.TenantId es
        // Guid? y puede ser null incluso con IsMultiTenancyEnabled == true (un job en background sin
        // HttpContext, o una cuenta de servicio sin claim de tenant -- ver HttpContextTenantProvider). Antes
        // del fix, "filter.TenantId != tenantContext.TenantId" con AMBOS null evaluaba a false (sin
        // mismatch) y la búsqueda se permitía SIN NINGUNA restricción de tenant (cross-tenant) con solo el
        // permiso base "auditoria.consultar". Con el fix, debe denegarse fail-closed sin el permiso
        // adicional CrossTenantPermission.
        var provider = BuildProvider(["auditoria.consultar"], multiTenancyEnabled: true, tenantId: null);
        var sut = provider.GetRequiredService<IAuditQueryService>();
        var user = CreateUser(Guid.NewGuid().ToString());

        var result = await sut.SearchAsync(user, new AuditSearchFilter(Page(), tenantId: null));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
    }

    [Fact]
    public async Task SearchAsync_MultiTenancyHabilitada_LlamadorSinTenantResuelto_ConPermisoCrossTenant_EsPermitida()
    {
        // Contraparte del test anterior: el caso de uso legítimo (job/cuenta de servicio de plataforma que
        // necesita ver auditoría cross-tenant deliberadamente) SÍ debe poder operar, pero únicamente con el
        // permiso adicional, más restrictivo, que el permiso base no otorga por sí solo.
        var provider = BuildProvider(
            ["auditoria.consultar", AuditQueryService.CrossTenantPermission], multiTenancyEnabled: true, tenantId: null);
        var sut = provider.GetRequiredService<IAuditQueryService>();
        var user = CreateUser(Guid.NewGuid().ToString());

        var result = await sut.SearchAsync(user, new AuditSearchFilter(Page(), tenantId: null));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ExportAsync_ConPermiso_ExportaElResultadoFiltradoAlPipelineWormExistente()
    {
        var provider = BuildProvider(["auditoria.consultar"]);
        var auditWriter = provider.GetRequiredService<IAuditWriter>();
        await SeedAsync(auditWriter, null, "actor-1", "pedidos.crear", AuditOutcome.Success);
        var sut = provider.GetRequiredService<IAuditQueryService>();
        var wormPipeline = provider.GetRequiredService<IAuditWormExportPipeline>();
        var user = CreateUser(Guid.NewGuid().ToString());

        var exportResult = await sut.ExportAsync(user, new AuditSearchFilter(Page(), action: "pedidos.crear"), "export/lote-1");

        exportResult.IsSuccess.Should().BeTrue();
        var readBack = await wormPipeline.ReadAsync("export/lote-1");
        readBack.IsSuccess.Should().BeTrue();
        readBack.Value.Batch.Should().ContainSingle().Which.Action.Should().Be("pedidos.crear");
    }

    [Fact]
    public async Task ExportAsync_SinElPermisoRequerido_EsDenegada_FailClosed()
    {
        var provider = BuildProvider([]);
        var sut = provider.GetRequiredService<IAuditQueryService>();
        var user = CreateUser(Guid.NewGuid().ToString());

        var result = await sut.ExportAsync(user, new AuditSearchFilter(Page()), "export/lote-x");

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
    }

    [Fact]
    public async Task ExportAsync_SinAddSharedAuditWormExport_DevuelveFalloExplicito_SinExcepcion()
    {
        var provider = BuildProvider(["auditoria.consultar"], withWormExport: false);
        var sut = provider.GetRequiredService<IAuditQueryService>();
        var user = CreateUser(Guid.NewGuid().ToString());

        var result = await sut.ExportAsync(user, new AuditSearchFilter(Page()), "export/lote-y");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Auditoria.ExportacionNoConfigurada");
    }

    [Fact]
    public async Task ExportAsync_Exitosa_GeneraSuPropiaEntradaDeAuditoriaDeExportacion()
    {
        var provider = BuildProvider(["auditoria.consultar"]);
        var auditWriter = (InMemoryAuditWriter)provider.GetRequiredService<IAuditWriter>();
        await SeedAsync(auditWriter, null, "actor-1", "pedidos.crear", AuditOutcome.Success);
        var sut = provider.GetRequiredService<IAuditQueryService>();
        var userId = Guid.NewGuid().ToString();
        var user = CreateUser(userId);

        await sut.ExportAsync(user, new AuditSearchFilter(Page(), action: "pedidos.crear"), "export/lote-z");

        var exportEntries = auditWriter.Entries.Where(e => e.Action == "auditoria.exportar").ToArray();
        exportEntries.Should().ContainSingle();
        exportEntries[0].Actor.Id.Should().Be(userId);
        exportEntries[0].Outcome.Should().Be(AuditOutcome.Success);
    }
}
