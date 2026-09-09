using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using BitCode.Framework.Platform.FeatureManagement;
using BitCode.Framework.Platform.FeatureManagement.Segmentos;
using BitCode.Framework.Shared.Infrastructure.Security.Abac;
using BitCode.Framework.Shared.Infrastructure.Security.Identity;
using BitCode.Framework.Shared.Infrastructure.Security.Jwt;
using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using BitCode.Framework.Shared.Testing;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Sample.FeatureManagement.Api.Tests.Integration;

/// <summary>
/// Verifica de punta a punta, contra SQL Server real (Testcontainers), el módulo Feature Management
/// (Fase 6, módulo 4): alta de flag (con idempotencia y rechazo de nombre duplicado), activación /
/// desactivación (RBAC + ABAC de alcance por <c>featureFlagId</c>), alta de segmentos (por tenant y por
/// porcentaje), alta de rollout (con rechazo de asociación duplicada) y, el criterio de aceptación más
/// importante del módulo, EVALUACIÓN REAL de un flag para distintos contextos de tenant/porcentaje.
/// Genera el JWT directamente vía <see cref="IJwtTokenGenerator"/>, mismo patrón que
/// <c>Sample.Catalogs.Api.Tests/Integration/CatalogsEndpointsIntegrationTests.cs</c>.
/// </summary>
public class FeatureManagementEndpointsIntegrationTests : IAsyncLifetime
{
    private readonly SqlServerContainerFixture _sqlServerFixture = new();
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        await _sqlServerFixture.InitializeAsync();

        Environment.SetEnvironmentVariable(
            "ConnectionStrings__Default",
            _sqlServerFixture.BuildIsolatedConnectionString("SampleFeatureManagementApiTests", Guid.NewGuid().ToString("N")));
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__Identity",
            _sqlServerFixture.BuildIsolatedConnectionString("SampleFeatureManagementApiTestsIdentity", Guid.NewGuid().ToString("N")));

        // Configura, solo para este suite de tests, la regla ABAC de alcance por "featureFlagId" que
        // docs/guia-feature-management.md documenta como la extensión típica de un consumidor real (el
        // host de referencia, Sample.FeatureManagement.Api, no la registra por defecto) -- necesaria
        // para demostrar el criterio de aceptación "RBAC y ABAC en operaciones sensibles" de punta a
        // punta.
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.Configure<AbacOptions>(options =>
                options.ScopeRules.Add(new AbacScopeAttributeRule
                {
                    ResourceType = "featuremanagement.flags",
                    ResourceAttributeKey = "featureFlagId",
                    ClaimType = "feature_flag_id",
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

    /// <summary>Único tenant usado por defecto por todo este suite salvo donde se necesitan tenants
    /// distintos explícitamente (pruebas de segmentación por tenant) -- estos tests ejercitan
    /// CRUD/RBAC/ABAC/evaluación de Feature Management, no el aislamiento multi-tenant (ya cubierto en
    /// Fase 1).</summary>
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
    public async Task CrearFeatureFlag_SinAutenticacion_Retorna401()
    {
        var response = await _client!.PostAsJsonAsync(
            "/api/v1/feature-flags", new { nombre = "nuevo-checkout", descripcion = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CrearFeatureFlag_ConPermiso_PersisteYEsIdempotente()
    {
        var (_, adminToken) = await SeedActorAsync("admin-crea-flag", [FeatureManagementPermissions.FlagsCrear]);
        var idempotencyKey = Guid.NewGuid().ToString();
        var payload = new { nombre = "flag-idempotente", descripcion = (string?)null };

        var primeraRespuesta = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, "/api/v1/feature-flags", adminToken, payload, idempotencyKey));
        primeraRespuesta.StatusCode.Should().Be(HttpStatusCode.Created);
        var primerId = await primeraRespuesta.Content.ReadFromJsonAsync<Guid>();

        // F1-22: mismo Idempotency-Key + mismo body -> mismo resultado, sin crear un segundo flag.
        var segundaRespuesta = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, "/api/v1/feature-flags", adminToken, payload, idempotencyKey));
        var segundoId = await segundaRespuesta.Content.ReadFromJsonAsync<Guid>();
        segundoId.Should().Be(primerId);
    }

    [Fact]
    public async Task CrearFeatureFlag_ConNombreDuplicado_Retorna409()
    {
        var (_, adminToken) = await SeedActorAsync("admin-flag-duplicado", [FeatureManagementPermissions.FlagsCrear]);
        var payload = new { nombre = "flag-duplicado", descripcion = (string?)null };

        (await _client!.SendAsync(BuildRequest(HttpMethod.Post, "/api/v1/feature-flags", adminToken, payload)))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var segundaRespuesta = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, "/api/v1/feature-flags", adminToken, payload));
        segundaRespuesta.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>Un flag recién creado nace apagado -- activarlo/desactivarlo requiere el permiso RBAC
    /// distinto correspondiente, y ambas transiciones quedan auditadas (verificado indirectamente por el
    /// éxito HTTP -- la auditoría en sí ya está cubierta por los tests unitarios del mecanismo en Fase
    /// 2).</summary>
    [Fact]
    public async Task ActivarYDesactivarFlag_ConPermiso_CambiaElEstado()
    {
        var (_, adminToken) = await SeedActorAsync(
            "admin-activa-flag",
            [
                FeatureManagementPermissions.FlagsCrear, FeatureManagementPermissions.FlagsVer,
                FeatureManagementPermissions.FlagsActivar, FeatureManagementPermissions.FlagsDesactivar,
            ]);

        var flagId = await (await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, "/api/v1/feature-flags", adminToken,
                new { nombre = "flag-activable", descripcion = (string?)null })))
            .Content.ReadFromJsonAsync<Guid>();

        var estadoInicial = await (await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/feature-flags/{flagId}", adminToken)))
            .Content.ReadFromJsonAsync<FeatureFlagResponseDto>();
        estadoInicial!.Activo.Should().BeFalse();

        (await _client!.SendAsync(BuildRequest(HttpMethod.Post, $"/api/v1/feature-flags/{flagId}/activar", adminToken)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var estadoActivo = await (await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/feature-flags/{flagId}", adminToken)))
            .Content.ReadFromJsonAsync<FeatureFlagResponseDto>();
        estadoActivo!.Activo.Should().BeTrue();

        // Activar un flag ya activo es un conflicto de negocio esperado (Result.Failure -> 409), nunca
        // una excepción.
        (await _client!.SendAsync(BuildRequest(HttpMethod.Post, $"/api/v1/feature-flags/{flagId}/activar", adminToken)))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await _client!.SendAsync(BuildRequest(HttpMethod.Post, $"/api/v1/feature-flags/{flagId}/desactivar", adminToken)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var estadoFinal = await (await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/feature-flags/{flagId}", adminToken)))
            .Content.ReadFromJsonAsync<FeatureFlagResponseDto>();
        estadoFinal!.Activo.Should().BeFalse();
    }

    /// <summary>
    /// El caso de referencia del requisito común "RBAC y ABAC en operaciones sensibles" (Fase 6): el
    /// actor TIENE el permiso RBAC (<see cref="FeatureManagementPermissions.FlagsActivar"/>) pero su
    /// claim <c>feature_flag_id</c> solo cubre OTRO flag -- la regla ABAC de alcance incorporada
    /// (<c>AttributeScopeAbacRule</c>, configurada en <see cref="InitializeAsync"/>) deniega la
    /// activación de todos modos.
    /// </summary>
    [Fact]
    public async Task ActivarFlag_FueraDeAlcanceAbac_EsDenegadoAunqueTengaElPermisoRbac()
    {
        var (_, superAdminToken) = await SeedActorAsync(
            "admin-crea-flags-abac", [FeatureManagementPermissions.FlagsCrear]);

        var flagPropioId = await (await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, "/api/v1/feature-flags", superAdminToken,
                new { nombre = "flag-propio-abac", descripcion = (string?)null })))
            .Content.ReadFromJsonAsync<Guid>();

        var flagAjenoId = await (await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, "/api/v1/feature-flags", superAdminToken,
                new { nombre = "flag-ajeno-abac", descripcion = (string?)null })))
            .Content.ReadFromJsonAsync<Guid>();

        // El actor limitado tiene el permiso RBAC de activar flags, pero su claim "feature_flag_id" solo
        // lo habilita para el flag propio.
        var (_, actorLimitadoToken) = await SeedActorAsync(
            "admin-limitado-flags-abac",
            [FeatureManagementPermissions.FlagsActivar],
            [new Claim("feature_flag_id", flagPropioId.ToString())]);

        var activarPropioResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, $"/api/v1/feature-flags/{flagPropioId}/activar", actorLimitadoToken));
        activarPropioResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var activarAjenoResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, $"/api/v1/feature-flags/{flagAjenoId}/activar", actorLimitadoToken));
        activarAjenoResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Criterio de aceptación central del módulo: un flag INACTIVO globalmente se evalúa como
    /// inactivo para cualquier contexto, sin importar segmentos.</summary>
    [Fact]
    public async Task Evaluar_FlagInactivo_SiempreDaFalse()
    {
        var (_, adminToken) = await SeedActorAsync(
            "admin-eval-inactivo",
            [FeatureManagementPermissions.FlagsCrear, FeatureManagementPermissions.FlagsEvaluar]);

        await _client!.SendAsync(BuildRequest(HttpMethod.Post, "/api/v1/feature-flags", adminToken,
            new { nombre = "flag-eval-inactivo", descripcion = (string?)null }));

        var evalResponse = await _client!.SendAsync(BuildRequest(
            HttpMethod.Get, $"/api/v1/feature-flags/flag-eval-inactivo/evaluar?tenantId={TenantId}", adminToken));
        var eval = await evalResponse.Content.ReadFromJsonAsync<FeatureFlagEvaluationResponseDto>();
        eval!.Activo.Should().BeFalse();
        eval.Motivo.Should().Be("flag-inactivo-globalmente");
    }

    /// <summary>Un flag ACTIVO sin ningún rollout asociado es un on/off simple: activo para TODO
    /// contexto (equivalente al caso más común de F4-12, pero resuelto desde persistencia propia).</summary>
    [Fact]
    public async Task Evaluar_FlagActivoSinSegmentacion_EsActivoParaCualquierContexto()
    {
        var (_, adminToken) = await SeedActorAsync(
            "admin-eval-sin-segmentacion",
            [
                FeatureManagementPermissions.FlagsCrear, FeatureManagementPermissions.FlagsActivar,
                FeatureManagementPermissions.FlagsEvaluar,
            ]);

        var flagId = await (await _client!.SendAsync(BuildRequest(HttpMethod.Post, "/api/v1/feature-flags", adminToken,
            new { nombre = "flag-eval-global", descripcion = (string?)null })))
            .Content.ReadFromJsonAsync<Guid>();
        await _client!.SendAsync(BuildRequest(HttpMethod.Post, $"/api/v1/feature-flags/{flagId}/activar", adminToken));

        var eval = await (await _client!.SendAsync(BuildRequest(
            HttpMethod.Get, $"/api/v1/feature-flags/flag-eval-global/evaluar?tenantId={Guid.NewGuid()}", adminToken)))
            .Content.ReadFromJsonAsync<FeatureFlagEvaluationResponseDto>();
        eval!.Activo.Should().BeTrue();
        eval.Motivo.Should().Be("activo-sin-segmentacion");
    }

    /// <summary>
    /// El vertical slice completo de segmentación por tenant: un flag activo con un rollout hacia un
    /// segmento "PorTenant" solo está activo para el tenant configurado en ese segmento -- cualquier
    /// otro tenant lo ve inactivo a pesar de que el flag está globalmente activo.
    /// </summary>
    [Fact]
    public async Task Evaluar_FlagConSegmentoPorTenant_SoloEsActivoParaEseTenant()
    {
        var (_, adminToken) = await SeedActorAsync(
            "admin-eval-por-tenant",
            [
                FeatureManagementPermissions.FlagsCrear, FeatureManagementPermissions.FlagsActivar,
                FeatureManagementPermissions.FlagsEvaluar, FeatureManagementPermissions.SegmentosCrear,
                FeatureManagementPermissions.RolloutsCrear,
            ]);

        var flagId = await (await _client!.SendAsync(BuildRequest(HttpMethod.Post, "/api/v1/feature-flags", adminToken,
            new { nombre = "flag-por-tenant", descripcion = (string?)null })))
            .Content.ReadFromJsonAsync<Guid>();
        await _client!.SendAsync(BuildRequest(HttpMethod.Post, $"/api/v1/feature-flags/{flagId}/activar", adminToken));

        var tenantElegido = Guid.NewGuid();
        var segmentoId = await (await _client!.SendAsync(BuildRequest(HttpMethod.Post, "/api/v1/segmentos", adminToken,
            new { nombre = "tenant-elegido", tipo = SegmentoTipo.PorTenant, tenantIdCriterio = tenantElegido, porcentaje = (int?)null })))
            .Content.ReadFromJsonAsync<Guid>();

        (await _client!.SendAsync(BuildRequest(HttpMethod.Post, "/api/v1/rollouts", adminToken,
            new { featureFlagId = flagId, segmentoId })))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var evalTenantElegido = await (await _client!.SendAsync(BuildRequest(
            HttpMethod.Get, $"/api/v1/feature-flags/flag-por-tenant/evaluar?tenantId={tenantElegido}", adminToken)))
            .Content.ReadFromJsonAsync<FeatureFlagEvaluationResponseDto>();
        evalTenantElegido!.Activo.Should().BeTrue();
        evalTenantElegido.Motivo.Should().Be("segmento:tenant-elegido");

        var evalOtroTenant = await (await _client!.SendAsync(BuildRequest(
            HttpMethod.Get, $"/api/v1/feature-flags/flag-por-tenant/evaluar?tenantId={Guid.NewGuid()}", adminToken)))
            .Content.ReadFromJsonAsync<FeatureFlagEvaluationResponseDto>();
        evalOtroTenant!.Activo.Should().BeFalse();
        evalOtroTenant.Motivo.Should().Be("fuera-de-todos-los-segmentos");
    }

    /// <summary>
    /// El vertical slice completo de rollout gradual por porcentaje: un segmento al 0% nunca incluye a
    /// nadie, uno al 100% siempre incluye a todos -- casos deterministas de los extremos del algoritmo
    /// de bucketing (<c>PorcentajeRolloutHasher</c>), sin depender de un hash concreto.
    /// </summary>
    [Fact]
    public async Task Evaluar_FlagConSegmentoPorPorcentaje_RespetaLosExtremos0Y100()
    {
        var (_, adminToken) = await SeedActorAsync(
            "admin-eval-por-porcentaje",
            [
                FeatureManagementPermissions.FlagsCrear, FeatureManagementPermissions.FlagsActivar,
                FeatureManagementPermissions.FlagsEvaluar, FeatureManagementPermissions.SegmentosCrear,
                FeatureManagementPermissions.RolloutsCrear,
            ]);

        var flagCeroPorCientoId = await (await _client!.SendAsync(BuildRequest(HttpMethod.Post, "/api/v1/feature-flags", adminToken,
            new { nombre = "flag-0-porciento", descripcion = (string?)null })))
            .Content.ReadFromJsonAsync<Guid>();
        await _client!.SendAsync(BuildRequest(HttpMethod.Post, $"/api/v1/feature-flags/{flagCeroPorCientoId}/activar", adminToken));
        var segmentoCeroId = await (await _client!.SendAsync(BuildRequest(HttpMethod.Post, "/api/v1/segmentos", adminToken,
            new { nombre = "cero-porciento", tipo = SegmentoTipo.PorPorcentaje, tenantIdCriterio = (Guid?)null, porcentaje = 0 })))
            .Content.ReadFromJsonAsync<Guid>();
        await _client!.SendAsync(BuildRequest(HttpMethod.Post, "/api/v1/rollouts", adminToken,
            new { featureFlagId = flagCeroPorCientoId, segmentoId = segmentoCeroId }));

        var evalCero = await (await _client!.SendAsync(BuildRequest(
            HttpMethod.Get, $"/api/v1/feature-flags/flag-0-porciento/evaluar?tenantId={TenantId}&userId=cualquier-usuario", adminToken)))
            .Content.ReadFromJsonAsync<FeatureFlagEvaluationResponseDto>();
        evalCero!.Activo.Should().BeFalse();

        var flagCienPorCientoId = await (await _client!.SendAsync(BuildRequest(HttpMethod.Post, "/api/v1/feature-flags", adminToken,
            new { nombre = "flag-100-porciento", descripcion = (string?)null })))
            .Content.ReadFromJsonAsync<Guid>();
        await _client!.SendAsync(BuildRequest(HttpMethod.Post, $"/api/v1/feature-flags/{flagCienPorCientoId}/activar", adminToken));
        var segmentoCienId = await (await _client!.SendAsync(BuildRequest(HttpMethod.Post, "/api/v1/segmentos", adminToken,
            new { nombre = "cien-porciento", tipo = SegmentoTipo.PorPorcentaje, tenantIdCriterio = (Guid?)null, porcentaje = 100 })))
            .Content.ReadFromJsonAsync<Guid>();
        await _client!.SendAsync(BuildRequest(HttpMethod.Post, "/api/v1/rollouts", adminToken,
            new { featureFlagId = flagCienPorCientoId, segmentoId = segmentoCienId }));

        var evalCien = await (await _client!.SendAsync(BuildRequest(
            HttpMethod.Get, $"/api/v1/feature-flags/flag-100-porciento/evaluar?tenantId={TenantId}&userId=cualquier-otro-usuario", adminToken)))
            .Content.ReadFromJsonAsync<FeatureFlagEvaluationResponseDto>();
        evalCien!.Activo.Should().BeTrue();
        evalCien.Motivo.Should().Be("segmento:cien-porciento");
    }

    /// <summary>Criterio de aceptación explícito del módulo (guardrail de datos, ver
    /// <c>CrearRolloutCommandHandler</c>): la misma asociación FeatureFlag-Segmento no puede darse de
    /// alta dos veces.</summary>
    [Fact]
    public async Task CrearRollout_AsociacionDuplicada_Retorna409()
    {
        var (_, adminToken) = await SeedActorAsync(
            "admin-rollout-duplicado",
            [
                FeatureManagementPermissions.FlagsCrear, FeatureManagementPermissions.SegmentosCrear,
                FeatureManagementPermissions.RolloutsCrear,
            ]);

        var flagId = await (await _client!.SendAsync(BuildRequest(HttpMethod.Post, "/api/v1/feature-flags", adminToken,
            new { nombre = "flag-rollout-duplicado", descripcion = (string?)null })))
            .Content.ReadFromJsonAsync<Guid>();
        var segmentoId = await (await _client!.SendAsync(BuildRequest(HttpMethod.Post, "/api/v1/segmentos", adminToken,
            new { nombre = "segmento-rollout-duplicado", tipo = SegmentoTipo.PorPorcentaje, tenantIdCriterio = (Guid?)null, porcentaje = 50 })))
            .Content.ReadFromJsonAsync<Guid>();

        (await _client!.SendAsync(BuildRequest(HttpMethod.Post, "/api/v1/rollouts", adminToken,
            new { featureFlagId = flagId, segmentoId })))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var duplicadoResponse = await _client!.SendAsync(BuildRequest(HttpMethod.Post, "/api/v1/rollouts", adminToken,
            new { featureFlagId = flagId, segmentoId }));
        duplicadoResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    private sealed record FeatureFlagResponseDto(Guid Id, string Nombre, string? Descripcion, bool Activo);

    private sealed record FeatureFlagEvaluationResponseDto(string Nombre, bool Activo, string Motivo);
}
