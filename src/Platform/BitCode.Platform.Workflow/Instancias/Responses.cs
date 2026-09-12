namespace BitCode.Framework.Platform.Workflow.Instancias;

internal sealed record WorkflowInstanceResponse(
    Guid Id, Guid WorkflowDefinitionId, Guid WorkflowVersionId, Guid EstadoActualId,
    WorkflowInstanceEstado Estado, Dictionary<string, string> Variables, DateTime? FinalizadaAtUtc);

internal sealed record WorkflowTaskResponse(
    Guid Id, Guid WorkflowInstanceId, Guid WorkflowStateId, string Titulo, Guid AsignadoAUserId,
    WorkflowTaskEstado Estado, string? AccionResuelta, DateTime? SlaVencimientoUtc, bool Escalada);

internal sealed record WorkflowHistorialResponse(Guid Id, DateTime FechaUtc, string TipoEvento, string Detalle, Guid? ActorUserId);
