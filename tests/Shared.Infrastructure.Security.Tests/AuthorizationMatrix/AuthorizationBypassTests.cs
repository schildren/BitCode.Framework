using System.Security.Claims;
using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Caching;
using BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Security.Abac;
using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using BitCode.Framework.Shared.Infrastructure.Security.PrivilegedOperations;
using FluentAssertions;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.AuthorizationMatrix;

/// <summary>
/// F2-11: pruebas de bypass -- cada caso reproduce un intento concreto de saltarse el stack real de
/// autorización (RBAC/F2-07, ABAC/F2-08, cache de permisos/F2-09, operaciones privilegiadas/F2-10) y
/// confirma que el intento falla, contra las implementaciones reales compuestas vía DI (no mocks
/// desconectados de <see cref="IAuthorizationPolicyEvaluator"/> ni de <see cref="IPermissionEvaluator"/>).
/// A diferencia de <see cref="AuthorizationMatrixTests"/> (variación sistemática de un mismo eje), estos
/// casos son adversariales: qué pasaría si un atacante intentara forjar un claim, reutilizar un permiso
/// fuera de su alcance, o explotar una ventana de cache no invalidada.
/// </summary>
public class AuthorizationBypassTests
{
    private static IServiceProvider BuildAbacProvider(
        string[] userPermissions,
        Action<AbacOptions>? configureAbac = null,
        Action<PrivilegedOperationsOptions>? configurePrivileged = null,
        Guid? resolvedTenantId = null,
        bool multiTenancyEnabled = false)
    {
        var services = new ServiceCollection();
        var permissionService = Substitute.For<IPermissionService>();
        permissionService.GetPermissionsForUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(userPermissions);
        services.AddSingleton(permissionService);

        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.IsMultiTenancyEnabled.Returns(multiTenancyEnabled);
        tenantContext.TenantId.Returns(resolvedTenantId);
        services.AddSingleton(tenantContext);

        services.AddSharedAbacAuthorization(configureAbac ?? (_ => { }));
        if (configurePrivileged is not null)
        {
            services.AddSharedPrivilegedOperationsPolicies(configurePrivileged);
        }

        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal CreateUser(Guid userId, params Claim[] extraClaims) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString()), .. extraClaims], "Test"));

    [Fact]
    public async Task Bypass_TokenConClaimsForjadosPeroSinAutenticar_NuncaEsEvaluado()
    {
        // Un ClaimsIdentity sin AuthenticationType (IsAuthenticated == false) puede traer CUALQUIER
        // claim, incluido un permiso directo -- el intento de bypass más obvio: forjar el claim de
        // permiso esperando que el evaluador ni siquiera mire IsAuthenticated. AuthorizationPolicyEvaluator
        // corta ahí mismo, antes de calcular ningún permiso efectivo.
        var provider = BuildAbacProvider(["pagos.aprobar"]);
        var evaluator = provider.GetRequiredService<IAuthorizationPolicyEvaluator>();
        var forgedPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(PermissionClaimTypes.Permission, "pagos.aprobar"),
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
        ])); // sin "authenticationType" -> IsAuthenticated == false
        var resource = new AbacResource("pagos");

        var decision = await evaluator.EvaluateAsync(forgedPrincipal, resource, "aprobar");

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Be(AbacDecisionReasons.NotAuthenticated);
    }

    [Fact]
    public async Task Bypass_TenantForjadoEnElToken_NoConcedePermisosDeOtroTenant()
    {
        // Intento de escalar cambiando el tenant_id del token a uno distinto del ya resuelto para el
        // request (ITenantContext, resuelto server-side -- no por el token) -- el resultado debe ser
        // "sin permisos", no "permisos del tenant ajeno".
        var tenantPropio = Guid.NewGuid();
        var tenantAjeno = Guid.NewGuid();
        var provider = BuildAbacProvider(["productos.crear"], resolvedTenantId: tenantPropio, multiTenancyEnabled: true);
        var evaluator = provider.GetRequiredService<IAuthorizationPolicyEvaluator>();
        var user = CreateUser(Guid.NewGuid(), new Claim(TenantClaimTypes.TenantId, tenantAjeno.ToString()));
        var resource = new AbacResource("productos");

        var decision = await evaluator.EvaluateAsync(user, resource, "crear");

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Be(AbacDecisionReasons.PermissionDenied("productos.crear"));
    }

    [Fact]
    public async Task Bypass_ScopeClaimNoPuedeAmpliarPermisosMasAllaDeLosOtorgadosPorRbac()
    {
        // El scope OAuth2 solo puede ANGOSTAR (intersectar) el resultado de RBAC (F2-07) -- nunca
        // ampliarlo. Un cliente que declara un scope con forma de permiso que el usuario NUNCA tuvo por
        // RBAC no debe, por eso, ganar ese permiso.
        var userId = Guid.NewGuid();
        var provider = BuildAbacProvider(["productos.crear"]); // el usuario solo tiene este permiso por RBAC
        var evaluator = provider.GetRequiredService<IAuthorizationPolicyEvaluator>();
        var user = CreateUser(userId, new Claim(ScopeClaimTypes.Scope, "productos.crear productos.eliminar"));
        var resource = new AbacResource("productos");

        // Pide autorización para una acción que el scope "declara" pero que RBAC nunca concedió.
        var decision = await evaluator.EvaluateAsync(user, resource, "eliminar");

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Be(AbacDecisionReasons.PermissionDenied("productos.eliminar"));
    }

    [Fact]
    public async Task Bypass_PermisoDeUnRecursoNoAutorizaAccionSobreOtroTipoDeRecurso()
    {
        // Confusión de recurso: un sujeto con "pedidos.aprobar" intenta usar ese permiso para aprobar un
        // recurso de un tipo distinto ("facturas") -- el permiso RBAC requerido se compone como
        // "{resource.Type}.{action}", así que no hay forma de que "pedidos.aprobar" autorice
        // "facturas.aprobar".
        var provider = BuildAbacProvider(["pedidos.aprobar"]);
        var evaluator = provider.GetRequiredService<IAuthorizationPolicyEvaluator>();
        var user = CreateUser(Guid.NewGuid());
        var resource = new AbacResource("facturas");

        var decision = await evaluator.EvaluateAsync(user, resource, "aprobar");

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Be(AbacDecisionReasons.PermissionDenied("facturas.aprobar"));
    }

    [Fact]
    public async Task Bypass_SegregacionDeFunciones_NoSeLimitaAUnSoloTipoDeRecurso()
    {
        // Un sujeto con dos permisos mutuamente excluyentes podría suponer que la restricción de SoD solo
        // aplica al par (tipo de recurso, acción) que motivó su configuración -- pero
        // SegregationOfDutiesAbacRule.AppliesTo declara que un par mutuamente excluyente aplica a
        // CUALQUIER evaluación (es una restricción de identidad, no de recurso concreto). Se verifica
        // contra un tipo de recurso/acción completamente ajeno a "pedidos".
        var provider = BuildAbacProvider(
            ["pedidos.crear", "pedidos.auditar", "reportes.ver"],
            configurePrivileged: options => options.MutuallyExclusivePermissions.Add(new MutuallyExclusivePermissionPair
            {
                PermissionA = "pedidos.crear",
                PermissionB = "pedidos.auditar",
            }));
        var evaluator = provider.GetRequiredService<IAuthorizationPolicyEvaluator>();
        var user = CreateUser(Guid.NewGuid());
        var resource = new AbacResource("reportes");

        var decision = await evaluator.EvaluateAsync(user, resource, "ver");

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Be("abac:rule-denied:sod:mutually-exclusive-permissions:pedidos.crear+pedidos.auditar");
    }

    [Fact]
    public async Task Bypass_MakerChecker_VariarElCasingDelIdDeActorNoEvadeLaComparacion()
    {
        // Intento de evadir la comparación "mismo actor" maker-checker cambiando el casing del Guid del
        // recurso (mayúsculas) esperando que la comparación sea Ordinal (case-sensitive) y no reconozca
        // que es el mismo actor -- SegregationOfDutiesAbacRule compara con OrdinalIgnoreCase a propósito.
        var userId = Guid.NewGuid();
        var provider = BuildAbacProvider(
            ["pedidos.aprobar"],
            configurePrivileged: options => options.MakerCheckerRules.Add(new MakerCheckerRule
            {
                ResourceType = "pedidos",
                Action = "aprobar",
                ActorResourceAttributeKey = "creadoPorUserId",
            }));
        var evaluator = provider.GetRequiredService<IAuthorizationPolicyEvaluator>();
        var user = CreateUser(userId);
        var resource = new AbacResource("pedidos", new Dictionary<string, object?>
        {
            ["creadoPorUserId"] = userId.ToString().ToUpperInvariant(),
        });

        var decision = await evaluator.EvaluateAsync(user, resource, "aprobar");

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().StartWith("abac:rule-denied:sod:same-actor:");
    }

    [Fact]
    public async Task Bypass_StepUp_AuthTimeForjadoEnElFuturoNoSatisfaceLaEvidencia()
    {
        // Intento de "step-up" falso: en vez de dejar vencer la evidencia (edad > máxima), un atacante
        // que controla el claim intenta ponerle una marca de tiempo FUTURA para simular una
        // reautenticación "recién ocurrida" con margen -- StepUpAbacRule calcula la edad como
        // (ahora - authTime) y la rechaza expresamente si es negativa, no solo si excede el máximo.
        var provider = BuildAbacProvider(
            ["pagos.aprobar"],
            configurePrivileged: options => options.StepUpRequirements.Add(new StepUpRequirement
            {
                ResourceType = "pagos",
                Action = "aprobar",
                MaxAuthenticationAge = TimeSpan.FromMinutes(5),
            }));
        var evaluator = provider.GetRequiredService<IAuthorizationPolicyEvaluator>();
        var authTimeEnElFuturo = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds().ToString();
        var user = CreateUser(Guid.NewGuid(), new Claim("auth_time", authTimeEnElFuturo));
        var resource = new AbacResource("pagos");

        var decision = await evaluator.EvaluateAsync(user, resource, "aprobar");

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Be("abac:rule-denied:step-up:authentication-too-old:auth_time");
    }

    [Fact]
    public async Task Bypass_RevocarPermisoDeRolNoSeReflejaHastaInvalidarElCacheExplicitamente()
    {
        // F2-09: mientras el cache de permisos siga vigente (sin invalidación explícita), un permiso
        // recién revocado en el origen sigue "concediendo" -- es la ventana de bypass que
        // IPermissionCacheInvalidator existe para cerrar. Esta prueba demuestra ambos lados con la
        // implementación REAL de HybridCache (L1 en memoria, sin mocks): (1) el permiso revocado sigue
        // vigente hasta que se invalida explícitamente (ventana esperada, documentada, acotada por TTL),
        // y (2) una vez invalidado, el intento de reutilizar el permiso revocado se deniega de inmediato
        // -- el bypass NO sobrevive a la invalidación explícita.
        var services = new ServiceCollection();
        services.AddHybridCache();
        await using var cacheProvider = services.BuildServiceProvider();
        var hybridCache = cacheProvider.GetRequiredService<HybridCache>();

        var roleName = "Aprobador";
        var innerPermissionService = Substitute.For<IPermissionService>();
        // Empieza CON el permiso: representa el estado del rol antes de la revocación.
        innerPermissionService.GetPermissionsForRoleAsync(roleName, Arg.Any<CancellationToken>())
            .Returns(["pagos.aprobar"]);

        var tenantContext = new TenantContext(new FakeTenantProvider(null, isMultiTenancyEnabled: false));
        var tenantAwareCache = new TenantAwareCache(hybridCache, tenantContext);
        var cacheOptions = Options.Create(new PermissionCacheOptions { Expiration = TimeSpan.FromMinutes(5) });
        var cachedPermissionService = new CachedPermissionService(
            innerPermissionService, tenantAwareCache, hybridCache, cacheOptions);

        var user = CreateUser(Guid.NewGuid(), new Claim(ClaimTypes.Role, roleName));
        var evaluator = new PermissionEvaluator(cachedPermissionService, tenantContext);

        // 1) Primera evaluación: puebla el cache con el permiso todavía vigente.
        var antesDeRevocar = await evaluator.EvaluateAsync(user);
        antesDeRevocar.HasPermission("pagos.aprobar").Should().BeTrue();

        // 2) Se revoca el permiso en el origen (RoleManager, fuera de este test) SIN invalidar el cache
        // todavía -- reconfigura el doble para simular que el origen ya no lo otorga.
        innerPermissionService.GetPermissionsForRoleAsync(roleName, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<string>());
        var mientrasElCacheSigueVigente = await evaluator.EvaluateAsync(user);
        mientrasElCacheSigueVigente.HasPermission("pagos.aprobar").Should().BeTrue(
            "el cache de F2-09 no revalida contra el origen en cada request -- por eso la invalidación explícita es obligatoria");

        // 3) Se invoca la invalidación explícita (lo que un proyecto consumidor DEBE hacer al revocar un
        // permiso de rol, ver RemovePermissionAsync/IPermissionCacheInvalidator) -- el bypass se cierra.
        await cachedPermissionService.InvalidateRoleAsync(roleName);
        var despuesDeInvalidar = await evaluator.EvaluateAsync(user);
        despuesDeInvalidar.HasPermission("pagos.aprobar").Should().BeFalse();
    }
}
