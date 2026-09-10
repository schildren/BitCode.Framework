using BitCode.Framework.Shared.Application.Messaging;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using FluentValidation;
using MediatR;

namespace MyApp.Modules.Elementos;

public record CrearElementoCommand(string Nombre) : ICommand<Guid>;

public class CrearElementoCommandValidator : AbstractValidator<CrearElementoCommand>
{
    public CrearElementoCommandValidator()
    {
        RuleFor(c => c.Nombre).NotEmpty();
    }
}

// No llama IUnitOfWork.SaveChangesAsync explícitamente: TransactionBehavior ya lo hace después de que el
// handler retorna un Result exitoso -- ver Shared.Application/Behaviors/TransactionBehavior.cs.
public class CrearElementoCommandHandler(IRepository<Elemento, Guid> repository)
    : IRequestHandler<CrearElementoCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CrearElementoCommand request, CancellationToken cancellationToken)
    {
        var elemento = new Elemento(Guid.NewGuid(), request.Nombre);
        await repository.AddAsync(elemento, cancellationToken);

        return elemento.Id;
    }
}
