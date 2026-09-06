using System.Security.Claims;

namespace BitCode.Framework.Shared.Infrastructure.Security.Abac;

/// <summary>
/// Evaluador de autorización combinada RBAC + ABAC (F2-08), la pieza que faltaba tras F2-07 para
/// reglas por atributo del recurso concreto (monto, empresa, sucursal -- criterio de aceptación literal
/// de esta tarea). A diferencia de <c>[RequirePermission]</c>/<c>IPermissionEvaluator</c> (F2-07,
/// declarativo, evaluado por el pipeline de autorización de ASP.NET Core antes de que el endpoint
/// corra), este evaluador es SIEMPRE invocado explícitamente desde código de aplicación (un handler,
/// típicamente después de leer la entidad de negocio de la base de datos) porque los atributos del
/// recurso (<see cref="AbacResource.Attributes"/>) son datos de negocio de una instancia concreta que
/// solo se conocen en ese momento -- no hay forma genérica de resolverlos desde la ruta/query string de
/// un request HTTP sin acoplar este contrato a un endpoint concreto.
/// <para>
/// El algoritmo es siempre fail-closed: <see cref="EvaluateAsync"/> primero exige el permiso RBAC base
/// (<c>"{resource.Type}.{action}"</c>, vía <c>IPermissionEvaluator</c>, F2-07) y solo si RBAC concede
/// evalúa las <see cref="IAbacRule"/> aplicables, combinadas con deny-overrides (alcanza con que UNA
/// regla aplicable deniegue para que el resultado final sea denegado) -- ninguna regla ABAC puede
/// conceder más de lo que RBAC ya concedió, solo restringir. Ver <c>docs/guia-abac.md</c>.
/// </para>
/// </summary>
public interface IAuthorizationPolicyEvaluator
{
    /// <summary>
    /// Evalúa si <paramref name="principal"/> puede ejercer <paramref name="action"/> sobre
    /// <paramref name="resource"/>. Nunca lanza por una decisión de negocio "denegado" -- eso es
    /// <see cref="AbacDecision.Allowed"/> en <see langword="false"/>, no una excepción.
    /// </summary>
    Task<AbacDecision> EvaluateAsync(
        ClaimsPrincipal principal,
        AbacResource resource,
        string action,
        AbacContext? context = null,
        CancellationToken cancellationToken = default);
}
