using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Platform.IntegrationHub.Conectores;

internal sealed record ActivarConectorCommand(Guid Id) : ICommand;

internal sealed class ActivarConectorCommandValidator : AbstractValidator<ActivarConectorCommand>
{
    public ActivarConectorCommandValidator() => RuleFor(c => c.Id).NotEmpty();
}

internal sealed class ActivarConectorCommandHandler(IRepository<IntegrationConnector, Guid> repository)
    : IRequestHandler<ActivarConectorCommand, Result>
{
    public async Task<Result> Handle(ActivarConectorCommand request, CancellationToken cancellationToken)
    {
        var connector = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (connector is null)
        {
            return Result.Failure(Error.NotFound(
                "IntegrationHub.Conectores.NoEncontrado", $"No existe el conector {request.Id}."));
        }

        connector.Activar();
        repository.Update(connector);
        return Result.Success();
    }
}
