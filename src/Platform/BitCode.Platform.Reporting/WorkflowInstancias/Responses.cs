namespace BitCode.Framework.Platform.Reporting.WorkflowInstancias;

internal sealed record ReporteWorkflowInstanciaResponse(
    Guid Id,
    Guid WorkflowDefinitionId,
    ReporteWorkflowInstanciaEstado Estado,
    DateTime? IniciadaAtUtc,
    DateTime? FinalizadaAtUtc,
    int? DuracionSegundos,
    Guid? EstadoFinalId);

/// <summary>Fila agregada del reporte "tiempo promedio de resolución por definición" -- el reporte real
/// mencionado en el Plan Maestro (Fase 6, módulo 11). Solo considera instancias ya finalizadas con
/// duración calculada (ver <c>remarks</c> de <see cref="ReporteWorkflowInstancia.DuracionSegundos"/>).</summary>
internal sealed record PromedioDuracionPorDefinicionResponse(
    Guid WorkflowDefinitionId, int CantidadInstanciasFinalizadas, double PromedioDuracionSegundos);

internal sealed record ReporteWorkflowInstanciaDescargaResponse(byte[] Contenido, string ContentType, string NombreArchivo);
