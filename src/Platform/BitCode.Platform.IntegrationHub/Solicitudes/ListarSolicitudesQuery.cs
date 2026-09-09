using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.IntegrationHub.Solicitudes;

internal sealed record ListarSolicitudesQuery(Guid? ConnectorId, IntegrationRequestEstado? Estado, int Page, int PageSize)
    : IQuery<PagedResult<IntegrationRequestResponse>>;

internal sealed class ListarSolicitudesQueryHandler(IReadRepository<IntegrationRequest, Guid> repository)
    : IRequestHandler<ListarSolicitudesQuery, Result<PagedResult<IntegrationRequestResponse>>>
{
    public async Task<Result<PagedResult<IntegrationRequestResponse>>> Handle(
        ListarSolicitudesQuery request, CancellationToken cancellationToken)
    {
        var pageRequestResult = PageRequest.Create(request.Page, request.PageSize);
        if (pageRequestResult.IsFailure)
        {
            return Result.Failure<PagedResult<IntegrationRequestResponse>>(pageRequestResult.Error);
        }

        return await repository.ListPagedAsync(
            new TodasLasSolicitudesSpecification(request.ConnectorId, request.Estado),
            r => new IntegrationRequestResponse(
                r.Id, r.ConnectorId, r.DisparadoPorUserId, r.PayloadInternoJson, r.PayloadExternoJson, r.Estado,
                r.IntentosRealizados, r.UltimoErrorMensaje, r.UltimoCodigoHttp, r.EnviadaAtUtc),
            pageRequestResult.Value,
            cancellationToken);
    }
}
