namespace BitCode.Framework.Platform.Dashboard.Actors;

/// <summary>Mismo rol que <c>ITaskInboxActorContext</c> (Fase 6, módulo 7): resuelve el actor autenticado
/// actual sin acoplar los handlers de este módulo a <c>HttpContext</c> directamente.</summary>
public interface IDashboardActorContext
{
    Guid? GetCurrentUserId();
}
