using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.FeatureManagement.Segmentos;

internal sealed record ListarSegmentosQuery(int Page, int PageSize) : IQuery<PagedResult<SegmentoResponse>>;

internal sealed class ListarSegmentosQueryHandler(IReadRepository<Segmento, Guid> repository)
    : IRequestHandler<ListarSegmentosQuery, Result<PagedResult<SegmentoResponse>>>
{
    public async Task<Result<PagedResult<SegmentoResponse>>> Handle(
        ListarSegmentosQuery request, CancellationToken cancellationToken)
    {
        var pageRequestResult = PageRequest.Create(request.Page, request.PageSize);
        if (pageRequestResult.IsFailure)
        {
            return Result.Failure<PagedResult<SegmentoResponse>>(pageRequestResult.Error);
        }

        return await repository.ListPagedAsync(
            new TodosLosSegmentosOrdenadosPorNombreSpecification(),
            s => new SegmentoResponse(s.Id, s.Nombre, s.Tipo, s.TenantIdCriterio, s.Porcentaje),
            pageRequestResult.Value,
            cancellationToken);
    }
}
