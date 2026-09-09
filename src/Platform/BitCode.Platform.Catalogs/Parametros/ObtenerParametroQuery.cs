using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.Catalogs.Parametros;

internal sealed record ObtenerParametroQuery(Guid Id) : IQuery<ParametroResponse>;

internal sealed class ObtenerParametroQueryHandler(IReadRepository<Parametro, Guid> repository)
    : IRequestHandler<ObtenerParametroQuery, Result<ParametroResponse>>
{
    public async Task<Result<ParametroResponse>> Handle(ObtenerParametroQuery request, CancellationToken cancellationToken)
    {
        var parametro = await repository.GetByIdAsync(request.Id, cancellationToken);

        return parametro is null
            ? Result.Failure<ParametroResponse>(
                Error.NotFound("Catalogos.Parametros.NoEncontrado", $"No existe el parámetro {request.Id}."))
            : new ParametroResponse(parametro.Id, parametro.Codigo, parametro.Nombre, parametro.Descripcion);
    }
}
