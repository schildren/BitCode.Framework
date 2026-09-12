using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace BitCode.Framework.Platform.FeatureManagement.Flags;

internal sealed record ObtenerFeatureFlagQuery(Guid Id) : IQuery<FeatureFlagResponse>;

internal sealed class ObtenerFeatureFlagQueryHandler(IReadRepository<FeatureFlag, Guid> repository)
    : IRequestHandler<ObtenerFeatureFlagQuery, Result<FeatureFlagResponse>>
{
    public async Task<Result<FeatureFlagResponse>> Handle(ObtenerFeatureFlagQuery request, CancellationToken cancellationToken)
    {
        var flag = await repository.GetByIdAsync(request.Id, cancellationToken);

        return flag is null
            ? Result.Failure<FeatureFlagResponse>(
                Error.NotFound("FeatureManagement.Flags.NoEncontrado", $"No existe el flag {request.Id}."))
            : new FeatureFlagResponse(flag.Id, flag.Nombre, flag.Descripcion, flag.Activo);
    }
}
