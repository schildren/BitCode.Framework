using System.Security.Claims;
using BitCode.Framework.Shared.Infrastructure.Security.Abac;
using BitCode.Framework.Shared.Infrastructure.Security.Permissions;

namespace BitCode.Framework.Shared.Infrastructure.Security.PrivilegedOperations;

/// <summary>
/// Configuración de operaciones privilegiadas (F2-10): step-up authentication
/// (<see cref="StepUpRequirements"/>) y segregación de funciones (<see cref="MakerCheckerRules"/>,
/// <see cref="MutuallyExclusivePermissions"/>). Ninguna de las tres colecciones trae entradas por
/// defecto -- una lista vacía significa que ninguna de las dos <see cref="IAbacRule"/> de esta
/// tarea (<see cref="StepUpAbacRule"/>, <see cref="SegregationOfDutiesAbacRule"/>) tiene nada que
/// restringir; cada proyecto declara sus propias operaciones críticas, típicamente en
/// <c>InfrastructureModule</c>:
/// <code>
/// services.AddSharedPrivilegedOperationsPolicies(options =>
/// {
///     options.StepUpRequirements.Add(new StepUpRequirement
///     {
///         ResourceType = "pagos",
///         Action = "aprobar",
///         MaxAuthenticationAge = TimeSpan.FromMinutes(5),
///     });
///     options.MakerCheckerRules.Add(new MakerCheckerRule
///     {
///         ResourceType = "pedidos",
///         Action = "aprobar",
///         ActorResourceAttributeKey = "creadoPorUserId",
///     });
///     options.MutuallyExclusivePermissions.Add(new MutuallyExclusivePermissionPair
///     {
///         PermissionA = "pedidos.crear",
///         PermissionB = "pedidos.auditar",
///     });
/// });
/// </code>
/// </summary>
public sealed class PrivilegedOperationsOptions
{
    /// <summary>Requisitos de step-up authentication (F2-10) -- ver <see cref="StepUpAbacRule"/>.</summary>
    public IList<StepUpRequirement> StepUpRequirements { get; } = [];

    /// <summary>Reglas de segregación de funciones tipo "maker-checker" (F2-10) -- ver
    /// <see cref="SegregationOfDutiesAbacRule"/>.</summary>
    public IList<MakerCheckerRule> MakerCheckerRules { get; } = [];

    /// <summary>Pares de permisos mutuamente excluyentes (F2-10) -- ver
    /// <see cref="SegregationOfDutiesAbacRule"/>.</summary>
    public IList<MutuallyExclusivePermissionPair> MutuallyExclusivePermissions { get; } = [];
}

/// <summary>
/// Un requisito de step-up authentication (F2-10): la operación identificada por
/// (<see cref="ResourceType"/>, <see cref="Action"/>) exige evidencia de una autenticación reciente o
/// reforzada, además del permiso RBAC/ABAC ya evaluado -- ver <see cref="StepUpAbacRule"/> para el
/// algoritmo exacto (incluida la política fail-closed, deliberadamente distinta de la política "sin
/// dato, no se restringe" de las reglas ABAC genéricas de F2-08).
/// </summary>
public sealed class StepUpRequirement
{
    /// <summary>Tipo de recurso al que aplica este requisito, o <c>"*"</c> para todos.</summary>
    public required string ResourceType { get; init; }

    /// <summary>Acción a la que aplica este requisito, o <c>"*"</c> para todas (por defecto).</summary>
    public string Action { get; init; } = "*";

    /// <summary>
    /// Métodos de autenticación aceptables (claim <c>amr</c> del token, u otro claim configurado en
    /// <see cref="AuthenticationMethodClaimType"/>) -- por ejemplo, <c>["mfa"]</c> u <c>["otp", "hwk"]</c>.
    /// <see langword="null"/> o vacío desactiva esta verificación puntual (pero no el requisito
    /// completo -- ver <see cref="MaxAuthenticationAge"/>; al menos uno de los dos debe configurarse,
    /// o <see cref="StepUpAbacRule"/> rechaza el requisito como mal configurado).
    /// </summary>
    public IReadOnlyCollection<string>? AcceptableAuthenticationMethods { get; init; }

    /// <summary>Claim que transporta los métodos de autenticación del sujeto (por defecto,
    /// <c>"amr"</c>, el claim estándar OIDC).</summary>
    public string AuthenticationMethodClaimType { get; init; } = "amr";

    /// <summary>
    /// Antigüedad máxima aceptable de la autenticación (claim <c>auth_time</c>, epoch Unix en segundos,
    /// u otro claim configurado en <see cref="AuthenticationTimeClaimType"/>) -- por ejemplo,
    /// <c>TimeSpan.FromMinutes(5)</c> exige que el sujeto se haya autenticado (o reautenticado) en los
    /// últimos 5 minutos. <see langword="null"/> desactiva esta verificación puntual (ver
    /// <see cref="AcceptableAuthenticationMethods"/> para la misma condición sobre el otro criterio).
    /// </summary>
    public TimeSpan? MaxAuthenticationAge { get; init; }

    /// <summary>Claim que transporta el instante de autenticación del sujeto, epoch Unix en segundos
    /// (por defecto, <c>"auth_time"</c>, el claim estándar OIDC).</summary>
    public string AuthenticationTimeClaimType { get; init; } = "auth_time";
}

/// <summary>
/// Una regla de segregación de funciones tipo "maker-checker" (F2-10): quien ejecutó la acción cuya
/// evidencia quedó en <see cref="ActorResourceAttributeKey"/> del recurso (por ejemplo, quien creó un
/// pedido) no puede ser el mismo sujeto que ejecuta la acción protegida por esta regla (por ejemplo,
/// aprobarlo) -- ver <see cref="SegregationOfDutiesAbacRule"/> para el algoritmo exacto.
/// </summary>
public sealed class MakerCheckerRule
{
    /// <summary>Tipo de recurso al que aplica esta regla, o <c>"*"</c> para todos.</summary>
    public required string ResourceType { get; init; }

    /// <summary>Acción a la que aplica esta regla, o <c>"*"</c> para todas (por defecto).</summary>
    public string Action { get; init; } = "*";

    /// <summary>Clave en <see cref="AbacResource.Attributes"/> que transporta el identificador del
    /// actor que ejecutó la acción anterior sobre este recurso (por ejemplo,
    /// <c>"creadoPorUserId"</c>).</summary>
    public required string ActorResourceAttributeKey { get; init; }

    /// <summary>Claim del sujeto actual cuyo valor se compara contra el atributo de actor del recurso
    /// (por defecto, <see cref="ClaimTypes.NameIdentifier"/>).</summary>
    public string SubjectClaimType { get; init; } = ClaimTypes.NameIdentifier;
}

/// <summary>
/// Un par de permisos mutuamente excluyentes (F2-10): ningún sujeto puede tener ambos entre sus
/// <see cref="EffectivePermissions"/> a la vez -- ver <see cref="SegregationOfDutiesAbacRule"/>
/// para el algoritmo exacto. A diferencia de <see cref="MakerCheckerRule"/> (por instancia de recurso),
/// esta restricción es de identidad: se verifica en toda evaluación, independientemente del recurso o
/// la acción concretos.
/// </summary>
public sealed class MutuallyExclusivePermissionPair
{
    public required string PermissionA { get; init; }

    public required string PermissionB { get; init; }
}
