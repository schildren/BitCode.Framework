using System.Security.Claims;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;

namespace BitCode.Framework.Platform.Workflow.Actors;

/// <summary>
/// Resuelve "quién" ejecuta la operación actual del módulo Workflow, para auditoría
/// (<see cref="GetCurrentActor"/>, F2-15) y para la verificación de ownership de tarea (comparar
/// <see cref="GetCurrentUserId"/> contra <c>WorkflowTask.AsignadoAUserId"/>) -- mismo patrón y mismo
/// motivo que <c>ICatalogsActorContext</c>/<c>IOrganizationActorContext</c> (Fase 6, módulos 3 y 2).
/// </summary>
public interface IWorkflowActorContext
{
    /// <summary>Actor para auditoría (F2-15) -- <see cref="AuditActorType.User"/> con el
    /// <c>NameIdentifier</c> del request autenticado, o <see cref="AuditActorType.System"/> ("system")
    /// si no hay <c>HttpContext</c>/usuario autenticado.</summary>
    AuditActor GetCurrentActor();

    /// <summary>El <see cref="ClaimsPrincipal"/> del request actual. <see langword="null"/> fuera de un
    /// pipeline HTTP.</summary>
    ClaimsPrincipal? GetCurrentPrincipal();

    /// <summary>El identificador del usuario autenticado actual (<c>ClaimTypes.NameIdentifier</c> como
    /// <see cref="Guid"/>) -- usado para la verificación de ownership de tarea (ver
    /// <c>ResolverTareaCommandHandler</c>/<c>DelegarTareaCommandHandler</c>). <see langword="null"/> si no
    /// hay un usuario autenticado o si el claim no es un <see cref="Guid"/> válido -- tratado como
    /// fail-closed por los handlers que lo consumen (una operación que exige ownership nunca se ejecuta
    /// sin poder determinar quién la pide).</summary>
    Guid? GetCurrentUserId();
}
