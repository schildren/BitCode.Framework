using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace MyApp.Modules.Elementos;

// Vertical slice generado por "dotnet new bitcode-feature" (BitCode.Framework) -- corrido DENTRO del
// directorio del módulo destino (ver README.md de este template). Reemplazá el cuerpo del record, las
// reglas del Validator y la lógica del Handler por el caso de uso real -- ver
// Elementos/CrearElementoCommand.cs (generado por "dotnet new bitcode-module") para un ejemplo completo
// con entidad y repositorio.
public record FeatureNameCommand : ICommand<Guid>;

public class FeatureNameCommandValidator : AbstractValidator<FeatureNameCommand>
{
    public FeatureNameCommandValidator()
    {
        // RuleFor(c => c.Propiedad).NotEmpty();
    }
}

// No llama IUnitOfWork.SaveChangesAsync explícitamente: TransactionBehavior ya lo hace después de que el
// handler retorna un Result exitoso -- ver Shared.Application/Behaviors/TransactionBehavior.cs.
public class FeatureNameCommandHandler : IRequestHandler<FeatureNameCommand, Result<Guid>>
{
    public Task<Result<Guid>> Handle(FeatureNameCommand request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException(
            "TODO: implementar el caso de uso (repository.AddAsync/UpdateAsync, etc.).");
    }
}
