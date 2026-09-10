using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace MyApp.Modules.Elementos;

public record ObtenerElementoQuery(Guid Id) : IQuery<ElementoResponse>;

public record ElementoResponse(Guid Id, string Nombre);

public class ObtenerElementoQueryHandler(IReadRepository<Elemento, Guid> repository)
    : IRequestHandler<ObtenerElementoQuery, Result<ElementoResponse>>
{
    public async Task<Result<ElementoResponse>> Handle(ObtenerElementoQuery request, CancellationToken cancellationToken)
    {
        var elemento = await repository.GetByIdAsync(request.Id, cancellationToken);

        return elemento is null
            ? Result.Failure<ElementoResponse>(Error.NotFound("Elemento.NoEncontrado", $"No existe el elemento {request.Id}."))
            : new ElementoResponse(elemento.Id, elemento.Nombre);
    }
}
