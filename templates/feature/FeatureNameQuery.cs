using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace MyApp.Modules.Elementos;

// Vertical slice generado por "dotnet new bitcode-feature --Kind Query" (BitCode.Framework) -- corrido
// DENTRO del directorio del módulo destino (ver README.md de este template). Reemplazá las propiedades de
// la Query, del Response y la lógica del Handler por el caso de uso real -- ver
// Elementos/ObtenerElementoQuery.cs (generado por "dotnet new bitcode-module") para un ejemplo completo
// con entidad y repositorio.
public record FeatureNameQuery : IQuery<FeatureNameResponse>;

public record FeatureNameResponse;

public class FeatureNameQueryHandler : IRequestHandler<FeatureNameQuery, Result<FeatureNameResponse>>
{
    public Task<Result<FeatureNameResponse>> Handle(FeatureNameQuery request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException(
            "TODO: implementar el caso de uso (repository.GetByIdAsync/ListPagedAsync, etc.).");
    }
}
