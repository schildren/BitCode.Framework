using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Domain.Specifications;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.Reporting.WorkflowInstancias;

/// <summary>Solo instancias finalizadas con duración calculada -- ver <c>remarks</c> de
/// <see cref="ReporteWorkflowInstancia.DuracionSegundos"/>.</summary>
internal sealed class ReporteWorkflowInstanciaFinalizadaConDuracionSpecification : Specification<ReporteWorkflowInstancia>
{
    public ReporteWorkflowInstanciaFinalizadaConDuracionSpecification(
        Guid? workflowDefinitionId, DateTime? desdeUtc, DateTime? hastaUtc)
    {
        ApplyCriteria(r =>
            r.Estado == ReporteWorkflowInstanciaEstado.Finalizada &&
            r.DuracionSegundos != null &&
            (workflowDefinitionId == null || r.WorkflowDefinitionId == workflowDefinitionId) &&
            (desdeUtc == null || (r.IniciadaAtUtc != null && r.IniciadaAtUtc >= desdeUtc)) &&
            (hastaUtc == null || (r.IniciadaAtUtc != null && r.IniciadaAtUtc <= hastaUtc)));
    }
}

/// <summary>
/// El reporte real mencionado explícitamente por el Plan Maestro para este módulo: "tiempo promedio de
/// resolución de instancias de workflow por definición". No paginado a propósito -- el resultado es UNA
/// fila por <c>WorkflowDefinitionId</c> distinto (típicamente decenas, no miles, de definiciones de
/// workflow por tenant), a diferencia de <see cref="ListarReporteWorkflowInstanciasQuery"/> que sí pagina
/// (una fila por instancia, potencialmente miles).
/// </summary>
/// <remarks>
/// <b>No es un <c>IHotPathQuery</c> (F1-18):</b> este handler proyecta únicamente
/// <c>(WorkflowDefinitionId, DuracionSegundos)</c> vía <c>IReadRepository.ListAsync(spec, selector)</c>
/// (regla dura 5, nunca <c>IQueryable</c> expuesto — <c>docs/convenciones.md</c>) y agrupa/promedia EN
/// MEMORIA sobre ese conjunto ya acotado por el filtro (`Estado = Finalizada` + rango de fechas opcional),
/// en vez de que la base de datos calcule el <c>AVG</c>/<c>GROUP BY</c>. Es una decisión deliberada, no una
/// omisión: la generación de reportes agregados es, por definición de este módulo, una operación de baja
/// frecuencia (a diferencia de un hot path de escritura/lectura transaccional), y <c>IHotPathQuery</c>
/// exige un <c>[HotPath(Justification, BenchmarkRef)]</c> con un benchmark real que demuestre que el
/// patrón genérico no alcanza (F1-18, <c>docs/guia-hot-paths.md</c>) -- sin ese benchmark, usar
/// <c>IHotPathQuery</c> acá sería la sobre-ingeniería que la sección 3.2 del Plan Maestro prohíbe. Si un
/// consumidor real reporta que este agregado se ejecuta con una frecuencia/volumen que sí lo justifica,
/// ese es el momento de medir y, si corresponde, migrar a <c>IHotPathQuery</c> -- no antes.
/// </remarks>
internal sealed record ListarPromedioDuracionPorDefinicionQuery(Guid? WorkflowDefinitionId, DateTime? DesdeUtc, DateTime? HastaUtc)
    : IQuery<IReadOnlyList<PromedioDuracionPorDefinicionResponse>>;

internal sealed class ListarPromedioDuracionPorDefinicionQueryHandler(
    IReadRepository<ReporteWorkflowInstancia, Guid> repository)
    : IRequestHandler<ListarPromedioDuracionPorDefinicionQuery, Result<IReadOnlyList<PromedioDuracionPorDefinicionResponse>>>
{
    public async Task<Result<IReadOnlyList<PromedioDuracionPorDefinicionResponse>>> Handle(
        ListarPromedioDuracionPorDefinicionQuery request, CancellationToken cancellationToken)
    {
        var specification = new ReporteWorkflowInstanciaFinalizadaConDuracionSpecification(
            request.WorkflowDefinitionId, request.DesdeUtc, request.HastaUtc);

        var filas = await repository.ListAsync(
            specification, r => new { r.WorkflowDefinitionId, r.DuracionSegundos }, cancellationToken);

        var resultado = filas
            .GroupBy(f => f.WorkflowDefinitionId)
            .Select(g => new PromedioDuracionPorDefinicionResponse(
                g.Key, g.Count(), g.Average(f => f.DuracionSegundos!.Value)))
            .OrderByDescending(r => r.PromedioDuracionSegundos)
            .ToList();

        return Result.Success<IReadOnlyList<PromedioDuracionPorDefinicionResponse>>(resultado);
    }
}
