using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.Catalogs.Catalogos;

internal sealed record ObtenerCatalogoQuery(Guid Id) : IQuery<CatalogoResponse>;

internal sealed class ObtenerCatalogoQueryHandler(IReadRepository<Catalogo, Guid> repository)
    : IRequestHandler<ObtenerCatalogoQuery, Result<CatalogoResponse>>
{
    public async Task<Result<CatalogoResponse>> Handle(ObtenerCatalogoQuery request, CancellationToken cancellationToken)
    {
        var catalogo = await repository.GetByIdAsync(request.Id, cancellationToken);

        return catalogo is null
            ? Result.Failure<CatalogoResponse>(
                Error.NotFound("Catalogos.Catalogos.NoEncontrado", $"No existe el catálogo {request.Id}."))
            : new CatalogoResponse(catalogo.Id, catalogo.Codigo, catalogo.Nombre, catalogo.Descripcion);
    }
}
