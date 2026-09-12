using System.Security.Claims;
using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Security.Abac;
using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using BitCode.Framework.Shared.Infrastructure.Security.PrivilegedOperations;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.AuthorizationMatrix;

/// <summary>
/// F2-11: matriz allow/deny consolidada del stack de autorización real (RBAC de F2-07 + ABAC de F2-08 +
/// operaciones privilegiadas de F2-10), compuesto vía el mismo contenedor de DI real que un proyecto
/// consumidor usaría (<c>AddSharedAbacAuthorization</c> + <c>AddSharedPrivilegedOperationsPolicies</c>),
/// sin mockear <see cref="IAuthorizationPolicyEvaluator"/> ni ninguna <see cref="IAbacRule"/> incorporada
/// -- solo se sustituye <see cref="IPermissionService"/> (la fuente de datos de Identity local), mismo
/// criterio que <c>AbacEndToEndTests</c>/<c>PrivilegedOperationsEndToEndTests</c>.
/// <para>
/// A diferencia de esas suites (una por regla/feature, en aislamiento), el propósito de ESTA clase es
/// dar una única tabla auditable que combine los ejes relevantes -- permiso RBAC base, atributos ABAC de
/// alcance (empresa/sucursal) y de monto, tenant del token -- y demuestre en cada fila el criterio de
/// aceptación literal de F2-11: <b>default deny</b> (toda fila sin una regla explícita que conceda
/// termina denegada; nunca "permitido por defecto" ni "permitido porque no se pudo evaluar algo").
/// </para>
/// </summary>
public class AuthorizationMatrixTests
{
    private static IServiceProvider BuildProvider(
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

    // ---- Matriz 1: RBAC + ABAC de alcance (empresa/sucursal) ------------------------------------

    public static TheoryData<bool, bool, bool, bool> ScopeMatrixCases()
    {
        // (tienePermisoRbac, empresaCoincide, sucursalCoincide, allowedEsperado)
        var data = new TheoryData<bool, bool, bool, bool>
        {
            { false, true, true, false }, // sin permiso RBAC -> deny, aunque los atributos coincidan
            { true, false, true, false }, // empresa distinta -> deny
            { true, true, false, false }, // sucursal distinta -> deny
            { true, false, false, false }, // ambos distintos -> deny
            { true, true, true, true }, // todo coincide -> allow
        };
        return data;
    }

    [Theory]
    [MemberData(nameof(ScopeMatrixCases))]
    public async Task ScopeMatrix_CombinacionesEmpresaSucursal(
        bool tienePermisoRbac, bool empresaCoincide, bool sucursalCoincide, bool allowedEsperado)
    {
        var provider = BuildProvider(
            tienePermisoRbac ? ["pedidos.aprobar"] : [],
            options =>
            {
                options.ScopeRules.Add(new AbacScopeAttributeRule { ResourceType = "pedidos", ResourceAttributeKey = "empresaId", ClaimType = "empresa_id" });
                options.ScopeRules.Add(new AbacScopeAttributeRule { ResourceType = "pedidos", ResourceAttributeKey = "sucursalId", ClaimType = "sucursal_id" });
            });
        var evaluator = provider.GetRequiredService<IAuthorizationPolicyEvaluator>();
        var user = CreateUser(Guid.NewGuid(), new Claim("empresa_id", "1"), new Claim("sucursal_id", "10"));
        var resource = new AbacResource("pedidos", new Dictionary<string, object?>
        {
            ["empresaId"] = empresaCoincide ? "1" : "99",
            ["sucursalId"] = sucursalCoincide ? "10" : "20",
        });

        var decision = await evaluator.EvaluateAsync(user, resource, "aprobar");

        decision.Allowed.Should().Be(allowedEsperado);
    }

    // ---- Matriz 2: RBAC + ABAC de monto -------------------------------------------------------

    public static TheoryData<bool, decimal, decimal, bool> AmountMatrixCases()
    {
        // (tienePermisoRbac, limiteDelSujeto, montoDelRecurso, allowedEsperado)
        var data = new TheoryData<bool, decimal, decimal, bool>
        {
            { false, 10_000m, 100m, false }, // sin permiso RBAC -> deny aunque el monto esté dentro del límite
            { true, 1_000m, 5_000m, false }, // monto excede el límite -> deny
            { true, 5_000m, 5_000m, true }, // monto igual al límite -> allow (límite inclusivo)
            { true, 10_000m, 100m, true }, // monto muy por debajo del límite -> allow
        };
        return data;
    }

    [Theory]
    [MemberData(nameof(AmountMatrixCases))]
    public async Task AmountMatrix_CombinacionesLimiteYMonto(
        bool tienePermisoRbac, decimal limiteDelSujeto, decimal montoDelRecurso, bool allowedEsperado)
    {
        var provider = BuildProvider(
            tienePermisoRbac ? ["pedidos.aprobar"] : [],
            options => options.AmountLimitRules.Add(new AbacAmountLimitRule
            {
                ResourceType = "pedidos",
                ResourceAttributeKey = "monto",
                ClaimType = "monto_maximo",
            }));
        var evaluator = provider.GetRequiredService<IAuthorizationPolicyEvaluator>();
        var user = CreateUser(Guid.NewGuid(), new Claim("monto_maximo", limiteDelSujeto.ToString()));
        var resource = new AbacResource("pedidos", new Dictionary<string, object?> { ["monto"] = montoDelRecurso });

        var decision = await evaluator.EvaluateAsync(user, resource, "aprobar");

        decision.Allowed.Should().Be(allowedEsperado);
    }

    // ---- Matriz 3: aislamiento de permisos por tenant -------------------------------------------

    public static TheoryData<bool, bool, bool, bool> TenantMatrixCases()
    {
        // (multiTenancyHabilitado, tenantResueltoPresente, tokenTraeTenantCoincidente, allowedEsperado)
        var data = new TheoryData<bool, bool, bool, bool>
        {
            { false, false, false, true }, // proyecto de un solo tenant -> nunca bloquea por tenant
            { true, true, true, true }, // multi-tenant, token coincide con el tenant resuelto -> allow
            { true, true, false, false }, // multi-tenant, token de OTRO tenant -> deny (aislamiento)
        };
        return data;
    }

    [Theory]
    [MemberData(nameof(TenantMatrixCases))]
    public async Task TenantMatrix_AislamientoDePermisosPorTenant(
        bool multiTenancyHabilitado, bool tenantResueltoPresente, bool tokenTraeTenantCoincidente, bool allowedEsperado)
    {
        var tenantResuelto = tenantResueltoPresente ? Guid.NewGuid() : (Guid?)null;
        var provider = BuildProvider(
            ["productos.crear"],
            resolvedTenantId: tenantResuelto,
            multiTenancyEnabled: multiTenancyHabilitado);
        var evaluator = provider.GetRequiredService<IAuthorizationPolicyEvaluator>();

        var claims = new List<Claim>();
        if (tenantResuelto is { } resuelto)
        {
            var tenantDelToken = tokenTraeTenantCoincidente ? resuelto : Guid.NewGuid();
            claims.Add(new Claim(TenantClaimTypes.TenantId, tenantDelToken.ToString()));
        }

        var user = CreateUser(Guid.NewGuid(), claims.ToArray());
        var resource = new AbacResource("productos");

        var decision = await evaluator.EvaluateAsync(user, resource, "crear");

        decision.Allowed.Should().Be(allowedEsperado);
    }

    // ---- Matriz 4: operaciones privilegiadas (step-up + segregación de funciones) ----------------

    public static TheoryData<bool, bool, bool, bool> PrivilegedOperationsMatrixCases()
    {
        // (tienePermisoRbac, stepUpSatisfecho, esMismoActorQueCreoElRecurso, allowedEsperado)
        var data = new TheoryData<bool, bool, bool, bool>
        {
            { false, true, false, false }, // sin permiso RBAC -> deny, aunque el resto esté en regla
            { true, false, false, false }, // sin evidencia de step-up -> deny
            { true, true, true, false }, // step-up correcto pero mismo actor (maker-checker) -> deny
            { true, true, false, true }, // step-up correcto y actor distinto -> allow
        };
        return data;
    }

    [Theory]
    [MemberData(nameof(PrivilegedOperationsMatrixCases))]
    public async Task PrivilegedOperationsMatrix_StepUpYMakerChecker(
        bool tienePermisoRbac, bool stepUpSatisfecho, bool esMismoActorQueCreoElRecurso, bool allowedEsperado)
    {
        var userId = Guid.NewGuid();
        var provider = BuildProvider(
            tienePermisoRbac ? ["pagos.aprobar"] : [],
            configurePrivileged: options =>
            {
                options.StepUpRequirements.Add(new StepUpRequirement
                {
                    ResourceType = "pagos",
                    Action = "aprobar",
                    MaxAuthenticationAge = TimeSpan.FromMinutes(5),
                });
                options.MakerCheckerRules.Add(new MakerCheckerRule
                {
                    ResourceType = "pagos",
                    Action = "aprobar",
                    ActorResourceAttributeKey = "creadoPorUserId",
                });
            });
        var evaluator = provider.GetRequiredService<IAuthorizationPolicyEvaluator>();

        var claims = new List<Claim>();
        if (stepUpSatisfecho)
        {
            claims.Add(new Claim("auth_time", DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds().ToString()));
        }

        var user = CreateUser(userId, claims.ToArray());
        var actorDelRecurso = esMismoActorQueCreoElRecurso ? userId : Guid.NewGuid();
        var resource = new AbacResource("pagos", new Dictionary<string, object?>
        {
            ["creadoPorUserId"] = actorDelRecurso.ToString(),
        });

        var decision = await evaluator.EvaluateAsync(user, resource, "aprobar");

        decision.Allowed.Should().Be(allowedEsperado);
    }

    // ---- Fila de cierre: sin ninguna regla configurada, un sujeto no autenticado nunca es "allow" ----

    [Fact]
    public async Task DefaultDeny_SujetoNoAutenticado_SiempreDeniega()
    {
        var provider = BuildProvider(["pedidos.aprobar"]);
        var evaluator = provider.GetRequiredService<IAuthorizationPolicyEvaluator>();
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity()); // sin autenticar (sin authenticationType)
        var resource = new AbacResource("pedidos");

        var decision = await evaluator.EvaluateAsync(anonymous, resource, "aprobar");

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Be(AbacDecisionReasons.NotAuthenticated);
    }
}
