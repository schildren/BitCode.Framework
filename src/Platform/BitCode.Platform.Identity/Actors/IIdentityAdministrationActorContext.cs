using System.Security.Claims;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;

namespace BitCode.Framework.Platform.Identity.Actors;

/// <summary>
/// Resuelve "quién" ejecuta la operación actual del módulo Identity Administration, para auditoría
/// (<see cref="GetCurrentActor"/>, F2-15) y para la evaluación ABAC explícita que algunos handlers
/// necesitan hacer después de leer la entidad (<see cref="GetCurrentPrincipal"/>, F2-08) -- mismo
/// espíritu que <c>ITenantContext</c> (F1-15): un handler de MediatR no depende de
/// <c>IHttpContextAccessor</c> directamente, depende de esta abstracción de más alto nivel.
/// <see cref="IdentityAdministrationServiceCollectionExtensions.AddSharedIdentityAdministration"/>
/// registra siempre <c>HttpContextIdentityAdministrationActorContext</c> (este módulo es HTTP por
/// diseño, a diferencia de <c>ITenantProvider</c>/<c>NullTenantProvider</c> no hay todavía un
/// consumidor no-HTTP de este módulo, ver el pendiente explícito de
/// <c>docs/guia-identity-administration.md</c>).
/// </summary>
public interface IIdentityAdministrationActorContext
{
    /// <summary>
    /// Actor para auditoría (F2-15) -- <see cref="AuditActorType.User"/> con el <c>NameIdentifier</c>
    /// del request autenticado, o <see cref="AuditActorType.System"/> ("system") si no hay
    /// <c>HttpContext</c>/usuario autenticado (por ejemplo, un seeder o un test unitario sin pipeline
    /// HTTP).
    /// </summary>
    AuditActor GetCurrentActor();

    /// <summary>
    /// El <see cref="ClaimsPrincipal"/> del request actual, para una evaluación ABAC explícita
    /// (<c>IAuthorizationPolicyEvaluator.EvaluateAsync</c>, F2-08). <see langword="null"/> fuera de un
    /// pipeline HTTP -- un handler que necesite este valor para autorizar debe tratar
    /// <see langword="null"/> como "no se puede evaluar ABAC" (fail-closed), nunca como "se omite la
    /// regla".
    /// </summary>
    ClaimsPrincipal? GetCurrentPrincipal();
}
