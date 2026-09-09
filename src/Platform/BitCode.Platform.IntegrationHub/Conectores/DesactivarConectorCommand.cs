using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace BitCode.Framework.Platform.IntegrationHub.Conectores;

internal sealed record DesactivarConectorCommand(Guid Id) : ICommand;

internal sealed class DesactivarConectorCommandValidator : AbstractValidator<DesactivarConectorCommand>
{
    public DesactivarConectorCommandValidator() => RuleFor(c => c.Id).NotEmpty();
}

internal sealed class DesactivarConectorCommandHandler(IRepository<IntegrationConnector, Guid> repository)
    : IRequestHandler<DesactivarConectorCommand, Result>
{
    public async Task<Result> Handle(DesactivarConectorCommand request, CancellationToken cancellationToken)
    {
        var connector = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (connector is null)
        {
            return Result.Failure(Error.NotFound(
                "IntegrationHub.Conectores.NoEncontrado", $"No existe el conector {request.Id}."));
        }

        connector.Desactivar();
        repository.Update(connector);
        return Result.Success();
    }
}
