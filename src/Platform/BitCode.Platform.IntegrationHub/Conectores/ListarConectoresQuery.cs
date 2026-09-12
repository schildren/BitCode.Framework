using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.IntegrationHub.Conectores;

internal sealed record ListarConectoresQuery(int Page, int PageSize) : IQuery<PagedResult<ConectorResumenResponse>>;

internal sealed class ListarConectoresQueryHandler(IReadRepository<IntegrationConnector, Guid> repository)
    : IRequestHandler<ListarConectoresQuery, Result<PagedResult<ConectorResumenResponse>>>
{
    public async Task<Result<PagedResult<ConectorResumenResponse>>> Handle(
        ListarConectoresQuery request, CancellationToken cancellationToken)
    {
        var pageRequestResult = PageRequest.Create(request.Page, request.PageSize);
        if (pageRequestResult.IsFailure)
        {
            return Result.Failure<PagedResult<ConectorResumenResponse>>(pageRequestResult.Error);
        }

        return await repository.ListPagedAsync(
            new TodosLosConectoresSpecification(),
            c => new ConectorResumenResponse(c.Id, c.Codigo, c.Nombre, c.Metodo, c.TipoAutenticacion, c.Activo),
            pageRequestResult.Value,
            cancellationToken);
    }
}
