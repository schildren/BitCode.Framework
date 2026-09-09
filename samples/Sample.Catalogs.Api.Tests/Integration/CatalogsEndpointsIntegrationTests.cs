using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using BitCode.Framework.Platform.Catalogs;
using BitCode.Framework.Shared.Infrastructure.Security.Abac;
using BitCode.Framework.Shared.Infrastructure.Security.Identity;
using BitCode.Framework.Shared.Infrastructure.Security.Jwt;
using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using BitCode.Framework.Shared.Testing;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Sample.Catalogs.Api.Tests.Integration;

/// <summary>
/// Verifica de punta a punta, contra SQL Server real (Testcontainers), el módulo Catalogs and Parameters
/// (Fase 6, módulo 3): alta de catálogo (con idempotencia), alta de versión en borrador con ítems,
/// publicación de la versión (RBAC + ABAC de alcance por <c>catalogoId</c>, y cierre automático de la
/// vigencia anterior al publicar una versión nueva), listado de ítems de la versión vigente a una fecha
/// dada, alta de parámetro, alta de vigencia (con rechazo por solapamiento) y consulta del valor vigente
/// a una fecha dada. Genera el JWT directamente vía <see cref="IJwtTokenGenerator"/>, mismo patrón que
/// <c>Sample.Organization.Api.Tests/Integration/OrganizationEndpointsIntegrationTests.cs</c>.
/// </summary>
public class CatalogsEndpointsIntegrationTests : IAsyncLifetime
{
    private readonly SqlServerContainerFixture _sqlServerFixture = new();
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        await _sqlServerFixture.InitializeAsync();

        Environment.SetEnvironmentVariable(
            "ConnectionStrings__Default",
            _sqlServerFixture.BuildIsolatedConnectionString("SampleCatalogsApiTests", Guid.NewGuid().ToString("N")));
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__Identity",
            _sqlServerFixture.BuildIsolatedConnectionString("SampleCatalogsApiTestsIdentity", Guid.NewGuid().ToString("N")));

        // Configura, solo para este suite de tests, la regla ABAC de alcance por "catalogoId" que
        // docs/guia-catalogs.md documenta como la extensión típica de un consumidor real (el host de
        // referencia, Sample.Catalogs.Api, no la registra por defecto) -- necesaria para demostrar el
        // criterio de aceptación "RBAC y ABAC en operaciones sensibles" de punta a punta.
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.Configure<AbacOptions>(options =>
                options.ScopeRules.Add(new AbacScopeAttributeRule
                {
                    ResourceType = "catalogos.versiones",
                    ResourceAttributeKey = "catalogoId",
                    ClaimType = "catalogo_id",
                }))));
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _sqlServerFixture.DisposeAsync();
    }

    /// <summary>Único tenant usado por todo este suite -- estos tests ejercitan CRUD/RBAC/ABAC/vigencia
    /// de Catalogs, no el aislamiento multi-tenant (ya cubierto en Fase 1); todos los actores comparten
    /// el mismo tenant para que <c>HttpContextTenantProvider</c> (multi-tenancy obligatoria, F1-12)
    /// resuelva un <c>tenant_id</c> válido del JWT en cada request.</summary>
    private static readonly Guid TenantId = Guid.NewGuid();

    private async Task<(Guid UserId, string Token)> SeedActorAsync(
        string userName, IReadOnlyList<string>? permissions = null, IReadOnlyList<Claim>? extraClaims = null)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var tokenGenerator = scope.ServiceProvider.GetRequiredService<IJwtTokenGenerator>();

        var roleName = $"rol-{userName}";
        var role = new ApplicationRole(roleName);
        (await roleManager.CreateAsync(role)).Succeeded.Should().BeTrue();

        foreach (var permission in permissions ?? [])
        {
            (await roleManager.AddPermissionAsync(role, permission)).Succeeded.Should().BeTrue();
        }

        var user = new ApplicationUser { UserName = userName, Email = $"{userName}@test.local", TenantId = TenantId };
        (await userManager.CreateAsync(user, "Contraseña!Segura1")).Succeeded.Should().BeTrue();
        (await userManager.AddToRoleAsync(user, roleName)).Succeeded.Should().BeTrue();

        var token = tokenGenerator.GenerateAccessToken(user, [roleName], extraClaims ?? []);
        return (user.Id, token);
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string url, string token, object? body = null, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (method != HttpMethod.Get)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString());
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    [Fact]
    public async Task CrearCatalogo_SinAutenticacion_Retorna401()
    {
        var response = await _client!.PostAsJsonAsync(
            "/api/v1/catalogos",
            new { codigo = "MONEDAS", nombre = "Monedas", descripcion = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CrearCatalogo_ConPermiso_PersisteYEsIdempotente()
    {
        var (_, adminToken) = await SeedActorAsync("admin-crea-catalogo", [CatalogsPermissions.CatalogosCrear]);
        var idempotencyKey = Guid.NewGuid().ToString();
        var payload = new { codigo = "MONEDAS-1", nombre = "Monedas", descripcion = (string?)null };

        var primeraRespuesta = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, "/api/v1/catalogos", adminToken, payload, idempotencyKey));
        primeraRespuesta.StatusCode.Should().Be(HttpStatusCode.Created);
        var primerId = await primeraRespuesta.Content.ReadFromJsonAsync<Guid>();

        // F1-22: mismo Idempotency-Key + mismo body -> mismo resultado, sin crear un segundo catálogo.
        var segundaRespuesta = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, "/api/v1/catalogos", adminToken, payload, idempotencyKey));
        var segundoId = await segundaRespuesta.Content.ReadFromJsonAsync<Guid>();
        segundoId.Should().Be(primerId);
    }

    [Fact]
    public async Task CrearCatalogo_ConCodigoDuplicado_Retorna409()
    {
        var (_, adminToken) = await SeedActorAsync("admin-catalogo-duplicado", [CatalogsPermissions.CatalogosCrear]);
        var payload = new { codigo = "MONEDAS-DUP", nombre = "Monedas", descripcion = (string?)null };

        (await _client!.SendAsync(BuildRequest(HttpMethod.Post, "/api/v1/catalogos", adminToken, payload)))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var segundaRespuesta = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, "/api/v1/catalogos", adminToken, payload));
        segundaRespuesta.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// El vertical slice completo de "Catálogos versionados": crear catálogo, cargar una versión en
    /// borrador con ítems, publicarla (RBAC + ABAC de alcance) y leer los ítems de la versión vigente a
    /// la fecha de publicación. Es la consulta REAL exigida por el Plan Maestro, no un CRUD de altas
    /// solamente.
    /// </summary>
    [Fact]
    public async Task CrearVersionYPublicar_ListaItemsDeLaVersionVigente_FuncionaDePuntaAPunta()
    {
        var (_, adminToken) = await SeedActorAsync(
            "admin-version-catalogo",
            [
                CatalogsPermissions.CatalogosCrear, CatalogsPermissions.CatalogosVer,
                CatalogsPermissions.VersionesCrear, CatalogsPermissions.VersionesPublicar,
            ]);

        var catalogoId = await (await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, "/api/v1/catalogos", adminToken,
                new { codigo = "TIPOS_DOCUMENTO", nombre = "Tipos de documento", descripcion = (string?)null })))
            .Content.ReadFromJsonAsync<Guid>();

        var crearVersionResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, $"/api/v1/catalogos/{catalogoId}/versiones", adminToken,
                new
                {
                    items = new[]
                    {
                        new { codigo = "DNI", etiqueta = "Documento Nacional de Identidad", valor = (string?)null, orden = 1 },
                        new { codigo = "PASAPORTE", etiqueta = "Pasaporte", valor = (string?)null, orden = 2 },
                    },
                }));
        crearVersionResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var versionId = await crearVersionResponse.Content.ReadFromJsonAsync<Guid>();

        var vigenteDesde = DateTime.UtcNow.AddMinutes(-1);
        var publicarResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, $"/api/v1/catalogos/versiones/{versionId}/publicar", adminToken,
                new { vigenteDesde, vigenteHasta = (DateTime?)null }));
        publicarResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var itemsResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/catalogos/{catalogoId}/items-vigentes", adminToken));
        itemsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await itemsResponse.Content.ReadFromJsonAsync<List<CatalogoItemResponseDto>>();
        items.Should().HaveCount(2);
        items.Should().ContainSingle(i => i.Codigo == "DNI");
        items.Should().ContainSingle(i => i.Codigo == "PASAPORTE");
    }

    /// <summary>Publicar una versión nueva del mismo catálogo cierra automáticamente la vigencia de la
    /// versión previamente vigente -- nunca conviven dos versiones vigentes del mismo catálogo al mismo
    /// tiempo (criterio de aceptación del modelo de versionado, ver <c>CatalogoVersion.CerrarVigencia</c>).</summary>
    [Fact]
    public async Task PublicarSegundaVersion_CierraLaVigenciaDeLaAnterior()
    {
        var (_, adminToken) = await SeedActorAsync(
            "admin-cierra-vigencia",
            [
                CatalogsPermissions.CatalogosCrear, CatalogsPermissions.CatalogosVer,
                CatalogsPermissions.VersionesCrear, CatalogsPermissions.VersionesPublicar,
            ]);

        var catalogoId = await (await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, "/api/v1/catalogos", adminToken,
                new { codigo = "MONEDAS_VIGENCIA", nombre = "Monedas", descripcion = (string?)null })))
            .Content.ReadFromJsonAsync<Guid>();

        var version1Id = await (await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, $"/api/v1/catalogos/{catalogoId}/versiones", adminToken,
                new { items = new[] { new { codigo = "ARS", etiqueta = "Peso argentino", valor = (string?)null, orden = 1 } } })))
            .Content.ReadFromJsonAsync<Guid>();

        var inicioV1 = DateTime.UtcNow.AddMinutes(-10);
        (await _client!.SendAsync(BuildRequest(HttpMethod.Post, $"/api/v1/catalogos/versiones/{version1Id}/publicar", adminToken,
            new { vigenteDesde = inicioV1, vigenteHasta = (DateTime?)null })))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var version2Id = await (await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, $"/api/v1/catalogos/{catalogoId}/versiones", adminToken,
                new { items = new[] { new { codigo = "USD", etiqueta = "Dólar estadounidense", valor = (string?)null, orden = 1 } } })))
            .Content.ReadFromJsonAsync<Guid>();

        var inicioV2 = DateTime.UtcNow;
        (await _client!.SendAsync(BuildRequest(HttpMethod.Post, $"/api/v1/catalogos/versiones/{version2Id}/publicar", adminToken,
            new { vigenteDesde = inicioV2, vigenteHasta = (DateTime?)null })))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // A una fecha ANTES de que la v2 empezara a regir, sigue vigente la v1.
        var itemsAntes = await (await _client!.SendAsync(BuildRequest(
            HttpMethod.Get, $"/api/v1/catalogos/{catalogoId}/items-vigentes?fecha={Uri.EscapeDataString(inicioV1.AddMinutes(1).ToString("O"))}", adminToken)))
            .Content.ReadFromJsonAsync<List<CatalogoItemResponseDto>>();
        itemsAntes.Should().ContainSingle(i => i.Codigo == "ARS");

        // A una fecha DESPUÉS de la publicación de la v2, ya rige la v2 (la v1 quedó cerrada).
        var itemsDespues = await (await _client!.SendAsync(BuildRequest(
            HttpMethod.Get, $"/api/v1/catalogos/{catalogoId}/items-vigentes", adminToken)))
            .Content.ReadFromJsonAsync<List<CatalogoItemResponseDto>>();
        itemsDespues.Should().ContainSingle(i => i.Codigo == "USD");
    }

    /// <summary>
    /// El caso de referencia del requisito común "RBAC y ABAC en operaciones sensibles" (Fase 6): el
    /// actor TIENE el permiso RBAC (<see cref="CatalogsPermissions.VersionesPublicar"/>) pero su claim
    /// <c>catalogo_id</c> solo cubre OTRO catálogo -- la regla ABAC de alcance incorporada
    /// (<c>AttributeScopeAbacRule</c>, configurada en <see cref="InitializeAsync"/>) deniega la
    /// publicación de todos modos.
    /// </summary>
    [Fact]
    public async Task PublicarVersion_FueraDeAlcanceAbac_EsDenegadaAunqueTengaElPermisoRbac()
    {
        var (_, superAdminToken) = await SeedActorAsync(
            "admin-crea-catalogos-abac", [CatalogsPermissions.CatalogosCrear, CatalogsPermissions.VersionesCrear]);

        var catalogoPropioId = await (await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, "/api/v1/catalogos", superAdminToken,
                new { codigo = "CATALOGO_PROPIO", nombre = "Catálogo propio", descripcion = (string?)null })))
            .Content.ReadFromJsonAsync<Guid>();
        var versionPropiaId = await (await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, $"/api/v1/catalogos/{catalogoPropioId}/versiones", superAdminToken,
                new { items = new[] { new { codigo = "X", etiqueta = "X", valor = (string?)null, orden = 1 } } })))
            .Content.ReadFromJsonAsync<Guid>();

        var catalogoAjenoId = await (await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, "/api/v1/catalogos", superAdminToken,
                new { codigo = "CATALOGO_AJENO", nombre = "Catálogo ajeno", descripcion = (string?)null })))
            .Content.ReadFromJsonAsync<Guid>();
        var versionAjenaId = await (await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, $"/api/v1/catalogos/{catalogoAjenoId}/versiones", superAdminToken,
                new { items = new[] { new { codigo = "Y", etiqueta = "Y", valor = (string?)null, orden = 1 } } })))
            .Content.ReadFromJsonAsync<Guid>();

        // El actor limitado tiene el permiso RBAC de publicar versiones, pero su claim "catalogo_id"
        // solo lo habilita para el catálogo propio.
        var (_, actorLimitadoToken) = await SeedActorAsync(
            "admin-limitado-catalogos-abac",
            [CatalogsPermissions.VersionesPublicar],
            [new Claim("catalogo_id", catalogoPropioId.ToString())]);

        var publicarPropiaResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, $"/api/v1/catalogos/versiones/{versionPropiaId}/publicar", actorLimitadoToken,
                new { vigenteDesde = DateTime.UtcNow, vigenteHasta = (DateTime?)null }));
        publicarPropiaResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var publicarAjenaResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, $"/api/v1/catalogos/versiones/{versionAjenaId}/publicar", actorLimitadoToken,
                new { vigenteDesde = DateTime.UtcNow, vigenteHasta = (DateTime?)null }));
        publicarAjenaResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CrearParametro_ConPermiso_Persiste()
    {
        var (_, adminToken) = await SeedActorAsync("admin-crea-parametro", [CatalogsPermissions.ParametrosCrear]);

        var response = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, "/api/v1/parametros", adminToken,
                new { codigo = "TASA_IVA", nombre = "Tasa de IVA", descripcion = (string?)null }));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>Criterio de aceptación explícito del módulo ("Concurrencia de vigencias"): dos vigencias
    /// del mismo parámetro que se solapan en el tiempo son un error de negocio esperado
    /// (<c>Result.Failure</c> -&gt; 409), nunca una excepción.</summary>
    [Fact]
    public async Task CrearVigencia_Solapada_Retorna409()
    {
        var (_, adminToken) = await SeedActorAsync(
            "admin-vigencia-solapada",
            [CatalogsPermissions.ParametrosCrear, CatalogsPermissions.VigenciasCrear]);

        var parametroId = await (await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, "/api/v1/parametros", adminToken,
                new { codigo = "LIMITE_DIARIO", nombre = "Límite diario de transferencia", descripcion = (string?)null })))
            .Content.ReadFromJsonAsync<Guid>();

        var desde = DateTime.UtcNow.Date;
        (await _client!.SendAsync(BuildRequest(HttpMethod.Post, $"/api/v1/parametros/{parametroId}/vigencias", adminToken,
            new { valor = "100000", vigenteDesde = desde, vigenteHasta = desde.AddDays(30) })))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        // Se solapa con la vigencia anterior (empieza 10 días antes de que termine la primera).
        var solapadaResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, $"/api/v1/parametros/{parametroId}/vigencias", adminToken,
                new { valor = "150000", vigenteDesde = desde.AddDays(20), vigenteHasta = (DateTime?)null }));

        solapadaResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>La consulta real exigida por el Plan Maestro para Parámetros: "¿cuál es el valor vigente
    /// de este parámetro en una fecha dada?" -- verificada con dos vigencias consecutivas no solapadas.</summary>
    [Fact]
    public async Task ObtenerValorVigente_ADistintasFechas_ResuelveLaVigenciaCorrespondiente()
    {
        var (_, adminToken) = await SeedActorAsync(
            "admin-valor-vigente",
            [CatalogsPermissions.ParametrosCrear, CatalogsPermissions.VigenciasCrear, CatalogsPermissions.ParametrosVer]);

        var parametroId = await (await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, "/api/v1/parametros", adminToken,
                new { codigo = "TASA_IVA_VIGENCIA", nombre = "Tasa de IVA", descripcion = (string?)null })))
            .Content.ReadFromJsonAsync<Guid>();

        var desde = DateTime.UtcNow.Date.AddDays(-60);
        var corte = DateTime.UtcNow.Date.AddDays(-30);

        (await _client!.SendAsync(BuildRequest(HttpMethod.Post, $"/api/v1/parametros/{parametroId}/vigencias", adminToken,
            new { valor = "21", vigenteDesde = desde, vigenteHasta = corte })))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        (await _client!.SendAsync(BuildRequest(HttpMethod.Post, $"/api/v1/parametros/{parametroId}/vigencias", adminToken,
            new { valor = "22", vigenteDesde = corte, vigenteHasta = (DateTime?)null })))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var valorAntesDelCorte = await (await _client!.SendAsync(BuildRequest(
            HttpMethod.Get,
            $"/api/v1/parametros/{parametroId}/valor-vigente?fecha={Uri.EscapeDataString(desde.AddDays(5).ToString("O"))}",
            adminToken)))
            .Content.ReadFromJsonAsync<ParametroValorVigenteResponseDto>();
        valorAntesDelCorte!.Valor.Should().Be("21");

        var valorHoy = await (await _client!.SendAsync(BuildRequest(
            HttpMethod.Get, $"/api/v1/parametros/{parametroId}/valor-vigente", adminToken)))
            .Content.ReadFromJsonAsync<ParametroValorVigenteResponseDto>();
        valorHoy!.Valor.Should().Be("22");
    }

    private sealed record CatalogoItemResponseDto(Guid Id, string Codigo, string Etiqueta, string? Valor, int Orden);

    private sealed record ParametroValorVigenteResponseDto(Guid ParametroId, string Valor, DateTime Fecha, DateTime VigenteDesde, DateTime? VigenteHasta);
}
