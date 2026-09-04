using BitCode.Framework.Shared.Kernel;
using MediatR;

namespace MyApp.Application.Features;

public class FeatureNameCommandHandler : IRequestHandler<FeatureNameCommand, Result<Guid>>
{
    public Task<Result<Guid>> Handle(FeatureNameCommand request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
