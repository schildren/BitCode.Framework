using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.FeatureManagement.Segmentos;

internal sealed record ObtenerSegmentoQuery(Guid Id) : IQuery<SegmentoResponse>;

internal sealed class ObtenerSegmentoQueryHandler(IReadRepository<Segmento, Guid> repository)
    : IRequestHandler<ObtenerSegmentoQuery, Result<SegmentoResponse>>
{
    public async Task<Result<SegmentoResponse>> Handle(ObtenerSegmentoQuery request, CancellationToken cancellationToken)
    {
        var segmento = await repository.GetByIdAsync(request.Id, cancellationToken);

        return segmento is null
            ? Result.Failure<SegmentoResponse>(
                Error.NotFound("FeatureManagement.Segmentos.NoEncontrado", $"No existe el segmento {request.Id}."))
            : new SegmentoResponse(segmento.Id, segmento.Nombre, segmento.Tipo, segmento.TenantIdCriterio, segmento.Porcentaje);
    }
}
