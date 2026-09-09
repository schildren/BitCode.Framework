namespace BitCode.Framework.Platform.TaskInbox.Actors;

/// <summary>Mismo rol que <c>IWorkflowActorContext</c> (Fase 6, módulo 6): resuelve el actor autenticado
/// actual sin acoplar los handlers de este módulo a <c>HttpContext</c> directamente.</summary>
public interface ITaskInboxActorContext
{
    Guid? GetCurrentUserId();
}
