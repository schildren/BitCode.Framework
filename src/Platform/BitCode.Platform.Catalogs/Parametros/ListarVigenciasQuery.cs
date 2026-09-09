using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.Catalogs.Parametros;

internal sealed record ListarVigenciasQuery(Guid ParametroId) : IQuery<IReadOnlyList<ParametroVigenciaResponse>>;

internal sealed class ListarVigenciasQueryHandler(IReadRepository<ParametroVigencia, Guid> repository)
    : IRequestHandler<ListarVigenciasQuery, Result<IReadOnlyList<ParametroVigenciaResponse>>>
{
    public async Task<Result<IReadOnlyList<ParametroVigenciaResponse>>> Handle(
        ListarVigenciasQuery request, CancellationToken cancellationToken)
    {
        var vigencias = await repository.ListAsync(
            new VigenciasDeParametroOrdenadasSpecification(request.ParametroId),
            v => new ParametroVigenciaResponse(v.Id, v.ParametroId, v.Valor, v.VigenteDesde, v.VigenteHasta),
            cancellationToken);

        return Result.Success<IReadOnlyList<ParametroVigenciaResponse>>(vigencias);
    }
}
