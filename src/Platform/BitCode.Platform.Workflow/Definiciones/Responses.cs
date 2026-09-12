namespace BitCode.Framework.Platform.Workflow.Definiciones;

internal sealed record WorkflowDefinitionResponse(Guid Id, string Codigo, string Nombre, string? Descripcion);

internal sealed record WorkflowStateResponse(
    Guid Id, string Codigo, string Nombre, bool EsInicial, bool EsFinal, bool RequiereTarea,
    string? TituloTarea, Guid? AsignadoPorDefectoUserId, int? SlaMinutos, Guid? EscalarAUserId);

internal sealed record WorkflowTransitionResponse(
    Guid Id, Guid DesdeEstadoId, Guid HaciaEstadoId, string Accion, string? ReglaExpresion, int Orden);

internal sealed record WorkflowVersionResponse(
    Guid Id, Guid WorkflowDefinitionId, int Numero, WorkflowVersionEstado Estado, DateTime? PublicadaAtUtc,
    IReadOnlyList<WorkflowStateResponse> Estados, IReadOnlyList<WorkflowTransitionResponse> Transiciones);
