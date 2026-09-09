using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.Workflow.Instancias;

/// <summary>Historial de negocio de una instancia (Fase 6, "History") -- lista completa, sin paginar: el
/// volumen está estructuralmente acotado por el número de pasos del propio grafo de la instancia (regla
/// dura 16, docs/convenciones.md, excepción "volumen acotado por diseño").</summary>
internal sealed record ObtenerHistorialInstanciaQuery(Guid WorkflowInstanceId) : IQuery<IReadOnlyList<WorkflowHistorialResponse>>;

internal sealed class ObtenerHistorialInstanciaQueryHandler(IReadRepository<WorkflowHistorial, Guid> repository)
    : IRequestHandler<ObtenerHistorialInstanciaQuery, Result<IReadOnlyList<WorkflowHistorialResponse>>>
{
    public async Task<Result<IReadOnlyList<WorkflowHistorialResponse>>> Handle(
        ObtenerHistorialInstanciaQuery request, CancellationToken cancellationToken)
    {
        var historial = await repository.ListAsync(
            new HistorialDeInstanciaSpecification(request.WorkflowInstanceId), cancellationToken);

        IReadOnlyList<WorkflowHistorialResponse> response =
            [.. historial.Select(h => new WorkflowHistorialResponse(h.Id, h.FechaUtc, h.TipoEvento, h.Detalle, h.ActorUserId))];
        return Result.Success(response);
    }
}
