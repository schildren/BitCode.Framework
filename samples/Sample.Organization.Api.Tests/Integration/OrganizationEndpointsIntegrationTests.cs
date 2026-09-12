using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using BitCode.Framework.Platform.Organization;
using BitCode.Framework.Shared.Infrastructure.Security.Abac;
using BitCode.Framework.Shared.Infrastructure.Security.Identity;
using BitCode.Framework.Shared.Infrastructure.Security.Jwt;
using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using BitCode.Framework.Shared.Testing;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Sample.Organization.Api.Tests.Integration;

/// <summary>
/// Verifica de punta a punta, contra SQL Server real (Testcontainers), el módulo Organization (Fase 6,
/// módulo 2): alta de empresa (con idempotencia), alta de sucursal bajo una empresa (rechazada si la
/// empresa está inactiva), listado, desactivación de empresa (RBAC con permiso distinto al de creación
/// + ABAC de alcance por <c>empresaId</c>) y desactivación de sucursal (RBAC simple). Genera el JWT
/// directamente vía <see cref="IJwtTokenGenerator"/>, mismo patrón que
/// <c>Sample.IdentityAdmin.Api.Tests/Integration/IdentityAdministrationEndpointsIntegrationTests.cs</c>.
/// </summary>
public class OrganizationEndpointsIntegrationTests : IAsyncLifetime
{
    private readonly SqlServerContainerFixture _sqlServerFixture = new();
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        await _sqlServerFixture.InitializeAsync();

        Environment.SetEnvironmentVariable(
            "ConnectionStrings__Default",
            _sqlServerFixture.BuildIsolatedConnectionString("SampleOrganizationApiTests", Guid.NewGuid().ToString("N")));
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__Identity",
            _sqlServerFixture.BuildIsolatedConnectionString("SampleOrganizationApiTestsIdentity", Guid.NewGuid().ToString("N")));

        // Configura, solo para este suite de tests, la regla ABAC de alcance por "empresaId" que
        // docs/guia-organization.md documenta como la extensión típica de un consumidor real (el host
        // de referencia, Sample.Organization.Api, no la registra por defecto) -- necesaria para
        // demostrar el criterio de aceptación "RBAC y ABAC en operaciones sensibles" de punta a punta.
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.Configure<AbacOptions>(options =>
                options.ScopeRules.Add(new AbacScopeAttributeRule
                {
                    ResourceType = "organizacion.empresas",
                    ResourceAttributeKey = "empresaId",
                    ClaimType = "empresa_id",
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

    /// <summary>Único tenant usado por todo este suite -- estos tests ejercitan CRUD/RBAC/ABAC de
    /// Organization, no el aislamiento multi-tenant (ya cubierto en Fase 1); todos los actores
    /// comparten el mismo tenant para que <c>HttpContextTenantProvider</c> (multi-tenancy obligatoria,
    /// F1-12) resuelva un <c>tenant_id</c> válido del JWT en cada request.</summary>
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
    public async Task CrearEmpresa_SinAutenticacion_Retorna401()
    {
        var response = await _client!.PostAsJsonAsync(
            "/api/v1/organizacion/empresas",
            new { razonSocial = "Acme SA", identificador = "30-11111111-1" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CrearEmpresa_ConPermiso_PersisteYEsIdempotente()
    {
        var (_, adminToken) = await SeedActorAsync("admin-crea-empresa", [OrganizationPermissions.EmpresasCrear]);
        var idempotencyKey = Guid.NewGuid().ToString();
        var payload = new { razonSocial = "Acme SA", identificador = "30-11111111-1" };

        var primeraRespuesta = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, "/api/v1/organizacion/empresas", adminToken, payload, idempotencyKey));
        primeraRespuesta.StatusCode.Should().Be(HttpStatusCode.Created);
        var primerId = await primeraRespuesta.Content.ReadFromJsonAsync<Guid>();

        // F1-22: mismo Idempotency-Key + mismo body -> mismo resultado, sin crear una segunda empresa.
        var segundaRespuesta = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, "/api/v1/organizacion/empresas", adminToken, payload, idempotencyKey));
        var segundoId = await segundaRespuesta.Content.ReadFromJsonAsync<Guid>();
        segundoId.Should().Be(primerId);
    }

    [Fact]
    public async Task ObtenerEmpresa_SinPermiso_Retorna403()
    {
        var (_, adminToken) = await SeedActorAsync("admin-crea-empresa2", [OrganizationPermissions.EmpresasCrear]);
        var crearResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, "/api/v1/organizacion/empresas", adminToken,
                new { razonSocial = "Beta SRL", identificador = "30-22222222-2" }));
        var empresaId = await crearResponse.Content.ReadFromJsonAsync<Guid>();

        var (_, tokenSinPermiso) = await SeedActorAsync("sin-permiso-empresa");

        var response = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/organizacion/empresas/{empresaId}", tokenSinPermiso));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CrearSucursal_BajoEmpresaActiva_FuncionaDePuntaAPunta()
    {
        var (_, adminToken) = await SeedActorAsync(
            "admin-sucursales",
            [OrganizationPermissions.EmpresasCrear, OrganizationPermissions.SucursalesCrear, OrganizationPermissions.SucursalesVer]);

        var crearEmpresaResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, "/api/v1/organizacion/empresas", adminToken,
                new { razonSocial = "Gamma SA", identificador = "30-33333333-3" }));
        var empresaId = await crearEmpresaResponse.Content.ReadFromJsonAsync<Guid>();

        var crearSucursalResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, $"/api/v1/organizacion/empresas/{empresaId}/sucursales", adminToken,
                new { nombre = "Casa Central", direccion = "Av. Siempre Viva 123" }));
        crearSucursalResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var listarResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/organizacion/empresas/{empresaId}/sucursales", adminToken));
        listarResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var pagina = await listarResponse.Content.ReadFromJsonAsync<PagedResultDto<SucursalResponseDto>>();
        pagina!.Items.Should().ContainSingle(s => s.Nombre == "Casa Central");
    }

    [Fact]
    public async Task CrearSucursal_BajoEmpresaDesactivada_Retorna400()
    {
        var (_, adminToken) = await SeedActorAsync(
            "admin-sucursal-inactiva",
            [OrganizationPermissions.EmpresasCrear, OrganizationPermissions.EmpresasDesactivar, OrganizationPermissions.SucursalesCrear]);

        var crearEmpresaResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, "/api/v1/organizacion/empresas", adminToken,
                new { razonSocial = "Delta SA", identificador = "30-44444444-4" }));
        var empresaId = await crearEmpresaResponse.Content.ReadFromJsonAsync<Guid>();

        var desactivarResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, $"/api/v1/organizacion/empresas/{empresaId}/desactivar", adminToken));
        desactivarResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var crearSucursalResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, $"/api/v1/organizacion/empresas/{empresaId}/sucursales", adminToken,
                new { nombre = "Sucursal Fantasma", direccion = (string?)null }));

        crearSucursalResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// El caso de referencia del requisito común "RBAC y ABAC en operaciones sensibles" (Fase 6): el
    /// actor TIENE el permiso RBAC (<see cref="OrganizationPermissions.EmpresasDesactivar"/>) pero su
    /// claim <c>empresa_id</c> solo cubre OTRA empresa -- la regla ABAC de alcance incorporada
    /// (<c>AttributeScopeAbacRule</c>, configurada en <see cref="InitializeAsync"/>) deniega la
    /// desactivación de todos modos.
    /// </summary>
    [Fact]
    public async Task DesactivarEmpresa_FueraDeAlcanceAbac_EsDenegadaAunqueTengaElPermisoRbac()
    {
        var (_, superAdminToken) = await SeedActorAsync("admin-crea-empresas-abac", [OrganizationPermissions.EmpresasCrear]);

        var crearEmpresaPropiaResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, "/api/v1/organizacion/empresas", superAdminToken,
                new { razonSocial = "Empresa Propia SA", identificador = "30-55555555-5" }));
        var empresaPropiaId = await crearEmpresaPropiaResponse.Content.ReadFromJsonAsync<Guid>();

        var crearOtraEmpresaResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, "/api/v1/organizacion/empresas", superAdminToken,
                new { razonSocial = "Otra Empresa SA", identificador = "30-66666666-6" }));
        var otraEmpresaId = await crearOtraEmpresaResponse.Content.ReadFromJsonAsync<Guid>();

        // El actor limitado tiene el permiso RBAC de desactivar empresas, pero su claim "empresa_id"
        // solo lo habilita para la empresa propia.
        var (_, actorLimitadoToken) = await SeedActorAsync(
            "admin-limitado-abac",
            [OrganizationPermissions.EmpresasDesactivar],
            [new Claim("empresa_id", empresaPropiaId.ToString())]);

        var desactivarPropiaResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, $"/api/v1/organizacion/empresas/{empresaPropiaId}/desactivar", actorLimitadoToken));
        desactivarPropiaResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var desactivarOtraResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, $"/api/v1/organizacion/empresas/{otraEmpresaId}/desactivar", actorLimitadoToken));
        desactivarOtraResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DesactivarSucursal_ConPermiso_FuncionaSinAbacAdicional()
    {
        var (_, adminToken) = await SeedActorAsync(
            "admin-desactiva-sucursal",
            [OrganizationPermissions.EmpresasCrear, OrganizationPermissions.SucursalesCrear, OrganizationPermissions.SucursalesDesactivar, OrganizationPermissions.SucursalesVer]);

        var crearEmpresaResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, "/api/v1/organizacion/empresas", adminToken,
                new { razonSocial = "Epsilon SA", identificador = "30-77777777-7" }));
        var empresaId = await crearEmpresaResponse.Content.ReadFromJsonAsync<Guid>();

        var crearSucursalResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, $"/api/v1/organizacion/empresas/{empresaId}/sucursales", adminToken,
                new { nombre = "Sucursal Norte", direccion = (string?)null }));
        var sucursalId = await crearSucursalResponse.Content.ReadFromJsonAsync<Guid>();

        var desactivarResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, $"/api/v1/organizacion/sucursales/{sucursalId}/desactivar", adminToken));
        desactivarResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var obtenerResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/organizacion/sucursales/{sucursalId}", adminToken));
        var sucursal = await obtenerResponse.Content.ReadFromJsonAsync<SucursalResponseDto>();
        sucursal!.Activa.Should().BeFalse();
    }

    [Fact]
    public async Task CrearAreaYCargo_BajoJerarquiaCompleta_FuncionaDePuntaAPunta()
    {
        var (_, adminToken) = await SeedActorAsync(
            "admin-jerarquia",
            [
                OrganizationPermissions.EmpresasCrear, OrganizationPermissions.SucursalesCrear,
                OrganizationPermissions.AreasCrear, OrganizationPermissions.AreasVer,
                OrganizationPermissions.CargosCrear, OrganizationPermissions.CargosVer,
            ]);

        var empresaId = await (await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, "/api/v1/organizacion/empresas", adminToken,
                new { razonSocial = "Zeta SA", identificador = "30-88888888-8" })))
            .Content.ReadFromJsonAsync<Guid>();

        var sucursalId = await (await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, $"/api/v1/organizacion/empresas/{empresaId}/sucursales", adminToken,
                new { nombre = "Sucursal Única", direccion = (string?)null })))
            .Content.ReadFromJsonAsync<Guid>();

        var areaResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, $"/api/v1/organizacion/sucursales/{sucursalId}/areas", adminToken,
                new { nombre = "Recursos Humanos", parentAreaId = (Guid?)null }));
        areaResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var areaId = await areaResponse.Content.ReadFromJsonAsync<Guid>();

        var listarAreasResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/organizacion/sucursales/{sucursalId}/areas", adminToken));
        var areas = await listarAreasResponse.Content.ReadFromJsonAsync<List<AreaResponseDto>>();
        areas.Should().ContainSingle(a => a.Nombre == "Recursos Humanos");

        var cargoResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, $"/api/v1/organizacion/areas/{areaId}/cargos", adminToken,
                new { nombre = "Analista de RRHH" }));
        cargoResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var listarCargosResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/organizacion/areas/{areaId}/cargos", adminToken));
        var cargos = await listarCargosResponse.Content.ReadFromJsonAsync<List<CargoResponseDto>>();
        cargos.Should().ContainSingle(c => c.Nombre == "Analista de RRHH");
    }

    private sealed record PagedResultDto<T>(List<T> Items, int Page, int PageSize, int TotalCount);

    private sealed record SucursalResponseDto(Guid Id, Guid EmpresaId, string Nombre, string? Direccion, bool Activa);

    private sealed record AreaResponseDto(Guid Id, Guid SucursalId, string Nombre, Guid? ParentAreaId);

    private sealed record CargoResponseDto(Guid Id, Guid AreaId, string Nombre);
}
