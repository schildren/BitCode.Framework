using System.Security.Claims;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;

namespace BitCode.Framework.Platform.Catalogs.Actors;

/// <summary>
/// Resuelve "quién" ejecuta la operación actual del módulo Catalogs and Parameters, para auditoría
/// (<see cref="GetCurrentActor"/>, F2-15) y para la evaluación ABAC explícita que
/// <c>PublicarCatalogoVersionCommandHandler</c> necesita hacer después de leer la entidad (F2-08) --
/// mismo patrón y mismo motivo que <c>IOrganizationActorContext</c> (Fase 6, módulo 2).
/// </summary>
public interface ICatalogsActorContext
{
    /// <summary>Actor para auditoría (F2-15) -- <see cref="AuditActorType.User"/> con el
    /// <c>NameIdentifier</c> del request autenticado, o <see cref="AuditActorType.System"/> ("system")
    /// si no hay <c>HttpContext</c>/usuario autenticado.</summary>
    AuditActor GetCurrentActor();

    /// <summary>El <see cref="ClaimsPrincipal"/> del request actual, para una evaluación ABAC explícita
    /// (<c>IAuthorizationPolicyEvaluator.EvaluateAsync</c>, F2-08). <see langword="null"/> fuera de un
    /// pipeline HTTP -- tratado como fail-closed por los handlers que lo consumen.</summary>
    ClaimsPrincipal? GetCurrentPrincipal();
}
